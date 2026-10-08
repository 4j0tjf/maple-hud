using System;
using System.Globalization;

namespace MapleHud.Core
{
    public enum ResetKind { Daily, WeeklyMon, WeeklyThu, Monthly }

    /// <summary>
    /// KST(UTC+9) 기준 초기화 시각.
    /// 메이플스토리 초기화: 일일 00:00, 주간 보스 목요일 00:00, 주간 콘텐츠 월요일 00:00, 월간 1일 00:00.
    /// 시각은 모두 유닉스 밀리초(long)로 다룬다.
    /// </summary>
    public static class KstTime
    {
        public const long Day = 86400000L;
        private const long Offset = 9 * 3600 * 1000L;
        private static readonly string[] Weekdays = { "일", "월", "화", "수", "목", "금", "토" };

        public static long NowMs() => DateTimeOffset.UtcNow.ToUnixTimeMilliseconds();

        private static long Mod(long a, long b) => ((a % b) + b) % b;

        // KST 시각을 UTC 필드로 읽을 수 있게 옮긴 값
        private static DateTime KstDate(long now) => DateTimeOffset.FromUnixTimeMilliseconds(now + Offset).UtcDateTime;

        private static long ToMs(DateTime utc) =>
            new DateTimeOffset(DateTime.SpecifyKind(utc, DateTimeKind.Utc)).ToUnixTimeMilliseconds();

        private static long KstMidnight(long now)
        {
            long k = now + Offset;
            return k - Mod(k, Day) - Offset;
        }

        private static long LastWeekly(long now, DayOfWeek dow)
        {
            long diff = Mod((int)KstDate(now).DayOfWeek - (int)dow, 7);
            return KstMidnight(now) - diff * Day;
        }

        private static long LastMonthly(long now)
        {
            var d = KstDate(now);
            return ToMs(new DateTime(d.Year, d.Month, 1)) - Offset;
        }

        private static long NextMonthly(long now)
        {
            var d = KstDate(now);
            return ToMs(new DateTime(d.Year, d.Month, 1).AddMonths(1)) - Offset;
        }

        public static long LastReset(ResetKind kind, long now)
        {
            switch (kind)
            {
                case ResetKind.WeeklyMon: return LastWeekly(now, DayOfWeek.Monday);
                case ResetKind.WeeklyThu: return LastWeekly(now, DayOfWeek.Thursday);
                case ResetKind.Monthly: return LastMonthly(now);
                default: return KstMidnight(now);
            }
        }

        public static long NextReset(ResetKind kind, long now)
        {
            switch (kind)
            {
                case ResetKind.WeeklyMon: return LastWeekly(now, DayOfWeek.Monday) + 7 * Day;
                case ResetKind.WeeklyThu: return LastWeekly(now, DayOfWeek.Thursday) + 7 * Day;
                case ResetKind.Monthly: return NextMonthly(now);
                default: return KstMidnight(now) + Day;
            }
        }

        private static string Pad(long n) => n.ToString("00", CultureInfo.InvariantCulture);

        /// <summary>
        /// 1일 이상이면 "2일 03:12", 미만이면 "03:12:45".
        /// withSeconds가 false면 1일 미만은 "3시간 12분"처럼 분 단위로 (남은 분은 올림)
        /// </summary>
        public static string FormatCountdown(long ms, bool withSeconds = true)
        {
            long s = Math.Max(0, ms / 1000);
            long d = s / 86400;
            long h = (s % 86400) / 3600;
            long m = (s % 3600) / 60;
            if (d > 0) return d + "일 " + Pad(h) + ":" + Pad(m);
            if (!withSeconds)
            {
                long total = (s + 59) / 60;
                long hh = total / 60;
                return hh > 0 ? hh + "시간 " + Pad(total % 60) + "분" : total + "분";
            }
            return Pad(h) + ":" + Pad(m) + ":" + Pad(s % 60);
        }

        public sealed class Clock
        {
            public string Date;
            public string Time;
            public string Seconds;
        }

        public static Clock FormatClock(long now)
        {
            var d = KstDate(now);
            return new Clock
            {
                Date = Pad(d.Month) + "." + Pad(d.Day) + " (" + Weekdays[(int)d.DayOfWeek] + ")",
                Time = Pad(d.Hour) + ":" + Pad(d.Minute),
                Seconds = Pad(d.Second)
            };
        }

        public static string FormatAgo(long ms)
        {
            long m = ms / 60000;
            if (m < 1) return "방금";
            if (m < 60) return m + "분 전";
            long h = m / 60;
            if (h < 24) return h + "시간 전";
            return (h / 24) + "일 전";
        }
    }
}
