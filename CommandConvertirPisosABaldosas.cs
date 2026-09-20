using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Reflection;
using Autodesk.Revit.Attributes;
using Autodesk.Revit.DB;
using Autodesk.Revit.UI;
using Autodesk.Revit.UI.Selection;

namespace RevitToSupabasePlugin
{
    public class FloorSelectionFilter : ISelectionFilter
    {
        public bool AllowElement(Element elem)
        {
            return elem is Floor;
        }

        public bool AllowReference(Reference reference, XYZ position)
        {
            return true;
        }
    }

    public class TilingHostSelectionFilter : ISelectionFilter
    {
        public bool AllowElement(Element elem)
        {
            return elem is Floor || elem is Wall;
        }

        public bool AllowReference(Reference reference, XYZ position)
        {
            return true;
        }
    }

    [Transaction(TransactionMode.Manual)]
    public class CommandConvertirPisosABaldosas : IExternalCommand
    {
        public Result Execute(ExternalCommandData commandData, ref string message, ElementSet elements)
        {
            UIDocument uidoc = commandData.Application.ActiveUIDocument;
            Document doc = uidoc.Document;

            try
            {
                // 1. Obtener selección de Suelos (Floors) y/o Muros (Walls)
                IList<Reference> selectedRefs;
                var currentSelection = uidoc.Selection.GetElementIds();
                var preselectedElements = currentSelection
                    .Select(id => doc.GetElement(id))
                    .Where(e => e is Floor || e is Wall)
                    .ToList();

                if (preselectedElements.Count > 0)
                {
                    selectedRefs = preselectedElements.Select(e => new Reference(e)).ToList();
                }
                else
                {
                    selectedRefs = uidoc.Selection.PickObjects(
                        ObjectType.Element,
                        new TilingHostSelectionFilter(),
                        "Selecciona los Suelos (Floors) o Muros (Walls) que deseas enchapar.");
                }

                if (selectedRefs.Count == 0) return Result.Cancelled;

                // 2. Extraer geometría y capas compuestas de los anfitriones seleccionados
                List<TilingHostInfo> hostInfos = new List<TilingHostInfo>();

                foreach (Reference r in selectedRefs)
                {
                    Element elem = doc.GetElement(r);
                    if (elem is Floor floor)
                    {
                        TilingHostInfo? info = ExtractFloorInfo(floor);
                        if (info != null && info.OuterBoundaryUV.Count >= 3)
                        {
                            hostInfos.Add(info);
                        }
                    }
                    else if (elem is Wall wall)
                    {
                        TilingHostInfo? info = ExtractWallInfo(wall);
                        if (info != null && info.OuterBoundaryUV.Count >= 3)
                        {
                            hostInfos.Add(info);
                        }
                    }
                }

                if (hostInfos.Count == 0)
                {
                    Autodesk.Revit.UI.TaskDialog.Show("Aviso", "No se pudo extraer la geometría de contorno de los elementos seleccionados.");
                    return Result.Cancelled;
                }

                // 3. Abrir Ventana de Configuración y Despiece
                ConvertirPisosWindow window = new ConvertirPisosWindow(hostInfos, doc);
                bool? dialogRes = window.ShowDialog();

                if (dialogRes != true || !window.IsConfirmed)
                {
                    return Result.Cancelled;
                }

                // 4. Cargar familias Face-Based reales desde Familias/Floors-Tiles/
                FamilySymbol? tileSymbol = LoadOrFindFamilySymbol(doc, "Tiles Base", new[] { "Tiles Base", "Base Tiles", "TilesBase", "BaldosaBase", "LadrilloBaseExtruH" });
                FamilySymbol? pegaSymbol = LoadOrFindFamilySymbol(doc, "Base Tiles Pega", new[] { "Base Tiles Pega", "Tiles Pega", "TilesPegaBase", "PegaH" });

                // 5. Instanciar en Revit dentro de una Transacción
                using (Transaction t = new Transaction(doc, "Generar Enchapes, Capas y Fragua"))
                {
                    t.Start();

                    ElementId categoryId = new ElementId(BuiltInCategory.OST_GenericModel);
                    ElementId tileMatId = window.TileMaterial?.Id ?? ElementId.InvalidElementId;
                    ElementId revoqueMatId = window.RevoqueMaterial?.Id ?? ElementId.InvalidElementId;
                    ElementId beddingMatId = window.BeddingMaterial?.Id ?? ElementId.InvalidElementId;
                    ElementId groutMatId = window.GroutMaterial?.Id ?? ElementId.InvalidElementId;
                    ElementId skirtingMatId = window.SkirtingMaterial?.Id ?? tileMatId;

                    int createdTilesCount = 0;
                    int cutTilesCount = 0;

                    double revoqueFt = window.ModelRevoque ? (window.RevoqueThickCm / 100.0) * 3.28084 : 0.0;
                    double beddingFt = window.ModelBeddingMortar ? (window.BeddingThickMm / 1000.0) * 3.28084 : 0.0;

                    // A. Modelar Capa de Revoque / Pañete Base continua si está habilitada
                    if (window.ModelRevoque)
                    {
                        foreach (var host in window.SelectedHosts)
                        {
                            Solid? revSolid = CreatePrismFromPolygon(host.OuterBoundary3D, host.Normal3D, revoqueFt);
                            if (revSolid != null)
                            {
                                DirectShape dsRev = DirectShape.CreateElement(doc, categoryId);
                                dsRev.SetShape(new GeometryObject[] { revSolid });
                                dsRev.Name = SanitizeRevitName($"Mortero Base - {host.Name}", "Mortero Base");

                                Parameter pComm = dsRev.get_Parameter(BuiltInParameter.ALL_MODEL_INSTANCE_COMMENTS);
                                if (pComm != null && !pComm.IsReadOnly) pComm.Set("ENCHAPE-MORTERO-BASE");

                                if (revoqueMatId != ElementId.InvalidElementId)
                                {
                                    Parameter pMat = dsRev.get_Parameter(BuiltInParameter.MATERIAL_ID_PARAM);
                                    if (pMat != null && !pMat.IsReadOnly) pMat.Set(revoqueMatId);
                                }
                            }
                        }
                    }

                    // B. Modelar Cama de Pega / Adhesivo continua si está habilitada
                    if (window.ModelBeddingMortar)
                    {
                        foreach (var host in window.SelectedHosts)
                        {
                            List<XYZ> bedBoundary = host.OuterBoundary3D.Select(p => p + host.Normal3D * revoqueFt).ToList();
                            Solid? bedSolid = CreatePrismFromPolygon(bedBoundary, host.Normal3D, beddingFt);
                            if (bedSolid != null)
                            {
                                DirectShape dsBed = DirectShape.CreateElement(doc, categoryId);
                                dsBed.SetShape(new GeometryObject[] { bedSolid });
                                dsBed.Name = SanitizeRevitName($"Cama de Pega - {host.Name}", "Cama de Pega");

                                Parameter pComm = dsBed.get_Parameter(BuiltInParameter.ALL_MODEL_INSTANCE_COMMENTS);
                                if (pComm != null && !pComm.IsReadOnly) pComm.Set("ENCHAPE-PEGA-CAMA");

                                if (beddingMatId != ElementId.InvalidElementId)
                                {
                                    Parameter pMat = dsBed.get_Parameter(BuiltInParameter.MATERIAL_ID_PARAM);
                                    if (pMat != null && !pMat.IsReadOnly) pMat.Set(beddingMatId);
                                }
                            }
                        }
                    }

                    // C. Modelar Baldosas / Enchapes (Face-Based FamilyInstance para enteras, DirectShape para cortes)
                    if (window.ModelTiles)
                    {
                        foreach (var tile in window.LayoutTiles)
                        {
                            if (tile.PolygonVertices3D.Count < 3) continue;

                            bool placedAsFamily = false;
                            if (!tile.IsCut && tileSymbol != null)
                            {
                                try
                                {
                                    if (!tileSymbol.IsActive) tileSymbol.Activate();
                                    FamilyInstance? inst = null;

                                    // Intento 1: Face-Based con Face Reference
                                    if (tile.HostFaceRef != null)
                                    {
                                        try
                                        {
                                            inst = doc.Create.NewFamilyInstance(
                                                tile.HostFaceRef,
                                                tile.Center3D,
                                                tile.DirectionU3D,
                                                tileSymbol);
                                        }
                                        catch { }
                                    }

                                    // Intento 2: Ubicación por nivel
                                    if (inst == null)
                                    {
                                        Level? level = hostInfos.FirstOrDefault()?.Level;
                                        inst = doc.Create.NewFamilyInstance(
                                            tile.Center3D,
                                            tileSymbol,
                                            level,
                                            Autodesk.Revit.DB.Structure.StructuralType.NonStructural);
                                    }

                                    if (inst != null)
                                    {
                                        // Rotar la baldosa según el ángulo del patrón si es necesario
                                        if (Math.Abs(tile.RotationRad) > 1e-4)
                                        {
                                            try
                                            {
                                                Line rotAxis = Line.CreateUnbound(tile.Center3D, tile.Normal3D);
                                                ElementTransformUtils.RotateElement(doc, inst.Id, rotAxis, tile.RotationRad);
                                            }
                                            catch { }
                                        }

                                        // Ajustar parámetros paramétricos de la familia editable
                                        SetParameterValue(inst, new[] { "Length", "Largo", "Depth", "Longitud", "Longitud_Baldosa" }, tile.Length_Ft);
                                        SetParameterValue(inst, new[] { "Width", "Ancho", "Ancho_Baldosa" }, tile.Width_Ft);
                                        SetParameterValue(inst, new[] { "Height", "Thickness", "Espesor", "Alto", "Espesor_Baldosa" }, tile.Thickness_Ft);

                                        Parameter pComm = inst.get_Parameter(BuiltInParameter.ALL_MODEL_INSTANCE_COMMENTS);
                                        if (pComm != null && !pComm.IsReadOnly) pComm.Set("ENCHAPE-BALDOSA-ENTERA");

                                        if (tileMatId != ElementId.InvalidElementId)
                                        {
                                            Parameter pMat = inst.get_Parameter(BuiltInParameter.MATERIAL_ID_PARAM);
                                            if (pMat != null && !pMat.IsReadOnly) pMat.Set(tileMatId);
                                        }

                                        createdTilesCount++;
                                        placedAsFamily = true;
                                    }
                                }
                                catch { }
                            }

                            // Si no se instanció como familia o es pieza cortada, crear DirectShape sólido
                            if (!placedAsFamily)
                            {
                                Solid? tileSolid = CreatePrismFromPolygon(tile.PolygonVertices3D, tile.Normal3D, tile.Thickness_Ft);
                                if (tileSolid != null)
                                {
                                    DirectShape ds = DirectShape.CreateElement(doc, categoryId);
                                    ds.SetShape(new GeometryObject[] { tileSolid });
                                    ds.Name = tile.IsCut ? "Baldosa Cortada" : "Baldosa";

                                    Parameter pComm = ds.get_Parameter(BuiltInParameter.ALL_MODEL_INSTANCE_COMMENTS);
                                    if (pComm != null && !pComm.IsReadOnly)
                                    {
                                        pComm.Set(tile.IsCut ? "ENCHAPE-BALDOSA-CORTADA" : "ENCHAPE-BALDOSA-ENTERA");
                                    }

                                    if (tileMatId != ElementId.InvalidElementId)
                                    {
                                        Parameter pMat = ds.get_Parameter(BuiltInParameter.MATERIAL_ID_PARAM);
                                        if (pMat != null && !pMat.IsReadOnly) pMat.Set(tileMatId);
                                    }

                                    if (tile.IsCut) cutTilesCount++;
                                    else createdTilesCount++;
                                }
                            }
                        }
                    }

                    // D. Modelar Juntas / Fragua 3D entre baldosas en dos direcciones (Grid bidireccional y perimetral)
                    int jointCount = 0;
                    if (window.GenerateJointMesh && window.LayoutJoints.Count > 0)
                    {
                        foreach (var j in window.LayoutJoints)
                        {
                            if (j.PolygonVertices3D.Count < 3) continue;

                            Solid? jSolid = CreatePrismFromPolygon(j.PolygonVertices3D, j.Normal3D, j.DepthFt);
                            if (jSolid != null)
                            {
                                DirectShape dsJ = DirectShape.CreateElement(doc, categoryId);
                                dsJ.SetShape(new GeometryObject[] { jSolid });
                                dsJ.Name = j.IsPerimeter ? "Junta Perimetral" : "Fragua de Junta";

                                Parameter pComm = dsJ.get_Parameter(BuiltInParameter.ALL_MODEL_INSTANCE_COMMENTS);
                                if (pComm != null && !pComm.IsReadOnly) pComm.Set(j.IsPerimeter ? "ENCHAPE-JUNTA-PERIMETRAL" : "ENCHAPE-PEGA-FRAGUA");

                                if (groutMatId != ElementId.InvalidElementId)
                                {
                                    Parameter pMat = dsJ.get_Parameter(BuiltInParameter.MATERIAL_ID_PARAM);
                                    if (pMat != null && !pMat.IsReadOnly) pMat.Set(groutMatId);
                                }
                                jointCount++;
                            }
                        }
                    }

                    // E. Modelar Zócalos perimetrales sobre nivel de piso terminado
                    int skirtingSegCount = 0;
                    if (window.EnableSkirting && window.SkirtingSegments.Count > 0)
                    {
                        foreach (var seg in window.SkirtingSegments)
                        {
                            Solid? skSolid = CreateSkirtingSolid(seg);
                            if (skSolid != null)
                            {
                                DirectShape dsSk = DirectShape.CreateElement(doc, categoryId);
                                dsSk.SetShape(new GeometryObject[] { skSolid });
                                dsSk.Name = "Zocalo Perimetral";

                                Parameter pComm = dsSk.get_Parameter(BuiltInParameter.ALL_MODEL_INSTANCE_COMMENTS);
                                if (pComm != null && !pComm.IsReadOnly) pComm.Set("ZOCALO-PERIMETRAL");

                                if (skirtingMatId != ElementId.InvalidElementId)
                                {
                                    Parameter pMat = dsSk.get_Parameter(BuiltInParameter.MATERIAL_ID_PARAM);
                                    if (pMat != null && !pMat.IsReadOnly) pMat.Set(skirtingMatId);
                                }
                                skirtingSegCount++;
                            }
                        }
                    }

                    t.Commit();

                    Autodesk.Revit.UI.TaskDialog.Show(
                        "Enchapes Generados con Éxito",
                        $"Se ha completado el modelado en Revit:\n\n" +
                        $"• Baldosas Enteras (Tiles Base): {createdTilesCount} unidades\n" +
                        $"• Baldosas Cortadas: {cutTilesCount} unidades\n" +
                        $"• Fragua / Juntas 3D (Base Tiles Pega): {jointCount} tramos\n" +
                        $"• Capa Revoque: {(window.ModelRevoque ? "Modelada (" + window.RevoqueThickCm + " cm)" : "No")}\n" +
                        $"• Cama de Pega: {(window.ModelBeddingMortar ? "Modelada (" + window.BeddingThickMm + " mm)" : "No")}\n" +
                        $"• Zócalos sobre Piso: {skirtingSegCount} tramos\n" +
                        $"• Categoría: Modelos Genéricos (OST_GenericModel)");
                }

                return Result.Succeeded;
            }
            catch (Exception ex)
            {
                message = ex.Message;
                return Result.Failed;
            }
        }

