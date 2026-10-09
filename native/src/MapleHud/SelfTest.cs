using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Text.Json;
using System.Windows.Forms;
using MapleHud.Core;

namespace MapleHud
{
    /// <summary>
    /// CI에서 실제 Windows 경로(레이어드 창, 트레이, 글꼴, Skia 네이티브 로드, 타이머)를 확인한다.
    /// 앱을 데모 모드로 몇 초 띄운 뒤 화면을 PNG로 저장하고 메모리 사용량을 출력한다.
    ///   set MAPLEHUD_HOME=%TEMP%\maplehud-selftest
    ///   MapleSchedulerHUD.exe --selftest out.png [초]
    /// settings를 붙이면 설정 창에 API 키·캐릭터를 넣고 저장 버튼을 눌러서
    /// 저장·창 닫힘·동기화 시작·저장 알림을 확인한다 (접속할 수 없는 주소를 써서 실제 API는 부르지 않는다).
    ///   MapleSchedulerHUD.exe --selftest out.png 6 settings
    /// cached를 붙이면 지난번에 받아 둔 캐릭터(스케줄러·이미지 캐시)가 있는 상태로 다시 켰을 때를 확인한다.
    ///   MapleSchedulerHUD.exe --selftest out.png 6 cached
    /// drag를 붙이면 설정 창을 연 채로 HUD를 끌어 옮기고 창을 닫아서, 놓은 자리가 저장·유지되는지 확인한다.
    ///   MapleSchedulerHUD.exe --selftest out.png 6 drag
    /// </summary>
    internal static class SelfTest
    {
        // CI 로그로 옮기기 쉽게 어두운 배경에 합쳐 작은 JPEG로도 남긴다
        private static void SavePreviewJpeg(string pngPath, string jpgPath)
        {
            using (var src = SkiaSharp.SKBitmap.Decode(pngPath))
            using (var surface = SkiaSharp.SKSurface.Create(new SkiaSharp.SKImageInfo(src.Width * 3 / 4, src.Height * 3 / 4)))
            {
                surface.Canvas.Clear(new SkiaSharp.SKColor(0x40, 0x58, 0x78));
                surface.Canvas.Scale(0.75f);
                using (var image = SkiaSharp.SKImage.FromBitmap(src))
                    surface.Canvas.DrawImage(image, 0, 0, new SkiaSharp.SKSamplingOptions(SkiaSharp.SKFilterMode.Linear, SkiaSharp.SKMipmapMode.None), null);
                using (var img = surface.Snapshot())
                using (var data = img.Encode(SkiaSharp.SKEncodedImageFormat.Jpeg, 72))
                    File.WriteAllBytes(jpgPath, data.ToArray());
            }
        }

        private const string TestKey = "selftest-key";
        private const string TestBase = "http://127.0.0.1:9";   // 접속할 수 없는 주소 (실제 API를 부르지 않게)

        // 지난번 실행에서 캐릭터 하나를 받아 둔 것처럼 설정·캐시·캐릭터 이미지 파일을 만든다
        private static void SeedCachedCharacter()
        {
            var home = Environment.GetEnvironmentVariable("MAPLEHUD_HOME");
            if (string.IsNullOrEmpty(home)) throw new InvalidOperationException("cached selftest needs MAPLEHUD_HOME");
            var now = KstTime.NowMs();
            var demo = DemoData.Build(now)[0];
            const string ocid = "selftest-ocid";
            const string image = TestBase + "/avatar.png";

            var settings = new HudSettings { ApiKey = TestKey, Characters = demo.Name, ApiBase = TestBase, ShowAvatar = true };
            settings.Save(Path.Combine(home, "data", "settings.json"));

            var store = new JsonStore(Path.Combine(home, "data", "store.json"));
            store.Set("targets", new SyncEngine.LastTargets
            {
                Sig = SyncEngine.TargetsSig(settings),
                List = new List<SyncEngine.Target> { new SyncEngine.Target { Name = demo.Name, Ocid = ocid, World = demo.World, Cls = demo.Cls, Level = demo.Level } }
            });
            store.SetCached("ocid:" + demo.Name, JsonSerializer.Serialize(ocid), now);
            store.SetCached("sched:" + ocid, demo.Body, now);
            store.SetCached("basic:" + ocid, JsonSerializer.Serialize(new Dictionary<string, object>
            {
                ["character_name"] = demo.Name,
                ["character_level"] = demo.Level,
                ["character_exp_rate"] = "61.20",
                ["character_image"] = image
            }), now);
            store.Flush();

            var dir = Path.Combine(home, "cache", "avatars");
            Directory.CreateDirectory(dir);
            using (var surface = SkiaSharp.SKSurface.Create(new SkiaSharp.SKImageInfo(96, 96)))
            {
                surface.Canvas.Clear(new SkiaSharp.SKColor(0x7c, 0xc7, 0xff));
                using (var img = surface.Snapshot())
                using (var data = img.Encode(SkiaSharp.SKEncodedImageFormat.Png, 100))
                    File.WriteAllBytes(Path.Combine(dir, AvatarCache.Hash(image) + ".png"), data.ToArray());
            }
        }

