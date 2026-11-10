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
        public BuiltInCategory Category { get; set; } = BuiltInCategory.OST_Doors;
        public string AssemblyCode { get; set; } = string.Empty;
        public string TypeMark { get; set; } = string.Empty;
        public string Mark { get; set; } = string.Empty;
        public string Description { get; set; } = string.Empty;
        public string FamilyName { get; set; } = string.Empty;
        public string TypeName { get; set; } = string.Empty;
        public string LevelName { get; set; } = string.Empty;
        public string Dimensions { get; set; } = string.Empty;
        public int InstanceCount { get; set; } = 1;
        public List<ElementId> MemberElementIds { get; set; } = new List<ElementId>();

        private bool _alreadyInAssembly;
        public bool AlreadyInAssembly
        {
            get => _alreadyInAssembly;
            set
            {
                if (_alreadyInAssembly != value)
                {
                    _alreadyInAssembly = value;
                    OnPropertyChanged();
                    OnPropertyChanged(nameof(IsAssemblyOn));
                    OnPropertyChanged(nameof(StatusBadge));
                    OnPropertyChanged(nameof(StatusColor));
                    OnPropertyChanged(nameof(SwitchLabel));
                    OnPropertyChanged(nameof(SwitchBackground));
                }
            }
        }

        public bool IsAssemblyOn
        {
            get => _alreadyInAssembly;
            set => AlreadyInAssembly = value;
        }

        private string _assemblyName = string.Empty;
        public string AssemblyName
        {
            get => _assemblyName;
            set
            {
                if (_assemblyName != value)
                {
                    _assemblyName = value;
                    OnPropertyChanged();
                    OnPropertyChanged(nameof(StatusBadge));
                    OnPropertyChanged(nameof(TargetAssemblyName));
                }
            }
        }

        public ElementId AssemblyId { get; set; } = ElementId.InvalidElementId;

        public string ComputedCode => DoorAssemblyManagerService.GetParameterBasedName(this);
        public string StatusBadge => AlreadyInAssembly ? $"🟢 ON ({(string.IsNullOrWhiteSpace(AssemblyName) ? ComputedCode : AssemblyName)})" : "⚪ OFF (Sin Assembly)";
        public string StatusColor => AlreadyInAssembly ? "#059669" : "#6B7280";
        public string SwitchLabel => AlreadyInAssembly ? "ON" : "OFF";
        public string SwitchBackground => AlreadyInAssembly ? "#059669" : "#9CA3AF";
        public string DisplayTitle => $"{ComputedCode}  —  {FamilyName} : {TypeName}";
        public string FormulaSubtitle => $"Assembly Code: '{(string.IsNullOrWhiteSpace(AssemblyCode) ? "—" : AssemblyCode)}'  +  Type Mark: '{(string.IsNullOrWhiteSpace(TypeMark) ? "—" : TypeMark)}'  ➔  {ComputedCode}";
        public string DescriptionInfo => string.IsNullOrWhiteSpace(Description) ? $"1 ejemplar para Assembly (Total en tabla: {InstanceCount} uds)" : $"{Description}  |  1 ud para Assembly ({InstanceCount} en tabla)";

        private string _targetAssemblyName = string.Empty;
        public string TargetAssemblyName
        {
            get
            {
                if (!string.IsNullOrWhiteSpace(_targetAssemblyName))
                    return _targetAssemblyName;
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
        public bool GroupByCode { get; set; } = false; // False = un ensamblaje por cada Familia+Tipo (P01-A, P01-B, P04-A...) con 1 sola instancia
        public bool SingleInstancePerType { get; set; } = true;
        public string InitialName { get; set; } = "P-01";
        public bool AppendTypeName { get; set; } = false;
        public bool AppendDimensions { get; set; } = false;
        public bool AppendInstanceCount { get; set; } = false;
        public string NamingMode { get; set; } = "Parameters"; // "Parameters", "Sequential", "Mark", "TypeName", "CustomPattern"
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
                new DoorScheduleFieldConfig { Key = "Family", HeaderName = "Family", BuiltInParam = BuiltInParameter.ELEM_FAMILY_PARAM, IsSelected = true },
                new DoorScheduleFieldConfig { Key = "Type", HeaderName = "Type", BuiltInParam = BuiltInParameter.ELEM_TYPE_PARAM, IsSelected = true },
                new DoorScheduleFieldConfig { Key = "AssemblyCode", HeaderName = "Assembly Code", BuiltInParam = (BuiltInParameter)(-1002504), IsSelected = true, IsSortKey = true, SortAscending = true },
                new DoorScheduleFieldConfig { Key = "TypeMark", HeaderName = "Type Mark", BuiltInParam = BuiltInParameter.ALL_MODEL_TYPE_MARK, IsSelected = true },
                new DoorScheduleFieldConfig { Key = "Mark", HeaderName = "Mark", BuiltInParam = BuiltInParameter.ALL_MODEL_MARK, IsSelected = true },
                new DoorScheduleFieldConfig { Key = "Description", HeaderName = "Description", BuiltInParam = BuiltInParameter.ALL_MODEL_DESCRIPTION, IsSelected = true },
                new DoorScheduleFieldConfig { Key = "Width", HeaderName = "Anchura (m)", BuiltInParam = isWindow ? BuiltInParameter.WINDOW_WIDTH : BuiltInParameter.DOOR_WIDTH, IsSelected = true },
                new DoorScheduleFieldConfig { Key = "Height", HeaderName = "Altura (m)", BuiltInParam = isWindow ? BuiltInParameter.WINDOW_HEIGHT : BuiltInParameter.DOOR_HEIGHT, IsSelected = true },
                new DoorScheduleFieldConfig { Key = "Count", HeaderName = "Cantidad (Local)", IsCount = true, IsSelected = true },
                new DoorScheduleFieldConfig { Key = "GlobalCount", HeaderName = "Total en Obra (Global)", BuiltInParam = null, IsSelected = true },
                new DoorScheduleFieldConfig { Key = "Level", HeaderName = "Nivel / Ubicación", BuiltInParam = BuiltInParameter.FAMILY_LEVEL_PARAM, IsSelected = true },
                new DoorScheduleFieldConfig { Key = "Locations", HeaderName = "Resumen Ubicaciones", BuiltInParam = null, IsSelected = false },
                new DoorScheduleFieldConfig { Key = "Comments", HeaderName = "Observaciones", BuiltInParam = BuiltInParameter.ALL_MODEL_INSTANCE_COMMENTS, IsSelected = true }
            };
        }

        public static string ReadAssemblyCodeFromElement(Document doc, FamilyInstance d)
        {
            BuiltInParameter bipAssCode = (BuiltInParameter)(-1002504);
            string? val = d.Symbol?.get_Parameter(bipAssCode)?.AsString()
                ?? d.Symbol?.LookupParameter("Assembly Code")?.AsString()
                ?? d.Symbol?.LookupParameter("Código de montaje")?.AsString()
                ?? d.Symbol?.LookupParameter("Código de ensamblaje")?.AsString()
                ?? d.get_Parameter(bipAssCode)?.AsString()
                ?? d.LookupParameter("Assembly Code")?.AsString();
            return val?.Trim() ?? string.Empty;
        }

        public static string ReadTypeMarkFromElement(FamilyInstance d)
        {
            string? val = d.Symbol?.get_Parameter(BuiltInParameter.ALL_MODEL_TYPE_MARK)?.AsString()
                ?? d.Symbol?.LookupParameter("Type Mark")?.AsString()
                ?? d.Symbol?.LookupParameter("Marca de tipo")?.AsString();
            return val?.Trim() ?? string.Empty;
        }

        public static string ReadDescriptionFromElement(FamilyInstance d)
        {
            string? val = d.Symbol?.get_Parameter(BuiltInParameter.ALL_MODEL_DESCRIPTION)?.AsString()
                ?? d.Symbol?.LookupParameter("Description")?.AsString()
                ?? d.Symbol?.LookupParameter("Descripción")?.AsString()
                ?? d.get_Parameter(BuiltInParameter.ALL_MODEL_DESCRIPTION)?.AsString();
            return val?.Trim() ?? string.Empty;
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

            return BuildDoorItemViewModels(doc, elements, groupByInstance, category);
        }

        public static List<DoorItemViewModel> BuildDoorItemViewModels(
            Document doc,
            IList<FamilyInstance> elements,
            bool groupByInstance,
            BuiltInCategory category)
        {
            var result = new List<DoorItemViewModel>();
            if (elements == null || elements.Count == 0) return result;

            string defaultFam = category == BuiltInCategory.OST_Windows ? "Ventana" : "Puerta";

            if (groupByInstance)
            {
                foreach (var d in elements)
                {
                    string assCode = ReadAssemblyCodeFromElement(doc, d);
                    string typeMark = ReadTypeMarkFromElement(d);
                    string desc = ReadDescriptionFromElement(d);
                    string mark = d.get_Parameter(BuiltInParameter.ALL_MODEL_MARK)?.AsString()
                        ?? d.LookupParameter("Marca")?.AsString()
                        ?? typeMark;

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
                        Category = category,
                        AssemblyCode = assCode,
                        TypeMark = typeMark,
                        Description = desc,
                        Mark = mark ?? "",
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
                // Agrupado por Familia + Tipo
                var groups = elements.GroupBy(d => d.GetTypeId()).ToList();

                foreach (var g in groups)
                {
                    var first = g.First();
                    string famName = first.Symbol?.FamilyName ?? defaultFam;
                    string typeName = first.Symbol?.Name ?? "Tipo";
                    string assCode = ReadAssemblyCodeFromElement(doc, first);
                    string typeMark = ReadTypeMarkFromElement(first);
                    string desc = ReadDescriptionFromElement(first);

                    string mark = "";
                    var marks = g.Select(x => x.get_Parameter(BuiltInParameter.ALL_MODEL_MARK)?.AsString() ?? x.LookupParameter("Marca")?.AsString())
                                 .Where(m => !string.IsNullOrWhiteSpace(m))
                                 .Distinct()
                                 .ToList();
                    if (marks.Count == 1)
                    {
                        mark = marks.First()!;
                    }
                    else if (!string.IsNullOrWhiteSpace(assCode) && !string.IsNullOrWhiteSpace(typeMark))
                    {
                        mark = $"{assCode}-{typeMark}";
                    }
                    else
                    {
                        mark = typeMark;
                    }

                    string dims = GetDoorDimensionsString(doc, first);

                    var inAssemblyInstances = g.Where(d => d.AssemblyInstanceId != ElementId.InvalidElementId).ToList();
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
                        Category = category,
                        AssemblyCode = assCode,
                        TypeMark = typeMark,
                        Description = desc,
                        Mark = mark,
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

            return result.OrderBy(r => r.ComputedCode).ThenBy(r => r.FamilyName).ThenBy(r => r.TypeName).ToList();
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
        /// Genera el siguiente nombre correlativo tal como lo hace Revit para marcas, vistas y ensamblajes:
        /// - Si termina en número, incrementa el número manteniendo ceros a la izquierda (P-01 -> P-02, V100 -> V101).
        /// - Si termina en letra (sola, tras separador o tras dígito), incrementa la letra alfabéticamente (P-A -> P-B, P-Z -> P-AA, P04A -> P04B).
        /// - Si termina en una palabra completa sin índice numérico/alfabético, añade " 1" (Puerta -> Puerta 1 -> Puerta 2).
        /// </summary>
        public static string GetNextRevitName(string currentName)
        {
            if (string.IsNullOrWhiteSpace(currentName)) return "1";
            currentName = currentName.Trim();

            // 1. Revisar si termina en dígitos
            int i = currentName.Length - 1;
            while (i >= 0 && char.IsDigit(currentName[i]))
            {
                i--;
            }

            if (i < currentName.Length - 1)
            {
                string prefix = currentName.Substring(0, i + 1);
                string numStr = currentName.Substring(i + 1);
                if (long.TryParse(numStr, out long val))
                {
                    long nextVal = val + 1;
                    string nextNumStr = nextVal.ToString().PadLeft(numStr.Length, '0');
                    return prefix + nextNumStr;
                }
            }

            // 2. Revisar si termina en letra(s) corta(s) (1 o 2 letras tras separador o dígito, o todo el texto es 1-3 letras)
            int j = currentName.Length - 1;
            while (j >= 0 && char.IsLetter(currentName[j]))
            {
                j--;
            }

            int letterCount = (currentName.Length - 1) - j;
            if (letterCount > 0)
            {
                bool isEntireString = (j < 0);
                bool hasSeparator = j >= 0 && (currentName[j] == '-' || currentName[j] == '_' || currentName[j] == ' ' || currentName[j] == '.' || currentName[j] == '/' || char.IsDigit(currentName[j]));

                if ((letterCount <= 2 && hasSeparator) || (isEntireString && letterCount <= 3))
                {
                    string prefix = currentName.Substring(0, j + 1);
                    string letters = currentName.Substring(j + 1);
                    string nextLetters = IncrementLetterSequence(letters);
                    return prefix + nextLetters;
                }
            }

            // 3. Palabra sin número ni letra índice -> "Puerta" -> "Puerta 1"
            return $"{currentName} 1";
        }

        private static string IncrementLetterSequence(string letters)
        {
            char[] chars = letters.ToCharArray();
            bool carry = true;
            for (int k = chars.Length - 1; k >= 0 && carry; k--)
            {
                char c = chars[k];
                if (c >= 'A' && c <= 'Z')
                {
                    if (c == 'Z')
                    {
                        chars[k] = 'A';
                        carry = true;
                    }
                    else
                    {
                        chars[k] = (char)(c + 1);
                        carry = false;
                    }
                }
                else if (c >= 'a' && c <= 'z')
                {
                    if (c == 'z')
                    {
                        chars[k] = 'a';
                        carry = true;
                    }
                    else
                    {
                        chars[k] = (char)(c + 1);
                        carry = false;
                    }
                }
                else
                {
                    break;
                }
            }

            string res = new string(chars);
            if (carry)
            {
                char firstChar = char.IsUpper(chars[0]) ? 'A' : 'a';
                res = firstChar + res;
            }
            return res;
        }

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
        /// Normaliza un código para que siempre lleve guion entre el código base y la letra de variante:
        /// "P04A" -> "P04-A", "P04_a" -> "P04-A", "P04-A" -> "P04-A".
        /// </summary>
        public static string NormalizeCodeWithHyphen(string raw)
        {
            if (string.IsNullOrWhiteSpace(raw)) return string.Empty;
            string trimmed = raw.Trim();
            var m = System.Text.RegularExpressions.Regex.Match(
                trimmed,
                @"^([A-Za-z]{1,5}\d{1,4})[-_ ]?([A-Za-z]{1,3})$");
            if (m.Success)
            {
                return $"{m.Groups[1].Value.ToUpperInvariant()}-{m.Groups[2].Value.ToUpperInvariant()}";
            }
            return trimmed;
        }

        /// <summary>
        /// Obtiene el código y nombre del ensamblaje combinando Assembly Code (o Familia) + "-" + Type Mark (o Tipo).
        /// Ejemplo: Assembly Code "P04" + Type Mark "A" -> "P04-A" (NUNCA "P04AA" ni "P04A").
        /// </summary>
        public static string GetParameterBasedName(DoorItemViewModel item)
        {
            if (item == null) return "Assembly";

            string assCode = item.AssemblyCode?.Trim() ?? "";
            string typeMark = item.TypeMark?.Trim() ?? "";
            string mark = item.Mark?.Trim() ?? "";
            string fam = item.FamilyName?.Trim() ?? "";
            string typ = item.TypeName?.Trim() ?? "";

            // 1. Prioridad máxima: Assembly Code + Type Mark
            string baseCode = !string.IsNullOrWhiteSpace(assCode)
                ? assCode
                : (ExtractBaseCode(fam) ?? ExtractBaseCode(mark) ?? ExtractBaseCode(typ) ?? "");

            string variant = !string.IsNullOrWhiteSpace(typeMark) ? typeMark : "";
            if (string.IsNullOrWhiteSpace(variant) && !string.IsNullOrWhiteSpace(typ) && typ.Length <= 4 && !typ.Contains(" "))
            {
                variant = typ;
            }

            if (!string.IsNullOrWhiteSpace(baseCode) && !string.IsNullOrWhiteSpace(variant))
            {
                // Si variant ya incluye el baseCode (ej: "P04-A" o "P04A"), normalizar con guion
                if (variant.StartsWith(baseCode, StringComparison.OrdinalIgnoreCase))
                {
                    string suffix = variant.Substring(baseCode.Length).TrimStart('-', '_', ' ');
                    if (!string.IsNullOrWhiteSpace(suffix))
                        return SanitizeRevitName($"{baseCode.ToUpperInvariant()}-{suffix.ToUpperInvariant()}");
                    return SanitizeRevitName(baseCode.ToUpperInvariant());
                }

                return SanitizeRevitName($"{baseCode.ToUpperInvariant()}-{variant.ToUpperInvariant()}");
            }

            // 2. Si Mark ya tiene un código explícito (ej: "P04A", "P04-A", "V01B")
            if (!string.IsNullOrWhiteSpace(mark))
            {
                if (mark.Length <= 2 && !string.IsNullOrWhiteSpace(fam))
                {
                    string cleanFam = fam.TrimEnd('-', '_');
                    return SanitizeRevitName($"{cleanFam}-{mark.ToUpperInvariant()}");
                }
                return SanitizeRevitName(NormalizeCodeWithHyphen(mark));
            }

            // 3. Analizar Familia y Tipo
            if (!string.IsNullOrWhiteSpace(fam) && !string.IsNullOrWhiteSpace(typ))
            {
                if (typ.StartsWith(fam, StringComparison.OrdinalIgnoreCase))
                {
                    return SanitizeRevitName(NormalizeCodeWithHyphen(typ));
                }

                if (typ.Length <= 4 && !typ.Contains(" "))
                {
                    string cleanFam = fam.TrimEnd('-', '_');
                    return SanitizeRevitName($"{cleanFam}-{typ.ToUpperInvariant()}");
                }

                return SanitizeRevitName($"{fam} - {typ}");
            }

            if (!string.IsNullOrWhiteSpace(typ)) return SanitizeRevitName(NormalizeCodeWithHyphen(typ));
            if (!string.IsNullOrWhiteSpace(fam)) return SanitizeRevitName(NormalizeCodeWithHyphen(fam));

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
        /// Extrae el código base de una marca o nombre de tipo: "P01A" -> "P01", "P-02B" -> "P-02", "V 03" -> "V 03".
        /// Devuelve null si el texto no empieza por un código (letras + número).
        /// </summary>
        public static string? ExtractBaseCode(string? text)
        {
            if (string.IsNullOrWhiteSpace(text)) return null;
            var m = System.Text.RegularExpressions.Regex.Match(
                text.Trim(),
                @"^([A-Za-z]{1,6}[-_ ]?\d{1,4})(?!\d)",
                System.Text.RegularExpressions.RegexOptions.IgnoreCase);
            return m.Success ? m.Groups[1].Value.ToUpperInvariant() : null;
        }

        /// <summary>
        /// Detecta el código que usa un cuadro (por ejemplo "P01") leyendo sus filtros de texto.
        /// </summary>
        public static string? DetectScheduleCode(ViewSchedule schedule)
        {
            if (schedule == null || !schedule.IsValidObject) return null;
            foreach (ScheduleFilter f in schedule.Definition.GetFilters())
            {
                string? value = null;
                try { value = f.GetStringValue(); } catch { }
                string? code = ExtractBaseCode(value);
                if (code != null) return code;
            }
            return null;
        }

        /// <summary>
        /// Grupo de elementos que se convierte en un mismo ensamblaje.
        /// IsCodeGroup = true cuando el grupo nace de un código base (P01 con variantes P01A, P01B...).
        /// </summary>
        public class DoorBatchGroup
        {
            public string Name { get; set; } = string.Empty;
            public bool IsCodeGroup { get; set; }
            public List<DoorItemViewModel> Items { get; } = new List<DoorItemViewModel>();
        }

        /// <summary>
        /// Agrupa los tipos seleccionados. Si GroupByCode es false (por defecto), cada Familia+Tipo (P01-A, P01-B, P04-A...)
        /// forma su propio ensamblaje con 1 sola instancia representativa y nombre exacto "P04-A".
        /// </summary>
        public static List<DoorBatchGroup> BuildGroups(IEnumerable<DoorItemViewModel> items, BatchAssemblyOptions options)
        {
            var groups = new List<DoorBatchGroup>();
            var byCode = new Dictionary<string, DoorBatchGroup>(StringComparer.OrdinalIgnoreCase);

            foreach (var item in items.Where(i => i != null))
            {
                string? code = ExtractBaseCode(item.AssemblyCode) ?? ExtractBaseCode(item.Mark) ?? ExtractBaseCode(item.FamilyName) ?? ExtractBaseCode(item.TypeName);

                if (options.GroupByCode && code != null)
                {
                    if (!byCode.TryGetValue(code, out var group))
                    {
                        group = new DoorBatchGroup { Name = code, IsCodeGroup = true };
                        byCode[code] = group;
                        groups.Add(group);
                    }
                    group.Items.Add(item);
                }
                else
                {
                    string typeCode = GetParameterBasedName(item);
                    var single = new DoorBatchGroup
                    {
                        Name = typeCode,
                        IsCodeGroup = !string.IsNullOrWhiteSpace(typeCode) && !string.Equals(typeCode, "Assembly", StringComparison.OrdinalIgnoreCase)
                    };
                    single.Items.Add(item);
                    groups.Add(single);
                }
            }

            return groups;
        }

        /// <summary>
        /// Elimina todos los AssemblyType huérfanos (que no tienen ningún AssemblyInstance en el modelo, como P04AA, P04BB...).
        /// Si ya hay una transacción abierta la usa; de lo contrario abre una propia.
        /// </summary>
        public static int PurgeOrphanAssemblyTypes(Document doc, List<string>? logs = null)
        {
            var usedTypeIds = new FilteredElementCollector(doc)
                .OfClass(typeof(AssemblyInstance))
                .Cast<AssemblyInstance>()
                .Select(ai => ai.GetTypeId())
                .Where(id => id != ElementId.InvalidElementId)
                .ToHashSet();

            var orphanTypes = new FilteredElementCollector(doc)
                .OfClass(typeof(AssemblyType))
                .Cast<AssemblyType>()
                .Where(at => !usedTypeIds.Contains(at.Id))
                .ToList();

            if (orphanTypes.Count == 0) return 0;

            int deleted = 0;
            bool ownTx = !doc.IsModifiable;
            Transaction? tx = ownTx ? new Transaction(doc, "Purgar AssemblyTypes Huérfanos") : null;
            try
            {
                tx?.Start();
                foreach (var at in orphanTypes)
                {
                    string oldName = at.Name;
                    try
                    {
                        doc.Delete(at.Id);
                        deleted++;
                        logs?.Add($"[Limpieza] Tipo de ensamblaje huérfano eliminado: '{oldName}'.");
                    }
                    catch { }
                }
                tx?.Commit();
            }
            catch
            {
                if (tx != null && tx.HasStarted()) tx.RollBack();
            }
            finally
            {
                tx?.Dispose();
            }

            return deleted;
        }

        /// <summary>
        /// Crea ensamblajes en lote utilizando un TransactionGroup de 2 fases:
        /// Fase 1 (tCreate): Limpia tipos huérfanos, valida con AreElementsValidForAssembly y ejecuta AssemblyInstance.Create + Commit().
        /// Fase 2 (tRename): Una vez que Revit ha asignado los AssemblyType tras el Commit de la Fase 1, renombra cada AssemblyType al código exacto (ej. "P04-A").
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
                logs.Add("No hay elementos seleccionados para ensamblar.");
                return (created, logs);
            }

            ElementId namingCatId = new ElementId(options.Category);
            string catLabel = options.Category == BuiltInCategory.OST_Windows ? "Ventanas" : "Puertas";

            string currentName = !string.IsNullOrWhiteSpace(options.InitialName)
                ? SanitizeRevitName(options.InitialName.Trim())
                : (options.Category == BuiltInCategory.OST_Windows ? "V-01" : "P-01");

            var pendingRenames = new List<(ElementId assemblyId, string desiredName, int memberCount, int variants, int totalUds)>();

            using (TransactionGroup tg = new TransactionGroup(doc, $"Creación y Nomenclatura de Ensamblajes de {catLabel}"))
            {
                tg.Start();

                // FASE 1: Purgar tipos vacíos (P04AA, P04BB...) y crear instancias de ensamblaje
                using (Transaction tCreate = new Transaction(doc, $" Fase 1 - Crear Ensamblajes de {catLabel}"))
                {
                    tCreate.Start();

                    PurgeOrphanAssemblyTypes(doc, logs);

                    if (options.GroupByInstance)
                    {
                        foreach (var item in doorItems)
                        {
                            if (item == null) continue;
                            string baseCode = GetParameterBasedName(item);
                            foreach (var mId in FreeMembers(doc, item))
                            {
                                try
                                {
                                    var memberList = new List<ElementId> { mId };
                                    if (!AssemblyInstance.AreElementsValidForAssembly(doc, memberList, ElementId.InvalidElementId))
                                    {
                                        logs.Add($"ID {mId.Value}: rechazado por AreElementsValidForAssembly.");
                                        continue;
                                    }

                                    var ai = AssemblyInstance.Create(doc, memberList, namingCatId);
                                    if (ai == null) continue;

                                    string desired = !string.IsNullOrWhiteSpace(baseCode) && baseCode != "Assembly" ? baseCode : currentName;
                                    pendingRenames.Add((ai.Id, desired, 1, 1, 1));
                                    currentName = GetNextRevitName(currentName);
                                }
                                catch (Exception ex)
                                {
                                    logs.Add($"Error al crear ensamblaje individual para ID {mId}: {ex.Message}");
                                }
                            }
                        }
                    }
                    else
                    {
                        foreach (var group in BuildGroups(doorItems, options))
                        {
                            var members = new List<ElementId>();
                            foreach (var item in group.Items)
                            {
                                var free = FreeMembers(doc, item);
                                if (free.Count == 0)
                                {
                                    // Si ya tiene un ensamblaje creado, verificar si solo necesita renombrarse a P04-A
                                    if (item.AlreadyInAssembly && item.AssemblyId != ElementId.InvalidElementId)
                                    {
                                        string targetCode = group.IsCodeGroup ? group.Name : GetParameterBasedName(item);
                                        pendingRenames.Add((item.AssemblyId, targetCode, 1, 1, item.InstanceCount));
                                    }
                                    else
                                    {
                                        logs.Add($"Elemento '{item.DisplayTitle}': sin ejemplares libres válidos.");
                                    }
                                    continue;
                                }

                                // Siempre tomar 1 sola instancia representativa por defecto para vistas limpias
                                members.AddRange(options.SingleInstancePerType ? free.Take(1) : free);
                            }

                            if (members.Count == 0) continue;

                            try
                            {
                                if (!AssemblyInstance.AreElementsValidForAssembly(doc, members, ElementId.InvalidElementId))
                                {
                                    // Si el grupo completo falla, intentar con el primer ejemplar válido
                                    var fallback = members.Where(m => AssemblyInstance.AreElementsValidForAssembly(doc, new List<ElementId> { m }, ElementId.InvalidElementId)).Take(1).ToList();
                                    if (fallback.Count == 0)
                                    {
                                        logs.Add($"Grupo '{group.Name}': los elementos seleccionados no son válidos para Assembly en Revit.");
                                        continue;
                                    }
                                    members = fallback;
                                }

                                var ai = AssemblyInstance.Create(doc, members, namingCatId);
                                if (ai == null) continue;

                                string desired = group.IsCodeGroup ? group.Name : currentName;
                                int variants = group.Items.Count;
                                int totalUds = group.Items.Sum(i => i.InstanceCount);
                                pendingRenames.Add((ai.Id, desired, members.Count, variants, totalUds));

                                if (!group.IsCodeGroup) currentName = GetNextRevitName(currentName);
                            }
                            catch (Exception ex)
                            {
                                logs.Add($"Error al crear ensamblaje para '{group.Name}': {ex.Message}");
                            }
                        }
                    }

                    tCreate.Commit();
                }

                // FASE 2: Con los AssemblyType ya asignados por Revit tras tCreate.Commit(), renombrarlos a P04-A, P01-A, etc.
                if (pendingRenames.Count > 0)
                {
                    using (Transaction tRename = new Transaction(doc, $"Fase 2 - Nombrar Ensamblajes de {catLabel}"))
                    {
                        tRename.Start();

                        var existingAssemblyTypeNames = new FilteredElementCollector(doc)
                            .OfClass(typeof(AssemblyType))
                            .Cast<AssemblyType>()
                            .Select(at => at.Name)
                            .ToHashSet(StringComparer.OrdinalIgnoreCase);

                        foreach (var item in pendingRenames)
                        {
                            if (doc.GetElement(item.assemblyId) is not AssemblyInstance ai || !ai.IsValidObject)
                                continue;

                            var (_, finalName) = SetAssemblyTypeName(doc, ai, item.desiredName, existingAssemblyTypeNames, logs);
                            created.Add(ai);
                            logs.Add($"✅ Ensamblaje '{finalName}' activo ({item.memberCount} ejemplar en Assembly | {item.totalUds} uds en obra).");
                        }

                        PurgeOrphanAssemblyTypes(doc, null);
                        tRename.Commit();
                    }
                }

                tg.Assimilate();
            }

            return (created, logs);
        }

        /// <summary>
        /// Miembros de un tipo que todavía no pertenecen a ningún ensamblaje y son válidos para AssemblyInstance.Create.
        /// </summary>
        private static List<ElementId> FreeMembers(Document doc, DoorItemViewModel item)
        {
            return item.MemberElementIds
                .Where(id =>
                {
                    var elem = doc.GetElement(id);
                    if (elem is not FamilyInstance) return false;
                    if (elem.AssemblyInstanceId != ElementId.InvalidElementId) return false;
                    if (elem.GroupId != ElementId.InvalidElementId) return false;
                    if (elem.GetTypeId() == ElementId.InvalidElementId) return false;
                    try
                    {
                        return AssemblyInstance.AreElementsValidForAssembly(doc, new List<ElementId> { id }, ElementId.InvalidElementId);
                    }
                    catch
                    {
                        return false;
                    }
                })
                .ToList();
        }

        /// <summary>
        /// Genera o agrega vistas técnicas a los ensamblajes correspondientes a los elementos seleccionados en la UI.
        /// Permite operar tanto sobre ensamblajes recién creados como sobre ensamblajes ya existentes en el modelo.
        /// </summary>
        public static (int createdViewsCount, List<string> logs) GenerateViewsForDoorItems(
            Document doc,
            IList<DoorItemViewModel> doorItems,
            AssemblyBatchViewOptions options,
            ViewSchedule? sourceTemplateSchedule = null)
        {
            var logs = new List<string>();
            if (doorItems == null || doorItems.Count == 0)
            {
                logs.Add("No hay elementos seleccionados para generar vistas.");
                return (0, logs);
            }

            var targetAssemblies = new List<AssemblyInstance>();
            var seenIds = new HashSet<ElementId>();

            foreach (var item in doorItems)
            {
                if (item.AssemblyId != ElementId.InvalidElementId && !seenIds.Contains(item.AssemblyId))
                {
                    if (doc.GetElement(item.AssemblyId) is AssemblyInstance ai && ai.IsValidObject)
                    {
                        targetAssemblies.Add(ai);
                        seenIds.Add(item.AssemblyId);
                    }
                }
            }

            if (targetAssemblies.Count == 0)
            {
                logs.Add("Ninguno de los elementos seleccionados tiene un ensamblaje creado todavía. Primero crea los ensamblajes con '1. Crear Ensamblajes'.");
                return (0, logs);
            }

            return CreateBatchAssemblyViews(doc, targetAssemblies, options, sourceTemplateSchedule);
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

        // =========================================================================
        // MOTOR DE 3 COLUMNAS: TABLAS -> FAMILIA+TIPO (ASSEMBLY ON/OFF) -> VISTAS (ON/OFF)
        // =========================================================================

        public static List<CarpentryScheduleItem> GetCarpentrySchedules(Document doc, BuiltInCategory? filterCategory = null)
        {
            var list = new List<CarpentryScheduleItem>();
            var schedules = new FilteredElementCollector(doc)
                .OfClass(typeof(ViewSchedule))
                .Cast<ViewSchedule>()
                .Where(s => !s.IsTemplate && !s.IsTitleblockRevisionSchedule && !s.IsInternalKeynoteSchedule && !s.IsAssemblyView)
                .ToList();

            long doorsCatId = new ElementId(BuiltInCategory.OST_Doors).Value;
            long windowsCatId = new ElementId(BuiltInCategory.OST_Windows).Value;

            foreach (var vs in schedules)
            {
                long catVal = vs.Definition.CategoryId.Value;
                BuiltInCategory bic;
                if (catVal == doorsCatId) bic = BuiltInCategory.OST_Doors;
                else if (catVal == windowsCatId) bic = BuiltInCategory.OST_Windows;
                else continue;

                if (filterCategory.HasValue && bic != filterCategory.Value)
                    continue;

                list.Add( InspectScheduleItem(doc, vs, bic) );
            }

            return list
                .OrderByDescending(x => x.IsComplete)
                .ThenBy(x => x.Name)
                .ToList();
        }

        public static CarpentryScheduleItem InspectScheduleItem(Document doc, ViewSchedule vs, BuiltInCategory bic)
        {
            var def = vs.Definition;
            bool hasFamily = false;
            bool hasType = false;
            bool hasAssCode = false;
            bool hasTypeMark = false;
            bool hasMark = false;
            bool hasDesc = false;

            long idFam = new ElementId(BuiltInParameter.ELEM_FAMILY_PARAM).Value;
            long idType = new ElementId(BuiltInParameter.ELEM_TYPE_PARAM).Value;
            long idSymName = new ElementId(BuiltInParameter.SYMBOL_NAME_PARAM).Value;
            long idFamAndType = new ElementId(BuiltInParameter.ELEM_FAMILY_AND_TYPE_PARAM).Value;
            long idAssCode = new ElementId((BuiltInParameter)(-1002504)).Value;
            long idTypeMark = new ElementId(BuiltInParameter.ALL_MODEL_TYPE_MARK).Value;
            long idMark = new ElementId(BuiltInParameter.ALL_MODEL_MARK).Value;
            long idDesc = new ElementId(BuiltInParameter.ALL_MODEL_DESCRIPTION).Value;

            foreach (ScheduleFieldId fId in def.GetFieldOrder())
            {
                ScheduleField sf = def.GetField(fId);
                long pVal = sf.ParameterId.Value;
                string fName = (sf.GetName() ?? "").Trim();
                string hName = (sf.ColumnHeading ?? "").Trim();

                if (pVal == idFam || pVal == idFamAndType || MatchesFieldName(fName, hName, "Family", "Familia"))
                    hasFamily = true;

                if (pVal == idType || pVal == idSymName || pVal == idFamAndType || MatchesFieldName(fName, hName, "Type", "Tipo"))
                    hasType = true;

                if (pVal == idAssCode || MatchesFieldName(fName, hName, "Assembly Code", "Código de montaje", "Código de ensamblaje"))
                    hasAssCode = true;

                if (pVal == idTypeMark || MatchesFieldName(fName, hName, "Type Mark", "Marca de tipo"))
                    hasTypeMark = true;

                if (pVal == idMark || MatchesFieldName(fName, hName, "Mark", "Marca"))
                    hasMark = true;

                if (pVal == idDesc || MatchesFieldName(fName, hName, "Description", "Descripción"))
                    hasDesc = true;
            }

            var missing = new List<string>();
            if (!hasFamily) missing.Add("Family");
            if (!hasType) missing.Add("Type");
            if (!hasAssCode) missing.Add("Assembly Code");
            if (!hasTypeMark) missing.Add("Type Mark");
            if (!hasMark) missing.Add("Mark");
            if (!hasDesc) missing.Add("Description");

            int elemCount = 0;
            try
            {
                elemCount = new FilteredElementCollector(doc, vs.Id)
                    .WhereElementIsNotElementType()
                    .OfClass(typeof(FamilyInstance))
                    .GetElementCount();
            }
            catch { }

            return new CarpentryScheduleItem
            {
                Id = vs.Id,
                Schedule = vs,
                Name = vs.Name,
                Category = bic,
                ElementCount = elemCount,
                MissingFields = missing
            };
        }

        private static bool MatchesFieldName(string fName, string hName, params string[] candidates)
        {
            foreach (var c in candidates)
            {
                if (string.Equals(fName, c, StringComparison.OrdinalIgnoreCase) ||
                    string.Equals(hName, c, StringComparison.OrdinalIgnoreCase))
                    return true;
            }
            return false;
        }

        /// <summary>
        /// Inyecta vía código en la tabla seleccionada las propiedades requeridas faltantes:
        /// Family, Type, Assembly Code, Type Mark, Mark, Description.
        /// </summary>
        public static (int addedCount, string message) AddMissingRequiredFields(Document doc, ViewSchedule schedule)
        {
            if (schedule == null || !schedule.IsValidObject)
                return (0, "Tabla inválida.");

            long catVal = schedule.Definition.CategoryId.Value;
            BuiltInCategory bic = catVal == new ElementId(BuiltInCategory.OST_Windows).Value
                ? BuiltInCategory.OST_Windows
                : BuiltInCategory.OST_Doors;

            var inspected = InspectScheduleItem(doc, schedule, bic);
            if (inspected.IsComplete)
                return (0, $"La tabla '{schedule.Name}' ya cuenta con todas las propiedades requeridas.");

            var requiredSpecs = new List<(string key, BuiltInParameter[] bips, string[] names)>
            {
                ("Family", new[] { BuiltInParameter.ELEM_FAMILY_PARAM, BuiltInParameter.ELEM_FAMILY_AND_TYPE_PARAM }, new[] { "Family", "Familia" }),
                ("Type", new[] { BuiltInParameter.ELEM_TYPE_PARAM, BuiltInParameter.SYMBOL_NAME_PARAM, BuiltInParameter.ELEM_FAMILY_AND_TYPE_PARAM }, new[] { "Type", "Tipo" }),
                ("Assembly Code", new[] { (BuiltInParameter)(-1002504) }, new[] { "Assembly Code", "Código de montaje", "Código de ensamblaje" }),
                ("Type Mark", new[] { BuiltInParameter.ALL_MODEL_TYPE_MARK }, new[] { "Type Mark", "Marca de tipo" }),
                ("Mark", new[] { BuiltInParameter.ALL_MODEL_MARK }, new[] { "Mark", "Marca" }),
                ("Description", new[] { BuiltInParameter.ALL_MODEL_DESCRIPTION }, new[] { "Description", "Descripción" })
            };

            int added = 0;
            var addedNames = new List<string>();

            using (Transaction t = new Transaction(doc, $"Agregar Propiedades Requeridas a '{schedule.Name}'"))
            {
                t.Start();
                var def = schedule.Definition;
                var schedulable = def.GetSchedulableFields();

                foreach (var spec in requiredSpecs)
                {
                    if (!inspected.MissingFields.Contains(spec.key))
                        continue;

                    SchedulableField? match = null;
                    foreach (var bip in spec.bips)
                    {
                        ElementId bipId = new ElementId(bip);
                        match = schedulable.FirstOrDefault(sf => sf.ParameterId == bipId);
                        if (match != null) break;
                    }

                    if (match == null)
                    {
                        match = schedulable.FirstOrDefault(sf =>
                            spec.names.Any(n => string.Equals(sf.GetName(doc), n, StringComparison.OrdinalIgnoreCase)));
                    }

                    if (match != null)
                    {
                        try
                        {
                            def.AddField(match);
                            added++;
                            addedNames.Add(spec.key);
                        }
                        catch { }
                    }
                }

                t.Commit();
            }

            string msg = added > 0
                ? $"Se agregaron {added} propiedad(es) a '{schedule.Name}': {string.Join(", ", addedNames)}."
                : $"No fue necesario agregar campos adicionales a '{schedule.Name}'.";

            return (added, msg);
        }

        /// <summary>
        /// Obtiene para la Columna 2 las Familias + Tipos pertenecientes a la Tabla seleccionada en la Columna 1.
        /// Forma el código combinando Assembly Code + "-" + Type Mark (ej. P01-A, P04-A).
        /// </summary>
        public static List<DoorItemViewModel> GetItemsFromSchedule(Document doc, ViewSchedule schedule)
        {
            if (schedule == null || !schedule.IsValidObject)
                return new List<DoorItemViewModel>();

            long catVal = schedule.Definition.CategoryId.Value;
            BuiltInCategory bic = catVal == new ElementId(BuiltInCategory.OST_Windows).Value
                ? BuiltInCategory.OST_Windows
                : BuiltInCategory.OST_Doors;

            List<FamilyInstance> elements;
            try
            {
                elements = new FilteredElementCollector(doc, schedule.Id)
                    .WhereElementIsNotElementType()
                    .OfClass(typeof(FamilyInstance))
                    .Cast<FamilyInstance>()
                    .ToList();
            }
            catch
            {
                elements = new List<FamilyInstance>();
            }

            // Si la tabla tiene filtros que dejan 0 elementos visibles, buscar por categoría y código de filtro
            if (elements.Count == 0)
            {
                var allCat = new FilteredElementCollector(doc)
                    .OfCategory(bic)
                    .WhereElementIsNotElementType()
                    .OfClass(typeof(FamilyInstance))
                    .Cast<FamilyInstance>()
                    .ToList();

                string? schedCode = DetectScheduleCode(schedule) ?? ExtractBaseCode(schedule.Name);
                if (!string.IsNullOrWhiteSpace(schedCode))
                {
                    var matching = allCat.Where(fi =>
                    {
                        string ac = ReadAssemblyCodeFromElement(doc, fi);
                        string fam = fi.Symbol?.FamilyName ?? "";
                        string mk = fi.get_Parameter(BuiltInParameter.ALL_MODEL_MARK)?.AsString() ?? "";
                        return ac.StartsWith(schedCode, StringComparison.OrdinalIgnoreCase)
                            || fam.StartsWith(schedCode, StringComparison.OrdinalIgnoreCase)
                            || mk.StartsWith(schedCode, StringComparison.OrdinalIgnoreCase);
                    }).ToList();

                    elements = matching.Count > 0 ? matching : allCat;
                }
                else
                {
                    elements = allCat;
                }
            }

            var items = BuildDoorItemViewModels(doc, elements, groupByInstance: false, category: bic);

            // Además, si ya existe en el modelo un AssemblyType con el código exacto (ej. P04-A) pero asociado a otra instancia del tipo, enlazarlo
            var allAssemblies = new FilteredElementCollector(doc)
                .OfClass(typeof(AssemblyInstance))
                .Cast<AssemblyInstance>()
                .ToList();

            foreach (var item in items)
            {
                if (!item.AlreadyInAssembly)
                {
                    var matchByName = allAssemblies.FirstOrDefault(a =>
                        string.Equals(a.AssemblyTypeName, item.ComputedCode, StringComparison.OrdinalIgnoreCase));
                    if (matchByName != null)
                    {
                        item.AlreadyInAssembly = true;
                        item.AssemblyId = matchByName.Id;
                        item.AssemblyName = matchByName.AssemblyTypeName ?? matchByName.Name;
                    }
                }
            }

            return items;
        }

        /// <summary>
        /// Prende (ON = Crear / Renombrar con 1 sola unidad) o Apaga (OFF = Disassemble + Purgar AssemblyType)
        /// el ensamblaje de una Familia + Tipo desde el Switch de la Columna 2.
        /// </summary>
        public static (bool success, string message) SetTypeAssemblyOnOff(
            Document doc,
            DoorItemViewModel item,
            bool turnOn)
        {
            if (item == null) return (false, "Elemento nulo.");

            string desiredCode = item.ComputedCode;
            ElementId namingCatId = new ElementId(item.Category);
            var logs = new List<string>();

            if (turnOn)
            {
                // 1. Verificar si ya tiene un AssemblyInstance activo
                AssemblyInstance? existingAi = null;
                if (item.AssemblyId != ElementId.InvalidElementId)
                {
                    existingAi = doc.GetElement(item.AssemblyId) as AssemblyInstance;
                }
                if (existingAi == null)
                {
                    foreach (var mId in item.MemberElementIds)
                    {
                        var el = doc.GetElement(mId);
                        if (el != null && el.AssemblyInstanceId != ElementId.InvalidElementId)
                        {
                            existingAi = doc.GetElement(el.AssemblyInstanceId) as AssemblyInstance;
                            if (existingAi != null) break;
                        }
                    }
                }

                if (existingAi != null && existingAi.IsValidObject)
                {
                    // Ya existe: asegurar que tenga solo 1 ejemplar y que su nombre sea exactamente desiredCode (ej. P04-A)
                    using (Transaction tFix = new Transaction(doc, $"Actualizar Ensamblaje '{desiredCode}'"))
                    {
                        tFix.Start();
                        PurgeOrphanAssemblyTypes(doc, logs);

                        var currentMembers = existingAi.GetMemberIds().ToList();
                        if (currentMembers.Count > 1)
                        {
                            var extras = currentMembers.Skip(1).ToList();
                            try { existingAi.RemoveMemberIds(extras); } catch { }
                        }

                        StampCodeParametersOnItem(doc, item, desiredCode);

                        var existingNames = new FilteredElementCollector(doc)
                            .OfClass(typeof(AssemblyType))
                            .Cast<AssemblyType>()
                            .Select(at => at.Name)
                            .ToHashSet(StringComparer.OrdinalIgnoreCase);

                        var (_, finalName) = SetAssemblyTypeName(doc, existingAi, desiredCode, existingNames, logs);
                        PurgeOrphanAssemblyTypes(doc, null);
                        tFix.Commit();

                        item.AlreadyInAssembly = true;
                        item.AssemblyId = existingAi.Id;
                        item.AssemblyName = finalName;
                        return (true, $"🟢 Assembly '{finalName}' activo y sincronizado (1 unidad).");
                    }
                }

                // 2. No existe: crear con 1 SOLA INSTANCIA en Doble Transacción (Fase 1 Create + Commit, Fase 2 Rename + Commit)
                var free = FreeMembers(doc, item);
                if (free.Count == 0)
                {
                    return (false, $"No se encontró un ejemplar libre y válido para '{desiredCode}' ({item.FamilyName} : {item.TypeName}).");
                }

                var singleMember = new List<ElementId> { free[0] };
                ElementId createdAiId = ElementId.InvalidElementId;
                string finalAssName = desiredCode;

                using (TransactionGroup tg = new TransactionGroup(doc, $"Activar Assembly {desiredCode}"))
                {
                    tg.Start();

                    using (Transaction t1 = new Transaction(doc, $"Fase 1 - Crear Assembly {desiredCode}"))
                    {
                        t1.Start();
                        PurgeOrphanAssemblyTypes(doc, logs);
                        StampCodeParametersOnItem(doc, item, desiredCode);

                        var ai = AssemblyInstance.Create(doc, singleMember, namingCatId);
                        if (ai != null)
                        {
                            createdAiId = ai.Id;
                        }
                        t1.Commit();
                    }

                    if (createdAiId != ElementId.InvalidElementId)
                    {
                        using (Transaction t2 = new Transaction(doc, $"Fase 2 - Nombrar Assembly {desiredCode}"))
                        {
                            t2.Start();
                            if (doc.GetElement(createdAiId) is AssemblyInstance ai && ai.IsValidObject)
                            {
                                var existingNames = new FilteredElementCollector(doc)
                                    .OfClass(typeof(AssemblyType))
                                    .Cast<AssemblyType>()
                                    .Select(at => at.Name)
                                    .ToHashSet(StringComparer.OrdinalIgnoreCase);

                                var (_, fName) = SetAssemblyTypeName(doc, ai, desiredCode, existingNames, logs);
                                finalAssName = fName;
                            }
                            PurgeOrphanAssemblyTypes(doc, null);
                            t2.Commit();
                        }
                    }

                    tg.Assimilate();
                }

                if (createdAiId != ElementId.InvalidElementId)
                {
                    item.AlreadyInAssembly = true;
                    item.AssemblyId = createdAiId;
                    item.AssemblyName = finalAssName;
                    return (true, $"🟢 Assembly '{finalAssName}' creado con éxito (1 sola instancia representativa).");
                }

                return (false, $"No se pudo crear el Assembly para '{desiredCode}'.");
            }
            else
            {
                // OFF: Desarmar (Disassemble) todos los ensamblajes asociados a este tipo y limpiar el AssemblyType
                var targetAssemblies = new HashSet<ElementId>();
                if (item.AssemblyId != ElementId.InvalidElementId)
                    targetAssemblies.Add(item.AssemblyId);

                foreach (var mId in item.MemberElementIds)
                {
                    var el = doc.GetElement(mId);
                    if (el != null && el.AssemblyInstanceId != ElementId.InvalidElementId)
                        targetAssemblies.Add(el.AssemblyInstanceId);
                }

                // También buscar cualquier AssemblyInstance cuyo nombre coincida con desiredCode
                foreach (var ai in new FilteredElementCollector(doc).OfClass(typeof(AssemblyInstance)).Cast<AssemblyInstance>())
                {
                    if (string.Equals(ai.AssemblyTypeName, desiredCode, StringComparison.OrdinalIgnoreCase))
                        targetAssemblies.Add(ai.Id);
                }

                int disassembledCount = 0;
                using (Transaction tOff = new Transaction(doc, $"Desarmar Assembly {desiredCode}"))
                {
                    tOff.Start();

                    // Eliminar primero las vistas dependientes de estos ensamblajes
                    var allViews = new FilteredElementCollector(doc)
                        .OfClass(typeof(Autodesk.Revit.DB.View))
                        .Cast<Autodesk.Revit.DB.View>()
                        .Where(v => !v.IsTemplate && v.IsAssemblyView && targetAssemblies.Contains(v.AssociatedAssemblyInstanceId))
                        .Select(v => v.Id)
                        .ToList();

                    foreach (var vId in allViews)
                    {
                        try { doc.Delete(vId); } catch { }
                    }

                    foreach (var aId in targetAssemblies)
                    {
                        if (doc.GetElement(aId) is AssemblyInstance ai && ai.IsValidObject)
                        {
                            try
                            {
                                ai.Disassemble();
                                disassembledCount++;
                            }
                            catch
                            {
                                try { doc.Delete(aId); disassembledCount++; } catch { }
                            }
                        }
                    }

                    PurgeOrphanAssemblyTypes(doc, logs);
                    tOff.Commit();
                }

                item.AlreadyInAssembly = false;
                item.AssemblyId = ElementId.InvalidElementId;
                item.AssemblyName = string.Empty;
                return (true, $"⚪ Assembly '{desiredCode}' desarmado (OFF) y tipo limpiado del navegador ({disassembledCount} desmontado).");
            }
        }

        private static void StampCodeParametersOnItem(Document doc, DoorItemViewModel item, string desiredCode)
        {
            try
            {
                foreach (var mId in item.MemberElementIds)
                {
                    var el = doc.GetElement(mId);
                    if (el == null) continue;
                    var pMark = el.get_Parameter(BuiltInParameter.ALL_MODEL_MARK) ?? el.LookupParameter("Mark") ?? el.LookupParameter("Marca");
                    if (pMark != null && !pMark.IsReadOnly && string.IsNullOrWhiteSpace(pMark.AsString()))
                    {
                        pMark.Set(desiredCode);
                    }
                }
            }
            catch { }
        }

        /// <summary>
        /// Obtiene para la Columna 3 el listado único de las 6 vistas técnicas con su estado ON/OFF
        /// según el Assembly o los Assemblies seleccionados en la Columna 2.
        /// </summary>
        public static List<AssemblyViewToggleItem> GetUnifiedViewsForAssemblies(
            Document doc,
            IList<DoorItemViewModel> selectedItems)
        {
            var activeAssemblyIds = new HashSet<ElementId>();
            var activeCodes = new List<string>();

            if (selectedItems != null)
            {
                foreach (var it in selectedItems)
                {
                    if (it.AlreadyInAssembly && it.AssemblyId != ElementId.InvalidElementId && doc.GetElement(it.AssemblyId) is AssemblyInstance)
                    {
                        activeAssemblyIds.Add(it.AssemblyId);
                        activeCodes.Add(it.ComputedCode);
                    }
                }
            }

            var assemblyViews = activeAssemblyIds.Count > 0
                ? new FilteredElementCollector(doc)
                    .OfClass(typeof(Autodesk.Revit.DB.View))
                    .Cast<Autodesk.Revit.DB.View>()
                    .Where(v => !v.IsTemplate && v.IsAssemblyView && activeAssemblyIds.Contains(v.AssociatedAssemblyInstanceId))
                    .ToList()
                : new List<Autodesk.Revit.DB.View>();

            int totalAss = activeAssemblyIds.Count;
            string samplePrefix = activeCodes.Count == 1 ? activeCodes[0] : (activeCodes.Count > 1 ? $"{activeCodes[0]} (+{activeCodes.Count - 1})" : "P01-A");

            var specs = new List<(string key, string icon, string title, string suffix)>
            {
                ("3D", "🧊", "Vista 3D Isométrica (3D Ortho)", "Vista 3D"),
                ("Plan", "📐", "Planta de Detalle (Horizontal Detail)", "Planta de Detalle"),
                ("Front", "🏛️", "Elevación Frontal (Elevation Front)", "Elevación Frontal"),
                ("Back", "🔍", "Elevación Posterior (Elevation Back)", "Elevación Posterior"),
                ("Section", "✂️", "Sección Lateral (Section / Left)", "Sección Lateral"),
                ("Schedule", "📊", "Cuadro de Despiece (Part List / Schedule)", "Despiece y Medidas")
            };

            var result = new List<AssemblyViewToggleItem>();
            foreach (var sp in specs)
            {
                var matchingViews = assemblyViews.Where(v => MatchesAssemblyViewType(v, sp.key)).ToList();
                int countWithView = matchingViews.Select(v => v.AssociatedAssemblyInstanceId).Distinct().Count();
                bool isOn = totalAss > 0 && countWithView > 0;

                string statusText = totalAss == 0
                    ? "Selecciona un Assembly activo (ON) en la Columna 2"
                    : (isOn
                        ? $"🟢 Activa en {countWithView}/{totalAss} Assembly(s) -> '{samplePrefix} - {sp.suffix}'"
                        : $"⚪ Apagada -> Creará '{samplePrefix} - {sp.suffix}'");

                result.Add(new AssemblyViewToggleItem
                {
                    ViewKey = sp.key,
                    Icon = sp.icon,
                    Title = sp.title,
                    NamingPreview = statusText,
                    IsViewOn = isOn,
                    IsEnabled = totalAss > 0
                });
            }

            return result;
        }

        private static bool MatchesAssemblyViewType(Autodesk.Revit.DB.View v, string viewKey)
        {
            string vName = v.Name ?? "";
            switch (viewKey)
            {
                case "3D":
                    return v is View3D;
                case "Schedule":
                    return v is ViewSchedule;
                case "Plan":
                    return v is ViewSection && (vName.IndexOf("Planta", StringComparison.OrdinalIgnoreCase) >= 0 || vName.IndexOf("Horizontal", StringComparison.OrdinalIgnoreCase) >= 0 || vName.IndexOf("Plan", StringComparison.OrdinalIgnoreCase) >= 0);
                case "Front":
                    return v is ViewSection && (vName.IndexOf("Frontal", StringComparison.OrdinalIgnoreCase) >= 0 || vName.IndexOf("Front", StringComparison.OrdinalIgnoreCase) >= 0);
                case "Back":
                    return v is ViewSection && (vName.IndexOf("Posterior", StringComparison.OrdinalIgnoreCase) >= 0 || vName.IndexOf("Back", StringComparison.OrdinalIgnoreCase) >= 0);
                case "Section":
                    return v is ViewSection && (vName.IndexOf("Sección", StringComparison.OrdinalIgnoreCase) >= 0 || vName.IndexOf("Section", StringComparison.OrdinalIgnoreCase) >= 0 || vName.IndexOf("Lateral", StringComparison.OrdinalIgnoreCase) >= 0 || vName.IndexOf("Left", StringComparison.OrdinalIgnoreCase) >= 0);
                default:
                    return false;
            }
        }

        /// <summary>
        /// Prende (ON = Crea o Sobrescribe la vista) o Apaga (OFF = Elimina la vista)
        /// para el tipo de vista indicado en todos los Assemblies seleccionados en la Columna 2.
        /// </summary>
        public static (int affectedCount, string message) SetAssemblyViewOnOff(
            Document doc,
            IList<DoorItemViewModel> selectedItems,
            string viewKey,
            bool turnOn,
            int viewScale = 20,
            bool autoDimension = true,
            ViewSchedule? templateSchedule = null)
        {
            if (selectedItems == null || selectedItems.Count == 0)
                return (0, "No hay ningún Assembly seleccionado en la Columna 2.");

            var targetAssemblies = new List<(DoorItemViewModel item, AssemblyInstance ai)>();
            foreach (var it in selectedItems)
            {
                if (it.AlreadyInAssembly && it.AssemblyId != ElementId.InvalidElementId && doc.GetElement(it.AssemblyId) is AssemblyInstance ai && ai.IsValidObject)
                {
                    targetAssemblies.Add((it, ai));
                }
            }

            if (targetAssemblies.Count == 0)
                return (0, "Primero activa el Switch ON de Assembly en la Columna 2 para poder prender o apagar sus vistas.");

            var existingAssemblyViews = new FilteredElementCollector(doc)
                .OfClass(typeof(Autodesk.Revit.DB.View))
                .Cast<Autodesk.Revit.DB.View>()
                .Where(v => !v.IsTemplate && v.IsAssemblyView)
                .ToList();

            int count = 0;
            var createdForDimensioning = new List<AssemblyInstance>();

            using (Transaction t = new Transaction(doc, $"{(turnOn ? "Activar" : "Desactivar")} Vista '{viewKey}' en Assemblies"))
            {
                t.Start();

                foreach (var (item, inputAi) in targetAssemblies)
                {
                    var ai = inputAi;
                    string aName = ai.AssemblyTypeName ?? item.ComputedCode;

                    // Buscar vistas previas de este tipo en el Assembly (para borrar en OFF o sobrescribir en ON)
                    var oldViews = existingAssemblyViews
                        .Where(v => v.AssociatedAssemblyInstanceId == ai.Id && MatchesAssemblyViewType(v, viewKey))
                        .ToList();

                    foreach (var oldV in oldViews)
                    {
                        try
                        {
                            doc.Delete(oldV.Id);
                            if (!turnOn) count++;
                        }
                        catch { }
                    }

                    if (!turnOn) continue;

                    // Crear la vista solicitada
                    ElementId catId = new ElementId(item.Category);
                    int scale = viewScale > 0 ? viewScale : 20;

                    try
                    {
                        switch (viewKey)
                        {
                            case "3D":
                            {
                                var v3d = AssemblyViewUtils.Create3DOrthographic(doc, ai.Id);
                                if (v3d != null)
                                {
                                    v3d.Name = AssemblyScheduleService.GetUniqueScheduleName(doc, $"{aName} - Vista 3D");
                                    try { v3d.Scale = scale; } catch { }
                                    count++;
                                }
                                break;
                            }
                            case "Plan":
                            {
                                var vPlan = AssemblyViewUtils.CreateDetailSection(doc, ai.Id, AssemblyDetailViewOrientation.HorizontalDetail);
                                if (vPlan != null)
                                {
                                    vPlan.Name = AssemblyScheduleService.GetUniqueScheduleName(doc, $"{aName} - Planta de Detalle");
                                    try { vPlan.Scale = scale; } catch { }
                                    count++;
                                    createdForDimensioning.Add(ai);
                                }
                                break;
                            }
                            case "Front":
                            {
                                var vFront = AssemblyViewUtils.CreateDetailSection(doc, ai.Id, AssemblyDetailViewOrientation.ElevationFront);
                                if (vFront != null)
                                {
                                    vFront.Name = AssemblyScheduleService.GetUniqueScheduleName(doc, $"{aName} - Elevación Frontal");
                                    try { vFront.Scale = scale; } catch { }
                                    count++;
                                    createdForDimensioning.Add(ai);
                                }
                                break;
                            }
                            case "Back":
                            {
                                var vBack = AssemblyViewUtils.CreateDetailSection(doc, ai.Id, AssemblyDetailViewOrientation.ElevationBack);
                                if (vBack != null)
                                {
                                    vBack.Name = AssemblyScheduleService.GetUniqueScheduleName(doc, $"{aName} - Elevación Posterior");
                                    try { vBack.Scale = scale; } catch { }
                                    count++;
                                    createdForDimensioning.Add(ai);
                                }
                                break;
                            }
                            case "Section":
                            {
                                var vSec = AssemblyViewUtils.CreateDetailSection(doc, ai.Id, AssemblyDetailViewOrientation.ElevationLeft);
                                if (vSec != null)
                                {
                                    vSec.Name = AssemblyScheduleService.GetUniqueScheduleName(doc, $"{aName} - Sección Lateral");
                                    try { vSec.Scale = scale; } catch { }
                                    count++;
                                    createdForDimensioning.Add(ai);
                                }
                                break;
                            }
                            case "Schedule":
                            {
                                ViewSchedule? sched = null;
                                try
                                {
                                    sched = AssemblyViewUtils.CreateSingleCategorySchedule(doc, ai.Id, catId);
                                }
                                catch
                                {
                                    sched = AssemblyViewUtils.CreatePartList(doc, ai.Id);
                                }

                                if (sched != null)
                                {
                                    sched.Name = AssemblyScheduleService.GetUniqueScheduleName(doc, $"{aName} - Despiece y Medidas");
                                    if (templateSchedule != null && templateSchedule.IsValidObject)
                                    {
                                        var snap = AssemblyScheduleService.TakeSnapshot(doc, templateSchedule);
                                        AssemblyScheduleService.ApplySnapshotToSchedule(
                                            doc, sched, snap,
                                            replicateFormat: true,
                                            replicateSorting: true,
                                            replicateTotals: true,
                                            replicateFilters: false,
                                            replicateTemplate: true);
                                    }
                                    AssemblyBridgeService.InjectBridgeFieldToSchedule(doc, sched);
                                    count++;
                                }
                                break;
                            }
                        }
                    }
                    catch { }
                }

                t.Commit();
            }

            if (turnOn && autoDimension && createdForDimensioning.Count > 0)
            {
                foreach (var ai in createdForDimensioning.DistinctBy(x => x.Id.Value))
                {
                    try { DoorAssemblyDimensionService.DimensionAssembly(doc, ai); } catch { }
                }
            }

            string actionLabel = turnOn ? "creada(s)/actualizada(s)" : "eliminada(s)";
            return (count, $"Vista '{viewKey}' {actionLabel} en {count} Assembly(s).");
        }
    }

    /// <summary>
    /// Elemento de la Columna 1: Tabla de Planificación de Puertas o Ventanas con validación de las 6 propiedades requeridas.
    /// </summary>
    public class CarpentryScheduleItem : System.ComponentModel.INotifyPropertyChanged
    {
        public event System.ComponentModel.PropertyChangedEventHandler? PropertyChanged;
        protected void OnPropertyChanged([System.Runtime.CompilerServices.CallerMemberName] string? name = null)
        {
            PropertyChanged?.Invoke(this, new System.ComponentModel.PropertyChangedEventArgs(name));
        }

        public ElementId Id { get; set; } = ElementId.InvalidElementId;
        public ViewSchedule Schedule { get; set; } = null!;
        public string Name { get; set; } = string.Empty;
        public BuiltInCategory Category { get; set; } = BuiltInCategory.OST_Doors;
        public int ElementCount { get; set; }
        public List<string> MissingFields { get; set; } = new List<string>();

        public bool IsComplete => MissingFields == null || MissingFields.Count == 0;
        public bool IsIncomplete => !IsComplete;
        public string CategoryBadge => Category == BuiltInCategory.OST_Windows ? "🪟 WINDOWS" : "🚪 DOORS";
        public string CategoryColor => Category == BuiltInCategory.OST_Windows ? "#0284C7" : "#0F4369";
        public string StatusBadge => IsComplete ? "✅ READY" : $"⚠️ MISSING ({MissingFields.Count})";
        public string StatusColor => IsComplete ? "#059669" : "#9CA3AF";
        public string CardBackground => IsComplete ? "#FFFFFF" : "#ECEEF1";
        public string TitleForeground => IsComplete ? "#1C1C19" : "#6B7280";
        public double CardOpacity => IsComplete ? 1.0 : 0.78;
        public string SubtitleInfo => IsComplete
            ? $"✓ Family, Type, Assembly Code, Type Mark, Mark, Description ({ElementCount} elems)"
            : $"Missing: {string.Join(", ", MissingFields)}";
    }

    /// <summary>
    /// Elemento de la Columna 3: Fila única por cada Tipo de Vista Técnica con Switch ON/OFF.
    /// </summary>
    public class AssemblyViewToggleItem : System.ComponentModel.INotifyPropertyChanged
    {
        public event System.ComponentModel.PropertyChangedEventHandler? PropertyChanged;
        protected void OnPropertyChanged([System.Runtime.CompilerServices.CallerMemberName] string? name = null)
        {
            PropertyChanged?.Invoke(this, new System.ComponentModel.PropertyChangedEventArgs(name));
        }

        public string ViewKey { get; set; } = string.Empty;
        public string Icon { get; set; } = string.Empty;
        public string Title { get; set; } = string.Empty;
        public string NamingPreview { get; set; } = string.Empty;
        public bool IsEnabled { get; set; } = true;

        private bool _isViewOn;
        public bool IsViewOn
        {
            get => _isViewOn;
            set
            {
                if (_isViewOn != value)
                {
                    _isViewOn = value;
                    OnPropertyChanged();
                    OnPropertyChanged(nameof(SwitchLabel));
                    OnPropertyChanged(nameof(SwitchBackground));
                    OnPropertyChanged(nameof(BorderStroke));
                }
            }
        }

        public string SwitchLabel => IsViewOn ? "ON" : "OFF";
        public string SwitchBackground => IsViewOn ? "#059669" : "#9CA3AF";
        public string BorderStroke => IsViewOn ? "#059669" : "#C2C7CF";
    }
}
