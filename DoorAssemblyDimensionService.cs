using System;
using System.Collections.Generic;
using System.Linq;
using Autodesk.Revit.DB;
using Autodesk.Revit.UI;
using RevitView = Autodesk.Revit.DB.View;

namespace RevitToSupabasePlugin
{
    /// <summary>
    /// Opciones de configuración para el acotado de vistas de ensamblajes de puertas.
    /// </summary>
    public class DoorDimensionOptions
    {
        public bool DimTotalWidth { get; set; } = true;
        public bool DimTotalHeight { get; set; } = true;
        public bool DimJambsAndOpening { get; set; } = true;
        public bool DimClearHeight { get; set; } = true;
        public bool DimFrameDepth { get; set; } = true;
        public bool DimHandleHeight { get; set; } = false;
        public double PaperOffsetMm { get; set; } = 10.0;
        public ElementId? DimensionTypeId { get; set; }
        public bool ReplaceExistingDimensions { get; set; } = true;
    }

    /// <summary>
    /// Tipo de vista técnica clasificada para ensamblajes de puertas.
    /// </summary>
    public enum DoorAssemblyViewOrientation
    {
        ElevationFront,
        ElevationBack,
        HorizontalDetail,
        SectionSide,
        ThreeD,
        Schedule,
        Other
    }

    /// <summary>
    /// Representa un ensamblaje de puerta en el proyecto para visualización y selección en la UI.
    /// </summary>
    public class DoorAssemblyViewModel : System.ComponentModel.INotifyPropertyChanged
    {
        public event System.ComponentModel.PropertyChangedEventHandler? PropertyChanged;
        protected void OnPropertyChanged([System.Runtime.CompilerServices.CallerMemberName] string? name = null)
        {
            PropertyChanged?.Invoke(this, new System.ComponentModel.PropertyChangedEventArgs(name));
        }

        private bool _isSelected = true;
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

        public ElementId AssemblyId { get; set; } = ElementId.InvalidElementId;
        public ElementId DoorInstanceId { get; set; } = ElementId.InvalidElementId;
        public string AssemblyName { get; set; } = string.Empty;
        public string Mark { get; set; } = string.Empty;
        public string FamilyName { get; set; } = string.Empty;
        public string TypeName { get; set; } = string.Empty;
        public string Dimensions { get; set; } = string.Empty;
        public int TechnicalViewsCount { get; set; }
        public string ViewsSummary { get; set; } = string.Empty;
        public string Status { get; set; } = "Listo";

        public string DisplayTitle => string.IsNullOrWhiteSpace(Mark)
            ? AssemblyName
            : $"[{Mark}] {AssemblyName}";
    }

    /// <summary>
    /// Servicio especializado de alto nivel para el acotado inteligente y paramétrico
    /// de vistas técnicas de ensamblajes de puertas (Elevaciones, Plantas de Detalle y Secciones).
    /// </summary>
    public static class DoorAssemblyDimensionService
    {
        #region Consultas y Modelos

        /// <summary>
        /// Obtiene todos los ensamblajes del proyecto que contienen elementos de categoría Puertas (OST_Doors).
        /// </summary>
        public static List<DoorAssemblyViewModel> GetDoorAssemblies(Document doc)
        {
            var result = new List<DoorAssemblyViewModel>();

            var assemblies = new FilteredElementCollector(doc)
                .OfClass(typeof(AssemblyInstance))
                .Cast<AssemblyInstance>()
                .ToList();

            if (assemblies.Count == 0) return result;

            // Mapear todas las vistas de ensamblaje por AssemblyId
            var assemblyViews = new FilteredElementCollector(doc)
                .OfClass(typeof(RevitView))
                .Cast<RevitView>()
                .Where(v => !v.IsTemplate && v.IsAssemblyView && v.AssociatedAssemblyInstanceId != ElementId.InvalidElementId)
                .GroupBy(v => v.AssociatedAssemblyInstanceId)
                .ToDictionary(g => g.Key, g => g.ToList());

            foreach (var ai in assemblies)
            {
                var memberIds = ai.GetMemberIds();
                FamilyInstance? doorInst = null;

                foreach (var mId in memberIds)
                {
                    var elem = doc.GetElement(mId);
                    if (elem is FamilyInstance fi && fi.Category != null &&
                        fi.Category.Id == new ElementId(BuiltInCategory.OST_Doors))
                    {
                        doorInst = fi;
                        break;
                    }
                }

                if (doorInst == null) continue; // No contiene puertas

                string aName = ai.AssemblyTypeName ?? ai.Name;
                string mark = doorInst.get_Parameter(BuiltInParameter.ALL_MODEL_MARK)?.AsString() ??
                              doorInst.Symbol?.get_Parameter(BuiltInParameter.ALL_MODEL_TYPE_MARK)?.AsString() ?? "";
                string famName = doorInst.Symbol?.FamilyName ?? "Puerta";
                string typeName = doorInst.Symbol?.Name ?? "Tipo";

                string dims = GetDoorDimensionsString(doc, doorInst);

                List<RevitView> views = assemblyViews.TryGetValue(ai.Id, out var vList) ? vList : new List<RevitView>();
                var technicalViews = views.Where(v => v.ViewType != ViewType.ThreeD && !(v is ViewSchedule)).ToList();

                var viewNames = technicalViews.Select(v => v.Name).ToList();
                string vSummary = viewNames.Count > 0 ? $"{technicalViews.Count} vista(s): " + string.Join(", ", viewNames.Take(3)) : "Sin vistas generadas";
                if (viewNames.Count > 3) vSummary += $" (+{viewNames.Count - 3} más)";

                result.Add(new DoorAssemblyViewModel
                {
                    AssemblyId = ai.Id,
                    DoorInstanceId = doorInst.Id,
                    AssemblyName = aName,
                    Mark = mark,
                    FamilyName = famName,
                    TypeName = typeName,
                    Dimensions = dims,
                    TechnicalViewsCount = technicalViews.Count,
                    ViewsSummary = vSummary,
                    IsSelected = true
                });
            }

            return result.OrderBy(r => r.AssemblyName).ToList();
        }

