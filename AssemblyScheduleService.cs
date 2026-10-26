using System;
using System.Collections.Generic;
using System.Linq;
using System.Text.RegularExpressions;
using Autodesk.Revit.DB;
using Autodesk.Revit.UI;

namespace RevitToSupabasePlugin
{
    public class AssemblyScheduleResult
    {
        public bool Success { get; set; }
        public string AssemblyName { get; set; } = string.Empty;
        public string ScheduleName { get; set; } = string.Empty;
        public string Message { get; set; } = string.Empty;
        public bool WasCreated { get; set; }
        public int FieldsReplicatedCount { get; set; }
    }

    public class AssemblyItemViewModel
    {
        public AssemblyInstance Assembly { get; set; } = null!;
        public ElementId Id { get; set; } = ElementId.InvalidElementId;
        public string Name { get; set; } = string.Empty;
        public string TypeName { get; set; } = string.Empty;
        public int MemberCount { get; set; }
        public string CategoriesSummary { get; set; } = string.Empty;
        public bool AllowsViewCreation { get; set; }
        public bool HasExistingSchedule { get; set; }
        public string ExistingScheduleInfo { get; set; } = string.Empty;
        public bool IsSelected { get; set; } = true;

        public bool IsValid => Assembly != null && Assembly.IsValidObject;
        public string StatusBadge => HasExistingSchedule ? "✅ Con Tabla" : (AllowsViewCreation ? "⚡ Listo" : "⚪ Tipo ya creado");
        public string StatusColor => HasExistingSchedule ? "#059669" : (AllowsViewCreation ? "#0F4369" : "#72777F");
    }

    public class ScheduleListItemViewModel
    {
        public ViewSchedule Schedule { get; set; } = null!;
        public ElementId Id { get; set; } = ElementId.InvalidElementId;
        public string Name { get; set; } = string.Empty;
        public bool IsAssemblyView { get; set; }
        public string AssociatedAssemblyName { get; set; } = string.Empty;
        public string CategoryName { get; set; } = string.Empty;
        public string ScheduleTypeDescription { get; set; } = string.Empty;
        public int FieldCount { get; set; }
        public bool IsSelectedAsTemplate { get; set; }

        public bool IsValid => Schedule != null && Schedule.IsValidObject;
        public string ScopeBadge => IsAssemblyView ? "📦 Ensamblaje" : "🏢 Proyecto";
        public string ScopeBadgeColor => IsAssemblyView ? "#059669" : "#0F4369";
        public string DisplaySubtitle => $"{ScheduleTypeDescription} • {CategoryName} • {FieldCount} campos";

        // Gestión y ubicación en láminas
        public bool IsChecked { get; set; }
        public bool IsOnSheet { get; set; }
        public string SheetPlacementsInfo { get; set; } = string.Empty;
        public string SheetBadge => IsOnSheet ? $"📄 Plano: {SheetPlacementsInfo}" : string.Empty;
        public bool HasSheetBadge => IsOnSheet;
    }

    public class FieldSnapshot
    {
        public ScheduleFieldId SourceFieldId { get; set; } = null!;
        public ScheduleFieldType FieldType { get; set; }
        public ElementId ParameterId { get; set; } = ElementId.InvalidElementId;
        public string Name { get; set; } = string.Empty;
        public string ColumnHeading { get; set; } = string.Empty;
        public bool IsHidden { get; set; }
        public ScheduleHeadingOrientation HeadingOrientation { get; set; }
        public ScheduleHorizontalAlignment HorizontalAlignment { get; set; }
        public ScheduleFieldDisplayType DisplayType { get; set; }
        public double GridColumnWidth { get; set; }
        public FormatOptions? FormatOptions { get; set; }
    }

    public class SortSnapshot
    {
        public ScheduleFieldId SourceFieldId { get; set; } = null!;
        public ScheduleSortOrder SortOrder { get; set; }
        public bool ShowHeader { get; set; }
        public bool ShowFooter { get; set; }
        public bool ShowBlankLine { get; set; }
    }

    public class FilterSnapshot
    {
        public ScheduleFieldId SourceFieldId { get; set; } = null!;
        public ScheduleFilterType FilterType { get; set; }
        public string? StringValue { get; set; }
        public double? DoubleValue { get; set; }
        public int? IntegerValue { get; set; }
        public ElementId? ElementIdValue { get; set; }
    }

    public class ScheduleSnapshot
    {
        public string Name { get; set; } = string.Empty;
        public ElementId CategoryId { get; set; } = ElementId.InvalidElementId;
        public bool IsMaterialTakeoff { get; set; }
        public bool IsItemized { get; set; }
        public bool ShowGrandTotal { get; set; }
        public bool ShowGrandTotalTitle { get; set; }
        public bool ShowGrandTotalCount { get; set; }
        public string GrandTotalTitle { get; set; } = string.Empty;
        public bool ShowTitle { get; set; }
        public bool ShowHeaders { get; set; }
        public ElementId ViewTemplateId { get; set; } = ElementId.InvalidElementId;
        public List<FieldSnapshot> Fields { get; set; } = new List<FieldSnapshot>();
        public List<SortSnapshot> Sorts { get; set; } = new List<SortSnapshot>();
        public List<FilterSnapshot> Filters { get; set; } = new List<FilterSnapshot>();
    }

