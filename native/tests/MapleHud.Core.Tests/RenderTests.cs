using System;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Threading.Tasks;
using MapleHud.Core.Render;
using SkiaSharp;
using Xunit;
using Xunit.Abstractions;

namespace MapleHud.Core.Tests
{
    public class RenderTests
    {
        private readonly ITestOutputHelper _out;
        public RenderTests(ITestOutputHelper output) { _out = output; }

        internal static string FontsDir()
        {
            var dir = AppContext.BaseDirectory;
            while (dir != null && !Directory.Exists(Path.Combine(dir, "fonts"))) dir = Path.GetDirectoryName(dir);
            return dir == null ? null : Path.Combine(dir, "fonts");
        }

        // MAPLEHUD_PREVIEW_DIR가 있으면 미리보기 PNG를 남긴다 (사람이 눈으로 확인할 때)
        private static void Save(SKBitmap bmp, string name)
        {
            var dir = Environment.GetEnvironmentVariable("MAPLEHUD_PREVIEW_DIR");
            if (string.IsNullOrEmpty(dir)) return;
            Directory.CreateDirectory(dir);
            using (var img = SKImage.FromBitmap(bmp))
            using (var data = img.Encode(SKEncodedImageFormat.Png, 100))
            using (var f = File.Create(Path.Combine(dir, name + ".png"))) data.SaveTo(f);
        }

        internal static (HudController ctl, SyncEngine engine) Demo(HudSettings s)
        {
            var engine = new SyncEngine(s, JsonStore.InMemory(), _ => null, () => T.Now);
            engine.SyncAsync(false).GetAwaiter().GetResult();
            return (new HudController(engine, JsonStore.InMemory(), s, () => T.Now), engine);
        }

        private static SKBitmap Render(HudRenderer r, RenderInput input, out HudLayout layout, float scale = 1)
        {
            layout = r.Measure(input);
            var bmp = new SKBitmap((int)Math.Ceiling(layout.Width * scale), (int)Math.Ceiling(layout.Height * scale), SKColorType.Bgra8888, SKAlphaType.Premul);
            using (var canvas = new SKCanvas(bmp))
            {
                canvas.Clear(SKColors.Transparent);
                canvas.Scale(scale);
                layout = r.Draw(canvas, input);
            }
            return bmp;
        }

        [Fact]
        public void 데모_화면을_그리고_클릭_영역을_만든다()
        {
            var s = new HudSettings();
            var (ctl, _) = Demo(s);
            using (var fonts = FontSet.Load(FontsDir()))
            using (var r = new HudRenderer(fonts))
            {
                var input = new RenderInput { View = ctl.Build(), Settings = s, Now = T.Now, MaxHeight = 1000 };
                var sw = Stopwatch.StartNew();
                using (var bmp = Render(r, input, out var layout))
                {
                    sw.Stop();
                    _out.WriteLine($"panel {layout.Width}x{layout.Height}, content {layout.ContentHeight}, {sw.ElapsedMilliseconds}ms");
                    Save(bmp, "demo");
                    Assert.Equal(428, layout.Width);
                    Assert.True(layout.Height <= 1000);
                    Assert.Contains(layout.Regions, h => h.Action == "refresh");
                    Assert.Contains(layout.Regions, h => h.Action == "settings");
                    Assert.Equal(3, layout.Regions.Count(h => h.Action == "edit"));
                    // 패널 바깥 모서리는 투명, 안쪽은 반투명
                    Assert.Equal(0, bmp.GetPixel(0, 0).Alpha);
                    Assert.InRange(bmp.GetPixel(200, 6).Alpha, 150, 200);
                }

                // 매초 그릴 때: 카드 영역은 캐시하고 시계만 다시 그린다
                input.BodyVersion = 1;
                Render(r, input, out var first).Dispose();
                var sw2 = Stopwatch.StartNew();
                for (int i = 0; i < 20; i++)
                {
                    input.Now += 1000;
                    Render(r, input, out var l2).Dispose();
                    Assert.Equal(first.Regions.Count, l2.Regions.Count);
                }
                _out.WriteLine($"cached frame avg {sw2.ElapsedMilliseconds / 20.0}ms");
                input.BodyVersion = -1;
                var sw3 = Stopwatch.StartNew();
                for (int i = 0; i < 20; i++) Render(r, input, out _).Dispose();
                _out.WriteLine($"full frame avg {sw3.ElapsedMilliseconds / 20.0}ms");
            }
        }

