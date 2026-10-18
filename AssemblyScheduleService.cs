using System;
using System.Collections.Generic;
using System.Linq;
using Autodesk.Revit.DB;

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
        public ElementId Id => Assembly.Id;
        public string Name => Assembly.Name;
        public string TypeName { get; set; } = string.Empty;
        public int MemberCount { get; set; }
        public string CategoriesSummary { get; set; } = string.Empty;
        public bool AllowsViewCreation { get; set; }
        public bool HasExistingSchedule { get; set; }
        public string ExistingScheduleInfo { get; set; } = string.Empty;
        public bool IsSelected { get; set; } = true;

        public string StatusBadge => HasExistingSchedule ? "✅ Con Tabla" : (AllowsViewCreation ? "⚡ Listo" : "⚪ Tipo ya creado");
        public string StatusColor => HasExistingSchedule ? "#10B981" : (AllowsViewCreation ? "#3B82F6" : "#71717A");
    }

    public static class AssemblyScheduleService
    {
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
        /// Crea o actualiza la tabla de planificación de un ensamblaje replicando los campos y formato de la tabla plantilla.
        /// </summary>
        public static AssemblyScheduleResult CreateOrUpdateAssemblySchedule(
            Document doc,
            AssemblyInstance assembly,
            ViewSchedule sourceSchedule,
            HashSet<string>? allowedFieldNames = null,
            bool replicateFilters = true,
            bool replicateSorting = true,
            bool replicateAppearance = true)
        {
            var result = new AssemblyScheduleResult
            {
                AssemblyName = assembly.AssemblyTypeName ?? assembly.Name
            };

            ScheduleDefinition sourceDef = sourceSchedule.Definition;
            ElementId categoryId = sourceDef.CategoryId;

            // Verificar si el ensamblaje ya tiene una tabla de la misma categoría o tipo
            var existingSchedules = GetAssemblySchedules(doc, assembly.Id);
            ViewSchedule? targetSchedule = null;

            if (sourceDef.IsMaterialTakeoff)
            {
                targetSchedule = existingSchedules.FirstOrDefault(vs => vs.Definition.IsMaterialTakeoff);
            }
            else if (categoryId != ElementId.InvalidElementId)
            {
                targetSchedule = existingSchedules.FirstOrDefault(vs => !vs.Definition.IsMaterialTakeoff && vs.Definition.CategoryId == categoryId);
            }
            else
            {
                // Multi-category (Part List)
                targetSchedule = existingSchedules.FirstOrDefault(vs => !vs.Definition.IsMaterialTakeoff && vs.Definition.CategoryId == ElementId.InvalidElementId);
            }

            bool wasCreated = false;

            if (targetSchedule == null)
            {
                if (!assembly.AllowsAssemblyViewCreation())
                {
                    result.Success = false;
                    result.Message = $"El ejemplar '{result.AssemblyName}' no permite crear nuevas vistas (las vistas ya pertenecen al tipo de ensamblaje o a otro ejemplar).";
                    return result;
                }

                // Crear nueva tabla de ensamblaje
                try
                {
                    if (sourceDef.IsMaterialTakeoff)
                    {
                        targetSchedule = AssemblyViewUtils.CreateMaterialTakeoff(doc, assembly.Id);
                    }
                    else if (categoryId != ElementId.InvalidElementId)
                    {
                        // Verificar que el ensamblaje contenga elementos de esa categoría
                        bool hasCatElements = assembly.GetMemberIds()
                            .Select(id => doc.GetElement(id))
                            .Where(e => e != null && e.Category != null)
                            .Any(e => e.Category.Id == categoryId);

                        if (!hasCatElements)
                        {
                            result.Success = false;
                            result.Message = $"El ensamblaje '{result.AssemblyName}' no contiene elementos de la categoría de la tabla seleccionada.";
                            return result;
                        }

                        targetSchedule = AssemblyViewUtils.CreateSingleCategorySchedule(doc, assembly.Id, categoryId);
                    }
                    else
                    {
                        // Lista de piezas (Part List - Multi-category)
                        targetSchedule = AssemblyViewUtils.CreatePartList(doc, assembly.Id);
                    }

                    wasCreated = true;

                    // Asignar nombre descriptivo único a la tabla creada
                    try
                    {
                        string desiredName = $"{assembly.AssemblyTypeName ?? assembly.Name} - {sourceSchedule.Name}";
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
                        targetSchedule.Name = uniqueName;
                    }
                    catch { }
                }
                catch (Exception ex)
                {
                    result.Success = false;
                    result.Message = $"Error al crear tabla para '{result.AssemblyName}': {ex.Message}";
                    return result;
                }
            }

            result.WasCreated = wasCreated;
            result.ScheduleName = targetSchedule.Name;

            // Replicar campos y formato
            try
            {
                int fieldsCount = ReplicateScheduleFieldsAndFormat(
                    doc,
                    sourceSchedule,
                    targetSchedule,
                    allowedFieldNames,
                    replicateFilters,
                    replicateSorting,
                    replicateAppearance);

                result.FieldsReplicatedCount = fieldsCount;
                result.Success = true;
                result.Message = wasCreated 
                    ? $"Tabla creada y {fieldsCount} campos replicados con formato exacto." 
                    : $"Tabla actualizada y {fieldsCount} campos replicados con formato exacto.";
                return result;
            }
            catch (Exception ex)
            {
                result.Success = false;
                result.Message = $"Error al replicar formato en '{result.AssemblyName}': {ex.Message}";
                return result;
            }
        }

        /// <summary>
        /// Reclona fielmente todos los campos, cabeceras, anchos, alineaciones, filtros y ordenaciones
        /// desde la tabla origen a la tabla destino.
        /// </summary>
        public static int ReplicateScheduleFieldsAndFormat(
            Document doc,
            ViewSchedule sourceSchedule,
            ViewSchedule targetSchedule,
            HashSet<string>? allowedFieldNames = null,
            bool replicateFilters = true,
            bool replicateSorting = true,
            bool replicateAppearance = true)
        {
            ScheduleDefinition sourceDef = sourceSchedule.Definition;
            ScheduleDefinition targetDef = targetSchedule.Definition;

            // Limpiar filtros y ordenación previa del destino antes de tocar los campos
            targetDef.ClearSortGroupFields();
            targetDef.ClearFilters();

            // Limpiar campos existentes del destino
            var currentTargetFields = targetDef.GetFieldOrder().ToList();
            for (int i = currentTargetFields.Count - 1; i >= 0; i--)
            {
                try
                {
                    targetDef.RemoveField(currentTargetFields[i]);
                }
                catch { }
            }

            // Mapeo entre ScheduleFieldId origen y ScheduleFieldId destino
            Dictionary<ScheduleFieldId, ScheduleFieldId> fieldMap = new Dictionary<ScheduleFieldId, ScheduleFieldId>();

            IList<SchedulableField> availableSchedulableFields = targetDef.GetSchedulableFields();
            IList<ScheduleFieldId> sourceFieldOrder = sourceDef.GetFieldOrder();

            int replicatedCount = 0;

            foreach (ScheduleFieldId sFieldId in sourceFieldOrder)
            {
                ScheduleField sourceField = sourceDef.GetField(sFieldId);
                string fieldDisplayName = string.IsNullOrEmpty(sourceField.ColumnHeading) 
                    ? sourceField.GetName() 
                    : sourceField.ColumnHeading;

                // Si se proporcionó un filtro de nombres permitidos y no está contenido, omitir
                if (allowedFieldNames != null && !allowedFieldNames.Contains(fieldDisplayName) && !allowedFieldNames.Contains(sourceField.GetName()))
                {
                    continue;
                }

                ScheduleField? targetField = null;

                // 1. Campo de Recuento (Count)
                if (sourceField.FieldType == ScheduleFieldType.Count)
                {
                    var countSf = availableSchedulableFields.FirstOrDefault(f => f.FieldType == ScheduleFieldType.Count);
                    if (countSf != null)
                    {
                        targetField = targetDef.AddField(countSf);
                    }
                }
                // 2. Campo basado en Parámetro
                else if (sourceField.ParameterId != ElementId.InvalidElementId)
                {
                    // Buscar coincidencia directa por ParameterId
                    var sf = availableSchedulableFields.FirstOrDefault(f => f.ParameterId == sourceField.ParameterId);
                    
                    // Fallback por nombre si el ID no coincide directamente
                    if (sf == null)
                    {
                        string sourceName = sourceField.GetName();
                        sf = availableSchedulableFields.FirstOrDefault(f => f.GetName(doc).Equals(sourceName, StringComparison.OrdinalIgnoreCase));
                    }

                    if (sf != null)
                    {
                        targetField = targetDef.AddField(sf);
                    }
                }

                // 3. Replicar propiedades detalladas del campo
                if (targetField != null)
                {
                    // Guardar mapeo de IDs para filtros y ordenación
                    fieldMap[sFieldId] = targetField.FieldId;

                    // Nombre de encabezado (Column Heading)
                    if (!string.IsNullOrEmpty(sourceField.ColumnHeading))
                    {
                        targetField.ColumnHeading = sourceField.ColumnHeading;
                    }

                    // Visibilidad (Oculto / Visible)
                    targetField.IsHidden = sourceField.IsHidden;

                    // Orientación del encabezado
                    try { targetField.HeadingOrientation = sourceField.HeadingOrientation; } catch { }

                    // Alineación horizontal
                    try { targetField.HorizontalAlignment = sourceField.HorizontalAlignment; } catch { }

                    // Tipo de visualización (Min, Max, Normal, etc.)
                    try { targetField.DisplayType = sourceField.DisplayType; } catch { }

                    // Ancho de columna en lámina
                    try 
                    { 
                        if (sourceField.GridColumnWidth > 0.0001)
                            targetField.GridColumnWidth = sourceField.GridColumnWidth; 
                    } 
                    catch { }

                    // Formato de unidades
                    try
                    {
                        FormatOptions fo = sourceField.GetFormatOptions();
                        if (fo != null)
                        {
                            targetField.SetFormatOptions(fo);
                        }
                    }
                    catch { }

                    replicatedCount++;
                }
            }

            // 4. Replicar Ordenación y Agrupación (Sorting/Grouping)
            if (replicateSorting)
            {
                var sourceSortList = sourceDef.GetSortGroupFields();
                foreach (var ssf in sourceSortList)
                {
                    if (fieldMap.TryGetValue(ssf.FieldId, out ScheduleFieldId targetFieldId))
                    {
                        try
                        {
                            ScheduleSortGroupField tsf = new ScheduleSortGroupField(targetFieldId, ssf.SortOrder)
                            {
                                ShowHeader = ssf.ShowHeader,
                                ShowFooter = ssf.ShowFooter,
                                ShowBlankLine = ssf.ShowBlankLine
                            };
                            targetDef.AddSortGroupField(tsf);
                        }
                        catch { }
                    }
                }
            }

            // 5. Replicar Filtros
            if (replicateFilters)
            {
                var sourceFilters = sourceDef.GetFilters();
                foreach (var sf in sourceFilters)
                {
                    if (fieldMap.TryGetValue(sf.FieldId, out ScheduleFieldId targetFieldId))
                    {
                        try
                        {
                            ScheduleFilter? clonedFilter = CloneFilter(sf, targetFieldId);
                            if (clonedFilter != null)
                            {
                                targetDef.AddFilter(clonedFilter);
                            }
                        }
                        catch { }
                    }
                }
            }

            // 6. Replicar Propiedades Globales de la Definición
            try { targetDef.IsItemized = sourceDef.IsItemized; } catch { }
            try 
            { 
                targetDef.ShowGrandTotal = sourceDef.ShowGrandTotal; 
                if (sourceDef.ShowGrandTotal)
                {
                    try { targetDef.ShowGrandTotalTitle = sourceDef.ShowGrandTotalTitle; } catch { }
                    try { targetDef.ShowGrandTotalCount = sourceDef.ShowGrandTotalCount; } catch { }
                    try { targetDef.GrandTotalTitle = sourceDef.GrandTotalTitle; } catch { }
                }
            } 
            catch { }
            try { targetDef.ShowTitle = sourceDef.ShowTitle; } catch { }
            try { targetDef.ShowHeaders = sourceDef.ShowHeaders; } catch { }

            // 7. Plantilla de Vista / Propiedades Gráficas
            if (replicateAppearance && sourceSchedule.ViewTemplateId != null && sourceSchedule.ViewTemplateId != ElementId.InvalidElementId)
            {
                try
                {
                    targetSchedule.ViewTemplateId = sourceSchedule.ViewTemplateId;
                }
                catch { }
            }

            return replicatedCount;
        }

        private static ScheduleFilter? CloneFilter(ScheduleFilter sourceFilter, ScheduleFieldId targetFieldId)
        {
            ScheduleFilterType type = sourceFilter.FilterType;

            // Filtros sin valor (HasValue, IsEmpty, etc.)
            if (type == ScheduleFilterType.HasValue || type == ScheduleFilterType.HasNoValue)
            {
                return new ScheduleFilter(targetFieldId, type);
            }

            // Probar clonación según el tipo de dato que contiene
            try
            {
                string sVal = sourceFilter.GetStringValue();
                if (sVal != null)
                {
                    return new ScheduleFilter(targetFieldId, type, sVal);
                }
            }
            catch { }

            try
            {
                double dVal = sourceFilter.GetDoubleValue();
                return new ScheduleFilter(targetFieldId, type, dVal);
            }
            catch { }

            try
            {
                int iVal = sourceFilter.GetIntegerValue();
                return new ScheduleFilter(targetFieldId, type, iVal);
            }
            catch { }

            try
            {
                ElementId elId = sourceFilter.GetElementIdValue();
                if (elId != null && elId != ElementId.InvalidElementId)
                {
                    return new ScheduleFilter(targetFieldId, type, elId);
                }
            }
            catch { }

            return null;
        }
    }
}
