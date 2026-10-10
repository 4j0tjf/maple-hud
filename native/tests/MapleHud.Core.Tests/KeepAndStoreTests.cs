using System;
using System.IO;
using System.Linq;
using System.Net;
using System.Net.Http;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using Xunit;
using static MapleHud.Core.Tests.SchedulerTests;
using static MapleHud.Core.Tests.T;

namespace MapleHud.Core.Tests
{
    /// <summary>유지하기 / API 불러오기, 사용자 설정과 캐시 분리, 깨진 저장 파일 복구</summary>
    public class KeepAndStoreTests
    {
        private static string[] Daily(SchedModel m) => m.Daily.Select(i => i.Name).ToArray();

        [Fact]
        public void 유지하기는_지금_보이는_대로_고정하고_새로_등록한_항목은_숨긴다()
        {
            var before = Body(daily: new object[] { Content("몬스터파크"), Content("우르스"), Content("미등록", "false") });
            var m = Scheduler.Normalize(before, Now - Min, Now);
            // 우르스를 끄고 유지하기
            var sel = Scheduler.EditSelection(null, m, "item", "daily:우르스");
            m = Scheduler.Normalize(before, Now - Min, Now, selection: sel);
            Assert.True(m.Customized);
            Assert.False(m.Kept);
            sel = Scheduler.EditSelection(sel, m, "keep", null);
            Assert.True(sel.Kept);

            // 인게임에서 우르스를 다시 등록하고, 미등록 항목과 새 항목도 등록했다
            var after = Body(daily: new object[] { Content("몬스터파크"), Content("우르스"), Content("미등록"), Content("새 콘텐츠") });
            var kept = Scheduler.Normalize(after, Now - Min, Now, selection: sel);
            Assert.Equal(new[] { "몬스터파크" }, Daily(kept));
            Assert.True(kept.Kept);
            Assert.Equal(1, kept.HiddenNew);   // 새 콘텐츠

            // 유지하지 않았다면 인게임 등록을 따른다
            var follow = Scheduler.Normalize(after, Now - Min, Now, selection: new Selection { Items = { ["daily:우르스"] = false } });
            Assert.Equal(new[] { "몬스터파크", "미등록", "새 콘텐츠" }, Daily(follow));

            // 유지 중에도 항목을 켜고 끌 수 있다
            sel = Scheduler.EditSelection(sel, kept, "item", "daily:새 콘텐츠");
            Assert.Equal(new[] { "몬스터파크", "새 콘텐츠" }, Daily(Scheduler.Normalize(after, Now - Min, Now, selection: sel)));
        }

        [Fact]
        public void 분류를_끈_채로_유지해도_분류를_다시_켜면_항목_선택이_살아_있다()
        {
            var b = Body(daily: new object[] { Content("몬스터파크"), Content("우르스") });
            var sel = new Selection { Groups = { ["daily"] = false } };
            var m = Scheduler.Normalize(b, Now - Min, Now, selection: sel);
            Assert.Empty(m.Daily);
            sel = Scheduler.EditSelection(sel, m, "keep", null);
            sel = Scheduler.EditSelection(sel, Scheduler.Normalize(b, Now - Min, Now, selection: sel), "group", "daily");
            Assert.Equal(new[] { "몬스터파크", "우르스" }, Daily(Scheduler.Normalize(b, Now - Min, Now, selection: sel)));
        }

        [Fact]
        public async Task API_불러오기는_내_설정을_지우고_스케줄러를_새로_받는다()
        {
            NexonApi.MinGapMs = 0;
            long now = T.Now;
            var fake = new FakeNexon();
            var cache = JsonStore.InMemory();
            var prefs = JsonStore.InMemory();
            var s = new HudSettings { ApiKey = "test_key", ShowAvatar = false };
            var engine = new SyncEngine(s, cache, x => new NexonApi(new HttpClient(fake), x.ApiKey, "https://api.test"), () => now);
            await engine.SyncAsync(false);
            var ctl = new HudController(engine, prefs, s, () => now);
            ctl.EditSelection("o1", "item", "daily:우르스");
            ctl.EditSelection("o1", "keep", null);
            Assert.True(ctl.Build().Cards[0].Model.Kept);
            Assert.NotNull(prefs.GetRaw("sel:o1"));
            Assert.Null(cache.GetRaw("sel:o1"));

            fake.Calls.Clear();
            ctl.EditSelection("o1", "reset", null);
            engine.RefreshNow();
            while (engine.Syncing) await Task.Delay(5);
            Assert.False(ctl.Build().Cards[0].Model.Customized);
            Assert.Equal(2, ctl.Build().Cards[0].Model.Daily.Count);
            // 캐시가 살아 있어도 스케줄러를 다시 받는다
            Assert.Contains("scheduler/character-state?ocid=o1", fake.Calls);
        }