    public static class AssemblyScheduleService
    {
        /// <summary>
        /// Obtiene todas las tablas de planificación del proyecto clasificadas para la UI.
        /// Filtra de forma estricta tablas internas de revisiones, plantillas y cuadros de rotulación.
        /// </summary>
        public static List<ScheduleListItemViewModel> GetSchedulesList(Document doc)
        {
            var result = new List<ScheduleListItemViewModel>();

            // Mapeo de tablas colocadas en Láminas/Planos (Sheets)
            var sheetMap = new Dictionary<ElementId, List<string>>();
            try
            {
                var instances = new FilteredElementCollector(doc)
                    .OfClass(typeof(ScheduleSheetInstance))
                    .Cast<ScheduleSheetInstance>()
                    .ToList();

                foreach (var ssi in instances)
                {
                    if (ssi.ScheduleId == ElementId.InvalidElementId) continue;

                    string sheetNumber = "";
                    if (doc.GetElement(ssi.OwnerViewId) is ViewSheet vsheet)
                    {
                        sheetNumber = vsheet.SheetNumber;
                    }

                    if (!sheetMap.ContainsKey(ssi.ScheduleId))
                    {
                        sheetMap[ssi.ScheduleId] = new List<string>();
                    }

                    if (!string.IsNullOrEmpty(sheetNumber) && !sheetMap[ssi.ScheduleId].Contains(sheetNumber))
                    {
                        sheetMap[ssi.ScheduleId].Add(sheetNumber);
                    }
                }
            }
            catch { }

            var allSchedules = new FilteredElementCollector(doc)
                .OfClass(typeof(ViewSchedule))
                .Cast<ViewSchedule>()
                .Where(vs => !IsInternalOrRevisionSchedule(vs))
                .OrderBy(vs => vs.IsAssemblyView ? 1 : 0)
                .ThenBy(vs => vs.Name)
                .ToList();

            var assemblyDict = new FilteredElementCollector(doc)
                .OfClass(typeof(AssemblyInstance))
                .Cast<AssemblyInstance>()
                .ToDictionary(a => a.Id, a => a.AssemblyTypeName ?? a.Name);

            foreach (var vs in allSchedules)
            {
                ScheduleDefinition def = vs.Definition;
                string catName = "Multi-categoría";
                if (def.CategoryId != ElementId.InvalidElementId)
                {
                    Category? cat = Category.GetCategory(doc, def.CategoryId);
                    catName = cat?.Name ?? $"Categoría ID: {def.CategoryId}";
                }

                string kindDesc = "Planificación";
                if (def.IsMaterialTakeoff)
                {
                    kindDesc = "Cómputo de Materiales";
                }
                else if (vs.IsAssemblyView && def.CategoryId == ElementId.InvalidElementId)
                {
                    kindDesc = "Lista de Piezas (Part List)";
                }

                string assocAssName = "";
                if (vs.IsAssemblyView && vs.AssociatedAssemblyInstanceId != ElementId.InvalidElementId)
                {
                    assemblyDict.TryGetValue(vs.AssociatedAssemblyInstanceId, out assocAssName!);
                    if (string.IsNullOrEmpty(assocAssName))
                    {
                        assocAssName = $"Ensamblaje ID {vs.AssociatedAssemblyInstanceId}";
                    }
                }

                bool isOnSheet = sheetMap.TryGetValue(vs.Id, out var sheetNums) && sheetNums != null && sheetNums.Count > 0;
                string sheetInfo = isOnSheet ? string.Join(", ", sheetNums!) : "";

                result.Add(new ScheduleListItemViewModel
                {
                    Schedule = vs,
                    Id = vs.Id,
                    Name = vs.Name,
                    IsAssemblyView = vs.IsAssemblyView,
                    AssociatedAssemblyName = assocAssName,
                    CategoryName = catName,
                    ScheduleTypeDescription = kindDesc,
                    FieldCount = def.GetFieldOrder().Count,
                    IsOnSheet = isOnSheet,
                    SheetPlacementsInfo = sheetInfo
                });
            }

            return result;
        }

        /// <summary>
        /// Determina si una tabla es una tabla interna de revisión o de cartela de plano que Revit oculta en el navegador de proyectos.
        /// </summary>
        public static bool IsInternalOrRevisionSchedule(ViewSchedule vs)
        {
            if (vs.IsTemplate || vs.IsInternalKeynoteSchedule) return true;

            // 1. Detección por propiedad nativa de la API de Revit para tablas de revisión de cartelas
            try
            {
                if (vs.IsTitleblockRevisionSchedule) return true;
            }
            catch { }

            // 2. Detección por categoría interna de Revisiones (OST_Revisions = -2006070)
            try
            {
                var catId = vs.Definition?.CategoryId;
                if (catId != null && catId != ElementId.InvalidElementId)
                {
                    long val = catId.Value;
                    if (val == (long)BuiltInCategory.OST_Revisions ||
                        val == -2006070)
                    {
                        return true;
                    }
                }
            }
            catch { }

            // 3. Detección por patrón de nombres del sistema
            string name = vs.Name?.Trim() ?? "";
            if (name.StartsWith("<") && name.EndsWith(">")) return true;
            if (name.IndexOf("Revision Schedule", StringComparison.OrdinalIgnoreCase) >= 0 && name.StartsWith("<")) return true;
            if (name.IndexOf("Tabla de revisiones", StringComparison.OrdinalIgnoreCase) >= 0 && name.StartsWith("<")) return true;

            return false;
        }

        /// <summary>
        /// Obtiene la lista enriquecida de ensamblajes para la interfaz de usuario.
        /// </summary>
        public static List<AssemblyItemViewModel> GetAssemblyViewModels(Document doc)
        {
            var assemblies = GetAssemblies(doc);
            var result = new List<AssemblyItemViewModel>();

            var allAssemblySchedules = new FilteredElementCollector(doc)
                .OfClass(typeof(ViewSchedule))
                .Cast<ViewSchedule>()
                .Where(vs => vs.IsAssemblyView && vs.AssociatedAssemblyInstanceId != ElementId.InvalidElementId)
                .GroupBy(vs => vs.AssociatedAssemblyInstanceId)
                .ToDictionary(g => g.Key, g => g.ToList());

            foreach (var a in assemblies)
            {
                var memberIds = a.GetMemberIds();
                var catNames = memberIds
                    .Select(id => doc.GetElement(id))
                    .Where(e => e != null && e.Category != null)
                    .Select(e => e.Category.Name)
                    .Distinct()
                    .OrderBy(n => n)
                    .ToList();

                allAssemblySchedules.TryGetValue(a.Id, out var schedules);
                bool hasSched = schedules != null && schedules.Count > 0;
                string schedInfo = hasSched 
                    ? $"{schedules!.Count} tabla(s): {string.Join(", ", schedules.Select(s => s.Name))}"
                    : "Sin tabla asignada";

                result.Add(new AssemblyItemViewModel
                {
                    Assembly = a,
                    Id = a.Id,
                    Name = a.Name,
                    TypeName = a.AssemblyTypeName ?? a.Name,
                    MemberCount = memberIds.Count,
                    CategoriesSummary = catNames.Count > 0 ? string.Join(", ", catNames) : "Varios",
                    AllowsViewCreation = a.AllowsAssemblyViewCreation(),
                    HasExistingSchedule = hasSched,
                    ExistingScheduleInfo = schedInfo,
                    IsSelected = a.AllowsAssemblyViewCreation() || hasSched
                });
            }

            return result;
        }

        /// <summary>
        /// Obtiene todos los ensamblajes (AssemblyInstance) del proyecto.
        /// </summary>
        public static List<AssemblyInstance> GetAssemblies(Document doc)
        {
            return new FilteredElementCollector(doc)
                .OfClass(typeof(AssemblyInstance))
                .Cast<AssemblyInstance>()
                .OrderBy(a => a.AssemblyTypeName ?? a.Name)
                .ToList();
        }

