using System.IO;
using Xunit;
using static MapleHud.Core.Tests.T;

namespace MapleHud.Core.Tests
{
    public class StoreAndSettingsTests
    {
        [Fact]
        public void 오래된_캐시만_정리()
        {
            var store = JsonStore.InMemory();
            store.SetCached("sched:old", "{\"a\":1}", Now - 40 * Day);
            store.SetCached("sched:new", "{\"a\":2}", Now - 2 * Day);
            store.Set("sel:x", new Selection());
            store.SetRaw("collapse:o1", "{\"V\":true,\"Day\":0}");
            Assert.Equal(1, store.Prune(31 * Day, Now));
            Assert.Null(store.GetRaw("sched:old"));
            Assert.Equal("{\"a\":2}", store.GetCached("sched:new", null, Now).Data);
            Assert.NotNull(store.GetRaw("sel:x"));
            Assert.NotNull(store.GetRaw("collapse:o1"));
        }

        [Fact]
        public void 캐시_유효_시간()
        {
            var store = JsonStore.InMemory();
            store.SetCached("ttl", "1", Now - 10 * Min);
            Assert.NotNull(store.GetCached("ttl", 15 * Min, Now));
            Assert.Null(store.GetCached("ttl", 5 * Min, Now));
        }

        [Fact]
        public void 저장소는_파일로_남는다()
        {
            var path = Path.Combine(Path.GetTempPath(), "maplehud-store-" + System.Guid.NewGuid() + ".json");
            try
            {
                var store = new JsonStore(path);
                store.SetCached("sched:a", "{\"x\":[1,2]}", Now);
                store.Set("sel:a", new Selection { Items = { ["daily:우르스"] = false } });
                store.Flush();
                var again = new JsonStore(path);
                Assert.Equal("{\"x\":[1,2]}", again.GetCached("sched:a", null, Now).Data);
                Assert.False(again.Get<Selection>("sel:a").Items["daily:우르스"]);
            }
            finally
            {
                File.Delete(path);
            }
        }

        [Fact]
        public void 설정은_범위를_바로잡고_왕복한다()
        {
            var s = HudSettings.FromJson("{\"apiKey\":\" key \",\"columns\":9,\"opacity\":-3,\"alignX\":\"weird\",\"accent\":\"12ABEF\"}");
            Assert.Equal("key", s.ApiKey);
            Assert.Equal(4, s.Columns);
            Assert.Equal(0, s.Opacity);
            Assert.Equal("right", s.AlignX);
            Assert.Equal("#12abef", s.Accent);
            Assert.Equal(s.ToJson(), HudSettings.FromJson(s.ToJson()).ToJson());
            Assert.Equal(200, HudSettings.FromJson("not json").MinLevel);
        }
    }
}
