using System;
using System.Collections.Generic;
using System.Linq;
using System.Net;
using System.Net.Http;
using System.Text;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;
using Xunit;
using static MapleHud.Core.Tests.SchedulerTests;

namespace MapleHud.Core.Tests
{
    /// <summary>넥슨 API를 흉내내는 가짜 서버 (wallpaper 버전 테스트의 mock과 같은 데이터)</summary>
    internal sealed class FakeNexon : HttpMessageHandler
    {
        public readonly List<string> Calls = new List<string>();
        private static readonly object[] Chars =
        {
            Char("o1", "메인캐릭", "나이트로드", 291), Char("o2", "부캐하나", "팔라딘", 268),
            Char("o3", "미등록캐", "보우마스터", 255), Char("o4", "부캐둘", "카인", 231), Char("o5", "저렙", "초보자", 120)
        };
        private static readonly Dictionary<string, bool> Registered = new Dictionary<string, bool>
        {
            ["o1"] = true, ["o2"] = true, ["o3"] = false, ["o4"] = true, ["o5"] = true
        };

        private static Dictionary<string, object> Char(string ocid, string name, string cls, int level) => new Dictionary<string, object>
        {
            ["ocid"] = ocid, ["character_name"] = name, ["world_name"] = "루나", ["character_class"] = cls, ["character_level"] = level
        };

        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken token)
        {
            var path = request.RequestUri.AbsolutePath.Replace("/maplestory/v1/", "");
            var query = request.RequestUri.Query.TrimStart('?').Split('&').Where(p => p.Contains('='))
                .ToDictionary(p => p.Split('=')[0], p => Uri.UnescapeDataString(p.Split('=')[1]));
            Calls.Add(path + (query.Count > 0 ? "?" + string.Join("&", query.Select(q => q.Key + "=" + q.Value)) : ""));
            request.Headers.TryGetValues("x-nxopen-api-key", out var keys);
            if (keys?.FirstOrDefault() != "test_key") return Json(400, Error("OPENAPI00005"));

            query.TryGetValue("ocid", out var ocid);
            var ch = Chars.Cast<Dictionary<string, object>>().FirstOrDefault(c => (string)c["ocid"] == ocid);
            switch (path)
            {
                case "character/list":
                    return Json(200, new { account_list = new[] { new { account_id = "a", character_list = Chars } } });
                case "id":
                    var found = Chars.Cast<Dictionary<string, object>>().FirstOrDefault(c => (string)c["character_name"] == query["character_name"]);
                    return found != null ? Json(200, new { ocid = found["ocid"] }) : Json(400, Error("OPENAPI00004"));
                case "character/basic":
                    if (ch == null) return Json(400, Error("OPENAPI00003"));
                    return Json(200, new { character_name = ch["character_name"], character_level = ch["character_level"], character_exp_rate = "42.123", character_image = "" });
                case "scheduler/character-state":
                    if (ch == null) return Json(400, Error("OPENAPI00003"));
                    var r = Registered[ocid] ? "true" : "false";
                    return Json(200, new Dictionary<string, object>
                    {
                        ["character_name"] = ch["character_name"], ["character_level"] = ch["character_level"],
                        ["daily_contents"] = new object[] { Content("몬스터파크", r, 1, 2), Content("우르스", r, 3, 3), Content("미등록 콘텐츠", "false") },
                        ["weekly_contents"] = new object[] { Content("에픽 던전 : 앵글러 컴퍼니", r) },
                        ["boss_contents"] = new object[] { Boss("스우", reg: r), Boss("검은 마법사", "월간", 2, reg: r) },
                        ["weekly_boss_clear_count"] = 1, ["weekly_boss_clear_limit_count"] = 14
                    });
            }
            return Json(400, Error("OPENAPI00006"));
        }

        private static object Error(string code) => new { error = new { name = code, message = "x" } };

