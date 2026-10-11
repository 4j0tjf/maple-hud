using System;
using System.Reflection;

namespace MapleHud
{
    /// <summary>
    /// 실행 중인 빌드를 알아보기 위한 버전. CI가 커밋 번호를 붙여 빌드한다 (예: "v2.1.0 (9adf442)").
    /// 트레이 메뉴 맨 위에 보여서, 업데이트가 됐는지 바로 알 수 있다.
    /// </summary>
    internal static class AppVersion
    {
        /// <summary>"2.1.0+9adf442…" 형태 그대로 (같은 빌드인지 비교할 때)</summary>
        public static string Full =>
            Assembly.GetExecutingAssembly().GetCustomAttribute<AssemblyInformationalVersionAttribute>()?.InformationalVersion ?? "";

        public static string Display => Format(Full);

        public static string Format(string info)
        {
            if (string.IsNullOrEmpty(info)) return "";
            int plus = info.IndexOf('+');
            if (plus < 0) return "v" + info;
            var rev = info.Substring(plus + 1);
            return "v" + info.Substring(0, plus) + " (" + rev.Substring(0, Math.Min(7, rev.Length)) + ")";
        }
    }
}
