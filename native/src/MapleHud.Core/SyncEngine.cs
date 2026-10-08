using System;
using System.Collections.Generic;
using System.Linq;
using System.Text.Json;
using System.Threading.Tasks;

namespace MapleHud.Core
{
    public sealed class CharState
    {
        public string Name;
        public string Ocid;
        public string World;
        public string Cls;
        public int Level;
        public string Body;        // scheduler/character-state 응답 JSON
        public long FetchedAt;
        public string Basic;       // character/basic 응답 JSON (아바타, 경험치)
        public string Error;
        public bool Loading;

        public string Key => string.IsNullOrEmpty(Ocid) ? Name : Ocid;
    }

    public sealed class Notice
    {
        public string Level;   // info, success, warn, error
        public string Text;
        public Notice(string level, string text) { Level = level; Text = text; }
    }

    /// <summary>
    /// 동기화 흐름
    ///  1) 캐릭터 결정: 설정에 이름이 있으면 그 캐릭터들, 없으면 계정 캐릭터 목록(character/list)에서
    ///     인게임 스케줄러에 항목이 등록된 캐릭터를 레벨 순으로 찾는다.
    ///  2) 캐릭터마다 scheduler/character-state (+ 아바타용 character/basic) 조회
    ///  3) 응답은 저장소에 캐시해서 다시 켜도 바로 보이고, 호출량도 아낀다.
    /// 타이머는 앱이 1초마다 Tick()을 불러서 처리한다.
    /// </summary>
    public sealed class SyncEngine
    {
        private const long Min = 60 * 1000;
        private const long Hour = 60 * Min;
        private const long TtlList = 12 * Hour;
        private const long TtlOcid = 30 * 24 * Hour;
        private const long TtlBasic = 6 * Hour;
        private const long TtlDiscovery = 6 * Hour;
        private const int MaxScan = 40;
        private const long ManualRefreshGap = 30 * 1000;

        private readonly JsonStore _store;
        private readonly Func<HudSettings, NexonApi> _apiFactory;
        private readonly Func<long> _now;
        private HudSettings _settings;
        private bool? _pendingForce;
        private long _lastManual;

        public List<CharState> Chars { get; private set; } = new List<CharState>();
        public Notice Notice { get; private set; }
        public bool Syncing { get; private set; }
        /// <summary>동기화 중 진행 상황 ("캐릭터 확인 중 3/12" 등). 동기화가 아니면 null</summary>
        public string Progress { get; private set; }
        public bool Demo { get; private set; }
        public long LastSync { get; private set; }
        public long NextSyncAt { get; private set; }
        /// <summary>마지막으로 난 예상 못 한 오류 (앱이 로그로 남긴다)</summary>
        public Exception LastError { get; private set; }

        /// <summary>표시할 내용이 바뀌었을 때 (다시 그리기)</summary>
        public event Action Changed;

        public SyncEngine(HudSettings settings, JsonStore store, Func<HudSettings, NexonApi> apiFactory, Func<long> now = null)
        {
            _settings = settings;
            _store = store;
            _apiFactory = apiFactory;
            _now = now ?? KstTime.NowMs;
        }

        private long Now => _now();
        private long RefreshMs => Math.Max(5, _settings.RefreshMin) * Min;
        private void Emit() => Changed?.Invoke();

        public void UpdateSettings(HudSettings settings)
        {
            bool dataChanged = !settings.DataEquals(_settings);
            bool keyChanged = settings.ApiKey != _settings.ApiKey || settings.ApiBase != _settings.ApiBase;
            _settings = settings;
            if (keyChanged) Chars = new List<CharState>();
            if (dataChanged) _ = SyncAsync(false);
            else ScheduleNext();
        }

        /// <summary>1초마다 호출. idle이면(전체 화면 게임 등) 갱신하지 않고, 다시 보일 때 밀린 갱신을 한다</summary>
        public void Tick(bool idle)
        {
            if (idle || Syncing || Demo) return;
            if (Now >= NextSyncAt) _ = SyncAsync(false);
        }

