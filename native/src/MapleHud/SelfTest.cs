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
    /// </summary>
    internal static class SelfTest
    {
        public static int Run(string outPath, int seconds)
        {
            int code = 1;
            try
            {
                Application.EnableVisualStyles();
                var app = new HudApp();
                var timer = new Timer { Interval = Math.Max(1, seconds) * 1000 };
                timer.Tick += (s, e) =>
                {
                    timer.Stop();
                    try
                    {
                        var report = app.SelfTestReport(outPath);
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
