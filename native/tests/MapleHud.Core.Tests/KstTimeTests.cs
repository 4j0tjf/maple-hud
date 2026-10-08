using Xunit;
using static MapleHud.Core.Tests.T;

namespace MapleHud.Core.Tests
{
    public class KstTimeTests
    {
        [Fact]
        public void 일일_초기화는_KST_자정()
        {
            Assert.Equal(Ms("2026-10-07T15:00:00Z"), KstTime.LastReset(ResetKind.Daily, Now));
            Assert.Equal(Ms("2026-10-08T15:00:00Z"), KstTime.NextReset(ResetKind.Daily, Now));
        }

        [Fact]
        public void 주간_보스는_목요일_주간_콘텐츠는_월요일()
        {
            Assert.Equal(Ms("2026-10-07T15:00:00Z"), KstTime.LastReset(ResetKind.WeeklyThu, Now));
            Assert.Equal(Ms("2026-10-14T15:00:00Z"), KstTime.NextReset(ResetKind.WeeklyThu, Now));
            Assert.Equal(Ms("2026-10-04T15:00:00Z"), KstTime.LastReset(ResetKind.WeeklyMon, Now));
            Assert.Equal(Ms("2026-10-11T15:00:00Z"), KstTime.NextReset(ResetKind.WeeklyMon, Now));
        }

        [Fact]
        public void 목요일_자정_직전에는_지난주_목요일이_기준()
        {
            Assert.Equal(Ms("2026-09-30T15:00:00Z"), KstTime.LastReset(ResetKind.WeeklyThu, Ms("2026-10-07T14:59:59.999Z")));
            Assert.Equal(Ms("2026-10-07T15:00:00Z"), KstTime.LastReset(ResetKind.WeeklyThu, Ms("2026-10-07T15:00:00Z")));
        }

        [Fact]
        public void UTC로는_전날이어도_KST_날짜_기준()
        {
            var t = Ms("2026-10-07T16:00:00Z");
            Assert.Equal(Ms("2026-10-07T15:00:00Z"), KstTime.LastReset(ResetKind.Daily, t));
            Assert.Equal(Ms("2026-10-07T15:00:00Z"), KstTime.LastReset(ResetKind.WeeklyThu, t));
        }

        [Fact]
        public void 월간_초기화는_매월_1일_연말_포함()
        {
            Assert.Equal(Ms("2026-09-30T15:00:00Z"), KstTime.LastReset(ResetKind.Monthly, Now));
            Assert.Equal(Ms("2026-10-31T15:00:00Z"), KstTime.NextReset(ResetKind.Monthly, Now));
            Assert.Equal(Ms("2026-12-31T15:00:00Z"), KstTime.NextReset(ResetKind.Monthly, Ms("2026-12-20T00:00:00Z")));
        }

        [Fact]
        public void 카운트다운_표시()
        {
            Assert.Equal("2일 03:12", KstTime.FormatCountdown(((2 * 24 + 3) * 3600 + 12 * 60 + 59) * 1000L));
            Assert.Equal("03:12:45", KstTime.FormatCountdown((3 * 3600 + 12 * 60 + 45) * 1000L));
            Assert.Equal("00:00:00", KstTime.FormatCountdown(-5));
            Assert.Equal("3시간 13분", KstTime.FormatCountdown((3 * 3600 + 12 * 60 + 45) * 1000L, false));
            Assert.Equal("1시간 00분", KstTime.FormatCountdown((59 * 60 + 1) * 1000L, false));
            Assert.Equal("1분", KstTime.FormatCountdown(30 * 1000L, false));
        }

        [Fact]
        public void KST_시계()
        {
            var c = KstTime.FormatClock(Now);
            Assert.Equal("10.08 (목)", c.Date);
            Assert.Equal("21:34", c.Time);
            Assert.Equal("00", c.Seconds);
        }
    }
}