        private FamilySymbol? LoadOrFindFamilySymbol(Document doc, string familyName, string[] alternateNames)
        {
            var family = new FilteredElementCollector(doc)
                .OfClass(typeof(Family))
                .Cast<Family>()
                .FirstOrDefault(f => alternateNames.Any(n => f.Name.IndexOf(n, StringComparison.OrdinalIgnoreCase) >= 0));

            if (family == null)
            {
                string addinFolder = Path.GetDirectoryName(Assembly.GetExecutingAssembly().Location) ?? "";
                string[] searchPaths = new[]
                {
                    Path.Combine(addinFolder, "Familias", "Floors-Tiles", $"{familyName}.rfa"),
                    Path.Combine(addinFolder, $"{familyName}.rfa"),
                    Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData), @"Autodesk\Revit\Addins\2027\Familias\Floors-Tiles", $"{familyName}.rfa"),
                    Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData), @"Autodesk\Revit\Addins\2027", $"{familyName}.rfa"),
                    $@"C:\Users\Alejandro\Documents\GitHub\reviewPlugIn\Familias\Floors-Tiles\{familyName}.rfa",
                    $@"C:\Users\Alejandro\Documents\GitHub\reviewPlugIn\{familyName}.rfa"
                };

                string? foundPath = searchPaths.FirstOrDefault(File.Exists);
                if (foundPath != null)
                {
                    using (Transaction tLoad = new Transaction(doc, $"Cargar {familyName}"))
                    {
                        tLoad.Start();
                        doc.LoadFamily(foundPath, out family);
                        tLoad.Commit();
                    }
                }
            }

            if (family != null)
            {
                ISet<ElementId> symbolIds = family.GetFamilySymbolIds();
                if (symbolIds.Count > 0)
                {
                    return doc.GetElement(symbolIds.First()) as FamilySymbol;
                }
            }

            return null;
        }

        private void SetParameterValue(Element elem, string[] paramNames, double val)
        {
            foreach (string name in paramNames)
            {
                Parameter p = elem.LookupParameter(name);
                if (p != null && !p.IsReadOnly)
                {
                    p.Set(val);
                    return;
                }
            }
        }

        private TilingHostInfo? ExtractFloorInfo(Floor floor)
        {
            Document doc = floor.Document;
            Options opt = new Options { DetailLevel = ViewDetailLevel.Fine, IncludeNonVisibleObjects = false };
            GeometryElement geomElem = floor.get_Geometry(opt);
            if (geomElem == null) return null;

            PlanarFace? topFace = null;
            double maxZ = -1e9;

            foreach (GeometryObject obj in geomElem)
            {
                if (obj is Solid solid && solid.Faces.Size > 0)
                {
                    foreach (Face face in solid.Faces)
                    {
                        if (face is PlanarFace pf && pf.FaceNormal.IsAlmostEqualTo(XYZ.BasisZ, 0.1))
                        {
                            if (pf.Origin.Z > maxZ)
                            {
                                maxZ = pf.Origin.Z;
                                topFace = pf;
                            }
                        }
                    }
                }
            }

            if (topFace == null) return null;

            List<XYZ> outer3D = new List<XYZ>();
            List<List<XYZ>> inner3DList = new List<List<XYZ>>();
            List<Curve> outerCurves = new List<Curve>();

            var edgeLoops = topFace.EdgeLoops;
            for (int i = 0; i < edgeLoops.Size; i++)
            {
                EdgeArray loop = edgeLoops.get_Item(i);
                List<XYZ> loopPoints = new List<XYZ>();

                foreach (Edge edge in loop)
                {
                    Curve curve = edge.AsCurve();
                    if (i == 0) outerCurves.Add(curve);
                    IList<XYZ> tess = curve.Tessellate();
                    for (int k = 0; k < tess.Count - 1; k++)
                    {
                        loopPoints.Add(tess[k]);
                    }
                }

                if (i == 0) outer3D = loopPoints;
                else inner3DList.Add(loopPoints);
            }

            if (outer3D.Count < 3) return null;

            double centerFloorX = outer3D.Average(p => p.X);
            double centerFloorY = outer3D.Average(p => p.Y);
            XYZ origin3D = new XYZ(centerFloorX, centerFloorY, maxZ);

            List<XYZ> outerUV = outer3D.Select(p => new XYZ(p.X - centerFloorX, p.Y - centerFloorY, 0)).ToList();
            List<List<XYZ>> innerUV = inner3DList
                .Select(inner => inner.Select(p => new XYZ(p.X - centerFloorX, p.Y - centerFloorY, 0)).ToList())
                .ToList();

            double areaSqM = 0;
            Parameter pArea = floor.get_Parameter(BuiltInParameter.HOST_AREA_COMPUTED);
            if (pArea != null && pArea.AsDouble() > 0) areaSqM = pArea.AsDouble() * 0.092903;

            double perimM = 0;
            Parameter pPerim = floor.get_Parameter(BuiltInParameter.HOST_PERIMETER_COMPUTED);
            if (pPerim != null && pPerim.AsDouble() > 0) perimM = pPerim.AsDouble() * 0.3048;

            Level? level = doc.GetElement(floor.LevelId) as Level;

            // Detectar capas compuestas del FloorType
            HostLayerDetection detectedLayers = DetectCompoundLayers(floor.FloorType, doc);

            return new TilingHostInfo
            {
                HostId = floor.Id,
                Name = floor.Name,
                HostType = HostElementType.Floor,
                Level = level,
                ElevationFt = maxZ,
                AreaSqM = areaSqM,
                PerimeterM = perimM,
                HostFaceReference = topFace.Reference,
                DetectedLayers = detectedLayers,
                Origin3D = origin3D,
                BasisU = XYZ.BasisX,
                BasisV = XYZ.BasisY,
                Normal3D = XYZ.BasisZ,
                MinU = outerUV.Min(p => p.X),
                MaxU = outerUV.Max(p => p.X),
                MinV = outerUV.Min(p => p.Y),
                MaxV = outerUV.Max(p => p.Y),
                OuterBoundaryUV = outerUV,
                InnerBoundariesUV = innerUV,
                OuterBoundary3D = outer3D,
                OuterCurves3D = outerCurves
            };
        }

        private TilingHostInfo? ExtractWallInfo(Wall wall)
        {
            Document doc = wall.Document;
            if (!(wall.Location is LocationCurve locCurve && locCurve.Curve is Line line)) return null;

            XYZ pStart = line.GetEndPoint(0);
            XYZ pEnd = line.GetEndPoint(1);
            XYZ dir = (pEnd - pStart).Normalize();
            XYZ normal = wall.Orientation;
            XYZ up = XYZ.BasisZ;

            double lengthFt = line.Length;
            double heightFt = 10.0;
            Parameter pHeight = wall.get_Parameter(BuiltInParameter.WALL_USER_HEIGHT_PARAM);
            if (pHeight != null && pHeight.AsDouble() > 0) heightFt = pHeight.AsDouble();

            double baseOffsetFt = 0;
            Parameter pBase = wall.get_Parameter(BuiltInParameter.WALL_BASE_OFFSET);
            if (pBase != null) baseOffsetFt = pBase.AsDouble();

            Level? level = doc.GetElement(wall.LevelId) as Level;
            double baseZ = (level?.Elevation ?? 0.0) + baseOffsetFt;

            XYZ centerWall = (pStart + pEnd) / 2.0;
            XYZ origin3D = new XYZ(centerWall.X, centerWall.Y, baseZ + heightFt / 2.0);

            double halfL = lengthFt / 2.0;
            double halfH = heightFt / 2.0;

            List<XYZ> outerUV = new List<XYZ>
            {
                new XYZ(-halfL, -halfH, 0),
                new XYZ( halfL, -halfH, 0),
                new XYZ( halfL,  halfH, 0),
                new XYZ(-halfL,  halfH, 0)
            };

            List<XYZ> outer3D = new List<XYZ>
            {
                origin3D - dir * halfL - up * halfH,
                origin3D + dir * halfL - up * halfH,
                origin3D + dir * halfL + up * halfH,
                origin3D - dir * halfL + up * halfH
            };

            double areaSqM = (lengthFt * heightFt) * 0.092903;
            double perimM = (2 * lengthFt + 2 * heightFt) * 0.3048;

            HostLayerDetection detectedLayers = DetectCompoundLayers(wall.WallType, doc);

            return new TilingHostInfo
            {
                HostId = wall.Id,
                Name = wall.Name,
                HostType = HostElementType.Wall,
                Level = level,
                ElevationFt = baseZ,
                AreaSqM = areaSqM,
                PerimeterM = perimM,
                DetectedLayers = detectedLayers,
                WallHeightFt = heightFt,
                WallLengthFt = lengthFt,
                Origin3D = origin3D,
                BasisU = dir,
                BasisV = up,
                Normal3D = normal,
                MinU = -halfL,
                MaxU = halfL,
                MinV = -halfH,
                MaxV = halfH,
                OuterBoundaryUV = outerUV,
                InnerBoundariesUV = new List<List<XYZ>>(),
                OuterBoundary3D = outer3D,
                OuterCurves3D = new List<Curve>()
            };
        }

        private HostLayerDetection DetectCompoundLayers(ElementType elemType, Document doc)
        {
            HostLayerDetection det = new HostLayerDetection();
            if (elemType == null) return det;

            CompoundStructure? cs = null;
            if (elemType is WallType wt) cs = wt.GetCompoundStructure();
            else if (elemType is FloorType ft) cs = ft.GetCompoundStructure();

            if (cs == null) return det;

            IList<CompoundStructureLayer> layers = cs.GetLayers();
            foreach (var layer in layers)
            {
                double thickCm = layer.Width * 30.48; // Pies a cm
                string matName = "";
                if (layer.MaterialId != ElementId.InvalidElementId && doc.GetElement(layer.MaterialId) is Autodesk.Revit.DB.Material mat)
                {
                    matName = mat.Name;
                }

                if (layer.Function == MaterialFunctionAssignment.Substrate ||
                    matName.IndexOf("Revoque", StringComparison.OrdinalIgnoreCase) >= 0 ||
                    matName.IndexOf("Pañete", StringComparison.OrdinalIgnoreCase) >= 0 ||
                    matName.IndexOf("Tarrajeo", StringComparison.OrdinalIgnoreCase) >= 0 ||
                    matName.IndexOf("Yeso", StringComparison.OrdinalIgnoreCase) >= 0)
                {
                    det.HasRevoque = true;
                    det.SuggestedRevoqueCm = thickCm;
                    det.SuggestedRevoqueMaterial = matName;
                    det.SuggestedRevoqueMaterialId = layer.MaterialId;
                }
                else if (layer.Function == MaterialFunctionAssignment.Membrane ||
                         matName.IndexOf("Pega", StringComparison.OrdinalIgnoreCase) >= 0 ||
                         matName.IndexOf("Adhesivo", StringComparison.OrdinalIgnoreCase) >= 0 ||
                         matName.IndexOf("Mortero", StringComparison.OrdinalIgnoreCase) >= 0)
                {
                    det.HasPega = true;
                    det.SuggestedPegaMm = thickCm * 10.0; // cm a mm
                    det.SuggestedPegaMaterial = matName;
                    det.SuggestedPegaMaterialId = layer.MaterialId;
                }
                else if (layer.Function == MaterialFunctionAssignment.Finish1 ||
                         layer.Function == MaterialFunctionAssignment.Finish2 ||
                         matName.IndexOf("Porcelanato", StringComparison.OrdinalIgnoreCase) >= 0 ||
                         matName.IndexOf("Ceram", StringComparison.OrdinalIgnoreCase) >= 0 ||
                         matName.IndexOf("Baldosa", StringComparison.OrdinalIgnoreCase) >= 0 ||
                         matName.IndexOf("Piso", StringComparison.OrdinalIgnoreCase) >= 0)
                {
                    det.HasFinish = true;
                    det.SuggestedFinishCm = thickCm;
                    det.SuggestedFinishMaterial = matName;
                    det.SuggestedFinishMaterialId = layer.MaterialId;
                }
            }

            return det;
        }

        private Solid? CreatePrismFromPolygon(List<XYZ> vertices, XYZ normal, double thickness)
        {
            if (vertices == null || vertices.Count < 3 || thickness < 1e-5) return null;

            try
            {
                // 1. Limpiar vértices duplicados o colapsados
                List<XYZ> cleanPts = new List<XYZ>();
                for (int i = 0; i < vertices.Count; i++)
                {
                    XYZ pt = vertices[i];
                    if (cleanPts.Count == 0 || cleanPts[cleanPts.Count - 1].DistanceTo(pt) > 0.003)
                    {
                        cleanPts.Add(pt);
                    }
                }
                if (cleanPts.Count >= 2 && cleanPts[0].DistanceTo(cleanPts[cleanPts.Count - 1]) < 0.003)
                {
                    cleanPts.RemoveAt(cleanPts.Count - 1);
                }

                if (cleanPts.Count < 3) return null;

                // 2. Calcular normal del polígono y garantizar sentido antihorario (CCW) respecto a la normal de extrusión
                XYZ polyNormal = XYZ.Zero;
                for (int i = 0; i < cleanPts.Count; i++)
                {
                    XYZ curr = cleanPts[i];
                    XYZ next = cleanPts[(i + 1) % cleanPts.Count];
                    polyNormal += curr.CrossProduct(next);
                }

                XYZ normDir = normal.Normalize();
                if (polyNormal.DotProduct(normDir) < 0)
                {
                    cleanPts.Reverse();
                }

                // 3. Crear CurveLoop cerrado
                CurveLoop loop = new CurveLoop();
                for (int i = 0; i < cleanPts.Count; i++)
                {
                    XYZ p1 = cleanPts[i];
                    XYZ p2 = cleanPts[(i + 1) % cleanPts.Count];
                    if (p1.DistanceTo(p2) > 0.001)
                    {
                        loop.Append(Line.CreateBound(p1, p2));
                    }
                }

                if (!loop.IsOpen())
                {
                    return GeometryCreationUtilities.CreateExtrusionGeometry(
                        new List<CurveLoop> { loop },
                        normDir,
                        thickness);
                }
            }
            catch { }

            return null;
        }

        private Solid? CreateSkirtingSolid(SkirtingItem seg)
        {
            try
            {
                XYZ p1 = seg.StartPoint;
                XYZ p2 = seg.EndPoint;
                XYZ dir = (p2 - p1);
                double len = dir.GetLength();
                if (len < 0.01) return null;

                XYZ norm = dir.Normalize();
                XYZ inward = new XYZ(-norm.Y, norm.X, 0);

                double th = seg.ThicknessFt;
                double h = seg.HeightFt;
                double z = seg.BaseElevationFt;

                XYZ pt0 = new XYZ(p1.X, p1.Y, z);
                XYZ pt1 = new XYZ(p2.X, p2.Y, z);
                XYZ pt2 = new XYZ(p2.X + inward.X * th, p2.Y + inward.Y * th, z);
                XYZ pt3 = new XYZ(p1.X + inward.X * th, p1.Y + inward.Y * th, z);

                List<XYZ> pts = new List<XYZ> { pt0, pt1, pt2, pt3 };
                return CreatePrismFromPolygon(pts, XYZ.BasisZ, h);
            }
            catch { }

            return null;
        }

        private static string SanitizeRevitName(string? name, string fallback = "Elemento")
        {
            if (string.IsNullOrWhiteSpace(name)) return fallback;
            char[] prohibited = new[] { '\\', ':', '{', '}', '[', ']', '|', ';', '<', '>', '?', '`', '~', '/', '*', '"' };
            string clean = name;
            foreach (char c in prohibited)
            {
                clean = clean.Replace(c, '-');
            }
            clean = clean.Trim();
            return string.IsNullOrWhiteSpace(clean) ? fallback : clean;
        }
    }
}
