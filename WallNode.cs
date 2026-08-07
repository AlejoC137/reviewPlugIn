using Autodesk.Revit.DB;

namespace RevitToSupabasePlugin
{
    public enum WallNodeType
    {
        None,
        L_Corner,
        T_Junction
    }

    public class WallNodeInfo
    {
        public WallNodeType NodeType { get; set; } = WallNodeType.None;
        public XYZ Point { get; set; } = XYZ.Zero;

        public Wall WallA { get; set; } = null!;
        public Wall WallB { get; set; } = null!;

        /// <summary>
        /// Extremo de WallA (0 = Start, 1 = End, -1 = Interior Segment)
        /// </summary>
        public int EndIndexA { get; set; } = -1;

        /// <summary>
        /// Extremo de WallB (0 = Start, 1 = End, -1 = Interior Segment)
        /// </summary>
        public int EndIndexB { get; set; } = -1;
    }
}
