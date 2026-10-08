using System.Collections.Generic;
using System.Linq;
using System.Text.Json;
using Xunit;
using static MapleHud.Core.Tests.T;

namespace MapleHud.Core.Tests
{
    public class SchedulerTests
    {
        internal static Dictionary<string, object> Content(string name, object regFlag = null, int now = 0, int max = 1,
            string quest = null, string type = "contents") => new Dictionary<string, object>
        {
            ["content_name"] = name, ["type"] = type, ["registration_flag"] = regFlag ?? "true",
            ["now_count"] = now, ["max_count"] = max, ["quest_state"] = quest
        };

        internal static Dictionary<string, object> Boss(string name, string cycle = "주간", int order = 1, bool done = false,
            string difficulty = "하드", string reg = "true") => new Dictionary<string, object>
        {
            ["content_name"] = name, ["difficulty"] = difficulty, ["cycle"] = cycle, ["list_order_no"] = order,
            ["registration_flag"] = reg, ["complete_flag"] = done ? "true" : "false"
        };

        internal static JsonElement Body(object[] daily = null, object[] weekly = null, object[] boss = null, int bossClear = 0)
        {
            var json = JsonSerializer.Serialize(new Dictionary<string, object>
            {
                ["character_name"] = "테스트", ["world_name"] = "스카니아", ["character_level"] = 280, ["character_class"] = "비숍",
                ["daily_contents"] = daily ?? new object[0], ["weekly_contents"] = weekly ?? new object[0],
                ["boss_contents"] = boss ?? new object[0],
                ["weekly_boss_clear_count"] = bossClear, ["weekly_boss_clear_limit_count"] = 12
            });
            return JsonDocument.Parse(json).RootElement.Clone();
        }

        [Fact]
        public void 횟수형과_퀘스트형_완료_판정()
        {
            var m = Scheduler.Normalize(Body(daily: new object[]
            {
                Content("몬스터파크", now: 2, max: 2), Content("우르스", now: 1, max: 3),
                Content("세르니움 일일 퀘스트", max: 0, quest: "2", type: "quest"),
                Content("오디움 일일 퀘스트", max: 0, quest: "1", type: "quest"),
                Content("도원경 일일 퀘스트", max: 0, quest: "0", type: "quest")
            }), Now - Min, Now);
            Assert.Equal(new[] { (true, false), (false, true), (true, false), (false, true), (false, false) },
                m.Daily.Select(i => (i.Done, i.Progress)).ToArray());
            Assert.Equal(new Tally(2, 5), m.CountDaily);
        }

        [Fact]
        public void 등록된_항목만_보여주고_없으면_전체()
        {
            var b = Body(daily: new object[] { Content("A"), Content("B", "false") }, boss: new object[] { Boss("스우", reg: "false") });
            var m = Scheduler.Normalize(b, Now - Min, Now);
            Assert.Equal(new[] { "A" }, m.Daily.Select(i => i.Name));
            Assert.Empty(m.Boss);
            Assert.Equal(2, Scheduler.Normalize(b, Now - Min, Now, showAll: true).Daily.Count);

            var none = Scheduler.Normalize(Body(daily: new object[] { Content("A", "false") }), Now - Min, Now);
            Assert.True(none.ShowingAll);
            Assert.Single(none.Daily);
            Assert.True(Scheduler.HasRegistered(b));
            Assert.False(Scheduler.HasRegistered(Body(daily: new object[] { Content("A", false) })));
            Assert.True(Scheduler.HasRegistered(Body(daily: new object[] { Content("A", true) })));
        }

        [Fact]
        public void 보스는_순서대로_완료_판정()
        {
            var m = Scheduler.Normalize(Body(boss: new object[]
            {
                Boss("루시드", order: 3, done: true), Boss("스우", order: 1), Boss("데미안", order: 2, done: true)
            }, bossClear: 2), Now - Min, Now);
            Assert.Equal(new[] { ("스우", false), ("데미안", true), ("루시드", true) }, m.Boss.Select(i => (i.Name, i.Done)));
            Assert.Equal(2, m.BossClear);
            Assert.Equal(12, m.BossLimit);
        }

        [Theory]
        [InlineData("주간", ResetKind.WeeklyThu)]
        [InlineData("매주 목요일", ResetKind.WeeklyThu)]
        [InlineData("월간", ResetKind.Monthly)]
        [InlineData("일간", ResetKind.Daily)]
        [InlineData("daily", ResetKind.Daily)]
        [InlineData("monthly", ResetKind.Monthly)]
        [InlineData(null, ResetKind.WeeklyThu)]
        public void 보스_초기화_주기(string cycle, ResetKind expected) => Assert.Equal(expected, Scheduler.CycleToReset(cycle));

