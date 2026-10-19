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

                // 2. Extraer geometría, Span Direction y capas compuestas de los anfitriones seleccionados
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

                // 3. Abrir Ventana de Configuración y Despiece Cartesiano (0,0)
                ConvertirPisosWindow window = new ConvertirPisosWindow(hostInfos, doc);
                bool? dialogRes = window.ShowDialog();

                if (dialogRes != true || !window.IsConfirmed)
                {
                    return Result.Cancelled;
                }

                // 4. Cargar familias cargables (.rfa) desde Familias/Floors-Tiles/
                FamilySymbol? tileSymbol = window.SelectedTile?.FamilySymbol ?? LoadOrFindFamilySymbol(doc, "Tiles Base", new[] { "Tiles Base", "Base Tiles" });
                FamilySymbol? pegaSymbol = LoadOrFindFamilySymbol(doc, "Base Tiles Pega", new[] { "Base Tiles Pega", "Tiles Pega", "Pega Base" });
                FamilySymbol? skirtingSymbol = LoadOrFindFamilySymbol(doc, "Zocalo Base", new[] { "Zocalo Base", "Base Zocalo", "Zocalo", "Base Tiles Zocalo", "Tiles Zocalo" }) ?? pegaSymbol ?? tileSymbol;

                if (tileSymbol == null)
                {
                    Autodesk.Revit.UI.TaskDialog.Show(
                        "Familia Requerida",
                        "No se encontró la familia cargable 'Tiles Base.rfa' en Familias/Floors-Tiles/. Asegúrate de que esté presente para instanciar el piso sin DirectShape.");
                    return Result.Cancelled;
                }

                // 5. Instanciar en Revit dentro de una Transacción (100% Familias Cargables, CERO DirectShape)
                using (Transaction t = new Transaction(doc, "Generar Piso por Familias desde (0,0)"))
                {
                    t.Start();

                    // Configurar preprocesador de fallos para suprimir advertencias molestas
                    FailureHandlingOptions failOpt = t.GetFailureHandlingOptions();
                    failOpt.SetFailuresPreprocessor(new SafeFailuresPreprocessor());
                    failOpt.SetClearAfterRollback(true);
                    t.SetFailureHandlingOptions(failOpt);

                    // Asegurar parámetros de medición en el proyecto
                    MeasurementManager.EnsureMeasurementParameters(doc);

                    ElementId tileMatId = window.TileMaterial?.Id ?? ElementId.InvalidElementId;
                    ElementId revoqueMatId = window.RevoqueMaterial?.Id ?? ElementId.InvalidElementId;
                    ElementId beddingMatId = window.BeddingMaterial?.Id ?? ElementId.InvalidElementId;
                    ElementId groutMatId = window.GroutMaterial?.Id ?? ElementId.InvalidElementId;
                    ElementId perimMatId = window.PerimeterJointMaterial?.Id ?? groutMatId;
                    ElementId skirtingMatId = window.SkirtingMaterial?.Id ?? tileMatId;

                    int createdTilesCount = 0;
                    int cutTilesCount = 0;

                    double tileHFt = (window.TileThickCm / 100.0) * 3.28084;
                    double revoqueFt = window.ModelRevoque ? (window.RevoqueThickCm / 100.0) * 3.28084 : 0.0;
                    double beddingFt = window.ModelBeddingMortar ? (window.BeddingThickMm / 1000.0) * 3.28084 : 0.0;

                    // A. Modelar Capa de Mortero de Nivel / Solado como Floor nativo de Revit (sin DirectShape)
                    if (window.ModelRevoque && revoqueFt > 0.001)
                    {
                        foreach (var host in window.SelectedHosts)
                        {
                            if (host.HostType == HostElementType.Floor && host.OuterCurveLoop != null && host.Level != null)
                            {
                                try
                                {
                                    ElementId floorTypeId = GetOrCreateLayerFloorType(doc, $"Mortero Base ({window.RevoqueThickCm:0.#}cm)", revoqueFt, revoqueMatId);
                                    if (floorTypeId != ElementId.InvalidElementId)
                                    {
                                        Floor revFloor = Floor.Create(doc, new List<CurveLoop> { host.OuterCurveLoop }, floorTypeId, host.Level.Id);
                                        Parameter? pOff = revFloor.get_Parameter(BuiltInParameter.FLOOR_HEIGHTABOVELEVEL_PARAM);
                                        if (pOff != null && !pOff.IsReadOnly)
                                        {
                                            pOff.Set((host.ElevationFt - host.Level.Elevation) - tileHFt - beddingFt);
                                        }
                                        Parameter? pComm = revFloor.get_Parameter(BuiltInParameter.ALL_MODEL_INSTANCE_COMMENTS);
                                        if (pComm != null && !pComm.IsReadOnly) pComm.Set("ENCHAPE-MORTERO-BASE");
                                        MeasurementManager.MeasureAndApply(revFloor, null, customAreaSqFt: host.AreaSqM * 10.7639, customThicknessFt: revoqueFt, customVolumeCuFt: host.AreaSqM * 10.7639 * revoqueFt, units: 1);
                                    }
                                }
                                catch { }
                            }
                        }
                    }

                    // B. Modelar Cama de Pega / Adhesivo como Floor nativo de Revit (sin DirectShape)
                    if (window.ModelBeddingMortar && beddingFt > 0.0005)
                    {
                        foreach (var host in window.SelectedHosts)
                        {
                            if (host.HostType == HostElementType.Floor && host.OuterCurveLoop != null && host.Level != null)
                            {
                                try
                                {
                                    ElementId floorTypeId = GetOrCreateLayerFloorType(doc, $"Cama de Pega ({window.BeddingThickMm:0.#}mm)", beddingFt, beddingMatId);
                                    if (floorTypeId != ElementId.InvalidElementId)
                                    {
                                        Floor bedFloor = Floor.Create(doc, new List<CurveLoop> { host.OuterCurveLoop }, floorTypeId, host.Level.Id);
                                        Parameter? pOff = bedFloor.get_Parameter(BuiltInParameter.FLOOR_HEIGHTABOVELEVEL_PARAM);
                                        if (pOff != null && !pOff.IsReadOnly)
                                        {
                                            pOff.Set((host.ElevationFt - host.Level.Elevation) - tileHFt);
                                        }
                                        Parameter? pComm = bedFloor.get_Parameter(BuiltInParameter.ALL_MODEL_INSTANCE_COMMENTS);
                                        if (pComm != null && !pComm.IsReadOnly) pComm.Set("ENCHAPE-PEGA-CAMA");
                                        MeasurementManager.MeasureAndApply(bedFloor, null, customAreaSqFt: host.AreaSqM * 10.7639, customThicknessFt: beddingFt, customVolumeCuFt: host.AreaSqM * 10.7639 * beddingFt, units: 1);
                                    }
                                }
                                catch { }
                            }
                        }
                    }

                    // C. Modelar Baldosas / Enchapes (Exclusivamente con familia cargable Tiles Base.rfa desde el (0,0) de cada celda)
                    if (window.ModelTiles)
                    {
                        InstantiateFloorTiles(
                            doc,
                            window,
                            tileSymbol,
                            tileMatId,
                            hostInfos,
                            ref createdTilesCount,
                            ref cutTilesCount);
                    }

                    // D. Modelar Juntas Perimetrales (5mm) e Internas (3-4mm) exclusivamente con familia cargable Base Tiles Pega.rfa
                    int jointCount = 0;
                    if (window.GenerateJointMesh && window.LayoutJoints.Count > 0 && pegaSymbol != null)
                    {
                        InstantiateFloorJoints(
                            doc,
                            window,
                            pegaSymbol,
                            groutMatId,
                            perimMatId,
                            hostInfos,
                            ref jointCount);
                    }

                    // E. Modelar Zócalos perimetrales desde el (0,0) usando familia cargable
                    int skirtingSegCount = 0;
                    if (window.EnableSkirting && window.SkirtingSegments.Count > 0 && skirtingSymbol != null)
                    {
                        InstantiateFloorSkirting(
                            doc,
                            window,
                            skirtingSymbol,
                            skirtingMatId,
                            hostInfos,
                            ref skirtingSegCount);
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
                        $"Se ha completado el modelado paramétrico desde el origen (0,0):\n\n" +
                        $"• Modo de Geometría: 100% Familias Cargables (.rfa) [Cero DirectShape]\n" +
                        $"• Baldosas Enteras (Tiles Base.rfa): {createdTilesCount} unidades\n" +
                        $"• Baldosas con Recorte (Cut Lat / Cut Top): {cutTilesCount} unidades\n" +
                        $"• Juntas / Pegas (Base Tiles Pega.rfa): {jointCount} tramos sin solape\n" +
                        $"• Mortero Base: {(window.ModelRevoque ? "Modelado (" + window.RevoqueThickCm + " cm)" : "No")}\n" +
                        $"• Cama de Pega: {(window.ModelBeddingMortar ? "Modelada (" + window.BeddingThickMm + " mm)" : "No")}\n" +
                        $"• Zócalos Perimetrales: {skirtingSegCount} tramos");
                }

                return Result.Succeeded;
            }
            catch (Exception ex)
            {
                message = ex.Message;
                return Result.Failed;
            }
        }

        // =========================================================================================
        // INSTANCIACIÓN DE BALDOSAS: Tiles Base.rfa desde el (0,0) de cada espacio entre juntas
        // =========================================================================================
        private void InstantiateFloorTiles(
            Document doc,
            ConvertirPisosWindow window,
            FamilySymbol? tileSymbol,
            ElementId tileMatId,
            List<TilingHostInfo> hostInfos,
            ref int createdTilesCount,
            ref int cutTilesCount)
        {
            if (window.LayoutTiles == null || window.LayoutTiles.Count == 0 || tileSymbol == null) return;

            double nomLFt = (window.TileLengthCm / 100.0) * 3.28084;
            double nomWFt = (window.TileWidthCm / 100.0) * 3.28084;
            double nomTFt = (window.TileThickCm / 100.0) * 3.28084;

            // Configurar o duplicar el tipo de familia Tiles Base.rfa con las dimensiones nominales exactas
            FamilySymbol activeTileSymbol = GetOrCreateConfiguredSymbol(
                doc,
                tileSymbol,
                $"Tiles Base {window.TileLengthCm:0.#}x{window.TileWidthCm:0.#}x{window.TileThickCm:0.#}cm",
                nomLFt,
                nomWFt,
                nomTFt,
                tileMatId);

            foreach (var tile in window.LayoutTiles)
            {
                if (tile.ActualLength_Ft <= 0.001 || tile.ActualWidth_Ft <= 0.001) continue;

                try
                {
                    FamilyInstance? inst = null;

                    // El origen de Tiles Base.rfa es el (0,0) inferior-izquierdo de la baldosa
                    XYZ placePoint = tile.Corner3D;

                    if (tile.HostFaceRef != null)
                    {
                        Element hostElem = doc.GetElement(tile.HostFaceRef.ElementId);
                        if (hostElem != null)
                        {
                            Face? planarFace = hostElem.GetGeometryObjectFromReference(tile.HostFaceRef) as Face;
                            if (planarFace != null)
                            {
                                IntersectionResult proj = planarFace.Project(tile.Corner3D);
                                if (proj != null)
                                {
                                    placePoint = proj.XYZPoint;
                                }
                            }
                        }

                        inst = doc.Create.NewFamilyInstance(
                            tile.HostFaceRef,
                            placePoint,
                            tile.DirectionU3D,
                            activeTileSymbol);
                    }

                    // Fallback por nivel si no existiese referencia de cara
                    if (inst == null)
                    {
                        Level? hostLevel = hostInfos.FirstOrDefault()?.Level;
                        if (hostLevel != null)
                        {
                            inst = doc.Create.NewFamilyInstance(
                                placePoint,
                                activeTileSymbol,
                                hostLevel,
                                Autodesk.Revit.DB.Structure.StructuralType.NonStructural);

                            if (inst != null && Math.Abs(tile.RotationRad) > 1e-4)
                            {
                                Line rotAxis = Line.CreateBound(placePoint, placePoint + tile.Normal3D);
                                ElementTransformUtils.RotateElement(doc, inst.Id, rotAxis, tile.RotationRad);
                            }
                        }
                    }

                    if (inst != null)
                    {
                        // En Tiles Base.rfa:
                        // - Width (Tipo) = dimensión nominal en X (Span Direction U)
                        // - Lenght (Tipo) = dimensión nominal en Y (V)
                        // - Offset (Tipo) = holgura exterior (5mm) de la extrusión de vacío en L (¡NUNCA poner en 0.0!)
                        // - Cut (Ejemplar, Sí/No) = activa/desactiva el corte de la extrusión de vacío en L
                        // - Cut Lat (Ejemplar) = cota entre planos de referencia en X desde el borde derecho (no puede ser 0.0 ni menor que ShortCurveTolerance ~0.8mm en Revit)
                        // - Cut Top (Ejemplar) = cota entre planos de referencia en Y desde el borde superior (no puede ser 0.0 ni menor que ShortCurveTolerance ~0.8mm en Revit)
                        double defaultFiveMmFt = (5.0 / 1000.0) * 3.28084;
                        double minValidCutFt = (1.0 / 1000.0) * 3.28084; // 1.0 mm (> ShortCurveTolerance 0.794 mm)

                        double rawCutLatFt = tile.IsCut ? Math.Max(0.0, tile.CutLat_Ft) : 0.0;
                        double rawCutTopFt = tile.IsCut ? Math.Max(0.0, tile.CutTop_Ft) : 0.0;
                        bool hasEffectiveCut = tile.IsCut && (rawCutLatFt > 0.0005 || rawCutTopFt > 0.0005);

                        if (hasEffectiveCut)
                        {
                            // Activar el corte del vacío en L
                            SetParameterValueInstanceOnly(inst, new[] { "Cut", "Corte", "Cortar", "IsCut" }, 1);

                            // Garantizar que ninguna de las dos cotas de planos de referencia del vacío en L colapse a 0.0
                            double safeCutLatFt = rawCutLatFt > minValidCutFt
                                ? Math.Min(rawCutLatFt, Math.Max(minValidCutFt, nomLFt - minValidCutFt))
                                : minValidCutFt;
                            double safeCutTopFt = rawCutTopFt > minValidCutFt
                                ? Math.Min(rawCutTopFt, Math.Max(minValidCutFt, nomWFt - minValidCutFt))
                                : minValidCutFt;

                            SetParameterValueInstanceOnly(inst, new[] { "Cut Lat", "CutLat", "Cut_Lat", "CutLateral", "Cut Lateral", "Corte Lat" }, safeCutLatFt);
                            SetParameterValueInstanceOnly(inst, new[] { "Cut Top", "CutTop", "Cut_Top", "CutTopCut", "CutSuperior", "Cut Superior" }, safeCutTopFt);
                        }
                        else
                        {
                            // Pieza entera: desactivar el corte del vacío ("Cut" = 0) y mantener cotas internas en 5mm para no invalidar el boceto del vacío
                            SetParameterValueInstanceOnly(inst, new[] { "Cut", "Corte", "Cortar", "IsCut" }, 0);
                            SetParameterValueInstanceOnly(inst, new[] { "Cut Lat", "CutLat", "Cut_Lat", "CutLateral", "Cut Lateral", "Corte Lat" }, defaultFiveMmFt);
                            SetParameterValueInstanceOnly(inst, new[] { "Cut Top", "CutTop", "Cut_Top", "CutTopCut", "CutSuperior", "Cut Superior" }, defaultFiveMmFt);
                        }

                        // Banderas opcionales (IMPORTANTE: nunca incluir "Cut Lat" ni "Cut Top" aquí para no sobrescribir el valor en pies con 1.0 ft)
                        SetParameterValueInstanceOnly(inst, new[] { "Cut Lat Flag", "IsCutLat" }, rawCutLatFt > 0.0005 ? 1 : 0);
                        SetParameterValueInstanceOnly(inst, new[] { "Cut Top Flag", "IsCutTop" }, rawCutTopFt > 0.0005 ? 1 : 0);

                        // Comentarios de trazabilidad
                        Parameter? pComm = inst.get_Parameter(BuiltInParameter.ALL_MODEL_INSTANCE_COMMENTS);
                        if (pComm != null && !pComm.IsReadOnly)
                        {
                            pComm.Set(hasEffectiveCut
                                ? $"ENCHAPE-BALDOSA-CORTADA [Fila:{tile.IndexY + 1}, Col:{tile.IndexX + 1}] (CutLat:{rawCutLatFt * 30.48:0.1}cm, CutTop:{rawCutTopFt * 30.48:0.1}cm)"
                                : $"ENCHAPE-BALDOSA-ENTERA [Fila:{tile.IndexY + 1}, Col:{tile.IndexX + 1}]");
                        }

                        // Asignación de Material ("Mat" en Tiles Base.rfa)
                        if (tileMatId != ElementId.InvalidElementId)
                        {
                            SetMaterialParameter(inst, tileMatId);
                        }

                        // Registro en gestor de cubicaciones
                        MeasurementManager.MeasureAndApply(inst, null,
                            customLengthFt: tile.ActualLength_Ft > 0 ? tile.ActualLength_Ft : tile.Length_Ft,
                            customWidthFt: tile.ActualWidth_Ft > 0 ? tile.ActualWidth_Ft : tile.Width_Ft,
                            customAreaSqFt: tile.ActualAreaSqM * 10.7639,
                            customThicknessFt: tile.Thickness_Ft,
                            customVolumeCuFt: tile.ActualAreaSqM * 10.7639 * tile.Thickness_Ft,
                            units: 1);

                        if (hasEffectiveCut) cutTilesCount++;
                        else createdTilesCount++;
                    }
                }
                catch { }
            }
        }

        // =========================================================================================
        // INSTANCIACIÓN DE JUNTAS (PERIMETRALES 5mm + HORIZONTALES SPAN + SUB-OBJETOS VERTICALES):
        // Exclusivamente con Base Tiles Pega.rfa desde el (0,0) de cada junta sin solapamiento
        // =========================================================================================
        private void InstantiateFloorJoints(
            Document doc,
            ConvertirPisosWindow window,
            FamilySymbol pegaSymbol,
            ElementId groutMatId,
            ElementId perimMatId,
            List<TilingHostInfo> hostInfos,
            ref int jointCount)
        {
            double jointInnerFt = (window.JointInnerMm / 1000.0) * 3.28084;
            double jointPerimFt = (window.JointPerimMm / 1000.0) * 3.28084;
            double tileHFt = (window.TileThickCm / 100.0) * 3.28084;

            // Crear tipos dedicados en Base Tiles Pega.rfa para junta interna (3-4mm) y junta perimetral (5mm)
            FamilySymbol innerJointSymbol = GetOrCreateConfiguredJointSymbol(
                doc,
                pegaSymbol,
                $"Pega Interna {window.JointInnerMm:0.#}mm",
                jointInnerFt,
                tileHFt,
                groutMatId);

            FamilySymbol perimJointSymbol = (Math.Abs(jointPerimFt - jointInnerFt) > 1e-5 || perimMatId != groutMatId)
                ? GetOrCreateConfiguredJointSymbol(
                    doc,
                    pegaSymbol,
                    $"Pega Perimetral {window.JointPerimMm:0.#}mm",
                    jointPerimFt,
                    tileHFt,
                    perimMatId)
                : innerJointSymbol;

            foreach (var j in window.LayoutJoints)
            {
                if (j.LengthFt <= 0.001 || j.WidthFt <= 0.0001) continue;

                try
                {
                    FamilySymbol targetSymbol = j.IsPerimeter ? perimJointSymbol : innerJointSymbol;
                    FamilyInstance? inst = null;

                    XYZ placePoint = j.Corner3D;
                    XYZ refDir = (j.DirectionU3D != null && j.DirectionU3D.GetLength() > 0.1)
                        ? j.DirectionU3D.Normalize()
                        : XYZ.BasisX;

                    if (j.HostFaceRef != null)
                    {
                        Element hostElem = doc.GetElement(j.HostFaceRef.ElementId);
                        if (hostElem != null)
                        {
                            Face? planarFace = hostElem.GetGeometryObjectFromReference(j.HostFaceRef) as Face;
                            if (planarFace != null)
                            {
                                IntersectionResult proj = planarFace.Project(j.Corner3D);
                                if (proj != null)
                                {
                                    placePoint = proj.XYZPoint;
                                }
                            }
                        }

                        inst = doc.Create.NewFamilyInstance(
                            j.HostFaceRef,
                            placePoint,
                            refDir,
                            targetSymbol);
                    }

                    if (inst == null)
                    {
                        Level? hostLevel = hostInfos.FirstOrDefault()?.Level;
                        if (hostLevel != null)
                        {
                            inst = doc.Create.NewFamilyInstance(
                                placePoint,
                                targetSymbol,
                                hostLevel,
                                Autodesk.Revit.DB.Structure.StructuralType.NonStructural);

                            if (inst != null && Math.Abs(j.RotationRad) > 1e-4)
                            {
                                Line rotAxis = Line.CreateBound(placePoint, placePoint + j.Normal3D);
                                ElementTransformUtils.RotateElement(doc, inst.Id, rotAxis, j.RotationRad);
                            }
                        }
                    }

                    if (inst != null)
                    {
                        // En Base Tiles Pega.rfa:
                        // - "Lenght" (Ejemplar) controla el largo del tramo de junta (2m en horizontales, 60cm en sub-objetos verticales)
                        // - "Width" controla el espesor de la junta (3-4mm interna o 5mm perimetral)
                        // - "Thickness" controla la profundidad de la junta
                        SetParameterValueInstanceOnly(inst, new[] { "Lenght", "Length", "Largo", "Longitud" }, j.LengthFt);
                        SetParameterValueInstanceOnly(inst, new[] { "Width", "Ancho", "Espesor Junta" }, j.WidthFt);
                        SetParameterValueInstanceOnly(inst, new[] { "Thickness", "Height", "Espesor", "Profundidad" }, j.DepthFt);
                        SetParameterValueInstanceOnly(inst, new[] { "Cut Lat", "CutLat", "Cut Top", "CutTop" }, 0.0);

                        ElementId matIdToUse = j.MaterialId != ElementId.InvalidElementId
                            ? j.MaterialId
                            : (j.IsPerimeter ? perimMatId : groutMatId);

                        if (matIdToUse != ElementId.InvalidElementId)
                        {
                            SetMaterialParameter(inst, matIdToUse);
                        }

                        Parameter? pComm = inst.get_Parameter(BuiltInParameter.ALL_MODEL_INSTANCE_COMMENTS);
                        if (pComm != null && !pComm.IsReadOnly)
                        {
                            string kind = j.IsPerimeter
                                ? "ENCHAPE-JUNTA-PERIMETRAL"
                                : (j.IsVertical ? "ENCHAPE-PEGA-VERTICAL-SUBOBJETO" : "ENCHAPE-PEGA-HORIZONTAL-SPAN");
                            pComm.Set($"{kind} (L:{j.LengthFt * 30.48:0.1}cm, W:{j.WidthFt * 304.8:0.1}mm)");
                        }

                        double areaSqFt = j.LengthFt * j.WidthFt;
                        MeasurementManager.MeasureAndApply(inst, null,
                            customLengthFt: j.LengthFt,
                            customWidthFt: j.WidthFt,
                            customAreaSqFt: areaSqFt,
                            customThicknessFt: j.DepthFt,
                            customVolumeCuFt: areaSqFt * j.DepthFt,
                            units: 1);

                        jointCount++;
                    }
                }
                catch { }
            }
        }

        // =========================================================================================
        // INSTANCIACIÓN DE ZÓCALOS DESDE EL (0,0) PERIMETRAL CON FAMILIA CARGABLE
        // =========================================================================================
        private void InstantiateFloorSkirting(
            Document doc,
            ConvertirPisosWindow window,
            FamilySymbol skirtingSymbol,
            ElementId skirtingMatId,
            List<TilingHostInfo> hostInfos,
            ref int skirtingSegCount)
        {
            double skHeightFt = (window.SkirtingHeightCm / 100.0) * 3.28084;
            double skThickFt = (window.SkirtingThickCm / 100.0) * 3.28084;
            double skBevelFt = (window.SkirtingBevelMm / 1000.0) * 3.28084;

            FamilySymbol activeSkirtingSymbol = GetOrCreateConfiguredJointSymbol(
                doc,
                skirtingSymbol,
                $"Zocalo {window.SkirtingHeightCm:0.#}x{window.SkirtingThickCm:0.#}cm",
                skThickFt,
                skHeightFt,
                skirtingMatId);

            foreach (var seg in window.SkirtingSegments)
            {
                if (seg.LengthFt <= 0.005) continue;

                try
                {
                    FamilyInstance? inst = null;
                    XYZ placePoint = seg.Corner3D;
                    XYZ dirLen = (seg.Direction3D != null && seg.Direction3D.GetLength() > 0.1)
                        ? seg.Direction3D.Normalize()
                        : (seg.EndPoint - seg.StartPoint).Normalize();

                    // Si el símbolo proviene de Base Tiles Pega / Tiles Base (donde +Y es Lenght y +X es Width),
                    // orientar +X hacia el interior del piso y +Y a lo largo del tramo del zócalo
                    bool isTileOrPegaFamily = activeSkirtingSymbol.FamilyName.IndexOf("Tiles", StringComparison.OrdinalIgnoreCase) >= 0;
                    XYZ refDir = isTileOrPegaFamily
                        ? dirLen.CrossProduct(seg.Normal3D).Normalize()
                        : dirLen;

                    if (seg.HostFaceRef != null)
                    {
                        Element hostElem = doc.GetElement(seg.HostFaceRef.ElementId);
                        if (hostElem != null)
                        {
                            Face? planarFace = hostElem.GetGeometryObjectFromReference(seg.HostFaceRef) as Face;
                            if (planarFace != null)
                            {
                                IntersectionResult proj = planarFace.Project(placePoint);
                                if (proj != null)
                                {
                                    placePoint = proj.XYZPoint;
                                }
                            }
                        }

                        inst = doc.Create.NewFamilyInstance(
                            seg.HostFaceRef,
                            placePoint,
                            refDir,
                            activeSkirtingSymbol);
                    }

                    if (inst == null)
                    {
                        Level? hostLevel = hostInfos.FirstOrDefault()?.Level;
                        if (hostLevel != null)
                        {
                            inst = doc.Create.NewFamilyInstance(
                                placePoint,
                                activeSkirtingSymbol,
                                hostLevel,
                                Autodesk.Revit.DB.Structure.StructuralType.NonStructural);
                        }
                    }

                    if (inst != null)
                    {
                        SetParameterValueInstanceOnly(inst, new[] { "Lenght", "Length", "Largo", "Longitud" }, seg.LengthFt);
                        SetParameterValueInstanceOnly(inst, new[] { "Width", "Ancho", "Espesor" }, seg.ThicknessFt);
                        SetParameterValueInstanceOnly(inst, new[] { "Thickness", "Height", "Altura", "Alto" }, seg.HeightFt);
                        SetParameterValueInstanceOnly(inst, new[] { "Bevel", "Chaflan", "Bisel" }, skBevelFt);
                        SetParameterValueInstanceOnly(inst, new[] { "Cut Lat", "CutLat", "Cut Top", "CutTop" }, 0.0);

                        if (skirtingMatId != ElementId.InvalidElementId)
                        {
                            SetMaterialParameter(inst, skirtingMatId);
                        }

                        Parameter? pComm = inst.get_Parameter(BuiltInParameter.ALL_MODEL_INSTANCE_COMMENTS);
                        if (pComm != null && !pComm.IsReadOnly)
                        {
                            pComm.Set($"ZOCALO-PERIMETRAL (L:{seg.LengthFt * 30.48:0.1}cm, H:{seg.HeightFt * 30.48:0.1}cm)");
                        }

                        MeasurementManager.MeasureAndApply(inst, null,
                            customLengthFt: seg.LengthFt,
                            customWidthFt: seg.ThicknessFt,
                            customAreaSqFt: seg.LengthFt * seg.HeightFt,
                            customThicknessFt: seg.ThicknessFt,
                            customVolumeCuFt: seg.LengthFt * seg.HeightFt * seg.ThicknessFt,
                            units: 1);

                        skirtingSegCount++;
                    }
                }
                catch { }
            }
        }

        private FamilySymbol GetOrCreateConfiguredSymbol(
            Document doc,
            FamilySymbol baseSymbol,
            string targetTypeName,
            double widthFt,
            double lengthFt,
            double thicknessFt,
            ElementId matId)
        {
            FamilySymbol target = baseSymbol;
            string cleanName = SanitizeRevitName(targetTypeName, "Tiles Base Custom");

            try
            {
                var existing = baseSymbol.Family.GetFamilySymbolIds()
                    .Select(id => doc.GetElement(id) as FamilySymbol)
                    .FirstOrDefault(s => s != null && s.Name.Equals(cleanName, StringComparison.OrdinalIgnoreCase));

                if (existing != null)
                {
                    target = existing;
                }
                else if (!baseSymbol.Name.Equals(cleanName, StringComparison.OrdinalIgnoreCase))
                {
                    target = (FamilySymbol)baseSymbol.Duplicate(cleanName);
                }

                if (!target.IsActive) target.Activate();

                // En Tiles Base.rfa:
                // - Width = eje X (U), Lenght = eje Y (V), Thickness = espesor
                // - Offset = holgura exterior (5mm) de la extrusión de vacío en L (¡NUNCA poner en 0.0 para no colapsar el vacío!)
                double fiveMmFt = (5.0 / 1000.0) * 3.28084;
                SetParameterValue(target, new[] { "Width", "Ancho" }, widthFt);
                SetParameterValue(target, new[] { "Lenght", "Length", "Largo" }, lengthFt);
                SetParameterValue(target, new[] { "Thickness", "Height", "Espesor" }, thicknessFt);
                SetParameterValue(target, new[] { "Offset", "Desfase" }, fiveMmFt);

                if (matId != ElementId.InvalidElementId)
                {
                    SetMaterialParameter(target, matId);
                }
            }
            catch { }

            return target;
        }

        private FamilySymbol GetOrCreateConfiguredJointSymbol(
            Document doc,
            FamilySymbol baseSymbol,
            string targetTypeName,
            double widthFt,
            double thicknessFt,
            ElementId matId)
        {
            FamilySymbol target = baseSymbol;
            string cleanName = SanitizeRevitName(targetTypeName, "Base Tiles Pega Custom");

            try
            {
                var existing = baseSymbol.Family.GetFamilySymbolIds()
                    .Select(id => doc.GetElement(id) as FamilySymbol)
                    .FirstOrDefault(s => s != null && s.Name.Equals(cleanName, StringComparison.OrdinalIgnoreCase));

                if (existing != null)
                {
                    target = existing;
                }
                else if (!baseSymbol.Name.Equals(cleanName, StringComparison.OrdinalIgnoreCase))
                {
                    target = (FamilySymbol)baseSymbol.Duplicate(cleanName);
                }

                if (!target.IsActive) target.Activate();

                SetParameterValue(target, new[] { "Width", "Ancho" }, widthFt);
                SetParameterValue(target, new[] { "Thickness", "Height", "Espesor" }, thicknessFt);
                SetParameterValue(target, new[] { "Offset", "Desfase" }, 0.0);

                if (matId != ElementId.InvalidElementId)
                {
                    SetMaterialParameter(target, matId);
                }
            }
            catch { }

            return target;
        }

        private void SetMaterialParameter(Element elem, ElementId matId)
        {
            if (matId == ElementId.InvalidElementId) return;
            Parameter? pMat = elem.LookupParameter("Mat")
                          ?? elem.LookupParameter("Material")
                          ?? elem.get_Parameter(BuiltInParameter.STRUCTURAL_MATERIAL_PARAM)
                          ?? elem.get_Parameter(BuiltInParameter.MATERIAL_ID_PARAM);
            if (pMat != null && !pMat.IsReadOnly && pMat.StorageType == StorageType.ElementId)
            {
                pMat.Set(matId);
            }
        }

        private ElementId GetOrCreateLayerFloorType(Document doc, string typeName, double thicknessFt, ElementId matId)
        {
            string cleanName = SanitizeRevitName(typeName, "Capa Piso");
            var floorTypes = new FilteredElementCollector(doc)
                .OfClass(typeof(FloorType))
                .Cast<FloorType>()
                .Where(ft => !ft.IsFoundationSlab)
                .ToList();

            FloorType? existing = floorTypes.FirstOrDefault(ft => ft.Name.Equals(cleanName, StringComparison.OrdinalIgnoreCase));
            if (existing != null) return existing.Id;

            FloorType? baseFt = floorTypes.FirstOrDefault(ft => ft.GetCompoundStructure() != null);
            if (baseFt == null) return ElementId.InvalidElementId;

            try
            {
                FloorType newFt = (FloorType)baseFt.Duplicate(cleanName);
                CompoundStructure cs = newFt.GetCompoundStructure();
                if (cs != null)
                {
                    var layers = new List<CompoundStructureLayer>
                    {
                        new CompoundStructureLayer(Math.Max(0.002, thicknessFt), MaterialFunctionAssignment.Structure, matId)
                    };
                    cs.SetLayers(layers);
                    newFt.SetCompoundStructure(cs);
                }
                return newFt.Id;
            }
            catch
            {
                return baseFt.Id;
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
                    $@"g:\Mi unidad\git\reviewPlugIn\Familias\Floors-Tiles\{familyName}.rfa",
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

            // 1. Extracción del Contorno Base: Obtener el PlanarFace superior con su Reference estable
            var (topFace, faceRef) = FloorTilingService.GetTopPlanarFaceWithRef(floor);
            if (topFace == null) return null;

            // 2. Extraer su CurveLoop exterior principal y validar que sea cerrado
            var (outerLoop, innerLoops) = FloorTilingService.ExtractBoundaryLoops(topFace);
            if (outerLoop == null || outerLoop.IsOpen()) return null;

            // 3. Sistema de Coordenadas Local Cartesiano (0,0) alineado con el Span Direction del Floor
            var (localTransform, longestSegment, spanU, spanV) = FloorTilingService.CreateLocalCoordinateSystem(outerLoop, topFace, floor);

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
                HostFaceReference = faceRef ?? topFace.Reference,
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
