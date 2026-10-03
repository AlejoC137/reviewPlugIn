using System;
using System.Collections.Generic;
using System.Linq;
using Autodesk.Revit.DB;

namespace RevitToSupabasePlugin
{
    public class TileGenerationConfig
    {
        public double TileLengthFt { get; set; } = (60.0 / 100.0) * 3.28084; // Largo (eje U)
        public double TileWidthFt { get; set; } = (60.0 / 100.0) * 3.28084;  // Ancho (eje V)
        public double TileThicknessFt { get; set; } = (1.0 / 100.0) * 3.28084; // Espesor
        public double JointThicknessFt { get; set; } = (3.0 / 1000.0) * 3.28084; // Espesor de junta
        public double OffsetUFt { get; set; } = 0.0;
        public double OffsetVFt { get; set; } = 0.0;
        public double RotationAngleRad { get; set; } = 0.0;
        public FloorPatternType Pattern { get; set; } = FloorPatternType.OrthogonalGrid;
    }

    public class TilePlacementConfig
    {
        public ElementId CategoryId { get; set; } = new ElementId(BuiltInCategory.OST_Floors);
        public ElementId MaterialId { get; set; } = ElementId.InvalidElementId;
        public bool MeasureElements { get; set; } = true;
        public string CommentsPrefix { get; set; } = "ENCHAPE-BALDOSA";
    }

    public class TileResult
    {
        public int ColIndex { get; set; }
        public int RowIndex { get; set; }
        public bool IsCut { get; set; }
        public CurveLoop TileLoop { get; set; } = null!;
        public Solid TileSolid { get; set; } = null!;
        public XYZ Center3D { get; set; } = XYZ.Zero;
        public XYZ Corner3D { get; set; } = XYZ.Zero;
        public List<XYZ> PolygonVertices3D { get; set; } = new List<XYZ>();
        public List<XYZ> PolygonVerticesUV { get; set; } = new List<XYZ>();
        public double AreaSqFt { get; set; }
        public double AreaSqM => AreaSqFt * 0.092903;
        public double CutLatFt { get; set; }
        public double CutTopFt { get; set; }
    }

    public static class FloorTilingService
    {
        /// <summary>
        /// 1. Extracción del Contorno Base: Obtener el PlanarFace superior del Floor anfitrión.
        /// </summary>
        public static PlanarFace? GetTopPlanarFace(Floor floor)
        {
            if (floor == null) return null;

            // Intento 1: HostObjectUtils.GetTopFaces
            try
            {
                IList<Reference> topFaceRefs = HostObjectUtils.GetTopFaces(floor);
                if (topFaceRefs != null && topFaceRefs.Count > 0)
                {
                    foreach (var r in topFaceRefs)
                    {
                        GeometryObject geomObj = floor.GetGeometryObjectFromReference(r);
                        if (geomObj is PlanarFace pf && pf.FaceNormal.Z > 0.5)
                        {
                            return pf;
                        }
                    }
                }
            }
            catch { }

            // Intento 2: Búsqueda geométrica en sólidos del elemento
            Options opt = new Options { DetailLevel = ViewDetailLevel.Fine, IncludeNonVisibleObjects = false };
            GeometryElement geomElem = floor.get_Geometry(opt);
            if (geomElem == null) return null;

            PlanarFace? bestFace = null;
            double maxZ = -1e9;

            foreach (GeometryObject obj in geomElem)
            {
                if (obj is Solid solid && solid.Faces.Size > 0)
                {
                    foreach (Face face in solid.Faces)
                    {
                        if (face is PlanarFace pf && pf.FaceNormal.Z > 0.5)
                        {
                            if (pf.Origin.Z > maxZ)
                            {
                                maxZ = pf.Origin.Z;
                                bestFace = pf;
                            }
                        }
                    }
                }
            }

            return bestFace;
        }