        private static string GetDoorDimensionsString(Document doc, FamilyInstance d)
        {
            try
            {
                var pW = d.Symbol?.get_Parameter(BuiltInParameter.DOOR_WIDTH) ?? d.get_Parameter(BuiltInParameter.DOOR_WIDTH);
                var pH = d.Symbol?.get_Parameter(BuiltInParameter.DOOR_HEIGHT) ?? d.get_Parameter(BuiltInParameter.DOOR_HEIGHT);

                if (pW != null && pH != null)
                {
                    double w = pW.AsDouble() * 0.3048;
                    double h = pH.AsDouble() * 0.3048;
                    return $"{w:0.00} x {h:0.00} m";
                }
            }
            catch { }
            return "-";
        }

        /// <summary>
        /// Obtiene todos los tipos de cotas lineales del documento.
        /// </summary>
        public static List<DimensionType> GetLinearDimensionTypes(Document doc)
        {
            return new FilteredElementCollector(doc)
                .OfClass(typeof(DimensionType))
                .Cast<DimensionType>()
                .Where(dt => dt.StyleType == DimensionStyleType.Linear)
                .OrderBy(dt => dt.Name)
                .ToList();
        }

        /// <summary>
        /// Determina la orientación y propósito de una vista técnica de puerta.
        /// </summary>
        public static DoorAssemblyViewOrientation DetectViewOrientation(RevitView RevitView, FamilyInstance? door = null)
        {
            if (RevitView is ViewSchedule) return DoorAssemblyViewOrientation.Schedule;
            if (RevitView.ViewType == ViewType.ThreeD) return DoorAssemblyViewOrientation.ThreeD;

            string name = RevitView.Name.ToLowerInvariant();
            if (name.Contains("frontal") || name.Contains("front"))
                return DoorAssemblyViewOrientation.ElevationFront;
            if (name.Contains("posterior") || name.Contains("back") || name.Contains("rear"))
                return DoorAssemblyViewOrientation.ElevationBack;
            if (name.Contains("planta") || name.Contains("horizontal") || name.Contains("plan"))
                return DoorAssemblyViewOrientation.HorizontalDetail;
            if (name.Contains("secci") || name.Contains("lateral") || name.Contains("section") || name.Contains("left") || name.Contains("right"))
                return DoorAssemblyViewOrientation.SectionSide;

            // Análisis geométrico por vectores
            XYZ viewDir = RevitView.ViewDirection;
            if (Math.Abs(viewDir.Z) > 0.7)
            {
                return DoorAssemblyViewOrientation.HorizontalDetail;
            }

            if (door != null)
            {
                try
                {
                    XYZ facing = door.FacingOrientation;
                    if (Math.Abs(viewDir.DotProduct(facing)) > 0.6)
                    {
                        return viewDir.DotProduct(facing) < 0
                            ? DoorAssemblyViewOrientation.ElevationFront
                            : DoorAssemblyViewOrientation.ElevationBack;
                    }

                    XYZ hand = door.HandOrientation;
                    if (Math.Abs(viewDir.DotProduct(hand)) > 0.6)
                    {
                        return DoorAssemblyViewOrientation.SectionSide;
                    }
                }
                catch { }
            }

            return DoorAssemblyViewOrientation.ElevationFront;
        }

        #endregion

        #region Acotado de Vistas