        /// <summary>
        /// Obtiene todas las tablas de planificación asociadas a un ensamblaje específico.
        /// </summary>
        public static List<ViewSchedule> GetAssemblySchedules(Document doc, ElementId assemblyId)
        {
            return new FilteredElementCollector(doc)
                .OfClass(typeof(ViewSchedule))
                .Cast<ViewSchedule>()
                .Where(vs => vs.IsAssemblyView && vs.AssociatedAssemblyInstanceId == assemblyId)
                .ToList();
        }

        /// <summary>
        /// Toma una copia instantánea en memoria (read-only) de una tabla para evitar modificarla o corromperla.
        /// </summary>
        public static ScheduleSnapshot TakeSnapshot(Document doc, ViewSchedule schedule)
        {
            ScheduleDefinition def = schedule.Definition;
            var snapshot = new ScheduleSnapshot
            {
                Name = schedule.Name,
                CategoryId = def.CategoryId,
                IsMaterialTakeoff = def.IsMaterialTakeoff,
                IsItemized = def.IsItemized,
                ShowGrandTotal = def.ShowGrandTotal,
                ShowTitle = def.ShowTitle,
                ShowHeaders = def.ShowHeaders,
                ViewTemplateId = schedule.ViewTemplateId
            };

            if (def.ShowGrandTotal)
            {
                try { snapshot.ShowGrandTotalTitle = def.ShowGrandTotalTitle; } catch { }
                try { snapshot.ShowGrandTotalCount = def.ShowGrandTotalCount; } catch { }
                try { snapshot.GrandTotalTitle = def.GrandTotalTitle; } catch { }
            }

            // Copiar campos
            foreach (ScheduleFieldId fId in def.GetFieldOrder())
            {
                try
                {
                    ScheduleField sf = def.GetField(fId);
                    var fSnap = new FieldSnapshot
                    {
                        SourceFieldId = fId,
                        FieldType = sf.FieldType,
                        ParameterId = sf.ParameterId,
                        Name = sf.GetName(),
                        ColumnHeading = sf.ColumnHeading,
                        IsHidden = sf.IsHidden,
                        DisplayType = sf.DisplayType
                    };

                    try { fSnap.HeadingOrientation = sf.HeadingOrientation; } catch { }
                    try { fSnap.HorizontalAlignment = sf.HorizontalAlignment; } catch { }
                    try { fSnap.GridColumnWidth = sf.GridColumnWidth; } catch { }
                    try { fSnap.FormatOptions = sf.GetFormatOptions(); } catch { }

                    snapshot.Fields.Add(fSnap);
                }
                catch { }
            }

            // Copiar reglas de ordenación
            try
            {
                foreach (ScheduleSortGroupField sgf in def.GetSortGroupFields())
                {
                    snapshot.Sorts.Add(new SortSnapshot
                    {
                        SourceFieldId = sgf.FieldId,
                        SortOrder = sgf.SortOrder,
                        ShowHeader = sgf.ShowHeader,
                        ShowFooter = sgf.ShowFooter,
                        ShowBlankLine = sgf.ShowBlankLine
                    });
                }
            }
            catch { }

            // Copiar filtros
            try
            {
                foreach (ScheduleFilter filter in def.GetFilters())
                {
                    var fSnap = new FilterSnapshot
                    {
                        SourceFieldId = filter.FieldId,
                        FilterType = filter.FilterType
                    };

                    try { fSnap.StringValue = filter.GetStringValue(); } catch { }
                    try { fSnap.DoubleValue = filter.GetDoubleValue(); } catch { }
                    try { fSnap.IntegerValue = filter.GetIntegerValue(); } catch { }
                    try { fSnap.ElementIdValue = filter.GetElementIdValue(); } catch { }

                    snapshot.Filters.Add(fSnap);
                }
            }
            catch { }

            return snapshot;
        }

        /// <summary>
        /// Encuentra el ejemplar del ensamblaje que puede alojar vistas (Owner primario de vistas del tipo).
        /// </summary>
        public static AssemblyInstance GetEffectiveViewOwnerInstance(Document doc, AssemblyInstance assembly)
        {
            if (assembly.AllowsAssemblyViewCreation()) return assembly;

            var siblings = new FilteredElementCollector(doc)
                .OfClass(typeof(AssemblyInstance))
                .Cast<AssemblyInstance>()
                .Where(a => a.GetTypeId() == assembly.GetTypeId())
                .ToList();

            var valid = siblings.FirstOrDefault(s => s.AllowsAssemblyViewCreation());
            return valid ?? assembly;
        }

        /// <summary>
        /// Sobrecarga compatible para replicación de tabla de ensamblaje.
        /// </summary>
        public static AssemblyScheduleResult CreateOrUpdateAssemblySchedule(
            Document doc,
            AssemblyInstance assembly,
            ViewSchedule sourceSchedule,
            HashSet<string>? allowedFieldNames = null,
            bool replicateFilters = false,
            bool replicateSorting = true,
            bool replicateTemplate = false,
            bool replicateAppearance = true)
        {
            return CreateOrUpdateFromTemplate(
                doc,
                assembly,
                sourceSchedule,
                allowedFieldNames,
                replicateAppearance,
                replicateSorting,
                true,
                replicateFilters,
                replicateTemplate);
        }