        /// <summary>
        /// 1. Extracción del Contorno Base: Extraer su CurveLoop exterior principal y validar que sea cerrado (curveLoop.IsOpen() == false).
        /// </summary>
        public static (CurveLoop? outerLoop, List<CurveLoop> innerLoops) ExtractBoundaryLoops(PlanarFace topFace)
        {
            if (topFace == null) return (null, new List<CurveLoop>());

            IList<CurveLoop> loops = topFace.GetEdgesAsCurveLoops();
            if (loops == null || loops.Count == 0) return (null, new List<CurveLoop>());

            CurveLoop? outerLoop = null;
            double maxArea = -1;
            int outerIndex = -1;

            XYZ normal = topFace.FaceNormal.Normalize();

            for (int i = 0; i < loops.Count; i++)
            {
                CurveLoop loop = loops[i];
                // Validación estricta de CurveLoop cerrado
                if (loop.IsOpen()) continue;

                double area = ComputeLoopArea(loop, normal);
                if (area > maxArea)
                {
                    maxArea = area;
                    outerLoop = loop;
                    outerIndex = i;
                }
            }

            if (outerLoop == null || outerLoop.IsOpen())
            {
                return (null, new List<CurveLoop>());
            }

            // Asegurar que el contorno exterior esté orientado antihorario (CCW) respecto a la normal del piso
            if (!outerLoop.IsCounterclockwise(normal))
            {
                outerLoop = ReverseCurveLoop(outerLoop);
            }

            // Procesar huecos / vanos interiores cerrados
            List<CurveLoop> innerLoops = new List<CurveLoop>();
            for (int i = 0; i < loops.Count; i++)
            {
                if (i == outerIndex) continue;
                CurveLoop inLoop = loops[i];
                if (inLoop.IsOpen()) continue;

                // Los huecos interiores deben estar en sentido horario (CW) respecto a la normal para extrusiones válidas
                if (inLoop.IsCounterclockwise(normal))
                {
                    inLoop = ReverseCurveLoop(inLoop);
                }
                innerLoops.Add(inLoop);
            }

            return (outerLoop, innerLoops);
        }

        /// <summary>
        /// 2. Sistema de Coordenadas Local (Replanteo):
        /// Definir un vector base alineado con el segmento más largo del contorno (Transform local con origen en la esquina mínima de la bounding box UV).
        /// </summary>
        public static (Transform localTransform, Curve longestSegment, double spanU, double spanV) CreateLocalCoordinateSystem(
            CurveLoop outerLoop, 
            PlanarFace topFace)
        {
            if (outerLoop == null) throw new ArgumentNullException(nameof(outerLoop));

            // A. Encontrar el segmento más largo del contorno exterior
            Curve? longestSegment = null;
            double maxLen = -1.0;

            foreach (Curve c in outerLoop)
            {
                if (c.Length > maxLen)
                {
                    maxLen = c.Length;
                    longestSegment = c;
                }
            }

            if (longestSegment == null)
            {
                throw new InvalidOperationException("No se encontraron segmentos válidos en el contorno del piso.");
            }

            // B. Definir vector base alineado con el segmento más largo
            XYZ p0 = longestSegment.GetEndPoint(0);
            XYZ p1 = longestSegment.GetEndPoint(1);
            XYZ dirU = (p1 - p0).Normalize();
            if (dirU.GetLength() < 1e-5)
            {
                dirU = XYZ.BasisX;
            }

            XYZ normal = topFace.FaceNormal.Normalize();
            XYZ dirV = normal.CrossProduct(dirU).Normalize();

            // C. Proyectar todos los vértices del contorno a UV para calcular la Bounding Box mínima
            double minU = double.MaxValue;
            double maxU = double.MinValue;
            double minV = double.MaxValue;
            double maxV = double.MinValue;

            XYZ refOrigin = p0;

            foreach (Curve c in outerLoop)
            {
                IList<XYZ> tess = c.Tessellate();
                foreach (XYZ pt in tess)
                {
                    double u = (pt - refOrigin).DotProduct(dirU);
                    double v = (pt - refOrigin).DotProduct(dirV);

                    if (u < minU) minU = u;
                    if (u > maxU) maxU = u;
                    if (v < minV) minV = v;
                    if (v > maxV) maxV = v;
                }
            }

            // D. Origen local en la esquina mínima (minU, minV) de la bounding box UV
            XYZ originLocal = refOrigin + dirU * minU + dirV * minV;

            Transform localTransform = Transform.Identity;
            localTransform.Origin = originLocal;
            localTransform.BasisX = dirU;
            localTransform.BasisY = dirV;
            localTransform.BasisZ = normal;

            double spanU = maxU - minU;
            double spanV = maxV - minV;

            return (localTransform, longestSegment, spanU, spanV);
        }