        /// <summary>
        /// Acota de forma inteligente y jerarquizada una vista individual de ensamblaje o que contiene puertas.
        /// </summary>
        public static (int createdDims, List<string> logs) DimensionView(
            Document doc,
            RevitView RevitView,
            DimensionType? dimType = null,
            DoorDimensionOptions? options = null)
        {
            options ??= new DoorDimensionOptions();
            var logs = new List<string>();
            int createdCount = 0;

            if (RevitView == null || !RevitView.IsValidObject)
            {
                logs.Add("Vista no válida.");
                return (0, logs);
            }

            if (RevitView.ViewType == ViewType.ThreeD || RevitView is ViewSchedule)
            {
                logs.Add($"La vista '{RevitView.Name}' es 3D o Tabla; no admite cotas lineales 2D.");
                return (0, logs);
            }

            // Buscar la puerta asociada a la vista
            FamilyInstance? door = FindDoorForView(doc, RevitView);
            if (door == null)
            {
                logs.Add($"No se encontró ningún elemento de puerta en la vista '{RevitView.Name}'.");
                return (0, logs);
            }

            var orientation = DetectViewOrientation(RevitView, door);

            using (Transaction t = new Transaction(doc, $"Acotar Vista de Puerta: {RevitView.Name}"))
            {
                t.Start();

                // 1. Limpiar cotas existentes si fue solicitado
                if (options.ReplaceExistingDimensions)
                {
                    int cleaned = ClearExistingDoorDimensions(doc, RevitView, door);
                    if (cleaned > 0)
                    {
                        logs.Add($"Se removieron {cleaned} cota(s) previas en '{RevitView.Name}'.");
                    }
                }

                // 2. Resolver tipo de cota
                DimensionType? effectiveDimType = dimType;
                if (effectiveDimType == null && options.DimensionTypeId != null && options.DimensionTypeId != ElementId.InvalidElementId)
                {
                    effectiveDimType = doc.GetElement(options.DimensionTypeId) as DimensionType;
                }

                // 3. Obtener referencias y dimensiones según orientación
                try
                {
                    switch (orientation)
                    {
                        case DoorAssemblyViewOrientation.ElevationFront:
                        case DoorAssemblyViewOrientation.ElevationBack:
                            createdCount += DimensionElevationView(doc, RevitView, door, effectiveDimType, options, logs);
                            break;

                        case DoorAssemblyViewOrientation.HorizontalDetail:
                            createdCount += DimensionHorizontalDetailView(doc, RevitView, door, effectiveDimType, options, logs);
                            break;

                        case DoorAssemblyViewOrientation.SectionSide:
                            createdCount += DimensionSectionView(doc, RevitView, door, effectiveDimType, options, logs);
                            break;

                        default:
                            createdCount += DimensionElevationView(doc, RevitView, door, effectiveDimType, options, logs);
                            break;
                    }
                }
                catch (Exception ex)
                {
                    logs.Add($"Error al acotar vista '{RevitView.Name}': {ex.Message}");
                }

                t.Commit();
            }

            return (createdCount, logs);
        }

        /// <summary>
        /// Acota todas las vistas técnicas (Frontal, Posterior, Planta, Sección) de un ensamblaje de puerta.
        /// </summary>
        public static (int totalCreatedDims, List<string> logs) DimensionAssembly(
            Document doc,
            AssemblyInstance ai,
            DimensionType? dimType = null,
            DoorDimensionOptions? options = null)
        {
            options ??= new DoorDimensionOptions();
            var logs = new List<string>();
            int totalCreated = 0;

            if (ai == null || !ai.IsValidObject)
            {
                logs.Add("Ensamblaje no válido.");
                return (0, logs);
            }

            string aName = ai.AssemblyTypeName ?? ai.Name;
            logs.Add($"--- Acotando vistas del Ensamblaje: '{aName}' ---");

            var views = new FilteredElementCollector(doc)
                .OfClass(typeof(RevitView))
                .Cast<RevitView>()
                .Where(v => !v.IsTemplate && v.IsAssemblyView && v.AssociatedAssemblyInstanceId == ai.Id)
                .Where(v => v.ViewType != ViewType.ThreeD && !(v is ViewSchedule))
                .ToList();

            if (views.Count == 0)
            {
                logs.Add($"El ensamblaje '{aName}' no tiene vistas 2D generadas.");
                return (0, logs);
            }

            foreach (var v in views)
            {
                var (created, viewLogs) = DimensionView(doc, v, dimType, options);
                totalCreated += created;
                logs.AddRange(viewLogs);
            }

            logs.Add($"Fin ensamblaje '{aName}': {totalCreated} cotas generadas en {views.Count} vista(s).");
            return (totalCreated, logs);
        }

        /// <summary>
        /// Acota en lote todas las vistas técnicas de los ensamblajes de puertas seleccionados.
        /// </summary>
        public static (int totalCreatedDims, List<string> logs) DimensionBatchDoorAssemblies(
            Document doc,
            IList<AssemblyInstance> assemblies,
            DimensionType? dimType = null,
            DoorDimensionOptions? options = null)
        {
            options ??= new DoorDimensionOptions();
            var logs = new List<string>();
            int totalCreated = 0;

            if (assemblies == null || assemblies.Count == 0)
            {
                logs.Add("No se especificaron ensamblajes para acotar.");
                return (0, logs);
            }

            foreach (var ai in assemblies)
            {
                var (created, assLogs) = DimensionAssembly(doc, ai, dimType, options);
                totalCreated += created;
                logs.AddRange(assLogs);
            }

            return (totalCreated, logs);
        }

