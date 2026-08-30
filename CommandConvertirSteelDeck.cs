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

                    ElementId floorCatId = new ElementId(BuiltInCategory.OST_Floors);
                    ElementId studCatId = new ElementId(BuiltInCategory.OST_StructuralFraming);

                    ElementId steelMatId = window.SelectedSteelMaterial?.Id ?? ElementId.InvalidElementId;
                    ElementId concreteMatId = window.SelectedConcreteMaterial?.Id ?? ElementId.InvalidElementId;

                    int createdDecksCount = 0;
                    int createdStudsCount = 0;

                    foreach (var floorInfo in floorInfos)
                    {
                        // A. Modelar Lámina Grecada Steel Deck
                        if (window.GenerateDeckSheet && window.DeckSolids.Count > 0)
                        {
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

                                createdDecksCount++;
                            }
                        }

                        // B. Modelar Capa de Compresión de Concreto
                        if (window.GenerateConcrete && window.ConcreteSolids.Count > 0)
                        {
                            foreach (var cs in window.ConcreteSolids)
                            {
                                if (cs == null || cs.Volume <= 0.00001) continue;

                                DirectShape dsC = DirectShape.CreateElement(doc, floorCatId);
                                dsC.SetShape(new GeometryObject[] { cs });
                                dsC.Name = $"Concreto Losa Mixta - {window.SelectedDeck.Name}";

                                Parameter pComm = dsC.get_Parameter(BuiltInParameter.ALL_MODEL_INSTANCE_COMMENTS);
                                if (pComm != null && !pComm.IsReadOnly) pComm.Set("STEELDECK-CONCRETO");

                                if (concreteMatId != ElementId.InvalidElementId)
                                {
                                    Parameter pMat = dsC.get_Parameter(BuiltInParameter.MATERIAL_ID_PARAM);
                                    if (pMat != null && !pMat.IsReadOnly) pMat.Set(concreteMatId);
                                }

                                Parameter pDesc = dsC.get_Parameter(BuiltInParameter.ALL_MODEL_DESCRIPTION);
                                if (pDesc != null && !pDesc.IsReadOnly) pDesc.Set($"Capa de Compresión e={window.ConcreteToppingCm}cm + Valles Steel Deck");
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

                                    createdStudsCount++;
                                }
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

        private SteelDeckFloorInfo? ExtractFloorInfo(Floor floor)
        {
            var info = new SteelDeckFloorInfo
            {
                FloorId = floor.Id,
                Name = floor.Name,
                Level = floor.Document.GetElement(floor.LevelId) as Level
            };

            Options opt = new Options
            {
                ComputeReferences = true,
                IncludeNonVisibleObjects = false,
                DetailLevel = ViewDetailLevel.Fine
            };

            GeometryElement geomElem = floor.get_Geometry(opt);
            if (geomElem == null) return null;

            double maxArea = 0;
            PlanarFace? bestFace = null;

            foreach (GeometryObject obj in geomElem)
            {
                if (obj is Solid solid && solid.Faces.Size > 0)
                {
                    foreach (Face face in solid.Faces)
                    {
                        if (face is PlanarFace pf)
                        {
                            // Cara superior o inferior (Normal hacia Z)
                            if (Math.Abs(pf.FaceNormal.Z) > 0.8 && pf.Area > maxArea)
                            {
                                maxArea = pf.Area;
                                bestFace = pf;
                            }
                        }
                    }
                }
            }

            if (bestFace == null) return null;

            info.AreaM2 = bestFace.Area * 0.092903;
            info.BaseElevationFt = bestFace.Origin.Z;
            info.BoundingBox = floor.get_BoundingBox(null);
            info.Normal = bestFace.FaceNormal;

            var edgeLoops = bestFace.GetEdgesAsCurveLoops();
            for (int i = 0; i < edgeLoops.Count; i++)
            {
                CurveLoop loop = edgeLoops[i];
                List<XYZ> pts = new List<XYZ>();
                foreach (Curve c in loop)
                {
                    pts.Add(c.GetEndPoint(0));
                }

                if (pts.Count >= 3)
                {
                    if (i == 0) info.OuterBoundary = pts;
                    else info.InnerBoundaries.Add(pts);
                }
            }

            return info;
        }

        private Solid? CreateStudSolid(XYZ baseCenter, double radiusFt, double heightFt)
        {
            try
            {
                double headRad = radiusFt * 1.6;
                double headHFt = heightFt * 0.15;
                double shaftHFt = heightFt - headHFt;

                // Crear vástago
                CurveLoop shaftLoop = new CurveLoop();
                Arc arc1 = Arc.Create(new XYZ(baseCenter.X - radiusFt, baseCenter.Y, baseCenter.Z),
                                      new XYZ(baseCenter.X + radiusFt, baseCenter.Y, baseCenter.Z),
                                      new XYZ(baseCenter.X, baseCenter.Y + radiusFt, baseCenter.Z));
                Arc arc2 = Arc.Create(new XYZ(baseCenter.X + radiusFt, baseCenter.Y, baseCenter.Z),
                                      new XYZ(baseCenter.X - radiusFt, baseCenter.Y, baseCenter.Z),
                                      new XYZ(baseCenter.X, baseCenter.Y - radiusFt, baseCenter.Z));
                shaftLoop.Append(arc1);
                shaftLoop.Append(arc2);

                Solid shaft = GeometryCreationUtilities.CreateExtrusionGeometry(new List<CurveLoop> { shaftLoop }, XYZ.BasisZ, shaftHFt);

                // Crear cabeza
                CurveLoop headLoop = new CurveLoop();
                Arc headArc1 = Arc.Create(new XYZ(baseCenter.X - headRad, baseCenter.Y, baseCenter.Z + shaftHFt),
                                          new XYZ(baseCenter.X + headRad, baseCenter.Y, baseCenter.Z + shaftHFt),
                                          new XYZ(baseCenter.X, baseCenter.Y + headRad, baseCenter.Z + shaftHFt));
                Arc headArc2 = Arc.Create(new XYZ(baseCenter.X + headRad, baseCenter.Y, baseCenter.Z + shaftHFt),
                                          new XYZ(baseCenter.X - headRad, baseCenter.Y, baseCenter.Z + shaftHFt),
                                          new XYZ(baseCenter.X, baseCenter.Y - headRad, baseCenter.Z + shaftHFt));
                headLoop.Append(headArc1);
                headLoop.Append(headArc2);

                Solid head = GeometryCreationUtilities.CreateExtrusionGeometry(new List<CurveLoop> { headLoop }, XYZ.BasisZ, headHFt);

                return BooleanOperationsUtils.ExecuteBooleanOperation(shaft, head, BooleanOperationsType.Union);
            }
            catch
            {
                return null;
            }
        }
    }
}