        /// <summary>
        /// 3. Malla de Despiece y 4. Recorte y Validación Geométrica:
        /// Genera la grilla de baldosas según dimensiones configuradas y recorta piezas mediante BooleanOperationsUtils.
        /// </summary>
        public static List<TileResult> GenerateFloorTiles(
            PlanarFace topFace,
            CurveLoop outerLoop,
            List<CurveLoop> innerLoops,
            Transform localTransform,
            double spanU,
            double spanV,
            TileGenerationConfig config)
        {
            List<TileResult> results = new List<TileResult>();

            if (topFace == null || outerLoop == null) return results;

            XYZ normal = topFace.FaceNormal.Normalize();
            double tileL = Math.Max(0.01, config.TileLengthFt);
            double tileW = Math.Max(0.01, config.TileWidthFt);
            double tileThick = Math.Max(0.001, config.TileThicknessFt);
            double joint = Math.Max(0.0, config.JointThicknessFt);

            double stepU = tileL + joint;
            double stepV = tileW + joint;

            // A. Crear Prisma de Recorte Extruido del Contorno del Piso
            // Se le da un margen deltaZ arriba y abajo para garantizar que las caras superior e inferior
            // de las baldosas no sean coplanares con el prisma, evitando errores de precisión en el kernel booleano.
            double deltaZ = 0.05; // ~1.5 cm de tolerancia vertical
            List<CurveLoop> floorLoops = new List<CurveLoop> { outerLoop };
            if (innerLoops != null && innerLoops.Count > 0)
            {
                floorLoops.AddRange(innerLoops);
            }

            // Desplazar loops hacia abajo para iniciar la extrusión por debajo del piso
            Transform shiftDown = Transform.CreateTranslation(-normal * deltaZ);
            List<CurveLoop> shiftedFloorLoops = new List<CurveLoop>();
            foreach (CurveLoop loop in floorLoops)
            {
                shiftedFloorLoops.Add(CurveLoop.CreateViaTransform(loop, shiftDown));
            }

            Solid? floorPrism = null;
            try
            {
                floorPrism = GeometryCreationUtilities.CreateExtrusionGeometry(
                    shiftedFloorLoops,
                    normal,
                    tileThick + (2.0 * deltaZ));
            }
            catch
            {
                return results;
            }

            if (floorPrism == null) return results;

            // B. Preparar Polígono 2D del Contorno en Coordenadas Locales UV para validación rápida
            List<XYZ> outerPolyUV = ExtractPolygonUV(outerLoop, localTransform);
            List<List<XYZ>> innerPolysUV = innerLoops != null
                ? innerLoops.Select(inLoop => ExtractPolygonUV(inLoop, localTransform)).ToList()
                : new List<List<XYZ>>();

            // C. Determinar rango de filas y columnas para cubrir toda la extensión spanU, spanV
            double rotRad = config.RotationAngleRad;
            double offU = config.OffsetUFt;
            double offV = config.OffsetVFt;

            double diag = Math.Sqrt(spanU * spanU + spanV * spanV);
            // Vectores directores rotados en el plano UV para mantener la retícula uniforme sin solapes
            double cosR = Math.Cos(rotRad);
            double sinR = Math.Sin(rotRad);
            XYZ dirU_UV = new XYZ(cosR, sinR, 0);
            XYZ dirV_UV = new XYZ(-sinR, cosR, 0);

            // Rango extendido para cubrir la bounding box del piso bajo cualquier ángulo de rotación
            int maxCells = (int)Math.Ceiling(diag / Math.Min(stepU, stepV)) + 5;
            int minCol = -maxCells;
            int numCols = maxCells;
            int minRow = -maxCells;
            int numRows = maxCells;

            double fullAreaSqFt = tileL * tileW;

            // D. Bucle de celdas de baldosas
            for (int row = minRow; row <= numRows; row++)
            {
                double rowShiftU = 0.0;
                if (config.Pattern == FloorPatternType.RunningBond50)
                {
                    if (Math.Abs(row) % 2 == 1) rowShiftU = stepU * 0.5;
                }
                else if (config.Pattern == FloorPatternType.ThirdBond33)
                {
                    rowShiftU = (Math.Abs(row) % 3) * (stepU / 3.0);
                }

                for (int col = minCol; col <= numCols; col++)
                {
                    double localU = col * stepU + rowShiftU;
                    double localV = row * stepV;

                    // Posición base P en UV rotado desde el punto de desfase (offU, offV)
                    XYZ P = new XYZ(offU, offV, 0) + dirU_UV * localU + dirV_UV * localV;

                    // Coordenadas de las 4 esquinas de la celda de baldosa perfectamente rectangulares y rotadas
                    XYZ cu0 = P;
                    XYZ cu1 = P + dirU_UV * tileL;
                    XYZ cu2 = P + dirU_UV * tileL + dirV_UV * tileW;
                    XYZ cu3 = P + dirV_UV * tileW;

                    List<XYZ> tileCornersUV = new List<XYZ> { cu0, cu1, cu2, cu3 };

                    // 1. Descarte rápido por Bounding Box (AABB) 2D
                    double tMinU = tileCornersUV.Min(p => p.X);
                    double tMaxU = tileCornersUV.Max(p => p.X);
                    double tMinV = tileCornersUV.Min(p => p.Y);
                    double tMaxV = tileCornersUV.Max(p => p.Y);

                    if (tMaxU < -1e-4 || tMinU > spanU + 1e-4 || tMaxV < -1e-4 || tMinV > spanV + 1e-4)
                    {
                        // Completamente fuera de la extensión del piso
                        continue;
                    }

                    // 2. Evaluar relación con el contorno del piso
                    int insideCount = 0;
                    for (int k = 0; k < 4; k++)
                    {
                        if (IsPointInsideContour(tileCornersUV[k], outerPolyUV, innerPolysUV))
                        {
                            insideCount++;
                        }
                    }

                    bool edgesIntersect = CheckEdgeIntersections(tileCornersUV, outerPolyUV, innerPolysUV);

                    // Si no tiene puntos dentro y ningún borde se cruza, está completamente fuera
                    if (insideCount == 0 && !edgesIntersect)
                    {
                        continue;
                    }

                    // 3. Construir CurveLoop individual de la celda en 3D en la base del acabado
                    XYZ w0 = localTransform.OfPoint(new XYZ(cu0.X, cu0.Y, 0.0));
                    XYZ w1 = localTransform.OfPoint(new XYZ(cu1.X, cu1.Y, 0.0));
                    XYZ w2 = localTransform.OfPoint(new XYZ(cu2.X, cu2.Y, 0.0));
                    XYZ w3 = localTransform.OfPoint(new XYZ(cu3.X, cu3.Y, 0.0));

                    CurveLoop tileBaseLoop = new CurveLoop();
                    tileBaseLoop.Append(Line.CreateBound(w0, w1));
                    tileBaseLoop.Append(Line.CreateBound(w1, w2));
                    tileBaseLoop.Append(Line.CreateBound(w2, w3));
                    tileBaseLoop.Append(Line.CreateBound(w3, w0));

                    if (tileBaseLoop.IsOpen()) continue;

                    Solid? tileSolid = null;
                    try
                    {
                        tileSolid = GeometryCreationUtilities.CreateExtrusionGeometry(
                            new List<CurveLoop> { tileBaseLoop },
                            normal,
                            tileThick);
                    }
                    catch
                    {
                        continue;
                    }

                    if (tileSolid == null) continue;

                    Solid? finalSolid = null;
                    bool isCut = false;

                    // 4. Evaluación de relación:
                    // Caso A: Totalmente contenida (4 esquinas dentro y ningún cruce de borde con perímetro o vanos)
                    if (insideCount == 4 && !edgesIntersect)
                    {
                        finalSolid = tileSolid;
                        isCut = false;
                    }
                    // Caso B: Intersecta el borde -> Recortar mediante BooleanOperationsUtils.ExecuteBooleanOperation
                    else
                    {
                        try
                        {
                            finalSolid = BooleanOperationsUtils.ExecuteBooleanOperation(
                                tileSolid,
                                floorPrism,
                                BooleanOperationsType.Intersect);
                        }
                        catch
                        {
                            finalSolid = null;
                        }

                        if (finalSolid == null || finalSolid.Volume < 1e-6 || finalSolid.Faces.Size == 0)
                        {
                            // Completamente fuera o astilla descartable
                            continue;
                        }

                        // Descartar si el remanente es una micro-astilla menor al 0.5% del área nominal
                        double cutVol = finalSolid.Volume;
                        double cutArea = cutVol / tileThick;
                        if (cutArea < 0.005 * fullAreaSqFt)
                        {
                            continue;
                        }

                        isCut = Math.Abs(cutArea - fullAreaSqFt) > 0.02 * fullAreaSqFt;
                    }

                    if (finalSolid == null) continue;

                    // 5. Extraer vértices y metadata de la baldosa (para UI preview y cómputo de métricas)
                    List<XYZ> vertices3D = new List<XYZ>();
                    List<XYZ> verticesUV = new List<XYZ>();
                    XYZ center3D = (w0 + w1 + w2 + w3) / 4.0;

                    // Buscar cara planar del sólido final en contacto con el anfitrión
                    foreach (Face f in finalSolid.Faces)
                    {
                        if (f is PlanarFace pf && Math.Abs(pf.FaceNormal.DotProduct(normal)) > 0.8)
                        {
                            IList<CurveLoop> cutLoops = pf.GetEdgesAsCurveLoops();
                            if (cutLoops != null && cutLoops.Count > 0)
                            {
                                foreach (Curve c in cutLoops[0])
                                {
                                    IList<XYZ> tess = c.Tessellate();
                                    for (int m = 0; m < tess.Count - 1; m++)
                                    {
                                        XYZ pt = tess[m];
                                        XYZ locPt = localTransform.Inverse.OfPoint(pt);
                                        XYZ ptOnFace = localTransform.OfPoint(new XYZ(locPt.X, locPt.Y, 0.0));
                                        vertices3D.Add(ptOnFace);
                                        verticesUV.Add(new XYZ(locPt.X, locPt.Y, 0.0));
                                    }
                                }
                            }
                            break;
                        }
                    }

                    if (vertices3D.Count >= 3)
                    {
                        center3D = new XYZ(vertices3D.Average(p => p.X), vertices3D.Average(p => p.Y), vertices3D.Average(p => p.Z));
                    }
                    else
                    {
                        vertices3D = new List<XYZ> { w0, w1, w2, w3 };
                        verticesUV = tileCornersUV;
                        center3D = (w0 + w1 + w2 + w3) / 4.0;
                    }

                    double pieceAreaSqFt = finalSolid.Volume / tileThick;

                    results.Add(new TileResult
                    {
                        ColIndex = col,
                        RowIndex = row,
                        IsCut = isCut,
                        TileLoop = tileBaseLoop,
                        TileSolid = finalSolid,
                        Center3D = center3D,
                        Corner3D = w0,
                        PolygonVertices3D = vertices3D,
                        PolygonVerticesUV = verticesUV,
                        AreaSqFt = pieceAreaSqFt,
                        CutLatFt = isCut ? Math.Max(0.0, tileL - (verticesUV.Max(p => p.X) - verticesUV.Min(p => p.X))) : 0.0,
                        CutTopFt = isCut ? Math.Max(0.0, tileW - (verticesUV.Max(p => p.Y) - verticesUV.Min(p => p.Y))) : 0.0
                    });
                }
            }

            return results;
        }