        /// <summary>
        /// Crea o actualiza una tabla de ensamblaje usando una plantilla seleccionada (RÉPLICA SEGURA).
        /// La tabla de origen NO se toca ni se modifica bajo ninguna circunstancia.
        /// </summary>
        public static AssemblyScheduleResult CreateOrUpdateFromTemplate(
            Document doc,
            AssemblyInstance assembly,
            ViewSchedule sourceSchedule,
            HashSet<string>? allowedFieldNames = null,
            bool replicateFormat = true,
            bool replicateSorting = true,
            bool replicateTotals = true,
            bool replicateFilters = false,
            bool replicateTemplate = false)
        {
            string assemblyDisplayName = assembly.AssemblyTypeName ?? assembly.Name;
            var result = new AssemblyScheduleResult
            {
                AssemblyName = assemblyDisplayName
            };

            // 1. Tomar Snapshot seguro en memoria de la tabla origen (100% aislada)
            ScheduleSnapshot snapshot = TakeSnapshot(doc, sourceSchedule);

            // 2. Determinar la instancia efectiva para vistas
            AssemblyInstance targetInstance = GetEffectiveViewOwnerInstance(doc, assembly);

            // 3. Buscar si ya existe una tabla para este ensamblaje
            var existingSchedules = GetAssemblySchedules(doc, targetInstance.Id);
            ViewSchedule? targetSchedule = null;

            if (snapshot.IsMaterialTakeoff)
            {
                targetSchedule = existingSchedules.FirstOrDefault(vs => vs.Definition.IsMaterialTakeoff);
            }
            else if (snapshot.CategoryId != ElementId.InvalidElementId)
            {
                targetSchedule = existingSchedules.FirstOrDefault(vs => !vs.Definition.IsMaterialTakeoff && vs.Definition.CategoryId == snapshot.CategoryId);
            }
            else
            {
                // Multi-category / Part List
                targetSchedule = existingSchedules.FirstOrDefault(vs => !vs.Definition.IsMaterialTakeoff && vs.Definition.CategoryId == ElementId.InvalidElementId);
            }

            // PROTECCIÓN CRÍTICA: La tabla de destino NUNCA puede ser la tabla origen
            if (targetSchedule != null && targetSchedule.Id == sourceSchedule.Id)
            {
                result.Success = true;
                result.ScheduleName = targetSchedule.Name;
                result.Message = "La tabla origen es la misma tabla del ensamblaje. Se mantuvo intacta para proteger los datos originales.";
                result.FieldsReplicatedCount = snapshot.Fields.Count;
                return result;
            }

            bool wasCreated = false;

            if (targetSchedule == null)
            {
                if (!targetInstance.AllowsAssemblyViewCreation())
                {
                    result.Success = false;
                    result.Message = $"El ensamblaje '{assemblyDisplayName}' no permite crear nuevas vistas automáticas.";
                    return result;
                }

                try
                {
                    if (snapshot.IsMaterialTakeoff)
                    {
                        targetSchedule = AssemblyViewUtils.CreateMaterialTakeoff(doc, targetInstance.Id);
                    }
                    else if (snapshot.CategoryId != ElementId.InvalidElementId)
                    {
                        // Verificar que contenga elementos de esa categoría
                        bool hasCatElements = targetInstance.GetMemberIds()
                            .Select(id => doc.GetElement(id))
                            .Where(e => e != null && e.Category != null)
                            .Any(e => e.Category.Id == snapshot.CategoryId);

                        if (!hasCatElements)
                        {
                            result.Success = false;
                            result.Message = $"El ensamblaje '{assemblyDisplayName}' no contiene elementos de la categoría de la plantilla.";
                            return result;
                        }

                        targetSchedule = AssemblyViewUtils.CreateSingleCategorySchedule(doc, targetInstance.Id, snapshot.CategoryId);
                    }
                    else
                    {
                        targetSchedule = AssemblyViewUtils.CreatePartList(doc, targetInstance.Id);
                    }

                    wasCreated = true;
                    string desiredName = $"{assemblyDisplayName} - {snapshot.Name}";
                    targetSchedule.Name = GetUniqueScheduleName(doc, desiredName);
                }
                catch (Exception ex)
                {
                    result.Success = false;
                    result.Message = $"Error al crear tabla para '{assemblyDisplayName}': {ex.Message}";
                    return result;
                }
            }

            result.WasCreated = wasCreated;
            result.ScheduleName = targetSchedule.Name;

            // 4. Modificar la definición de targetSchedule de forma segura
            try
            {
                // Desvincular plantilla gráfica temporalmente si la tuviera para desbloquear campos
                ElementId prevTemplateId = targetSchedule.ViewTemplateId;
                if (prevTemplateId != ElementId.InvalidElementId)
                {
                    try { targetSchedule.ViewTemplateId = ElementId.InvalidElementId; } catch { }
                }

                ScheduleDefinition targetDef = targetSchedule.Definition;

                // Limpiar reglas de ordenación y filtros existentes en destino
                targetDef.ClearSortGroupFields();
                targetDef.ClearFilters();

                // Limpiar campos existentes del destino
                var currentTargetFields = targetDef.GetFieldOrder().ToList();
                for (int i = currentTargetFields.Count - 1; i >= 0; i--)
                {
                    try { targetDef.RemoveField(currentTargetFields[i]); } catch { }
                }

                // Obtener campos disponibles en el destino
                IList<SchedulableField> availableFields = targetDef.GetSchedulableFields();
                Dictionary<ScheduleFieldId, ScheduleFieldId> fieldMap = new Dictionary<ScheduleFieldId, ScheduleFieldId>();

                int replicatedCount = 0;

                foreach (var fSnap in snapshot.Fields)
                {
                    // Si hay filtro de nombres y no está incluido, omitir
                    if (allowedFieldNames != null && 
                        !allowedFieldNames.Contains(fSnap.Name) && 
                        !allowedFieldNames.Contains(fSnap.ColumnHeading))
                    {
                        continue;
                    }

                    SchedulableField? matchedField = null;

                    // 1. Conteo
                    if (fSnap.FieldType == ScheduleFieldType.Count)
                    {
                        matchedField = availableFields.FirstOrDefault(f => f.FieldType == ScheduleFieldType.Count);
                    }
                    // 2. Parámetro por ID
                    else if (fSnap.ParameterId != ElementId.InvalidElementId)
                    {
                        matchedField = availableFields.FirstOrDefault(f => f.ParameterId == fSnap.ParameterId);
                        if (matchedField == null)
                        {
                            matchedField = availableFields.FirstOrDefault(f => f.GetName(doc).Equals(fSnap.Name, StringComparison.OrdinalIgnoreCase));
                        }
                    }
                    // 3. Fallback por Nombre
                    else
                    {
                        matchedField = availableFields.FirstOrDefault(f => f.GetName(doc).Equals(fSnap.Name, StringComparison.OrdinalIgnoreCase));
                    }

                    if (matchedField != null)
                    {
                        try
                        {
                            ScheduleField tf = targetDef.AddField(matchedField);
                            fieldMap[fSnap.SourceFieldId] = tf.FieldId;

                            if (replicateFormat)
                            {
                                if (!string.IsNullOrEmpty(fSnap.ColumnHeading))
                                {
                                    tf.ColumnHeading = fSnap.ColumnHeading;
                                }

                                tf.IsHidden = fSnap.IsHidden;
                                try { tf.HeadingOrientation = fSnap.HeadingOrientation; } catch { }
                                try { tf.HorizontalAlignment = fSnap.HorizontalAlignment; } catch { }
                                try { tf.DisplayType = fSnap.DisplayType; } catch { }
                                try
                                {
                                    if (fSnap.GridColumnWidth > 0.0001)
                                        tf.GridColumnWidth = fSnap.GridColumnWidth;
                                }
                                catch { }
                                try
                                {
                                    if (fSnap.FormatOptions != null)
                                        tf.SetFormatOptions(fSnap.FormatOptions);
                                }
                                catch { }
                            }

                            replicatedCount++;
                        }
                        catch { }
                    }
                }

                // 5. Replicar Sorting & Grouping
                if (replicateSorting)
                {
                    foreach (var sSnap in snapshot.Sorts)
                    {
                        if (fieldMap.TryGetValue(sSnap.SourceFieldId, out var targetFieldId))
                        {
                            try
                            {
                                var tsf = new ScheduleSortGroupField(targetFieldId, sSnap.SortOrder)
                                {
                                    ShowHeader = sSnap.ShowHeader,
                                    ShowFooter = sSnap.ShowFooter,
                                    ShowBlankLine = sSnap.ShowBlankLine
                                };
                                targetDef.AddSortGroupField(tsf);
                            }
                            catch { }
                        }
                    }
                }

                // 6. Totales y Agrupación
                if (replicateTotals)
                {
                    try { targetDef.IsItemized = snapshot.IsItemized; } catch { }
                    try
                    {
                        targetDef.ShowGrandTotal = snapshot.ShowGrandTotal;
                        if (snapshot.ShowGrandTotal)
                        {
                            try { targetDef.ShowGrandTotalTitle = snapshot.ShowGrandTotalTitle; } catch { }
                            try { targetDef.ShowGrandTotalCount = snapshot.ShowGrandTotalCount; } catch { }
                            try { targetDef.GrandTotalTitle = snapshot.GrandTotalTitle; } catch { }
                        }
                    }
                    catch { }
                }

                try { targetDef.ShowTitle = snapshot.ShowTitle; } catch { }
                try { targetDef.ShowHeaders = snapshot.ShowHeaders; } catch { }

                // 7. Filtros (Opcional, desactivado por defecto para no vaciar la tabla de ensamble)
                if (replicateFilters)
                {
                    foreach (var fltSnap in snapshot.Filters)
                    {
                        if (fieldMap.TryGetValue(fltSnap.SourceFieldId, out var targetFieldId))
                        {
                            try
                            {
                                ScheduleFilter? cloned = BuildFilter(fltSnap, targetFieldId);
                                if (cloned != null) targetDef.AddFilter(cloned);
                            }
                            catch { }
                        }
                    }
                }

                // 8. View Template
                if (replicateTemplate && snapshot.ViewTemplateId != ElementId.InvalidElementId)
                {
                    try { targetSchedule.ViewTemplateId = snapshot.ViewTemplateId; } catch { }
                }

                result.FieldsReplicatedCount = replicatedCount;
                result.Success = true;
                result.Message = wasCreated
                    ? $"Tabla creada y {replicatedCount} campos replicados con formato exacto."
                    : $"Tabla actualizada y {replicatedCount} campos replicados con formato exacto.";
                return result;
            }
            catch (Exception ex)
            {
                result.Success = false;
                result.Message = $"Error al replicar en '{assemblyDisplayName}': {ex.Message}";
                return result;
            }
        }

