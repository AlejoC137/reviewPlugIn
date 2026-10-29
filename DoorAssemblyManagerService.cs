using System;
using System.Collections.Generic;
using System.Linq;
using Autodesk.Revit.DB;

namespace RevitToSupabasePlugin
{
    /// <summary>
    /// Modelo de campo configurable para el Cuadro de Puertas.
    /// </summary>
    public class DoorScheduleFieldConfig
    {
        public string Key { get; set; } = string.Empty;
        public string HeaderName { get; set; } = string.Empty;
        public BuiltInParameter? BuiltInParam { get; set; }
        public bool IsCount { get; set; }
        public bool IsSelected { get; set; } = true;
        public bool SortAscending { get; set; }
        public bool IsSortKey { get; set; }
    }

    /// <summary>
    /// Representa un elemento o tipo de puerta en la interfaz de usuario.
    /// </summary>
    public class DoorItemViewModel : System.ComponentModel.INotifyPropertyChanged
    {
        public event System.ComponentModel.PropertyChangedEventHandler? PropertyChanged;
        protected void OnPropertyChanged([System.Runtime.CompilerServices.CallerMemberName] string? name = null)
        {
            PropertyChanged?.Invoke(this, new System.ComponentModel.PropertyChangedEventArgs(name));
        }

        private bool _isSelected = false;
        public bool IsSelected
        {
            get => _isSelected;
            set
            {
                if (_isSelected != value)
                {
                    _isSelected = value;
                    OnPropertyChanged();
                }
            }
        }

        public ElementId Id { get; set; } = ElementId.InvalidElementId;
        public ElementId TypeId { get; set; } = ElementId.InvalidElementId;
        public string Mark { get; set; } = string.Empty;
        public string FamilyName { get; set; } = string.Empty;
        public string TypeName { get; set; } = string.Empty;
        public string LevelName { get; set; } = string.Empty;
        public string Dimensions { get; set; } = string.Empty;
        public int InstanceCount { get; set; } = 1;
        public bool AlreadyInAssembly { get; set; }
        public string AssemblyName { get; set; } = string.Empty;
        public ElementId AssemblyId { get; set; } = ElementId.InvalidElementId;
        public List<ElementId> MemberElementIds { get; set; } = new List<ElementId>();

        public string StatusBadge => AlreadyInAssembly ? $"📦 {AssemblyName}" : "⚡ Listo";
        public string StatusColor => AlreadyInAssembly ? "#059669" : "#0F4369";
        public string DisplayTitle => string.IsNullOrWhiteSpace(Mark)
            ? $"{FamilyName} - {TypeName}"
            : $"[{Mark}] {FamilyName} - {TypeName}";

        private string _targetAssemblyName = string.Empty;
        public string TargetAssemblyName
        {
            get
            {
                if (!string.IsNullOrWhiteSpace(_targetAssemblyName))
                    return _targetAssemblyName;
                if (AlreadyInAssembly && !string.IsNullOrWhiteSpace(AssemblyName))
                    return AssemblyName;
                return DoorAssemblyManagerService.GetParameterBasedName(this);
            }
            set
            {
                if (_targetAssemblyName != value)
                {
                    _targetAssemblyName = value;
                    OnPropertyChanged();
                }
            }
        }
    }

    /// <summary>
    /// Opciones para la generación de vistas y tablas de ensamblajes en lote.
    /// </summary>
    public class AssemblyBatchViewOptions
    {
        public bool CreateSchedule { get; set; } = true;
        public string ScheduleNameTemplate { get; set; } = "Cuadro de Despiece";
        public bool CreatePlan { get; set; } = true;
        public bool CreateFrontElevation { get; set; } = true;
        public bool CreateBackElevation { get; set; } = false;
        public bool Create3DOrthographic { get; set; } = true;
        public bool CreateSection { get; set; } = false;
        public bool AutoDimensionViews { get; set; } = true;
        public int ViewScale { get; set; } = 20; // 1:20 por defecto (estándar carpintería)
        public ElementId? ViewTemplateId { get; set; }
        public ElementId? CategoryId { get; set; }
    }

    /// <summary>
    /// Opciones para la creación en lote de Ensamblajes (Batch Assemblies).
    /// </summary>
    public class BatchAssemblyOptions
    {
        public bool GroupByInstance { get; set; } = false;
        public bool SingleInstancePerType { get; set; } = true;
        public string InitialName { get; set; } = "P-01";
        public bool AppendTypeName { get; set; } = false;
        public bool AppendDimensions { get; set; } = false;
        public bool AppendInstanceCount { get; set; } = false;
        public string NamingMode { get; set; } = "Sequential"; // "Sequential", "Mark", "TypeName", "CustomPattern"
        public string CustomPattern { get; set; } = "{Prefix}{Seq}";
        public BuiltInCategory Category { get; set; } = BuiltInCategory.OST_Doors;
    }

    /// <summary>
    /// Servicio de alto rendimiento para creación de Cuadros de Puertas/Ventanas personalizables,
    /// ensamblajes por tipo o ejemplar y generación batch de vistas.
    /// </summary>
    public static class DoorAssemblyManagerService
    {
        /// <summary>
        /// Obtiene la lista de campos predeterminados recomendados para un Cuadro Técnico de Puertas.
        /// </summary>
        public static List<DoorScheduleFieldConfig> GetDefaultDoorFields()
        {
            return GetDefaultFields(BuiltInCategory.OST_Doors);
        }

