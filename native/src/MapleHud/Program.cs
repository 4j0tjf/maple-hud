using System;
using System.IO;
using System.Threading;
using System.Windows.Forms;

namespace MapleHud
{
    internal static class Program
    {
        private const string MutexName = "MapleSchedulerHUD.SingleInstance";
        private const string ShowEventName = "MapleSchedulerHUD.Show";

        [STAThread]
        private static int Main(string[] args)
        {
            if (args.Length >= 2 && args[0] == "--selftest")
                return SelfTest.Run(args[1], args.Length >= 3 ? int.Parse(args[2]) : 5, args.Length >= 4 ? args[3] : null);

            using (var mutex = new Mutex(true, MutexName, out bool first))
            {
                if (!first)
                {
                    // 이미 실행 중이면 그 창을 앞으로 가져오고 끝낸다
                    try
                    {
                        using (var ev = EventWaitHandle.OpenExisting(ShowEventName)) ev.Set();
                    }
                    catch (WaitHandleCannotBeOpenedException)
                    {
                        // 먼저 뜬 쪽이 아직 준비 중
                    }
                    return 0;
                }

                Application.EnableVisualStyles();
                Application.SetCompatibleTextRenderingDefault(false);
                Application.SetUnhandledExceptionMode(UnhandledExceptionMode.CatchException);
                Application.ThreadException += (s, e) => HudApp.Log(e.Exception);
                AppDomain.CurrentDomain.UnhandledException += (s, e) => HudApp.Log(e.ExceptionObject as Exception ?? new Exception("unknown"));

                using (var show = new EventWaitHandle(false, EventResetMode.AutoReset, ShowEventName))
                {
                    var app = new HudApp();
                    var ui = SynchronizationContext.Current;
                    var wait = ThreadPool.RegisterWaitForSingleObject(show, (state, timeout) => ui.Post(_ => app.BringToFront(), null), null, -1, false);
                    Application.Run(app);
                    wait.Unregister(null);
                }
            }
            return 0;
        }
    }
}