        [Fact]
        public void 처음_불러오는_중에는_진행_상황과_저장_알림을_보여준다()
        {
            var s = new HudSettings();
            using (var fonts = FontSet.Load(FontsDir()))
            using (var r = new HudRenderer(fonts))
            {
                var none = r.Measure(new RenderInput { View = new HudView(), Settings = s, Now = T.Now });
                // 캐릭터가 없다는 문구도 패널 안에 들어간다
                Assert.Equal(none.CardsTop + 26 + 14, none.Height);

                var view = new HudView
                {
                    Syncing = true,
                    Progress = "캐릭터 확인 중 1/2",
                    Flash = new Notice("success", "설정을 저장했습니다")
                };
                using (var bmp = Render(r, new RenderInput { View = view, Settings = s, Now = T.Now }, out var layout))
                {
                    Save(bmp, "loading");
                    // 저장 알림 한 줄과 진행 상황 줄만큼 커진다
                    Assert.True(layout.CardsTop > none.CardsTop + 30);
                    Assert.Equal(layout.CardsTop + 26 + 18 + 14, layout.Height);
                }
            }
        }

        [Fact]
        public void 편집_버튼을_누르면_편집_화면이_된다()
        {
            var s = new HudSettings();
            var (ctl, _) = Demo(s);
            using (var fonts = FontSet.Load(FontsDir()))
            using (var r = new HudRenderer(fonts))
            {
                var first = ctl.Build().Cards[0].Key;
                ctl.ToggleEdit(first);
                ctl.EditSelection(first, "group", "bossMonthly");
                ctl.EditSelection(first, "item", "daily:우르스");
                var input = new RenderInput { View = ctl.Build(), Settings = s, Now = T.Now, MaxHeight = 1400, HoverCard = first };
                using (var bmp = Render(r, input, out var layout))
                {
                    Save(bmp, "editor");
                    Assert.Contains(layout.Regions, h => h.Action == "group" && h.Id == "bossMonthly");
                    Assert.Contains(layout.Regions, h => h.Action == "item" && h.Id == "daily:몬스터파크");
                    Assert.Contains(layout.Regions, h => h.Action == "reset");
                    // 영역 가운데를 누르면 같은 영역이 잡힌다
                    var pick = layout.Regions.First(h => h.Action == "item" && h.Id == "daily:몬스터파크");
                    Assert.Same(pick, layout.HitTest(pick.Rect.MidX, pick.Rect.MidY));
                }
            }
        }

        [Fact]
        public void 여러_열_왼쪽_아래_배치와_스크롤()
        {
            var s = new HudSettings { Columns = 2, CardWidth = 330, HideDone = true, ShowSeconds = false, Opacity = 40, Accent = "#7cc7ff" };
            var (ctl, _) = Demo(s);
            using (var fonts = FontSet.Load(FontsDir()))
            using (var r = new HudRenderer(fonts))
            {
                var input = new RenderInput { View = ctl.Build(), Settings = s, Now = T.Now, MaxHeight = 420, Scroll = 30 };
                using (var bmp = Render(r, input, out var layout, 1.25f))
                {
                    Save(bmp, "columns");
                    Assert.Equal(330 * 2 + 10 + 28, layout.Width);
                    Assert.True(layout.MaxScroll > 0);
                    Assert.Equal(420, layout.Height, 1);
                }
            }
        }
    }
}