        /// <summary>
        /// Crea una tabla de planificación estándar y 100% funcional para el ensamblaje SIN necesitar plantilla previa.
        /// </summary>
        public static AssemblyScheduleResult CreateStandardAssemblySchedule(
            Document doc,
            AssemblyInstance assembly,
            bool isMaterialTakeoff = false,
            bool groupIdentical = true)
        {
            string assemblyDisplayName = assembly.AssemblyTypeName ?? assembly.Name;
            var result = new AssemblyScheduleResult
            {
                AssemblyName = assemblyDisplayName
            };

            AssemblyInstance targetInstance = GetEffectiveViewOwnerInstance(doc, assembly);
            var existingSchedules = GetAssemblySchedules(doc, targetInstance.Id);
            ViewSchedule? targetSchedule = null;

            if (isMaterialTakeoff)
            {
                targetSchedule = existingSchedules.FirstOrDefault(s => s.Definition.IsMaterialTakeoff);
            }
            else
            {
                targetSchedule = existingSchedules.FirstOrDefault(s => !s.Definition.IsMaterialTakeoff && s.Definition.CategoryId == ElementId.InvalidElementId);
            }

            bool wasCreated = false;
            if (targetSchedule == null)
            {
                if (!targetInstance.AllowsAssemblyViewCreation())
                {
                    result.Success = false;
                    result.Message = $"El ensamblaje '{assemblyDisplayName}' no permite crear nuevas vistas.";
                    return result;
                }

                try
                {
                    if (isMaterialTakeoff)
                    {
                        targetSchedule = AssemblyViewUtils.CreateMaterialTakeoff(doc, targetInstance.Id);
                    }
                    else
                    {
                        targetSchedule = AssemblyViewUtils.CreatePartList(doc, targetInstance.Id);
                    }

                    wasCreated = true;
                    string baseName = $"{assemblyDisplayName} - {(isMaterialTakeoff ? "Cómputo de Materiales" : "Lista de Piezas")}";
                    targetSchedule.Name = GetUniqueScheduleName(doc, baseName);
                }
                catch (Exception ex)
                {
                    result.Success = false;
                    result.Message = $"Error al crear tabla: {ex.Message}";
                    return result;
                }
            }

            result.WasCreated = wasCreated;
            result.ScheduleName = targetSchedule.Name;

            try
            {
                ScheduleDefinition def = targetSchedule.Definition;
                def.ClearSortGroupFields();
                def.ClearFilters();

                var curFields = def.GetFieldOrder().ToList();
                for (int i = curFields.Count - 1; i >= 0; i--)
                {
                    try { def.RemoveField(curFields[i]); } catch { }
                }

                var schedulable = def.GetSchedulableFields();
                int added = 0;
                ScheduleField? primarySortField = null;

                if (isMaterialTakeoff)
                {
                    // 1. Familia y Tipo
                    var fFam = schedulable.FirstOrDefault(f => f.ParameterId == new ElementId(BuiltInParameter.ELEM_FAMILY_AND_TYPE_PARAM));
                    if (fFam != null)
                    {
                        var f = def.AddField(fFam);
                        f.ColumnHeading = "Familia y Tipo";
                        primarySortField = f;
                        added++;
                    }

                    // 2. Material: Nombre
                    var fMatName = schedulable.FirstOrDefault(f => f.ParameterId == new ElementId(BuiltInParameter.MATERIAL_NAME) || 
                                                                  f.GetName(doc).IndexOf("Material: Name", StringComparison.OrdinalIgnoreCase) >= 0 || 
                                                                  f.GetName(doc).IndexOf("Material: Nombre", StringComparison.OrdinalIgnoreCase) >= 0);
                    if (fMatName != null)
                    {
                        var f = def.AddField(fMatName);
                        f.ColumnHeading = "Material";
                        if (primarySortField == null) primarySortField = f;
                        added++;
                    }

                    // 3. Cantidad / Recuento
                    var fCount = schedulable.FirstOrDefault(f => f.FieldType == ScheduleFieldType.Count);
                    if (fCount != null)
                    {
                        var f = def.AddField(fCount);
                        f.ColumnHeading = "Cantidad";
                        added++;
                    }

                    // 4. Material: Área
                    var fMatArea = schedulable.FirstOrDefault(f => f.ParameterId == new ElementId(BuiltInParameter.MATERIAL_AREA) || 
                                                                  f.GetName(doc).IndexOf("Material: Area", StringComparison.OrdinalIgnoreCase) >= 0 || 
                                                                  f.GetName(doc).IndexOf("Material: Área", StringComparison.OrdinalIgnoreCase) >= 0);
                    if (fMatArea != null)
                    {
                        var f = def.AddField(fMatArea);
                        f.ColumnHeading = "Área Material (m²)";
                        added++;
                    }

                    // 5. Material: Volumen
                    var fMatVol = schedulable.FirstOrDefault(f => f.ParameterId == new ElementId(BuiltInParameter.MATERIAL_VOLUME) || 
                                                                 f.GetName(doc).IndexOf("Material: Volume", StringComparison.OrdinalIgnoreCase) >= 0 || 
                                                                 f.GetName(doc).IndexOf("Material: Volumen", StringComparison.OrdinalIgnoreCase) >= 0);
                    if (fMatVol != null)
                    {
                        var f = def.AddField(fMatVol);
                        f.ColumnHeading = "Volumen Material (m³)";
                        added++;
                    }
                }
                else
                {
                    // Part List
                    // 1. Categoría
                    var fCat = schedulable.FirstOrDefault(f => f.ParameterId == new ElementId(BuiltInParameter.ELEM_CATEGORY_PARAM));
                    if (fCat != null)
                    {
                        var f = def.AddField(fCat);
                        f.ColumnHeading = "Categoría";
                        added++;
                    }

                    // 2. Familia y Tipo
                    var fFam = schedulable.FirstOrDefault(f => f.ParameterId == new ElementId(BuiltInParameter.ELEM_FAMILY_AND_TYPE_PARAM));
                    if (fFam != null)
                    {
                        var f = def.AddField(fFam);
                        f.ColumnHeading = "Familia y Tipo";
                        primarySortField = f;
                        added++;
                    }

                    // 3. Cantidad
                    var fCount = schedulable.FirstOrDefault(f => f.FieldType == ScheduleFieldType.Count);
                    if (fCount != null)
                    {
                        var f = def.AddField(fCount);
                        f.ColumnHeading = "Cantidad";
                        added++;
                    }

                    // 4. Dimensiones (Área / Volumen / Longitud)
                    var fArea = schedulable.FirstOrDefault(f => f.GetName(doc).IndexOf("Area", StringComparison.OrdinalIgnoreCase) >= 0 || 
                                                               f.GetName(doc).IndexOf("Área", StringComparison.OrdinalIgnoreCase) >= 0);
                    if (fArea != null)
                    {
                        var f = def.AddField(fArea);
                        f.ColumnHeading = "Área (m²)";
                        added++;
                    }

                    var fVol = schedulable.FirstOrDefault(f => f.GetName(doc).IndexOf("Volume", StringComparison.OrdinalIgnoreCase) >= 0 || 
                                                              f.GetName(doc).IndexOf("Volumen", StringComparison.OrdinalIgnoreCase) >= 0);
                    if (fVol != null)
                    {
                        var f = def.AddField(fVol);
                        f.ColumnHeading = "Volumen (m³)";
                        added++;
                    }

                    // 5. Comentarios
                    var fComm = schedulable.FirstOrDefault(f => f.ParameterId == new ElementId(BuiltInParameter.ALL_MODEL_INSTANCE_COMMENTS));
                    if (fComm != null)
                    {
                        var f = def.AddField(fComm);
                        f.ColumnHeading = "Comentarios";
                        added++;
                    }
                }

                // Sorting y Totales
                if (primarySortField != null)
                {
                    try
                    {
                        var sortGroup = new ScheduleSortGroupField(primarySortField.FieldId, ScheduleSortOrder.Ascending)
                        {
                            ShowFooter = true,
                            ShowBlankLine = false
                        };
                        def.AddSortGroupField(sortGroup);
                    }
                    catch { }
                }

                def.IsItemized = !groupIdentical;
                def.ShowGrandTotal = true;
                def.ShowGrandTotalCount = true;
                def.ShowGrandTotalTitle = true;
                def.GrandTotalTitle = "TOTAL GENERAL";
                def.ShowTitle = true;
                def.ShowHeaders = true;

                result.FieldsReplicatedCount = added;
                result.Success = true;
                result.Message = wasCreated
                    ? $"Tabla estándar creada ({added} columnas funcionales asignadas)."
                    : $"Tabla estándar actualizada ({added} columnas funcionales asignadas).";
                return result;
            }
            catch (Exception ex)
            {
                result.Success = false;
                result.Message = $"Error al estructurar tabla estándar: {ex.Message}";
                return result;
            }
        }

