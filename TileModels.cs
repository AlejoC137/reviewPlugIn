using System;
using System.Collections.Generic;
using System.Text.Json.Serialization;
using Autodesk.Revit.DB;

namespace RevitToSupabasePlugin
{
    public class TileType
    {
        [JsonPropertyName("id")]
        public string? Id { get; set; }

        [JsonPropertyName("name")]
        public string? Name { get; set; }

        [JsonPropertyName("width_cm")]
        public double WidthCm { get; set; } = 60.0;

        [JsonPropertyName("length_cm")]
        public double LengthCm { get; set; } = 60.0;

        [JsonPropertyName("thickness_cm")]
        public double ThicknessCm { get; set; } = 1.0;

        [JsonPropertyName("bevel_mm")]
        public double BevelMm { get; set; } = 1.0;

        [JsonPropertyName("category")]
        public string? Category { get; set; } = "Porcelanato";

        [JsonPropertyName("material")]
        public string? Material { get; set; } = "Porcelanato Pulido";

        [JsonPropertyName("cost_m2")]
        public double CostM2 { get; set; } = 25.0;

        public FamilySymbol? FamilySymbol { get; set; }
    }

    public class TilesDatabase
    {
        [JsonPropertyName("tiles")]
        public List<TileType>? Tiles { get; set; }
    }

    public enum FloorPatternType
    {
        OrthogonalGrid,   // Retícula Alineada / Cuadrícula
        RunningBond50,    // Traba al 50% / Matajuntas
        ThirdBond33,      // Traba a 1/3 (Tercio)
        Herringbone90,    // Espina de Pez 90°
        Herringbone45,    // Espina de Pez 45°
        Diagonal45,       // Diagonal 45°
        Basketweave       // Modular / Trenzado
    }

    public enum TileRole
    {
        Full,       // Baldosa completa sin corte
        CutEdge,    // Baldosa perimetral cortada
        Corner,     // Baldosa de esquina cortada
        Trimmed     // Baldosa con recorte especial (por vanos o huecos)
    }

    public enum HostElementType
    {
        Floor,
        Wall
    }

    public class HostLayerDetection
    {
        public bool HasRevoque { get; set; }
        public double SuggestedRevoqueCm { get; set; } = 1.5;
        public string SuggestedRevoqueMaterial { get; set; } = "";
        public ElementId SuggestedRevoqueMaterialId { get; set; } = ElementId.InvalidElementId;

        public bool HasPega { get; set; }
        public double SuggestedPegaMm { get; set; } = 5.0;
        public string SuggestedPegaMaterial { get; set; } = "";
        public ElementId SuggestedPegaMaterialId { get; set; } = ElementId.InvalidElementId;

        public bool HasFinish { get; set; }
        public double SuggestedFinishCm { get; set; } = 1.0;
        public string SuggestedFinishMaterial { get; set; } = "";
        public ElementId SuggestedFinishMaterialId { get; set; } = ElementId.InvalidElementId;
    }

    public class TilingHostInfo
    {
        public ElementId HostId { get; set; } = ElementId.InvalidElementId;
        public string Name { get; set; } = string.Empty;
        public HostElementType HostType { get; set; } = HostElementType.Floor;
        public Level? Level { get; set; }
        public double ElevationFt { get; set; }
        public double AreaSqM { get; set; }
        public double PerimeterM { get; set; }

        public double TotalHostThicknessFt { get; set; } = 0.328084; // ~10cm default
        public double TotalHostThicknessCm => TotalHostThicknessFt * 30.48;

        public double WallHeightFt { get; set; }
        public double WallLengthFt { get; set; }

        public Reference? HostFaceReference { get; set; }

        // Capas detectadas en la estructura compuesta
        public HostLayerDetection DetectedLayers { get; set; } = new HostLayerDetection();

        // Sistema de Coordenadas Local 3D
        public XYZ Origin3D { get; set; } = XYZ.Zero;
        public XYZ BasisU { get; set; } = XYZ.BasisX; // Dirección longitudinal (U)
        public XYZ BasisV { get; set; } = XYZ.BasisY; // Dirección transversal o altura (V)
        public XYZ Normal3D { get; set; } = XYZ.BasisZ; // Normal hacia el acabado

        // Límites en plano local U-V (pies)
        public double MinU { get; set; }
        public double MaxU { get; set; }
        public double MinV { get; set; }
        public double MaxV { get; set; }

