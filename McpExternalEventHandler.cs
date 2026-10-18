using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Autodesk.Revit.DB;
using Autodesk.Revit.DB.Architecture;
using Autodesk.Revit.UI;
using Newtonsoft.Json;
using Newtonsoft.Json.Linq;

namespace RevitToSupabasePlugin
{
    public class McpWorkItem
    {
        public string ToolName { get; set; } = string.Empty;
        public JObject Arguments { get; set; } = new JObject();
        public TaskCompletionSource<JObject> Tcs { get; set; } = new TaskCompletionSource<JObject>(TaskCreationOptions.RunContinuationsAsynchronously);
    }

    public class McpExternalEventHandler : IExternalEventHandler
    {
        private readonly ConcurrentQueue<McpWorkItem> _queue = new ConcurrentQueue<McpWorkItem>();
        private const int DefaultTimeoutSeconds = 45;

        public async Task<JObject> EnqueueTaskAsync(ExternalEvent extEvent, string tool, JObject args)
        {
            var workItem = new McpWorkItem
            {
                ToolName = tool,
                Arguments = args ?? new JObject()
            };

            _queue.Enqueue(workItem);
            extEvent.Raise();

            using var cts = new CancellationTokenSource(TimeSpan.FromSeconds(DefaultTimeoutSeconds));
            try
            {
                var completedTask = await Task.WhenAny(workItem.Tcs.Task, Task.Delay(Timeout.Infinite, cts.Token));
                if (completedTask == workItem.Tcs.Task)
                {
                    return await workItem.Tcs.Task;
                }
            }
            catch (OperationCanceledException)
            {
            }

            return new JObject
            {
                ["status"] = "error",
                ["error"] = $"Timeout ({DefaultTimeoutSeconds}s) esperando al hilo principal de Revit. Verifique que Revit no tenga una ventana modal abierta."
            };
        }

        public void Execute(UIApplication app)
        {
            while (_queue.TryDequeue(out McpWorkItem? item))
            {
                if (item == null) continue;

                try
                {
                    JObject res = ExecuteSingleTool(app, item.ToolName, item.Arguments);
                    item.Tcs.TrySetResult(res);
                }
                catch (Exception ex)
                {
                    item.Tcs.TrySetResult(new JObject
                    {
                        ["status"] = "error",
                        ["error"] = ex.Message,
                        ["stackTrace"] = ex.StackTrace ?? ""
                    });
                }
            }
        }

