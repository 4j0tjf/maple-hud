using System;
using System.IO;
using SkiaSharp;

namespace MapleHud.Core.Render
{
    /// <summary>
    /// HUD 글꼴. 앱과 함께 배포하는 Pretendard를 먼저 쓰고, 없으면 맑은 고딕, 그것도 없으면 기본 글꼴.
    /// 파일에서 읽은 글꼴은 메모리 매핑되어 개인 메모리를 거의 쓰지 않는다.
    /// </summary>
    public sealed class FontSet : IDisposable
    {
        public SKTypeface Regular { get; private set; }
        public SKTypeface SemiBold { get; private set; }
        public SKTypeface Bold { get; private set; }
        public SKTypeface ExtraBold { get; private set; }

        public SKTypeface ForWeight(int weight) =>
            weight >= 800 ? ExtraBold : weight >= 700 ? Bold : weight >= 600 ? SemiBold : Regular;

        public static FontSet Load(string fontsDir)
        {
            var set = new FontSet();
            if (fontsDir != null && File.Exists(Path.Combine(fontsDir, "Pretendard-Regular.otf")))
            {
                set.Regular = FromFile(Path.Combine(fontsDir, "Pretendard-Regular.otf"));
                set.SemiBold = FromFile(Path.Combine(fontsDir, "Pretendard-SemiBold.otf")) ?? set.Regular;
                set.Bold = FromFile(Path.Combine(fontsDir, "Pretendard-Bold.otf")) ?? set.SemiBold;
                set.ExtraBold = FromFile(Path.Combine(fontsDir, "Pretendard-ExtraBold.otf")) ?? set.Bold;
            }
            if (set.Regular == null)
            {
                var winFonts = Environment.GetFolderPath(Environment.SpecialFolder.Fonts);
                set.Regular = FromFile(Path.Combine(winFonts, "malgun.ttf"));
                set.SemiBold = set.Regular;
                set.Bold = FromFile(Path.Combine(winFonts, "malgunbd.ttf")) ?? set.Regular;
                set.ExtraBold = set.Bold;
            }
            if (set.Regular == null)
            {
                set.Regular = SKTypeface.Default;
                set.SemiBold = set.Bold = set.ExtraBold = SKTypeface.FromFamilyName(null, SKFontStyle.Bold) ?? SKTypeface.Default;
            }
            return set;
        }

        private static SKTypeface FromFile(string path)
        {
            try { return File.Exists(path) ? SKTypeface.FromFile(path) : null; }
            catch (Exception) { return null; }
        }

        public void Dispose()
        {
            foreach (var tf in new[] { Regular, SemiBold, Bold, ExtraBold })
            {
                if (tf != null && tf != SKTypeface.Default) tf.Dispose();
            }
        }
    }
}