        /// <summary>
        /// Obtiene la lista de campos predeterminados recomendados según la categoría (Puertas o Ventanas).
        /// </summary>
        public static List<DoorScheduleFieldConfig> GetDefaultFields(BuiltInCategory category = BuiltInCategory.OST_Doors)
        {
            bool isWindow = category == BuiltInCategory.OST_Windows;
            return new List<DoorScheduleFieldConfig>
            {
                new DoorScheduleFieldConfig { Key = "TypeMark", HeaderName = "Código / Tipo", BuiltInParam = BuiltInParameter.ALL_MODEL_TYPE_MARK, IsSelected = true, IsSortKey = true, SortAscending = true },
                new DoorScheduleFieldConfig { Key = "Mark", HeaderName = "Marca", BuiltInParam = BuiltInParameter.ALL_MODEL_MARK, IsSelected = true },
                new DoorScheduleFieldConfig { Key = "FamilyAndType", HeaderName = "Familia y Tipo", BuiltInParam = BuiltInParameter.ELEM_FAMILY_AND_TYPE_PARAM, IsSelected = true },
                new DoorScheduleFieldConfig { Key = "Width", HeaderName = "Anchura (m)", BuiltInParam = isWindow ? BuiltInParameter.WINDOW_WIDTH : BuiltInParameter.DOOR_WIDTH, IsSelected = true },
                new DoorScheduleFieldConfig { Key = "Height", HeaderName = "Altura (m)", BuiltInParam = isWindow ? BuiltInParameter.WINDOW_HEIGHT : BuiltInParameter.DOOR_HEIGHT, IsSelected = true },
                new DoorScheduleFieldConfig { Key = "Count", HeaderName = "Cantidad (Local)", IsCount = true, IsSelected = true },
                new DoorScheduleFieldConfig { Key = "GlobalCount", HeaderName = "Total en Obra (Global)", BuiltInParam = null, IsSelected = true },
                new DoorScheduleFieldConfig { Key = "Level", HeaderName = "Nivel / Ubicación", BuiltInParam = BuiltInParameter.FAMILY_LEVEL_PARAM, IsSelected = true },
                new DoorScheduleFieldConfig { Key = "Locations", HeaderName = "Resumen Ubicaciones", BuiltInParam = null, IsSelected = false },
                new DoorScheduleFieldConfig { Key = "Comments", HeaderName = "Observaciones", BuiltInParam = BuiltInParameter.ALL_MODEL_INSTANCE_COMMENTS, IsSelected = true },
                new DoorScheduleFieldConfig { Key = "FrameMaterial", HeaderName = "Material Marco", BuiltInParam = isWindow ? null : BuiltInParameter.DOOR_FRAME_MATERIAL, IsSelected = false },
                new DoorScheduleFieldConfig { Key = "PanelMaterial", HeaderName = isWindow ? "Material Vidrio" : "Material Hoja", BuiltInParam = null, IsSelected = false }
            };
        }

        /// <summary>
        /// Obtiene todas las puertas del modelo agrupadas por Tipo o por Ejemplar para la UI.
        /// </summary>
        public static List<DoorItemViewModel> GetDoorItems(Document doc, bool groupByInstance = false)
        {
            return GetDoorItems(doc, groupByInstance, BuiltInCategory.OST_Doors);
        }

        /// <summary>
        /// Obtiene todas las puertas o ventanas del modelo agrupadas por Tipo o por Ejemplar para la UI.
        /// </summary>
        public static List<DoorItemViewModel> GetDoorItems(Document doc, bool groupByInstance, BuiltInCategory category)
        {
            var result = new List<DoorItemViewModel>();

            var elements = new FilteredElementCollector(doc)
                .OfCategory(category)
                .WhereElementIsNotElementType()
                .Cast<FamilyInstance>()
                .ToList();

            if (elements.Count == 0) return result;

            string defaultFam = category == BuiltInCategory.OST_Windows ? "Ventana" : "Puerta";

            if (groupByInstance)
            {
                foreach (var d in elements)
                {
                    string mark = d.get_Parameter(BuiltInParameter.ALL_MODEL_MARK)?.AsString() ?? "";
                    if (string.IsNullOrWhiteSpace(mark))
                    {
                        mark = d.LookupParameter("Marca")?.AsString() ?? "";
                    }
                    if (string.IsNullOrWhiteSpace(mark))
                    {
                        mark = d.Symbol?.get_Parameter(BuiltInParameter.ALL_MODEL_TYPE_MARK)?.AsString() ?? "";
                    }
                    if (string.IsNullOrWhiteSpace(mark))
                    {
                        mark = d.Symbol?.LookupParameter("Marca de tipo")?.AsString() ?? "";
                    }

                    string famName = d.Symbol?.FamilyName ?? defaultFam;
                    string typeName = d.Symbol?.Name ?? "Tipo";
                    string lvlName = doc.GetElement(d.LevelId)?.Name ?? "";
                    string dims = GetDoorDimensionsString(doc, d);

                    bool inAss = d.AssemblyInstanceId != ElementId.InvalidElementId;
                    string assName = "";
                    if (inAss && doc.GetElement(d.AssemblyInstanceId) is AssemblyInstance ai)
                    {
                        assName = ai.AssemblyTypeName ?? ai.Name;
                    }

                    result.Add(new DoorItemViewModel
                    {
                        Id = d.Id,
                        TypeId = d.GetTypeId(),
                        Mark = mark,
                        FamilyName = famName,
                        TypeName = typeName,
                        LevelName = lvlName,
                        Dimensions = dims,
                        InstanceCount = 1,
                        AlreadyInAssembly = inAss,
                        AssemblyName = assName,
                        AssemblyId = d.AssemblyInstanceId,
                        MemberElementIds = new List<ElementId> { d.Id },
                        IsSelected = false
                    });
                }
            }
            else
            {
                // Agrupado por Tipo de Familia
                var groups = elements.GroupBy(d => d.GetTypeId()).ToList();

                foreach (var g in groups)
                {
                    var first = g.First();
                    string famName = first.Symbol?.FamilyName ?? defaultFam;
                    string typeName = first.Symbol?.Name ?? "Tipo";
                    string typeMark = first.Symbol?.get_Parameter(BuiltInParameter.ALL_MODEL_TYPE_MARK)?.AsString() ?? "";
                    if (string.IsNullOrWhiteSpace(typeMark))
                    {
                        typeMark = first.Symbol?.LookupParameter("Marca de tipo")?.AsString() ?? "";
                    }
                    if (string.IsNullOrWhiteSpace(typeMark))
                    {
                        var marks = g.Select(x => x.get_Parameter(BuiltInParameter.ALL_MODEL_MARK)?.AsString() ?? x.LookupParameter("Marca")?.AsString())
                                     .Where(m => !string.IsNullOrWhiteSpace(m))
                                     .Distinct()
                                     .ToList();
                        if (marks.Count == 1)
                        {
                            typeMark = marks.First()!;
                        }
                    }

                    string dims = GetDoorDimensionsString(doc, first);

                    var inAssemblyInstances = g.Where(d => d.AssemblyInstanceId != ElementId.InvalidElementId).ToList();
                    bool allInAss = inAssemblyInstances.Count == g.Count();
                    bool hasAnyInAss = inAssemblyInstances.Count > 0;
                    string assName = "";
                    ElementId assId = ElementId.InvalidElementId;

                    if (hasAnyInAss)
                    {
                        var firstAss = doc.GetElement(inAssemblyInstances.First().AssemblyInstanceId) as AssemblyInstance;
                        if (firstAss != null)
                        {
                            assName = firstAss.AssemblyTypeName ?? firstAss.Name;
                            assId = firstAss.Id;
                        }
                    }

                    result.Add(new DoorItemViewModel
                    {
                        Id = first.Id,
                        TypeId = g.Key,
                        Mark = typeMark,
                        FamilyName = famName,
                        TypeName = typeName,
                        LevelName = $"{g.Count()} ejemplar(es)",
                        Dimensions = dims,
                        InstanceCount = g.Count(),
                        AlreadyInAssembly = hasAnyInAss,
                        AssemblyName = assName,
                        AssemblyId = assId,
                        MemberElementIds = g.Select(d => d.Id).ToList(),
                        IsSelected = false
                    });
                }
            }

            return result.OrderBy(r => r.FamilyName).ThenBy(r => r.TypeName).ToList();
        }

