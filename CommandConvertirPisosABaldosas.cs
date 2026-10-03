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
                FamilySymbol? tileSymbol = window.SelectedTile?.FamilySymbol ?? LoadOrFindFamilySymbol(doc, "Tiles Base", new[] { "Tiles Base" });
                FamilySymbol? pegaSymbol = LoadOrFindFamilySymbol(doc, "Base Tiles Pega", new[] { "Base Tiles Pega" });

                // 5. Instanciar en Revit dentro de una Transacción
                using (Transaction t = new Transaction(doc, "Generar Enchapes, Capas y Fragua"))
                {
                    t.Start();

                    // Configurar preprocesador de fallos para suprimir advertencias molestas
                    FailureHandlingOptions failOpt = t.GetFailureHandlingOptions();
                    failOpt.SetFailuresPreprocessor(new SafeFailuresPreprocessor());
                    failOpt.SetClearAfterRollback(true);
                    t.SetFailureHandlingOptions(failOpt);

                    // Asegurar parámetros de medición en el proyecto
                    MeasurementManager.EnsureMeasurementParameters(doc);

                    ElementId categoryId = new ElementId(window.TargetSystemCategory);
                    if (!DirectShape.IsValidCategoryId(categoryId, doc))
                    {
                        categoryId = new ElementId(BuiltInCategory.OST_GenericModel);
                    }
                    ElementId tileMatId = window.TileMaterial?.Id ?? ElementId.InvalidElementId;
                    ElementId revoqueMatId = window.RevoqueMaterial?.Id ?? ElementId.InvalidElementId;
                    ElementId beddingMatId = window.BeddingMaterial?.Id ?? ElementId.InvalidElementId;
                    ElementId groutMatId = window.GroutMaterial?.Id ?? ElementId.InvalidElementId;
                    ElementId skirtingMatId = window.SkirtingMaterial?.Id ?? tileMatId;

                    // Tipos de DirectShape con nombre explícito para la categoría destino (ej. Suelos)
                    DirectShapeType? dsTypeFull = CategoryAssignmentService.GetOrCreateDirectShapeType(doc, categoryId, $"Baldosa {window.TileLengthCm:0.#}x{window.TileWidthCm:0.#}cm");
                    DirectShapeType? dsTypeCut = CategoryAssignmentService.GetOrCreateDirectShapeType(doc, categoryId, $"Baldosa Cortada {window.TileLengthCm:0.#}x{window.TileWidthCm:0.#}cm");
                    DirectShapeType? dsTypeJoint = CategoryAssignmentService.GetOrCreateDirectShapeType(doc, categoryId, "Fragua - Juntas");
                    DirectShapeType? dsTypeMortero = CategoryAssignmentService.GetOrCreateDirectShapeType(doc, categoryId, "Mortero de Nivelación");
                    DirectShapeType? dsTypeBedding = CategoryAssignmentService.GetOrCreateDirectShapeType(doc, categoryId, "Cama de Pega");
                    DirectShapeType? dsTypeSkirting = CategoryAssignmentService.GetOrCreateDirectShapeType(doc, categoryId, "Zócalo Perimetral");

                    int createdTilesCount = 0;
                    int cutTilesCount = 0;

                    double tileHFt = (window.TileThickCm / 100.0) * 3.28084;
                    double revoqueFt = window.ModelRevoque ? (window.RevoqueThickCm / 100.0) * 3.28084 : 0.0;
                    double beddingFt = window.ModelBeddingMortar ? (window.BeddingThickMm / 1000.0) * 3.28084 : 0.0;

                    // A. Modelar Capa de Mortero de Nivel / Solado / Revoque Base
                    if (window.ModelRevoque && revoqueFt > 0.001)
                    {
                        foreach (var host in window.SelectedHosts)
                        {
                            List<XYZ> revBoundary;
                            if (host.HostType == HostElementType.Floor)
                            {
                                // Mortero en la base inferior del suelo original
                                double revOffset = -(tileHFt + beddingFt + revoqueFt);
                                revBoundary = host.OuterBoundary3D.Select(p => p + host.Normal3D * revOffset).ToList();
                            }
                            else
                            {
                                revBoundary = host.OuterBoundary3D;
                            }

                            Solid? revSolid = CreatePrismFromPolygon(revBoundary, host.Normal3D, revoqueFt);
                            if (revSolid != null)
                            {
                                DirectShape dsRev = DirectShape.CreateElement(doc, categoryId);
                                dsRev.SetShape(new GeometryObject[] { revSolid });
                                dsRev.Name = SanitizeRevitName($"Mortero Base - {host.Name}", "Mortero Base");

                                if (dsTypeMortero != null)
                                {
                                    try { dsRev.SetTypeId(dsTypeMortero.Id); } catch { }
                                }

                                Parameter pComm = dsRev.get_Parameter(BuiltInParameter.ALL_MODEL_INSTANCE_COMMENTS);
                                if (pComm != null && !pComm.IsReadOnly) pComm.Set("ENCHAPE-MORTERO-BASE");

                                if (revoqueMatId != ElementId.InvalidElementId)
                                {
                                    Parameter pMat = dsRev.get_Parameter(BuiltInParameter.MATERIAL_ID_PARAM);
                                    if (pMat != null && !pMat.IsReadOnly) pMat.Set(revoqueMatId);
                                }

                                MeasurementManager.MeasureAndApply(dsRev, revSolid, customThicknessFt: revoqueFt);
                            }
                        }
                    }

                    // B. Modelar Cama de Pega / Adhesivo
                    if (window.ModelBeddingMortar && beddingFt > 0.0005)
                    {
                        foreach (var host in window.SelectedHosts)
                        {
                            List<XYZ> bedBoundary;
                            if (host.HostType == HostElementType.Floor)
                            {
                                // Cama de pega inmediatamente bajo el enchape
                                double bedOffset = -(tileHFt + beddingFt);
                                bedBoundary = host.OuterBoundary3D.Select(p => p + host.Normal3D * bedOffset).ToList();
                            }
                            else
                            {
                                bedBoundary = host.OuterBoundary3D.Select(p => p + host.Normal3D * revoqueFt).ToList();
                            }

                            Solid? bedSolid = CreatePrismFromPolygon(bedBoundary, host.Normal3D, beddingFt);
                            if (bedSolid != null)
                            {
                                DirectShape dsBed = DirectShape.CreateElement(doc, categoryId);
                                dsBed.SetShape(new GeometryObject[] { bedSolid });
                                dsBed.Name = SanitizeRevitName($"Cama de Pega - {host.Name}", "Cama de Pega");

                                if (dsTypeBedding != null)
                                {
                                    try { dsBed.SetTypeId(dsTypeBedding.Id); } catch { }
                                }

                                Parameter pComm = dsBed.get_Parameter(BuiltInParameter.ALL_MODEL_INSTANCE_COMMENTS);
                                if (pComm != null && !pComm.IsReadOnly) pComm.Set("ENCHAPE-PEGA-CAMA");

                                if (beddingMatId != ElementId.InvalidElementId)
                                {
                                    Parameter pMat = dsBed.get_Parameter(BuiltInParameter.MATERIAL_ID_PARAM);
                                    if (pMat != null && !pMat.IsReadOnly) pMat.Set(beddingMatId);
                                }

                                MeasurementManager.MeasureAndApply(dsBed, bedSolid, customThicknessFt: beddingFt);
                            }
                        }
                    }

                    // C. Modelar Baldosas / Enchapes (Instanciación con Tiles Base.rfa para piezas enteras y cortadas)
                    if (window.ModelTiles)
                    {
                        if (tileSymbol != null)
                        {
                            try
                            {
                                if (!tileSymbol.IsActive) tileSymbol.Activate();
                                double nomLFt = (window.TileLengthCm / 100.0) * 3.28084;
                                double nomWFt = (window.TileWidthCm / 100.0) * 3.28084;
                                double nomTFt = (window.TileThickCm / 100.0) * 3.28084;
                                if (nomLFt > 0.001) SetParameterValue(tileSymbol, new[] { "Lenght", "Length", "Largo", "Depth" }, nomLFt);
                                if (nomWFt > 0.001) SetParameterValue(tileSymbol, new[] { "Width", "Ancho" }, nomWFt);
                                if (nomTFt > 0.001) SetParameterValue(tileSymbol, new[] { "Thickness", "Height", "Espesor" }, nomTFt);
                                // NOTA CRÍTICA: NUNCA tocar 'Offset' en tileSymbol porque corrompe la familia Tiles Base.rfa y Revit arroja "Can't make type"
                            }
                            catch { }
                        }

                        foreach (var tile in window.LayoutTiles)
                        {
                            if (tile.PolygonVertices3D.Count < 3) continue;

                            bool placedAsFamily = false;

                            // Modelar como ejemplar de la familia nativa Tiles Base.rfa exclusivamente para baldosas enteras
                            if (!window.PreferDirectShapeGeometry && !tile.IsCut && tileSymbol != null)
                            {
                                try
                                {
                                    FamilyInstance? inst = null;
                                    XYZ placePoint = tile.Center3D;

                                    if (tile.HostFaceRef != null)
                                    {
                                        try
                                        {
                                            Element hostElem = doc.GetElement(tile.HostFaceRef.ElementId);
                                            if (hostElem != null)
                                            {
                                                Face? planarFace = hostElem.GetGeometryObjectFromReference(tile.HostFaceRef) as Face;
                                                if (planarFace != null)
                                                {
                                                    IntersectionResult ir = planarFace.Project(tile.Center3D);
                                                    if (ir != null) placePoint = ir.XYZPoint;
                                                }
                                            }

                                            inst = doc.Create.NewFamilyInstance(
                                                tile.HostFaceRef,
                                                placePoint,
                                                tile.DirectionU3D,
                                                tileSymbol);
                                        }
                                        catch { }
                                    }

                                    if (inst == null)
                                    {
                                        Level? level = hostInfos.FirstOrDefault()?.Level;
                                        inst = doc.Create.NewFamilyInstance(
                                            placePoint,
                                            tileSymbol,
                                            level,
                                            Autodesk.Revit.DB.Structure.StructuralType.NonStructural);

                                        if (inst != null && Math.Abs(tile.RotationRad) > 1e-4)
                                        {
                                            try
                                             {
                                                Line rotAxis = Line.CreateUnbound(placePoint, tile.Normal3D);
                                                ElementTransformUtils.RotateElement(doc, inst.Id, rotAxis, tile.RotationRad);
                                            }
                                            catch { }
                                        }
                                    }

                                    if (inst != null)
                                    {
                                        // Parámetros de ejemplar: Cut Lat y Cut Top en pies
                                        if (tile.IsCut)
                                        {
                                            SetParameterValueInstanceOnly(inst, new[] { "Cut Lat", "CutLat", "Cut_Lat", "CutLateral", "Cut Lateral", "Corte Lat", "Corte Lateral", "Recorte Lateral", "RecorteLateral", "CutLat_Depth", "CutLatDepth" }, tile.CutLat_Ft);
                                            SetParameterValueInstanceOnly(inst, new[] { "Cut Lat Flag", "Cut Lat", "CutLat" }, tile.CutLat_Ft > 0.001 ? 1 : 0);

                                            SetParameterValueInstanceOnly(inst, new[] { "Cut Top", "CutTop", "Cut_Top", "CutTopCut", "CutSuperior", "Cut Superior", "Corte Top", "Corte Superior", "Recorte Superior", "RecorteSuperior", "CutTop_Depth", "CutTopDepth" }, tile.CutTop_Ft);
                                            SetParameterValueInstanceOnly(inst, new[] { "Cut Top Flag", "Cut Top", "CutTop" }, tile.CutTop_Ft > 0.001 ? 1 : 0);

                                            SetParameterValueInstanceOnly(inst, new[] { "Cut", "Recorte" }, Math.Max(tile.CutLat_Ft, tile.CutTop_Ft));
                                            SetParameterValueInstanceOnly(inst, new[] { "Cut Flag" }, (tile.CutLat_Ft > 0.001 || tile.CutTop_Ft > 0.001) ? 1 : 0);
                                        }
                                        else
                                        {
                                            SetParameterValueInstanceOnly(inst, new[] { "Cut Lat", "CutLat", "Cut_Lat", "CutLateral", "Cut Lateral", "Corte Lat", "Corte Lateral", "Recorte Lateral", "RecorteLateral" }, 0.0);
                                            SetParameterValueInstanceOnly(inst, new[] { "Cut Lat Flag", "Cut Lat", "CutLat" }, 0);
                                            SetParameterValueInstanceOnly(inst, new[] { "Cut Top", "CutTop", "Cut_Top", "CutTopCut", "CutSuperior", "Cut Superior", "Corte Top", "Corte Superior", "Recorte Superior", "RecorteSuperior" }, 0.0);
                                            SetParameterValueInstanceOnly(inst, new[] { "Cut Top Flag", "Cut Top", "CutTop" }, 0);
                                            SetParameterValueInstanceOnly(inst, new[] { "Cut", "Recorte" }, 0.0);
                                            SetParameterValueInstanceOnly(inst, new[] { "Cut Flag" }, 0);
                                        }

                                        // Parámetros dimensionales a nivel ejemplar (si la familia los tiene como ejemplar)
                                        SetParameterValueInstanceOnly(inst, new[] { "Width", "Ancho" }, tile.Width_Ft);
                                        SetParameterValueInstanceOnly(inst, new[] { "Lenght", "Length", "Largo" }, tile.Length_Ft);
                                        SetParameterValueInstanceOnly(inst, new[] { "Thickness", "Height", "Espesor" }, tile.Thickness_Ft);

                                        // Optimización de rendimiento: El punto de inserción placePoint ya se proyecta
                                        // y orienta analíticamente en la cara anfitriona sin forzar doc.Regenerate() por baldosa.

                                        Parameter pComm = inst.get_Parameter(BuiltInParameter.ALL_MODEL_INSTANCE_COMMENTS);
                                        if (pComm != null && !pComm.IsReadOnly)
                                        {
                                            pComm.Set(tile.IsCut 
                                                ? $"ENCHAPE-BALDOSA-CORTADA (CutLat:{tile.CutLat_Ft * 30.48:0.0}cm, CutTop:{tile.CutTop_Ft * 30.48:0.0}cm)" 
                                                : "ENCHAPE-BALDOSA-ENTERA");
                                        }

                                        if (tileMatId != ElementId.InvalidElementId)
                                        {
                                            Parameter? pMat = inst.LookupParameter("Mat")
                                                          ?? inst.LookupParameter("Material")
                                                          ?? inst.get_Parameter(BuiltInParameter.STRUCTURAL_MATERIAL_PARAM)
                                                          ?? inst.get_Parameter(BuiltInParameter.MATERIAL_ID_PARAM);
                                            if (pMat != null && !pMat.IsReadOnly) pMat.Set(tileMatId);
                                        }

                                        MeasurementManager.MeasureAndApply(inst, null, 
                                            customLengthFt: tile.ActualLength_Ft > 0 ? tile.ActualLength_Ft : tile.Length_Ft, 
                                            customWidthFt: tile.ActualWidth_Ft > 0 ? tile.ActualWidth_Ft : tile.Width_Ft, 
                                            customAreaSqFt: tile.ActualAreaSqM * 10.7639, 
                                            customThicknessFt: tile.Thickness_Ft, 
                                            customVolumeCuFt: tile.ActualAreaSqM * 10.7639 * tile.Thickness_Ft, 
                                            units: 1);

                                        if (tile.IsCut) cutTilesCount++;
                                        else createdTilesCount++;
                                        placedAsFamily = true;
                                    }
                                }
                                catch { }
                            }

                            if (!placedAsFamily)
                            {
                                Solid? tileSolid = tile.TileSolid ?? CreatePrismFromPolygon(tile.PolygonVertices3D, tile.Normal3D, tile.Thickness_Ft);
                                if (tileSolid != null)
                                {
                                    DirectShape ds = DirectShape.CreateElement(doc, categoryId);
                                    ds.SetShape(new GeometryObject[] { tileSolid });
                                    ds.Name = tile.IsCut ? $"Baldosa Cortada ({tile.ActualAreaSqM:0.00}m²)" : $"Baldosa {window.TileLengthCm:0.#}x{window.TileWidthCm:0.#}cm";

                                    DirectShapeType? targetType = tile.IsCut ? dsTypeCut : dsTypeFull;
                                    if (targetType != null)
                                    {
                                        try { ds.SetTypeId(targetType.Id); } catch { }
                                    }

                                    Parameter pComm = ds.get_Parameter(BuiltInParameter.ALL_MODEL_INSTANCE_COMMENTS);
                                    if (pComm != null && !pComm.IsReadOnly)
                                    {
                                        pComm.Set(tile.IsCut 
                                            ? $"ENCHAPE-BALDOSA-CORTADA (CutLat:{tile.CutLat_Ft * 30.48:0.0}cm, CutTop:{tile.CutTop_Ft * 30.48:0.0}cm)" 
                                            : "ENCHAPE-BALDOSA-ENTERA");
                                    }

                                    if (tileMatId != ElementId.InvalidElementId)
                                    {
                                        Parameter pMat = ds.get_Parameter(BuiltInParameter.MATERIAL_ID_PARAM);
                                        if (pMat != null && !pMat.IsReadOnly) pMat.Set(tileMatId);
                                    }

                                    MeasurementManager.MeasureAndApply(ds, tileSolid, customThicknessFt: tile.Thickness_Ft, units: 1);

                                    if (tile.IsCut) cutTilesCount++;
                                    else createdTilesCount++;
                                }
                            }
                        }
                    }

                    // D. Modelar Juntas / Fragua (Instanciación con Base Tiles Pega.rfa para juntas interiores y perimetrales)
                    int jointCount = 0;
                    if (window.GenerateJointMesh && window.LayoutJoints.Count > 0)
                    {
                        if (pegaSymbol != null)
                        {
                            try
                            {
                                if (!pegaSymbol.IsActive) pegaSymbol.Activate();
                                double jointInnerFt = (window.JointInnerMm / 1000.0) * 3.28084;
                                SetParameterValue(pegaSymbol, new[] { "Width", "Ancho", "GrosorPega", "GrosorJunta" }, jointInnerFt);
                                SetParameterValue(pegaSymbol, new[] { "Thickness", "Height", "Espesor", "Alto" }, tileHFt);
                            }
                            catch { }
                        }

                        foreach (var j in window.LayoutJoints)
                        {
                            bool placedPega = false;
                            if (!window.PreferDirectShapeGeometry && pegaSymbol != null && j.LengthFt > 0.005)
                            {
                                try
                                {
                                    FamilyInstance? pegaInst = null;
                                    XYZ segDir = (j.EndPoint3D - j.StartPoint3D);
                                    XYZ dir = segDir.GetLength() > 1e-4 ? segDir.Normalize() : XYZ.BasisX;
                                    XYZ targetCenter = (j.StartPoint3D + j.EndPoint3D) / 2.0;
                                    XYZ placePoint = targetCenter;

                                    if (j.HostFaceRef != null)
                                    {
                                        try
                                        {
                                            Element hostElem = doc.GetElement(j.HostFaceRef.ElementId);
                                            if (hostElem != null)
                                            {
                                                Face? planarFace = hostElem.GetGeometryObjectFromReference(j.HostFaceRef) as Face;
                                                if (planarFace != null)
                                                {
                                                    IntersectionResult ir = planarFace.Project(targetCenter);
                                                    if (ir != null) placePoint = ir.XYZPoint;
                                                }
                                            }

                                            pegaInst = doc.Create.NewFamilyInstance(
                                                j.HostFaceRef,
                                                placePoint,
                                                dir,
                                                pegaSymbol);
                                        }
                                        catch { }
                                    }

                                    if (pegaInst == null)
                                    {
                                        Level? level = hostInfos.FirstOrDefault()?.Level;
                                        pegaInst = doc.Create.NewFamilyInstance(
                                            placePoint,
                                            pegaSymbol,
                                            level,
                                            Autodesk.Revit.DB.Structure.StructuralType.NonStructural);

                                        if (pegaInst != null && Math.Abs(j.RotationRad) > 1e-4)
                                        {
                                            try
                                            {
                                                Line rotAxis = Line.CreateUnbound(placePoint, j.Normal3D);
                                                ElementTransformUtils.RotateElement(doc, pegaInst.Id, rotAxis, j.RotationRad);
                                            }
                                            catch { }
                                        }
                                    }

                                    if (pegaInst != null)
                                    {
                                        // Ajustar dimensiones en la familia de pega (Base Tiles Pega)
                                        SetParameterValueInstanceOnly(pegaInst, new[] { "Length", "Lenght", "Largo", "Longitud" }, j.LengthFt);
                                        SetParameterValueInstanceOnly(pegaInst, new[] { "Width", "Ancho", "GrosorPega", "GrosorJunta" }, j.WidthFt);
                                        SetParameterValueInstanceOnly(pegaInst, new[] { "Thickness", "Height", "Espesor", "Alto" }, j.DepthFt);
                                        SetParameterValueInstanceOnly(pegaInst, new[] { "Cut", "Cut Lat", "Cut Top", "Recorte" }, 0.0);

                                        // Recentrado planar exacto por BoundingBox:
                                        // Garantiza que la junta quede perfectamente centrada entre StartPoint3D y EndPoint3D
                                        // sin proyectarse hacia el exterior del piso.
                                        try
                                        {
                                            doc.Regenerate();
                                            BoundingBoxXYZ? pegaBbox = pegaInst.get_BoundingBox(null);
                                            if (pegaBbox != null)
                                            {
                                                XYZ geomCenter = (pegaBbox.Min + pegaBbox.Max) / 2.0;
                                                XYZ shift = targetCenter - geomCenter;
                                                shift = shift - j.Normal3D * shift.DotProduct(j.Normal3D);
                                                if (shift.GetLength() > 0.001)
                                                {
                                                    ElementTransformUtils.MoveElement(doc, pegaInst.Id, shift);
                                                }
                                            }
                                        }
                                        catch { }

                                        Parameter pComm = pegaInst.get_Parameter(BuiltInParameter.ALL_MODEL_INSTANCE_COMMENTS);
                                        if (pComm != null && !pComm.IsReadOnly)
                                        {
                                            pComm.Set(j.IsPerimeter ? "ENCHAPE-JUNTA-PERIMETRAL" : "ENCHAPE-PEGA-FRAGUA");
                                        }

                                        if (groutMatId != ElementId.InvalidElementId)
                                        {
                                            Parameter? pMat = pegaInst.LookupParameter("Mat")
                                                          ?? pegaInst.LookupParameter("Material")
                                                          ?? pegaInst.get_Parameter(BuiltInParameter.STRUCTURAL_MATERIAL_PARAM)
                                                          ?? pegaInst.get_Parameter(BuiltInParameter.MATERIAL_ID_PARAM);
                                            if (pMat != null && !pMat.IsReadOnly) pMat.Set(groutMatId);
                                        }

                                        MeasurementManager.MeasureAndApply(pegaInst, null, 
                                            customLengthFt: j.LengthFt, 
                                            customWidthFt: j.WidthFt, 
                                            customThicknessFt: j.DepthFt, 
                                            customAreaSqFt: j.LengthFt * j.WidthFt, 
                                            customVolumeCuFt: j.LengthFt * j.WidthFt * j.DepthFt, 
                                            units: 1);

                                        jointCount++;
                                        placedPega = true;
                                    }
                                }
                                catch { }
                            }

                            // Respaldo DirectShape si no se pudo instanciar la familia o si se prefiere DirectShape
                            if (!placedPega && j.PolygonVertices3D.Count >= 3)
                            {
                                Solid? jSolid = CreatePrismFromPolygon(j.PolygonVertices3D, j.Normal3D, j.DepthFt);
                                if (jSolid != null)
                                {
                                    DirectShape dsJ = DirectShape.CreateElement(doc, categoryId);
                                    dsJ.SetShape(new GeometryObject[] { jSolid });
                                    dsJ.Name = j.IsPerimeter ? "Junta Perimetral" : "Fragua de Junta";

                                    if (dsTypeJoint != null)
                                    {
                                        try { dsJ.SetTypeId(dsTypeJoint.Id); } catch { }
                                    }

                                    Parameter pComm = dsJ.get_Parameter(BuiltInParameter.ALL_MODEL_INSTANCE_COMMENTS);
                                    if (pComm != null && !pComm.IsReadOnly) pComm.Set(j.IsPerimeter ? "ENCHAPE-JUNTA-PERIMETRAL" : "ENCHAPE-PEGA-FRAGUA");

                                    if (groutMatId != ElementId.InvalidElementId)
                                    {
                                        Parameter pMat = dsJ.get_Parameter(BuiltInParameter.MATERIAL_ID_PARAM);
                                        if (pMat != null && !pMat.IsReadOnly) pMat.Set(groutMatId);
                                    }

                                    MeasurementManager.MeasureAndApply(dsJ, jSolid, customLengthFt: j.LengthFt, customWidthFt: j.WidthFt, customThicknessFt: j.DepthFt, units: 1);

                                    jointCount++;
                                }
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

                                if (dsTypeSkirting != null)
                                {
                                    try { dsSk.SetTypeId(dsTypeSkirting.Id); } catch { }
                                }

                                Parameter pComm = dsSk.get_Parameter(BuiltInParameter.ALL_MODEL_INSTANCE_COMMENTS);
                                if (pComm != null && !pComm.IsReadOnly) pComm.Set("ZOCALO-PERIMETRAL");

                                if (skirtingMatId != ElementId.InvalidElementId)
                                {
                                    Parameter pMat = dsSk.get_Parameter(BuiltInParameter.MATERIAL_ID_PARAM);
                                    if (pMat != null && !pMat.IsReadOnly) pMat.Set(skirtingMatId);
                                }

                                MeasurementManager.MeasureAndApply(dsSk, skSolid, customLengthFt: seg.LengthFt, units: 1);

                                skirtingSegCount++;
                            }
                        }
                    }

                    // F. Eliminar Suelo Inicial si se seleccionó la opción de reemplazo total de torta de piso
                    if (window.DeleteOriginalFloor)
                    {
                        foreach (var host in window.SelectedHosts)
                        {
                            if (host.HostType == HostElementType.Floor && host.HostId != ElementId.InvalidElementId)
                            {
                                try
                                {
                                    doc.Delete(host.HostId);
                                }
                                catch { }
                            }
                        }
                    }

                    t.Commit();

                    Autodesk.Revit.UI.TaskDialog.Show(
                        "Enchapes Generados con Éxito",
                        $"Se ha completado el modelado en Revit:\n\n" +
                        $"• Modo de Geometría: {(window.PreferDirectShapeGeometry ? "DirectShape B-Rep (Fiel al Preview 3D)" : "Familias (.rfa) + Sólidos")}\n" +
                        $"• Baldosas Enteras: {createdTilesCount} unidades\n" +
                        $"• Baldosas Cortadas: {cutTilesCount} unidades\n" +
                        $"• Fragua / Juntas 3D: {jointCount} tramos\n" +
                        $"• Mortero Base: {(window.ModelRevoque ? "Modelado (" + window.RevoqueThickCm + " cm)" : "No")}\n" +
                        $"• Cama de Pega: {(window.ModelBeddingMortar ? "Modelada (" + window.BeddingThickMm + " mm)" : "No")}\n" +
                        $"• Zócalos sobre Piso: {skirtingSegCount} tramos\n" +
                        $"• Categoría de Revit asignada: {window.TargetSystemCategory}");
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
                .FirstOrDefault(f => alternateNames.Any(n => f.Name.Equals(n, StringComparison.OrdinalIgnoreCase)));

            if (family == null)
            {
                string addinFolder = Path.GetDirectoryName(Assembly.GetExecutingAssembly().Location) ?? "";
                string[] searchPaths = new[]
                {
                    $@"C:\Users\Alejandro\Documents\GitHub\reviewPlugIn\Familias\Floors-Tiles\{familyName}.rfa",
                    Path.Combine(addinFolder, "Familias", "Floors-Tiles", $"{familyName}.rfa"),
                    Path.Combine(addinFolder, $"{familyName}.rfa"),
                    Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData), @"Autodesk\Revit\Addins\2027\Familias\Floors-Tiles", $"{familyName}.rfa"),
                    Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData), @"Autodesk\Revit\Addins\2027", $"{familyName}.rfa"),
                    $@"C:\Users\Alejandro\Documents\GitHub\reviewPlugIn\{familyName}.rfa"
                };

                string? foundPath = searchPaths.FirstOrDefault(File.Exists);
                if (foundPath != null)
                {
                    if (doc.IsModifiable)
                    {
                        doc.LoadFamily(foundPath, out family);
                    }
                    else
                    {
                        using (Transaction tLoad = new Transaction(doc, $"Cargar {familyName}"))
                        {
                            tLoad.Start();
                            doc.LoadFamily(foundPath, out family);
                            tLoad.Commit();
                        }
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

        private void SetParameterValueInstanceOnly(Element elem, string[] paramNames, double val)
        {
            foreach (string name in paramNames)
            {
                string cleanName = name.Replace("(default)", "").Replace("(predeterminado)", "").Trim();
                Parameter? p = elem.LookupParameter(name) ?? elem.LookupParameter(cleanName);
                if (p != null && !p.IsReadOnly)
                {
                    if (p.StorageType == StorageType.Double)
                        p.Set(val);
                    else if (p.StorageType == StorageType.Integer)
                        p.Set((int)Math.Round(val));
                    else if (p.StorageType == StorageType.String)
                        p.Set(val.ToString("0.###", System.Globalization.CultureInfo.InvariantCulture));
                    return;
                }

                foreach (Parameter pElem in elem.Parameters)
                {
                    if (!pElem.IsReadOnly && pElem.Definition != null)
                    {
                        string defClean = pElem.Definition.Name.Replace("(default)", "").Replace("(predeterminado)", "").Trim();
                        if (defClean.Equals(cleanName, StringComparison.OrdinalIgnoreCase))
                        {
                            if (pElem.StorageType == StorageType.Double)
                                pElem.Set(val);
                            else if (pElem.StorageType == StorageType.Integer)
                                pElem.Set((int)Math.Round(val));
                            else if (pElem.StorageType == StorageType.String)
                                pElem.Set(val.ToString("0.###", System.Globalization.CultureInfo.InvariantCulture));
                            return;
                        }
                    }
                }
            }
        }

        private void SetParameterValueInstanceOnly(Element elem, string[] paramNames, int val)
        {
            foreach (string name in paramNames)
            {
                string cleanName = name.Replace("(default)", "").Replace("(predeterminado)", "").Trim();
                Parameter? p = elem.LookupParameter(name) ?? elem.LookupParameter(cleanName);
                if (p != null && !p.IsReadOnly)
                {
                    if (p.StorageType == StorageType.Integer)
                        p.Set(val);
                    else if (p.StorageType == StorageType.Double)
                        p.Set((double)val);
                    return;
                }

                foreach (Parameter pElem in elem.Parameters)
                {
                    if (!pElem.IsReadOnly && pElem.Definition != null)
                    {
                        string defClean = pElem.Definition.Name.Replace("(default)", "").Replace("(predeterminado)", "").Trim();
                        if (defClean.Equals(cleanName, StringComparison.OrdinalIgnoreCase))
                        {
                            if (pElem.StorageType == StorageType.Integer)
                                pElem.Set(val);
                            else if (pElem.StorageType == StorageType.Double)
                                pElem.Set((double)val);
                            return;
                        }
                    }
                }
            }
        }

        private void SetParameterValue(Element elem, string[] paramNames, double val)
        {
            foreach (string name in paramNames)
            {
                Parameter p = elem.LookupParameter(name);
                if (p != null && !p.IsReadOnly)
                {
                    if (p.StorageType == StorageType.Double)
                        p.Set(val);
                    else if (p.StorageType == StorageType.Integer)
                        p.Set((int)Math.Round(val));
                    else if (p.StorageType == StorageType.String)
                        p.Set(val.ToString("0.###", System.Globalization.CultureInfo.InvariantCulture));
                    return;
                }
            }

            if (elem is FamilyInstance fi && fi.Symbol != null)
            {
                foreach (string name in paramNames)
                {
                    Parameter p = fi.Symbol.LookupParameter(name);
                    if (p != null && !p.IsReadOnly)
                    {
                        if (p.StorageType == StorageType.Double)
                            p.Set(val);
                        else if (p.StorageType == StorageType.Integer)
                            p.Set((int)Math.Round(val));
                        else if (p.StorageType == StorageType.String)
                            p.Set(val.ToString("0.###", System.Globalization.CultureInfo.InvariantCulture));
                        return;
                    }
                }
            }
        }

        private void SetParameterValue(Element elem, string[] paramNames, int val)
        {
            foreach (string name in paramNames)
            {
                Parameter p = elem.LookupParameter(name);
                if (p != null && !p.IsReadOnly)
                {
                    if (p.StorageType == StorageType.Integer)
                        p.Set(val);
                    else if (p.StorageType == StorageType.Double)
                        p.Set((double)val);
                    return;
                }
            }

            if (elem is FamilyInstance fi && fi.Symbol != null)
            {
                foreach (string name in paramNames)
                {
                    Parameter p = fi.Symbol.LookupParameter(name);
                    if (p != null && !p.IsReadOnly)
                    {
                        if (p.StorageType == StorageType.Integer)
                            p.Set(val);
                        else if (p.StorageType == StorageType.Double)
                            p.Set((double)val);
                        return;
                    }
                }
            }
        }

        private TilingHostInfo? ExtractFloorInfo(Floor floor)
        {
            Document doc = floor.Document;

            // 1. Extracción del Contorno Base: Obtener el PlanarFace superior
            PlanarFace? topFace = FloorTilingService.GetTopPlanarFace(floor);
            if (topFace == null) return null;

            // 1. Extraer su CurveLoop exterior principal y validar que sea cerrado (curveLoop.IsOpen() == false)
            var (outerLoop, innerLoops) = FloorTilingService.ExtractBoundaryLoops(topFace);
            if (outerLoop == null || outerLoop.IsOpen()) return null;

            // 2. Sistema de Coordenadas Local (Replanteo):
            // Definir un vector base alineado con el segmento más largo del contorno
            // (Transform local con origen en la esquina mínima de la bounding box UV).
            var (localTransform, longestSegment, spanU, spanV) = FloorTilingService.CreateLocalCoordinateSystem(outerLoop, topFace);

            List<XYZ> outer3D = new List<XYZ>();
            List<Curve> outerCurves = new List<Curve>();
            foreach (Curve curve in outerLoop)
            {
                outerCurves.Add(curve);
                IList<XYZ> tess = curve.Tessellate();
                for (int k = 0; k < tess.Count - 1; k++)
                {
                    outer3D.Add(tess[k]);
                }
            }

            if (outer3D.Count < 3) return null;

            List<List<XYZ>> inner3DList = new List<List<XYZ>>();
            List<List<XYZ>> innerUVList = new List<List<XYZ>>();
            foreach (CurveLoop inLoop in innerLoops)
            {
                List<XYZ> inPts3D = new List<XYZ>();
                List<XYZ> inPtsUV = new List<XYZ>();
                foreach (Curve c in inLoop)
                {
                    IList<XYZ> tess = c.Tessellate();
                    for (int k = 0; k < tess.Count - 1; k++)
                    {
                        inPts3D.Add(tess[k]);
                        XYZ loc = localTransform.Inverse.OfPoint(tess[k]);
                        inPtsUV.Add(new XYZ(loc.X, loc.Y, 0));
                    }
                }
                inner3DList.Add(inPts3D);
                innerUVList.Add(inPtsUV);
            }

            List<XYZ> outerUV = outer3D.Select(p =>
            {
                XYZ loc = localTransform.Inverse.OfPoint(p);
                return new XYZ(loc.X, loc.Y, 0);
            }).ToList();

            double areaSqM = 0;
            Parameter pArea = floor.get_Parameter(BuiltInParameter.HOST_AREA_COMPUTED);
            if (pArea != null && pArea.AsDouble() > 0) areaSqM = pArea.AsDouble() * 0.092903;

            double perimM = 0;
            Parameter pPerim = floor.get_Parameter(BuiltInParameter.HOST_PERIMETER_COMPUTED);
            if (pPerim != null && pPerim.AsDouble() > 0) perimM = pPerim.AsDouble() * 0.3048;

            Level? level = doc.GetElement(floor.LevelId) as Level;

            double totalThickFt = 0.328084; // ~10cm default
            if (floor.FloorType != null)
            {
                var cs = floor.FloorType.GetCompoundStructure();
                if (cs != null)
                {
                    totalThickFt = cs.GetWidth();
                }
                else
                {
                    Parameter pThick = floor.FloorType.get_Parameter(BuiltInParameter.FLOOR_ATTR_DEFAULT_THICKNESS_PARAM);
                    if (pThick != null && pThick.AsDouble() > 0) totalThickFt = pThick.AsDouble();
                }
            }

            // Detectar capas compuestas del FloorType
            HostLayerDetection detectedLayers = DetectCompoundLayers(floor.FloorType, doc);

            return new TilingHostInfo
            {
                HostId = floor.Id,
                Name = floor.Name,
                HostType = HostElementType.Floor,
                Level = level,
                ElevationFt = localTransform.Origin.Z,
                TotalHostThicknessFt = totalThickFt,
                AreaSqM = areaSqM,
                PerimeterM = perimM,
                HostFaceReference = topFace.Reference,
                DetectedLayers = detectedLayers,
                Origin3D = localTransform.Origin,
                BasisU = localTransform.BasisX,
                BasisV = localTransform.BasisY,
                Normal3D = localTransform.BasisZ,
                MinU = 0.0,
                MaxU = spanU,
                MinV = 0.0,
                MaxV = spanV,
                OuterBoundaryUV = outerUV,
                InnerBoundariesUV = innerUVList,
                OuterBoundary3D = outer3D,
                OuterCurves3D = outerCurves,
                TopPlanarFace = topFace,
                OuterCurveLoop = outerLoop,
                InnerCurveLoops = innerLoops,
                LocalTransform = localTransform
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
                TotalHostThicknessFt = wall.Width,
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

    public class SafeFailuresPreprocessor : IFailuresPreprocessor
    {
        public FailureProcessingResult PreprocessFailures(FailuresAccessor failuresAccessor)
        {
            IList<FailureMessageAccessor> fmas = failuresAccessor.GetFailureMessages();
            foreach (FailureMessageAccessor fma in fmas)
            {
                FailureSeverity severity = fma.GetSeverity();
                if (severity == FailureSeverity.Warning)
                {
                    failuresAccessor.DeleteWarning(fma);
                }
            }
            return FailureProcessingResult.Continue;
        }
    }
}