        public bool ManualRefresh()
        {
            var now = Now;
            if (Syncing || now - _lastManual < ManualRefreshGap) return false;
            _lastManual = now;
            _ = SyncAsync(true);
            return true;
        }

        private void ScheduleNext()
        {
            var now = Now;
            // 정기 갱신 + 자정 초기화 후 API 반영 시점에 한 번 더
            var afterReset = KstTime.NextReset(ResetKind.Daily, now) + Scheduler.DataDelayMs + Min;
            NextSyncAt = Math.Max(now + Min, Math.Min(now + RefreshMs, afterReset));
        }

        /* ---------- 동기화 ---------- */

        public async Task SyncAsync(bool force)
        {
            if (Syncing)
            {
                _pendingForce = force || (_pendingForce ?? false);
                return;
            }
            var settings = _settings;

            if (settings.Demo || (string.IsNullOrEmpty(settings.ApiKey) && string.IsNullOrEmpty(settings.ApiBase)))
            {
                LoadDemo();
                Emit();
                return;
            }

            if (Demo) Chars = new List<CharState>();
            Demo = false;
            Syncing = true;
            Notice = null;
            Progress = "캐릭터 확인 중";
            if (Chars.Count == 0)
            {
                // 다시 켜졌을 때 지난번 캐릭터와 캐시를 먼저 보여준다 (오프라인이어도)
                var last = _store.Get<LastTargets>("targets");
                if (last != null && last.Sig == TargetsSig(settings)) Chars = Adopt(last.List ?? new List<Target>());
            }
            Emit();

            try
            {
                var api = _apiFactory(settings);
                var names = ParseNames(settings.Characters);
                var targets = names.Count > 0
                    ? await ResolveByNames(api, names).ConfigureAwait(true)
                    : await ResolveFromAccount(api, settings).ConfigureAwait(true);
                Chars = Adopt(targets);
                _store.Set("targets", new LastTargets { Sig = TargetsSig(settings), List = targets });
                Emit();
                // 스케줄러를 먼저 모두 받아 화면을 채우고, 캐릭터 이미지는 그다음에 받는다
                var list = Chars;
                for (int i = 0; i < list.Count; i++)
                {
                    SetProgress("스케줄러 받는 중 " + (i + 1) + "/" + list.Count);
                    await LoadSchedule(api, list[i], force).ConfigureAwait(true);
                }
                if (settings.ShowAvatar)
                {
                    SetProgress("캐릭터 이미지 받는 중");
                    foreach (var c in list) await LoadBasic(api, c).ConfigureAwait(true);
                }
                LastSync = Now;
            }
            catch (ApiException e)
            {
                Notice = new Notice("error", e.Message);
            }
            catch (Exception e) when (!(e is OutOfMemoryException))
            {
                // 응답 형식이 바뀌는 등 예상 못 한 문제도 화면에 알린다
                Notice = new Notice("error", "동기화 중 오류: " + e.Message);
                LastError = e;
            }
            finally
            {
                Syncing = false;
                Progress = null;
                ScheduleNext();
                _store.Flush();
                Emit();
            }

            if (_pendingForce.HasValue)
            {
                var next = _pendingForce.Value;
                _pendingForce = null;
                await SyncAsync(next).ConfigureAwait(true);
            }
        }

        private void SetProgress(string text)
        {
            Progress = text;
            Emit();
        }

        private void LoadDemo()
        {
            Demo = true;
            Notice = null;
            var now = Now;
            Chars = DemoData.Build(now - 4 * Min);
            LastSync = now;
            NextSyncAt = long.MaxValue;
        }

        public static List<string> ParseNames(string text)
        {
            var seen = new HashSet<string>();
            return (text ?? "").Split(',', '\n', ';').Select(s => s.Trim())
                .Where(s => s.Length > 0 && seen.Add(s)).ToList();
        }