        private static string GetDoorDimensionsString(Document doc, FamilyInstance d)
        {
            try
            {
                var pW = d.Symbol?.get_Parameter(BuiltInParameter.DOOR_WIDTH) 
                    ?? d.get_Parameter(BuiltInParameter.DOOR_WIDTH)
                    ?? d.Symbol?.get_Parameter(BuiltInParameter.WINDOW_WIDTH)
                    ?? d.get_Parameter(BuiltInParameter.WINDOW_WIDTH)
                    ?? d.Symbol?.get_Parameter(BuiltInParameter.GENERIC_WIDTH)
                    ?? d.get_Parameter(BuiltInParameter.GENERIC_WIDTH)
                    ?? d.LookupParameter("Width") ?? d.LookupParameter("Anchura") ?? d.LookupParameter("Ancho")
                    ?? d.Symbol?.LookupParameter("Width") ?? d.Symbol?.LookupParameter("Anchura") ?? d.Symbol?.LookupParameter("Ancho");

                var pH = d.Symbol?.get_Parameter(BuiltInParameter.DOOR_HEIGHT) 
                    ?? d.get_Parameter(BuiltInParameter.DOOR_HEIGHT)
                    ?? d.Symbol?.get_Parameter(BuiltInParameter.WINDOW_HEIGHT)
                    ?? d.get_Parameter(BuiltInParameter.WINDOW_HEIGHT)
                    ?? d.Symbol?.get_Parameter(BuiltInParameter.GENERIC_HEIGHT)
                    ?? d.get_Parameter(BuiltInParameter.GENERIC_HEIGHT)
                    ?? d.LookupParameter("Height") ?? d.LookupParameter("Altura") ?? d.LookupParameter("Alto")
                    ?? d.Symbol?.LookupParameter("Height") ?? d.Symbol?.LookupParameter("Altura") ?? d.Symbol?.LookupParameter("Alto");

                if (pW != null && pH != null)
                {
                    double w = pW.AsDouble() * 0.3048; // A metros
                    double h = pH.AsDouble() * 0.3048;
                    return $"{w:0.00} x {h:0.00} m";
                }
            }
            catch { }
            return "-";
        }

