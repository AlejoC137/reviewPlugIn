using System;
using System.Collections.Generic;
using Autodesk.Revit.DB;

namespace RevitToSupabasePlugin
{
    public static class CadToBimImporter
    {
        public static (int wallsCreated, int linesSkipped) ConvertDwgToNative3D(Document doc, ImportInstance dwgInstance, Level level)
        {
            int wallsCreated = 0;
            int linesSkipped = 0;

            Options opt = new Options
            {
                ComputeReferences = true,
                IncludeNonVisibleObjects = false
            };

            GeometryElement geomElem = dwgInstance.get_Geometry(opt);
            if (geomElem == null) return (0, 0);

            // Obtener el tipo de muro por defecto
            WallType? defaultWallType = new FilteredElementCollector(doc)
                .OfClass(typeof(WallType))
                .FirstElement() as WallType;

            if (defaultWallType == null) return (0, 0);

            // Extraer líneas desde el CAD
            List<Line> validLines = new List<Line>();
            ExtractLinesFromGeometry(geomElem, validLines);

            if (validLines.Count == 0) return (0, 0);

            using (Transaction t = new Transaction(doc, "Convertir CAD a Elementos Base Revit"))
            {
                t.Start();

                double heightFeet = 3000.0 / 304.8; // 3 metros estándar

                foreach (Line line in validLines)
                {
                    // Filtrar vectores verticales puros o líneas microscópicas (< 30 cm)
                    XYZ dir = line.Direction;
                    if (Math.Abs(dir.Z) > 0.9 || line.Length < (300.0 / 304.8))
                    {
                        linesSkipped++;
                        continue;
                    }

                    // Aplanar la línea al plano horizontal del nivel
                    XYZ p0 = new XYZ(line.GetEndPoint(0).X, line.GetEndPoint(0).Y, level.Elevation);
                    XYZ p1 = new XYZ(line.GetEndPoint(1).X, line.GetEndPoint(1).Y, level.Elevation);

                    if (p0.DistanceTo(p1) < (300.0 / 304.8))
                    {
                        linesSkipped++;
                        continue;
                    }

                    Line flatLine = Line.CreateBound(p0, p1);

                    try
                    {
                        Wall.Create(doc, flatLine, defaultWallType.Id, level.Id, heightFeet, 0.0, false, false);
                        wallsCreated++;
                    }
                    catch
                    {
                        linesSkipped++;
                    }
                }

                t.Commit();
            }

            return (wallsCreated, linesSkipped);
        }

        private static void ExtractLinesFromGeometry(GeometryElement geomElem, List<Line> lines)
        {
            foreach (GeometryObject geomObj in geomElem)
            {
                if (geomObj is Line l)
                {
                    lines.Add(l);
                }
                else if (geomObj is PolyLine poly)
                {
                    IList<XYZ> pts = poly.GetCoordinates();
                    for (int i = 0; i < pts.Count - 1; i++)
                    {
                        if (pts[i].DistanceTo(pts[i + 1]) > 0.01)
                        {
                            lines.Add(Line.CreateBound(pts[i], pts[i + 1]));
                        }
                    }
                }
                else if (geomObj is GeometryInstance inst)
                {
                    GeometryElement instanceGeom = inst.GetInstanceGeometry();
                    if (instanceGeom != null)
                    {
                        ExtractLinesFromGeometry(instanceGeom, lines);
                    }
                }
            }
        }
    }
}