        #endregion

        #region Motores de Acotado por Orientación

        private static int DimensionElevationView(
            Document doc,
            RevitView RevitView,
            FamilyInstance door,
            DimensionType? dimType,
            DoorDimensionOptions options,
            List<string> logs)
        {
            int createdInView = 0;
            var extents = GetViewExtents(door, RevitView);
            double offsetFeet = GetOffsetFeet(RevitView, options.PaperOffsetMm);

            // ==========================================
            // 1. COTAS HORIZONTALES (ANCHO TOTAL Y JAMBAS)
            // ==========================================
            var horizRefs = ExtractHorizontalReferences(door, RevitView);

            // Cadena de Vanos y Jambas (Nivel 1 de apilamiento: más cercano al elemento)
            if (options.DimJambsAndOpening && horizRefs.JambAndOpeningChain.Count >= 3)
            {
                double yLine = extents.MaxY + (1.2 * offsetFeet);
                var line = CreateHorizontalLineInView(RevitView, extents.MinX, extents.MaxX, yLine);
                var refArray = BuildReferenceArray(horizRefs.JambAndOpeningChain);

                var d = TryCreateDimension(doc, RevitView, line, refArray, dimType);
                if (d != null)
                {
                    createdInView++;
                    logs.Add($"  [COTA] Cadena de vanos y jambas colocada ({horizRefs.JambAndOpeningChain.Count} referencias).");
                }
            }

            // Cota de Ancho Total (Nivel 2 de apilamiento: exterior)
            if (options.DimTotalWidth)
            {
                var widthRefs = new List<Reference>();
                if (horizRefs.LeftRef != null && horizRefs.RightRef != null)
                {
                    widthRefs.Add(horizRefs.LeftRef);
                    widthRefs.Add(horizRefs.RightRef);
                }

                if (widthRefs.Count == 2)
                {
                    double yLine = options.DimJambsAndOpening && horizRefs.JambAndOpeningChain.Count >= 3
                        ? extents.MaxY + (2.4 * offsetFeet)
                        : extents.MaxY + (1.2 * offsetFeet);

                    var line = CreateHorizontalLineInView(RevitView, extents.MinX, extents.MaxX, yLine);
                    var refArray = BuildReferenceArray(widthRefs);

                    var d = TryCreateDimension(doc, RevitView, line, refArray, dimType);
                    if (d != null)
                    {
                        createdInView++;
                        logs.Add($"  [COTA] Cota de Ancho Total Exterior colocada.");
                    }
                }
            }

            // ==========================================
            // 2. COTAS VERTICALES (ALTURA TOTAL Y LIBRE)
            // ==========================================
            var vertRefs = ExtractVerticalReferences(door, RevitView);

            // Cadena de Altura Libre y Cabezal (Nivel 1 de apilamiento a la izquierda)
            if (options.DimClearHeight && vertRefs.ClearAndHeadChain.Count >= 3)
            {
                double xLine = extents.MinX - (1.2 * offsetFeet);
                var line = CreateVerticalLineInView(RevitView, extents.MinY, extents.MaxY, xLine);
                var refArray = BuildReferenceArray(vertRefs.ClearAndHeadChain);

                var d = TryCreateDimension(doc, RevitView, line, refArray, dimType);
                if (d != null)
                {
                    createdInView++;
                    logs.Add($"  [COTA] Cadena de altura libre de paso y cabezal colocada.");
                }
            }

            // Cota de Altura Total (Nivel 2 de apilamiento a la izquierda)
            if (options.DimTotalHeight)
            {
                var heightRefs = new List<Reference>();
                if (vertRefs.BottomRef != null && vertRefs.TopRef != null)
                {
                    heightRefs.Add(vertRefs.BottomRef);
                    heightRefs.Add(vertRefs.TopRef);
                }

                if (heightRefs.Count == 2)
                {
                    double xLine = options.DimClearHeight && vertRefs.ClearAndHeadChain.Count >= 3
                        ? extents.MinX - (2.4 * offsetFeet)
                        : extents.MinX - (1.2 * offsetFeet);

                    var line = CreateVerticalLineInView(RevitView, extents.MinY, extents.MaxY, xLine);
                    var refArray = BuildReferenceArray(heightRefs);

                    var d = TryCreateDimension(doc, RevitView, line, refArray, dimType);
                    if (d != null)
                    {
                        createdInView++;
                        logs.Add($"  [COTA] Cota de Altura Total Exterior colocada.");
                    }
                }
            }

            return createdInView;
        }