        // 캐릭터 구성이 바뀌는 설정(키 끝자리, 이름 목록, 레벨 조건)
        private static string TargetsSig(HudSettings s)
        {
            var key = s.ApiKey ?? "";
            return string.Join("|", key.Length > 8 ? key.Substring(key.Length - 8) : key, s.ApiBase, s.Characters, s.MinLevel, s.MaxChars);
        }

        private static List<Target> FlattenList(string json)
        {
            var list = new List<Target>();
            if (string.IsNullOrEmpty(json)) return list;
            var root = J.Parse(json);
            foreach (var acc in J.Arr(root, "account_list"))
            {
                foreach (var c in J.Arr(acc, "character_list"))
                {
                    list.Add(new Target
                    {
                        Name = J.Str(c, "character_name"),
                        Ocid = J.Str(c, "ocid"),
                        World = J.Str(c, "world_name"),
                        Cls = J.Str(c, "character_class"),
                        Level = (int)J.Num(c, "character_level")
                    });
                }
            }
            return list;
        }

        private async Task<CachedJson> Cached(string key, long ttl, bool force, Func<Task<string>> fetch)
        {
            if (!force)
            {
                var hit = _store.GetCached(key, ttl, Now);
                if (hit != null) return hit;
            }
            var data = await fetch().ConfigureAwait(true);
            return _store.SetCached(key, data, Now);
        }

        // 갱신 주기보다 살짝 짧게 잡아야 다음 주기에 새로 받아온다
        private Task<CachedJson> SchedulerEntry(NexonApi api, string ocid, bool force) =>
            Cached("sched:" + ocid, RefreshMs - 30 * 1000, force, () => api.SchedulerAsync(ocid));

        private async Task<List<Target>> ResolveByNames(NexonApi api, List<string> names)
        {
            var known = new Dictionary<string, Target>();
            var listEntry = _store.GetCached("charlist", null, Now);
            foreach (var c in FlattenList(listEntry?.Data)) known[c.Name] = c;

            var result = new List<Target>();
            foreach (var name in names)
            {
                SetProgress("캐릭터 확인 중 " + (result.Count + 1) + "/" + names.Count);
                if (known.TryGetValue(name, out var t))
                {
                    result.Add(t);
                    continue;
                }
                try
                {
                    var entry = await Cached("ocid:" + name, TtlOcid, false, async () =>
                        JsonSerializer.Serialize(await api.OcidAsync(name).ConfigureAwait(true))).ConfigureAwait(true);
                    result.Add(new Target { Name = name, Ocid = JsonSerializer.Deserialize<string>(entry.Data) });
                }
                catch (ApiException e) when (!e.IsFatal)
                {
                    result.Add(new Target { Name = name, Error = e.Code == "OPENAPI00004" ? "캐릭터를 찾을 수 없습니다" : e.Message });
                }
            }
            return result;
        }

        private async Task<List<Target>> ResolveFromAccount(NexonApi api, HudSettings s)
        {
            var entry = await Cached("charlist", TtlList, false, api.CharacterListAsync).ConfigureAwait(true);
            var all = FlattenList(entry.Data).Where(c => c.Level >= s.MinLevel)
                .OrderByDescending(c => c.Level).Take(MaxScan).ToList();
            if (all.Count == 0)
            {
                Notice = new Notice("warn", "레벨 " + s.MinLevel + " 이상 캐릭터가 계정에 없습니다. 최소 레벨 설정을 확인하세요.");
                return all;
            }

            int maxChars = Math.Max(1, s.MaxChars);
            var found = new List<Target>();
            // 스케줄러에 항목이 등록된 캐릭터를 레벨 순으로 찾는다. 결과는 TtlDiscovery 동안 재사용
            int scanned = 0;
            foreach (var c in all)
            {
                if (found.Count >= maxChars) break;
                SetProgress("스케줄러 등록 캐릭터 찾는 중 " + (++scanned) + "/" + all.Count);
                var reg = _store.GetCached("reg:" + c.Ocid, TtlDiscovery, Now);
                if (reg != null)
                {
                    if (reg.Data == "true") found.Add(c);
                    continue;
                }
                try
                {
                    var sched = await SchedulerEntry(api, c.Ocid, false).ConfigureAwait(true);
                    bool registered = Scheduler.HasRegistered(J.Parse(sched.Data));
                    _store.SetCached("reg:" + c.Ocid, registered ? "true" : "false", Now);
                    if (registered) found.Add(c);
                }
                catch (ApiException e) when (!e.IsFatal)
                {
                    // 이 캐릭터만 건너뛴다
                }
            }
            if (found.Count > 0) return found;
            Notice = new Notice("info", "인게임 스케줄러에 등록된 캐릭터가 없어 레벨 순으로 표시합니다.");
            return all.Take(maxChars).ToList();
        }