        /// <summary>
        /// 5. Salida: Crear entidades válidas en Revit sin generar líneas auxiliares excedentes.
        /// No deja transacciones abiertas dentro del bucle.
        /// </summary>
        public static List<ElementId> CreateTileElements(
            Document doc,
            List<TileResult> tiles,
            TilePlacementConfig placementConfig)
        {
            List<ElementId> createdIds = new List<ElementId>();
            if (doc == null || tiles == null || tiles.Count == 0) return createdIds;

            ElementId categoryId = placementConfig.CategoryId;
            if (!DirectShape.IsValidCategoryId(categoryId, doc))
            {
                categoryId = new ElementId(BuiltInCategory.OST_Floors);
            }
            if (!DirectShape.IsValidCategoryId(categoryId, doc))
            {
                categoryId = new ElementId(BuiltInCategory.OST_GenericModel);
            }

            DirectShapeType? dsTypeFull = CategoryAssignmentService.GetOrCreateDirectShapeType(doc, categoryId, "Baldosa Entera");
            DirectShapeType? dsTypeCut = CategoryAssignmentService.GetOrCreateDirectShapeType(doc, categoryId, "Baldosa Cortada");

            // Instanciación directa y eficiente dentro de la transacción activa (sin abrir transacciones internas en bucle)
            foreach (var tile in tiles)
            {
                if (tile.TileSolid == null || tile.TileSolid.Volume < 1e-6) continue;

                try
                {
                    DirectShape ds = DirectShape.CreateElement(doc, categoryId);
                    ds.SetShape(new GeometryObject[] { tile.TileSolid });
                    ds.Name = tile.IsCut ? "Baldosa Cortada" : "Baldosa Entera";

                    DirectShapeType? targetType = tile.IsCut ? dsTypeCut : dsTypeFull;
                    if (targetType != null)
                    {
                        try { ds.SetTypeId(targetType.Id); } catch { }
                    }

                    Parameter pComm = ds.get_Parameter(BuiltInParameter.ALL_MODEL_INSTANCE_COMMENTS);
                    if (pComm != null && !pComm.IsReadOnly)
                    {
                        pComm.Set(tile.IsCut ? $"{placementConfig.CommentsPrefix}-CORTADA" : $"{placementConfig.CommentsPrefix}-ENTERA");
                    }

                    if (placementConfig.MaterialId != ElementId.InvalidElementId)
                    {
                        Parameter pMat = ds.get_Parameter(BuiltInParameter.MATERIAL_ID_PARAM);
                        if (pMat != null && !pMat.IsReadOnly) pMat.Set(placementConfig.MaterialId);
                    }

                    if (placementConfig.MeasureElements)
                    {
                        double customThick = tile.TileSolid.Volume / Math.Max(0.001, tile.AreaSqFt);
                        MeasurementManager.MeasureAndApply(ds, tile.TileSolid, customThicknessFt: customThick, units: 1);
                    }

                    createdIds.Add(ds.Id);
                }
                catch { }
            }

            return createdIds;
        }