        /// <summary>
        /// Crea o actualiza en Revit el Cuadro Maestro de Puertas o Ventanas (Schedule) personalizado.
        /// </summary>
        public static (ViewSchedule? schedule, string message) CreateOrUpdateDoorSchedule(
            Document doc,
            string scheduleName,
            List<DoorScheduleFieldConfig> fields,
            bool itemizeInstances = false,
            BuiltInCategory category = BuiltInCategory.OST_Doors)
        {
            string catLabel = category == BuiltInCategory.OST_Windows ? "Ventanas" : "Puertas";
            if (string.IsNullOrWhiteSpace(scheduleName))
                scheduleName = $"CUADRO GENERAL DE {catLabel.ToUpperInvariant()}";

            ElementId catId = new ElementId(category);

            using (Transaction t = new Transaction(doc, $"Crear/Configurar Cuadro de {catLabel}"))
            {
                t.Start();
                try
                {
                    // Buscar si ya existe una tabla con ese nombre
                    var existing = new FilteredElementCollector(doc)
                        .OfClass(typeof(ViewSchedule))
                        .Cast<ViewSchedule>()
                        .FirstOrDefault(s => !s.IsTemplate && s.Name.Equals(scheduleName.Trim(), StringComparison.OrdinalIgnoreCase));

                    ViewSchedule vs;
                    bool wasCreated = false;

                    if (existing != null)
                    {
                        vs = existing;
                    }
                    else
                    {
                        vs = ViewSchedule.CreateSchedule(doc, catId);
                        vs.Name = AssemblyScheduleService.GetUniqueScheduleName(doc, scheduleName.Trim());
                        wasCreated = true;
                    }

                    ScheduleDefinition def = vs.Definition;
                    def.ClearSortGroupFields();
                    def.ClearFilters();

                    // Asegurar parámetros puente para que estén disponibles en los campos planificables
                    AssemblyBridgeService.EnsureBridgeParameters(doc, category);

                    // Limpiar campos existentes
                    var oldFields = def.GetFieldOrder().ToList();
                    for (int i = oldFields.Count - 1; i >= 0; i--)
                    {
                        try { def.RemoveField(oldFields[i]); } catch { }
                    }

                    var schedulable = def.GetSchedulableFields();
                    int addedCount = 0;
                    ScheduleField? primarySort = null;

                    foreach (var cfg in fields.Where(f => f.IsSelected))
                    {
                        SchedulableField? match = null;

                        if (cfg.IsCount)
                        {
                            match = schedulable.FirstOrDefault(f => f.FieldType == ScheduleFieldType.Count);
                        }
                        else if (cfg.Key == "GlobalCount")
                        {
                            match = schedulable.FirstOrDefault(f =>
                                f.GetName(doc).Equals(AssemblyBridgeService.ParamTotalObra, StringComparison.OrdinalIgnoreCase) ||
                                f.GetName(doc).Contains("TotalTipo_Obra") ||
                                f.GetName(doc).Contains("Total en Obra"));
                        }
                        else if (cfg.Key == "Locations")
                        {
                            match = schedulable.FirstOrDefault(f =>
                                f.GetName(doc).Equals(AssemblyBridgeService.ParamUbicacionesObra, StringComparison.OrdinalIgnoreCase) ||
                                f.GetName(doc).Contains("Ubicaciones_Obra") ||
                                f.GetName(doc).Contains("Ubicaciones"));
                        }
                        else if (cfg.BuiltInParam.HasValue)
                        {
                            ElementId bipId = new ElementId(cfg.BuiltInParam.Value);
                            match = schedulable.FirstOrDefault(f => f.ParameterId == bipId);
                        }

                        if (match == null)
                        {
                            match = schedulable.FirstOrDefault(f =>
                                f.GetName(doc).IndexOf(cfg.Key, StringComparison.OrdinalIgnoreCase) >= 0 ||
                                f.GetName(doc).IndexOf(cfg.HeaderName, StringComparison.OrdinalIgnoreCase) >= 0);
                        }

                        if (match != null)
                        {
                            try
                            {
                                var sf = def.AddField(match);
                                if (!string.IsNullOrWhiteSpace(cfg.HeaderName))
                                {
                                    sf.ColumnHeading = cfg.HeaderName;
                                }

                                if (cfg.IsSortKey && primarySort == null)
                                {
                                    primarySort = sf;
                                }
                                addedCount++;
                            }
                            catch { }
                        }
                    }

                    if (primarySort != null)
                    {
                        try
                        {
                            var sort = new ScheduleSortGroupField(primarySort.FieldId, ScheduleSortOrder.Ascending)
                            {
                                ShowHeader = true,
                                ShowFooter = true,
                                ShowBlankLine = false
                            };
                            def.AddSortGroupField(sort);
                        }
                        catch { }
                    }

                    def.IsItemized = itemizeInstances;
                    def.ShowGrandTotal = true;
                    def.ShowGrandTotalCount = true;
                    def.ShowGrandTotalTitle = true;
                    def.GrandTotalTitle = "TOTAL PUERTAS";
                    def.ShowTitle = true;
                    def.ShowHeaders = true;

                    t.Commit();

                    string msg = wasCreated
                        ? $"Cuadro de Puertas '{vs.Name}' creado con {addedCount} columnas configuradas."
                        : $"Cuadro de Puertas '{vs.Name}' actualizado con {addedCount} columnas configuradas.";

                    return (vs, msg);
                }
                catch (Exception ex)
                {
                    t.RollBack();
                    return (null, $"Error al crear Cuadro de Puertas: {ex.Message}");
                }
            }
        }

        /// <summary>
        /// Descompone un nombre inicial (ej: "P-01", "PUERTA-101", "E_1") en su prefijo, número inicial y cantidad de dígitos.
        /// </summary>
        public static (string prefix, int startNumber, int digitCount) ParseNamingPattern(string? input)
        {
            if (string.IsNullOrWhiteSpace(input))
                return ("P-", 1, 2);

            input = input.Trim();

            int i = input.Length - 1;
            while (i >= 0 && char.IsDigit(input[i]))
            {
                i--;
            }

            if (i == input.Length - 1)
            {
                return (input, 1, 1);
            }

            string prefix = input.Substring(0, i + 1);
            string digitsStr = input.Substring(i + 1);

            if (int.TryParse(digitsStr, out int startNum))
            {
                return (prefix, startNum, digitsStr.Length);
            }

            return (input, 1, 1);
        }

        /// <summary>
        /// Da formato correlativo a un prefijo y número secuencial.
        /// </summary>
        public static string FormatSequentialName(string prefix, int number, int digitCount)
        {
            return $"{prefix}{number.ToString($"D{digitCount}")}";
        }

        /// <summary>
        /// Sanitiza un nombre eliminando o transformando caracteres no permitidos en Revit para nombres de tipo y familias.
        /// Caracteres prohibidos en Revit: \ : { } [ ] | ; < > ? * "
        /// </summary>
        public static string SanitizeRevitName(string name)
        {
            if (string.IsNullOrWhiteSpace(name)) return "Assembly";

            string s = name.Replace('[', '(')
                           .Replace(']', ')')
                           .Replace('{', '(')
                           .Replace('}', ')')
                           .Replace('/', '-')
                           .Replace('\\', '-')
                           .Replace(':', '-')
                           .Replace(';', '-')
                           .Replace('|', '-')
                           .Replace('<', '(')
                           .Replace('>', ')')
                           .Replace('?', ' ')
                           .Replace('*', 'x')
                           .Replace('"', '\'');

            s = string.Concat(s.Where(c => !char.IsControl(c))).Trim();
            while (s.Contains("  ")) s = s.Replace("  ", " ");

            return string.IsNullOrWhiteSpace(s) ? "Assembly" : s;
        }