        private static int DimensionHorizontalDetailView(
            Document doc,
            RevitView RevitView,
            FamilyInstance door,
            DimensionType? dimType,
            DoorDimensionOptions options,
            List<string> logs)
        {
            int createdInView = 0;
            var extents = GetViewExtents(door, RevitView);
            double offsetFeet = GetOffsetFeet(RevitView, options.PaperOffsetMm);

            // 1. Cota de Ancho Total y Jambas (Horizontal en planta)
            var horizRefs = ExtractHorizontalReferences(door, RevitView);

            if (options.DimJambsAndOpening && horizRefs.JambAndOpeningChain.Count >= 3)
            {
                double yLine = extents.MaxY + (1.2 * offsetFeet);
                var line = CreateHorizontalLineInView(RevitView, extents.MinX, extents.MaxX, yLine);
                var refArray = BuildReferenceArray(horizRefs.JambAndOpeningChain);

                var d = TryCreateDimension(doc, RevitView, line, refArray, dimType);
                if (d != null)
                {
                    createdInView++;
                    logs.Add("  [COTA] Planta: Cadena de jambas y vano libre colocada.");
                }
            }

            if (options.DimTotalWidth && horizRefs.LeftRef != null && horizRefs.RightRef != null)
            {
                double yLine = options.DimJambsAndOpening && horizRefs.JambAndOpeningChain.Count >= 3
                    ? extents.MaxY + (2.4 * offsetFeet)
                    : extents.MaxY + (1.2 * offsetFeet);

                var line = CreateHorizontalLineInView(RevitView, extents.MinX, extents.MaxX, yLine);
                var refArray = BuildReferenceArray(new List<Reference> { horizRefs.LeftRef, horizRefs.RightRef });

                var d = TryCreateDimension(doc, RevitView, line, refArray, dimType);
                if (d != null)
                {
                    createdInView++;
                    logs.Add("  [COTA] Planta: Cota de Ancho Total colocada.");
                }
            }

            // 2. Cota de Profundidad / Espesor del Marco (Vertical en planta)
            if (options.DimFrameDepth)
            {
                var depthRefs = ExtractDepthReferences(door, RevitView);
                if (depthRefs.Count >= 2)
                {
                    double xLine = extents.MaxX + (1.2 * offsetFeet);
                    var line = CreateVerticalLineInView(RevitView, extents.MinY, extents.MaxY, xLine);
                    var refArray = BuildReferenceArray(depthRefs);

                    var d = TryCreateDimension(doc, RevitView, line, refArray, dimType);
                    if (d != null)
                    {
                        createdInView++;
                        logs.Add("  [COTA] Planta: Cota de Profundidad / Espesor de Marco colocada.");
                    }
                }
            }

            return createdInView;
        }

        private static int DimensionSectionView(
            Document doc,
            RevitView RevitView,
            FamilyInstance door,
            DimensionType? dimType,
            DoorDimensionOptions options,
            List<string> logs)
        {
            int createdInView = 0;
            var extents = GetViewExtents(door, RevitView);
            double offsetFeet = GetOffsetFeet(RevitView, options.PaperOffsetMm);

            // 1. Cota de Altura Total y Libre
            var vertRefs = ExtractVerticalReferences(door, RevitView);

            if (options.DimClearHeight && vertRefs.ClearAndHeadChain.Count >= 3)
            {
                double xLine = extents.MinX - (1.2 * offsetFeet);
                var line = CreateVerticalLineInView(RevitView, extents.MinY, extents.MaxY, xLine);
                var refArray = BuildReferenceArray(vertRefs.ClearAndHeadChain);

                var d = TryCreateDimension(doc, RevitView, line, refArray, dimType);
                if (d != null)
                {
                    createdInView++;
                    logs.Add("  [COTA] Sección: Altura libre y cabezal colocada.");
                }
            }

            if (options.DimTotalHeight && vertRefs.BottomRef != null && vertRefs.TopRef != null)
            {
                double xLine = options.DimClearHeight && vertRefs.ClearAndHeadChain.Count >= 3
                    ? extents.MinX - (2.4 * offsetFeet)
                    : extents.MinX - (1.2 * offsetFeet);

                var line = CreateVerticalLineInView(RevitView, extents.MinY, extents.MaxY, xLine);
                var refArray = BuildReferenceArray(new List<Reference> { vertRefs.BottomRef, vertRefs.TopRef });

                var d = TryCreateDimension(doc, RevitView, line, refArray, dimType);
                if (d != null)
                {
                    createdInView++;
                    logs.Add("  [COTA] Sección: Cota de Altura Total colocada.");
                }
            }

            // 2. Cota de Profundidad de Marco (Horizontal en sección)
            if (options.DimFrameDepth)
            {
                var depthRefs = ExtractHorizontalReferences(door, RevitView);
                if (depthRefs.LeftRef != null && depthRefs.RightRef != null)
                {
                    double yLine = extents.MaxY + (1.2 * offsetFeet);
                    var line = CreateHorizontalLineInView(RevitView, extents.MinX, extents.MaxX, yLine);
                    var refArray = BuildReferenceArray(new List<Reference> { depthRefs.LeftRef, depthRefs.RightRef });

                    var d = TryCreateDimension(doc, RevitView, line, refArray, dimType);
                    if (d != null)
                    {
                        createdInView++;
                        logs.Add("  [COTA] Sección: Espesor / Fondo de Marco colocado.");
                    }
                }
            }

            return createdInView;
        }

