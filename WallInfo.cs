using System;
using Autodesk.Revit.DB;

namespace RevitToSupabasePlugin
{
    public class WallInfo
    {
        public ElementId WallId { get; set; } = ElementId.InvalidElementId;
        public string Name { get; set; } = "Muro";
        public double LengthFt { get; set; } = 10.0;
        public double HeightFt { get; set; } = 10.0;
        public double WidthFt { get; set; } = 0.5;
        public XYZ StartPoint { get; set; } = XYZ.Zero;
        public XYZ EndPoint { get; set; } = XYZ.BasisX * 10.0;
        public XYZ Direction { get; set; } = XYZ.BasisX;
        public XYZ Normal { get; set; } = XYZ.BasisY;
        public double Angle { get; set; } = 0.0;
    }
}
