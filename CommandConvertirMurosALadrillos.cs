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

                // 3. Obtener familias directamente de Revit (ignorar BricksDB.json)
                List<BrickType> availableBricks = new List<BrickType>();
                FilteredElementCollector familyCollector = new FilteredElementCollector(doc);
                var brickSymbols = familyCollector.OfClass(typeof(FamilySymbol))
                                            .Cast<FamilySymbol>()
                                            .Where(s => s.FamilyName.Contains("Ladrillo") || s.Name.Contains("Ladrillo"));

                foreach (FamilySymbol symbol in brickSymbols)
                {
                    double lFt = GetSymbolParameterValue(symbol, new[] { "Depth" });
                    double hFt = GetSymbolParameterValue(symbol, new[] { "Height" });
                    double wFt = GetSymbolParameterValue(symbol, new[] { "Width" });
                    double tFt = GetSymbolParameterValue(symbol, new[] { "Thickness" });

                    // Fallback to BoundingBox si faltan parámetros
                    if (hFt == 0 || lFt == 0 || wFt == 0)
                    {
                        BoundingBoxXYZ bbox = symbol.get_BoundingBox(null);
                        if (bbox != null)
                        {
                            if (hFt == 0) hFt = bbox.Max.Z - bbox.Min.Z;
                            if (lFt == 0) lFt = bbox.Max.X - bbox.Min.X;
                            if (wFt == 0) wFt = bbox.Max.Y - bbox.Min.Y;
                        }
                    }

                    if (hFt > 0 && lFt > 0 && wFt > 0)
                    {
                        BrickType bt = new BrickType
                        {
                            Id = symbol.UniqueId,
                            Name = symbol.FamilyName,
                            HeightCm = Math.Round(hFt * 30.48, 2),
                            LengthCm = Math.Round(lFt * 30.48, 2),
                            WidthCm = Math.Round(wFt * 30.48, 2),
                            HasCanchado = true,
                            HasDovela = true,
                            HasMortar = true,
                            HasTrims = true
                        };
                        
                        if (!availableBricks.Any(b => b.Name == bt.Name && b.HeightCm == bt.HeightCm))
                        {
                            availableBricks.Add(bt);
                        }
                    }
                }

                if (availableBricks.Count == 0)
                {
                    Autodesk.Revit.UI.TaskDialog.Show("Error", "No se encontraron familias de ladrillos válidas en el proyecto. Asegúrate de cargarlas primero.");
                    return Result.Failed;
                }

                // 4. Mostrar la ventana de configuración
                var window = new ConvertirMurosWindow(selectedRefs.Count, firstWallLength, firstWallHeight, availableBricks, doc);
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

                    List<Wall> selectedWalls = selectedRefs
                        .Select(r => doc.GetElement(r) as Wall)
                        .Where(w => w != null && w.Location is LocationCurve lc && lc.Curve is Line)
                        .Cast<Wall>()
                        .ToList();

                    List<WallNodeInfo> allNodes = WallNodeGraph.AnalyzeWallNodes(selectedWalls);
                    int wallsProcessed = 0;

                    foreach (Wall wall in selectedWalls)
                    {
                        LocationCurve locCurve = (wall.Location as LocationCurve)!;
                        Line line = (locCurve.Curve as Line)!;

                        XYZ startPoint = line.GetEndPoint(0);
                        XYZ endPoint = line.GetEndPoint(1);
                        XYZ dir = (endPoint - startPoint).Normalize();
                        double totalLength = line.Length;
                        double wallWidth = wall.Width;

                        // Obtener Nodos conectados a este muro
                        var wallNodes = allNodes.Where(n => n.WallA.Id == wall.Id || n.WallB.Id == wall.Id).ToList();

                        // Obtener Altura del muro
                        double totalHeight = 10.0;
                        Parameter heightParam = wall.get_Parameter(BuiltInParameter.WALL_USER_HEIGHT_PARAM);
                        if (heightParam != null) totalHeight = heightParam.AsDouble();

                        double brickLFt = brick.LengthCm / 30.48;
                        double brickHFt = brick.HeightCm / 30.48;
                        double pegaFt = window.PegaCm / 30.48;

                        double angle = XYZ.BasisX.AngleTo(dir);
                        if (dir.Y < 0) angle = 2 * Math.PI - angle;

                        Level? wallLevel = doc.GetElement(wall.LevelId) as Level;
                        List<BrickLayoutItem> wallPlacedItems = new List<BrickLayoutItem>();

                        // Instanciar ladrillos por hilada aplicando alternancia de traba en esquinas L y T
                        foreach (var item in window.LayoutItems)
                        {
                            int rowIndex = item.ZIndex;
                            bool isOddRow = (rowIndex % 2 == 1);

                            double itemPosX = item.PosX_Ft;
                            double itemLength = item.Length_Ft;
                            bool skipBrick = false;

                            WallIntersectionRole brickRole = WallIntersectionRole.Standard;

                            // Ajustar posiciones según los nodos de intersección L y T
                            foreach (var node in wallNodes)
                            {
                                bool isWallA = (node.WallA.Id == wall.Id);
                                Wall otherWall = isWallA ? node.WallB : node.WallA;
                                double otherWidth = otherWall.Width;

                                if (node.NodeType == WallNodeType.L_Corner)
                                {
                                    int myEndIndex = isWallA ? node.EndIndexA : node.EndIndexB;
                                    bool isDominant = isWallA ? isOddRow : !isOddRow;

                                    if (myEndIndex == 0 && itemPosX < brickLFt)
                                    {
                                        if (isDominant)
                                        {
                                            itemPosX -= (otherWidth / 2.0);
                                            itemLength += (otherWidth / 2.0);
                                            brickRole = WallIntersectionRole.CornerHeader;
                                        }
                                        else
                                        {
                                            itemPosX += (otherWidth / 2.0);
                                            itemLength -= (otherWidth / 2.0);
                                            brickRole = WallIntersectionRole.CornerAbutment;
                                        }
                                    }
                                    else if (myEndIndex == 1 && itemPosX + itemLength > totalLength - brickLFt)
                                    {
                                        if (isDominant)
                                        {
                                            itemLength += (otherWidth / 2.0);
                                            brickRole = WallIntersectionRole.CornerHeader;
                                        }
                                        else
                                        {
                                            itemLength -= (otherWidth / 2.0);
                                            brickRole = WallIntersectionRole.CornerAbutment;
                                        }
                                    }
                                }
                                else if (node.NodeType == WallNodeType.T_Junction)
                                {
                                    if (!isWallA) // Muro Secundario (Pierna de la T)
                                    {
                                        int myEndIndex = node.EndIndexB;
                                        if (myEndIndex == 0 && itemPosX < brickLFt)
                                        {
                                            if (isOddRow) // Hilada Impar: Enjarje penetrante dentro del Muro Principal
                                            {
                                                itemPosX -= (otherWidth / 2.0);
                                                itemLength += (otherWidth / 2.0);
                                                brickRole = WallIntersectionRole.TieKey;
                                            }
                                            else // Hilada Par: Muere de tope (a testa) contra la cara del Muro Principal
                                            {
                                                itemPosX += (otherWidth / 2.0);
                                                itemLength -= (otherWidth / 2.0);
                                                brickRole = WallIntersectionRole.CornerAbutment;
                                            }
                                        }
                                        else if (myEndIndex == 1 && itemPosX + itemLength > totalLength - brickLFt)
                                        {
                                            if (isOddRow) // Hilada Impar: Enjarje penetrante
                                            {
                                                itemLength += (otherWidth / 2.0);
                                                brickRole = WallIntersectionRole.TieKey;
                                            }
                                            else // Hilada Par: Muere a testa
                                            {
                                                itemLength -= (otherWidth / 2.0);
                                                brickRole = WallIntersectionRole.CornerAbutment;
                                            }
                                        }
                                    }
                                    else // Muro Principal (Longitudinal)
                                    {
                                        double nodeDistOnMain = (node.Point - startPoint).DotProduct(dir);
                                        double secWidth = node.WallB.Width;
                                        double gapStart = nodeDistOnMain - (secWidth / 2.0);
                                        double gapEnd = nodeDistOnMain + (secWidth / 2.0);

                                        if (isOddRow) // Hilada Impar: Abrir ventana de enjarje para el Muro Secundario
                                        {
                                            if (itemPosX >= gapStart - 0.001 && itemPosX + itemLength <= gapEnd + 0.001)
                                            {
                                                skipBrick = true;
                                                break;
                                            }
                                            if (itemPosX < gapStart && itemPosX + itemLength > gapStart)
                                            {
                                                itemLength = gapStart - itemPosX;
                                            }
                                            else if (itemPosX < gapEnd && itemPosX + itemLength > gapEnd)
                                            {
                                                double newLen = (itemPosX + itemLength) - gapEnd;
                                                itemPosX = gapEnd;
                                                itemLength = newLen;
                                            }
                                        }
                                    }
                                }
                            }

                            if (skipBrick || itemLength <= 0.01) continue;

                            var placedItem = new BrickLayoutItem
                            {
                                ZIndex = item.ZIndex,
                                XIndex = item.XIndex,
                                PosX_Ft = itemPosX,
                                PosZ_Ft = item.PosZ_Ft,
                                Length_Ft = itemLength,
                                Height_Ft = item.Height_Ft,
                                CutLeft_Ft = item.CutLeft_Ft,
                                CutRight_Ft = item.CutRight_Ft,
                                CutTop_Ft = item.CutTop_Ft,
                                HasDovela = item.HasDovela,
                                IsCanchado = item.IsCanchado,
                                Role = brickRole
                            };
                            wallPlacedItems.Add(placedItem);

                            XYZ location = startPoint + dir * itemPosX + XYZ.BasisZ * item.PosZ_Ft;
                            FamilyInstance inst = doc.Create.NewFamilyInstance(location, brickSymbol, wallLevel, Autodesk.Revit.DB.Structure.StructuralType.NonStructural);
                            
                            Line axis = Line.CreateBound(location, location + XYZ.BasisZ);
                            ElementTransformUtils.RotateElement(doc, inst.Id, axis, angle);

                                // Auto-centrar el ladrillo en el eje del muro sin importar el origen de la familia
                                doc.Regenerate(); // Necesario para obtener el BoundingBox actualizado tras la rotación
                                BoundingBoxXYZ bbox = inst.get_BoundingBox(null);
                                if (bbox != null)
                                {
                                    XYZ normal = dir.CrossProduct(XYZ.BasisZ);
                                    XYZ bboxCenter = (bbox.Min + bbox.Max) / 2.0;
                                    double distFromCenterline = (bboxCenter - startPoint).DotProduct(normal);
                                    // Movemos el ladrillo en la dirección opuesta para que su centro físico quede sobre el eje
                                    ElementTransformUtils.MoveElement(doc, inst.Id, normal * (-distFromCenterline));
                                }

                                // Recorte lateral (solo si la pieza es más corta que el ladrillo completo)
                                double lateralCut = brickLFt - itemLength;
                                if (lateralCut > 0.001)
                                {
                                    Parameter pCutLat = inst.LookupParameter("Cut Lat");
                                    if (pCutLat != null && !pCutLat.IsReadOnly) pCutLat.Set(1);

                                    Parameter pRecorteLat = inst.LookupParameter("RECORTE LATERAL");
                                    if (pRecorteLat != null && !pRecorteLat.IsReadOnly) pRecorteLat.Set(lateralCut);
                                }

                                // Recorte superior (solo si la pieza es más baja que la altura completa)
                                double topCut = brickHFt - item.Height_Ft;
                                if (topCut > 0.001)
                                {
                                    Parameter pCutTop = inst.LookupParameter("Cut Top");
                                    if (pCutTop != null && !pCutTop.IsReadOnly) pCutTop.Set(1);

                                    Parameter pRecorteSup = inst.LookupParameter("RECORTE SUPERIOR");
                                    if (pRecorteSup != null && !pRecorteSup.IsReadOnly) pRecorteSup.Set(topCut);
                                }

                                Parameter pCutCancha = inst.LookupParameter("Cut Cancha");
                                if (item.IsCanchado && pCutCancha != null && !pCutCancha.IsReadOnly) 
                                    pCutCancha.Set(1);

                                Parameter pCutDobela = inst.LookupParameter("Cut Dobela");
                                if (item.HasDovela && pCutDobela != null && !pCutDobela.IsReadOnly) 
                                    pCutDobela.Set(1);

                                // Aplicar Pega (si la familia aún tiene el parámetro)
                                Parameter pGrosorP = inst.LookupParameter("GrosorPega");
                                if (pGrosorP != null && !pGrosorP.IsReadOnly) pGrosorP.Set(pegaFt);
                                
                                Parameter pVerP = inst.LookupParameter("VerPega");
                                if (pVerP != null && !pVerP.IsReadOnly) pVerP.Set(window.VerPega ? 1 : 0);

                                Parameter pRole = inst.LookupParameter("IntersectionRole");
                                if (pRole != null && !pRole.IsReadOnly) pRole.Set(brickRole.ToString());
                            }

                            // 3. Generar Mortero Centralizado (DirectShape) desde cero por Juntas
                            if (window.VerPega && pegaFt > 0 && wallPlacedItems.Count > 0)
                            {
                                try
                                {
                                    XYZ normal = dir.CrossProduct(XYZ.BasisZ);

                                    double reviteFt = window.ReviteCm / 30.48;
                                    double desfaseFt = window.DesfaseCm / 30.48;
                                    double originalDepth = brick.WidthCm / 30.48;
                                    double depth = originalDepth - (reviteFt * 2);
                                    if (depth <= 0.005) depth = 0.005;

                                    double translationDist = (-originalDepth / 2.0) + reviteFt + desfaseFt;

                                    List<GeometryObject> mortarSolids = new List<GeometryObject>();

                                    // Agrupar ladrillos por hilada (ZIndex)
                                    var rowGroups = wallPlacedItems.GroupBy(i => i.ZIndex).OrderBy(g => g.Key).ToList();

                                    for (int r = 0; r < rowGroups.Count; r++)
                                    {
                                        var rowItems = rowGroups[r].OrderBy(i => i.PosX_Ft).ToList();
                                        if (rowItems.Count == 0) continue;

                                        double rowZMin = rowItems.Min(i => i.PosZ_Ft);
                                        double rowZMax = rowItems.Max(i => i.PosZ_Ft + i.Height_Ft);

                                        // A) Junta Horizontal (Bed Joint) por ENCIMA de la hilada (salvo la última)
                                        if (r < rowGroups.Count - 1)
                                        {
                                            double hZMin = rowZMax;
                                            double hZMax = rowZMax + pegaFt;
                                            double hXMin = rowItems.Min(i => i.PosX_Ft);
                                            double hXMax = rowItems.Max(i => i.PosX_Ft + i.Length_Ft);

                                            Solid? hSolid = CreateMortarBoxSolid(startPoint, dir, normal, hXMin, hXMax, hZMin, hZMax, depth, translationDist);
                                            if (hSolid != null && hSolid.Volume > 0)
                                            {
                                                mortarSolids.Add(hSolid);
                                            }
                                        }

                                        // B) Juntas Verticales (Head Joints) ENTRE ladrillos de la misma hilada
                                        for (int i = 0; i < rowItems.Count - 1; i++)
                                        {
                                            var cur = rowItems[i];
                                            var nxt = rowItems[i + 1];

                                            double vXMin = cur.PosX_Ft + cur.Length_Ft;
                                            double vXMax = nxt.PosX_Ft;

                                            if (vXMax > vXMin + 0.0001)
                                            {
                                                double vZMin = cur.PosZ_Ft;
                                                double vZMax = cur.PosZ_Ft + cur.Height_Ft;

                                                Solid? vSolid = CreateMortarBoxSolid(startPoint, dir, normal, vXMin, vXMax, vZMin, vZMax, depth, translationDist);
                                                if (vSolid != null && vSolid.Volume > 0)
                                                {
                                                    mortarSolids.Add(vSolid);
                                                }
                                            }
                                        }

                                        // C) Junta Vertical Inicial (si PosX_Ft > 0)
                                        double firstPosX = rowItems[0].PosX_Ft;
                                        if (firstPosX > 0.001)
                                        {
                                            double vXMin = Math.Max(0, firstPosX - pegaFt);
                                            double vXMax = firstPosX;
                                            double vZMin = rowItems[0].PosZ_Ft;
                                            double vZMax = rowItems[0].PosZ_Ft + rowItems[0].Height_Ft;

                                            Solid? vStartSolid = CreateMortarBoxSolid(startPoint, dir, normal, vXMin, vXMax, vZMin, vZMax, depth, translationDist);
                                            if (vStartSolid != null && vStartSolid.Volume > 0)
                                            {
                                                mortarSolids.Add(vStartSolid);
                                            }
                                        }

                                        // D) Junta Vertical Final (si el último ladrillo no llega al final del muro)
                                        var lastBrick = rowItems[rowItems.Count - 1];
                                        double lastPosXEnd = lastBrick.PosX_Ft + lastBrick.Length_Ft;
                                        if (lastPosXEnd < totalLength - 0.001)
                                        {
                                            double vXMin = lastPosXEnd;
                                            double vXMax = Math.Min(totalLength, lastPosXEnd + pegaFt);
                                            double vZMin = lastBrick.PosZ_Ft;
                                            double vZMax = lastBrick.PosZ_Ft + lastBrick.Height_Ft;

                                            Solid? vEndSolid = CreateMortarBoxSolid(startPoint, dir, normal, vXMin, vXMax, vZMin, vZMax, depth, translationDist);
                                            if (vEndSolid != null && vEndSolid.Volume > 0)
                                            {
                                                mortarSolids.Add(vEndSolid);
                                            }
                                        }
                                    }

                                    // Si se generaron sólidos de pega válidos, instanciar el DirectShape directo
                                    if (mortarSolids.Count > 0)
                                    {
                                        ElementId catId = new ElementId(BuiltInCategory.OST_GenericModel);
                                        
                                        DirectShape ds = DirectShape.CreateElement(doc, catId);
                                        ds.Name = $"Pega (Mortero) {window.PegaCm:0.0}cm";
                                        ds.SetShape(mortarSolids);

                                        if (window.PegaMaterial != null)
                                        {
                                            try
                                            {
                                                Parameter pMatInst = ds.get_Parameter(BuiltInParameter.STRUCTURAL_MATERIAL_PARAM)
                                                                   ?? ds.get_Parameter(BuiltInParameter.MATERIAL_ID_PARAM);
                                                if (pMatInst != null && !pMatInst.IsReadOnly)
                                                {
                                                    pMatInst.Set(window.PegaMaterial.Id);
                                                }
                                            }
                                            catch { }
                                        }

                                        if (window.PegaCosto > 0)
                                        {
                                            try
                                            {
                                                Parameter pCost = ds.get_Parameter(BuiltInParameter.ALL_MODEL_COST);
                                                if (pCost != null && !pCost.IsReadOnly) pCost.Set(window.PegaCosto);
                                            }
                                            catch { }
                                        }

                                        if (!string.IsNullOrWhiteSpace(window.PegaDescripcion))
                                        {
                                            try
                                            {
                                                Parameter pDesc = ds.get_Parameter(BuiltInParameter.ALL_MODEL_DESCRIPTION);
                                                if (pDesc != null && !pDesc.IsReadOnly) pDesc.Set(window.PegaDescripcion);
                                            }
                                            catch { }
                                        }
                                    }
                                }
                                catch (Exception ex)
                                {
                                    Autodesk.Revit.UI.TaskDialog.Show("Advertencia Pega", $"No se pudo generar la pega 3D en DirectShape: {ex.GetType().Name} - {ex.Message}\n\n{ex.StackTrace}");
                                }
                            }

                            // 4. Eliminar el muro original
                            doc.Delete(wall.Id);
                            wallsProcessed++;
                        }

                    tx.Commit();

                    Autodesk.Revit.UI.TaskDialog.Show("Conversión Exitosa", 
                        $"Se eliminaron {wallsProcessed} muros.\n\n" +
                        $"Preparado para instanciar:\n" +
                        $"- Ladrillo: {brick.Name} ({brick.LengthCm}x{brick.WidthCm}x{brick.HeightCm} cm)\n" +
                        $"- Pega: {window.PegaCm} cm\n" +
                        $"- Revite: {window.ReviteCm} cm\n\n" +
                        $"Revisa las familias cargadas en el proyecto.");

                    return Result.Succeeded;
                }
            }
            catch (Exception ex)
            {
                message = ex.Message;
                return Result.Failed;
            }
        }

        private static Solid? CreateMortarBoxSolid(XYZ startPoint, XYZ dir, XYZ normal, double xMin, double xMax, double zMin, double zMax, double depth, double translationDist)
        {
            if (xMax <= xMin + 0.0001 || zMax <= zMin + 0.0001) return null;

            try
            {
                XYZ p1 = startPoint + dir * xMin + XYZ.BasisZ * zMin;
                XYZ p2 = startPoint + dir * xMax + XYZ.BasisZ * zMin;
                XYZ p3 = startPoint + dir * xMax + XYZ.BasisZ * zMax;
                XYZ p4 = startPoint + dir * xMin + XYZ.BasisZ * zMax;

                CurveLoop loop = new CurveLoop();
                loop.Append(Line.CreateBound(p1, p2));
                loop.Append(Line.CreateBound(p2, p3));
                loop.Append(Line.CreateBound(p3, p4));
                loop.Append(Line.CreateBound(p4, p1));

                List<XYZ> pts = new List<XYZ> { p1, p2, p3, p4 };
                XYZ sumCross = (p2 - p1).CrossProduct(p3 - p1) + (p3 - p1).CrossProduct(p4 - p1);
                if (sumCross.DotProduct(normal) < 0)
                {
                    loop.Flip();
                }

                Solid rawSolid = GeometryCreationUtilities.CreateExtrusionGeometry(new List<CurveLoop> { loop }, normal, depth);
                if (rawSolid == null) return null;

                Transform trans = Transform.CreateTranslation(normal * translationDist);
                return SolidUtils.CreateTransformed(rawSolid, trans);
            }
            catch
            {
                return null;
            }
        }

        private double GetSymbolParameterValue(FamilySymbol symbol, string[] possibleNames)
        {
            foreach (string name in possibleNames)
            {
                Parameter p = symbol.LookupParameter(name);
                if (p != null && p.HasValue)
                {
                    return p.AsDouble();
                }
            }
            return 0;
        }

        private static bool IsLoopCounterClockwise(CurveLoop loop, XYZ normal)
        {
            List<XYZ> points = new List<XYZ>();
            foreach (Curve curve in loop)
            {
                IList<XYZ> tess = curve.Tessellate();
                for (int i = 0; i < tess.Count - 1; i++)
                {
                    points.Add(tess[i]);
                }
            }
            if (points.Count < 3) return true;

            XYZ v0 = points[0];
            XYZ sumCross = XYZ.Zero;
            for (int i = 1; i < points.Count - 1; i++)
            {
                XYZ vec1 = points[i] - v0;
                XYZ vec2 = points[i + 1] - v0;
                sumCross += vec1.CrossProduct(vec2);
            }
            return sumCross.DotProduct(normal) > 0;
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
