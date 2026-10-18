using System;
using System.Collections.Generic;
using System.ComponentModel;
using System.Linq;
using System.Net.Http;
using System.Reflection;
using System.Text;
using System.Threading.Tasks;
using Autodesk.Revit.DB;
using Newtonsoft.Json;
using Newtonsoft.Json.Linq;

namespace reviewPlugIn.Services
{
    public class SheetChangeItem : INotifyPropertyChanged
    {
        private bool _isSelected = true;

        [JsonProperty("element_id")]
        public string ElementId { get; set; } = "";

        [JsonProperty("current_number")]
        public string CurrentNumber { get; set; } = "";

        [JsonProperty("current_name")]
        public string CurrentName { get; set; } = "";

        [JsonProperty("current_collection")]
        public string CurrentCollection { get; set; } = "";

        [JsonProperty("target_number")]
        public string TargetNumber { get; set; } = "";

        [JsonProperty("target_name")]
        public string TargetName { get; set; } = "";

        [JsonProperty("target_collection")]
        public string TargetCollection { get; set; } = "";

        [JsonProperty("target_sheet_order")]
        public string TargetSheetOrder { get; set; } = "";

        [JsonProperty("target_drawn_by")]
        public string TargetDrawnBy { get; set; } = "";

        [JsonProperty("target_checked_by")]
        public string TargetCheckedBy { get; set; } = "";

        [JsonProperty("target_approved_by")]
        public string TargetApprovedBy { get; set; } = "";

        [JsonProperty("target_designed_by")]
        public string TargetDesignedBy { get; set; } = "";

        [JsonProperty("target_issue_date")]
        public string TargetIssueDate { get; set; } = "";

        [JsonProperty("custom_parameters")]
        public Dictionary<string, string> CustomParameters { get; set; } = new Dictionary<string, string>();

        [JsonIgnore]
        public bool IsSelected
        {
            get => _isSelected;
            set
            {
                _isSelected = value;
                PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(nameof(IsSelected)));
            }
        }

        [JsonIgnore]
        public string NumberChangeSummary =>
            string.Equals(CurrentNumber, TargetNumber, StringComparison.Ordinal)
                ? CurrentNumber
                : $"{CurrentNumber}  ➔  {TargetNumber}";

        [JsonIgnore]
        public string NameChangeSummary =>
            string.Equals(CurrentName, TargetName, StringComparison.Ordinal)
                ? CurrentName
                : $"{CurrentName}  ➔  {TargetName}";

        [JsonIgnore]
        public string CollectionChangeSummary =>
            string.Equals(CurrentCollection, TargetCollection, StringComparison.Ordinal)
                ? CurrentCollection
                : $"{CurrentCollection}  ➔  {TargetCollection}";

        [JsonIgnore]
        public string PropertiesSummary
        {
            get
            {
                var parts = new List<string>();
                if (!string.IsNullOrWhiteSpace(TargetSheetOrder)) parts.Add($"Orden: {TargetSheetOrder}");
                if (!string.IsNullOrWhiteSpace(TargetDrawnBy)) parts.Add($"Dib: {TargetDrawnBy}");
                if (!string.IsNullOrWhiteSpace(TargetCheckedBy)) parts.Add($"Rev: {TargetCheckedBy}");
                if (!string.IsNullOrWhiteSpace(TargetApprovedBy)) parts.Add($"Apr: {TargetApprovedBy}");
                if (CustomParameters != null && CustomParameters.Count > 0)
                {
                    foreach (var kv in CustomParameters)
                        parts.Add($"{kv.Key}: {kv.Value}");
                }
                return parts.Count > 0 ? string.Join(" | ", parts) : "—";
            }
        }

