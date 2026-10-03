using System;
using System.Collections.Generic;
using System.Linq;
using Autodesk.Revit.Attributes;
using Autodesk.Revit.DB;
using Autodesk.Revit.UI;
using Autodesk.Revit.UI.Selection;

namespace RevitToSupabasePlugin
{
    public class SteelDeckFloorInfo
    {
        public ElementId FloorId { get; set; } = ElementId.InvalidElementId;
        public string Name { get; set; } = "";
        public double AreaM2 { get; set; }
        public double BaseElevationFt { get; set; }
        public Level? Level { get; set; }
        public List<XYZ> OuterBoundary { get; set; } = new List<XYZ>();
        public List<List<XYZ>> InnerBoundaries { get; set; } = new List<List<XYZ>>();
        public BoundingBoxXYZ? BoundingBox { get; set; }
        public XYZ Normal { get; set; } = XYZ.BasisZ;
    }

    [Transaction(TransactionMode.Manual)]
    public class CommandConvertirSteelDeck : IExternalCommand
    {
        public Result Execute(ExternalCommandData commandData, ref string message, ElementSet elements)
        {
            UIDocument uidoc = commandData.Application.ActiveUIDocument;
            Document doc = uidoc.Document;

            try
            {
                // 1. Obtener selección de Suelos (Floors)
                IList<Reference> selectedRefs;
                var currentSelection = uidoc.Selection.GetElementIds();
                var preselectedFloors = currentSelection
                    .Select(id => doc.GetElement(id))
                    .OfType<Floor>()
                    .ToList();

                if (preselectedFloors.Count > 0)
                {
                    selectedRefs = preselectedFloors.Select(f => new Reference(f)).ToList();
                }
                else
                {
                    selectedRefs = uidoc.Selection.PickObjects(
                        ObjectType.Element,
                        new FloorSelectionFilter(),
                        "Selecciona los Suelos (Floors) para modelar el sistema Steel Deck.");
                }

                if (selectedRefs.Count == 0) return Result.Cancelled;

                // 2. Extraer geometría de los suelos seleccionados
                List<SteelDeckFloorInfo> floorInfos = new List<SteelDeckFloorInfo>();
                foreach (Reference r in selectedRefs)
                {
                    if (doc.GetElement(r) is Floor floor)
                    {
                        var info = ExtractFloorInfo(floor);
                        if (info != null && info.OuterBoundary.Count >= 3)
                        {
                            floorInfos.Add(info);
                        }
                    }
                }

                if (floorInfos.Count == 0)
                {
                    Autodesk.Revit.UI.TaskDialog.Show("Aviso", "No se pudo extraer la geometría de los suelos seleccionados.");
                    return Result.Cancelled;
                }

                // 3. Abrir Ventana de Configuración y Modelado de Steel Deck
                var window = new ConvertirSteelDeckWindow(floorInfos, doc);
                bool? dialogRes = window.ShowDialog();

                if (dialogRes != true || !window.IsConfirmed || window.SelectedDeck == null)
                {
                    return Result.Cancelled;
                }

                // 4. Instanciar en Revit dentro de una Transacción
                using (Transaction t = new Transaction(doc, "Modelar Steel Deck y Concreto"))
                {
                    t.Start();

                    // Asegurar que los parámetros de medición (Longitud, Área, Volumen, Espesor, Unidades) existan y estén vinculados
                    MeasurementManager.EnsureMeasurementParameters(doc);

                    ElementId floorCatId = new ElementId(BuiltInCategory.OST_Floors);
                    ElementId studCatId = new ElementId(BuiltInCategory.OST_StructuralFraming);

                    ElementId steelMatId = window.SelectedSteelMaterial?.Id ?? ElementId.InvalidElementId;
                    ElementId concreteMatId = window.SelectedConcreteMaterial?.Id ?? ElementId.InvalidElementId;

                    int createdDecksCount = 0;
                    int createdStudsCount = 0;

                    double totalAreaSqFt = window.TotalAreaM2 / 0.092903;

                    // A. Modelar Lámina Grecada Steel Deck
                    if (window.GenerateDeckSheet && window.DeckSolids.Count > 0)
                    {
                        double ribHeightFt = (window.SelectedDeck.RibHeightCm / 100.0) * 3.28084;
                        double areaPerSolid = totalAreaSqFt / window.DeckSolids.Count;

                        foreach (var s in window.DeckSolids)
                        {
                            if (s == null || s.Volume <= 0.00001) continue;

                            DirectShape ds = DirectShape.CreateElement(doc, floorCatId);
                            ds.SetShape(new GeometryObject[] { s });
                            ds.Name = $"Lámina Steel Deck - {window.SelectedDeck.Name}";

                            Parameter pComm = ds.get_Parameter(BuiltInParameter.ALL_MODEL_INSTANCE_COMMENTS);
                            if (pComm != null && !pComm.IsReadOnly) pComm.Set("STEELDECK-LAMINA");

                            if (steelMatId != ElementId.InvalidElementId)
                            {
                                Parameter pMat = ds.get_Parameter(BuiltInParameter.MATERIAL_ID_PARAM);
                                if (pMat != null && !pMat.IsReadOnly) pMat.Set(steelMatId);
                            }

                            Parameter pDesc = ds.get_Parameter(BuiltInParameter.ALL_MODEL_DESCRIPTION);
                            if (pDesc != null && !pDesc.IsReadOnly) pDesc.Set($"Chapa Colaborante {window.SelectedDeck.Gauge} - Paso: {window.SelectedDeck.PitchCm}cm");

                            // Medición exacta sin duplicación de área
                            MeasurementManager.MeasureAndApply(ds, s, customAreaSqFt: areaPerSolid, customThicknessFt: ribHeightFt, units: 1);

                            createdDecksCount++;
                        }
                    }

                    // B. Modelar Capa de Compresión y Valles de Concreto
                    if (window.GenerateConcrete && window.ConcreteSolids.Count > 0)
                    {
                        double concreteThickFt = (window.ConcreteToppingCm / 100.0) * 3.28084;

                        for (int i = 0; i < window.ConcreteSolids.Count; i++)
                        {
                            Solid cs = window.ConcreteSolids[i];
                            if (cs == null || cs.Volume <= 0.00001) continue;

                            bool isTopping = (i == 0); // El primer sólido es la loseta de compresión superior
                            string concreteRole = isTopping ? "Capa de Compresión" : "Relleno de Valles";

                            DirectShape dsC = DirectShape.CreateElement(doc, floorCatId);
                            dsC.SetShape(new GeometryObject[] { cs });
                            dsC.Name = $"Concreto ({concreteRole}) - {window.SelectedDeck.Name}";

                            Parameter pComm = dsC.get_Parameter(BuiltInParameter.ALL_MODEL_INSTANCE_COMMENTS);
                            if (pComm != null && !pComm.IsReadOnly) pComm.Set("STEELDECK-CONCRETO");

                            if (concreteMatId != ElementId.InvalidElementId)
                            {
                                Parameter pMat = dsC.get_Parameter(BuiltInParameter.MATERIAL_ID_PARAM);
                                if (pMat != null && !pMat.IsReadOnly) pMat.Set(concreteMatId);
                            }

                            Parameter pDesc = dsC.get_Parameter(BuiltInParameter.ALL_MODEL_DESCRIPTION);
                            if (pDesc != null && !pDesc.IsReadOnly) pDesc.Set($"Concreto {concreteRole} e={window.ConcreteToppingCm}cm");

                            // Asignar área solo a la capa de compresión principal para evitar duplicar el área de piso en la tabla
                            double solidArea = isTopping ? totalAreaSqFt : 0.0;
                            MeasurementManager.MeasureAndApply(dsC, cs, customAreaSqFt: solidArea, customVolumeCuFt: cs.Volume, customThicknessFt: concreteThickFt, units: 1);
                        }
                    }

                    // C. Modelar Conectores de Cortante (Shear Studs)
                    if (window.GenerateStuds && window.StudPoints.Count > 0)
                    {
                        double studRadFt = (window.SelectedDeck.StudDiameterIn > 0 ? (window.SelectedDeck.StudDiameterIn * 0.0254) / 2.0 : 0.0095) * 3.28084;
                        double studHFt = (window.StudHeightCm / 100.0) * 3.28084;

                        foreach (var pt in window.StudPoints)
                        {
                            Solid? studSolid = CreateStudSolid(pt, studRadFt, studHFt);
                            if (studSolid != null)
                            {
                                DirectShape dsS = DirectShape.CreateElement(doc, studCatId);
                                dsS.SetShape(new GeometryObject[] { studSolid });
                                dsS.Name = $"Stud Cortante Ø{window.SelectedDeck.StudDiameterIn}\" - Nelson";

                                Parameter pComm = dsS.get_Parameter(BuiltInParameter.ALL_MODEL_INSTANCE_COMMENTS);
                                if (pComm != null && !pComm.IsReadOnly) pComm.Set("STEELDECK-STUD");

                                if (steelMatId != ElementId.InvalidElementId)
                                {
                                    Parameter pMat = dsS.get_Parameter(BuiltInParameter.MATERIAL_ID_PARAM);
                                    if (pMat != null && !pMat.IsReadOnly) pMat.Set(steelMatId);
                                }

                                MeasurementManager.MeasureAndApply(dsS, studSolid, customLengthFt: studHFt, customThicknessFt: studHFt, customWidthFt: studRadFt * 2, customVolumeCuFt: studSolid.Volume, units: 1);

                                createdStudsCount++;
                            }
                        }
                    }

                    t.Commit();

                    Autodesk.Revit.UI.TaskDialog.Show(
                        "Modelación Steel Deck Completada",
                        $"Se ha generado con éxito el sistema Steel Deck en Revit:\n\n" +
                        $"• Perfil: {window.SelectedDeck.Name}\n" +
                        $"• Área neta modelada: {window.TotalAreaM2:0.00} m²\n" +
                        $"• Concreto estimado: {window.TotalConcreteVolM3:0.00} m³ (Topping {window.ConcreteToppingCm}cm)\n" +
                        $"• Pernos de cortante (Studs): {createdStudsCount} unidades");
                }

                return Result.Succeeded;
            }
            catch (Exception ex)
            {
                message = ex.Message;
                return Result.Failed;
            }
        }