        /// <summary>
        /// Flujo consolidado completo de modulación y despiece para un piso individual.
        /// Abre una ÚNICA transacción consolidada para todo el proceso.
        /// </summary>
        public static List<ElementId> ExecuteFloorTiling(
            Floor floor,
            TileGenerationConfig genConfig,
            TilePlacementConfig placementConfig)
        {
            Document doc = floor.Document;

            // 1. Extracción del Contorno Base
            PlanarFace? topFace = GetTopPlanarFace(floor);
            if (topFace == null) return new List<ElementId>();

            var (outerLoop, innerLoops) = ExtractBoundaryLoops(topFace);
            if (outerLoop == null || outerLoop.IsOpen()) return new List<ElementId>();

            // 2. Sistema de Coordenadas Local (Replanteo)
            var (localTransform, _, spanU, spanV) = CreateLocalCoordinateSystem(outerLoop, topFace);

            // 3 y 4. Malla de Despiece y Recorte Booleano
            List<TileResult> tiles = GenerateFloorTiles(
                topFace,
                outerLoop,
                innerLoops,
                localTransform,
                spanU,
                spanV,
                genConfig);

            if (tiles.Count == 0) return new List<ElementId>();

            // 5. Salida consolidada en una única Transacción
            List<ElementId> createdIds = new List<ElementId>();
            using (Transaction t = new Transaction(doc, "Generar Despiece de Baldosas"))
            {
                t.Start();
                MeasurementManager.EnsureMeasurementParameters(doc);
                createdIds = CreateTileElements(doc, tiles, placementConfig);
                t.Commit();
            }

            return createdIds;
        }