        [Fact]
        public void 예전_파일에_있던_사용자_설정을_옮겨_온다()
        {
            var cache = JsonStore.InMemory();
            var prefs = JsonStore.InMemory();
            cache.Set("sel:o1", new Selection { Items = { ["daily:우르스"] = false } });
            cache.SetRaw("collapse:o1", "{\"V\":true,\"Day\":1}");
            cache.SetCached("sched:o1", "{}", 1);
            prefs.Set("sel:o2", new Selection { Kept = true });
            Assert.Equal(2, HudController.MigratePrefs(cache, prefs));
            Assert.Equal(new[] { "sched:o1" }, cache.Keys.ToArray());
            Assert.False(prefs.Get<Selection>("sel:o1").Items["daily:우르스"]);
            Assert.True(prefs.Get<Selection>("sel:o2").Kept);
            Assert.NotNull(prefs.GetRaw("collapse:o1"));
            Assert.Equal(0, HudController.MigratePrefs(cache, prefs));
        }

        [Fact]
        public void JSON이_아닌_응답은_저장소를_깨뜨리지_않는다()
        {
            var path = TempFile();
            var a = new JsonStore(path);
            a.Set("sel:o1", new Selection { Kept = true });
            a.SetCached("sched:o1", "<html>점검 중</html>", 1);
            a.Flush();
            var b = new JsonStore(path);
            Assert.False(b.Recovered);
            Assert.True(b.Get<Selection>("sel:o1").Kept);
            Assert.Equal("null", b.GetCached("sched:o1", null, 1).Data);
        }

        [Fact]
        public void 깨진_파일은_직전_백업에서_되살리고_원본은_남겨_둔다()
        {
            var path = TempFile();
            var a = new JsonStore(path);
            a.Set("sel:o1", new Selection { Kept = true });
            a.Flush();
            a.Set("sel:o2", new Selection { Kept = true });
            a.Flush();   // 이때 첫 번째 내용이 .bak으로 남는다
            Assert.True(File.Exists(path + ".bak"));
            File.WriteAllText(path, "{\"sel:o1\": {\"Kept\": tr");   // 쓰다 만 파일

            var b = new JsonStore(path);
            Assert.True(b.Recovered);
            Assert.True(b.Get<Selection>("sel:o1").Kept);
            Assert.True(File.Exists(path + ".broken"));
            b.Flush();   // 되살린 내용으로 다시 쓴다
            Assert.False(new JsonStore(path).Recovered);
        }

        [Fact]
        public async Task 점검_페이지_같은_응답은_캐시하지_않고_알린다()
        {
            NexonApi.MinGapMs = 0;
            var engine = new SyncEngine(new HudSettings { ApiKey = "k" }, JsonStore.InMemory(),
                x => new NexonApi(new HttpClient(new Html()), x.ApiKey, "https://api.test"), () => T.Now);
            await engine.SyncAsync(false);
            Assert.Equal("error", engine.Notice.Level);
            Assert.Contains("응답을 읽을 수 없습니다", engine.Notice.Text);
        }

        private sealed class Html : HttpMessageHandler
        {
            protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken token) =>
                Task.FromResult(new HttpResponseMessage(HttpStatusCode.OK) { Content = new StringContent("<html>점검 중</html>", Encoding.UTF8, "text/html") });
        }

        private static string TempFile() => Path.Combine(Path.GetTempPath(), "mh-" + Guid.NewGuid().ToString("N"), "prefs.json");
    }
}
