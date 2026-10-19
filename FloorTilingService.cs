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
        public double JointThicknessFt { get; set; } = (3.0 / 1000.0) * 3.28084; // Espesor de junta interna (3-4 mm)
        public double PerimeterJointThicknessFt { get; set; } = (5.0 / 1000.0) * 3.28084; // Espesor de junta perimetral (5 mm)
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
        public Reference? HostFaceReference { get; set; }
        public Level? HostLevel { get; set; }
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
        public XYZ DirectionU3D { get; set; } = XYZ.BasisX;
        public XYZ DirectionV3D { get; set; } = XYZ.BasisY;
        public XYZ Normal3D { get; set; } = XYZ.BasisZ;
        public double NominalLengthFt { get; set; }
        public double NominalWidthFt { get; set; }
        public double ThicknessFt { get; set; }
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
        public static PlanarFace? GetTopPlanarFace(Floor floor) => GetTopPlanarFaceWithRef(floor).face;

        public static (PlanarFace? face, Reference? faceRef) GetTopPlanarFaceWithRef(Floor floor)
        {
            if (floor == null) return (null, null);

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
                            return (pf, r);
                        }
                    }
                }
            }
            catch { }

            // Intento 2: Búsqueda geométrica en sólidos del elemento
            Options opt = new Options { DetailLevel = ViewDetailLevel.Fine, IncludeNonVisibleObjects = false, ComputeReferences = true };
            GeometryElement geomElem = floor.get_Geometry(opt);
            if (geomElem == null) return (null, null);

            PlanarFace? bestFace = null;
            Reference? bestRef = null;
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
                                bestRef = pf.Reference;
                            }
                        }
                    }
                }
            }

            return (bestFace, bestRef);
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
        /// 2. Sistema de Coordenadas Local Cartesiano (0,0) alineado con la línea de Span Direction del piso.
        /// Establece el origen (0,0) en la esquina inferior-izquierda (minU, minV) como plano cartesiano tipo AutoCAD,
        /// donde +U sigue la dirección de Span Direction (sentido de las juntas largas horizontales) y +V va de abajo hacia arriba.
        /// </summary>
        public static (Transform localTransform, Curve spanDirectionSegment, double spanU, double spanV) CreateLocalCoordinateSystem(
            CurveLoop outerLoop,
            PlanarFace topFace,
            Floor? floor = null)
        {
            if (outerLoop == null) throw new ArgumentNullException(nameof(outerLoop));

            XYZ normal = topFace.FaceNormal.Normalize();

            // A. Identificar el segmento más largo como referencia geométrica base
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

            XYZ dirU = XYZ.Zero;

            // B. Identificar la línea / ángulo de Span Direction del Floor en Revit
            if (floor != null)
            {
                try
                {
                    double spanAngle = floor.SpanDirectionAngle;
                    XYZ rawSpanDir = new XYZ(Math.Cos(spanAngle), Math.Sin(spanAngle), 0.0);
                    // Proyectar sobre el plano del piso por si tiene pendiente
                    XYZ projectedSpan = rawSpanDir - normal * rawSpanDir.DotProduct(normal);
                    if (projectedSpan.GetLength() > 1e-5)
                    {
                        dirU = projectedSpan.Normalize();
                    }
                }
                catch { }
            }

            // Fallback al segmento principal si no se pudo leer SpanDirectionAngle
            if (dirU.GetLength() < 1e-5)
            {
                XYZ p0 = longestSegment.GetEndPoint(0);
                XYZ p1 = longestSegment.GetEndPoint(1);
                dirU = (p1 - p0).Normalize();
                if (dirU.GetLength() < 1e-5)
                {
                    dirU = XYZ.BasisX;
                }
            }

            // Orientar consistentemente dirU de izquierda a derecha (+X o +Y en el plano global)
            if (dirU.X < -1e-4 || (Math.Abs(dirU.X) < 1e-4 && dirU.Y < -1e-4))
            {
                dirU = -dirU;
            }

            // Eje V perpendicular en el plano cartesiano (de abajo hacia arriba: Normal x dirU)
            XYZ dirV = normal.CrossProduct(dirU).Normalize();

            // C. Proyectar todos los vértices del contorno a UV para hallar el (0,0) cartesiano en (minU, minV)
            double minU = double.MaxValue;
            double maxU = double.MinValue;
            double minV = double.MaxValue;
            double maxV = double.MinValue;

            XYZ refOrigin = longestSegment.GetEndPoint(0);

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

            // D. Punto (0,0) Cartesiano del piso en la esquina inferior-izquierda (minU, minV)
            XYZ originLocal = refOrigin + dirU * minU + dirV * minV;

            Transform localTransform = Transform.Identity;
            localTransform.Origin = originLocal;
            localTransform.BasisX = dirU;
            localTransform.BasisY = dirV;
            localTransform.BasisZ = normal;

            double spanU = maxU - minU;
            double spanV = maxV - minV;

            // Crear línea representativa de Span Direction desde el (0,0) a lo largo de U
            Curve spanLine = spanU > 0.01
                ? Line.CreateBound(originLocal, originLocal + dirU * spanU)
                : longestSegment;

            return (localTransform, spanLine, spanU, spanV);
        }

        /// <summary>
        /// 3. Malla de Despiece Cartesiana desde el (0,0):
        /// Traza junta perimetral (5mm) -> espacio de baldosa -> espacio de junta interna (3-4mm) -> espacio de baldosa...
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
            double perimJoint = Math.Max(0.0, config.PerimeterJointThicknessFt);

            double stepU = tileL + joint;
            double stepV = tileW + joint;

            // A. Crear Prisma de Recorte Extruido del Contorno del Piso
            double deltaZ = 0.05;
            List<CurveLoop> floorLoops = new List<CurveLoop> { outerLoop };
            if (innerLoops != null && innerLoops.Count > 0)
            {
                floorLoops.AddRange(innerLoops);
            }

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

            // B. Polígono 2D del Contorno en Coordenadas Locales UV
            List<XYZ> outerPolyUV = ExtractPolygonUV(outerLoop, localTransform);
            List<List<XYZ>> innerPolysUV = innerLoops != null
                ? innerLoops.Select(inLoop => ExtractPolygonUV(inLoop, localTransform)).ToList()
                : new List<List<XYZ>>();

            // C. Inicio desde el (0,0) cartesiano + junta perimetral (5mm)
            double rotRad = config.RotationAngleRad;
            double startU = perimJoint + config.OffsetUFt;
            double startV = perimJoint + config.OffsetVFt;

            double cosR = Math.Cos(rotRad);
            double sinR = Math.Sin(rotRad);
            XYZ dirU_UV = new XYZ(cosR, sinR, 0);
            XYZ dirV_UV = new XYZ(-sinR, cosR, 0);

            XYZ dirU_3D = (localTransform.BasisX * cosR + localTransform.BasisY * sinR).Normalize();
            XYZ dirV_3D = (-localTransform.BasisX * sinR + localTransform.BasisY * cosR).Normalize();

            int maxCols = (int)Math.Ceiling(Math.Max(0.01, spanU - 2.0 * perimJoint) / stepU) + 2;
            int maxRows = (int)Math.Ceiling(Math.Max(0.01, spanV - 2.0 * perimJoint) / stepV) + 2;
            int minCol = Math.Abs(rotRad) > 1e-4 ? -maxCols : 0;
            int minRow = Math.Abs(rotRad) > 1e-4 ? -maxRows : 0;

            double fullAreaSqFt = tileL * tileW;

            // D. Array de filas de abajo hacia arriba (V) y piezas de izquierda a derecha (U)
            for (int row = minRow; row <= maxRows; row++)
            {
                double rowShiftU = 0.0;
                if (config.Pattern == FloorPatternType.RunningBond50)
                {
                    if (Math.Abs(row) % 2 == 1) rowShiftU = -tileL * 0.5;
                }
                else if (config.Pattern == FloorPatternType.ThirdBond33)
                {
                    rowShiftU = -(Math.Abs(row) % 3) * (tileL / 3.0);
                }

                for (int col = minCol; col <= maxCols; col++)
                {
                    double localU = col * stepU + rowShiftU;
                    double localV = row * stepV;

                    XYZ P = new XYZ(startU, startV, 0) + dirU_UV * localU + dirV_UV * localV;

                    // Ajustar límites al espacio neto entre juntas perimetrales [perimJoint, span - perimJoint]
                    if (Math.Abs(rotRad) < 1e-4)
                    {
                        double u0 = Math.Max(perimJoint, P.X);
                        double v0 = Math.Max(perimJoint, P.Y);
                        double u1 = Math.Min(spanU - perimJoint, P.X + tileL);
                        double v1 = Math.Min(spanV - perimJoint, P.Y + tileW);

                        if (u1 - u0 <= 0.005 || v1 - v0 <= 0.005) continue;

                        double actualL = u1 - u0;
                        double actualW = v1 - v0;
                        double cutLat = Math.Max(0.0, tileL - actualL);
                        double cutTop = Math.Max(0.0, tileW - actualW);
                        bool isCutPiece = cutLat > 0.001 || cutTop > 0.001;

                        XYZ c0 = new XYZ(u0, v0, 0);
                        XYZ c1 = new XYZ(u1, v0, 0);
                        XYZ c2 = new XYZ(u1, v1, 0);
                        XYZ c3 = new XYZ(u0, v1, 0);

                        XYZ w0 = localTransform.OfPoint(c0);
                        XYZ w1 = localTransform.OfPoint(c1);
                        XYZ w2 = localTransform.OfPoint(c2);
                        XYZ w3 = localTransform.OfPoint(c3);

                        CurveLoop loop = new CurveLoop();
                        loop.Append(Line.CreateBound(w0, w1));
                        loop.Append(Line.CreateBound(w1, w2));
                        loop.Append(Line.CreateBound(w2, w3));
                        loop.Append(Line.CreateBound(w3, w0));

                        Solid? solid = null;
                        try
                        {
                            solid = GeometryCreationUtilities.CreateExtrusionGeometry(new List<CurveLoop> { loop }, normal, tileThick);
                        }
                        catch { continue; }

                        if (solid == null) continue;

                        results.Add(new TileResult
                        {
                            ColIndex = col,
                            RowIndex = row,
                            IsCut = isCutPiece,
                            TileLoop = loop,
                            TileSolid = solid,
                            Center3D = (w0 + w1 + w2 + w3) / 4.0,
                            Corner3D = w0,
                            DirectionU3D = dirU_3D,
                            DirectionV3D = dirV_3D,
                            Normal3D = normal,
                            NominalLengthFt = tileL,
                            NominalWidthFt = tileW,
                            ThicknessFt = tileThick,
                            PolygonVertices3D = new List<XYZ> { w0, w1, w2, w3 },
                            PolygonVerticesUV = new List<XYZ> { c0, c1, c2, c3 },
                            AreaSqFt = actualL * actualW,
                            CutLatFt = cutLat,
                            CutTopFt = cutTop
                        });
                        continue;
                    }

                    // Caso con rotación arbitraria
                    XYZ cu0 = P;
                    XYZ cu1 = P + dirU_UV * tileL;
                    XYZ cu2 = P + dirU_UV * tileL + dirV_UV * tileW;
                    XYZ cu3 = P + dirV_UV * tileW;

                    List<XYZ> tileCornersUV = new List<XYZ> { cu0, cu1, cu2, cu3 };

                    double tMinU = tileCornersUV.Min(p => p.X);
                    double tMaxU = tileCornersUV.Max(p => p.X);
                    double tMinV = tileCornersUV.Min(p => p.Y);
                    double tMaxV = tileCornersUV.Max(p => p.Y);

                    if (tMaxU < perimJoint - 1e-4 || tMinU > spanU - perimJoint + 1e-4 ||
                        tMaxV < perimJoint - 1e-4 || tMinV > spanV - perimJoint + 1e-4)
                    {
                        continue;
                    }

                    int insideCount = 0;
                    for (int k = 0; k < 4; k++)
                    {
                        if (IsPointInsideContour(tileCornersUV[k], outerPolyUV, innerPolysUV))
                        {
                            insideCount++;
                        }
                    }

                    bool edgesIntersect = CheckEdgeIntersections(tileCornersUV, outerPolyUV, innerPolysUV);
                    if (insideCount == 0 && !edgesIntersect) continue;

                    XYZ rw0 = localTransform.OfPoint(new XYZ(cu0.X, cu0.Y, 0.0));
                    XYZ rw1 = localTransform.OfPoint(new XYZ(cu1.X, cu1.Y, 0.0));
                    XYZ rw2 = localTransform.OfPoint(new XYZ(cu2.X, cu2.Y, 0.0));
                    XYZ rw3 = localTransform.OfPoint(new XYZ(cu3.X, cu3.Y, 0.0));

                    CurveLoop tileBaseLoop = new CurveLoop();
                    tileBaseLoop.Append(Line.CreateBound(rw0, rw1));
                    tileBaseLoop.Append(Line.CreateBound(rw1, rw2));
                    tileBaseLoop.Append(Line.CreateBound(rw2, rw3));
                    tileBaseLoop.Append(Line.CreateBound(rw3, rw0));

                    if (tileBaseLoop.IsOpen()) continue;

                    Solid? tileSolid = null;
                    try
                    {
                        tileSolid = GeometryCreationUtilities.CreateExtrusionGeometry(
                            new List<CurveLoop> { tileBaseLoop },
                            normal,
                            tileThick);
                    }
                    catch { continue; }

                    if (tileSolid == null) continue;

                    Solid? finalSolid = tileSolid;
                    bool isCut = false;

                    if (insideCount < 4 || edgesIntersect)
                    {
                        try
                        {
                            finalSolid = BooleanOperationsUtils.ExecuteBooleanOperation(
                                tileSolid,
                                floorPrism,
                                BooleanOperationsType.Intersect);
                        }
                        catch { finalSolid = null; }

                        if (finalSolid == null || finalSolid.Volume < 1e-6 || finalSolid.Faces.Size == 0) continue;

                        double cutArea = finalSolid.Volume / tileThick;
                        if (cutArea < 0.005 * fullAreaSqFt) continue;
                        isCut = Math.Abs(cutArea - fullAreaSqFt) > 0.02 * fullAreaSqFt;
                    }

                    List<XYZ> vertices3D = new List<XYZ> { rw0, rw1, rw2, rw3 };
                    List<XYZ> verticesUV = tileCornersUV;
                    XYZ center3D = (rw0 + rw1 + rw2 + rw3) / 4.0;
                    double pieceAreaSqFt = finalSolid.Volume / tileThick;

                    results.Add(new TileResult
                    {
                        ColIndex = col,
                        RowIndex = row,
                        IsCut = isCut,
                        TileLoop = tileBaseLoop,
                        TileSolid = finalSolid,
                        Center3D = center3D,
                        Corner3D = rw0,
                        DirectionU3D = dirU_3D,
                        DirectionV3D = dirV_3D,
                        Normal3D = normal,
                        NominalLengthFt = tileL,
                        NominalWidthFt = tileW,
                        ThicknessFt = tileThick,
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
        /// 5. Salida: Crear exclusivamente instancias de Familias Cargables (Tiles Base.rfa) desde el (0,0) de cada pieza.
        /// Prohibido el uso de DirectShape.
        /// </summary>
        public static List<ElementId> CreateTileElements(
            Document doc,
            List<TileResult> tiles,
            TilePlacementConfig placementConfig)
        {
            List<ElementId> createdIds = new List<ElementId>();
            if (doc == null || tiles == null || tiles.Count == 0) return createdIds;

            FamilySymbol? tileSymbol = FindOrLoadTilesBaseSymbol(doc);
            if (tileSymbol == null) return createdIds;

            if (!tileSymbol.IsActive)
            {
                tileSymbol.Activate();
            }

            // Configurar dimensiones nominales y material en el tipo de familia (Tiles Base.rfa)
            double defaultFiveMmFt = (5.0 / 1000.0) * 3.28084;
            double minValidCutFt = (1.0 / 1000.0) * 3.28084; // 1.0 mm (> ShortCurveTolerance)

            var first = tiles[0];
            if (first.NominalLengthFt > 0.001 && first.NominalWidthFt > 0.001)
            {
                // En Tiles Base.rfa: Width = eje X (U / NominalLengthFt), Lenght = eje Y (V / NominalWidthFt), Offset = 5mm
                SetParam(tileSymbol, new[] { "Width", "Ancho" }, first.NominalLengthFt);
                SetParam(tileSymbol, new[] { "Lenght", "Length", "Largo" }, first.NominalWidthFt);
                SetParam(tileSymbol, new[] { "Thickness", "Espesor", "Height" }, first.ThicknessFt);
                SetParam(tileSymbol, new[] { "Offset", "Desfase" }, defaultFiveMmFt);
            }
            if (placementConfig.MaterialId != ElementId.InvalidElementId)
            {
                Parameter? pTypeMat = tileSymbol.LookupParameter("Mat") ?? tileSymbol.LookupParameter("Material");
                if (pTypeMat != null && !pTypeMat.IsReadOnly) pTypeMat.Set(placementConfig.MaterialId);
            }

            foreach (var tile in tiles)
            {
                try
                {
                    FamilyInstance? inst = null;
                    XYZ placePoint = tile.Corner3D;

                    if (placementConfig.HostFaceReference != null)
                    {
                        inst = doc.Create.NewFamilyInstance(
                            placementConfig.HostFaceReference,
                            placePoint,
                            tile.DirectionU3D,
                            tileSymbol);
                    }
                    else if (placementConfig.HostLevel != null)
                    {
                        inst = doc.Create.NewFamilyInstance(
                            placePoint,
                            tileSymbol,
                            placementConfig.HostLevel,
                            Autodesk.Revit.DB.Structure.StructuralType.NonStructural);
                    }

                    if (inst == null) continue;

                    // Sincronizar recortes de ejemplar en Tiles Base.rfa ("Cut", "Cut Lat" y "Cut Top")
                    double rawCutLatFt = tile.IsCut ? Math.Max(0.0, tile.CutLatFt) : 0.0;
                    double rawCutTopFt = tile.IsCut ? Math.Max(0.0, tile.CutTopFt) : 0.0;
                    bool hasEffectiveCut = tile.IsCut && (rawCutLatFt > 0.0005 || rawCutTopFt > 0.0005);

                    if (hasEffectiveCut)
                    {
                        SetParamIntInstanceOnly(inst, new[] { "Cut", "Corte", "Cortar", "IsCut" }, 1);

                        double safeCutLatFt = rawCutLatFt > minValidCutFt
                            ? Math.Min(rawCutLatFt, Math.Max(minValidCutFt, tile.NominalLengthFt - minValidCutFt))
                            : minValidCutFt;
                        double safeCutTopFt = rawCutTopFt > minValidCutFt
                            ? Math.Min(rawCutTopFt, Math.Max(minValidCutFt, tile.NominalWidthFt - minValidCutFt))
                            : minValidCutFt;

                        SetParamInstanceOnly(inst, new[] { "Cut Lat", "CutLat", "Cut_Lat" }, safeCutLatFt);
                        SetParamInstanceOnly(inst, new[] { "Cut Top", "CutTop", "Cut_Top" }, safeCutTopFt);
                    }
                    else
                    {
                        SetParamIntInstanceOnly(inst, new[] { "Cut", "Corte", "Cortar", "IsCut" }, 0);
                        SetParamInstanceOnly(inst, new[] { "Cut Lat", "CutLat", "Cut_Lat" }, defaultFiveMmFt);
                        SetParamInstanceOnly(inst, new[] { "Cut Top", "CutTop", "Cut_Top" }, defaultFiveMmFt);
                    }

                    if (placementConfig.MaterialId != ElementId.InvalidElementId)
                    {
                        Parameter? pMat = inst.LookupParameter("Mat")
                                       ?? inst.LookupParameter("Material")
                                       ?? inst.get_Parameter(BuiltInParameter.MATERIAL_ID_PARAM);
                        if (pMat != null && !pMat.IsReadOnly) pMat.Set(placementConfig.MaterialId);
                    }

                    Parameter pComm = inst.get_Parameter(BuiltInParameter.ALL_MODEL_INSTANCE_COMMENTS);
                    if (pComm != null && !pComm.IsReadOnly)
                    {
                        pComm.Set(hasEffectiveCut ? $"{placementConfig.CommentsPrefix}-CORTADA" : $"{placementConfig.CommentsPrefix}-ENTERA");
                    }

                    if (placementConfig.MeasureElements)
                    {
                        double actualL = Math.Max(0.001, tile.NominalLengthFt - tile.CutLatFt);
                        double actualW = Math.Max(0.001, tile.NominalWidthFt - tile.CutTopFt);
                        MeasurementManager.MeasureAndApply(inst, null,
                            customLengthFt: actualL,
                            customWidthFt: actualW,
                            customAreaSqFt: tile.AreaSqFt,
                            customThicknessFt: tile.ThicknessFt,
                            customVolumeCuFt: tile.AreaSqFt * tile.ThicknessFt,
                            units: 1);
                    }

                    createdIds.Add(inst.Id);
                }
                catch { }
            }

            return createdIds;
        }

        private static FamilySymbol? FindOrLoadTilesBaseSymbol(Document doc)
        {
            var family = new FilteredElementCollector(doc)
                .OfClass(typeof(Family))
                .Cast<Family>()
                .FirstOrDefault(f => f.Name.Equals("Tiles Base", StringComparison.OrdinalIgnoreCase) ||
                                     f.Name.Equals("Base Tiles", StringComparison.OrdinalIgnoreCase));

            if (family == null)
            {
                string addinFolder = System.IO.Path.GetDirectoryName(System.Reflection.Assembly.GetExecutingAssembly().Location) ?? "";
                string[] searchPaths = new[]
                {
                    System.IO.Path.Combine(addinFolder, "Familias", "Floors-Tiles", "Tiles Base.rfa"),
                    System.IO.Path.Combine(addinFolder, "Tiles Base.rfa"),
                    System.IO.Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData), @"Autodesk\Revit\Addins\2027\Familias\Floors-Tiles", "Tiles Base.rfa")
                };

                string? found = searchPaths.FirstOrDefault(System.IO.File.Exists);
                if (found != null)
                {
                    doc.LoadFamily(found, out family);
                }
            }

            if (family != null)
            {
                var ids = family.GetFamilySymbolIds();
                if (ids.Count > 0) return doc.GetElement(ids.First()) as FamilySymbol;
            }
            return null;
        }

        private static void SetParam(Element elem, string[] names, double val)
        {
            foreach (string n in names)
            {
                Parameter? p = elem.LookupParameter(n);
                if (p != null && !p.IsReadOnly && p.StorageType == StorageType.Double)
                {
                    p.Set(val);
                    return;
                }
            }
        }

        private static void SetParamInstanceOnly(Element elem, string[] names, double val)
        {
            foreach (string n in names)
            {
                Parameter? p = elem.LookupParameter(n);
                if (p != null && !p.IsReadOnly && p.StorageType == StorageType.Double)
                {
                    p.Set(val);
                    return;
                }
            }
        }

        private static void SetParamIntInstanceOnly(Element elem, string[] names, int val)
        {
            foreach (string n in names)
            {
                Parameter? p = elem.LookupParameter(n);
                if (p != null && !p.IsReadOnly && p.StorageType == StorageType.Integer)
                {
                    p.Set(val);
                    return;
                }
            }
        }

        /// <summary>
        /// Flujo consolidado completo de modulación y despiece para un piso individual usando exclusivamente familias cargables.
        /// </summary>
        public static List<ElementId> ExecuteFloorTiling(
            Floor floor,
            TileGenerationConfig genConfig,
            TilePlacementConfig placementConfig)
        {
            Document doc = floor.Document;

            // 1. Extracción del Contorno Base y Referencia de Cara
            var (topFace, faceRef) = GetTopPlanarFaceWithRef(floor);
            if (topFace == null) return new List<ElementId>();

            var (outerLoop, innerLoops) = ExtractBoundaryLoops(topFace);
            if (outerLoop == null || outerLoop.IsOpen()) return new List<ElementId>();

            // 2. Sistema de Coordenadas Local Cartesiano (0,0) alineado con Span Direction
            var (localTransform, _, spanU, spanV) = CreateLocalCoordinateSystem(outerLoop, topFace, floor);

            // 3 y 4. Malla de Despiece desde el (0,0)
            List<TileResult> tiles = GenerateFloorTiles(
                topFace,
                outerLoop,
                innerLoops,
                localTransform,
                spanU,
                spanV,
                genConfig);

            if (tiles.Count == 0) return new List<ElementId>();

            placementConfig.HostFaceReference = faceRef ?? topFace.Reference;
            placementConfig.HostLevel = doc.GetElement(floor.LevelId) as Level;

            // 5. Salida consolidada en una única Transacción (100% Familias Cargables)
            List<ElementId> createdIds = new List<ElementId>();
            using (Transaction t = new Transaction(doc, "Generar Despiece de Baldosas (Familias RFA)"))
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

        public static List<UV> ClipPolygonWithRect(List<UV> poly, double uMin, double vMin, double uMax, double vMax)
        {
            if (poly == null || poly.Count < 3) return new List<UV>();
            List<UV> output = new List<UV>(poly);

            output = ClipEdgeUV(output, p => p.U >= uMin, (p1, p2) => new UV(uMin, p1.V + (p2.V - p1.V) * (uMin - p1.U) / (p2.U - p1.U)));
            output = ClipEdgeUV(output, p => p.U <= uMax, (p1, p2) => new UV(uMax, p1.V + (p2.V - p1.V) * (uMax - p1.U) / (p2.U - p1.U)));
            output = ClipEdgeUV(output, p => p.V >= vMin, (p1, p2) => new UV(p1.U + (p2.U - p1.U) * (vMin - p1.V) / (p2.V - p1.V), vMin));
            output = ClipEdgeUV(output, p => p.V <= vMax, (p1, p2) => new UV(p1.U + (p2.U - p1.U) * (vMax - p1.V) / (p2.V - p1.V), vMax));

            return output;
        }

        private static List<UV> ClipEdgeUV(List<UV> input, Func<UV, bool> isInside, Func<UV, UV, UV> intersect)
        {
            List<UV> output = new List<UV>();
            if (input == null || input.Count == 0) return output;

            UV S = input[input.Count - 1];
            foreach (UV E in input)
            {
                if (isInside(E))
                {
                    if (!isInside(S)) output.Add(intersect(S, E));
                    output.Add(E);
                }
                else if (isInside(S))
                {
                    output.Add(intersect(S, E));
                }
                S = E;
            }
            return output;
        }

        #endregion
    }
}