        #region Utilidades Geométricas Auxiliares

        private static double ComputeLoopArea(CurveLoop loop, XYZ normal)
        {
            List<XYZ> pts = new List<XYZ>();
            foreach (Curve c in loop)
            {
                IList<XYZ> tess = c.Tessellate();
                for (int k = 0; k < tess.Count - 1; k++)
                {
                    pts.Add(tess[k]);
                }
            }
            if (pts.Count < 3) return 0.0;

            XYZ totalCross = XYZ.Zero;
            for (int i = 0; i < pts.Count; i++)
            {
                XYZ p1 = pts[i];
                XYZ p2 = pts[(i + 1) % pts.Count];
                totalCross += p1.CrossProduct(p2);
            }
            return Math.Abs(totalCross.DotProduct(normal.Normalize())) / 2.0;
        }

        private static List<XYZ> ExtractPolygonUV(CurveLoop loop, Transform localTransform)
        {
            List<XYZ> polyUV = new List<XYZ>();
            foreach (Curve c in loop)
            {
                IList<XYZ> tess = c.Tessellate();
                for (int k = 0; k < tess.Count - 1; k++)
                {
                    XYZ locPt = localTransform.Inverse.OfPoint(tess[k]);
                    polyUV.Add(new XYZ(locPt.X, locPt.Y, 0));
                }
            }
            return polyUV;
        }

