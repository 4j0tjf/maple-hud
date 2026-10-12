using System.Linq;
using Xunit;
using static MapleHud.Core.Tests.SchedulerTests;
using static MapleHud.Core.Tests.T;

namespace MapleHud.Core.Tests
{
    /// <summary>
    /// 실제 넥슨 API 응답(사용자가 보내 준 스케줄러 원본)과 같은 모양으로 확인한다.
    /// - 보스는 난이도마다 따로 오고, 난이도는 영문 소문자, cycle은 bossWeekly 같은 값
    /// - 길드 지하 수로처럼 최대치 없이 점수만 오는 주간 항목 (31570/0)
    /// </summary>
    public class RealResponseTests
    {
        private static object B(string name, string difficulty, string cycle, bool reg, bool done) =>
            Boss(name, cycle: cycle, difficulty: difficulty, reg: reg ? "true" : "false", done: done);

        private static readonly object[] Bosses =
        {
            B("루시드", "easy", "bossWeekly", false, false), B("루시드", "normal", "bossWeekly", false, false), B("루시드", "hard", "bossWeekly", true, true),
            B("카링", "easy", "bossWeekly", true, true), B("카링", "normal", "bossWeekly", false, false), B("카링", "hard", "bossWeekly", false, false),
            B("아카이럼", "normal", "bossDaily", true, false), B("검은 마법사", "hard", "bossMonthly", true, false)
        };

        [Fact]
        public void 난이도별_보스를_각각_판정한다()
        {
            var m = Scheduler.Normalize(Body(boss: Bosses), Now - Min, Now);
            Assert.Equal(new[] { "루시드 하드 True", "카링 이지 True", "아카이럼 노멀 False", "검은 마법사 하드 False" },
                m.Boss.Select(b => b.Name + " " + b.Difficulty + " " + b.Done));
            Assert.Equal(new[] { "bossWeekly", "bossWeekly", "bossDaily", "bossMonthly" }, m.Boss.Select(b => b.Group));
            // 편집 화면에서 하나만 눌러도 다른 난이도는 그대로
            var sel = Scheduler.EditSelection(null, m, "item", Scheduler.BossKey("카링", "normal"));
            var after = Scheduler.Normalize(Body(boss: Bosses), Now - Min, Now, selection: sel);
            Assert.Equal(new[] { "루시드 하드", "카링 이지", "카링 노멀", "아카이럼 노멀", "검은 마법사 하드" },
                after.Boss.Select(b => b.Name + " " + b.Difficulty));
        }

        [Fact]
        public void 이전_빌드에서_유지하기한_캐릭터도_등록된_난이도만_보인다()
        {
            // 이전 빌드의 유지하기: 보스를 이름으로만 적었고, 같은 이름의 마지막 난이도 값이 남았다
            // (루시드 easy/normal/hard 중 hard가 마지막 → "boss:루시드" = true → 예전엔 세 난이도가 다 보였다)
            var old = new Selection { Kept = true };
            foreach (var g in new[] { ("루시드", true), ("카링", false), ("아카이럼", true), ("검은 마법사", true) })
                old.Items["boss:" + g.Item1] = g.Item2;
            var m = Scheduler.Normalize(Body(boss: Bosses), Now - Min, Now, selection: old);
            Assert.Equal(new[] { "루시드 하드", "카링 이지", "아카이럼 노멀", "검은 마법사 하드" }, m.Boss.Select(b => b.Name + " " + b.Difficulty));
            // 유지하기를 다시 누르면 난이도별로 다시 적는다
            var again = Scheduler.EditSelection(old, m, "keep", null);
            Assert.True(again.Items[Scheduler.BossKey("루시드", "hard")]);
            Assert.False(again.Items[Scheduler.BossKey("루시드", "easy")]);
            Assert.DoesNotContain("boss:루시드", again.Items.Keys);
        }

        [Fact]
        public void 최대치_0인_주간_항목은_인게임과_같게_판정()
        {
            // 인게임: 에픽 던전 : 악몽선경(5/0)은 체크, [길드] 지하 수로(31570/0)는 "31570 점"만 있고 체크 없음
            var m = Scheduler.Normalize(Body(weekly: new object[]
            {
                Content("에픽 던전 : 악몽선경", now: 5, max: 0), Content("에픽 던전 : 앵글러 컴퍼니", now: 0, max: 0),
                Content("[길드] 지하 수로", now: 31570, max: 0), Content("[길드] 플래그 레이스", now: 0, max: 0),
                Content("[길드] 주간 미션 포인트", now: 10, max: 10), Content("에르다 스펙트럼", now: 0, max: 1)
            }), Now - Min, Now, showAll: true);
            Assert.Equal(new[] { true, false, false, false, true, false }, m.Weekly.Select(i => i.Done));
            Assert.True(m.Weekly[2].Score);
            Assert.True(m.Weekly[2].Progress);
            Assert.False(m.Weekly[0].Score);
        }

        [Fact]
        public void 원본_보기에서도_난이도별로_판정이_맞는다()
        {
            var c = new CharState { Name = "부누꿍", Body = System.Text.Json.JsonSerializer.Serialize(Body(boss: Bosses)), FetchedAt = Now - Min };
            var text = SchedulerReport.Build(new[] { c }, Now);
            Assert.Contains("루시드 hard  |  등록 · bossWeekly · complete_flag=true  →  완료", text);
            Assert.Contains("카링 normal  |  미등록 · bossWeekly · complete_flag=false  →  미완료", text);
        }
    }
}
