using System;
using System.Collections.Generic;
using System.Linq;
using Autodesk.Revit.Attributes;
using Autodesk.Revit.DB;
using Autodesk.Revit.UI;
using Autodesk.Revit.UI.Selection;

namespace RevitToSupabasePlugin
{
    [Transaction(TransactionMode.Manual)]
    public class CommandConvertirMurosALadrillos : IExternalCommand
    {
        public Result Execute(
          ExternalCommandData commandData,
          ref string message,
          ElementSet elements)
        {
            UIDocument uidoc = commandData.Application.ActiveUIDocument;
            Document doc = uidoc.Document;

            try
            {
                // 1. Permitir al usuario seleccionar muros
                IList<Reference> selectedRefs = uidoc.Selection.PickObjects(ObjectType.Element, new WallSelectionFilter(), "Selecciona los muros que deseas convertir a ladrillos.");

                if (selectedRefs.Count == 0)
                {
                    return Result.Cancelled;
                }

                // 2. Extraer dimensiones del primer muro para previsualización
                double firstWallLength = 10.0; // default 10 ft
                double firstWallHeight = 10.0;

                Element firstElem = doc.GetElement(selectedRefs.First());
                if (firstElem is Wall firstWall)
                {
                    if (firstWall.Location as LocationCurve is LocationCurve locCurve && locCurve.Curve is Line line)
                    {
                        firstWallLength = line.Length;
                    }
                    Parameter hp = firstWall.get_Parameter(BuiltInParameter.WALL_USER_HEIGHT_PARAM);
                    if (hp != null) firstWallHeight = hp.AsDouble();
                }

                // 3. Mostrar la ventana de configuración
                var window = new ConvertirMurosWindow(selectedRefs.Count, firstWallLength, firstWallHeight);
                window.ShowDialog();

                if (!window.IsConfirmed || window.SelectedBrick == null)
                {
                    return Result.Cancelled;
                }

                BrickType brick = window.SelectedBrick;
                string pattern = window.SelectedPattern;

                // Buscar el FamilySymbol en el documento
                FilteredElementCollector collector = new FilteredElementCollector(doc);
                collector.OfClass(typeof(Family));
                Family? targetFamily = collector.Cast<Family>().FirstOrDefault(f => f.Name.StartsWith(brick.Name ?? ""));

                if (targetFamily == null)
                {
                    Autodesk.Revit.UI.TaskDialog.Show("Error", $"No se encontró la familia para el ladrillo '{brick.Name}' en el proyecto. Genera la familia primero.");
                    return Result.Failed;
                }

                ISet<ElementId> symbolIds = targetFamily.GetFamilySymbolIds();
                if (symbolIds.Count == 0)
                {
                    Autodesk.Revit.UI.TaskDialog.Show("Error", $"La familia '{brick.Name}' no tiene símbolos (tipos).");
                    return Result.Failed;
                }

                FamilySymbol? brickSymbol = doc.GetElement(symbolIds.First()) as FamilySymbol;
                if (brickSymbol == null) return Result.Failed;

                using (Transaction tx = new Transaction(doc, "Convertir Muros a Ladrillos"))
                {
                    tx.Start();
                    if (!brickSymbol.IsActive) brickSymbol.Activate();

                    int wallsProcessed = 0;
                    foreach (Reference r in selectedRefs)
                    {
                        Element elem = doc.GetElement(r);
                        if (elem is Wall wall)
                        {
                            LocationCurve? locCurve = wall.Location as LocationCurve;
                            if (locCurve == null) continue;
                            
                            Line? line = locCurve.Curve as Line;
                            if (line == null) continue; // Only handle straight walls for now

                            XYZ startPoint = line.GetEndPoint(0);
                            XYZ endPoint = line.GetEndPoint(1);
                            XYZ dir = (endPoint - startPoint).Normalize();
                            double totalLength = line.Length;
                            
                            // Obtener Altura del muro
                            double totalHeight = 10.0; // default (10 pies ~ 3m)
                            Parameter heightParam = wall.get_Parameter(BuiltInParameter.WALL_USER_HEIGHT_PARAM);
                            if (heightParam != null) totalHeight = heightParam.AsDouble();

                            double brickLFt = brick.LengthCm / 30.48;
                            double brickHFt = brick.HeightCm / 30.48;
                            double pegaFt = window.PegaCm / 30.48;

                            // Ángulo del muro respecto al Eje X para rotar el ladrillo
                            double angle = XYZ.BasisX.AngleTo(dir);
                            if (dir.Y < 0) angle = 2 * Math.PI - angle;

                            Level? wallLevel = doc.GetElement(wall.LevelId) as Level;

                            // Instanciar los ladrillos exactamente según el patrón diseñado en el Editor (LayoutItems)
                            foreach (var item in window.LayoutItems)
                            {
                                // Si el ladrillo tiene CutLeft, su origen (X=0 en la familia) estaba más a la izquierda
                                // Restamos CutLeft_Ft de PosX_Ft para que el corte empiece exactamente en PosX_Ft
                                double originX = item.PosX_Ft - item.CutLeft_Ft;
                                
                                XYZ location = startPoint + dir * originX + XYZ.BasisZ * item.PosZ_Ft;
                                FamilyInstance inst = doc.Create.NewFamilyInstance(location, brickSymbol, wallLevel, Autodesk.Revit.DB.Structure.StructuralType.NonStructural);
                                
                                Line axis = Line.CreateBound(location, location + XYZ.BasisZ);
                                ElementTransformUtils.RotateElement(doc, inst.Id, axis, angle);

                                // Aplicar recortes
                                Parameter pRecorteI = inst.LookupParameter("RecorteIzquierdo");
                                if (pRecorteI != null && !pRecorteI.IsReadOnly) 
                                    pRecorteI.Set(item.CutLeft_Ft > 0 ? item.CutLeft_Ft : 0.003); // 0.003 es el min

                                Parameter pRecorteP = inst.LookupParameter("RecorteProfundidad");
                                if (pRecorteP != null && !pRecorteP.IsReadOnly) 
                                    pRecorteP.Set(item.CutRight_Ft > 0 ? item.CutRight_Ft : 0.003);

                                Parameter pRecorteV = inst.LookupParameter("RecorteVertical");
                                if (pRecorteV != null && !pRecorteV.IsReadOnly) 
                                    pRecorteV.Set(item.CutTop_Ft > 0 ? item.CutTop_Ft : 0.003);

                                // Aplicar Dovela
                                Parameter pDovela = inst.LookupParameter("ProfundidadDovela");
                                if (pDovela != null && !pDovela.IsReadOnly) 
                                {
                                    // Si tiene dovela, perforamos todo el alto (heightFt + margen)
                                    double brickHeightFt = brick.HeightCm / 30.48;
                                    pDovela.Set(item.HasDovela ? brickHeightFt + 0.5 : 0.003);
                                }

                                // Aplicar Pega
                                Parameter pGrosorP = inst.LookupParameter("GrosorPega");
                                if (pGrosorP != null && !pGrosorP.IsReadOnly) pGrosorP.Set(pegaFt);
                                
                                Parameter pVerP = inst.LookupParameter("VerPega");
                                if (pVerP != null && !pVerP.IsReadOnly) pVerP.Set(window.VerPega ? 1 : 0);

                                // Aplicar Canchado (solo si hay un parámetro o lógica para él, de momento lo dejamos como dato de UI)
                                // Podríamos cambiar el material si hubiera un parámetro.
                            }

                            // 3. Eliminar el muro original
                            doc.Delete(wall.Id);
                            wallsProcessed++;
                        }
                    }

                    tx.Commit();

                    Autodesk.Revit.UI.TaskDialog.Show("Conversión Exitosa", 
                        $"Se eliminaron {wallsProcessed} muros.\n\n" +
                        $"Preparado para instanciar:\n" +
                        $"- Ladrillo: {brick.Name} ({brick.LengthCm}x{brick.WidthCm}x{brick.HeightCm} cm)\n" +
                        $"- Pega: {window.PegaCm} cm\n" +
                        $"- Patrón: {pattern}\n\n" +
                        "(La instanciación 3D requerirá que la familia .rfa correspondiente esté cargada en el proyecto).");
                }

                return Result.Succeeded;
            }
            catch (Autodesk.Revit.Exceptions.OperationCanceledException)
            {
                return Result.Cancelled;
            }
            catch (Exception ex)
            {
                message = ex.Message;
                return Result.Failed;
            }
        }
    }

    public class WallSelectionFilter : ISelectionFilter
    {
        public bool AllowElement(Element elem)
        {
            return elem is Wall;
        }

        public bool AllowReference(Reference reference, XYZ position)
        {
            return false;
        }
    }
}