        private static XYZ RotatePoint2D(XYZ pt, double angleRad, double origX, double origY)
        {
            if (Math.Abs(angleRad) < 1e-6) return pt;
            double dx = pt.X - origX;
            double dy = pt.Y - origY;
            double cos = Math.Cos(angleRad);
            double sin = Math.Sin(angleRad);
            return new XYZ(origX + dx * cos - dy * sin, origY + dx * sin + dy * cos, 0);
        }

        private static bool IsPointInsideContour(XYZ pt, List<XYZ> outerPoly, List<List<XYZ>> innerPolys)
        {
            if (!IsPointInPolygon2D(pt, outerPoly)) return false;
            if (innerPolys != null)
            {
                foreach (var inner in innerPolys)
                {
                    if (IsPointInPolygon2D(pt, inner)) return false;
                }
            }
            return true;
        }

        private static bool IsPointInPolygon2D(XYZ pt, List<XYZ> poly)
        {
            if (poly == null || poly.Count < 3) return false;
            bool inside = false;
            for (int i = 0, j = poly.Count - 1; i < poly.Count; j = i++)
            {
                if (((poly[i].Y > pt.Y) != (poly[j].Y > pt.Y)) &&
                    (pt.X < (poly[j].X - poly[i].X) * (pt.Y - poly[i].Y) / (poly[j].Y - poly[i].Y) + poly[i].X))
                {
                    inside = !inside;
                }
            }
            return inside;
        }

        private static bool CheckEdgeIntersections(List<XYZ> tileCorners, List<XYZ> outerPoly, List<List<XYZ>> innerPolys)
        {
            for (int i = 0; i < 4; i++)
            {
                XYZ a1 = tileCorners[i];
                XYZ a2 = tileCorners[(i + 1) % 4];

                for (int j = 0; j < outerPoly.Count; j++)
                {
                    XYZ b1 = outerPoly[j];
                    XYZ b2 = outerPoly[(j + 1) % outerPoly.Count];
                    if (DoLinesIntersect2D(a1, a2, b1, b2)) return true;
                }

                if (innerPolys != null)
                {
                    foreach (var inner in innerPolys)
                    {
                        for (int j = 0; j < inner.Count; j++)
                        {
                            XYZ b1 = inner[j];
                            XYZ b2 = inner[(j + 1) % inner.Count];
                            if (DoLinesIntersect2D(a1, a2, b1, b2)) return true;
                        }
                    }
                }
            }
            return false;
        }

        private static bool DoLinesIntersect2D(XYZ p1, XYZ p2, XYZ p3, XYZ p4)
        {
            double d1 = Direction2D(p3, p4, p1);
            double d2 = Direction2D(p3, p4, p2);
            double d3 = Direction2D(p1, p2, p3);
            double d4 = Direction2D(p1, p2, p4);

            if (((d1 > 0 && d2 < 0) || (d1 < 0 && d2 > 0)) &&
                ((d3 > 0 && d4 < 0) || (d3 < 0 && d4 > 0)))
                return true;

            return false;
        }

        private static double Direction2D(XYZ pi, XYZ pj, XYZ pk)
        {
            return (pk.X - pi.X) * (pj.Y - pi.Y) - (pj.X - pi.X) * (pk.Y - pi.Y);
        }

        private static CurveLoop ReverseCurveLoop(CurveLoop loop)
        {
            CurveLoop rev = new CurveLoop();
            var curves = loop.ToList();
            curves.Reverse();
            foreach (Curve c in curves)
            {
                rev.Append(c.CreateReversed());
            }
            return rev;
        }

        #endregion
    }
}