        private JObject ExecuteSingleTool(UIApplication app, string toolName, JObject args)
        {
            UIDocument? uidoc = app.ActiveUIDocument;
            Document? doc = uidoc?.Document;

            JObject result = new JObject();

            if (doc == null || uidoc == null)
            {
                result["status"] = "error";
                result["error"] = "No hay ningún documento o proyecto activo abierto en Autodesk Revit.";
                return result;
            }

            switch (toolName)
            {
                case "revit_get_active_session":
                {
                    var projInfo = doc.ProjectInformation;
                    result["status"] = "success";
                    result["project_name"] = projInfo?.Name ?? "Sin Nombre";
                    result["project_number"] = projInfo?.Number ?? "S/N";
                    result["project_address"] = projInfo?.Address ?? "";
                    result["client_name"] = projInfo?.ClientName ?? "";
                    result["active_view"] = doc.ActiveView?.Name ?? "Sin Vista";
                    result["active_view_id"] = doc.ActiveView?.Id.Value ?? 0;
                    result["view_type"] = doc.ActiveView?.ViewType.ToString() ?? "";
                    result["document_path"] = string.IsNullOrEmpty(doc.PathName) ? "No guardado en disco" : doc.PathName;
                    result["is_workshared"] = doc.IsWorkshared;
                    result["usuario_supabase"] = AuthenticationService.IsLoggedIn ? AuthenticationService.UserEmail : "Sesión AnonKey";
                    result["proyecto_activo_supabase"] = AuthenticationService.ActiveProjectName;
                    result["selection_count"] = uidoc.Selection.GetElementIds().Count;
                    break;
                }

                case "revit_get_selection":
                {
                    var selIds = uidoc.Selection.GetElementIds();
                    JArray elements = new JArray();

                    foreach (ElementId id in selIds)
                    {
                        Element? elem = doc.GetElement(id);
                        if (elem == null) continue;
                        elements.Add(SerializeElementSummary(doc, elem));
                    }

                    result["status"] = "success";
                    result["count"] = elements.Count;
                    result["selected_elements"] = elements;
                    break;
                }

                case "revit_select_elements":
                {
                    var idsArray = args["element_ids"] as JArray;
                    bool zoomToElements = args["zoom_to_elements"]?.Value<bool>() ?? true;

                    if (idsArray == null || idsArray.Count == 0)
                    {
                        result["status"] = "error";
                        result["error"] = "Debe proporcionar un arreglo 'element_ids' con al menos un ID entero.";
                        break;
                    }

                    List<ElementId> validIds = new List<ElementId>();
                    foreach (var token in idsArray)
                    {
                        long rawId = token.Value<long>();
                        ElementId eid = new ElementId(rawId);
                        if (doc.GetElement(eid) != null)
                        {
                            validIds.Add(eid);
                        }
                    }

                    uidoc.Selection.SetElementIds(validIds);
                    if (zoomToElements && validIds.Count > 0)
                    {
                        uidoc.ShowElements(validIds);
                    }

                    result["status"] = "success";
                    result["selected_count"] = validIds.Count;
                    result["selected_ids"] = new JArray(validIds.Select(x => x.Value));
                    break;
                }

                case "revit_query_elements":
                {
                    string categoryName = args["category"]?.ToString() ?? "Floors";
                    string levelFilter = args["level_name"]?.ToString() ?? "";
                    string searchFilter = args["search_text"]?.ToString() ?? "";
                    int limit = Math.Clamp(args["limit"]?.Value<int>() ?? 50, 1, 500);

                    BuiltInCategory? bic = ResolveBuiltInCategory(categoryName);
                    FilteredElementCollector collector = new FilteredElementCollector(doc).WhereElementIsNotElementType();

                    if (bic.HasValue)
                    {
                        collector.OfCategory(bic.Value);
                    }

                    JArray items = new JArray();
                    int totalMatched = 0;

                    foreach (Element elem in collector)
                    {
                        if (elem.Category == null) continue;
                        if (!bic.HasValue && !elem.Category.Name.Contains(categoryName, StringComparison.OrdinalIgnoreCase)) continue;

                        if (!string.IsNullOrWhiteSpace(levelFilter))
                        {
                            string lvlName = GetElementLevelName(doc, elem);
                            if (!lvlName.Contains(levelFilter, StringComparison.OrdinalIgnoreCase)) continue;
                        }

                        if (!string.IsNullOrWhiteSpace(searchFilter))
                        {
                            string comm = elem.get_Parameter(BuiltInParameter.ALL_MODEL_INSTANCE_COMMENTS)?.AsString() ?? "";
                            bool matches = elem.Name.Contains(searchFilter, StringComparison.OrdinalIgnoreCase) || comm.Contains(searchFilter, StringComparison.OrdinalIgnoreCase);
                            if (!matches) continue;
                        }

                        totalMatched++;
                        if (items.Count < limit)
                        {
                            items.Add(SerializeElementSummary(doc, elem));
                        }
                    }

                    result["status"] = "success";
                    result["category_queried"] = categoryName;
                    result["total_matched"] = totalMatched;
                    result["returned_count"] = items.Count;
                    result["elements"] = items;
                    break;
                }

                case "revit_get_levels_and_rooms":
                {
                    JArray levelsArr = new JArray();
                    var levels = new FilteredElementCollector(doc)
                        .OfClass(typeof(Level))
                        .Cast<Level>()
                        .OrderBy(l => l.Elevation)
                        .ToList();

                    foreach (Level lvl in levels)
                    {
                        levelsArr.Add(new JObject
                        {
                            ["id"] = lvl.Id.Value,
                            ["name"] = lvl.Name,
                            ["elevation_m"] = Math.Round(lvl.Elevation * 0.3048, 3)
                        });
                    }

                    JArray roomsArr = new JArray();
                    var rooms = new FilteredElementCollector(doc)
                        .OfCategory(BuiltInCategory.OST_Rooms)
                        .WhereElementIsNotElementType()
                        .Cast<Room>()
                        .Where(r => r.Area > 0)
                        .ToList();

                    foreach (Room rm in rooms)
                    {
                        roomsArr.Add(new JObject
                        {
                            ["id"] = rm.Id.Value,
                            ["name"] = rm.Name,
                            ["number"] = rm.Number,
                            ["level"] = rm.Level?.Name ?? "",
                            ["area_m2"] = Math.Round(rm.Area * 0.092903, 2),
                            ["perimeter_m"] = Math.Round(rm.Perimeter * 0.3048, 2)
                        });
                    }

                    result["status"] = "success";
                    result["levels_count"] = levelsArr.Count;
                    result["levels"] = levelsArr;
                    result["rooms_count"] = roomsArr.Count;
                    result["rooms"] = roomsArr;
                    break;
                }

                case "revit_get_materials":
                {
                    string search = args["search"]?.ToString() ?? "";
                    int limit = Math.Clamp(args["limit"]?.Value<int>() ?? 100, 1, 500);

                    var mats = new FilteredElementCollector(doc)
                        .OfClass(typeof(Material))
                        .Cast<Material>()
                        .Where(m => string.IsNullOrWhiteSpace(search) || m.Name.Contains(search, StringComparison.OrdinalIgnoreCase))
                        .OrderBy(m => m.Name)
                        .Take(limit)
                        .ToList();

                    JArray matArr = new JArray();
                    foreach (Material m in mats)
                    {
                        matArr.Add(new JObject
                        {
                            ["id"] = m.Id.Value,
                            ["name"] = m.Name,
                            ["material_class"] = m.MaterialClass ?? "",
                            ["color_rgb"] = $"{m.Color.Red},{m.Color.Green},{m.Color.Blue}"
                        });
                    }

                    result["status"] = "success";
                    result["count"] = matArr.Count;
                    result["materials"] = matArr;
                    break;
                }

                case "revit_modify_parameters":
                {
                    var idsArray = args["element_ids"] as JArray;
                    var parametersObj = args["parameters"] as JObject;

                    if (idsArray == null || idsArray.Count == 0 || parametersObj == null)
                    {
                        result["status"] = "error";
                        result["error"] = "Se requieren 'element_ids' (array) y 'parameters' (objeto clave-valor).";
                        break;
                    }

                    int updatedElements = 0;
                    using (Transaction tx = new Transaction(doc, "MCP: Modificar Parámetros"))
                    {
                        tx.Start();
                        foreach (var token in idsArray)
                        {
                            Element? elem = doc.GetElement(new ElementId(token.Value<long>()));
                            if (elem == null) continue;

                            bool elemChanged = false;
                            foreach (var prop in parametersObj.Properties())
                            {
                                Parameter? p = elem.LookupParameter(prop.Name);
                                if (prop.Name.Equals("Comments", StringComparison.OrdinalIgnoreCase) || prop.Name.Equals("Comentarios", StringComparison.OrdinalIgnoreCase))
                                {
                                    p ??= elem.get_Parameter(BuiltInParameter.ALL_MODEL_INSTANCE_COMMENTS);
                                }

                                if (p != null && !p.IsReadOnly)
                                {
                                    if (p.StorageType == StorageType.String)
                                    {
                                        p.Set(prop.Value.ToString());
                                        elemChanged = true;
                                    }
                                    else if (p.StorageType == StorageType.Double && double.TryParse(prop.Value.ToString(), out double dVal))
                                    {
                                        p.Set(dVal);
                                        elemChanged = true;
                                    }
                                    else if (p.StorageType == StorageType.Integer && int.TryParse(prop.Value.ToString(), out int iVal))
                                    {
                                        p.Set(iVal);
                                        elemChanged = true;
                                    }
                                }
                            }
                            if (elemChanged) updatedElements++;
                        }
                        tx.Commit();
                    }

                    result["status"] = "success";
                    result["updated_elements_count"] = updatedElements;
                    break;
                }

                case "revit_manage_sheets":
                {
                    string action = args["action"]?.ToString()?.ToLowerInvariant() ?? "list";

                    if (action == "list")
                    {
                        var sheets = new FilteredElementCollector(doc)
                            .OfClass(typeof(ViewSheet))
                            .Cast<ViewSheet>()
                            .Where(s => !s.IsTemplate)
                            .OrderBy(s => s.SheetNumber)
                            .ToList();

                        JArray sheetsArr = new JArray();
                        foreach (ViewSheet s in sheets)
                        {
                            JObject sObj = new JObject
                            {
                                ["id"] = s.Id.Value,
                                ["sheet_number"] = s.SheetNumber,
                                ["sheet_name"] = s.Name,
                                ["is_placeholder"] = s.IsPlaceholder
                            };

                            var tb = new FilteredElementCollector(doc, s.Id)
                                .OfCategory(BuiltInCategory.OST_TitleBlocks)
                                .FirstElement();
                            if (tb != null)
                            {
                                sObj["title_block"] = tb.Name;
                                sObj["title_block_type_id"] = tb.GetTypeId().Value;
                            }

                            var vpIds = s.GetAllViewports();
                            sObj["viewports_count"] = vpIds.Count;
                            sheetsArr.Add(sObj);
                        }

                        result["status"] = "success";
                        result["total_sheets"] = sheetsArr.Count;
                        result["sheets"] = sheetsArr;
                        break;
                    }

                    if (action == "create")
                    {
                        string sheetNum = args["sheet_number"]?.ToString() ?? "";
                        string sheetName = args["sheet_name"]?.ToString() ?? "NUEVO PLANO";
                        long templateSheetId = args["template_sheet_id"]?.Value<long>() ?? 0;
                        long tbTypeId = args["title_block_type_id"]?.Value<long>() ?? 0;

                        if (string.IsNullOrWhiteSpace(sheetNum))
                        {
                            result["status"] = "error";
                            result["error"] = "Se requiere 'sheet_number' para crear un plano.";
                            break;
                        }

                        ElementId targetTbId = ElementId.InvalidElementId;
                        if (tbTypeId > 0)
                        {
                            targetTbId = new ElementId(tbTypeId);
                        }
                        else if (templateSheetId > 0 && doc.GetElement(new ElementId(templateSheetId)) is ViewSheet tSheet)
                        {
                            var tb = new FilteredElementCollector(doc, tSheet.Id)
                                .OfCategory(BuiltInCategory.OST_TitleBlocks)
                                .FirstElement();
                            if (tb != null) targetTbId = tb.GetTypeId();
                        }
                        else
                        {
                            var defaultTb = new FilteredElementCollector(doc)
                                .OfCategory(BuiltInCategory.OST_TitleBlocks)
                                .WhereElementIsElementType()
                                .FirstOrDefault();
                            if (defaultTb != null) targetTbId = defaultTb.Id;
                        }

                        using (Transaction tx = new Transaction(doc, "MCP: Crear Plano"))
                        {
                            tx.Start();
                            ViewSheet newSheet = ViewSheet.Create(doc, targetTbId);
                            newSheet.SheetNumber = sheetNum;
                            newSheet.Name = sheetName;
                            tx.Commit();

                            result["status"] = "success";
                            result["created_sheet_id"] = newSheet.Id.Value;
                            result["sheet_number"] = newSheet.SheetNumber;
                            result["sheet_name"] = newSheet.Name;
                            result["title_block_type_id"] = targetTbId.Value;
                        }
                        break;
                    }

                    if (action == "shift_range" || action == "renumber" || action == "batch_rename")
                    {
                        var renamesArr = args["renames"] as JArray;
                        var newSheetsToInsert = args["new_sheets"] as JArray;
                        long cloneTbFromSheetId = args["clone_titleblock_from_sheet_id"]?.Value<long>() ?? 0;

                        List<(ViewSheet sheet, string targetNumber, string? targetName)> operations = new();

                        var allSheets = new FilteredElementCollector(doc)
                            .OfClass(typeof(ViewSheet))
                            .Cast<ViewSheet>()
                            .Where(s => !s.IsTemplate)
                            .ToList();

                        if (renamesArr != null && renamesArr.Count > 0)
                        {
                            foreach (var item in renamesArr)
                            {
                                string curNum = item["current_number"]?.ToString() ?? "";
                                string newNum = item["new_number"]?.ToString() ?? "";
                                string? newName = item["new_name"]?.ToString();

                                var targetSheet = allSheets.FirstOrDefault(s => s.SheetNumber.Equals(curNum, StringComparison.OrdinalIgnoreCase));
                                if (targetSheet != null && !string.IsNullOrEmpty(newNum))
                                {
                                    operations.Add((targetSheet, newNum, newName));
                                }
                            }
                        }
                        else if (args["offset"] != null)
                        {
                            int offset = args["offset"]?.Value<int>() ?? 0;
                            int startNum = args["start_number"]?.Value<int>() ?? 0;
                            int endNum = args["end_number"]?.Value<int>() ?? int.MaxValue;
                            string prefix = args["prefix"]?.ToString() ?? "";

                            foreach (var s in allSheets)
                            {
                                string numOnly = new string(s.SheetNumber.Where(char.IsDigit).ToArray());
                                if (int.TryParse(numOnly, out int n) && n >= startNum && n <= endNum)
                                {
                                    string finalNum = prefix.Length > 0 ? $"{prefix}{n + offset}" : (n + offset).ToString();
                                    operations.Add((s, finalNum, null));
                                }
                            }
                        }

                        if (operations.Count == 0 && (newSheetsToInsert == null || newSheetsToInsert.Count == 0))
                        {
                            result["status"] = "error";
                            result["error"] = "No se encontraron planos que coincidan con los criterios para renumerar.";
                            break;
                        }

                        ElementId targetTbId = ElementId.InvalidElementId;
                        if (cloneTbFromSheetId > 0 && doc.GetElement(new ElementId(cloneTbFromSheetId)) is ViewSheet refSheet)
                        {
                            var tb = new FilteredElementCollector(doc, refSheet.Id)
                                .OfCategory(BuiltInCategory.OST_TitleBlocks)
                                .FirstElement();
                            if (tb != null) targetTbId = tb.GetTypeId();
                        }
                        if (targetTbId == ElementId.InvalidElementId && operations.Count > 0)
                        {
                            var tb = new FilteredElementCollector(doc, operations[0].sheet.Id)
                                .OfCategory(BuiltInCategory.OST_TitleBlocks)
                                .FirstElement();
                            if (tb != null) targetTbId = tb.GetTypeId();
                        }
                        if (targetTbId == ElementId.InvalidElementId)
                        {
                            var defaultTb = new FilteredElementCollector(doc)
                                .OfCategory(BuiltInCategory.OST_TitleBlocks)
                                .WhereElementIsElementType()
                                .FirstOrDefault();
                            if (defaultTb != null) targetTbId = defaultTb.Id;
                        }

                        JArray logArr = new JArray();
                        JArray createdArr = new JArray();

                        using (Transaction tx = new Transaction(doc, "MCP: Desplazar y Crear Planos (Two-Pass)"))
                        {
                            tx.Start();

                            // PASE 1: Asignación temporal con prefijo único __TMP_ para liberar los números
                            string runTag = Guid.NewGuid().ToString("N").Substring(0, 6);
                            int tmpCounter = 1;
                            foreach (var op in operations)
                            {
                                op.sheet.SheetNumber = $"__TMP_{runTag}_{tmpCounter++}";
                            }

                            // PASE 2: Asignación definitiva sin riesgo de colisión
                            foreach (var op in operations)
                            {
                                op.sheet.SheetNumber = op.targetNumber;
                                if (!string.IsNullOrEmpty(op.targetName))
                                {
                                    op.sheet.Name = op.targetName;
                                }
                                logArr.Add($"Plano {op.sheet.Id.Value} renumerado a '{op.targetNumber}'");
                            }

                            // PASE 3: Inserción de nuevos planos liberados
                            if (newSheetsToInsert != null)
                            {
                                foreach (var token in newSheetsToInsert)
                                {
                                    string nsNum = token["sheet_number"]?.ToString() ?? "";
                                    string nsName = token["sheet_name"]?.ToString() ?? "NUEVO PLANO";
                                    if (string.IsNullOrWhiteSpace(nsNum)) continue;

                                    ViewSheet created = ViewSheet.Create(doc, targetTbId);
                                    created.SheetNumber = nsNum;
                                    created.Name = nsName;
                                    createdArr.Add(new JObject
                                    {
                                        ["id"] = created.Id.Value,
                                        ["sheet_number"] = created.SheetNumber,
                                        ["sheet_name"] = created.Name
                                    });
                                }
                            }

                            tx.Commit();
                        }

                        result["status"] = "success";
                        result["renamed_count"] = operations.Count;
                        result["renamed_log"] = logArr;
                        result["created_count"] = createdArr.Count;
                        result["created_sheets"] = createdArr;
                        break;
                    }

                    if (action == "apply_supabase_queue")
                    {
                        string projName = doc.ProjectInformation?.Name;
                        if (string.IsNullOrWhiteSpace(projName) || projName == "Project Name")
                            projName = doc.Title;

                        var queues = reviewPlugIn.Services.SheetsQueueService.FetchQueuesAsync(projName).GetAwaiter().GetResult();
                        var pendingQueue = queues.FirstOrDefault(q => q.Payload.Status == "pending") ?? queues.FirstOrDefault();

                        if (pendingQueue == null || (pendingQueue.Payload.Changes.Count == 0 && pendingQueue.Payload.NewSheets.Count == 0))
                        {
                            result["status"] = "error";
                            result["error"] = "No se encontró ninguna cola pendiente de cambios de Sheets en Supabase.";
                            break;
                        }

                        var applyRes = reviewPlugIn.Services.SheetsQueueService.ApplyQueueToRevitDocument(
                            doc,
                            pendingQueue.Payload.Changes,
                            pendingQueue.Payload.NewSheets
                        );

                        if (applyRes.Success)
                        {
                            reviewPlugIn.Services.SheetsQueueService.MarkQueueAsAppliedAsync(pendingQueue, applyRes).GetAwaiter().GetResult();
                        }

                        result["status"] = applyRes.Success ? "success" : "error";
                        result["queue_doc_id"] = pendingQueue.AdminDocId;
                        result["revit_project_name"] = pendingQueue.Payload.RevitProjectName;
                        result["modified_sheets_count"] = applyRes.ModifiedCount;
                        result["created_sheets_count"] = applyRes.CreatedCount;
                        result["collections_assigned_count"] = applyRes.CollectionsAssignedCount;
                        result["logs"] = JArray.FromObject(applyRes.Logs);
                        result["errors"] = JArray.FromObject(applyRes.Errors);
                        break;
                    }

                    if (action == "open_queue_dialog")
                    {
                        var win = new reviewPlugIn.Views.SyncSheetsQueueWindow(doc);
                        win.ShowDialog();
                        result["status"] = "success";
                        result["message"] = "Ventana de Cola de Cambios de Sheets abierta y procesada en Revit.";
                        break;
                    }

                    result["status"] = "error";
                    result["error"] = $"Acción '{action}' no soportada en revit_manage_sheets.";
                    break;
                }

                case "revit_get_element_parameters":
                {
                    long elemIdLong = args["element_id"]?.Value<long>() ?? 0;
                    if (elemIdLong <= 0)
                    {
                        result["status"] = "error";
                        result["error"] = "Se requiere un 'element_id' numérico válido.";
                        break;
                    }

                    Element? elem = doc.GetElement(new ElementId(elemIdLong));
                    if (elem == null)
                    {
                        result["status"] = "error";
                        result["error"] = $"No se encontró el elemento con ID {elemIdLong}.";
                        break;
                    }

                    result["status"] = "success";
                    result["element_id"] = elem.Id.Value;
                    result["element_name"] = elem.Name;
                    result["category"] = elem.Category?.Name ?? "Desconocida";

                    JArray instParams = new JArray();
                    foreach (Parameter p in elem.Parameters)
                    {
                        if (p.Definition == null) continue;
                        string val = p.AsValueString() ?? p.AsString() ?? "";
                        if (string.IsNullOrEmpty(val))
                        {
                            if (p.StorageType == StorageType.Double) val = Math.Round(p.AsDouble(), 4).ToString();
                            else if (p.StorageType == StorageType.Integer) val = p.AsInteger().ToString();
                            else if (p.StorageType == StorageType.ElementId) val = p.AsElementId().Value.ToString();
                        }

                        instParams.Add(new JObject
                        {
                            ["name"] = p.Definition.Name,
                            ["value"] = val,
                            ["storage_type"] = p.StorageType.ToString(),
                            ["is_read_only"] = p.IsReadOnly,
                            ["is_shared"] = p.IsShared
                        });
                    }
                    result["instance_parameters"] = instParams;

                    ElementId typeId = elem.GetTypeId();
                    if (typeId != ElementId.InvalidElementId && doc.GetElement(typeId) is ElementType elemType)
                    {
                        result["type_id"] = elemType.Id.Value;
                        result["type_name"] = elemType.Name;
                        result["family_name"] = elemType.FamilyName ?? "";

                        JArray typeParams = new JArray();
                        foreach (Parameter tp in elemType.Parameters)
                        {
                            if (tp.Definition == null) continue;
                            string val = tp.AsValueString() ?? tp.AsString() ?? "";
                            if (string.IsNullOrEmpty(val))
                            {
                                if (tp.StorageType == StorageType.Double) val = Math.Round(tp.AsDouble(), 4).ToString();
                                else if (tp.StorageType == StorageType.Integer) val = tp.AsInteger().ToString();
                                else if (tp.StorageType == StorageType.ElementId) val = tp.AsElementId().Value.ToString();
                            }

                            typeParams.Add(new JObject
                            {
                                ["name"] = tp.Definition.Name,
                                ["value"] = val,
                                ["storage_type"] = tp.StorageType.ToString(),
                                ["is_read_only"] = tp.IsReadOnly
                            });
                        }
                        result["type_parameters"] = typeParams;
                    }

                    break;
                }

                case "revit_manage_views_and_viewports":
                {
                    string action = args["action"]?.ToString()?.ToLowerInvariant() ?? "list";

                    if (action == "list")
                    {
                        string viewTypeFilter = args["view_type"]?.ToString() ?? "";
                        var views = new FilteredElementCollector(doc)
                            .OfClass(typeof(Autodesk.Revit.DB.View))
                            .Cast<Autodesk.Revit.DB.View>()
                            .Where(v => !v.IsTemplate && v.ViewType != ViewType.DrawingSheet && v.ViewType != ViewType.ProjectBrowser && v.ViewType != ViewType.SystemBrowser)
                            .ToList();

                        JArray viewsArr = new JArray();
                        foreach (var v in views)
                        {
                            if (!string.IsNullOrEmpty(viewTypeFilter) && !v.ViewType.ToString().Contains(viewTypeFilter, StringComparison.OrdinalIgnoreCase))
                                continue;

                            viewsArr.Add(new JObject
                            {
                                ["id"] = v.Id.Value,
                                ["name"] = v.Name,
                                ["view_type"] = v.ViewType.ToString(),
                                ["scale"] = v.Scale,
                                ["can_be_added_to_sheet"] = Viewport.CanAddViewToSheet(doc, ElementId.InvalidElementId, v.Id)
                            });
                        }

                        result["status"] = "success";
                        result["total_views"] = viewsArr.Count;
                        result["views"] = viewsArr;
                        break;
                    }

                    if (action == "duplicate")
                    {
                        long viewId = args["view_id"]?.Value<long>() ?? 0;
                        string newName = args["new_name"]?.ToString() ?? "";
                        string dupOption = args["duplicate_option"]?.ToString()?.ToLowerInvariant() ?? "simple";

                        if (doc.GetElement(new ElementId(viewId)) is not Autodesk.Revit.DB.View srcView)
                        {
                            result["status"] = "error";
                            result["error"] = $"No se encontró la vista con ID {viewId}.";
                            break;
                        }

                        ViewDuplicateOption opt = dupOption switch
                        {
                            "with_detailing" or "detailing" => ViewDuplicateOption.WithDetailing,
                            "dependent" => ViewDuplicateOption.AsDependent,
                            _ => ViewDuplicateOption.Duplicate
                        };

                        using (Transaction tx = new Transaction(doc, "MCP: Duplicar Vista"))
                        {
                            tx.Start();
                            ElementId newVId = srcView.Duplicate(opt);
                            var newView = doc.GetElement(newVId) as Autodesk.Revit.DB.View;
                            if (!string.IsNullOrWhiteSpace(newName) && newView != null)
                            {
                                try { newView.Name = newName; } catch { }
                            }
                            tx.Commit();

                            result["status"] = "success";
                            result["duplicated_view_id"] = newVId.Value;
                            result["name"] = newView?.Name ?? "";
                        }
                        break;
                    }

                    if (action == "add_to_sheet")
                    {
                        long sheetId = args["sheet_id"]?.Value<long>() ?? 0;
                        long viewId = args["view_id"]?.Value<long>() ?? 0;
                        double centerXM = args["center_x_m"]?.Value<double>() ?? 0.42;
                        double centerYM = args["center_y_m"]?.Value<double>() ?? 0.297;

                        ElementId sEid = new ElementId(sheetId);
                        ElementId vEid = new ElementId(viewId);

                        if (doc.GetElement(sEid) is not ViewSheet sheet)
                        {
                            result["status"] = "error";
                            result["error"] = $"No se encontró el plano (Sheet) con ID {sheetId}.";
                            break;
                        }

                        if (doc.GetElement(vEid) is not Autodesk.Revit.DB.View view)
                        {
                            result["status"] = "error";
                            result["error"] = $"No se encontró la vista con ID {viewId}.";
                            break;
                        }

                        if (!Viewport.CanAddViewToSheet(doc, sEid, vEid))
                        {
                            result["status"] = "error";
                            result["error"] = $"La vista '{view.Name}' no se puede añadir al plano '{sheet.SheetNumber}' (posiblemente ya está colocada en otro plano).";
                            break;
                        }

                        using (Transaction tx = new Transaction(doc, "MCP: Colocar Vista en Plano"))
                        {
                            tx.Start();
                            XYZ centerPt = new XYZ(centerXM * 3.28084, centerYM * 3.28084, 0);
                            Viewport vp = Viewport.Create(doc, sEid, vEid, centerPt);
                            tx.Commit();

                            result["status"] = "success";
                            result["viewport_id"] = vp.Id.Value;
                            result["sheet_number"] = sheet.SheetNumber;
                            result["view_name"] = view.Name;
                        }
                        break;
                    }

                    result["status"] = "error";
                    result["error"] = $"Acción '{action}' no soportada en revit_manage_views_and_viewports.";
                    break;
                }

                case "revit_delete_or_change_type":
                {
                    string action = args["action"]?.ToString()?.ToLowerInvariant() ?? "delete";
                    var idsArray = args["element_ids"] as JArray;

                    if (idsArray == null || idsArray.Count == 0)
                    {
                        result["status"] = "error";
                        result["error"] = "Se requiere un arreglo 'element_ids' con al menos un ID.";
                        break;
                    }

                    List<ElementId> targetIds = idsArray.Select(t => new ElementId(t.Value<long>())).ToList();

                    if (action == "delete")
                    {
                        using (Transaction tx = new Transaction(doc, "MCP: Eliminar Elementos"))
                        {
                            tx.Start();
                            var deletedIds = doc.Delete(targetIds);
                            tx.Commit();

                            result["status"] = "success";
                            result["deleted_count"] = deletedIds.Count;
                            result["deleted_ids"] = new JArray(deletedIds.Select(x => x.Value));
                        }
                        break;
                    }

                    if (action == "change_type")
                    {
                        long newTypeIdLong = args["new_type_id"]?.Value<long>() ?? 0;
                        string newTypeName = args["new_type_name"]?.ToString() ?? "";

                        ElementId targetTypeId = ElementId.InvalidElementId;
                        if (newTypeIdLong > 0)
                        {
                            targetTypeId = new ElementId(newTypeIdLong);
                        }
                        else if (!string.IsNullOrEmpty(newTypeName))
                        {
                            var foundType = new FilteredElementCollector(doc)
                                .WhereElementIsElementType()
                                .Cast<ElementType>()
                                .FirstOrDefault(t => t.Name.Equals(newTypeName, StringComparison.OrdinalIgnoreCase));
                            if (foundType != null) targetTypeId = foundType.Id;
                        }

                        if (targetTypeId == ElementId.InvalidElementId || doc.GetElement(targetTypeId) is not ElementType)
                        {
                            result["status"] = "error";
                            result["error"] = $"No se encontró un tipo válido con ID {newTypeIdLong} o nombre '{newTypeName}'.";
                            break;
                        }

                        int changedCount = 0;
                        using (Transaction tx = new Transaction(doc, "MCP: Cambiar Tipo de Elementos"))
                        {
                            tx.Start();
                            foreach (var eid in targetIds)
                            {
                                var elem = doc.GetElement(eid);
                                if (elem != null && elem.IsValidType(targetTypeId))
                                {
                                    elem.ChangeTypeId(targetTypeId);
                                    changedCount++;
                                }
                            }
                            tx.Commit();
                        }

                        result["status"] = "success";
                        result["changed_count"] = changedCount;
                        result["target_type_id"] = targetTypeId.Value;
                        break;
                    }

                    result["status"] = "error";
                    result["error"] = $"Acción '{action}' no soportada en revit_delete_or_change_type.";
                    break;
                }

                case "revit_apply_template_config":
                {
                    string configJson = args["config_json"]?.ToString() ?? "";
                    if (string.IsNullOrWhiteSpace(configJson))
                    {
                        result["status"] = "error";
                        result["error"] = "Debe proporcionar 'config_json' con la estructura de PlantillaJsonConfig.";
                        break;
                    }

                    ApplicatorResult appRes = PlantillaApplicatorService.ApplyJson(doc, configJson);
                    result["status"] = appRes.Success ? "success" : "error";
                    result["summary"] = appRes.Summary;
                    result["changes_log"] = new JArray(appRes.Log);
                    result["errors"] = new JArray(appRes.Errors);
                    break;
                }

                case "revit_convertir_pisos_3d":
                case "revit_convertir_pisos":
                {
                    long floorIdLong = args["floor_id"]?.Value<long>() ?? 0;
                    string materialName = args["material_baldosa"]?.ToString() ?? "Porcelanato 60x60";
                    double lenCm = args["longitud_cm"]?.Value<double>() ?? 60.0;
                    double widthCm = args["ancho_cm"]?.Value<double>() ?? 60.0;
                    double thickCm = args["espesor_cm"]?.Value<double>() ?? 1.0;
                    double jointMm = args["junta_mm"]?.Value<double>() ?? 3.0;
                    string patternStr = args["patron"]?.ToString() ?? "OrthogonalGrid";

                    Floor? targetFloor = floorIdLong > 0
                        ? doc.GetElement(new ElementId(floorIdLong)) as Floor
                        : uidoc.Selection.GetElementIds().Select(id => doc.GetElement(id)).OfType<Floor>().FirstOrDefault();

                    if (targetFloor == null)
                    {
                        result["status"] = "error";
                        result["error"] = "No se encontró un elemento Floor válido. Pase un 'floor_id' o seleccione un piso en Revit.";
                        break;
                    }

                    PlanarFace? topFace = FloorTilingService.GetTopPlanarFace(targetFloor);
                    if (topFace == null)
                    {
                        result["status"] = "error";
                        result["error"] = $"No se pudo extraer la cara superior planar del piso {targetFloor.Id.Value}.";
                        break;
                    }

                    Enum.TryParse(patternStr, true, out FloorPatternType patternType);
                    var genConfig = new TileGenerationConfig
                    {
                        TileLengthFt = (lenCm / 100.0) * 3.28084,
                        TileWidthFt = (widthCm / 100.0) * 3.28084,
                        TileThicknessFt = (thickCm / 100.0) * 3.28084,
                        JointThicknessFt = (jointMm / 1000.0) * 3.28084,
                        Pattern = patternType
                    };

                    double areaM2 = (targetFloor.get_Parameter(BuiltInParameter.HOST_AREA_COMPUTED)?.AsDouble() ?? 0) * 0.092903;

                    using (Transaction t = new Transaction(doc, "MCP: Despiece 3D de Pisos y Baldosas"))
                    {
                        t.Start();
                        MeasurementManager.EnsureMeasurementParameters(doc);
                        MeasurementManager.MeasureAndApply(targetFloor, customAreaSqFt: areaM2 / 0.092903);
                        Parameter? hostComm = targetFloor.get_Parameter(BuiltInParameter.ALL_MODEL_INSTANCE_COMMENTS);
                        if (hostComm != null && !hostComm.IsReadOnly)
                        {
                            hostComm.Set($"ENCHAPE-BALDOSA | {materialName} ({lenCm}x{widthCm}cm)");
                        }
                        t.Commit();
                    }

                    List<ElementId> directShapeIds = new List<ElementId>();
                    try
                    {
                        var placementConfig = new TilePlacementConfig
                        {
                            CommentsPrefix = $"ENCHAPE-{materialName.Replace(" ", "_")}"
                        };
                        var matElem = new FilteredElementCollector(doc)
                            .OfClass(typeof(Material))
                            .Cast<Material>()
                            .FirstOrDefault(m => m.Name.IndexOf(materialName, StringComparison.OrdinalIgnoreCase) >= 0);
                        if (matElem != null)
                        {
                            placementConfig.MaterialId = matElem.Id;
                        }
                        directShapeIds = FloorTilingService.ExecuteFloorTiling(targetFloor, genConfig, placementConfig);
                    }
                    catch (Exception ex)
                    {
                        result["warning_3d"] = "Aviso generando geometría 3D: " + ex.Message;
                    }

                    double tileAreaM2 = (lenCm / 100.0) * (widthCm / 100.0);
                    int estimatedTiles = tileAreaM2 > 0 ? (int)Math.Ceiling(areaM2 / tileAreaM2 * 1.05) : 0;

                    result["status"] = "success";
                    result["floor_id"] = targetFloor.Id.Value;
                    result["material"] = materialName;
                    result["area_host_m2"] = Math.Round(areaM2, 2);
                    result["piezas_estimadas_con_desperdicio"] = estimatedTiles;
                    result["directshapes_creados"] = directShapeIds.Count;
                    break;
                }

                case "revit_crear_tablas_computos":
                {
                    string especialidad = args["especialidad"]?.ToString()?.ToLowerInvariant() ?? "pisos";

                    using (Transaction txEnsure = new Transaction(doc, "MCP: Asegurar Parámetros de Medición"))
                    {
                        txEnsure.Start();
                        MeasurementManager.EnsureMeasurementParameters(doc);
                        txEnsure.Commit();
                    }

                    var schedulesConfig = new List<PlantillaSchedule>();

                    if (especialidad == "pisos" || especialidad == "todos")
                    {
                        schedulesConfig.Add(new PlantillaSchedule
                        {
                            Name = "CÓMPUTO BIM - ENCHAPES Y PISOS (MCP)",
                            Category = "Floors",
                            Fields = new List<string> { "Family and Type", "Comments", "Medida_Area", "Medida_Volumen", "Medida_Espesor", "Medida_Unidades" }
                        });
                    }

                    if (especialidad == "muros" || especialidad == "albanileria" || especialidad == "todos")
                    {
                        schedulesConfig.Add(new PlantillaSchedule
                        {
                            Name = "CÓMPUTO BIM - ALBAÑILERÍA Y MUROS (MCP)",
                            Category = "Walls",
                            Fields = new List<string> { "Family and Type", "Comments", "Medida_Area", "Medida_Volumen", "Medida_Longitud", "Medida_Unidades" }
                        });
                    }

                    if (especialidad == "steeldeck" || especialidad == "todos")
                    {
                        schedulesConfig.Add(new PlantillaSchedule
                        {
                            Name = "CÓMPUTO BIM - STEEL DECK Y CONECTORES (MCP)",
                            Category = "Structural Framing",
                            Fields = new List<string> { "Family and Type", "Comments", "Medida_Longitud", "Medida_Volumen", "Medida_Unidades" }
                        });
                    }

                    var configObj = new PlantillaJsonConfig
                    {
                        SchedulesToCreate = schedulesConfig
                    };

                    var appRes = PlantillaApplicatorService.ApplyJson(doc, JsonConvert.SerializeObject(configObj));
                    result["status"] = appRes.Success ? "success" : "error";
                    result["schedules_created"] = new JArray(appRes.Log);
                    result["errors"] = new JArray(appRes.Errors);
                    break;
                }

                case "revit_sync_to_supabase":
                {
                    bool include3D = args["include_3d_scene"]?.Value<bool>() ?? false;

                    var package = MetadataExporterService.CollectMetadata(
                        doc,
                        includeElementDetails: true,
                        includeProjectInfo: true,
                        includeSiteLocation: true,
                        includeLevels: true,
                        includeCategorySummary: true,
                        includeRooms: true,
                        includeSchedules: true,
                        includeSheetsAndViews: true,
                        includeMaterials: true,
                        includeWeb3dModel: include3D,
                        progressCallback: null,
                        isInterruptedCheck: null
                    );

                    bool ok = MetadataExporterService.SendToSupabaseDirect(package, out string responseMsg);
                    result["status"] = ok ? "success" : "error";
                    result["message"] = responseMsg;
                    result["project_name"] = package.ProjectName;
                    result["elementos_sincronizados"] = package.ElementsById?.Count ?? 0;
                    result["schedules_sincronizados"] = package.Schedules?.Count ?? 0;
                    result["rooms_sincronizados"] = package.Rooms?.Count ?? 0;
                    break;
                }

                default:
                    result["status"] = "error";
                    result["error"] = $"Herramienta '{toolName}' no reconocida en McpExternalEventHandler.";
                    break;
            }

            return result;
        }

