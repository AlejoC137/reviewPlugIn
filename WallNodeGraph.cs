using System;
using System.Collections.Generic;
using System.Linq;
using Autodesk.Revit.DB;

namespace RevitToSupabasePlugin
{
    public static class WallNodeGraph
    {
        private const double Tolerance = 0.05; // ~15mm tolerancia en pies

        public static List<WallNodeInfo> AnalyzeWallNodes(List<Wall> walls)
        {
            List<WallNodeInfo> nodes = new List<WallNodeInfo>();

            for (int i = 0; i < walls.Count; i++)
            {
                Wall wA = walls[i];
                LocationCurve lcA = wA.Location as LocationCurve;
                if (lcA == null) continue;
                Curve cA = lcA.Curve;

                XYZ a0 = cA.GetEndPoint(0);
                XYZ a1 = cA.GetEndPoint(1);

                for (int j = i + 1; j < walls.Count; j++)
                {
                    Wall wB = walls[j];
                    LocationCurve lcB = wB.Location as LocationCurve;
                    if (lcB == null) continue;
                    Curve cB = lcB.Curve;

                    XYZ b0 = cB.GetEndPoint(0);
                    XYZ b1 = cB.GetEndPoint(1);

                    // 1. Verificar Esquinas en L (coincidencia de extremos)
                    if (a0.DistanceTo(b0) < Tolerance)
                    {
                        nodes.Add(new WallNodeInfo { NodeType = WallNodeType.L_Corner, Point = (a0 + b0) * 0.5, WallA = wA, WallB = wB, EndIndexA = 0, EndIndexB = 0 });
                        continue;
                    }
                    if (a0.DistanceTo(b1) < Tolerance)
                    {
                        nodes.Add(new WallNodeInfo { NodeType = WallNodeType.L_Corner, Point = (a0 + b1) * 0.5, WallA = wA, WallB = wB, EndIndexA = 0, EndIndexB = 1 });
                        continue;
                    }
                    if (a1.DistanceTo(b0) < Tolerance)
                    {
                        nodes.Add(new WallNodeInfo { NodeType = WallNodeType.L_Corner, Point = (a1 + b0) * 0.5, WallA = wA, WallB = wB, EndIndexA = 1, EndIndexB = 0 });
                        continue;
                    }
                    if (a1.DistanceTo(b1) < Tolerance)
                    {
                        nodes.Add(new WallNodeInfo { NodeType = WallNodeType.L_Corner, Point = (a1 + b1) * 0.5, WallA = wA, WallB = wB, EndIndexA = 1, EndIndexB = 1 });
                        continue;
                    }

                    // 2. Verificar Encuentros en T (un extremo de B proyecta sobre el tramo interior de A, o viceversa)
                    IntersectionResult resB0OnA = cA.Project(b0);
                    if (resB0OnA != null && resB0OnA.Distance < Tolerance)
                    {
                        double param = resB0OnA.Parameter;
                        if (param > cA.GetEndParameter(0) + Tolerance && param < cA.GetEndParameter(1) - Tolerance)
                        {
                            nodes.Add(new WallNodeInfo { NodeType = WallNodeType.T_Junction, Point = b0, WallA = wA, WallB = wB, EndIndexA = -1, EndIndexB = 0 });
                            continue;
                        }
                    }

                    IntersectionResult resB1OnA = cA.Project(b1);
                    if (resB1OnA != null && resB1OnA.Distance < Tolerance)
                    {
                        double param = resB1OnA.Parameter;
                        if (param > cA.GetEndParameter(0) + Tolerance && param < cA.GetEndParameter(1) - Tolerance)
                        {
                            nodes.Add(new WallNodeInfo { NodeType = WallNodeType.T_Junction, Point = b1, WallA = wA, WallB = wB, EndIndexA = -1, EndIndexB = 1 });
                            continue;
                        }
                    }

                    // Proyectar A sobre B
                    IntersectionResult resA0OnB = cB.Project(a0);
                    if (resA0OnB != null && resA0OnB.Distance < Tolerance)
                    {
                        double param = resA0OnB.Parameter;
                        if (param > cB.GetEndParameter(0) + Tolerance && param < cB.GetEndParameter(1) - Tolerance)
                        {
                            nodes.Add(new WallNodeInfo { NodeType = WallNodeType.T_Junction, Point = a0, WallA = wB, WallB = wA, EndIndexA = -1, EndIndexB = 0 });
                            continue;
                        }
                    }

                    IntersectionResult resA1OnB = cB.Project(a1);
                    if (resA1OnB != null && resA1OnB.Distance < Tolerance)
                    {
                        double param = resA1OnB.Parameter;
                        if (param > cB.GetEndParameter(0) + Tolerance && param < cB.GetEndParameter(1) - Tolerance)
                        {
                            nodes.Add(new WallNodeInfo { NodeType = WallNodeType.T_Junction, Point = a1, WallA = wB, WallB = wA, EndIndexA = -1, EndIndexB = 1 });
                            continue;
                        }
                    }
                }
            }

            return nodes;
        }
    }
}
