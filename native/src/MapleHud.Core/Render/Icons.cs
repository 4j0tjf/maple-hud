using SkiaSharp;

namespace MapleHud.Core.Render
{
    /// <summary>웹 버전과 같은 SVG 아이콘 (16x16 또는 24x24 좌표)</summary>
    internal static class Icons
    {
        public static readonly SKPath Check = SKPath.ParseSvgPathData("M3.6 8.4l2.9 2.9 5.9-6.6");
        public static readonly SKPath WarnTriangle = SKPath.ParseSvgPathData("M8 1.8l6.6 11.7H1.4z");
        public static readonly SKPath WarnMark = SKPath.ParseSvgPathData("M8 6.2v3.4M8 11.6v.1");
        public static readonly SKPath Pencil = SKPath.ParseSvgPathData("M10.6 2.6l2.8 2.8-7.6 7.6-3.4.6.6-3.4z");
        public static readonly SKPath PencilLine = SKPath.ParseSvgPathData("M9.2 4l2.8 2.8");
        // 24x24
        public static readonly SKPath RefreshArc = SKPath.ParseSvgPathData("M20 12a8 8 0 1 1-2.34-5.66");
        public static readonly SKPath RefreshHead = SKPath.ParseSvgPathData("M20 4v5h-5");
        public static readonly SKPath GearRays = SKPath.ParseSvgPathData(
            "M12 2.5v3M12 18.5v3M2.5 12h3M18.5 12h3M5.3 5.3l2.1 2.1M16.6 16.6l2.1 2.1M5.3 18.7l2.1-2.1M16.6 7.4l2.1-2.1");
        public static readonly SKPath Leaf = SKPath.ParseSvgPathData(
            "M12 1L13.6 4.6L15.6 3.8L15 8.4L18.2 5.6L18.8 7.4L22 6.8L20.8 10.2L22.4 11.2L17.2 15.4L17.8 17.2L12.8 16.6" +
            "L12.9 22.5L11.1 22.5L11.2 16.6L6.2 17.2L6.8 15.4L1.6 11.2L3.2 10.2L2 6.8L5.2 7.4L5.8 5.6L9 8.4L8.4 3.8L10.4 4.6Z");
        public static readonly SKPath Star = SKPath.ParseSvgPathData(
            "M8 1.2l2 4.3 4.7.5-3.5 3.2 1 4.6L8 11.4l-4.2 2.4 1-4.6L1.3 6l4.7-.5z");
    }
}