        /// <summary>
        /// Obtiene un nombre de ensamblaje inteligente basado exclusivamente en los parámetros
        /// del elemento o tipo seleccionado (Familia -> Tipos: ej. P04 -> P04A, P04B; P05 -> P05A, P05B).
        /// </summary>
        public static string GetParameterBasedName(DoorItemViewModel item)
        {
            if (item == null) return "Assembly";

            string mark = item.Mark?.Trim() ?? "";
            string fam = item.FamilyName?.Trim() ?? "";
            string typ = item.TypeName?.Trim() ?? "";

            // 1. Si Mark ya tiene un código explícito (ej: "P04A", "P05a", "P05-A", "V01B")
            if (!string.IsNullOrWhiteSpace(mark))
            {
                // Si Mark es solo un sufijo de 1-2 letras/dígitos (ej: "a", "b", "1") y la Familia es un código (ej: "P05")
                if (mark.Length <= 2 && !string.IsNullOrWhiteSpace(fam))
                {
                    if (char.IsDigit(fam[fam.Length - 1]) && char.IsLetter(mark[0]))
                        return SanitizeRevitName($"{fam}{mark.ToUpperInvariant()}");
                    return SanitizeRevitName($"{fam}_{mark}");
                }
                return SanitizeRevitName(mark);
            }

            // 2. Analizar Familia y Tipo
            if (!string.IsNullOrWhiteSpace(fam) && !string.IsNullOrWhiteSpace(typ))
            {
                // Si el Tipo ya contiene o empieza con el nombre de la Familia (ej: Fam "P05", Tipo "P05A" o "P05-A")
                if (typ.StartsWith(fam, StringComparison.OrdinalIgnoreCase))
                {
                    return SanitizeRevitName(typ);
                }

                // Si el Tipo es un sufijo corto (ej: "A", "B", "C", "a", "b", "c", "01", "02")
                if (typ.Length <= 4 && !typ.Contains(" "))
                {
                    // Fam "P04" o "P05" + Tipo "A" => "P04A", "P05A"
                    if (char.IsDigit(fam[fam.Length - 1]) && char.IsLetter(typ[0]))
                    {
                        return SanitizeRevitName($"{fam}{typ.ToUpperInvariant()}");
                    }
                    if (fam.EndsWith("-") || fam.EndsWith("_"))
                    {
                        return SanitizeRevitName($"{fam}{typ.ToUpperInvariant()}");
                    }
                    return SanitizeRevitName($"{fam}_{typ}");
                }

                // Si el tipo es un nombre descriptivo pero la familia es el código (ej: Fam "P04", Tipo "Madera Batiente")
                return SanitizeRevitName($"{fam} - {typ}");
            }

            if (!string.IsNullOrWhiteSpace(typ)) return SanitizeRevitName(typ);
            if (!string.IsNullOrWhiteSpace(fam)) return SanitizeRevitName(fam);

            return "Assembly";
        }

        /// <summary>
        /// Construye el nombre deseado para el ensamblaje según las opciones de nomenclatura configuradas.
        /// </summary>
        public static string BuildAssemblyName(
            DoorItemViewModel item,
            int sequenceNumber,
            BatchAssemblyOptions options)
        {
            string mode = options.NamingMode ?? "Parameters";
            var (prefix, _, digitCount) = ParseNamingPattern(options.InitialName);
            string seqCode = FormatSequentialName(prefix, sequenceNumber, digitCount);

            string baseName;
            switch (mode)
            {
                case "Parameters":
                    baseName = GetParameterBasedName(item);
                    break;

                case "Mark":
                    baseName = !string.IsNullOrWhiteSpace(item.Mark)
                        ? item.Mark.Trim()
                        : GetParameterBasedName(item);
                    break;

                case "TypeName":
                    baseName = $"{item.FamilyName} - {item.TypeName}".Trim();
                    break;

                case "CustomPattern":
                    string pattern = string.IsNullOrWhiteSpace(options.CustomPattern) ? "{Prefix}{Seq}" : options.CustomPattern;
                    baseName = pattern
                        .Replace("{Prefix}", prefix)
                        .Replace("{Seq}", sequenceNumber.ToString($"D{digitCount}"))
                        .Replace("{Mark}", item.Mark)
                        .Replace("{Family}", item.FamilyName)
                        .Replace("{Type}", item.TypeName)
                        .Replace("{Dims}", item.Dimensions);
                    break;

                case "Sequential":
                default:
                    baseName = seqCode;
                    break;
            }

            if (options.AppendTypeName && mode != "TypeName" && mode != "Parameters")
            {
                baseName = $"{baseName} - {item.TypeName}".Trim();
            }

            if (options.AppendDimensions && !string.IsNullOrWhiteSpace(item.Dimensions) && item.Dimensions != "-")
            {
                baseName = $"{baseName} ({item.Dimensions})";
            }

            if (options.AppendInstanceCount && item.InstanceCount > 1)
            {
                baseName = $"{baseName} ({item.InstanceCount} uds)";
            }

            return SanitizeRevitName(baseName);
        }