        public event PropertyChangedEventHandler? PropertyChanged;
    }

    public class NewSheetQueueItem : INotifyPropertyChanged
    {
        private bool _isSelected = true;

        [JsonProperty("sheet_number")]
        public string SheetNumber { get; set; } = "";

        [JsonProperty("sheet_name")]
        public string SheetName { get; set; } = "";

        [JsonProperty("sheet_collection")]
        public string SheetCollection { get; set; } = "";

        [JsonProperty("drawn_by")]
        public string DrawnBy { get; set; } = "";

        [JsonProperty("checked_by")]
        public string CheckedBy { get; set; } = "";

        [JsonProperty("approved_by")]
        public string ApprovedBy { get; set; } = "";

        [JsonProperty("designed_by")]
        public string DesignedBy { get; set; } = "";

        [JsonProperty("issue_date")]
        public string IssueDate { get; set; } = "";

        [JsonIgnore]
        public bool IsSelected
        {
            get => _isSelected;
            set
            {
                _isSelected = value;
                PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(nameof(IsSelected)));
            }
        }

        public event PropertyChangedEventHandler? PropertyChanged;
    }

    public class SheetQueuePayload
    {
        [JsonProperty("queue_version")]
        public string QueueVersion { get; set; } = "1.0";

        [JsonProperty("status")]
        public string Status { get; set; } = "pending";

        [JsonProperty("source")]
        public string Source { get; set; } = "REVIEW_WEB";

        [JsonProperty("project_id")]
        public string ProjectId { get; set; } = "";

        [JsonProperty("export_id")]
        public string ExportId { get; set; } = "";

        [JsonProperty("revit_project_name")]
        public string RevitProjectName { get; set; } = "";

        [JsonProperty("updated_at")]
        public string UpdatedAt { get; set; } = "";

        [JsonProperty("applied_at")]
        public string? AppliedAt { get; set; }

        [JsonProperty("changes")]
        public List<SheetChangeItem> Changes { get; set; } = new List<SheetChangeItem>();

        [JsonProperty("new_sheets")]
        public List<NewSheetQueueItem> NewSheets { get; set; } = new List<NewSheetQueueItem>();
    }

    public class SheetQueueDocument
    {
        public string AdminDocId { get; set; } = "";
        public string Title { get; set; } = "";
        public string ProjectId { get; set; } = "";
        public string CreatedAt { get; set; } = "";
        public SheetQueuePayload Payload { get; set; } = new SheetQueuePayload();

        public string DisplayTitle =>
            $"{Payload.RevitProjectName ?? Title} [{Payload.Status?.ToUpperInvariant()}] — {Payload.Changes.Count} mod. / {Payload.NewSheets.Count} nuevos ({Payload.UpdatedAt})";
    }

    public class SheetQueueApplyResult
    {
        public bool Success { get; set; }
        public int ModifiedCount { get; set; }
        public int CreatedCount { get; set; }
        public int CollectionsAssignedCount { get; set; }
        public List<string> Logs { get; set; } = new List<string>();
        public List<string> Errors { get; set; } = new List<string>();
    }

    public static class SheetsQueueService
    {
        private const string SupabaseUrl = "https://irkrljhfbtnjspyvrapa.supabase.co";
        private const string AnonKey = "eyJhbGciOiJIUzI1NiIsInR5cCI6IkpXVCJ9.eyJpc3MiOiJzdXBhYmFzZSIsInJlZiI6Imlya3JsamhmYnRuanNweXZyYXBhIiwicm9sZSI6ImFub24iLCJpYXQiOjE3NzQzMTI3OTEsImV4cCI6MjA4OTg4ODc5MX0.c_Rd0hc11NRXX3aWYOODhU-e5GjGcoxobJQbBOc6LOE";

        private static string Normalize(string? input)
        {
            if (string.IsNullOrWhiteSpace(input)) return "";
            return input.Trim().ToLowerInvariant();
        }

        /// <summary>
        /// Obtiene todas las colas de cambios de Sheets almacenadas en Supabase (admin_documents?type=eq.revit_sheets_queue)
        /// </summary>
        public static async Task<List<SheetQueueDocument>> FetchQueuesAsync(string? currentRevitProjectName = null)
        {
            var result = new List<SheetQueueDocument>();
            using var client = new HttpClient { Timeout = TimeSpan.FromSeconds(20) };
            client.DefaultRequestHeaders.Add("apikey", AnonKey);
            client.DefaultRequestHeaders.Add("Authorization", $"Bearer {AnonKey}");

            string url = $"{SupabaseUrl}/rest/v1/admin_documents?type=eq.revit_sheets_queue&order=created_at.desc&limit=25";
            var response = await client.GetAsync(url);
            string json = await response.Content.ReadAsStringAsync();

            if (!response.IsSuccessStatusCode)
            {
                throw new Exception($"Error consultando cola en Supabase ({response.StatusCode}): {json}");
            }

            var array = JArray.Parse(json);
            foreach (var item in array)
            {
                try
                {
                    string docId = item["id"]?.ToString() ?? "";
                    string title = item["title"]?.ToString() ?? "";
                    string projId = item["project_id"]?.ToString() ?? "";
                    string createdAt = item["created_at"]?.ToString() ?? "";
                    string contentStr = item["content"]?.ToString() ?? "{}";

                    var payload = JsonConvert.DeserializeObject<SheetQueuePayload>(contentStr) ?? new SheetQueuePayload();
                    result.Add(new SheetQueueDocument
                    {
                        AdminDocId = docId,
                        Title = title,
                        ProjectId = projId,
                        CreatedAt = createdAt,
                        Payload = payload
                    });
                }
                catch
                {
                    // Ignorar registros malformados
                }
            }

            // Ordenar priorizando las colas pendientes del proyecto actual
            if (!string.IsNullOrWhiteSpace(currentRevitProjectName))
            {
                string normCurrent = Normalize(currentRevitProjectName);
                result = result
                    .OrderByDescending(q => q.Payload.Status == "pending")
                    .ThenByDescending(q =>
                    {
                        string normQueueProj = Normalize(q.Payload.RevitProjectName);
                        return !string.IsNullOrEmpty(normQueueProj) &&
                               (normQueueProj.Contains(normCurrent) || normCurrent.Contains(normQueueProj));
                    })
                    .ToList();
            }

            return result;
        }

        /// <summary>
        /// Marca una cola en Supabase como 'applied' tras haber aplicado los cambios en Revit
        /// </summary>
        public static async Task MarkQueueAsAppliedAsync(SheetQueueDocument queueDoc, SheetQueueApplyResult applyResult)
        {
            if (queueDoc == null || string.IsNullOrWhiteSpace(queueDoc.AdminDocId)) return;

            using var client = new HttpClient { Timeout = TimeSpan.FromSeconds(20) };
            client.DefaultRequestHeaders.Add("apikey", AnonKey);
            client.DefaultRequestHeaders.Add("Authorization", $"Bearer {AnonKey}");

            queueDoc.Payload.Status = "applied";
            queueDoc.Payload.AppliedAt = DateTime.UtcNow.ToString("o");
            queueDoc.Payload.UpdatedAt = DateTime.UtcNow.ToString("o");

            var updateBody = new
            {
                title = $"REVIT_SHEETS_QUEUE :: {queueDoc.Payload.RevitProjectName} [APPLIED]",
                content = JsonConvert.SerializeObject(queueDoc.Payload, Formatting.Indented)
            };

            string endpoint = $"{SupabaseUrl}/rest/v1/admin_documents?id=eq.{queueDoc.AdminDocId}";
            var request = new HttpRequestMessage(new HttpMethod("PATCH"), endpoint)
            {
                Content = new StringContent(JsonConvert.SerializeObject(updateBody), Encoding.UTF8, "application/json")
            };
            await client.SendAsync(request);
        }

        /// <summary>
        /// Aplica los cambios de Sheets seleccionados al documento activo de Revit dentro de una transacción segura
        /// con renumeración en 2 pasos (anti-colisión de números duplicados) y soporte de Sheet Collections.
        /// </summary>
        public static SheetQueueApplyResult ApplyQueueToRevitDocument(
            Document doc,
            List<SheetChangeItem> selectedChanges,
            List<NewSheetQueueItem> selectedNewSheets)
        {
            var result = new SheetQueueApplyResult();

            if (doc == null)
            {
                result.Errors.Add("No hay un documento activo en Revit.");
                return result;
            }

            // 1. Indexar todos los ViewSheet actuales por ElementId y por SheetNumber
            var allSheets = new FilteredElementCollector(doc)
                .OfClass(typeof(ViewSheet))
                .Cast<ViewSheet>()
                .Where(s => !s.IsTemplate)
                .ToList();

            var sheetsById = new Dictionary<long, ViewSheet>();
            var sheetsByNumber = new Dictionary<string, ViewSheet>(StringComparer.OrdinalIgnoreCase);

            foreach (var vs in allSheets)
            {
                sheetsById[vs.Id.Value] = vs;
                if (!string.IsNullOrWhiteSpace(vs.SheetNumber))
                {
                    sheetsByNumber[vs.SheetNumber.Trim()] = vs;
                }
            }

            // 2. Indexar Sheet Collections existentes en el modelo (Revit 2025+: OST_SheetCollections)
            var collectionsByName = new Dictionary<string, ElementId>(StringComparer.OrdinalIgnoreCase);
            try
            {
                var colElements = new FilteredElementCollector(doc)
                    .OfCategory(BuiltInCategory.OST_SheetCollections)
                    .WhereElementIsNotElementType()
                    .ToList();

                foreach (var colEl in colElements)
                {
                    if (!string.IsNullOrWhiteSpace(colEl.Name))
                    {
                        collectionsByName[colEl.Name.Trim()] = colEl.Id;
                    }
                }
            }
            catch
            {
                // En caso de versiones donde OST_SheetCollections no aplique
            }

            using (var trans = new Transaction(doc, "Review: Aplicar Cola de Cambios de Sheets"))
            {
                trans.Start();
                try
                {
                    // Resolver pares (ViewSheet, SheetChangeItem)
                    var resolvedPairs = new List<(ViewSheet Sheet, SheetChangeItem Change)>();
                    foreach (var change in selectedChanges)
                    {
                        ViewSheet? targetSheet = null;
                        if (!string.IsNullOrWhiteSpace(change.ElementId) && long.TryParse(change.ElementId.Trim(), out long parsedId))
                        {
                            sheetsById.TryGetValue(parsedId, out targetSheet);
                        }
                        if (targetSheet == null && !string.IsNullOrWhiteSpace(change.CurrentNumber))
                        {
                            sheetsByNumber.TryGetValue(change.CurrentNumber.Trim(), out targetSheet);
                        }

                        if (targetSheet == null)
                        {
                            result.Errors.Add($"No se encontró el Sheet ID={change.ElementId} ({change.CurrentNumber} - {change.CurrentName}) en el modelo.");
                            continue;
                        }

                        resolvedPairs.Add((targetSheet, change));
                    }

                    // PASO 0: Asegurar que todas las Sheet Collections destino existan
                    var requiredCollections = resolvedPairs
                        .Select(p => p.Change.TargetCollection)
                        .Concat(selectedNewSheets.Select(n => n.SheetCollection))
                        .Where(c => !string.IsNullOrWhiteSpace(c) && !c.Equals("SIN COLECCIÓN", StringComparison.OrdinalIgnoreCase))
                        .Select(c => c.Trim())
                        .Distinct(StringComparer.OrdinalIgnoreCase)
                        .ToList();

                    foreach (var colName in requiredCollections)
                    {
                        if (!collectionsByName.ContainsKey(colName))
                        {
                            ElementId? createdColId = TryCreateSheetCollection(doc, colName);
                            if (createdColId != null && createdColId != ElementId.InvalidElementId)
                            {
                                collectionsByName[colName] = createdColId;
                                result.Logs.Add($"Categoría / Sheet Collection creada: '{colName}' (ID: {createdColId.Value})");
                            }
                        }
                    }

                    // PASO 1: Renumeración temporal anti-colisión para todos los planos que cambian su SheetNumber
                    int tmpIdx = 1;
                    string batchToken = Guid.NewGuid().ToString("N").Substring(0, 5).ToUpperInvariant();
                    var sheetsWithTempNumbers = new List<(ViewSheet Sheet, SheetChangeItem Change)>();

                    foreach (var pair in resolvedPairs)
                    {
                        string newNum = (pair.Change.TargetNumber ?? "").Trim();
                        if (!string.IsNullOrEmpty(newNum) &&
                            !string.Equals(pair.Sheet.SheetNumber, newNum, StringComparison.Ordinal))
                        {
                            try
                            {
                                pair.Sheet.SheetNumber = $"__TMP_{batchToken}_{tmpIdx++}";
                                sheetsWithTempNumbers.Add(pair);
                            }
                            catch (Exception exTmp)
                            {
                                result.Errors.Add($"Error asignando número temporal a {pair.Change.CurrentNumber}: {exTmp.Message}");
                            }
                        }
                    }

                    // PASO 2: Aplicar SheetNumber definitivo, SheetName, Sheet Collection y Parámetros
                    foreach (var pair in resolvedPairs)
                    {
                        var sheet = pair.Sheet;
                        var ch = pair.Change;
                        bool sheetModified = false;

                        try
                        {
                            // 2.1 Número definitivo
                            string targetNum = (ch.TargetNumber ?? "").Trim();
                            if (!string.IsNullOrEmpty(targetNum) && !string.Equals(sheet.SheetNumber, targetNum, StringComparison.Ordinal))
                            {
                                // Verificar si otro plano ajeno al lote tiene ese número
                                var conflicting = new FilteredElementCollector(doc)
                                    .OfClass(typeof(ViewSheet))
                                    .Cast<ViewSheet>()
                                    .FirstOrDefault(s => !s.IsTemplate && s.Id != sheet.Id && string.Equals(s.SheetNumber, targetNum, StringComparison.OrdinalIgnoreCase));

                                if (conflicting != null)
                                {
                                    // Liberar el número del plano en conflicto añadiendo sufijo
                                    conflicting.SheetNumber = $"{conflicting.SheetNumber}_OLD_{tmpIdx++}";
                                }

                                sheet.SheetNumber = targetNum;
                                sheetModified = true;
                            }

                            // 2.2 Nombre del plano
                            string targetName = (ch.TargetName ?? "").Trim();
                            if (!string.IsNullOrEmpty(targetName) && !string.Equals(sheet.Name, targetName, StringComparison.Ordinal))
                            {
                                sheet.Name = targetName;
                                sheetModified = true;
                            }

                            // 2.3 Categoría / Sheet Collection
                            if (!string.IsNullOrWhiteSpace(ch.TargetCollection))
                            {
                                bool colUpdated = AssignSheetCollection(sheet, ch.TargetCollection.Trim(), collectionsByName);
                                if (colUpdated)
                                {
                                    result.CollectionsAssignedCount++;
                                    sheetModified = true;
                                }
                            }

                            // 2.4 Parámetros estándar y personalizados (en ViewSheet y en su TitleBlock)
                            if (ApplySheetParameters(doc, sheet, ch))
                            {
                                sheetModified = true;
                            }

                            if (sheetModified)
                            {
                                result.ModifiedCount++;
                                result.Logs.Add($"Actualizado: [{sheet.SheetNumber}] {sheet.Name} (Categoría: {ch.TargetCollection})");
                            }
                        }
                        catch (Exception exSheet)
                        {
                            result.Errors.Add($"Error actualizando plano {ch.CurrentNumber} -> {ch.TargetNumber}: {exSheet.Message}");
                        }
                    }

                    // PASO 3: Crear nuevos planos encolados
                    if (selectedNewSheets != null && selectedNewSheets.Count > 0)
                    {
                        ElementId titleBlockTypeId = FindDefaultTitleBlockTypeId(doc);
                        foreach (var newSh in selectedNewSheets)
                        {
                            try
                            {
                                ViewSheet created = ViewSheet.Create(doc, titleBlockTypeId);
                                if (!string.IsNullOrWhiteSpace(newSh.SheetNumber))
                                    created.SheetNumber = newSh.SheetNumber.Trim();
                                if (!string.IsNullOrWhiteSpace(newSh.SheetName))
                                    created.Name = newSh.SheetName.Trim();

                                if (!string.IsNullOrWhiteSpace(newSh.SheetCollection))
                                {
                                    AssignSheetCollection(created, newSh.SheetCollection.Trim(), collectionsByName);
                                }

                                var synthChange = new SheetChangeItem
                                {
                                    TargetDrawnBy = newSh.DrawnBy,
                                    TargetCheckedBy = newSh.CheckedBy,
                                    TargetApprovedBy = newSh.ApprovedBy,
                                    TargetDesignedBy = newSh.DesignedBy,
                                    TargetIssueDate = newSh.IssueDate
                                };
                                ApplySheetParameters(doc, created, synthChange);

                                result.CreatedCount++;
                                result.Logs.Add($"Nuevo plano creado: [{created.SheetNumber}] {created.Name}");
                            }
                            catch (Exception exNew)
                            {
                                result.Errors.Add($"Error creando nuevo plano {newSh.SheetNumber}: {exNew.Message}");
                            }
                        }
                    }

                    trans.Commit();
                    result.Success = true;
                }
                catch (Exception exTrans)
                {
                    if (trans.HasStarted()) trans.RollBack();
                    result.Success = false;
                    result.Errors.Add($"Error crítico en transacción Revit: {exTrans.Message}");
                }
            }

            return result;
        }

        private static ElementId? TryCreateSheetCollection(Document doc, string collectionName)
        {
            try
            {
                // En Revit 2025+, SheetCollection.Create(Document, string) o SheetCollection.Create(Document)
                var sheetCollectionType = typeof(Document).Assembly.GetType("Autodesk.Revit.DB.SheetCollection");
                if (sheetCollectionType != null)
                {
                    var createWithName = sheetCollectionType.GetMethod("Create", BindingFlags.Public | BindingFlags.Static, null, new[] { typeof(Document), typeof(string) }, null);
                    if (createWithName != null)
                    {
                        var createdObj = createWithName.Invoke(null, new object[] { doc, collectionName }) as Element;
                        return createdObj?.Id;
                    }

                    var createDocOnly = sheetCollectionType.GetMethod("Create", BindingFlags.Public | BindingFlags.Static, null, new[] { typeof(Document) }, null);
                    if (createDocOnly != null)
                    {
                        var createdObj = createDocOnly.Invoke(null, new object[] { doc }) as Element;
                        if (createdObj != null)
                        {
                            createdObj.Name = collectionName;
                            return createdObj.Id;
                        }
                    }
                }
            }
            catch
            {
                // Ignorar si la versión de API no soporta SheetCollection.Create
            }
            return null;
        }

        private static bool AssignSheetCollection(ViewSheet sheet, string targetCollectionName, Dictionary<string, ElementId> collectionsByName)
        {
            try
            {
                Parameter? colParam = sheet.LookupParameter("Sheet Collection")
                                   ?? sheet.LookupParameter("Colección de planos")
                                   ?? sheet.get_Parameter(BuiltInParameter.SHEET_COLLECTION);

                if (colParam != null && !colParam.IsReadOnly)
                {
                    if (targetCollectionName.Equals("SIN COLECCIÓN", StringComparison.OrdinalIgnoreCase) ||
                        targetCollectionName.Equals("NONE", StringComparison.OrdinalIgnoreCase))
                    {
                        if (colParam.StorageType == StorageType.ElementId)
                        {
                            colParam.Set(ElementId.InvalidElementId);
                            return true;
                        }
                    }
                    else if (collectionsByName.TryGetValue(targetCollectionName, out ElementId? colId) && colId != null)
                    {
                        if (colParam.StorageType == StorageType.ElementId)
                        {
                            if (colParam.AsElementId() != colId)
                            {
                                colParam.Set(colId);
                                return true;
                            }
                        }
                        else if (colParam.StorageType == StorageType.String)
                        {
                            colParam.Set(targetCollectionName);
                            return true;
                        }
                    }
                    else if (colParam.StorageType == StorageType.String)
                    {
                        colParam.Set(targetCollectionName);
                        return true;
                    }
                }
            }
            catch
            {
                // Ignorar si el parámetro no es editable
            }
            return false;
        }

        private static bool ApplySheetParameters(Document doc, ViewSheet sheet, SheetChangeItem ch)
        {
            bool anyChanged = false;

            // Encontrar el TitleBlock asociado a este ViewSheet por si algún parámetro (ej. Númeración plano) reside en el rótulo
            Element? titleBlockInstance = null;
            try
            {
                titleBlockInstance = new FilteredElementCollector(doc, sheet.Id)
                    .OfCategory(BuiltInCategory.OST_TitleBlocks)
                    .WhereElementIsNotElementType()
                    .FirstOrDefault();
            }
            catch { }

            void SetParamIfPresent(string[] paramNames, BuiltInParameter? bip, string? value)
            {
                if (value == null) return;
                string trimmed = value.Trim();
                if (string.IsNullOrEmpty(trimmed)) return;

                Parameter? p = null;
                if (bip.HasValue)
                {
                    p = sheet.get_Parameter(bip.Value);
                }
                if (p == null)
                {
                    foreach (var name in paramNames)
                    {
                        p = sheet.LookupParameter(name);
                        if (p != null) break;
                    }
                }
                if ((p == null || p.IsReadOnly) && titleBlockInstance != null)
                {
                    foreach (var name in paramNames)
                    {
                        p = titleBlockInstance.LookupParameter(name);
                        if (p != null && !p.IsReadOnly) break;
                    }
                }

                if (p != null && !p.IsReadOnly && p.StorageType == StorageType.String)
                {
                    string currentVal = p.AsString() ?? "";
                    if (!string.Equals(currentVal, trimmed, StringComparison.Ordinal))
                    {
                        p.Set(trimmed);
                        anyChanged = true;
                    }
                }
            }

            SetParamIfPresent(new[] { "Númeración plano", "Numeración plano", "Numeracion plano", "Sheet Order" }, null, ch.TargetSheetOrder);
            SetParamIfPresent(new[] { "Drawn By", "Dibujado por" }, BuiltInParameter.SHEET_DRAWN_BY, ch.TargetDrawnBy);
            SetParamIfPresent(new[] { "Checked By", "Revisado por" }, BuiltInParameter.SHEET_CHECKED_BY, ch.TargetCheckedBy);
            SetParamIfPresent(new[] { "Approved By", "Aprobado por" }, BuiltInParameter.SHEET_APPROVED_BY, ch.TargetApprovedBy);
            SetParamIfPresent(new[] { "Designed By", "Diseñado por" }, BuiltInParameter.SHEET_DESIGNED_BY, ch.TargetDesignedBy);
            SetParamIfPresent(new[] { "Sheet Issue Date", "Fecha de emisión" }, BuiltInParameter.SHEET_ISSUE_DATE, ch.TargetIssueDate);

            if (ch.CustomParameters != null)
            {
                foreach (var kv in ch.CustomParameters)
                {
                    if (!string.IsNullOrWhiteSpace(kv.Key) && kv.Value != null)
                    {
                        SetParamIfPresent(new[] { kv.Key }, null, kv.Value);
                    }
                }
            }

            return anyChanged;
        }

        private static ElementId FindDefaultTitleBlockTypeId(Document doc)
        {
            // Intentar clonar el tipo de rótulo del primer plano existente
            var existingTitleBlock = new FilteredElementCollector(doc)
                .OfCategory(BuiltInCategory.OST_TitleBlocks)
                .WhereElementIsNotElementType()
                .FirstOrDefault();

            if (existingTitleBlock != null)
            {
                return existingTitleBlock.GetTypeId();
            }

            var anyTitleBlockSymbol = new FilteredElementCollector(doc)
                .OfCategory(BuiltInCategory.OST_TitleBlocks)
                .WhereElementIsElementType()
                .FirstOrDefault();

            return anyTitleBlockSymbol?.Id ?? ElementId.InvalidElementId;
        }
    }
}