        [Fact]
        public void 자정이_지나면_일일_완료를_해제()
        {
            var b = Body(daily: new object[] { Content("몬스터파크", now: 2, max: 2) }, weekly: new object[] { Content("무릉도장", now: 1, max: 1) });
            var m = Scheduler.Normalize(b, Ms("2026-10-09T14:50:00Z"), Ms("2026-10-09T16:00:00Z"));
            Assert.False(m.Daily[0].Done);
            Assert.Equal(0, m.Daily[0].Now);
            Assert.True(m.Weekly[0].Done);
        }

        [Fact]
        public void 초기화_직후_반영_지연()
        {
            var b = Body(daily: new object[] { Content("몬스터파크", now: 2, max: 2) });
            var reset = Ms("2026-10-08T15:00:00Z");
            Assert.False(Scheduler.Normalize(b, reset + 5 * Min, reset + 6 * Min).Daily[0].Done);
            Assert.True(Scheduler.Normalize(b, reset + 25 * Min, reset + 26 * Min).Daily[0].Done);
            Assert.True(Scheduler.Normalize(b, reset + 5 * Min, reset + 6 * Min, delay: 0).Daily[0].Done);
        }

        [Fact]
        public void 목요일이_지나면_주간_보스와_클리어_횟수_초기화()
        {
            var b = Body(boss: new object[] { Boss("스우", done: true), Boss("검은 마법사", cycle: "월간", done: true) }, bossClear: 5);
            var m = Scheduler.Normalize(b, Ms("2026-10-07T14:00:00Z"), Now);
            Assert.False(m.Boss[0].Done);
            Assert.True(m.Boss[1].Done);
            Assert.Equal(0, m.BossClear);
        }

        [Fact]
        public void 캐릭터별_표시_설정()
        {
            var b = Body(daily: new object[] { Content("몬스터파크"), Content("우르스"), Content("미등록", "false") },
                weekly: new object[] { Content("무릉도장") },
                boss: new object[] { Boss("스우", order: 1), Boss("검은 마법사", cycle: "월간", order: 2) });
            var sel = new Selection
            {
                Groups = new Dictionary<string, bool> { ["weekly"] = false },
                Items = new Dictionary<string, bool> { ["daily:우르스"] = false, ["daily:미등록"] = true, ["boss:검은 마법사"] = false }
            };
            var m = Scheduler.Normalize(b, Now - Min, Now, selection: sel);
            Assert.Equal(new[] { "몬스터파크", "미등록" }, m.Daily.Select(i => i.Name));
            Assert.Empty(m.Weekly);
            Assert.Equal(new[] { "스우" }, m.Boss.Select(i => i.Name));
            Assert.True(m.Customized);
            Assert.Equal(new[] { ("daily", true, 3), ("weekly", false, 1), ("bossWeekly", true, 1), ("bossMonthly", true, 1) },
                m.Groups.Select(g => (g.Id, g.On, g.Items.Count)));
        }

        [Fact]
        public void 표시_설정_편집()
        {
            var b = Body(daily: new object[] { Content("몬스터파크"), Content("우르스") },
                boss: new object[] { Boss("스우"), Boss("검은 마법사", cycle: "월간") });
            SchedModel View(Selection s) => Scheduler.Normalize(b, Now - Min, Now, selection: s);

            var sel = Scheduler.EditSelection(null, View(null), "item", "daily:우르스");
            Assert.Equal(new[] { "몬스터파크" }, View(sel).Daily.Select(i => i.Name));
            sel = Scheduler.EditSelection(sel, View(sel), "item", "daily:우르스");
            Assert.Equal(2, View(sel).Daily.Count);

            sel = Scheduler.EditSelection(sel, View(sel), "group", "bossMonthly");
            Assert.Equal(new[] { "스우" }, View(sel).Boss.Select(i => i.Name));
            sel = Scheduler.EditSelection(sel, View(sel), "group", "bossMonthly");
            Assert.Equal(2, View(sel).Boss.Count);

            sel = Scheduler.EditSelection(sel, View(sel), "none", "daily");
            Assert.Empty(View(sel).Daily);
            sel = Scheduler.EditSelection(sel, View(sel), "all", "daily");
            Assert.Equal(2, View(sel).Daily.Count);
        }

        [Fact]
        public void 보스_난이도가_바뀌어도_선택_유지()
        {
            var sel = new Selection { Items = new Dictionary<string, bool> { ["boss:스우"] = false } };
            var m = Scheduler.Normalize(Body(boss: new object[] { Boss("스우", difficulty: "익스트림") }), Now - Min, Now, selection: sel);
            Assert.Empty(m.Boss);
        }
    }
}
