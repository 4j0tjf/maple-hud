using Microsoft.Win32;

namespace MapleHud
{
    /// <summary>Windows 시작 시 자동 실행 (현재 사용자 Run 레지스트리)</summary>
    internal static class AutoStart
    {
        private const string RunKey = @"Software\Microsoft\Windows\CurrentVersion\Run";
        private const string Name = "MapleSchedulerHUD";

        private static string Command => "\"" + System.Windows.Forms.Application.ExecutablePath + "\"";

        /// <summary>
        /// 자동 실행이 다른 곳의 exe를 가리키면 지금 실행한 exe로 바꾼다.
        /// 새 버전을 다른 폴더에 풀어 실행했을 때, 다음 부팅에 옛 버전이 뜨지 않게 한다.
        /// </summary>
        public static void Repoint()
        {
            using (var key = Registry.CurrentUser.OpenSubKey(RunKey, true))
            {
                var current = key?.GetValue(Name) as string;
                if (current != null && current != Command) key.SetValue(Name, Command);
            }
        }

        public static bool Enabled
        {
            get
            {
                using (var key = Registry.CurrentUser.OpenSubKey(RunKey)) return key?.GetValue(Name) as string == Command;
            }
            set
            {
                using (var key = Registry.CurrentUser.CreateSubKey(RunKey))
                {
                    if (value) key.SetValue(Name, Command);
                    else key.DeleteValue(Name, false);
                }
            }
        }
    }
}
