using System;
using System.Diagnostics;
using System.IO;
using System.Windows.Forms;

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

        public static int Run(string outPath, int seconds, string scenario = null)
        {
            int code = 1;
            try
            {
                Application.EnableVisualStyles();
                var app = new HudApp();
                string saveReport = null;
                if (scenario == "settings")
                {
                    var save = new Timer { Interval = 1500 };
                    save.Tick += (s, e) =>
                    {
                        save.Stop();
                        var form = app.OpenSettingsForTest();
                        form.FillApiForTest(TestKey, "테스트캐릭터", "http://127.0.0.1:9");
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
                        if (scenario == "settings")
                        {
                            saveReport = app.SettingsSaveReport(TestKey);
                            if (!saveReport.StartsWith("ok")) throw new InvalidOperationException(saveReport);
                        }
                        var report = app.SelfTestReport(outPath) + (saveReport != null ? " | " + saveReport : "");
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