        public static void OpenScheduleInRevit(UIDocument uidoc, ViewSchedule schedule)
        {
            if (uidoc != null && schedule != null)
            {
                uidoc.ActiveView = schedule;
            }
        }

        public static ViewSchedule DuplicateSchedule(Document doc, ViewSchedule schedule)
        {
            using (Transaction t = new Transaction(doc, "Duplicar Tabla de Planificación"))
            {
                t.Start();
                ElementId newId = schedule.Duplicate(ViewDuplicateOption.Duplicate);
                ViewSchedule newVs = (ViewSchedule)doc.GetElement(newId);
                newVs.Name = GetUniqueScheduleName(doc, $"{schedule.Name} (Copia)");
                t.Commit();
                return newVs;
            }
        }

        /// <summary>
        /// Duplica un conjunto de tablas de planificación en una única transacción atómica, permitiendo
        /// personalizar el patrón de nombrado y reasignar valores de filtros (ej: cambiar P01 por P03).
        /// </summary>
        public static (List<ViewSchedule> duplicatedSchedules, List<string> logMessages) DuplicateSchedules(
            Document doc,
            IList<ViewSchedule> schedules,
            string? findName = null,
            string? replaceName = null,
            bool modifyFilters = false,
            string? filterFindValue = null,
            string? filterNewValue = null)
        {
            var resultList = new List<ViewSchedule>();
            var logMessages = new List<string>();

            if (schedules == null || schedules.Count == 0)
                return (resultList, logMessages);

            using (Transaction t = new Transaction(doc, "Duplicar Tablas de Planificación"))
            {
                t.Start();

                foreach (var srcSchedule in schedules)
                {
                    if (srcSchedule == null || !srcSchedule.IsValidObject) continue;

                    try
                    {
                        ElementId newId = srcSchedule.Duplicate(ViewDuplicateOption.Duplicate);
                        ViewSchedule newSchedule = (ViewSchedule)doc.GetElement(newId);
                        if (newSchedule == null) continue;

                        // 1. Determinar el nuevo nombre
                        string originalName = srcSchedule.Name;
                        string newName = originalName;

                        if (!string.IsNullOrWhiteSpace(findName) && originalName.IndexOf(findName, StringComparison.OrdinalIgnoreCase) >= 0)
                        {
                            newName = Regex.Replace(
                                originalName, 
                                Regex.Escape(findName), 
                                replaceName ?? "", 
                                RegexOptions.IgnoreCase);
                        }
                        else if (!string.IsNullOrWhiteSpace(replaceName) && string.IsNullOrWhiteSpace(findName))
                        {
                            newName = $"{originalName} {replaceName}";
                        }
                        else
                        {
                            newName = $"{originalName} (Copia)";
                        }

                        // Sanear caracteres inválidos de Revit
                        char[] invalidChars = new char[] { '\\', ':', '{', '}', '[', ']', '|', ';', '<', '>', '?', '`', '~' };
                        foreach (char c in invalidChars)
                        {
                            newName = newName.Replace(c, '_');
                        }

                        newSchedule.Name = GetUniqueScheduleName(doc, newName.Trim());

                        // 2. Modificar filtros si fue solicitado
                        int filtersModified = 0;
                        if (modifyFilters && !string.IsNullOrEmpty(filterNewValue))
                        {
                            ScheduleDefinition def = newSchedule.Definition;
                            IList<ScheduleFilter> currentFilters = def.GetFilters();

                            for (int i = 0; i < currentFilters.Count; i++)
                            {
                                ScheduleFilter filter = currentFilters[i];
                                ScheduleFieldId fieldId = filter.FieldId;
                                ScheduleFilterType filterType = filter.FilterType;

                                // Verificar si es filtro de string
                                string? currentStrVal = null;
                                try { currentStrVal = filter.GetStringValue(); } catch { }

                                if (currentStrVal != null)
                                {
                                    bool shouldUpdate = false;
                                    string updatedStrVal = currentStrVal;

                                    if (string.IsNullOrWhiteSpace(filterFindValue))
                                    {
                                        shouldUpdate = true;
                                        updatedStrVal = filterNewValue;
                                    }
                                    else if (currentStrVal.Equals(filterFindValue.Trim(), StringComparison.OrdinalIgnoreCase))
                                    {
                                        shouldUpdate = true;
                                        updatedStrVal = filterNewValue;
                                    }
                                    else if (currentStrVal.IndexOf(filterFindValue.Trim(), StringComparison.OrdinalIgnoreCase) >= 0)
                                    {
                                        shouldUpdate = true;
                                        updatedStrVal = Regex.Replace(
                                            currentStrVal, 
                                            Regex.Escape(filterFindValue.Trim()), 
                                            filterNewValue, 
                                            RegexOptions.IgnoreCase);
                                    }

                                    if (shouldUpdate)
                                    {
                                        try
                                        {
                                            ScheduleFilter updatedFilter = new ScheduleFilter(fieldId, filterType, updatedStrVal);
                                            def.SetFilter(i, updatedFilter);
                                            filtersModified++;
                                        }
                                        catch (Exception fEx)
                                        {
                                            System.Diagnostics.Debug.WriteLine($"Error actualizando filtro en {newSchedule.Name}: {fEx.Message}");
                                        }
                                    }
                                }
                                else
                                {
                                    // Comprobar si es filtro entero
                                    try
                                    {
                                        int intVal = filter.GetIntegerValue();
                                        if (int.TryParse(filterFindValue, out int findInt) && intVal == findInt && int.TryParse(filterNewValue, out int newInt))
                                        {
                                            def.SetFilter(i, new ScheduleFilter(fieldId, filterType, newInt));
                                            filtersModified++;
                                        }
                                    }
                                    catch { }
                                }
                            }
                        }

                        resultList.Add(newSchedule);
                        string filterMsg = filtersModified > 0 ? $" [Filtro actualizado a '{filterNewValue}']" : "";
                        logMessages.Add($"Tabla '{srcSchedule.Name}' duplicada como '{newSchedule.Name}'{filterMsg}.");
                    }
                    catch (Exception ex)
                    {
                        logMessages.Add($"Error al duplicar '{srcSchedule.Name}': {ex.Message}");
                    }
                }

                t.Commit();
            }

            return (resultList, logMessages);
        }

