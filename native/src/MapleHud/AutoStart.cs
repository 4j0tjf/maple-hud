using Microsoft.Win32;

namespace MapleHud
{
    /// <summary>Windows 시작 시 자동 실행 (현재 사용자 Run 레지스트리)</summary>
    internal static class AutoStart
    {
        private const string RunKey = @"Software\Microsoft\Windows\CurrentVersion\Run";
        private const string Name = "MapleSchedulerHUD";

        private static string Command => "\"" + System.Windows.Forms.Application.ExecutablePath + "\"";

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