        // Polígono exterior e interiores (vanos/puertas/ventanas) en 2D (U, V)
        public List<XYZ> OuterBoundaryUV { get; set; } = new List<XYZ>();
        public List<List<XYZ>> InnerBoundariesUV { get; set; } = new List<List<XYZ>>();

        // Polígono interior con setback de junta perimetral (área de baldosas y cama de pega)
        public List<XYZ> TilingBoundaryUV { get; set; } = new List<XYZ>();
        public List<XYZ> TilingBoundary3D { get; set; } = new List<XYZ>();

        // Vértices 3D
        public List<XYZ> OuterBoundary3D { get; set; } = new List<XYZ>();
        public List<Curve> OuterCurves3D { get; set; } = new List<Curve>();

        // Geometría Revit 2027
        public PlanarFace? TopPlanarFace { get; set; }
        public CurveLoop? OuterCurveLoop { get; set; }
        public List<CurveLoop> InnerCurveLoops { get; set; } = new List<CurveLoop>();
        public Transform? LocalTransform { get; set; }

        public XYZ LocalToWorld(XYZ uvPoint, double normalOffsetFt = 0.0)
        {
            return Origin3D + (BasisU * uvPoint.X) + (BasisV * uvPoint.Y) + (Normal3D * normalOffsetFt);
        }
    }

    public class TileLayoutItem
    {
        public int IndexX { get; set; }
        public int IndexY { get; set; }

        // Posición en plano local U-V
        public double CenterU_Ft { get; set; }
        public double CenterV_Ft { get; set; }
        public XYZ Center3D { get; set; } = XYZ.Zero;
        public XYZ Corner3D { get; set; } = XYZ.Zero;
        public XYZ Normal3D { get; set; } = XYZ.BasisZ;
        public XYZ DirectionU3D { get; set; } = XYZ.BasisX;
        public XYZ DirectionV3D { get; set; } = XYZ.BasisY;

        public double Length_Ft { get; set; }
        public double Width_Ft { get; set; }
        public double Thickness_Ft { get; set; }
        public double RotationRad { get; set; }

        public bool IsCut { get; set; }
        public double CutLat_Ft { get; set; } = 0.0;
        public double CutTop_Ft { get; set; } = 0.0;
        public double ActualLength_Ft { get; set; }
        public double ActualWidth_Ft { get; set; }
        public TileRole Role { get; set; } = TileRole.Full;

        // Vértices 2D en plano U-V y 3D en el espacio
        public List<XYZ> PolygonVerticesUV { get; set; } = new List<XYZ>();
        public List<XYZ> PolygonVertices3D { get; set; } = new List<XYZ>();

        public double ActualAreaSqM { get; set; }
        public Reference? HostFaceRef { get; set; }
        public Solid? TileSolid { get; set; }
    }

    public class JointLayoutItem
    {
        public bool IsPerimeter { get; set; }
        public bool IsVertical { get; set; }
        public XYZ Center3D { get; set; } = XYZ.Zero;
        public XYZ Corner3D { get; set; } = XYZ.Zero;
        public XYZ StartPoint3D { get; set; } = XYZ.Zero;
        public XYZ EndPoint3D { get; set; } = XYZ.Zero;
        public XYZ DirectionU3D { get; set; } = XYZ.BasisX;
        public XYZ Normal3D { get; set; } = XYZ.BasisZ;
        public double RotationRad { get; set; }
        public double LengthFt { get; set; }
        public double WidthFt { get; set; }
        public double DepthFt { get; set; }
        public string MaterialName { get; set; } = "Fragua";
        public ElementId MaterialId { get; set; } = ElementId.InvalidElementId;
        public List<XYZ> PolygonVertices3D { get; set; } = new List<XYZ>();
        public Reference? HostFaceRef { get; set; }
    }

    public class SkirtingItem
    {
        public XYZ StartPoint { get; set; } = XYZ.Zero;
        public XYZ EndPoint { get; set; } = XYZ.Zero;
        public XYZ Corner3D { get; set; } = XYZ.Zero;
        public XYZ Direction3D { get; set; } = XYZ.BasisX;
        public XYZ Normal3D { get; set; } = XYZ.BasisZ;
        public double HeightFt { get; set; }
        public double ThicknessFt { get; set; }
        public double BevelFt { get; set; }
        public double BaseElevationFt { get; set; }
        public double LengthFt => StartPoint.DistanceTo(EndPoint);
        public string MaterialName { get; set; } = "Zócalo";
        public ElementId MaterialId { get; set; } = ElementId.InvalidElementId;
        public Reference? HostFaceRef { get; set; }
    }
}