        public class ScheduleFilterInfo
        {
            public string ScheduleName { get; set; } = string.Empty;
            public string FieldName { get; set; } = string.Empty;
            public ScheduleFilterType FilterType { get; set; }
            public string FilterTypeDisplay { get; set; } = string.Empty;
            public string CurrentValue { get; set; } = string.Empty;
            public bool IsString { get; set; }

            public string DisplaySummary => $"{ScheduleName} ➔ [{FieldName}] {FilterTypeDisplay} '{CurrentValue}'";
        }

        public static List<ScheduleFilterInfo> GetScheduleFiltersInfo(Document doc, IEnumerable<ViewSchedule> schedules)
        {
            var result = new List<ScheduleFilterInfo>();
            if (schedules == null) return result;

            foreach (var sched in schedules)
            {
                if (sched == null || !sched.IsValidObject) continue;
                try
                {
                    ScheduleDefinition def = sched.Definition;
                    IList<ScheduleFilter> filters = def.GetFilters();
                    foreach (ScheduleFilter f in filters)
                    {
                        string fieldName = "Campo";
                        try { fieldName = def.GetField(f.FieldId).GetName(); } catch { }

                        string val = "";
                        bool isStr = false;
                        try
                        {
                            val = f.GetStringValue();
                            isStr = true;
                        }
                        catch { }

                        if (string.IsNullOrEmpty(val))
                        {
                            try { val = f.GetIntegerValue().ToString(); } catch { }
                        }
                        if (string.IsNullOrEmpty(val))
                        {
                            try { val = f.GetDoubleValue().ToString(); } catch { }
                        }

                        if (!string.IsNullOrEmpty(val))
                        {
                            result.Add(new ScheduleFilterInfo
                            {
                                ScheduleName = sched.Name,
                                FieldName = fieldName,
                                FilterType = f.FilterType,
                                FilterTypeDisplay = GetFilterTypeName(f.FilterType),
                                CurrentValue = val,
                                IsString = isStr
                            });
                        }
                    }
                }
                catch { }
            }

            return result;
        }

