using System;
using System.Diagnostics;
using System.Net.Http;
using System.Threading.Tasks;
using MapleHud.Core.Render;
using SkiaSharp;
using Xunit;
using Xunit.Abstractions;

namespace MapleHud.Core.Tests
{
    /// <summary>15분 주기 동기화와 다시 그리기를 오래 반복해도 메모리가 늘지 않는지</summary>
    [Collection("serial")]
    public class LeakTests
    {
        private readonly ITestOutputHelper _out;
        public LeakTests(ITestOutputHelper output) { _out = output; }

        [Fact]
        public async Task 동기화와_그리기를_반복해도_메모리가_늘지_않는다()
        {
            int gap = NexonApi.MinGapMs;
            NexonApi.MinGapMs = 0;
            try
            {
                long now = T.Now;
                var fake = new FakeNexon();
                var http = new HttpClient(fake);
                var store = JsonStore.InMemory();
                var s = new HudSettings { ApiKey = "test_key" };
                var engine = new SyncEngine(s, store, x => new NexonApi(http, x.ApiKey, "https://api.test"), () => now);
                var ctl = new HudController(engine, store, s, () => now);
                using (var fonts = FontSet.Load(RenderTests.FontsDir()))
                using (var renderer = new HudRenderer(fonts))
                {
                    int version = 0;
                    int calls = 0;
                    long Cycle()
                    {
                        engine.SyncAsync(false).GetAwaiter().GetResult();
                        var input = new RenderInput { View = ctl.Build(), Settings = s, Now = now, MaxHeight = 1000, BodyVersion = ++version };
                        var layout = renderer.Measure(input);
                        using (var bmp = new SKBitmap((int)layout.Width + 1, (int)layout.Height + 1))
                        using (var canvas = new SKCanvas(bmp))
                        {
                            renderer.Draw(canvas, input);
                            // 매초 그리기 (카드 영역 캐시 사용)
                            for (int i = 0; i < 5; i++)
                            {
                                input.Now += 1000;
                                renderer.Draw(canvas, input);
                            }
                        }
                        now += 15 * 60 * 1000;
                        // 가짜 서버가 기록하는 호출 목록은 비워서 측정에서 뺀다
                        calls += fake.Calls.Count;
                        fake.Calls.Clear();
                        return 0;
                    }

                    long Managed()
                    {
                        GC.Collect();
                        GC.WaitForPendingFinalizers();
                        GC.Collect();
                        return GC.GetTotalMemory(true);
                    }

                    for (int i = 0; i < 50; i++) Cycle();
                    long m1 = Managed();
                    long p1 = Process.GetCurrentProcess().PrivateMemorySize64;
                    var sw = Stopwatch.StartNew();
                    for (int i = 0; i < 300; i++) Cycle();
                    long mid = Managed();
                    for (int i = 0; i < 300; i++) Cycle();
                    long m2 = Managed();
                    _out.WriteLine($"managed after 350: {mid >> 10}KB, after 650: {m2 >> 10}KB");
                    var proc = Process.GetCurrentProcess();
                    proc.Refresh();
                    long p2 = proc.PrivateMemorySize64;
                    _out.WriteLine($"600 cycles in {sw.ElapsedMilliseconds}ms, managed {m1 >> 10}KB -> {m2 >> 10}KB, private {p1 >> 20}MB -> {p2 >> 20}MB, api calls {calls}");
                    Assert.True(m2 - m1 < 256 * 1024, $"managed grew {(m2 - m1) >> 10}KB");
                    Assert.True(calls > 1800);
                }
            }
            finally
            {
                NexonApi.MinGapMs = gap;
            }
        }
    }
}