        /// <summary>
        /// Renombra de forma segura y robusta un AssemblyInstance y su AssemblyType en Revit,
        /// evitando colisiones y garantizando que nunca quede con el nombre por defecto (ej: "Window 1", "Door 1").
        /// </summary>
        public static (bool success, string finalName) SetAssemblyTypeName(
            Document doc,
            AssemblyInstance ai,
            string desiredName,
            HashSet<string> existingTypeNames,
            List<string>? logs = null)
        {
            string sanitized = SanitizeRevitName(desiredName);

            // Obtener el tipo de ensamblaje actual del ejemplar
            ElementId typeId = ai.GetTypeId();
            AssemblyType? currentType = typeId != ElementId.InvalidElementId ? doc.GetElement(typeId) as AssemblyType : null;
            string currentTypeName = currentType?.Name ?? ai.AssemblyTypeName ?? "";

            // Si ya tiene el nombre deseado, no hacer nada más
            if (string.Equals(currentTypeName, sanitized, StringComparison.OrdinalIgnoreCase))
            {
                existingTypeNames.Add(currentTypeName);
                return (true, currentTypeName);
            }

            // Buscar si ya existe en el documento un AssemblyType con el nombre deseado
            var matchingType = new FilteredElementCollector(doc)
                .OfClass(typeof(AssemblyType))
                .Cast<AssemblyType>()
                .FirstOrDefault(at => string.Equals(at.Name, sanitized, StringComparison.OrdinalIgnoreCase));

            if (matchingType != null)
            {
                try
                {
                    ai.ChangeTypeId(matchingType.Id);
                    existingTypeNames.Add(sanitized);
                    return (true, sanitized);
                }
                catch (Exception exChange)
                {
                    logs?.Add($"  [Nomenclatura] ChangeTypeId a '{sanitized}' no aplicó: {exChange.Message}");
                }
            }

            // Intentar renombrar directamente el AssemblyType actual
            if (currentType != null)
            {
                try
                {
                    currentType.Name = sanitized;
                    existingTypeNames.Add(sanitized);
                    return (true, sanitized);
                }
                catch (Exception ex1)
                {
                    logs?.Add($"  [Nomenclatura] Renombrado de AssemblyType a '{sanitized}' falló: {ex1.Message}");
                }
            }

            // Intentar a través de la propiedad AssemblyTypeName de la instancia
            try
            {
                ai.AssemblyTypeName = sanitized;
                existingTypeNames.Add(sanitized);
                return (true, sanitized);
            }
            catch (Exception ex2)
            {
                logs?.Add($"  [Nomenclatura] ai.AssemblyTypeName a '{sanitized}' falló: {ex2.Message}");
            }

            // Resolver con sufijo único para evitar colisión si el nombre ya existe y pertenece a otra geometría
            string uniqueCandidate = EnsureUniqueAssemblyTypeName(sanitized, existingTypeNames);
            if (!string.Equals(uniqueCandidate, sanitized, StringComparison.OrdinalIgnoreCase))
            {
                if (currentType != null)
                {
                    try
                    {
                        currentType.Name = uniqueCandidate;
                        existingTypeNames.Add(uniqueCandidate);
                        return (true, uniqueCandidate);
                    }
                    catch { }
                }

                try
                {
                    ai.AssemblyTypeName = uniqueCandidate;
                    existingTypeNames.Add(uniqueCandidate);
                    return (true, uniqueCandidate);
                }
                catch { }
            }

            return (false, ai.AssemblyTypeName ?? ai.Name);
        }

        private static string EnsureUniqueAssemblyTypeName(string baseName, HashSet<string> existingNames)
        {
            if (!existingNames.Contains(baseName))
                return baseName;

            int count = 1;
            while (existingNames.Contains($"{baseName}_{count}"))
            {
                count++;
            }
            return $"{baseName}_{count}";
        }

        /// <summary>
        /// Sobrecarga compatible para crear ensamblajes en lote por defecto.
        /// </summary>
        public static (List<AssemblyInstance> assemblies, List<string> logs) CreateBatchDoorAssemblies(
            Document doc,
            IList<DoorItemViewModel> doorItems,
            bool groupByInstance = false)
        {
            return CreateBatchDoorAssemblies(doc, doorItems, new BatchAssemblyOptions { GroupByInstance = groupByInstance });
        }

