using System;
using System.IO;
using System.Runtime.CompilerServices;
using System.Threading;
using System.Windows.Forms;

namespace MapleHud
{
    internal static class Program
    {
        private const string MutexName = "MapleSchedulerHUD.SingleInstance";
        private const string ShowEventName = "MapleSchedulerHUD.Show";

        private static bool _selfTest;

        [STAThread]
        private static int Main(string[] args)
        {
            _selfTest = args.Length >= 1 && args[0] == "--selftest";
            // 시작하다 실패하면 말없이 꺼지지 않고 이유를 보여준다
            AppDomain.CurrentDomain.UnhandledException += (s, e) => Fatal(e.ExceptionObject as Exception ?? new Exception("unknown"));
            try
            {
                return Run(args);
            }
            catch (Exception e)
            {
                Fatal(e);
                return 1;
            }
        }

        // 필요한 dll이 없으면 이 메서드를 부를 때 실패하므로 Main과 나눠 둔다
        [MethodImpl(MethodImplOptions.NoInlining)]
        private static int Run(string[] args)
        {
            if (args.Length >= 2 && args[0] == "--selftest")
                return SelfTest.Run(args[1], args.Length >= 3 ? int.Parse(args[2]) : 5, args.Length >= 4 ? args[3] : null);

            using (var mutex = new Mutex(true, MutexName, out bool first))
            {
                if (!first)
                {
                    // 이미 실행 중이면 그 창을 앞으로 가져오고 끝낸다 (실행 중인 쪽이 트레이 알림을 띄운다)
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

                using (var show = new EventWaitHandle(false, EventResetMode.AutoReset, ShowEventName))
                {
                    var app = new HudApp();
                    var ui = SynchronizationContext.Current;
                    var wait = ThreadPool.RegisterWaitForSingleObject(show, (state, timeout) => ui.Post(_ => app.ShowAlreadyRunning(), null), null, -1, false);
                    Application.Run(app);
                    wait.Unregister(null);
                }
            }
            return 0;
        }

        private static bool MissingFiles(Exception e)
        {
            for (var x = e; x != null; x = x.InnerException)
            {
                if (x is FileNotFoundException || x is FileLoadException || x is DllNotFoundException || x is BadImageFormatException ||
                    x is TypeLoadException || x is MissingMemberException)
                    return true;
            }
            return false;
        }

        private static int _fatalShown;

        private static void Fatal(Exception e)
        {
            if (Interlocked.Exchange(ref _fatalShown, 1) == 1) return;
            string log = null;
            try
            {
                // HudApp(MapleHud.Core 사용)에 기대지 않고 직접 남긴다
                var home = Environment.GetEnvironmentVariable("MAPLEHUD_HOME");
                var dir = !string.IsNullOrEmpty(home) ? Path.Combine(home, "cache")
                    : Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "MapleSchedulerHUD");
                Directory.CreateDirectory(dir);
                log = Path.Combine(dir, "error.log");
                File.AppendAllText(log, DateTime.Now.ToString("s") + " " + e + Environment.NewLine);
            }
            catch (Exception)
            {
                // 로그를 못 남겨도 안내는 띄운다
            }
            // 자체 점검은 결과 파일로 확인하므로 메시지 상자로 멈추지 않는다
            if (_selfTest || !Environment.UserInteractive) return;
            var text = MissingFiles(e)
                ? "실행에 필요한 파일이 없거나 이전 버전 파일과 섞였습니다.\n\n" +
                  "압축 파일 안에서 바로 실행했다면 먼저 압축을 풀어 주세요. 업데이트 중이었다면 앱을 종료한 뒤 " +
                  "MapleSchedulerHUD 폴더를 통째로 새로 받은 폴더로 바꿔 주세요.\n\n"
                : "오류가 나서 Maple Scheduler HUD를 종료합니다.\n\n";
            text += e.GetType().Name + ": " + e.Message + (log != null ? "\n\n자세한 내용: " + log : "");
            try
            {
                MessageBox.Show(text, "Maple Scheduler HUD", MessageBoxButtons.OK, MessageBoxIcon.Error);
            }
            catch (Exception)
            {
                // 메시지 상자도 못 띄우는 환경
            }
        }
    }
}