        #endregion

        #region Extracción Paramétrica y Geométrica de Referencias

        private class HorizontalRefData
        {
            public Reference? LeftRef { get; set; }
            public Reference? RightRef { get; set; }
            public List<Reference> JambAndOpeningChain { get; set; } = new List<Reference>();
        }

        private class VerticalRefData
        {
            public Reference? BottomRef { get; set; }
            public Reference? TopRef { get; set; }
            public List<Reference> ClearAndHeadChain { get; set; } = new List<Reference>();
        }

        private class ExtractedRefPoint
        {
            public Reference Ref { get; set; } = null!;
            public double Position { get; set; }
            public double AreaOrLength { get; set; }
        }

        private static HorizontalRefData ExtractHorizontalReferences(FamilyInstance door, RevitView RevitView)
        {
            var data = new HorizontalRefData();

            // 1. Extraer referencias de planos de familia
            try
            {
                var lList = door.GetReferences(FamilyInstanceReferenceType.Left);
                if (lList != null && lList.Count > 0) data.LeftRef = lList[0];

                var rList = door.GetReferences(FamilyInstanceReferenceType.Right);
                if (rList != null && rList.Count > 0) data.RightRef = rList[0];
            }
            catch { }

            // 2. Extraer caras verticales geométricas perpendiculares al ancho
            var points = new List<ExtractedRefPoint>();
            CollectPlanarFacesAlongAxis(door, RevitView, RevitView.RightDirection, points);

            if (points.Count >= 2)
            {
                // Ordenar de izquierda a derecha
                points = points.OrderBy(p => p.Position).ToList();

                if (data.LeftRef == null) data.LeftRef = points.First().Ref;
                if (data.RightRef == null) data.RightRef = points.Last().Ref;

                // Si hay caras intermedias (jambas interiores), construir cadena
                if (points.Count >= 4)
                {
                    data.JambAndOpeningChain = points.Select(p => p.Ref).ToList();
                }
                else
                {
                    data.JambAndOpeningChain = new List<Reference> { points.First().Ref, points.Last().Ref };
                }
            }
            else if (data.LeftRef != null && data.RightRef != null)
            {
                data.JambAndOpeningChain = new List<Reference> { data.LeftRef, data.RightRef };
            }

            return data;
        }

        private static VerticalRefData ExtractVerticalReferences(FamilyInstance door, RevitView RevitView)
        {
            var data = new VerticalRefData();

            // 1. Extraer referencias de planos de familia
            try
            {
                var bList = door.GetReferences(FamilyInstanceReferenceType.Bottom);
                if (bList != null && bList.Count > 0) data.BottomRef = bList[0];

                var tList = door.GetReferences(FamilyInstanceReferenceType.Top);
                if (tList != null && tList.Count > 0) data.TopRef = tList[0];
            }
            catch { }

            // 2. Extraer caras horizontales geométricas perpendiculares a la altura
            var points = new List<ExtractedRefPoint>();
            CollectPlanarFacesAlongAxis(door, RevitView, RevitView.UpDirection, points);

            if (points.Count >= 2)
            {
                points = points.OrderBy(p => p.Position).ToList();

                if (data.BottomRef == null) data.BottomRef = points.First().Ref;
                if (data.TopRef == null) data.TopRef = points.Last().Ref;

                if (points.Count >= 3)
                {
                    data.ClearAndHeadChain = points.Select(p => p.Ref).ToList();
                }
                else
                {
                    data.ClearAndHeadChain = new List<Reference> { points.First().Ref, points.Last().Ref };
                }
            }
            else if (data.BottomRef != null && data.TopRef != null)
            {
                data.ClearAndHeadChain = new List<Reference> { data.BottomRef, data.TopRef };
            }

            return data;
        }

        private static List<Reference> ExtractDepthReferences(FamilyInstance door, RevitView RevitView)
        {
            var points = new List<ExtractedRefPoint>();
            CollectPlanarFacesAlongAxis(door, RevitView, RevitView.UpDirection, points);

            if (points.Count >= 2)
            {
                points = points.OrderBy(p => p.Position).ToList();
                return new List<Reference> { points.First().Ref, points.Last().Ref };
            }

            // Fallback: buscar planos Front y Back
            try
            {
                var f = door.GetReferences(FamilyInstanceReferenceType.Front);
                var b = door.GetReferences(FamilyInstanceReferenceType.Back);
                if (f != null && f.Count > 0 && b != null && b.Count > 0)
                {
                    return new List<Reference> { f[0], b[0] };
                }
            }
            catch { }

            return new List<Reference>();
        }