        private static Task<HttpResponseMessage> Json(int status, object body) => Task.FromResult(new HttpResponseMessage((HttpStatusCode)status)
        {
            Content = new StringContent(JsonSerializer.Serialize(body), Encoding.UTF8, "application/json")
        });
    }

    public class SyncEngineTests
    {
        private long _now = T.Now;
        private readonly FakeNexon _fake = new FakeNexon();
        private readonly JsonStore _store = JsonStore.InMemory();

        private SyncEngine Engine(HudSettings s) =>
            new SyncEngine(s, _store, x => new NexonApi(new HttpClient(_fake), x.ApiKey, "https://api.test"), () => _now);

        [Fact]
        public async Task 계정에서_스케줄러_등록_캐릭터를_찾는다()
        {
            var engine = Engine(new HudSettings { ApiKey = "test_key" });
            await engine.SyncAsync(false);

            Assert.Null(engine.Notice);
            Assert.Equal(new[] { "메인캐릭", "부캐하나", "부캐둘" }, engine.Chars.Select(c => c.Name));
            Assert.Equal(new[]
            {
                "character/list",
                "scheduler/character-state?ocid=o1", "scheduler/character-state?ocid=o2",
                "scheduler/character-state?ocid=o3", "scheduler/character-state?ocid=o4",
                "character/basic?ocid=o1", "character/basic?ocid=o2", "character/basic?ocid=o4"
            }, _fake.Calls);

            // 캐시가 살아 있으면 다시 불러도 호출하지 않는다
            _fake.Calls.Clear();
            _now += 60 * 1000;
            await engine.SyncAsync(false);
            Assert.Empty(_fake.Calls);

            // 갱신 주기가 지나면 스케줄러만 다시 받는다
            _now += 15 * 60 * 1000;
            await engine.SyncAsync(false);
            Assert.Equal(new[] { "scheduler/character-state?ocid=o1", "scheduler/character-state?ocid=o2", "scheduler/character-state?ocid=o4" }, _fake.Calls);
        }

        [Fact]
        public async Task 이름으로_지정하면_그_순서대로_없는_캐릭터는_오류()
        {
            var engine = Engine(new HudSettings { ApiKey = "test_key", Characters = "부캐둘, 없는캐릭,메인캐릭", ShowAvatar = false });
            await engine.SyncAsync(false);
            Assert.Equal(new[] { "부캐둘", "없는캐릭", "메인캐릭" }, engine.Chars.Select(c => c.Name));
            Assert.Equal("캐릭터를 찾을 수 없습니다", engine.Chars[1].Error);
            Assert.NotNull(engine.Chars[0].Body);
            Assert.DoesNotContain(_fake.Calls, c => c.StartsWith("character/basic"));
        }

        [Fact]
        public async Task 잘못된_키는_전체_알림()
        {
            var engine = Engine(new HudSettings { ApiKey = "wrong" });
            await engine.SyncAsync(false);
            Assert.Equal("error", engine.Notice.Level);
            Assert.Equal("API 키가 올바르지 않습니다", engine.Notice.Text);
        }

        [Fact]
        public async Task 키가_없으면_데모()
        {
            var engine = Engine(new HudSettings());
            await engine.SyncAsync(false);
            Assert.True(engine.Demo);
            Assert.Equal(3, engine.Chars.Count);
            Assert.Empty(_fake.Calls);
        }

        [Fact]
        public async Task 다시_켜면_지난_캐릭터와_캐시를_먼저_보여준다()
        {
            var s = new HudSettings { ApiKey = "test_key" };
            await Engine(s).SyncAsync(false);
            var fresh = Engine(s);
            fresh.Changed += () => { };
            var first = new List<string>();
            fresh.Changed += () => { if (first.Count == 0) first.AddRange(fresh.Chars.Where(c => c.Body != null).Select(c => c.Name)); };
            _fake.Calls.Clear();
            await fresh.SyncAsync(false);
            Assert.Equal(new[] { "메인캐릭", "부캐하나", "부캐둘" }, first);
            Assert.Empty(_fake.Calls);
        }

        [Fact]
        public async Task 표시_설정과_접기는_컨트롤러가_저장한다()
        {
            var s = new HudSettings { ApiKey = "test_key", ShowAvatar = false };
            var engine = Engine(s);
            await engine.SyncAsync(false);
            var ctl = new HudController(engine, _store, s, () => _now);

            var view = ctl.Build();
            Assert.Equal(3, view.Cards.Count);
            Assert.Equal(2, view.Cards[0].Model.Daily.Count);

            ctl.EditSelection("o1", "group", "bossMonthly");
            ctl.EditSelection("o1", "item", "daily:우르스");
            var card = ctl.Build().Cards[0];
            Assert.Equal(new[] { "몬스터파크" }, card.Model.Daily.Select(i => i.Name));
            Assert.Equal(new[] { "스우" }, card.Model.Boss.Select(i => i.Name));
            Assert.Equal(2, ctl.Build().Cards[1].Model.Boss.Count);

            ctl.ToggleCollapse("o2");
            Assert.True(ctl.Build().Cards[1].Collapsed);
            ctl.ToggleEdit("o2");
            Assert.True(ctl.Build().Cards[1].Editing);
            ctl.EditSelection("o1", "reset", null);
            Assert.Equal(2, ctl.Build().Cards[0].Model.Daily.Count);
        }
    }
}
