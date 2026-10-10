using System.Collections.Generic;
using System.Globalization;
using System.Linq;

namespace MapleHud.Core
{
    public sealed class CardView
    {
        public string Key;
        public string Name;
        public int Level;
        public string Cls;
        public string World;
        public string Exp;          // "61.20"
        public string ImageUrl;
        public SchedModel Model;
        public bool Loading;
        public string Error;
        public bool Editing;
        public bool Collapsed;
        public bool Complete => Model != null && Model.CountAll.Complete;
    }

    public sealed class HudView
    {
        public List<CardView> Cards = new List<CardView>();
        public Notice Notice;
        /// <summary>앱이 잠깐 보여주는 알림 ("설정을 저장했습니다" 등). 동기화 알림보다 위에 표시</summary>
        public Notice Flash;
        public bool Demo;
        public bool Syncing;
        public string Progress;
        public long LastSync;
        public int RemainDaily, RemainWeekly, RemainBoss;
    }

    /// <summary>
    /// 화면 상태와 사용자 조작(접기, 표시 항목 편집). 캐릭터별 상태는 사용자 설정 저장소(prefs.json)에 둔다.
    /// API 캐시와 따로 두어서 캐시가 깨지거나 지워져도 사용자가 고른 것은 남는다.
    ///   sel:{key}      표시 항목 선택 (Selection)
    ///   collapse:{key} 접기 상태 { v, day } — 접은 상태는 유지, 펼친 상태는 그날만 유효
    /// </summary>
    public sealed class HudController
    {
        private readonly SyncEngine _engine;
        private readonly JsonStore _store;   // 사용자 설정 (prefs)
        private readonly System.Func<long> _now;

        public HudSettings Settings { get; set; }
        public string Editing { get; private set; }

        public HudController(SyncEngine engine, JsonStore prefs, HudSettings settings, System.Func<long> now = null)
        {
            _engine = engine;
            _store = prefs;
            Settings = settings;
            _now = now ?? KstTime.NowMs;
        }

        /// <summary>
        /// 예전 버전은 사용자 설정을 API 캐시(store.json)에 같이 두었다. prefs.json이 처음 생길 때 옮겨 온다.
        /// </summary>
        public static int MigratePrefs(JsonStore cache, JsonStore prefs)
        {
            var keys = cache.Keys.Where(IsPrefKey).ToList();
            foreach (var k in keys)
            {
                if (prefs.GetRaw(k) == null) prefs.SetRaw(k, cache.GetRaw(k));
                cache.Remove(k);
            }
            return keys.Count;
        }

        private static bool IsPrefKey(string key) => key.StartsWith("sel:") || key.StartsWith("collapse:");

        public Selection SelectionOf(string key) => _store.Get<Selection>("sel:" + key);

        private sealed class CollapsePref
        {
            public bool V { get; set; }
            public long Day { get; set; }
        }

        private bool? CollapsedPref(string key, long now)
        {
            var pref = _store.Get<CollapsePref>("collapse:" + key);
            if (pref == null) return null;
            if (!pref.V && pref.Day != KstTime.LastReset(ResetKind.Daily, now)) return null;
            return pref.V;
        }

        public HudView Build()
        {
            var now = _now();
            var view = new HudView
            {
                Notice = _engine.Notice,
                Demo = _engine.Demo,
                Syncing = _engine.Syncing,
                Progress = _engine.Progress,
                LastSync = _engine.LastSync
            };
            foreach (var c in _engine.Chars)
            {
                var key = c.Key;
                SchedModel model = null;
                if (c.Body != null)
                {
                    model = Scheduler.Normalize(J.Parse(c.Body), c.FetchedAt, now, Settings.ShowAll, SelectionOf(key));
                }
                var basic = J.Parse(c.Basic);
                var expRate = J.Num(basic, "character_exp_rate");
                var card = new CardView
                {
                    Key = key,
                    Name = FirstNonEmpty(model?.Name, J.Str(basic, "character_name"), c.Name),
                    Level = FirstPositive((int)J.Num(basic, "character_level"), model?.Level ?? 0, c.Level),
                    Cls = FirstNonEmpty(J.Str(basic, "character_class"), model?.Cls, c.Cls),
                    World = FirstNonEmpty(J.Str(basic, "world_name"), model?.World, c.World),
                    Exp = J.Str(basic, "character_exp_rate").Length > 0 ? expRate.ToString("0.00", CultureInfo.InvariantCulture) : "",
                    ImageUrl = Settings.ShowAvatar ? J.Str(basic, "character_image") : "",
                    Model = model,
                    Loading = c.Loading,
                    Error = c.Error,
                    Editing = Editing == key && model != null
                };
                var pref = CollapsedPref(key, now);
                card.Collapsed = pref ?? (Settings.CollapseDone && card.Complete);
                view.Cards.Add(card);
                if (model == null) continue;
                view.RemainDaily += model.CountDaily.Remaining;
                view.RemainWeekly += model.CountWeekly.Remaining;
                view.RemainBoss += model.CountBoss.Remaining;
            }
            return view;
        }

        private static string FirstNonEmpty(params string[] values) => values.FirstOrDefault(v => !string.IsNullOrEmpty(v)) ?? "";

        private static int FirstPositive(params int[] values) => values.FirstOrDefault(v => v > 0);

        /* ---------- 조작 ---------- */

        public void ToggleCollapse(string key)
        {
            var card = Build().Cards.FirstOrDefault(c => c.Key == key);
            if (card == null || card.Editing) return;
            _store.Set("collapse:" + key, new CollapsePref { V = !card.Collapsed, Day = KstTime.LastReset(ResetKind.Daily, _now()) });
        }

        public void ToggleEdit(string key) => Editing = Editing == key ? null : key;

        /// <summary>action: item, group, all, none, keep(유지하기), reset(인게임 등록 기준으로 되돌리기)</summary>
        public void EditSelection(string key, string action, string id)
        {
            if (action == "reset")
            {
                _store.Remove("sel:" + key);
                return;
            }
            var card = Build().Cards.FirstOrDefault(c => c.Key == key);
            if (card?.Model == null) return;
            _store.Set("sel:" + key, Scheduler.EditSelection(SelectionOf(key), card.Model, action, id));
        }
    }
}