        private static void CollectPlanarFacesAlongAxis(
            FamilyInstance door,
            RevitView RevitView,
            XYZ axis,
            List<ExtractedRefPoint> targetList)
        {
            Options opt = new Options
            {
                ComputeReferences = true,
                IncludeNonVisibleObjects = false,
                
                View = RevitView
            };

            GeometryElement? geom = null;
            try { geom = door.get_Geometry(opt); } catch { }

            if (geom == null || !geom.Any())
            {
                opt.View = null;
                try { geom = door.get_Geometry(opt); } catch { }
            }

            if (geom == null) return;

            XYZ normAxis = axis.Normalize();
            XYZ viewDir = RevitView.ViewDirection.Normalize();

            var rawPoints = new List<ExtractedRefPoint>();
            TraverseGeometrySolids(geom, door, (pf, refItem) =>
            {
                XYZ n = pf.FaceNormal.Normalize();

                // Debe ser perpendicular o alineada al eje
                if (Math.Abs(n.DotProduct(normAxis)) > 0.85 && Math.Abs(n.DotProduct(viewDir)) < 0.25)
                {
                    double pos = (pf.Origin - RevitView.Origin).DotProduct(normAxis);
                    rawPoints.Add(new ExtractedRefPoint
                    {
                        Ref = refItem,
                        Position = pos,
                        AreaOrLength = pf.Area
                    });
                }
            });

            // Agrupar puntos con posición muy cercana (< 5 mm = 0.016 ft)
            var groups = rawPoints
                .GroupBy(p => Math.Round(p.Position * 60.0) / 60.0) // Redondeo a incrementos de ~5mm
                .Select(g => g.OrderByDescending(p => p.AreaOrLength).First())
                .OrderBy(p => p.Position)
                .ToList();

            targetList.AddRange(groups);
        }

        private static void TraverseGeometrySolids(
            GeometryElement geomElem,
            FamilyInstance door,
            Action<PlanarFace, Reference> onFace)
        {
            foreach (GeometryObject obj in geomElem)
            {
                if (obj is Solid solid && solid.Faces.Size > 0 && solid.Volume > 1e-6)
                {
                    foreach (Face f in solid.Faces)
                    {
                        if (f is PlanarFace pf && pf.Reference != null)
                        {
                            onFace(pf, pf.Reference);
                        }
                    }
                }
                else if (obj is GeometryInstance gi)
                {
                    GeometryElement? instGeom = null;
                    try { instGeom = gi.GetInstanceGeometry(); } catch { }

                    if (instGeom != null)
                    {
                        foreach (GeometryObject instObj in instGeom)
                        {
                            if (instObj is Solid instSolid && instSolid.Faces.Size > 0 && instSolid.Volume > 1e-6)
                            {
                                foreach (Face f in instSolid.Faces)
                                {
                                    if (f is PlanarFace pf && pf.Reference != null)
                                    {
                                        onFace(pf, pf.Reference);
                                    }
                                }
                            }
                        }
                    }
                }
            }
        }

        #endregion

        #region Geometría de Vistas y Cálculo de Líneas

        private class ViewExtents
        {
            public double MinX { get; set; }
            public double MaxX { get; set; }
            public double MinY { get; set; }
            public double MaxY { get; set; }
        }

        private static ViewExtents GetViewExtents(FamilyInstance door, RevitView RevitView)
        {
            var ext = new ViewExtents
            {
                MinX = -1.5,
                MaxX = 1.5,
                MinY = 0.0,
                MaxY = 7.0
            };

            try
            {
                BoundingBoxXYZ? bbox = door.get_BoundingBox(RevitView) ?? door.get_BoundingBox(null);
                if (bbox != null)
                {
                    XYZ[] corners = new XYZ[]
                    {
                        new XYZ(bbox.Min.X, bbox.Min.Y, bbox.Min.Z),
                        new XYZ(bbox.Max.X, bbox.Min.Y, bbox.Min.Z),
                        new XYZ(bbox.Min.X, bbox.Max.Y, bbox.Min.Z),
                        new XYZ(bbox.Max.X, bbox.Max.Y, bbox.Min.Z),
                        new XYZ(bbox.Min.X, bbox.Min.Y, bbox.Max.Z),
                        new XYZ(bbox.Max.X, bbox.Min.Y, bbox.Max.Z),
                        new XYZ(bbox.Min.X, bbox.Max.Y, bbox.Max.Z),
                        new XYZ(bbox.Max.X, bbox.Max.Y, bbox.Max.Z),
                    };

                    Transform trans = bbox.Transform;
                    double minU = double.MaxValue, maxU = double.MinValue;
                    double minV = double.MaxValue, maxV = double.MinValue;

                    foreach (var c in corners)
                    {
                        XYZ ptWorld = trans.OfPoint(c);
                        double u = (ptWorld - RevitView.Origin).DotProduct(RevitView.RightDirection);
                        double v = (ptWorld - RevitView.Origin).DotProduct(RevitView.UpDirection);

                        if (u < minU) minU = u;
                        if (u > maxU) maxU = u;
                        if (v < minV) minV = v;
                        if (v > maxV) maxV = v;
                    }

                    if (maxU > minU && maxV > minV)
                    {
                        ext.MinX = minU;
                        ext.MaxX = maxU;
                        ext.MinY = minV;
                        ext.MaxY = maxV;
                    }
                }
            }
            catch { }

            return ext;
        }

