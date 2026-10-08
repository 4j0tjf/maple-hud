using System;

namespace MapleHud.Core.Tests
{
    internal static class T
    {
        public static long Ms(string iso) => DateTimeOffset.Parse(iso).ToUnixTimeMilliseconds();
        public const long Min = 60 * 1000;
        public const long Day = 24 * 60 * Min;
        // 2026-10-08(목) 21:34 KST
        public static readonly long Now = Ms("2026-10-08T12:34:00Z");
    }
}