        public static string GetFilterTypeName(ScheduleFilterType type)
        {
            string s = type.ToString();
            return s switch
            {
                "Equal" => "IGUAL A",
                "NotEqual" => "NO ES IGUAL A",
                "Contains" => "CONTIENE",
                "NotContains" or "DoesNotContain" => "NO CONTIENE",
                "BeginsWith" => "COMIENZA CON",
                "NotBeginsWith" => "NO COMIENZA CON",
                "EndsWith" => "TERMINA CON",
                "NotEndsWith" => "NO TERMINA CON",
                "GreaterThan" => "MAYOR QUE",
                "GreaterThanOrEqual" => "MAYOR O IGUAL QUE",
                "LessThan" => "MENOR QUE",
                "LessThanOrEqual" => "MENOR O IGUAL QUE",
                "HasValue" => "TIENE VALOR",
                "HasNoValue" => "NO TIENE VALOR",
                _ => s
            };
        }

        public static void DeleteSchedule(Document doc, ViewSchedule schedule)
        {
            if (schedule == null || !schedule.IsValidObject) return;
            using (Transaction t = new Transaction(doc, "Eliminar Tabla de Planificación"))
            {
                t.Start();
                if (doc.GetElement(schedule.Id) != null)
                {
                    doc.Delete(schedule.Id);
                }
                t.Commit();
            }
        }

        /// <summary>
        /// Elimina un conjunto de tablas de planificación por sus ElementIds en una única transacción atómica.
        /// </summary>
        public static (int deletedCount, string message) DeleteSchedules(Document doc, IEnumerable<ElementId> scheduleIds)
        {
            if (scheduleIds == null)
                return (0, "No se seleccionó ninguna tabla para eliminar.");

            var validIds = scheduleIds
                .Where(id => id != null && id != ElementId.InvalidElementId)
                .Distinct()
                .ToList();

            if (validIds.Count == 0)
                return (0, "No se seleccionó ninguna tabla para eliminar.");

            int count = 0;
            using (Transaction t = new Transaction(doc, "Eliminar Tablas de Planificación"))
            {
                t.Start();
                foreach (var id in validIds)
                {
                    try
                    {
                        if (doc.GetElement(id) != null)
                        {
                            doc.Delete(id);
                            count++;
                        }
                    }
                    catch { }
                }
                t.Commit();
            }
            return (count, $"Se eliminaron {count} tabla(s) de planificación exitosamente.");
        }

        /// <summary>
        /// Elimina un conjunto de tablas de planificación en una única transacción atómica.
        /// </summary>
        public static (int deletedCount, string message) DeleteSchedules(Document doc, IList<ViewSchedule> schedules)
        {
            if (schedules == null || schedules.Count == 0)
                return (0, "No se seleccionó ninguna tabla para eliminar.");

            var ids = schedules
                .Where(s => s != null && s.IsValidObject)
                .Select(s => s.Id)
                .ToList();

            return DeleteSchedules(doc, ids);
        }

        /// <summary>
        /// Renombra una tabla de planificación validando caracteres prohibidos de Revit y nombres únicos.
        /// </summary>
        public static (bool success, string message) RenameSchedule(Document doc, ViewSchedule schedule, string newName)
        {
            if (string.IsNullOrWhiteSpace(newName))
                return (false, "El nombre de la tabla no puede estar vacío.");

            string cleanName = newName.Trim();

            // Caracteres prohibidos por Revit en nombres de vistas
            char[] invalidChars = new char[] { '\\', ':', '{', '}', '[', ']', '|', ';', '<', '>', '?', '`', '~' };
            if (cleanName.IndexOfAny(invalidChars) >= 0)
            {
                return (false, "El nombre contiene caracteres prohibidos por Revit (\\ : { } [ ] | ; < > ? ` ~).");
            }

            try
            {
                using (Transaction t = new Transaction(doc, "Renombrar Tabla"))
                {
                    t.Start();
                    schedule.Name = cleanName;
                    t.Commit();
                }
                return (true, $"Tabla renombrada a '{cleanName}'.");
            }
            catch (Exception ex)
            {
                return (false, $"Error al renombrar: {ex.Message}");
            }
        }

        public static string GetUniqueScheduleName(Document doc, string desiredName)
        {
            var allViewNames = new HashSet<string>(
                new FilteredElementCollector(doc)
                    .OfClass(typeof(Autodesk.Revit.DB.View))
                    .Cast<Autodesk.Revit.DB.View>()
                    .Select(v => v.Name),
                StringComparer.OrdinalIgnoreCase);

            string uniqueName = desiredName;
            int suffix = 1;
            while (allViewNames.Contains(uniqueName))
            {
                uniqueName = $"{desiredName} ({suffix++})";
            }
            return uniqueName;
        }

        private static ScheduleFilter? BuildFilter(FilterSnapshot snap, ScheduleFieldId targetFieldId)
        {
            ScheduleFilterType type = snap.FilterType;

            if (type == ScheduleFilterType.HasValue || type == ScheduleFilterType.HasNoValue)
            {
                return new ScheduleFilter(targetFieldId, type);
            }

            if (snap.StringValue != null)
            {
                return new ScheduleFilter(targetFieldId, type, snap.StringValue);
            }

            if (snap.DoubleValue.HasValue)
            {
                return new ScheduleFilter(targetFieldId, type, snap.DoubleValue.Value);
            }

            if (snap.IntegerValue.HasValue)
            {
                return new ScheduleFilter(targetFieldId, type, snap.IntegerValue.Value);
            }

            if (snap.ElementIdValue != null && snap.ElementIdValue != ElementId.InvalidElementId)
            {
                return new ScheduleFilter(targetFieldId, type, snap.ElementIdValue);
            }

            return null;
        }
    }
}