        private static SteelDeckFloorInfo? ExtractFloorInfo(Floor floor)
        {
            if (floor == null) return null;

            var info = new SteelDeckFloorInfo
            {
                FloorId = floor.Id,
                Name = floor.Name,
                Level = floor.Document.GetElement(floor.LevelId) as Level,
                BoundingBox = floor.get_BoundingBox(null)
            };

            PlanarFace? topFace = FloorTilingService.GetTopPlanarFace(floor);
            if (topFace != null)
            {
                info.Normal = topFace.FaceNormal.Normalize();
                info.BaseElevationFt = topFace.Origin.Z;

                var (outer, inners) = FloorTilingService.ExtractBoundaryLoops(topFace);
                if (outer != null && !outer.IsOpen())
                {
                    foreach (Curve c in outer)
                    {
                        info.OuterBoundary.Add(c.GetEndPoint(0));
                    }

                    foreach (var loop in inners)
                    {
                        var innerPts = new List<XYZ>();
                        foreach (Curve c in loop)
                        {
                            innerPts.Add(c.GetEndPoint(0));
                        }
                        info.InnerBoundaries.Add(innerPts);
                    }
                }
            }

            Parameter pArea = floor.get_Parameter(BuiltInParameter.HOST_AREA_COMPUTED);
            if (pArea != null && pArea.HasValue)
            {
                info.AreaM2 = pArea.AsDouble() * 0.092903;
            }

            return info;
        }

        private static Solid? CreateStudSolid(XYZ baseCenter, double radiusFt, double heightFt)
        {
            try
            {
                Plane plane = Plane.CreateByNormalAndOrigin(XYZ.BasisZ, baseCenter);
                Arc arc1 = Arc.Create(plane, radiusFt, 0, Math.PI);
                Arc arc2 = Arc.Create(plane, radiusFt, Math.PI, 2 * Math.PI);

                CurveLoop loop = new CurveLoop();
                loop.Append(arc1);
                loop.Append(arc2);

                Solid cylinder = GeometryCreationUtilities.CreateExtrusionGeometry(
                    new List<CurveLoop> { loop },
                    XYZ.BasisZ,
                    heightFt);

                return cylinder;
            }
            catch
            {
                return null;
            }
        }
    }
}