        private static JObject SerializeElementSummary(Document doc, Element elem)
        {
            JObject eObj = new JObject
            {
                ["id"] = elem.Id.Value,
                ["name"] = elem.Name,
                ["category"] = elem.Category?.Name ?? "Desconocida",
                ["level"] = GetElementLevelName(doc, elem)
            };

            if (elem is ViewSheet sheet)
            {
                eObj["sheet_number"] = sheet.SheetNumber;
                eObj["sheet_name"] = sheet.Name;
                eObj["is_placeholder"] = sheet.IsPlaceholder;
                var tbInst = new FilteredElementCollector(doc, sheet.Id)
                    .OfCategory(BuiltInCategory.OST_TitleBlocks)
                    .FirstElement();
                if (tbInst != null)
                {
                    eObj["title_block_name"] = tbInst.Name;
                    eObj["title_block_type_id"] = tbInst.GetTypeId().Value;
                }
            }
            else if (elem is Autodesk.Revit.DB.View view)
            {
                eObj["view_type"] = view.ViewType.ToString();
                eObj["is_template"] = view.IsTemplate;
            }

            if (doc.GetElement(elem.GetTypeId()) is ElementType elemType)
            {
                eObj["family"] = elemType.FamilyName ?? "";
                eObj["type"] = elemType.Name ?? "";
            }

            string comm = elem.get_Parameter(BuiltInParameter.ALL_MODEL_INSTANCE_COMMENTS)?.AsString() ?? "";
            if (!string.IsNullOrEmpty(comm)) eObj["comments"] = comm;

            Parameter? pArea = elem.LookupParameter(MeasurementManager.ParamArea) ?? elem.get_Parameter(BuiltInParameter.HOST_AREA_COMPUTED);
            if (pArea != null && pArea.HasValue && pArea.StorageType == StorageType.Double)
            {
                eObj["area_m2"] = Math.Round(pArea.AsDouble() * 0.092903, 2);
            }

            Parameter? pVol = elem.LookupParameter(MeasurementManager.ParamVolumen) ?? elem.get_Parameter(BuiltInParameter.HOST_VOLUME_COMPUTED);
            if (pVol != null && pVol.HasValue && pVol.StorageType == StorageType.Double)
            {
                eObj["volumen_m3"] = Math.Round(pVol.AsDouble() * 0.0283168, 3);
            }

            return eObj;
        }