        public static int Run(string outPath, int seconds, string scenario = null)
        {
            int code = 1;
            try
            {
                Application.EnableVisualStyles();
                if (scenario == "cached")
                {
                    SeedCachedCharacter();
                    seconds = 3;
                }
                var app = new HudApp();
                string saveReport = null;
                var dropped = System.Drawing.Rectangle.Empty;
                bool topWhileOpen = false;
                if (scenario == "drag")
                {
                    var drag = new Timer { Interval = 1500 };
                    drag.Tick += (s, e) =>
                    {
                        drag.Stop();
                        var form = app.OpenSettingsForTest();
                        topWhileOpen = app.HudTopMost;
                        dropped = app.DragForTest(-200, 0);
                        form.Close();
                    };
                    drag.Start();
                    seconds = 3;
                }
                if (scenario == "settings")
                {
                    var save = new Timer { Interval = 1500 };
                    save.Tick += (s, e) =>
                    {
                        save.Stop();
                        var form = app.OpenSettingsForTest();
                        form.FillApiForTest(TestKey, "테스트캐릭터", TestBase);
                        form.SaveButton.PerformClick();
                    };
                    save.Start();
                    // 저장 알림이 떠 있는 동안 확인한다
                    seconds = 4;
                }
                var timer = new Timer { Interval = Math.Max(1, seconds) * 1000 };
                timer.Tick += (s, e) =>
                {
                    timer.Stop();
                    try
                    {
                        if (scenario == "settings") saveReport = app.SettingsSaveReport(TestKey);
                        if (scenario == "drag") saveReport = app.DragReport(dropped, topWhileOpen);
                        if (saveReport != null && !saveReport.StartsWith("ok")) throw new InvalidOperationException(saveReport);
                        var report = app.SelfTestReport(outPath) + (saveReport != null ? " | " + saveReport : "");
                        // 캐시해 둔 캐릭터가 이미지와 함께 보여야 한다
                        if (scenario == "cached" && !app.ShowsCachedCharacter) throw new InvalidOperationException(report);
                        SavePreviewJpeg(outPath, outPath + ".jpg");
                        GC.Collect();
                        GC.WaitForPendingFinalizers();
                        var p = Process.GetCurrentProcess();
                        p.Refresh();
                        File.WriteAllText(outPath + ".txt", "ok " + report +
                            " privateMB=" + (p.PrivateMemorySize64 >> 20) + " workingSetMB=" + (p.WorkingSet64 >> 20) +
                            " managedMB=" + (GC.GetTotalMemory(false) >> 20) + " cpuMs=" + (long)p.TotalProcessorTime.TotalMilliseconds);
                        code = 0;
                    }
                    catch (Exception ex)
                    {
                        File.WriteAllText(outPath + ".txt", "fail " + ex);
                    }
                    app.ExitThread();
                };
                timer.Start();
                Application.Run(app);
            }
            catch (Exception e)
            {
                File.WriteAllText(outPath + ".txt", "fail " + e);
            }
            return code;
        }
    }
}
