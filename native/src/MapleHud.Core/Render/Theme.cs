using SkiaSharp;

namespace MapleHud.Core.Render
{
    /// <summary>웹 버전 hud.css와 같은 색</summary>
    public sealed class Theme
    {
        public SKColor Accent;
        public SKColor Text = new SKColor(244, 246, 251);
        public SKColor Text2 = new SKColor(232, 238, 252, 184);   // .72
        public SKColor Text3 = new SKColor(232, 238, 252, 122);   // .48
        public SKColor Line = new SKColor(255, 255, 255, 23);     // .09
        public SKColor Done = new SKColor(0x6f, 0xe3, 0xa5);
        public SKColor Prog = new SKColor(0x7c, 0xc7, 0xff);
        public SKColor Danger = new SKColor(0xff, 0x7a, 0x7a);
        public SKColor Dark = new SKColor(0x1a, 0x12, 0x06);
        public SKColor DoneDark = new SKColor(0x0e, 0x1a, 0x14);
        public byte PanelAlpha;

        public Theme(string accentHex, int opacityPercent)
        {
            Accent = SKColor.TryParse(accentHex, out var c) ? c : new SKColor(0xff, 0xb5, 0x47);
            PanelAlpha = (byte)(System.Math.Max(0, System.Math.Min(100, opacityPercent)) * 255 / 100);
        }

        public static SKColor A(SKColor c, double alpha) => c.WithAlpha((byte)System.Math.Round(255 * alpha));
        public static SKColor White(double alpha) => new SKColor(255, 255, 255, (byte)System.Math.Round(255 * alpha));

        public static SKColor Difficulty(string d, out SKColor text)
        {
            text = new SKColor(0x11, 0x13, 0x1a);
            switch (d)
            {
                case "이지": return new SKColor(0xa9, 0xb4, 0xc4);
                case "노멀": return new SKColor(0x61, 0xd0, 0xb0);
                case "하드": return new SKColor(0xff, 0x7a, 0x7a);
                case "카오스": return new SKColor(0xb4, 0x8c, 0xff);
                case "익스트림":
                    text = SKColors.White;
                    return new SKColor(0xff, 0x4d, 0x79);
                default: return new SKColor(0x9a, 0xa4, 0xb2);
            }
        }
    }
}