        /// <summary>
        /// Crea ensamblajes en lote (Batch Assemblies) para las puertas seleccionadas en la interfaz
        /// con soporte para conteo secuencial personalizado y control de instancias por tipo.
        /// </summary>
        public static (List<AssemblyInstance> assemblies, List<string> logs) CreateBatchDoorAssemblies(
            Document doc,
            IList<DoorItemViewModel> doorItems,
            BatchAssemblyOptions options)
        {
            options ??= new BatchAssemblyOptions();
            var created = new List<AssemblyInstance>();
            var logs = new List<string>();

            if (doorItems == null || doorItems.Count == 0)
            {
                logs.Add("No hay puertas seleccionadas para ensamblar.");
                return (created, logs);
            }

            ElementId namingCatId = new ElementId(options.Category);
            string catLabel = options.Category == BuiltInCategory.OST_Windows ? "Ventanas" : "Puertas";

            // Analizar patrón de nombres y contador
            var (prefix, startNumber, digitCount) = ParseNamingPattern(options.InitialName);
            int currentNumber = startNumber;

            // Conjunto de nombres de tipo de ensamblaje existentes para evitar colisiones en Revit
            var existingAssemblyTypeNames = new FilteredElementCollector(doc)
                .OfClass(typeof(AssemblyType))
                .Cast<AssemblyType>()
                .Select(at => at.Name)
                .ToHashSet(StringComparer.OrdinalIgnoreCase);

            using (Transaction t = new Transaction(doc, $"Creación Batch de Ensamblajes de {catLabel}"))
            {
                t.Start();

                foreach (var item in doorItems)
                {
                    if (item == null) continue;

                    string desiredName = BuildAssemblyName(item, currentNumber, options);

                    // 1. Verificar si los miembros ya pertenecen a un ensamblaje en Revit
                    var existingAssIds = item.MemberElementIds
                        .Select(id => doc.GetElement(id)?.AssemblyInstanceId ?? ElementId.InvalidElementId)
                        .Where(id => id != ElementId.InvalidElementId)
                        .Distinct()
                        .ToList();

                    if (existingAssIds.Count > 0)
                    {
                        bool anyReused = false;
                        foreach (var assId in existingAssIds)
                        {
                            if (doc.GetElement(assId) is AssemblyInstance existingAi)
                            {
                                var (renamed, finalName) = SetAssemblyTypeName(doc, existingAi, desiredName, existingAssemblyTypeNames, logs);
                                if (!created.Any(a => a.Id == existingAi.Id))
                                {
                                    created.Add(existingAi);
                                }
                                logs.Add($"Ensamblaje existente ID {existingAi.Id} ('{finalName}') sincronizado para generar vistas/cotas.");
                                anyReused = true;
                            }
                        }

                        if (anyReused)
                        {
                            currentNumber++;
                            continue;
                        }
                    }

                    // 2. Si no pertenecen a un ensamblaje, filtrar miembros válidos para crearlo
                    var validMemberIds = item.MemberElementIds
                        .Where(id =>
                        {
                            var elem = doc.GetElement(id);
                            return elem != null && elem.AssemblyInstanceId == ElementId.InvalidElementId;
                        })
                        .ToList();

                    if (validMemberIds.Count == 0)
                    {
                        logs.Add($"Elemento '{item.DisplayTitle}': No tiene ejemplares libres para ensamblar.");
                        continue;
                    }

                    if (options.GroupByInstance)
                    {
                        // Un ensamblaje por cada ejemplar individual
                        foreach (var mId in validMemberIds)
                        {
                            try
                            {
                                var ai = AssemblyInstance.Create(doc, new List<ElementId> { mId }, namingCatId);
                                if (ai != null)
                                {
                                    string instDesiredName = BuildAssemblyName(item, currentNumber++, options);
                                    var (renamed, finalName) = SetAssemblyTypeName(doc, ai, instDesiredName, existingAssemblyTypeNames, logs);

                                    created.Add(ai);
                                    logs.Add($"Ensamblaje creado con éxito: '{finalName}' (ID {ai.Id}).");
                                }
                            }
                            catch (Exception ex)
                            {
                                logs.Add($"Error al crear ensamblaje individual para ID {mId}: {ex.Message}");
                            }
                        }
                    }
                    else
                    {
                        // Modo por Tipo de Puerta:
                        // Si SingleInstancePerType es true -> solo 1 ejemplar de muestra en el ensamblaje.
                        // Si SingleInstancePerType es false -> todas las instancias disponibles del tipo agrupadas en el ensamblaje.
                        try
                        {
                            var membersToAssemble = options.SingleInstancePerType
                                ? new List<ElementId> { validMemberIds.First() }
                                : validMemberIds;

                            var ai = AssemblyInstance.Create(doc, membersToAssemble, namingCatId);
                            if (ai != null)
                            {
                                var (renamed, finalName) = SetAssemblyTypeName(doc, ai, desiredName, existingAssemblyTypeNames, logs);

                                created.Add(ai);
                                string modeDesc = options.SingleInstancePerType
                                    ? "1 ejemplar representativo"
                                    : $"{membersToAssemble.Count} elementos agrupados";
                                logs.Add($"Ensamblaje de Tipo creado ({modeDesc}): '{finalName}' (Total en proyecto: {item.InstanceCount} uds).");
                                currentNumber++;
                            }
                        }
                        catch (Exception ex)
                        {
                            logs.Add($"Error al crear ensamblaje para '{item.DisplayTitle}': {ex.Message}");
                        }
                    }
                }

                t.Commit();
            }

            // Sincronizar automáticamente los datos globales en los elementos de los ensamblajes creados
            if (created.Count > 0)
            {
                var (syncedCount, syncLogs) = AssemblyBridgeService.SyncBridgeData(doc, options.Category);
                logs.AddRange(syncLogs);
            }

            return (created, logs);
        }

