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
                    double hVal = 0;
                    Parameter hp = firstWall.get_Parameter(BuiltInParameter.WALL_USER_HEIGHT_PARAM);
                    if (hp != null && hp.AsDouble() > 0) hVal = hp.AsDouble();
                    
                    BoundingBoxXYZ wallBBox = firstWall.get_BoundingBox(null);
                    if (wallBBox != null)
                    {
                        double bboxHeight = wallBBox.Max.Z - wallBBox.Min.Z;
                        if (bboxHeight > hVal) hVal = bboxHeight;
                    }
                    if (hVal > 0) firstWallHeight = hVal;
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

                // Buscar o Cargar las familias Pega.rfa y PegaV.rfa para instanciación paramétrica
                FamilySymbol? pegaSymbol = null;
                FamilySymbol? pegaVSymbol = null;

                FilteredElementCollector pegaCollector = new FilteredElementCollector(doc);
                pegaCollector.OfClass(typeof(Family));
                List<Family> loadedFamilies = pegaCollector.Cast<Family>().ToList();

                Family? pegaFamily = loadedFamilies.FirstOrDefault(f => f.Name.Equals("Pega", StringComparison.OrdinalIgnoreCase) || f.Name.Equals("PegaH", StringComparison.OrdinalIgnoreCase));
                Family? pegaVFamily = loadedFamilies.FirstOrDefault(f => f.Name.Equals("PegaV", StringComparison.OrdinalIgnoreCase) || f.Name.StartsWith("PegaV", StringComparison.OrdinalIgnoreCase) || f.Name.Equals("PegaVert", StringComparison.OrdinalIgnoreCase));

                string addinDir = System.IO.Path.GetDirectoryName(System.Reflection.Assembly.GetExecutingAssembly().Location) ?? "";

                if (pegaFamily == null)
                {
                    string pegaPath = System.IO.Path.Combine(addinDir, "Pega.rfa");
                    if (!System.IO.File.Exists(pegaPath))
                    {
                        pegaPath = System.IO.Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData), @"Autodesk\Revit\Addins\2027\Pega.rfa");
                    }
                    if (!System.IO.File.Exists(pegaPath))
                    {
                        pegaPath = @"C:\Users\Alejandro\Documents\GitHub\reviewPlugIn\Pega.rfa";
                    }

                    if (System.IO.File.Exists(pegaPath))
                    {
                        using (Transaction tLoad = new Transaction(doc, "Cargar Pega.rfa"))
                        {
                            tLoad.Start();
                            doc.LoadFamily(pegaPath, out pegaFamily);
                            tLoad.Commit();
                        }
                    }
                }

                if (pegaVFamily == null)
                {
                    string pegaVPath = System.IO.Path.Combine(addinDir, "PegaV.rfa");
                    if (!System.IO.File.Exists(pegaVPath))
                    {
                        pegaVPath = System.IO.Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData), @"Autodesk\Revit\Addins\2027\PegaV.rfa");
                    }
                    if (!System.IO.File.Exists(pegaVPath))
                    {
                        pegaVPath = @"C:\Users\Alejandro\Documents\GitHub\reviewPlugIn\PegaV.rfa";
                    }

                    if (System.IO.File.Exists(pegaVPath))
                    {
                        using (Transaction tLoad = new Transaction(doc, "Cargar PegaV.rfa"))
                        {
                            tLoad.Start();
                            doc.LoadFamily(pegaVPath, out pegaVFamily);
                            tLoad.Commit();
                        }
                    }
                }

                // Cargar familia de vacio de dovela ARCA_DOBELA VOID.rfa
                FamilySymbol? dobelaVoidSymbol = null;
                Family? dobelaVoidFamily = new FilteredElementCollector(doc)
                    .OfClass(typeof(Family))
                    .Cast<Family>()
                    .FirstOrDefault(f => f.Name.IndexOf("ARCA_DOBELA VOID", StringComparison.OrdinalIgnoreCase) >= 0 ||
                                         f.Name.IndexOf("DOBELA VOID", StringComparison.OrdinalIgnoreCase) >= 0);

                if (dobelaVoidFamily == null)
                {
                    string addinFolder = System.IO.Path.GetDirectoryName(System.Reflection.Assembly.GetExecutingAssembly().Location) ?? "";
                    string dobelaVoidPath = System.IO.Path.Combine(addinFolder, "ARCA_DOBELA VOID.rfa");
                    if (!System.IO.File.Exists(dobelaVoidPath))
                    {
                        dobelaVoidPath = System.IO.Path.Combine(System.IO.Path.GetDirectoryName(addinFolder) ?? "", "ARCA_DOBELA VOID.rfa");
                    }

                    if (System.IO.File.Exists(dobelaVoidPath))
                    {
                        using (Transaction tLoad = new Transaction(doc, "Cargar ARCA_DOBELA VOID.rfa"))
                        {
                            tLoad.Start();
                            doc.LoadFamily(dobelaVoidPath, out dobelaVoidFamily);
                            tLoad.Commit();
                        }
                    }
                }

                if (pegaFamily != null)
                {
                    ISet<ElementId> pegaSymbolIds = pegaFamily.GetFamilySymbolIds();
                    if (pegaSymbolIds.Count > 0)
                    {
                        pegaSymbol = doc.GetElement(pegaSymbolIds.First()) as FamilySymbol;
                    }
                }

                if (pegaVFamily != null)
                {
                    ISet<ElementId> pegaVSymbolIds = pegaVFamily.GetFamilySymbolIds();
                    if (pegaVSymbolIds.Count > 0)
                    {
                        pegaVSymbol = doc.GetElement(pegaVSymbolIds.First()) as FamilySymbol;
                    }
                }
                if (pegaVSymbol == null) pegaVSymbol = pegaSymbol;

                if (dobelaVoidFamily != null)
                {
                    ISet<ElementId> voidSymbolIds = dobelaVoidFamily.GetFamilySymbolIds();
                    if (voidSymbolIds.Count > 0)
                    {
                        dobelaVoidSymbol = doc.GetElement(voidSymbolIds.First()) as FamilySymbol;
                    }
                }

                using (Transaction tx = new Transaction(doc, "Convertir Muros a Ladrillos"))
                {
                    tx.Start();
                    if (!brickSymbol.IsActive) brickSymbol.Activate();
                    if (pegaSymbol != null && !pegaSymbol.IsActive) pegaSymbol.Activate();
                    if (pegaVSymbol != null && !pegaVSymbol.IsActive) pegaVSymbol.Activate();
                    if (dobelaVoidSymbol != null && !dobelaVoidSymbol.IsActive) dobelaVoidSymbol.Activate();

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

                        // Obtener Altura real del muro (considerando restricciones de nivel superior, offsets y geometry bbox)
                        double totalHeight = 0;
                        Parameter heightParam = wall.get_Parameter(BuiltInParameter.WALL_USER_HEIGHT_PARAM);
                        if (heightParam != null && heightParam.AsDouble() > 0) totalHeight = heightParam.AsDouble();

                        BoundingBoxXYZ wallBBoxCur = wall.get_BoundingBox(null);
                        if (wallBBoxCur != null)
                        {
                            double bboxH = wallBBoxCur.Max.Z - wallBBoxCur.Min.Z;
                            if (bboxH > totalHeight) totalHeight = bboxH;
                        }
                        if (totalHeight <= 0) totalHeight = 10.0;

                        double brickLFt = brick.LengthCm / 30.48;
                        double brickHFt = brick.HeightCm / 30.48;
                        double pegaFt = window.PegaCm / 30.48;

                        double angle = XYZ.BasisX.AngleTo(dir);
                        if (dir.Y < 0) angle = 2 * Math.PI - angle;

                        XYZ extNormal = wall.Orientation;
                        int locLine = wall.get_Parameter(BuiltInParameter.WALL_KEY_REF_PARAM)?.AsInteger() ?? 0;
                        // Alineación estricta al Eje Central del Muro Original
                        double centerShift = (locLine == 2 || locLine == 4) ? -(wallWidth / 2.0) : ((locLine == 3 || locLine == 5) ? (wallWidth / 2.0) : 0);
                        XYZ adjustedStartPoint = startPoint + extNormal * centerShift;

                        XYZ localY = XYZ.BasisZ.CrossProduct(dir);
                        XYZ normal = dir.CrossProduct(XYZ.BasisZ);
                        double wallAngle = (localY.DotProduct(extNormal) < 0) ? (angle + Math.PI) : angle;
                        double brickWidthFt = brick.WidthCm / 30.48;

                        Level? wallLevel = doc.GetElement(wall.LevelId) as Level;
                        List<BrickLayoutItem> wallPlacedItems = new List<BrickLayoutItem>();

                        double canchaStartHFt = window.CanchaStartHeightCm / 30.48;
                        double canchaHFt = (window.CanchaAltoCm > 0 ? window.CanchaAltoCm : 12.0) / 30.48;
                        double canchaDepthFt = (window.CanchaProfCm > 0 ? window.CanchaProfCm : 8.0) / 30.48;

                        double dovelaWidthFt = (window.DovelaWidthCm > 0 ? window.DovelaWidthCm : 2.5) / 30.48;
                        double dovelaDepthFt = (window.DovelaDepthCm > 0 ? window.DovelaDepthCm : 12.5) / 30.48;
                        double dovelaStartDepthFt = (window.DovelaStartDepthCm > 0 ? window.DovelaStartDepthCm : 17.5) / 30.48;

                        List<BrickLayoutItem> wallLayoutItems = GenerateLayoutForWall(totalLength, totalHeight, brick, pattern, window.PegaCm, window.VerPega);

                        // Sincronizar configuraciones interactivas del usuario (Canchado, Dovelas, etc.) desde la ventana
                        if (window.LayoutItems != null && window.LayoutItems.Count > 0)
                        {
                            foreach (var item in wallLayoutItems)
                            {
                                var match = window.LayoutItems.FirstOrDefault(w => w.ZIndex == item.ZIndex && w.XIndex == item.XIndex);
                                if (match == null)
                                {
                                    match = window.LayoutItems.FirstOrDefault(w => Math.Abs(w.PosZ_Ft - item.PosZ_Ft) < 0.05 && Math.Abs(w.PosX_Ft - item.PosX_Ft) < 0.05);
                                }

                                if (match != null)
                                {
                                    item.IsCanchado = match.IsCanchado;
                                    item.HasDovela1 = match.HasDovela1;
                                    item.HasDovela2 = match.HasDovela2;
                                    item.CutLeft_Ft = match.CutLeft_Ft;
                                    item.CutRight_Ft = match.CutRight_Ft;
                                    item.CutTop_Ft = match.CutTop_Ft;
                                    if (match.Length_Ft > 0) item.Length_Ft = match.Length_Ft;
                                    if (match.Height_Ft > 0) item.Height_Ft = match.Height_Ft;
                                }
                            }
                        }

                        // Instanciar ladrillos por hilada aplicando alternancia de traba en esquinas L y T
                        foreach (var item in wallLayoutItems)
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
                                    bool isMainWall = (node.WallA.Id == wall.Id);

                                    if (!isMainWall)
                                    {
                                        int myEndIndex = node.EndIndexB;
                                        if (myEndIndex == 0 && itemPosX < brickLFt)
                                        {
                                            if (isOddRow)
                                            {
                                                itemPosX -= (otherWidth / 2.0);
                                                itemLength += (otherWidth / 2.0);
                                                brickRole = WallIntersectionRole.TieKey;
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
                                            if (isOddRow)
                                            {
                                                itemLength += (otherWidth / 2.0);
                                                brickRole = WallIntersectionRole.TieKey;
                                            }
                                            else
                                            {
                                                itemLength -= (otherWidth / 2.0);
                                                brickRole = WallIntersectionRole.CornerAbutment;
                                            }
                                        }
                                    }
                                    else
                                    {
                                        double nodeDistOnMain = (node.Point - startPoint).DotProduct(dir);
                                        double secWidth = node.WallB.Width;
                                        double gapStart = nodeDistOnMain - (secWidth / 2.0);
                                        double gapEnd = nodeDistOnMain + (secWidth / 2.0);

                                        if (isOddRow)
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

                            if (skipBrick) continue;

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
                                HasDovela1 = item.HasDovela1,
                                HasDovela2 = item.HasDovela2,
                                IsCanchado = item.IsCanchado,
                                Role = brickRole
                            };
                            wallPlacedItems.Add(placedItem);

                            XYZ location = adjustedStartPoint - extNormal * (brickWidthFt / 2.0) + dir * itemPosX + XYZ.BasisZ * item.PosZ_Ft;
                            FamilyInstance inst = doc.Create.NewFamilyInstance(location, brickSymbol, wallLevel, Autodesk.Revit.DB.Structure.StructuralType.NonStructural);
                            
                            Line axis = Line.CreateBound(location, location + XYZ.BasisZ);
                            ElementTransformUtils.RotateElement(doc, inst.Id, axis, wallAngle);

                                // 1. Asignar primero todos los parámetros dimensionales y recortes
                                double lateralCut = brickLFt - itemLength;
                                if (lateralCut > 0.001)
                                {
                                    SetInstanceParameterOnly(inst, new[] { "Cut Lat", "CutLat", "Cut Lat (default)", "CutLateral", "Cut Lateral", "RECORTE LATERAL", "Recorte Lateral", "RecorteLateral", "CutLat_Depth", "CutLatDepth", "RECORTE LATERAL (default)" }, lateralCut);
                                    SetInstanceParameterOnly(inst, new[] { "Cut Lat Flag", "Cut Lat", "CutLat", "CutLateral", "Cut Lateral" }, 1);
                                }

                                // Recorte superior (solo si la pieza es más baja que la altura completa)
                                double topCut = brickHFt - item.Height_Ft;
                                if (topCut > 0.001)
                                {
                                    SetInstanceParameterOnly(inst, new[] { "Cut Top", "CutTop", "Cut Top (default)", "CutTopCut", "CutSuperior", "Cut Superior" }, 1);
                                    SetInstanceParameterOnly(inst, new[] { "RECORTE SUPERIOR", "Recorte Superior", "RecorteSuperior", "CutTop_Depth", "CutTopDepth", "Cut Superior", "RECORTE SUPERIOR (default)" }, topCut);
                                }

                                if (item.IsCanchado)
                                {
                                    SetInstanceParameterOnly(inst, new[] { "Cut Cancha", "CutCancha", "Cut Cancha (default)", "Cancha", "Canchado", "CanchaCut", "Cut_Cancha" }, 1);
                                    SetInstanceParameterOnly(inst, new[] { "Cancha_Start_Height", "Cancha Start Height", "CanchaStartHeight", "Start Height Cancha", "StartHeightCancha", "Cancha_Start_Height (default)", "Inicio Cancha", "InicioCancha" }, canchaStartHFt);
                                    SetInstanceParameterOnly(inst, new[] { "Cancha Height", "CanchaHeight", "Alto Cancha", "AltoCancha", "Height Cancha", "Cancha_Height", "Cancha Height (default)" }, canchaHFt);
                                    SetInstanceParameterOnly(inst, new[] { "Cancha Depth", "CanchaDepth", "Profundidad Cancha", "ProfundidadCancha", "Cancha_Depth", "Cancha Depth (default)" }, canchaDepthFt);
                                }
                                else
                                {
                                    SetInstanceParameterOnly(inst, new[] { "Cut Cancha", "CutCancha", "Cut Cancha (default)", "Cancha", "Canchado", "CanchaCut", "Cut_Cancha" }, 0);
                                }

                                if (item.HasDovela1 || item.HasDovela2)
                                {
                                    SetInstanceParameterOnly(inst, new[] { "Dovela_Width", "Dovela Width", "DovelaWidth", "Width Dovela", "WidthDovela", "Ancho Dovela", "AnchoDovela", "Dovela_Width (default)", "Dobela_Width", "Dobela Width" }, dovelaWidthFt);
                                    
                                    // Cálculo exacto de Dobela_Depth y Dobela_Offset_Start (D = depth, T = thickness)
                                    double brickDepthParam = itemLength; // Largo/Depth del ladrillo en pies
                                    double thicknessParam = 0;

                                    Parameter? pThick = inst.Symbol?.LookupParameter("Thickness") ?? inst.LookupParameter("Thickness");
                                    if (pThick != null && pThick.AsDouble() > 0)
                                    {
                                        thicknessParam = pThick.AsDouble();
                                    }
                                    else
                                    {
                                        thicknessParam = (window.DovelaWidthCm > 0 ? window.DovelaWidthCm : 1.5) / 30.48;
                                    }

                                    // Dobela_Depth es constante para ambas mitades: (Depth - 3 * Thickness) / 2
                                    dovelaDepthFt = (brickDepthParam - (thicknessParam * 3.0)) / 2.0;
                                    if (dovelaDepthFt <= 0) dovelaDepthFt = (brickDepthParam / 2.0) - thicknessParam;

                                    double offsetStartFt = thicknessParam;

                                    if (item.HasDovela1 && !item.HasDovela2)
                                    {
                                        // Mitad Izquierda únicamente: Start = T
                                        offsetStartFt = thicknessParam;
                                    }
                                    else if (!item.HasDovela1 && item.HasDovela2)
                                    {
                                        // Mitad Derecha únicamente: Start = (Depth / 2) + (Thickness / 2)
                                        offsetStartFt = (brickDepthParam / 2.0) + (thicknessParam / 2.0);
                                    }
                                    else
                                    {
                                        // Ambas Mitades: Start = T
                                        offsetStartFt = thicknessParam;
                                    }

                                    // Protección estricta de límites
                                    if (offsetStartFt < 0.001) offsetStartFt = thicknessParam > 0 ? thicknessParam : 0.01;

                                    SetInstanceParameterOnly(inst, new[] { "Dovela_Depth", "Dovela Depth", "DovelaDepth", "Depth Dovela", "DepthDovela", "Largo Dovela", "LargoDovela", "Dovela_Depth (default)", "Dobela_Depth", "Dobela Depth" }, dovelaDepthFt);
                                    SetInstanceParameterOnly(inst, new[] { "Dobela_Offset_Start", "Dobela Offset Start", "DobelaOffsetStart", "Dovela_Offset_Start", "Dovela Offset Start", "DovelaOffsetStart", "Dovela_Start_Depth", "Dovela Start Depth", "DovelaStartDepth", "Start Depth Dovela", "StartDepthDovela", "Inicio Dovela", "InicioDovela", "Dovela_Start_Depth (default)" }, offsetStartFt);

                                    SetInstanceParameterOnly(inst, new[] { "Cut Dobela 1", "CutDobela1", "Cut Dobela1", "Cut Dobela 1 (default)", "Cut Dovela 1", "CutDovela1", "Dovela 1", "Dovela1", "Dobela 1", "Dobela1" }, item.HasDovela1 ? 1 : 0);
                                    SetInstanceParameterOnly(inst, new[] { "Cut Dobela 2", "CutDobela2", "Cut Dobela2", "Cut Dobela 2 (default)", "Cut Dovela 2", "CutDovela2", "Dovela 2", "Dovela2", "Dobela 2", "Dobela2" }, item.HasDovela2 ? 1 : 0);
                                    SetInstanceParameterOnly(inst, new[] { "Cut Dobela", "CutDobela", "Cut Dobela (default)", "CutDovela", "Cut Dovela", "Dovela", "Dobela" }, 1);
                                }
                                else
                                {
                                    SetInstanceParameterOnly(inst, new[] { "Cut Dobela 1", "CutDobela1", "Cut Dobela1", "Cut Dobela 1 (default)", "Cut Dovela 1", "CutDovela1", "Dovela 1", "Dovela1", "Dobela 1", "Dobela1" }, 0);
                                    SetInstanceParameterOnly(inst, new[] { "Cut Dobela 2", "CutDobela2", "Cut Dobela2", "Cut Dobela 2 (default)", "Cut Dovela 2", "CutDovela2", "Dovela 2", "Dovela2", "Dobela 2", "Dobela2" }, 0);
                                    SetInstanceParameterOnly(inst, new[] { "Cut Dobela", "CutDobela", "Cut Dobela (default)", "CutDovela", "Cut Dovela", "Dovela", "Dobela" }, 0);
                                }

                                // Aplicar Pega (si la familia aún tiene el parámetro)
                                Parameter pGrosorP = inst.LookupParameter("GrosorPega");
                                if (pGrosorP != null && !pGrosorP.IsReadOnly) pGrosorP.Set(pegaFt);
                                
                                Parameter pVerP = inst.LookupParameter("VerPega");
                                if (pVerP != null && !pVerP.IsReadOnly) pVerP.Set(window.VerPega ? 1 : 0);

                                Parameter pRole = inst.LookupParameter("IntersectionRole");
                                if (pRole != null && !pRole.IsReadOnly) pRole.Set(brickRole.ToString());

                            }

                            // 3. Generar Mortero Centralizado (Familia Paramétrica Pega.rfa o DirectShape Fallback)
                            if (window.VerPega && pegaFt > 0 && wallPlacedItems.Count > 0)
                            {
                                try
                                {
                                    double reviteFt = window.ReviteCm / 30.48;
                                    double desfaseFt = window.DesfaseCm / 30.48;
                                    double originalDepth = brick.WidthCm / 30.48;
                                    double depthFt = originalDepth - (reviteFt * 2);
                                    if (depthFt <= 0.005) depthFt = 0.005;

                                    double translationDist = (-originalDepth / 2.0) + reviteFt + desfaseFt;

                                    double dovelaRadius = (dovelaDepthFt / 2.0);
                                    if (dovelaRadius <= 0) dovelaRadius = (10.0 / 2.0) / 30.48;

                                    double dovelaVoidWidthFt = dovelaWidthFt;
                                    double dovelaVoidLengthFt = dovelaDepthFt;

                                    var dovelaXCenters = new List<double>();
                                    foreach (var pi in wallPlacedItems)
                                    {
                                        if (pi.HasDovela1)
                                        {
                                            dovelaXCenters.Add(pi.PosX_Ft + dovelaStartDepthFt);
                                        }
                                        if (pi.HasDovela2)
                                        {
                                            dovelaXCenters.Add(pi.PosX_Ft + pi.Length_Ft - dovelaStartDepthFt);
                                        }
                                    }
                                    dovelaXCenters = dovelaXCenters.Distinct().ToList();

                                    var rowGroups = wallPlacedItems.GroupBy(i => i.ZIndex).OrderBy(g => g.Key).ToList();
                                    if (pegaSymbol != null)
                                    {
                                        // -------------------------------------------------------------
                                        // MODO A: FAMILIA PARAMÉTRICA Pega.rfa (Centrada en eje de muro)
                                        // -------------------------------------------------------------

                                        // A1. Pega Perimetral Base Inferior (Sin cancha en PegaH)
                                        if (rowGroups.Count > 0)
                                        {
                                            var firstRowItems = rowGroups[0].OrderBy(i => i.PosX_Ft).ToList();
                                            PlaceHorizontalPegaJointWithDovelaStrips(doc, pegaSymbol, wallLevel, adjustedStartPoint, dir, extNormal, wallAngle, 0, totalLength, pegaFt, depthFt, originalDepth, reviteFt, desfaseFt, window.PegaMaterial, window.PegaCosto, window.PegaDescripcion, firstRowItems, window, brickSymbol);
                                        }

                                        // A2. Juntas Horizontales y Verticales por Hilada
                                        for (int r = 0; r < rowGroups.Count; r++)
                                        {
                                            var rowItems = rowGroups[r].OrderBy(i => i.PosX_Ft).ToList();
                                            if (rowItems.Count == 0) continue;

                                            double rowZMin = rowItems[0].PosZ_Ft;
                                            double rowZMax = rowItems.Max(i => i.PosZ_Ft + i.Height_Ft);
                                            double rowHeight = rowItems[0].Height_Ft; // Altura real de los ladrillos de esta hilada

                                            // Junta Horizontal sobre la hilada (Sin cancha en PegaH)
                                            PlaceHorizontalPegaJointWithDovelaStrips(doc, pegaSymbol, wallLevel, adjustedStartPoint, dir, extNormal, wallAngle, rowZMax, totalLength, pegaFt, depthFt, originalDepth, reviteFt, desfaseFt, window.PegaMaterial, window.PegaCosto, window.PegaDescripcion, rowItems, window, brickSymbol);

                                            // C1. Pega Vertical Perimetral Inicial de la Hilada (X = 0)
                                            if (pegaFt > 0.0001)
                                            {
                                                bool firstBrickCancha = rowItems[0].IsCanchado;
                                                PlaceVerticalPegaFamilyInstance(doc, pegaVSymbol ?? pegaSymbol, wallLevel, adjustedStartPoint, dir, extNormal, wallAngle, 0, rowZMin, pegaFt, brickHFt, rowHeight, depthFt, originalDepth, reviteFt, desfaseFt, window.PegaMaterial, window.PegaCosto, window.PegaDescripcion, firstBrickCancha, canchaStartHFt, canchaHFt, canchaDepthFt);
                                            }

                                            // C2. Juntas Verticales Intermedias (entre ladrillos de la hilada)
                                            for (int i = 0; i < rowItems.Count - 1; i++)
                                            {
                                                var cur = rowItems[i];
                                                var nxt = rowItems[i + 1];

                                                double vXMin = cur.PosX_Ft + cur.Length_Ft;
                                                double vXMax = nxt.PosX_Ft;

                                                if (vXMax > vXMin + 0.0001)
                                                {
                                                    double vGap = vXMax - vXMin;
                                                    bool isCanchado = cur.IsCanchado || nxt.IsCanchado;

                                                    PlaceVerticalPegaFamilyInstance(doc, pegaVSymbol ?? pegaSymbol, wallLevel, adjustedStartPoint, dir, extNormal, wallAngle, vXMin, cur.PosZ_Ft, vGap, brickHFt, cur.Height_Ft, depthFt, originalDepth, reviteFt, desfaseFt, window.PegaMaterial, window.PegaCosto, window.PegaDescripcion, isCanchado, canchaStartHFt, canchaHFt, canchaDepthFt);
                                                }
                                            }

                                            // C3. Pega Vertical Perimetral Final de la Hilada (X = totalLength - pegaFt)
                                            if (pegaFt > 0.0001)
                                            {
                                                bool lastBrickCancha = rowItems[rowItems.Count - 1].IsCanchado;
                                                PlaceVerticalPegaFamilyInstance(doc, pegaVSymbol ?? pegaSymbol, wallLevel, adjustedStartPoint, dir, extNormal, wallAngle, totalLength - pegaFt, rowZMin, pegaFt, brickHFt, rowHeight, depthFt, originalDepth, reviteFt, desfaseFt, window.PegaMaterial, window.PegaCosto, window.PegaDescripcion, lastBrickCancha, canchaStartHFt, canchaHFt, canchaDepthFt);
                                            }
                                        }
                                    }
                                    else
                                    {
                                        // -------------------------------------------------------------
                                        // MODO B: DIRECTSHAPE FALLBACK
                                        // -------------------------------------------------------------
                                        List<GeometryObject> mortarSolids = new List<GeometryObject>();

                                        if (rowGroups.Count > 0)
                                        {
                                            AddMortarJointWithDovelaCutouts(mortarSolids, adjustedStartPoint, dir, normal, 0, totalLength, 0, pegaFt, depthFt, translationDist, dovelaXCenters, dovelaRadius);
                                        }

                                        for (int r = 0; r < rowGroups.Count; r++)
                                        {
                                            var rowItems = rowGroups[r].OrderBy(i => i.PosX_Ft).ToList();
                                            if (rowItems.Count == 0) continue;

                                            double rowZMax = rowItems.Max(i => i.PosZ_Ft + i.Height_Ft);

                                            AddMortarJointWithDovelaCutouts(mortarSolids, adjustedStartPoint, dir, normal, 0, totalLength, rowZMax, rowZMax + pegaFt, depthFt, translationDist, dovelaXCenters, dovelaRadius);

                                            double firstPosX = rowItems[0].PosX_Ft;
                                            if (firstPosX > 0.001)
                                            {
                                                AddVerticalJointWithCanchaCutout(mortarSolids, adjustedStartPoint, dir, normal, 0, firstPosX, rowItems[0].PosZ_Ft, rowZMax, depthFt, translationDist, rowItems, canchaHFt);
                                            }

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

                                                    AddVerticalJointWithCanchaCutout(mortarSolids, adjustedStartPoint, dir, normal, vXMin, vXMax, vZMin, vZMax, depthFt, translationDist, new List<BrickLayoutItem> { cur, nxt }, canchaHFt);
                                                }
                                            }

                                            var lastBrick = rowItems[rowItems.Count - 1];
                                            double lastPosXEnd = lastBrick.PosX_Ft + lastBrick.Length_Ft;
                                            if (lastPosXEnd < totalLength - 0.001)
                                            {
                                                AddVerticalJointWithCanchaCutout(mortarSolids, adjustedStartPoint, dir, normal, lastPosXEnd, totalLength, lastBrick.PosZ_Ft, rowZMax, depthFt, translationDist, new List<BrickLayoutItem> { lastBrick }, canchaHFt);
                                            }
                                        }

                                        if (mortarSolids.Count > 0)
                                        {
                                            ElementId catId = new ElementId(BuiltInCategory.OST_GenericModel);
                                            DirectShape ds = DirectShape.CreateElement(doc, catId);
                                            ds.Name = $"Pega (Mortero) {window.PegaCm:0.0}cm";
                                            ds.SetShape(mortarSolids);

                                            double totalVolumeFt3 = mortarSolids.OfType<Solid>().Sum(s => s.Volume);
                                            double totalVolumeM3 = totalVolumeFt3 * 0.0283168;
                                            double totalAreaM2 = (totalLength * totalHeight) * 0.092903;
                                            double costoTotal = window.PegaCosto > 0 ? (window.PegaCosto * totalVolumeM3) : 0;

                                            if (window.PegaMaterial != null)
                                            {
                                                try
                                                {
                                                    Parameter pMatInst = ds.get_Parameter(BuiltInParameter.STRUCTURAL_MATERIAL_PARAM)
                                                                       ?? ds.get_Parameter(BuiltInParameter.MATERIAL_ID_PARAM);
                                                    if (pMatInst != null && !pMatInst.IsReadOnly) pMatInst.Set(window.PegaMaterial.Id);
                                                }
                                                catch { }
                                            }

                                            try
                                            {
                                                Parameter pComments = ds.get_Parameter(BuiltInParameter.ALL_MODEL_INSTANCE_COMMENTS);
                                                if (pComments != null && !pComments.IsReadOnly) 
                                                    pComments.Set($"PEGA-MORTERO-3D | Pega: {window.PegaCm}cm | Vol: {totalVolumeM3:F3} m³ | Área Muro: {totalAreaM2:F2} m²");

                                                if (costoTotal > 0)
                                                {
                                                    Parameter pCost = ds.get_Parameter(BuiltInParameter.ALL_MODEL_COST);
                                                    if (pCost != null && !pCost.IsReadOnly) pCost.Set(costoTotal);
                                                }

                                                if (!string.IsNullOrWhiteSpace(window.PegaDescripcion))
                                                {
                                                    Parameter pDesc = ds.get_Parameter(BuiltInParameter.ALL_MODEL_DESCRIPTION);
                                                    if (pDesc != null && !pDesc.IsReadOnly) pDesc.Set(window.PegaDescripcion);
                                                }
                                            }
                                            catch { }
                                        }
                                    }
                                }
                                catch (Exception ex)
                                {
                                    Autodesk.Revit.UI.TaskDialog.Show("Advertencia Pega", $"No se pudo generar la pega 3D: {ex.GetType().Name} - {ex.Message}\n\n{ex.StackTrace}");
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

        private static void AddMortarJointWithDovelaCutouts(
            List<GeometryObject> mortarSolids,
            XYZ startPoint, XYZ dir, XYZ normal,
            double xMin, double xMax, double zMin, double zMax,
            double depth, double translationDist,
            List<double> dovelaXCenters, double dovelaRadius)
        {
            var activeDovelas = dovelaXCenters.Where(x => x > xMin + dovelaRadius && x < xMax - dovelaRadius)
                                              .OrderBy(x => x)
                                              .ToList();

            if (activeDovelas.Count == 0)
            {
                Solid? s = CreateMortarBoxSolid(startPoint, dir, normal, xMin, xMax, zMin, zMax, depth, translationDist);
                if (s != null && s.Volume > 0) mortarSolids.Add(s);
                return;
            }

            double currentX = xMin;
            foreach (double dovX in activeDovelas)
            {
                double segXMax = dovX - dovelaRadius;
                if (segXMax > currentX + 0.0001)
                {
                    Solid? s = CreateMortarBoxSolid(startPoint, dir, normal, currentX, segXMax, zMin, zMax, depth, translationDist);
                    if (s != null && s.Volume > 0) mortarSolids.Add(s);
                }
                currentX = dovX + dovelaRadius;
            }

            if (xMax > currentX + 0.0001)
            {
                Solid? s = CreateMortarBoxSolid(startPoint, dir, normal, currentX, xMax, zMin, zMax, depth, translationDist);
                if (s != null && s.Volume > 0) mortarSolids.Add(s);
            }
        }

        private static void AddVerticalJointWithCanchaCutout(
            List<GeometryObject> mortarSolids,
            XYZ startPoint, XYZ dir, XYZ normal,
            double xMin, double xMax, double zMin, double zMax,
            double depth, double translationDist,
            List<BrickLayoutItem> nearbyItems, double canchaH)
        {
            var canchadoItem = nearbyItems.FirstOrDefault(i => i.IsCanchado);
            if (canchadoItem == null)
            {
                Solid? s = CreateMortarBoxSolid(startPoint, dir, normal, xMin, xMax, zMin, zMax, depth, translationDist);
                if (s != null && s.Volume > 0) mortarSolids.Add(s);
                return;
            }

            double zCenter = canchadoItem.PosZ_Ft + (canchadoItem.Height_Ft / 2.0);
            double canchaZMin = zCenter - (canchaH / 2.0);
            double canchaZMax = zCenter + (canchaH / 2.0);

            if (canchaZMin > zMin + 0.0001)
            {
                Solid? sLow = CreateMortarBoxSolid(startPoint, dir, normal, xMin, xMax, zMin, Math.Min(zMax, canchaZMin), depth, translationDist);
                if (sLow != null && sLow.Volume > 0) mortarSolids.Add(sLow);
            }

            if (zMax > canchaZMax + 0.0001)
            {
                Solid? sHigh = CreateMortarBoxSolid(startPoint, dir, normal, xMin, xMax, Math.Max(zMin, canchaZMax), zMax, depth, translationDist);
                if (sHigh != null && sHigh.Volume > 0) mortarSolids.Add(sHigh);
            }
        }

        private struct DovelaInterval
        {
            public double StartX;
            public double EndX;
            public double DovelaDepthFt;
            public double BrickThicknessFt;
        }

        private static void PlaceHorizontalPegaJointWithDovelaStrips(
            Document doc,
            FamilySymbol pegaSymbol,
            Level? level,
            XYZ startPoint,
            XYZ dir,
            XYZ normal,
            double angle,
            double rowZ,
            double totalLength,
            double pegaFt,
            double depthFt,
            double originalDepth,
            double reviteFt,
            double desfaseFt,
            Material? material,
            double cost,
            string description,
            List<BrickLayoutItem> rowItems,
            ConvertirMurosWindow window,
            FamilySymbol? brickSymbol)
        {
            if (totalLength <= 0.0005 || pegaFt <= 0.0005) return;

            List<DovelaInterval> intervals = new List<DovelaInterval>();

            double defaultThicknessFt = (window.DovelaWidthCm > 0 ? window.DovelaWidthCm : 1.5) / 30.48;
            Parameter? pThick = brickSymbol?.LookupParameter("Thickness") ?? brickSymbol?.LookupParameter("Pared");
            if (pThick != null && pThick.AsDouble() > 0)
            {
                defaultThicknessFt = pThick.AsDouble();
            }

            if (rowItems != null && rowItems.Count > 0)
            {
                foreach (var item in rowItems)
                {
                    if (!item.HasDovela1 && !item.HasDovela2) continue;

                    double brickL = item.Length_Ft;
                    double thicknessFt = defaultThicknessFt;

                    double dovelaDepthFt = (brickL - (thicknessFt * 3.0)) / 2.0;
                    if (dovelaDepthFt <= 0) dovelaDepthFt = (brickL / 2.0) - thicknessFt;

                    if (item.HasDovela1)
                    {
                        double start1 = item.PosX_Ft + thicknessFt;
                        double end1 = start1 + dovelaDepthFt;

                        start1 = Math.Max(0, Math.Min(totalLength, start1));
                        end1 = Math.Max(0, Math.Min(totalLength, end1));

                        if (end1 > start1 + 0.0005)
                        {
                            intervals.Add(new DovelaInterval
                            {
                                StartX = start1,
                                EndX = end1,
                                DovelaDepthFt = dovelaDepthFt,
                                BrickThicknessFt = thicknessFt
                            });
                        }
                    }

                    if (item.HasDovela2)
                    {
                        double start2 = item.PosX_Ft + (brickL / 2.0) + (thicknessFt / 2.0);
                        double end2 = start2 + dovelaDepthFt;

                        start2 = Math.Max(0, Math.Min(totalLength, start2));
                        end2 = Math.Max(0, Math.Min(totalLength, end2));

                        if (end2 > start2 + 0.0005)
                        {
                            intervals.Add(new DovelaInterval
                            {
                                StartX = start2,
                                EndX = end2,
                                DovelaDepthFt = dovelaDepthFt,
                                BrickThicknessFt = thicknessFt
                            });
                        }
                    }
                }
            }

            intervals = intervals.OrderBy(i => i.StartX).ToList();

            double currentX = 0;

            foreach (var dov in intervals)
            {
                // Tramo Sólido previo
                if (dov.StartX > currentX + 0.0005)
                {
                    double segLen = dov.StartX - currentX;
                    PlacePegaFamilyInstance(doc, pegaSymbol, level, startPoint, dir, normal, angle, currentX, rowZ, segLen, pegaFt, depthFt, originalDepth, reviteFt, desfaseFt, material, cost, description, false, 0, 0, 0, false);
                }

                // Tramo con Dobela: Generar 2 Pegas Paralelas (Frontal y Posterior)
                if (dov.EndX > dov.StartX + 0.0005)
                {
                    double dobLen = dov.EndX - dov.StartX;
                    double tFt = dov.BrickThicknessFt;

                    double yOffsetFront = -(originalDepth / 2.0) + reviteFt;
                    double yOffsetBack = (originalDepth / 2.0) - reviteFt - tFt;

                    PlaceShiftedPegaFamilyInstance(doc, pegaSymbol, level, startPoint, dir, normal, angle, dov.StartX, rowZ, yOffsetFront, dobLen, pegaFt, tFt, originalDepth, reviteFt, material, cost, description);
                    PlaceShiftedPegaFamilyInstance(doc, pegaSymbol, level, startPoint, dir, normal, angle, dov.StartX, rowZ, yOffsetBack, dobLen, pegaFt, tFt, originalDepth, reviteFt, material, cost, description);
                }

                currentX = Math.Max(currentX, dov.EndX);
            }

            // Tramo Sólido Final
            if (totalLength > currentX + 0.0005)
            {
                double segLen = totalLength - currentX;
                PlacePegaFamilyInstance(doc, pegaSymbol, level, startPoint, dir, normal, angle, currentX, rowZ, segLen, pegaFt, depthFt, originalDepth, reviteFt, desfaseFt, material, cost, description, false, 0, 0, 0, false);
            }
        }

        private static FamilyInstance? PlaceShiftedPegaFamilyInstance(
            Document doc,
            FamilySymbol pegaSymbol,
            Level? level,
            XYZ startPoint,
            XYZ dir,
            XYZ normal,
            double angle,
            double posX,
            double posZ,
            double offsetY_Ft,
            double lengthFt,
            double pegaFt,
            double stripWidthFt,
            double originalWidthFt,
            double reviteFt,
            Material? material,
            double cost,
            string description)
        {
            if (lengthFt <= 0.0005 || pegaFt <= 0.0005 || stripWidthFt <= 0.0005) return null;

            XYZ location = startPoint + normal * offsetY_Ft + dir * posX + XYZ.BasisZ * posZ;
            FamilyInstance inst = doc.Create.NewFamilyInstance(location, pegaSymbol, level, Autodesk.Revit.DB.Structure.StructuralType.NonStructural);

            Line axisZ = Line.CreateBound(location, location + XYZ.BasisZ);
            ElementTransformUtils.RotateElement(doc, inst.Id, axisZ, angle);

            SetInstanceParameterOnly(inst, new[] { "GrosorPega", "Grosor Pega", "Grosor", "Height", "Alto", "Height (default)", "Alto (default)" }, pegaFt);
            SetInstanceParameterOnly(inst, new[] { "Depth", "Largo", "Length", "Longitud", "Depth (default)", "Largo (default)" }, lengthFt);
            SetInstanceParameterOnly(inst, new[] { "Width", "Ancho", "Width (default)", "Ancho (default)", "Thickness", "Thickness (default)", "Profundidad", "Profundidad (default)", "GrosorMortero", "NetWidth" }, stripWidthFt);
            SetInstanceParameterOnly(inst, new[] { "Cut Dobela", "CutDobela", "Cut Dobela (default)", "CutDovela", "Cut Dovela", "Dovela", "Tunnel", "Tunel" }, 1);

            Parameter pCommentsH = inst.get_Parameter(BuiltInParameter.ALL_MODEL_INSTANCE_COMMENTS);
            if (pCommentsH != null && !pCommentsH.IsReadOnly) pCommentsH.Set("PEGA-HORIZ-DOBELA");

            if (material != null)
            {
                SetElementMaterial(inst, material);
            }

            if (cost > 0)
            {
                Parameter pCost = inst.get_Parameter(BuiltInParameter.ALL_MODEL_COST);
                if (pCost != null && !pCost.IsReadOnly) pCost.Set(cost);
            }

            if (!string.IsNullOrWhiteSpace(description))
            {
                Parameter pDesc = inst.get_Parameter(BuiltInParameter.ALL_MODEL_DESCRIPTION);
                if (pDesc != null && !pDesc.IsReadOnly) pDesc.Set(description);
            }

            return inst;
        }

        private static FamilyInstance? PlacePegaFamilyInstance(
            Document doc,
            FamilySymbol pegaSymbol,
            Level? level,
            XYZ startPoint,
            XYZ dir,
            XYZ normal,
            double angle,
            double posX,
            double posZ,
            double lengthFt,
            double pegaFt,
            double depthFt,
            double originalWidthFt,
            double reviteFt,
            double desfaseFt,
            Material? material,
            double cost,
            string description,
            bool cutCancha,
            double canchaStartHFt,
            double canchaHFt,
            double canchaDepthFt,
            bool cutDobela,
            FamilySymbol? dobelaVoidSymbol = null,
            List<double>? dovelaXCenters = null,
            double dovelaVoidWidthFt = 0,
            double dovelaVoidLengthFt = 0)
        {
            if (lengthFt <= 0.0005 || pegaFt <= 0.0005) return null;

            XYZ location = startPoint - normal * (depthFt / 2.0) + dir * posX + XYZ.BasisZ * posZ;
            FamilyInstance inst = doc.Create.NewFamilyInstance(location, pegaSymbol, level, Autodesk.Revit.DB.Structure.StructuralType.NonStructural);

            Line axisZ = Line.CreateBound(location, location + XYZ.BasisZ);
            ElementTransformUtils.RotateElement(doc, inst.Id, axisZ, angle);

            // Set parameters FIRST so bounding box reflects correct physical dimensions
            SetPegaParameters(inst, pegaFt, lengthFt, depthFt, originalWidthFt, reviteFt, cutCancha, canchaStartHFt, canchaHFt, canchaDepthFt, cutDobela);



            // Aplicar vacio de dovela ARCA_DOBELA VOID solo en PegaH en las posiciones de dovela
            if (dobelaVoidSymbol != null && dovelaXCenters != null && dovelaXCenters.Count > 0)
            {
                foreach (double dovX in dovelaXCenters)
                {
                    if (dovX >= posX - 0.001 && dovX <= posX + lengthFt + 0.001)
                    {
                        PlaceDovelaVoidInPegaH(doc, dobelaVoidSymbol, inst, level, startPoint, dir, angle, dovX, posZ, pegaFt, dovelaVoidWidthFt, dovelaVoidLengthFt);
                    }
                }
            }

            Parameter pCommentsH = inst.get_Parameter(BuiltInParameter.ALL_MODEL_INSTANCE_COMMENTS);
            if (pCommentsH != null && !pCommentsH.IsReadOnly) pCommentsH.Set("PEGA-HORIZ");

            if (material != null)
            {
                SetElementMaterial(inst, material);
            }

            if (cost > 0)
            {
                Parameter pCost = inst.get_Parameter(BuiltInParameter.ALL_MODEL_COST);
                if (pCost != null && !pCost.IsReadOnly) pCost.Set(cost);
            }

            if (!string.IsNullOrWhiteSpace(description))
            {
                Parameter pDesc = inst.get_Parameter(BuiltInParameter.ALL_MODEL_DESCRIPTION);
                if (pDesc != null && !pDesc.IsReadOnly) pDesc.Set(description);
            }

            return inst;
        }

        private static void PlaceDovelaVoidInPegaH(
            Document doc,
            FamilySymbol dobelaVoidSymbol,
            FamilyInstance pegaHInst,
            Level? level,
            XYZ startPoint,
            XYZ dir,
            double angle,
            double dovX,
            double posZ,
            double pegaFt,
            double voidWidthFt,
            double voidLengthFt)
        {
            if (dobelaVoidSymbol == null || pegaHInst == null) return;

            XYZ location = startPoint + dir * dovX + XYZ.BasisZ * posZ;
            FamilyInstance voidInst = doc.Create.NewFamilyInstance(location, dobelaVoidSymbol, level, Autodesk.Revit.DB.Structure.StructuralType.NonStructural);

            Line axisZ = Line.CreateBound(location, location + XYZ.BasisZ);
            ElementTransformUtils.RotateElement(doc, voidInst.Id, axisZ, angle);

            // Asignar dimensiones por instancia a ARCA_DOBELA VOID
            SetInstanceParameterOnly(voidInst, new[] { "Height", "Alto", "Depth", "Thickness", "Grosor", "GrosorPega", "Height (default)", "Alto (default)" }, pegaFt);
            SetInstanceParameterOnly(voidInst, new[] { "Length", "Largo", "Longitud", "Length (default)", "Largo (default)" }, voidLengthFt);
            SetInstanceParameterOnly(voidInst, new[] { "Width", "Ancho", "Profundidad", "Width (default)", "Ancho (default)" }, voidWidthFt);

            doc.Regenerate();

            BoundingBoxXYZ bbox = voidInst.get_BoundingBox(null);
            if (bbox != null)
            {
                XYZ bboxCenter = (bbox.Min + bbox.Max) / 2.0;
                XYZ targetCenter = startPoint + dir * dovX + XYZ.BasisZ * (posZ + pegaFt / 2.0);
                XYZ shift = targetCenter - bboxCenter;
                if (shift.GetLength() > 0.0001)
                {
                    ElementTransformUtils.MoveElement(doc, voidInst.Id, shift);
                }
            }

            try
            {
                InstanceVoidCutUtils.AddInstanceVoidCut(doc, pegaHInst, voidInst);
            }
            catch { }
        }

        private static FamilyInstance? PlaceVerticalPegaFamilyInstance(
            Document doc,
            FamilySymbol pegaSymbol,
            Level? level,
            XYZ startPoint,
            XYZ dir,
            XYZ normal,
            double angle,
            double posX,
            double posZ,
            double pegaFt,
            double fullBrickHFt,
            double actualCutHeightFt,
            double depthFt,
            double originalWidthFt,
            double reviteFt,
            double desfaseFt,
            Material? material,
            double cost,
            string description,
            bool cutCancha,
            double canchaStartHFt,
            double canchaHFt,
            double canchaDepthFt)
        {
            if (fullBrickHFt <= 0.0005 || pegaFt <= 0.0005) return null;

            // 1. Punto de Inserción Base (X centrado en la junta vertical, Z en el borde inferior posZ)
            XYZ location = startPoint - normal * (depthFt / 2.0) + dir * posX + XYZ.BasisZ * posZ;

            // 2. Instanciar la familia PegaV en la ubicación base
            FamilyInstance inst = doc.Create.NewFamilyInstance(location, pegaSymbol, level, Autodesk.Revit.DB.Structure.StructuralType.NonStructural);

            // 3. Rotación en el plano XY (dirección del muro)
            Line axisZ = Line.CreateBound(location, location + XYZ.BasisZ);
            ElementTransformUtils.RotateElement(doc, inst.Id, axisZ, angle);

            // 4. Asignar todos los parámetros de dimensiones (Depth = fullBrickHFt, Cut Top + RECORTE SUPERIOR por instancia)
            SetVerticalPegaParameters(inst, fullBrickHFt, actualCutHeightFt, pegaFt, depthFt, originalWidthFt, reviteFt, cutCancha, canchaStartHFt, canchaHFt, canchaDepthFt);
            doc.Regenerate();

            // 5. Asignación directa en coordenadas exactas del eje del muro (sin desplazar por AABB)

            Parameter pCommentsV = inst.get_Parameter(BuiltInParameter.ALL_MODEL_INSTANCE_COMMENTS);
            if (pCommentsV != null && !pCommentsV.IsReadOnly) pCommentsV.Set("PEGA-VERT");

            if (material != null)
            {
                SetElementMaterial(inst, material);
            }

            if (cost > 0)
            {
                Parameter pCost = inst.get_Parameter(BuiltInParameter.ALL_MODEL_COST);
                if (pCost != null && !pCost.IsReadOnly) pCost.Set(cost);
            }

            if (!string.IsNullOrWhiteSpace(description))
            {
                Parameter pDesc = inst.get_Parameter(BuiltInParameter.ALL_MODEL_DESCRIPTION);
                if (pDesc != null && !pDesc.IsReadOnly) pDesc.Set(description);
            }

            return inst;
        }

        private static bool SetPegaParameters(Element inst, double mortarGapFt, double lengthAlongAxisFt, double netDepthFt, double originalWidthFt, double reviteFt, bool cutCancha, double canchaStartHFt, double canchaHFt, double canchaDepthFt, bool cutDobela)
        {
            // 1. Height / Alto / GrosorPega = Grosor de la Pega Horizontal entre Hiladas (mortarGapFt) - SOLO INSTANCIA
            SetInstanceParameterOnly(inst, new[] { "GrosorPega", "Grosor Pega", "Grosor", "Height", "Alto", "Height (default)", "Alto (default)" }, mortarGapFt);

            // 2. Depth / Largo = Longitud Horizontal del Tramo (lengthAlongAxisFt) - SOLO INSTANCIA
            SetInstanceParameterOnly(inst, new[] { "Depth", "Largo", "Length", "Longitud", "Depth (default)", "Largo (default)" }, lengthAlongAxisFt);

            // 3. Width y Thickness / Profundidad y Ancho = Profundidad NETA del mortero descontando el Revite por ambos lados - SOLO INSTANCIA
            SetInstanceParameterOnly(inst, new[] { "Width", "Ancho", "Width (default)", "Ancho (default)", "Thickness", "Thickness (default)", "Profundidad", "Profundidad (default)", "GrosorMortero", "NetWidth" }, netDepthFt);

            // 4. Ancho Original (si existe en la familia)
            SetInstanceParameterOnly(inst, new[] { "WidthOriginal", "AnchoOriginal", "OriginalWidth" }, originalWidthFt);

            // 5. Revite (si la familia tiene parámetro explícito "Revite")
            if (reviteFt > 0)
            {
                SetInstanceParameterOnly(inst, new[] { "Revite", "GrosorRevite", "ReviteDepth", "Revite (cm)", "Bisel", "BiselDepth" }, reviteFt);
            }

            // 6. Canchado (Cancha) para PegaH
            if (cutCancha)
            {
                SetInstanceParameterOnly(inst, new[] { "Cut Cancha", "CutCancha", "Cut Cancha (default)", "Cancha", "Canchado", "CanchaCut" }, 1);
                SetInstanceParameterOnly(inst, new[] { "Cancha_Start_Height", "Cancha Start Height", "CanchaStartHeight", "Start Height Cancha", "StartHeightCancha", "Cancha_Start_Height (default)" }, canchaStartHFt);
                SetInstanceParameterOnly(inst, new[] { "Cancha Height", "CanchaHeight", "Alto Cancha", "AltoCancha", "Height Cancha", "Cancha_Height", "Cancha Height (default)" }, canchaHFt);
                SetInstanceParameterOnly(inst, new[] { "Cancha Depth", "CanchaDepth", "Profundidad Cancha", "ProfundidadCancha", "Cancha_Depth", "Cancha Depth (default)" }, canchaDepthFt);
            }
            else
            {
                SetInstanceParameterOnly(inst, new[] { "Cut Cancha", "CutCancha", "Cut Cancha (default)", "Cancha", "Canchado", "CanchaCut" }, 0);
            }

            // 7. Dovela (Dovelas / Túneles) para PegaH
            if (cutDobela)
            {
                SetInstanceParameterOnly(inst, new[] { "Cut Dobela", "CutDobela", "Cut Dobela (default)", "CutDovela", "Cut Dovela", "Dovela", "Tunnel", "Tunel" }, 1);
            }
            else
            {
                SetInstanceParameterOnly(inst, new[] { "Cut Dobela", "CutDobela", "Cut Dobela (default)", "CutDovela", "Cut Dovela", "Dovela", "Tunnel", "Tunel" }, 0);
            }

            return true;
        }

        private static bool SetVerticalPegaParameters(Element inst, double fullBrickHFt, double actualCutHeightFt, double mortarGapFt, double netDepthFt, double originalWidthFt, double reviteFt, bool cutCancha, double canchaStartHFt, double canchaHFt, double canchaDepthFt)
        {
            // 1. Depth / Largo = Altura COMPLETA del Ladrillo Estándar (fullBrickHFt) - SOLO INSTANCIA
            SetInstanceParameterOnly(inst, new[] { "Depth", "Largo", "Length", "Longitud", "Depth (default)", "Largo (default)" }, fullBrickHFt);

            // 2. GrosorPega / Height / Alto / Grosor = Espesor de la Junta Vertical entre Ladrillos (mortarGapFt) - SOLO INSTANCIA
            SetInstanceParameterOnly(inst, new[] { "Height", "Alto", "GrosorPega", "Grosor Pega", "Grosor", "Espesor", "GrosorJunta", "Height (default)", "Alto (default)" }, mortarGapFt);

            // 3. Width / Ancho / Thickness = Profundidad NETA del mortero descontando el Revite por ambos lados - SOLO INSTANCIA
            SetInstanceParameterOnly(inst, new[] { "Width", "Ancho", "Width (default)", "Ancho (default)", "Thickness", "Thickness (default)", "Profundidad", "Profundidad (default)", "GrosorMortero", "NetWidth" }, netDepthFt);

            // 4. Ancho Original (si existe en la familia)
            SetInstanceParameterOnly(inst, new[] { "WidthOriginal", "AnchoOriginal", "OriginalWidth" }, originalWidthFt);

            // 5. Revite (si la familia tiene parámetro explícito "Revite")
            if (reviteFt > 0)
            {
                SetInstanceParameterOnly(inst, new[] { "Revite", "GrosorRevite", "ReviteDepth", "Revite (cm)", "Bisel", "BiselDepth" }, reviteFt);
            }

            // 6. Recorte Superior (Cut Top / RECORTE SUPERIOR) por Instancia
            double cutTopAmount = fullBrickHFt - actualCutHeightFt;
            if (cutTopAmount > 0.001)
            {
                cutTopAmount = Math.Min(cutTopAmount, fullBrickHFt - 0.01);
                SetInstanceParameterOnly(inst, new[] { "Cut Top", "CutTop", "Cut Top (default)", "CutTopCut", "CutSuperior" }, 1);
                SetInstanceParameterOnly(inst, new[] { "RECORTE SUPERIOR", "Recorte Superior", "RecorteSuperior", "CutTop_Depth", "CutTopDepth", "Cut Superior", "RECORTE SUPERIOR (default)" }, cutTopAmount);
            }
            else
            {
                SetInstanceParameterOnly(inst, new[] { "Cut Top", "CutTop", "Cut Top (default)", "CutTopCut", "CutSuperior" }, 0);
            }

            // 7. Canchado (Cancha) para PegaV
            if (cutCancha)
            {
                SetInstanceParameterOnly(inst, new[] { "Cut Cancha", "CutCancha", "Cut Cancha (default)", "Cancha", "Canchado", "CanchaCut" }, 1);
                SetInstanceParameterOnly(inst, new[] { "Cancha_Start_Height", "Cancha Start Height", "CanchaStartHeight", "Start Height Cancha", "StartHeightCancha", "Cancha_Start_Height (default)" }, canchaStartHFt);
                SetInstanceParameterOnly(inst, new[] { "Cancha Height", "CanchaHeight", "Alto Cancha", "AltoCancha", "Height Cancha", "Cancha_Height", "Cancha Height (default)" }, canchaHFt);
                SetInstanceParameterOnly(inst, new[] { "Cancha Depth", "CanchaDepth", "Profundidad Cancha", "ProfundidadCancha", "Cancha_Depth", "Cancha Depth (default)" }, canchaDepthFt);
            }
            else
            {
                SetInstanceParameterOnly(inst, new[] { "Cut Cancha", "CutCancha", "Cut Cancha (default)", "Cancha", "Canchado", "CanchaCut" }, 0);
            }

            return true;
        }

        private static bool SetInstanceParameterOnly(Element elem, string[] possibleNames, double val)
        {
            bool setAny = false;
            foreach (string name in possibleNames)
            {
                string cleanName = name.Replace("(default)", "").Replace("(predeterminado)", "").Trim();

                // 1. Lookup directo en Instancia
                Parameter? p = elem.LookupParameter(name) ?? elem.LookupParameter(cleanName);
                if (p != null && !p.IsReadOnly)
                {
                    p.Set(val);
                    setAny = true;
                }

                // 2. Recorrer la colección completa de parámetros de la Instancia únicamente (con limpieza de (default))
                foreach (Parameter pElem in elem.Parameters)
                {
                    if (!pElem.IsReadOnly && pElem.Definition != null)
                    {
                        string cleanDefName = pElem.Definition.Name.Replace("(default)", "").Replace("(predeterminado)", "").Trim();
                        if (cleanDefName.Equals(cleanName, StringComparison.OrdinalIgnoreCase))
                        {
                            pElem.Set(val);
                            setAny = true;
                        }
                    }
                }
            }
            return setAny;
        }

        private static bool SetInstanceParameterOnly(Element elem, string[] possibleNames, int val)
        {
            bool setAny = false;
            foreach (string name in possibleNames)
            {
                string cleanName = name.Replace("(default)", "").Replace("(predeterminado)", "").Trim();

                Parameter? p = elem.LookupParameter(name) ?? elem.LookupParameter(cleanName);
                if (p != null && !p.IsReadOnly)
                {
                    p.Set(val);
                    setAny = true;
                }

                foreach (Parameter pElem in elem.Parameters)
                {
                    if (!pElem.IsReadOnly && pElem.Definition != null)
                    {
                        string cleanDefName = pElem.Definition.Name.Replace("(default)", "").Replace("(predeterminado)", "").Trim();
                        if (cleanDefName.Equals(cleanName, StringComparison.OrdinalIgnoreCase))
                        {
                            pElem.Set(val);
                            setAny = true;
                        }
                    }
                }
            }
            return setAny;
        }

        private static bool SetParameterValue(Element elem, string[] possibleNames, double val)
        {
            bool setAny = false;
            foreach (string name in possibleNames)
            {
                // 1. LookupDirecto en Instancia
                Parameter? p = elem.LookupParameter(name);
                if (p != null && !p.IsReadOnly)
                {
                    p.Set(val);
                    setAny = true;
                }

                // 2. LookupDirecto en Tipo (Symbol)
                if (elem is FamilyInstance fi && fi.Symbol != null)
                {
                    Parameter? pSym = fi.Symbol.LookupParameter(name);
                    if (pSym != null && !pSym.IsReadOnly)
                    {
                        pSym.Set(val);
                        setAny = true;
                    }
                }

                // 3. Recorrer la colección completa de parámetros de la Instancia
                foreach (Parameter pElem in elem.Parameters)
                {
                    if (!pElem.IsReadOnly && pElem.Definition != null && pElem.Definition.Name.Equals(name, StringComparison.OrdinalIgnoreCase))
                    {
                        pElem.Set(val);
                        setAny = true;
                    }
                }

                // 4. Recorrer la colección completa de parámetros del Tipo (Symbol)
                if (elem is FamilyInstance fiAll && fiAll.Symbol != null)
                {
                    foreach (Parameter pSym in fiAll.Symbol.Parameters)
                    {
                        if (!pSym.IsReadOnly && pSym.Definition != null && pSym.Definition.Name.Equals(name, StringComparison.OrdinalIgnoreCase))
                        {
                            pSym.Set(val);
                            setAny = true;
                        }
                    }
                }
            }
            return setAny;
        }

        private static bool SetParameterValue(Element elem, string[] possibleNames, int val)
        {
            bool setAny = false;
            foreach (string name in possibleNames)
            {
                Parameter? p = elem.LookupParameter(name);
                if ((p == null || p.IsReadOnly) && elem is FamilyInstance fi && fi.Symbol != null)
                {
                    p = fi.Symbol.LookupParameter(name);
                }

                if (p != null && !p.IsReadOnly)
                {
                    p.Set(val);
                    setAny = true;
                }
            }
            return setAny;
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

        private static bool SetElementMaterial(Element elem, Material? material)
        {
            if (material == null || elem == null) return false;

            string[] possibleMatNames = new[] { "Mat", "Material", "Mat.", "Materiales", "MatLadrillo", "MatPega", "Material Ladrillo", "Material Pega" };
            bool setAny = false;

            // 1. Buscar en parámetros de Instancia por nombre directo
            foreach (string name in possibleMatNames)
            {
                Parameter p = elem.LookupParameter(name);
                if (p != null && !p.IsReadOnly && p.StorageType == StorageType.ElementId)
                {
                    p.Set(material.Id);
                    setAny = true;
                }
            }

            // 2. Buscar en parámetros de Tipo (Symbol) por nombre directo
            if (elem is FamilyInstance fi && fi.Symbol != null)
            {
                foreach (string name in possibleMatNames)
                {
                    Parameter pSym = fi.Symbol.LookupParameter(name);
                    if (pSym != null && !pSym.IsReadOnly && pSym.StorageType == StorageType.ElementId)
                    {
                        pSym.Set(material.Id);
                        setAny = true;
                    }
                }
            }

            // 3. Recorrer la colección completa de parámetros de Instancia
            foreach (Parameter pElem in elem.Parameters)
            {
                if (!pElem.IsReadOnly && pElem.StorageType == StorageType.ElementId && pElem.Definition != null)
                {
                    string defName = pElem.Definition.Name;
                    if (possibleMatNames.Any(n => n.Equals(defName, StringComparison.OrdinalIgnoreCase)))
                    {
                        pElem.Set(material.Id);
                        setAny = true;
                    }
                }
            }

            // 4. Recorrer la colección completa de parámetros del Tipo (Symbol)
            if (elem is FamilyInstance fiAll && fiAll.Symbol != null)
            {
                foreach (Parameter pSym in fiAll.Symbol.Parameters)
                {
                    if (!pSym.IsReadOnly && pSym.StorageType == StorageType.ElementId && pSym.Definition != null)
                    {
                        string defName = pSym.Definition.Name;
                        if (possibleMatNames.Any(n => n.Equals(defName, StringComparison.OrdinalIgnoreCase)))
                        {
                            pSym.Set(material.Id);
                            setAny = true;
                        }
                    }
                }
            }

            // 5. Fallback a parámetros integrados de Revit (STRUCTURAL_MATERIAL_PARAM, MATERIAL_ID_PARAM)
            Parameter pBuiltIn = elem.get_Parameter(BuiltInParameter.STRUCTURAL_MATERIAL_PARAM)
                              ?? elem.get_Parameter(BuiltInParameter.MATERIAL_ID_PARAM);
            if (pBuiltIn != null && !pBuiltIn.IsReadOnly)
            {
                pBuiltIn.Set(material.Id);
                setAny = true;
            }

            if (elem is FamilyInstance fiBuiltIn && fiBuiltIn.Symbol != null)
            {
                Parameter pBuiltInSym = fiBuiltIn.Symbol.get_Parameter(BuiltInParameter.STRUCTURAL_MATERIAL_PARAM)
                                     ?? fiBuiltIn.Symbol.get_Parameter(BuiltInParameter.MATERIAL_ID_PARAM);
                if (pBuiltInSym != null && !pBuiltInSym.IsReadOnly)
                {
                    pBuiltInSym.Set(material.Id);
                    setAny = true;
                }
            }

            return setAny;
        }

        private static List<BrickLayoutItem> GenerateLayoutForWall(
            double wallLengthFt,
            double wallHeightFt,
            BrickType brick,
            string pattern,
            double pegaCm,
            bool verPega)
        {
            List<BrickLayoutItem> items = new List<BrickLayoutItem>();
            double pegaFt = pegaCm / 30.48;

            double brickLFt = pattern.Contains("Soga") ? (brick.LengthCm / 30.48) : (brick.WidthCm / 30.48);
            double brickHFt = brick.HeightCm / 30.48;

            if (brickLFt <= 0 || brickHFt <= 0) return items;

            double stepX = brickLFt + pegaFt;
            double stepZ = brickHFt + pegaFt;

            int zIndex = 0;
            bool hasPega = (verPega && pegaFt > 0);

            double currentZ = hasPega ? pegaFt : 0;
            double startXOffset = hasPega ? pegaFt : 0;
            double maxWallX = hasPega ? (wallLengthFt - pegaFt) : wallLengthFt;

            while (currentZ < wallHeightFt - 0.0001)
            {
                bool isCutT = (currentZ + brickHFt > wallHeightFt + 0.001);
                double itemH = isCutT ? (wallHeightFt - currentZ) : brickHFt;
                if (itemH <= 0.001) break;

                bool isOddRow = (zIndex % 2 != 0);

                if (isOddRow && (pattern.Contains("Soga") || pattern.Contains("Tizón") || pattern.Contains("Inglés")))
                {
                    // Hilada Impar: Comienza con medio ladrillo en startXOffset para lograr traba a 1/2
                    double halfBrickLen = (brickLFt - pegaFt) / 2.0;
                    if (halfBrickLen > 0.001)
                    {
                        double firstLen = Math.Min(halfBrickLen, maxWallX - startXOffset);
                        items.Add(new BrickLayoutItem
                        {
                            PosX_Ft = startXOffset,
                            PosZ_Ft = currentZ,
                            Length_Ft = firstLen,
                            Height_Ft = itemH,
                            CutLeft_Ft = 0,
                            CutRight_Ft = (brickLFt - firstLen),
                            CutTop_Ft = isCutT ? (brickHFt - itemH) : 0,
                            ZIndex = zIndex,
                            XIndex = -1,
                            HasDovela1 = false,
                            HasDovela2 = false,
                            IsCanchado = false
                        });
                    }

                    double currentX = startXOffset + (stepX / 2.0);
                    int xIndex = 0;

                    while (currentX < maxWallX - 0.0001)
                    {
                        double remainingX = maxWallX - currentX;
                        double len = Math.Min(brickLFt, remainingX);
                        bool isCutR = (remainingX < brickLFt - 0.001);

                        items.Add(new BrickLayoutItem
                        {
                            PosX_Ft = currentX,
                            PosZ_Ft = currentZ,
                            Length_Ft = len,
                            Height_Ft = itemH,
                            CutLeft_Ft = 0,
                            CutRight_Ft = isCutR ? (brickLFt - len) : 0,
                            CutTop_Ft = isCutT ? (brickHFt - itemH) : 0,
                            ZIndex = zIndex,
                            XIndex = xIndex,
                            HasDovela1 = false,
                            HasDovela2 = false,
                            IsCanchado = false
                        });

                        currentX += stepX;
                        xIndex++;
                    }
                }
                else
                {
                    // Hilada Par: Comienza con ladrillo completo en startXOffset
                    double currentX = startXOffset;
                    int xIndex = 0;

                    while (currentX < maxWallX - 0.0001)
                    {
                        double remainingX = maxWallX - currentX;
                        double len = Math.Min(brickLFt, remainingX);
                        bool isCutR = (remainingX < brickLFt - 0.001);

                        items.Add(new BrickLayoutItem
                        {
                            PosX_Ft = currentX,
                            PosZ_Ft = currentZ,
                            Length_Ft = len,
                            Height_Ft = itemH,
                            CutLeft_Ft = 0,
                            CutRight_Ft = isCutR ? (brickLFt - len) : 0,
                            CutTop_Ft = isCutT ? (brickHFt - itemH) : 0,
                            ZIndex = zIndex,
                            XIndex = xIndex,
                            HasDovela1 = false,
                            HasDovela2 = false,
                            IsCanchado = false
                        });

                        currentX += stepX;
                        xIndex++;
                    }
                }

                currentZ += stepZ;
                zIndex++;
            }

            return items;
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
