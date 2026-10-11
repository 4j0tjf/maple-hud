using System.Linq;
using System.Net.Http;
using System.Threading.Tasks;
using Xunit;
using static MapleHud.Core.Tests.SchedulerTests;
using static MapleHud.Core.Tests.T;

namespace MapleHud.Core.Tests
{
    /// <summary>주간 퀘스트 완료 판정과 "스케줄러 원본 보기"</summary>
    public class WeeklyQuestTests
    {
        private static SchedItem Weekly(object item) =>
            Scheduler.Normalize(Body(weekly: new[] { item }), Now - Min, Now, showAll: true).Weekly.Single();

        [Fact]
        public void 퀘스트는_quest_state_2일_때만_완료()
        {
            var accepted = Weekly(Content("주간 퀘스트", now: 0, max: 0, quest: "1", type: "quest"));
            Assert.False(accepted.Done);
            Assert.True(accepted.Progress);
            Assert.True(Weekly(Content("주간 퀘스트", now: 0, max: 0, quest: "2", type: "quest")).Done);
            // 횟수가 다 차도 완료 보고 전이면 미완료
            Assert.False(Weekly(Content("처치 퀘스트", now: 3, max: 3, quest: "1", type: "quest")).Done);
        }

        [Fact]
        public void quest_state_2면_종류_표기와_상관없이_완료()
        {
            Assert.True(Weekly(Content("주간 퀘스트", now: 0, max: 1, quest: "2", type: "contents")).Done);
            Assert.True(Weekly(Content("주간 퀘스트", now: 0, max: 1, quest: "2", type: "Quest")).Done);
            Assert.False(Weekly(Content("주간 퀘스트", now: 0, max: 1, quest: "1", type: "Quest")).Done);
            // 아직 받지 않은 퀘스트
            Assert.False(Weekly(Content("주간 퀘스트", now: 0, max: 0, quest: null, type: "quest")).Done);
        }

        [Fact]
        public void 등록_여부는_대소문자를_가리지_않는다()
        {
            Assert.True(Weekly(Content("무릉도장", "True")).Registered);
            Assert.False(Weekly(Content("무릉도장", "False")).Registered);
        }

        [Fact]
        public void 스케줄러_원본에_API_값과_판정이_나란히_나온다()
        {
            var chars = DemoData.Build(Now - 5 * Min);
            var text = SchedulerReport.Build(chars, Now);
            Assert.Contains("[" + chars[0].Name + "]", text);
            Assert.Contains("받은 시각", text);
            Assert.Contains("5분 전", text);
            Assert.Contains("quest_state=", text);
            Assert.Contains("→  완료", text);
            Assert.Contains("complete_flag=", text);
            var none = SchedulerReport.Build(new[] { new CharState { Name = "새캐릭" } }, Now);
            Assert.Contains("아직 받은 스케줄러가 없습니다", none);
        }

        [Fact]
        public async Task 게임에서_나오면_오래된_스케줄러를_바로_다시_받는다()
        {
            NexonApi.MinGapMs = 0;
            long now = Now;
            var fake = new FakeNexon();
            var engine = new SyncEngine(new HudSettings { ApiKey = "test_key", ShowAvatar = false }, JsonStore.InMemory(),
                x => new NexonApi(new HttpClient(fake), x.ApiKey, "https://api.test"), () => now);
            await engine.SyncAsync(false);

            now += 2 * Min;
            Assert.False(engine.RefreshIfOlder(3 * Min));
            now += 2 * Min;
            fake.Calls.Clear();
            Assert.True(engine.RefreshIfOlder(3 * Min));
            while (engine.Syncing) await Task.Delay(5);
            // 캐시가 남아 있어도(15분 안) 스케줄러를 새로 받는다
            Assert.Contains("scheduler/character-state?ocid=o1", fake.Calls);
        }
    }
}