        private static double GetOffsetFeet(RevitView RevitView, double paperMm)
        {
            int scale = RevitView.Scale > 0 ? RevitView.Scale : 50;
            // 1 mm = 0.001 m = 0.00328084 ft
            return (paperMm / 1000.0) * scale * 3.28084;
        }

        private static Line CreateHorizontalLineInView(RevitView RevitView, double minX, double maxX, double yCoord)
        {
            XYZ p1 = RevitView.Origin + (RevitView.RightDirection * (minX - 0.5)) + (RevitView.UpDirection * yCoord);
            XYZ p2 = RevitView.Origin + (RevitView.RightDirection * (maxX + 0.5)) + (RevitView.UpDirection * yCoord);
            return Line.CreateBound(p1, p2);
        }

        private static Line CreateVerticalLineInView(RevitView RevitView, double minY, double maxY, double xCoord)
        {
            XYZ p1 = RevitView.Origin + (RevitView.RightDirection * xCoord) + (RevitView.UpDirection * (minY - 0.5));
            XYZ p2 = RevitView.Origin + (RevitView.RightDirection * xCoord) + (RevitView.UpDirection * (maxY + 0.5));
            return Line.CreateBound(p1, p2);
        }

        private static ReferenceArray BuildReferenceArray(IEnumerable<Reference> refs)
        {
            var arr = new ReferenceArray();
            foreach (var r in refs)
            {
                if (r != null) arr.Append(r);
            }
            return arr;
        }

        private static Dimension? TryCreateDimension(
            Document doc,
            RevitView RevitView,
            Line line,
            ReferenceArray refArray,
            DimensionType? dimType)
        {
            if (refArray.Size < 2) return null;

            // Intento 1: Con el estilo de cota solicitado
            if (dimType != null)
            {
                try
                {
                    return doc.Create.NewDimension(RevitView, line, refArray, dimType);
                }
                catch { }
            }

            // Intento 2: Con el estilo por defecto del proyecto
            try
            {
                return doc.Create.NewDimension(RevitView, line, refArray);
            }
            catch { }

            // Intento 3: Si la cadena falló (por ejemplo, 3 o 4 referencias no coplanares), intentar solo extremos
            if (refArray.Size > 2)
            {
                try
                {
                    var twoRefs = new ReferenceArray();
                    int count = 0;
                    Reference? first = null, last = null;
                    foreach (Reference r in refArray)
                    {
                        if (count == 0) first = r;
                        last = r;
                        count++;
                    }

                    if (first != null && last != null)
                    {
                        twoRefs.Append(first);
                        twoRefs.Append(last);
                        if (dimType != null)
                        {
                            try { return doc.Create.NewDimension(RevitView, line, twoRefs, dimType); } catch { }
                        }
                        return doc.Create.NewDimension(RevitView, line, twoRefs);
                    }
                }
                catch { }
            }

            return null;
        }

        private static FamilyInstance? FindDoorForView(Document doc, RevitView RevitView)
        {
            // 1. Si es vista de ensamblaje, obtener el miembro puerta del ensamblaje
            if (RevitView.IsAssemblyView && RevitView.AssociatedAssemblyInstanceId != ElementId.InvalidElementId)
            {
                var ai = doc.GetElement(RevitView.AssociatedAssemblyInstanceId) as AssemblyInstance;
                if (ai != null)
                {
                    foreach (var mId in ai.GetMemberIds())
                    {
                        var elem = doc.GetElement(mId);
                        if (elem is FamilyInstance fi && fi.Category != null &&
                            fi.Category.Id == new ElementId(BuiltInCategory.OST_Doors))
                        {
                            return fi;
                        }
                    }
                }
            }

            // 2. Si no es de ensamblaje, buscar puertas visibles en la vista
            return new FilteredElementCollector(doc, RevitView.Id)
                .OfCategory(BuiltInCategory.OST_Doors)
                .WhereElementIsNotElementType()
                .Cast<FamilyInstance>()
                .FirstOrDefault();
        }

        private static int ClearExistingDoorDimensions(Document doc, RevitView RevitView, FamilyInstance door)
        {
            int deleted = 0;
            try
            {
                var dims = new FilteredElementCollector(doc, RevitView.Id)
                    .OfClass(typeof(Dimension))
                    .Cast<Dimension>()
                    .Where(d => !d.IsLocked)
                    .ToList();

                foreach (var d in dims)
                {
                    bool touchesDoor = false;
                    try
                    {
                        foreach (Reference r in d.References)
                        {
                            if (r.ElementId == door.Id || r.ElementId == door.GetTypeId())
                            {
                                touchesDoor = true;
                                break;
                            }
                        }
                    }
                    catch { }

                    if (touchesDoor)
                    {
                        try
                        {
                            doc.Delete(d.Id);
                            deleted++;
                        }
                        catch { }
                    }
                }
            }
            catch { }

            return deleted;
        }

        #endregion
    }
}