        // 이전 동기화 결과/캐시를 이어받아 로딩 중에도 화면이 비지 않게 한다
        private List<CharState> Adopt(List<Target> targets)
        {
            var prev = new Dictionary<string, CharState>();
            foreach (var c in Chars) prev[c.Key] = c;
            var now = Now;
            return targets.Select(t =>
            {
                var key = string.IsNullOrEmpty(t.Ocid) ? t.Name : t.Ocid;
                prev.TryGetValue(key, out var old);
                var c = new CharState
                {
                    Name = t.Name,
                    Ocid = t.Ocid,
                    World = t.World ?? old?.World,
                    Cls = t.Cls ?? old?.Cls,
                    Level = t.Level != 0 ? t.Level : old?.Level ?? 0,
                    Body = old?.Body,
                    FetchedAt = old?.FetchedAt ?? 0,
                    Basic = old?.Basic,
                    Error = t.Error
                };
                if (!string.IsNullOrEmpty(c.Ocid) && c.Body == null)
                {
                    var s = _store.GetCached("sched:" + c.Ocid, null, now);
                    if (s != null) { c.Body = s.Data; c.FetchedAt = s.At; }
                }
                if (!string.IsNullOrEmpty(c.Ocid) && c.Basic == null)
                {
                    c.Basic = _store.GetCached("basic:" + c.Ocid, null, now)?.Data;
                }
                return c;
            }).ToList();
        }

        private async Task LoadSchedule(NexonApi api, CharState c, bool force)
        {
            if (string.IsNullOrEmpty(c.Ocid)) return;
            c.Loading = true;
            Emit();
            try
            {
                var entry = await SchedulerEntry(api, c.Ocid, force).ConfigureAwait(true);
                c.Body = entry.Data;
                c.FetchedAt = entry.At;
                c.Error = null;
                _store.SetCached("reg:" + c.Ocid, Scheduler.HasRegistered(J.Parse(entry.Data)) ? "true" : "false", Now);
            }
            catch (ApiException e) when (!e.IsFatal)
            {
                if (e.Code == "OPENAPI00003") _store.Remove("ocid:" + c.Name);
                c.Error = e.Message;
            }
            finally
            {
                c.Loading = false;
                Emit();
            }
        }

        private async Task LoadBasic(NexonApi api, CharState c)
        {
            if (string.IsNullOrEmpty(c.Ocid) || c.Error != null) return;
            try
            {
                var basic = await Cached("basic:" + c.Ocid, TtlBasic, false, () => api.BasicAsync(c.Ocid)).ConfigureAwait(true);
                if (basic.Data != c.Basic)
                {
                    c.Basic = basic.Data;
                    Emit();
                }
            }
            catch (ApiException e) when (!e.IsFatal)
            {
                // 아바타는 없어도 된다
            }
        }

        public sealed class Target
        {
            public string Name { get; set; }
            public string Ocid { get; set; }
            public string World { get; set; }
            public string Cls { get; set; }
            public int Level { get; set; }
            public string Error { get; set; }
        }

        public sealed class LastTargets
        {
            public string Sig { get; set; }
            public List<Target> List { get; set; }
        }
    }
}