        /// <summary>
        /// Genera vistas en lote (Batch Assembly Views) para los ensamblajes seleccionados.
        /// Crea Planta, Elevaciones, Isométrica 3D y Tabla de Despiece para cada ensamblaje.
        /// </summary>
        public static (int createdViewsCount, List<string> logs) CreateBatchAssemblyViews(
            Document doc,
            IList<AssemblyInstance> assemblies,
            AssemblyBatchViewOptions options,
            ViewSchedule? sourceTemplateSchedule = null)
        {
            int createdCount = 0;
            var logs = new List<string>();

            if (assemblies == null || assemblies.Count == 0)
            {
                logs.Add("No se especificaron ensamblajes para generar vistas.");
                return (0, logs);
            }

            ElementId targetCatId = options.CategoryId ?? new ElementId(BuiltInCategory.OST_Doors);

            using (Transaction t = new Transaction(doc, "Generación Batch de Vistas de Ensamblaje"))
            {
                t.Start();

                foreach (var inputAi in assemblies)
                {
                    if (inputAi == null || !inputAi.IsValidObject) continue;

                    var ai = inputAi;
                    string aName = ai.AssemblyTypeName ?? ai.Name;

                    if (!ai.AllowsAssemblyViewCreation())
                    {
                        var primaryInstance = new FilteredElementCollector(doc)
                            .OfClass(typeof(AssemblyInstance))
                            .Cast<AssemblyInstance>()
                            .FirstOrDefault(other => other.GetTypeId() == ai.GetTypeId() && other.AllowsAssemblyViewCreation());

                        if (primaryInstance != null)
                        {
                            ai = primaryInstance;
                            aName = ai.AssemblyTypeName ?? ai.Name;
                        }
                    }

                    if (!ai.AllowsAssemblyViewCreation())
                    {
                        logs.Add($"El ensamblaje '{aName}' ya tiene vistas generadas o su tipo no permite vistas adicionales.");
                        continue;
                    }

                    // 1. Tabla de Despiece de Ensamblaje
                    if (options.CreateSchedule)
                    {
                        try
                        {
                            ViewSchedule? sched = null;
                            try
                            {
                                sched = AssemblyViewUtils.CreateSingleCategorySchedule(doc, ai.Id, targetCatId);
                            }
                            catch
                            {
                                sched = AssemblyViewUtils.CreatePartList(doc, ai.Id);
                            }

                            if (sched != null)
                            {
                                string sName = $"{aName} - {options.ScheduleNameTemplate}";
                                sched.Name = AssemblyScheduleService.GetUniqueScheduleName(doc, sName);

                                if (sourceTemplateSchedule != null && sourceTemplateSchedule.IsValidObject)
                                {
                                    // Replicar formato y campos de la tabla maestra
                                    var snap = AssemblyScheduleService.TakeSnapshot(doc, sourceTemplateSchedule);
                                    AssemblyScheduleService.ApplySnapshotToSchedule(
                                        doc,
                                        sched,
                                        snap,
                                        replicateFormat: true,
                                        replicateSorting: true,
                                        replicateTotals: true,
                                        replicateFilters: false, // Sin filtros para no ocultar las piezas
                                        replicateTemplate: true);
                                }

                                // Asegurar que la tabla del ensamblaje incluya el parámetro puente con el conteo global
                                AssemblyBridgeService.InjectBridgeFieldToSchedule(doc, sched);

                                createdCount++;
                                logs.Add($"  [VISTA] Tabla creada: '{sched.Name}'");
                            }
                        }
                        catch (Exception ex)
                        {
                            logs.Add($"  [AVISO] No se pudo crear tabla para '{aName}': {ex.Message}");
                        }
                    }

                    int viewScale = options.ViewScale > 0 ? options.ViewScale : 20;

                    // 2. Vista 3D Isométrica
                    if (options.Create3DOrthographic)
                    {
                        try
                        {
                            var v3d = AssemblyViewUtils.Create3DOrthographic(doc, ai.Id);
                            if (v3d != null)
                            {
                                v3d.Name = AssemblyScheduleService.GetUniqueScheduleName(doc, $"{aName} - Vista 3D");
                                try { v3d.Scale = viewScale; } catch { }
                                if (options.ViewTemplateId != null && options.ViewTemplateId != ElementId.InvalidElementId)
                                {
                                    try { v3d.ViewTemplateId = options.ViewTemplateId; } catch { }
                                }
                                createdCount++;
                                logs.Add($"  [VISTA] 3D Isométrica creada (1:{v3d.Scale}): '{v3d.Name}'");
                            }
                        }
                        catch { }
                    }

                    // 3. Vista de Planta (Detalle Horizontal)
                    if (options.CreatePlan)
                    {
                        try
                        {
                            var vPlan = AssemblyViewUtils.CreateDetailSection(doc, ai.Id, AssemblyDetailViewOrientation.HorizontalDetail);
                            if (vPlan != null)
                            {
                                vPlan.Name = AssemblyScheduleService.GetUniqueScheduleName(doc, $"{aName} - Planta de Detalle");
                                try { vPlan.Scale = viewScale; } catch { }
                                if (options.ViewTemplateId != null && options.ViewTemplateId != ElementId.InvalidElementId)
                                {
                                    try { vPlan.ViewTemplateId = options.ViewTemplateId; } catch { }
                                }
                                createdCount++;
                                logs.Add($"  [VISTA] Planta de Detalle creada (1:{vPlan.Scale}): '{vPlan.Name}'");
                            }
                        }
                        catch { }
                    }

                    // 4. Elevación Frontal
                    if (options.CreateFrontElevation)
                    {
                        try
                        {
                            var vFront = AssemblyViewUtils.CreateDetailSection(doc, ai.Id, AssemblyDetailViewOrientation.ElevationFront);
                            if (vFront != null)
                            {
                                vFront.Name = AssemblyScheduleService.GetUniqueScheduleName(doc, $"{aName} - Elevación Frontal");
                                try { vFront.Scale = viewScale; } catch { }
                                if (options.ViewTemplateId != null && options.ViewTemplateId != ElementId.InvalidElementId)
                                {
                                    try { vFront.ViewTemplateId = options.ViewTemplateId; } catch { }
                                }
                                createdCount++;
                                logs.Add($"  [VISTA] Elevación Frontal creada (1:{vFront.Scale}): '{vFront.Name}'");
                            }
                        }
                        catch { }
                    }

                    // 5. Elevación Posterior
                    if (options.CreateBackElevation)
                    {
                        try
                        {
                            var vBack = AssemblyViewUtils.CreateDetailSection(doc, ai.Id, AssemblyDetailViewOrientation.ElevationBack);
                            if (vBack != null)
                            {
                                vBack.Name = AssemblyScheduleService.GetUniqueScheduleName(doc, $"{aName} - Elevación Posterior");
                                try { vBack.Scale = viewScale; } catch { }
                                if (options.ViewTemplateId != null && options.ViewTemplateId != ElementId.InvalidElementId)
                                {
                                    try { vBack.ViewTemplateId = options.ViewTemplateId; } catch { }
                                }
                                createdCount++;
                                logs.Add($"  [VISTA] Elevación Posterior creada (1:{vBack.Scale}): '{vBack.Name}'");
                            }
                        }
                        catch { }
                    }

                    // 6. Sección Transversal
                    if (options.CreateSection)
                    {
                        try
                        {
                            var vSec = AssemblyViewUtils.CreateDetailSection(doc, ai.Id, AssemblyDetailViewOrientation.ElevationLeft);
                            if (vSec != null)
                            {
                                vSec.Name = AssemblyScheduleService.GetUniqueScheduleName(doc, $"{aName} - Sección Lateral");
                                try { vSec.Scale = viewScale; } catch { }
                                if (options.ViewTemplateId != null && options.ViewTemplateId != ElementId.InvalidElementId)
                                {
                                    try { vSec.ViewTemplateId = options.ViewTemplateId; } catch { }
                                }
                                createdCount++;
                                logs.Add($"  [VISTA] Sección creada (1:{vSec.Scale}): '{vSec.Name}'");
                            }
                        }
                        catch { }
                    }
                }

                t.Commit();
            }

            // Auto-acotado inteligente con el Ambientador de Puertas
            if (options.AutoDimensionViews && assemblies.Count > 0)
            {
                logs.Add("\n--- ACOTADO AUTOMÁTICO DE VISTAS TÉCNICAS (AMBIENTADOR) ---");
                foreach (var ai in assemblies)
                {
                    if (ai == null || !ai.IsValidObject) continue;
                    try
                    {
                        var (dims, dimLogs) = DoorAssemblyDimensionService.DimensionAssembly(doc, ai);
                        if (dims > 0)
                        {
                            logs.Add($"  [COTAS] {dims} cotas añadidas al ensamblaje '{ai.AssemblyTypeName ?? ai.Name}'.");
                        }
                    }
                    catch { }
                }
            }

            return (createdCount, logs);
        }
    }
}