        private static string GetElementLevelName(Document doc, Element elem)
        {
            if (elem.LevelId != ElementId.InvalidElementId && doc.GetElement(elem.LevelId) is Level lvl) return lvl.Name;

            Parameter? pLvl = elem.get_Parameter(BuiltInParameter.SCHEDULE_LEVEL_PARAM) ?? elem.get_Parameter(BuiltInParameter.FAMILY_LEVEL_PARAM);
            if (pLvl != null && pLvl.StorageType == StorageType.ElementId && doc.GetElement(pLvl.AsElementId()) is Level l2) return l2.Name;

            return "";
        }

        private static BuiltInCategory? ResolveBuiltInCategory(string categoryName)
        {
            return categoryName.Trim().ToLowerInvariant() switch
            {
                "floors" or "suelos" or "pisos" => BuiltInCategory.OST_Floors,
                "walls" or "muros" => BuiltInCategory.OST_Walls,
                "rooms" or "habitaciones" or "espacios" => BuiltInCategory.OST_Rooms,
                "doors" or "puertas" => BuiltInCategory.OST_Doors,
                "windows" or "ventanas" => BuiltInCategory.OST_Windows,
                "ceilings" or "cielorrasos" => BuiltInCategory.OST_Ceilings,
                "roofs" or "cubiertas" => BuiltInCategory.OST_Roofs,
                "structuralcolumns" or "structural columns" or "columnas" => BuiltInCategory.OST_StructuralColumns,
                "structuralframing" or "structural framing" or "vigas" or "steeldeck" => BuiltInCategory.OST_StructuralFraming,
                "genericmodel" or "generic models" or "modelos genericos" => BuiltInCategory.OST_GenericModel,
                "plumbingfixtures" or "aparatos sanitarios" => BuiltInCategory.OST_PlumbingFixtures,
                "pipecurves" or "tuberias" => BuiltInCategory.OST_PipeCurves,
                "sheets" or "planos" or "hojas" => BuiltInCategory.OST_Sheets,
                "views" or "vistas" => BuiltInCategory.OST_Views,
                "titleblocks" or "title blocks" or "rotulos" => BuiltInCategory.OST_TitleBlocks,
                _ => null
            };
        }

        public string GetName() => "Review_McpExternalEventHandler";
    }
}
