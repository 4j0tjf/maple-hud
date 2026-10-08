using System;
using System.Collections.Generic;
using System.Linq;
using System.Text.Json;
using System.Text.RegularExpressions;

namespace MapleHud.Core
{
    public sealed class SchedItem
    {
        public string Key;        // 표시 설정에 쓰는 식별자 (분류:이름, 보스는 boss:이름)
        public string Group;      // daily, weekly, bossWeekly, bossMonthly, bossDaily
        public string Name;
        public bool IsBoss;
        public bool Registered;   // 인게임 스케줄러에 등록됨
        public int Now;
        public int Max;
        public bool Quest;        // 횟수가 아니라 퀘스트 진행 상태로 완료를 판단
        public bool Done;
        public bool Progress;
        public ResetKind Reset;
        public string Difficulty = "";
        public string Cycle = "";
        public double Order;
        public bool Visible;
        public bool Stale;
    }

    public sealed class SchedGroup
    {
        public string Id;
        public string Label;
        public bool On;
        public List<SchedItem> Items = new List<SchedItem>();
    }

    public struct Tally
    {
        public int Done;
        public int Total;
        public Tally(int done, int total) { Done = done; Total = total; }
        public bool Complete => Total > 0 && Done == Total;
        public int Remaining => Total - Done;
    }

    public sealed class SchedModel
    {
        public string Name = "";
        public string World = "";
        public int Level;
        public string Cls = "";
        public int RegisteredCount;
        public bool ShowingAll;
        public bool Customized;
        public List<SchedGroup> Groups = new List<SchedGroup>();
        public List<SchedItem> Daily = new List<SchedItem>();
        public List<SchedItem> Weekly = new List<SchedItem>();
        public List<SchedItem> Boss = new List<SchedItem>();
        public int BossClear;
        public int BossLimit;
        public Tally CountDaily, CountWeekly, CountBoss, CountAll;
    }

    /// <summary>캐릭터별 표시 설정. Groups[분류]=false면 분류 전체 숨김, Items[key]가 있으면 기본 규칙보다 우선</summary>
    public sealed class Selection
    {
        public Dictionary<string, bool> Groups { get; set; } = new Dictionary<string, bool>();
        public Dictionary<string, bool> Items { get; set; } = new Dictionary<string, bool>();
        public bool IsEmpty => (Groups == null || Groups.Count == 0) && (Items == null || Items.Count == 0);

        public Selection Clone() => new Selection
        {
            Groups = new Dictionary<string, bool>(Groups ?? new Dictionary<string, bool>()),
            Items = new Dictionary<string, bool>(Items ?? new Dictionary<string, bool>())
        };
    }

    /// <summary>
    /// /maplestory/v1/scheduler/character-state 응답을 HUD용 모델로 정리한다.
    /// - 인게임 스케줄러에 등록된(registration_flag) 항목만 보여주는 것이 기본
    /// - 캐릭터마다 사용자가 고른 표시 설정(Selection)이 있으면 그걸 먼저 따른다
    /// - 데이터를 받은 뒤 초기화 시각이 지났다면 완료 표시를 미리 해제한다 (API 반영 지연 대비)
    /// </summary>
    public static class Scheduler
    {
        // 초기화 직후 API는 한동안(평균 15분) 이전 데이터를 줄 수 있다
        public const long DataDelayMs = 20 * 60 * 1000;

        public static readonly string[][] GroupDefs =
        {
            new[] { "daily", "일일" },
            new[] { "weekly", "주간" },
            new[] { "bossWeekly", "주간 보스" },
            new[] { "bossMonthly", "월간 보스" },
            new[] { "bossDaily", "일일 보스" }
        };

        private static readonly Regex WeekdaySuffix = new Regex("[월화수목금토일]요일");

        /// <summary>보스 초기화 주기 문자열 → 초기화 종류. "매주 목요일" 같은 표현의 "요일"은 무시한다</summary>
        public static ResetKind CycleToReset(string cycle)
        {
            var s = WeekdaySuffix.Replace((cycle ?? "").ToLowerInvariant(), "");
            if (s.Contains("주") || s.Contains("week")) return ResetKind.WeeklyThu;
            if (s.Contains("월") || s.Contains("month")) return ResetKind.Monthly;
            if (s.Contains("일") || s.Contains("day") || s.Contains("daily")) return ResetKind.Daily;
            return ResetKind.WeeklyThu;
        }

        private static string BossGroup(ResetKind reset)
        {
            if (reset == ResetKind.Monthly) return "bossMonthly";
            if (reset == ResetKind.Daily) return "bossDaily";
            return "bossWeekly";
        }

        private static SchedItem Content(JsonElement it, ResetKind reset, string group)
        {
            int now = (int)J.Num(it, "now_count");
            int max = (int)J.Num(it, "max_count");
            var questState = J.StrOrNull(it, "quest_state");
            var type = J.Str(it, "type");
            bool isQuest = type == "quest" || (max <= 0 && questState != null);
            bool done = isQuest ? questState == "2" : (max > 0 && now >= max);
            var name = J.Str(it, "content_name");
            return new SchedItem
            {
                Key = group + ":" + name,
                Group = group,
                Name = name,
                Registered = J.Flag(it, "registration_flag"),
                Now = now,
                Max = max,
                Quest = isQuest,
                Done = done,
                Progress = !done && (questState == "1" || now > 0),
                Reset = reset
            };
        }

        // 보스는 난이도가 주마다 바뀔 수 있어서 이름으로만 구분한다
        private static SchedItem Boss(JsonElement it)
        {
            var name = J.Str(it, "content_name");
            var cycle = J.Str(it, "cycle");
            var reset = CycleToReset(cycle);
            return new SchedItem
            {
                Key = "boss:" + name,
                Group = BossGroup(reset),
                Name = name,
                IsBoss = true,
                Difficulty = J.Str(it, "difficulty"),
                Cycle = cycle,
                Order = J.Num(it, "list_order_no"),
                Registered = J.Flag(it, "registration_flag"),
                Done = J.Flag(it, "complete_flag"),
                Reset = reset
            };
        }

        // 데이터가 마지막 초기화(+반영 지연) 이전 것이면 완료 상태를 해제
        private static bool IsStale(ResetKind reset, long fetchedAt, long now, long delay) =>
            fetchedAt < KstTime.LastReset(reset, now) + delay;

        private static Tally Count(List<SchedItem> items) => new Tally(items.Count(i => i.Done), items.Count);

        public static SchedModel Normalize(JsonElement body, long fetchedAt, long now,
            bool showAll = false, Selection selection = null, long delay = DataDelayMs)
        {
            var groupsSel = selection?.Groups ?? new Dictionary<string, bool>();
            var itemsSel = selection?.Items ?? new Dictionary<string, bool>();

            var daily = J.Arr(body, "daily_contents").Select(i => Content(i, ResetKind.Daily, "daily")).ToList();
            var weekly = J.Arr(body, "weekly_contents").Select(i => Content(i, ResetKind.WeeklyMon, "weekly")).ToList();
            // OrderBy는 같은 순서값끼리 원래 순서를 유지한다
            var boss = J.Arr(body, "boss_contents").Select(Boss).OrderBy(b => b.Order).ToList();
            var all = daily.Concat(weekly).Concat(boss).ToList();

            foreach (var it in all)
            {
                if (!IsStale(it.Reset, fetchedAt, now, delay)) continue;
                it.Stale = true;
                if (it.Done || it.Progress)
                {
                    it.Done = false;
                    it.Progress = false;
                    it.Now = 0;
                }
            }

            int registered = all.Count(i => i.Registered);
            // 기본 규칙: 등록된 항목만. 등록된 항목이 하나도 없으면 전체를 보여준다
            bool showAllByDefault = showAll || registered == 0;
            // 사용자 선택: 분류를 끄면 그 분류 전체를 숨기고, 항목별 선택은 기본 규칙보다 우선한다
            foreach (var it in all)
            {
                if (groupsSel.TryGetValue(it.Group, out var groupOn) && !groupOn) it.Visible = false;
                else if (itemsSel.TryGetValue(it.Key, out var on)) it.Visible = on;
                else it.Visible = showAllByDefault || it.Registered;
            }

            int bossClear = (int)J.Num(body, "weekly_boss_clear_count");
            if (IsStale(ResetKind.WeeklyThu, fetchedAt, now, delay)) bossClear = 0;

            var m = new SchedModel
            {
                Name = J.Str(body, "character_name"),
                World = J.Str(body, "world_name"),
                Level = (int)J.Num(body, "character_level"),
                Cls = J.Str(body, "character_class"),
                RegisteredCount = registered,
                ShowingAll = showAllByDefault,
                Customized = selection != null && !selection.IsEmpty,
                Daily = daily.Where(i => i.Visible).ToList(),
                Weekly = weekly.Where(i => i.Visible).ToList(),
                Boss = boss.Where(i => i.Visible).ToList(),
                BossClear = bossClear,
                BossLimit = (int)J.Num(body, "weekly_boss_clear_limit_count")
            };
            foreach (var def in GroupDefs)
            {
                var items = all.Where(i => i.Group == def[0]).ToList();
                if (items.Count == 0) continue;
                m.Groups.Add(new SchedGroup
                {
                    Id = def[0],
                    Label = def[1],
                    On = !(groupsSel.TryGetValue(def[0], out var on) && !on),
                    Items = items
                });
            }
            m.CountDaily = Count(m.Daily);
            m.CountWeekly = Count(m.Weekly);
            m.CountBoss = Count(m.Boss);
            m.CountAll = new Tally(m.CountDaily.Done + m.CountWeekly.Done + m.CountBoss.Done,
                m.CountDaily.Total + m.CountWeekly.Total + m.CountBoss.Total);
            return m;
        }

        /// <summary>
        /// 표시 설정 편집. 원본은 그대로 두고 새 선택을 돌려준다.
        /// model은 지금 선택으로 Normalize한 결과 (항목의 현재 표시 여부를 알기 위해)
        ///   item: 항목 하나 켜기/끄기, group: 분류 전체 켜기/끄기, all/none: 분류의 항목 모두 켜기/끄기
        /// </summary>
        public static Selection EditSelection(Selection selection, SchedModel model, string action, string id)
        {
            var next = (selection ?? new Selection()).Clone();
            var groups = model?.Groups ?? new List<SchedGroup>();
            switch (action)
            {
                case "item":
                    foreach (var it in groups.SelectMany(g => g.Items).Where(i => i.Key == id))
                        next.Items[id] = !it.Visible;
                    break;
                case "group":
                    if (next.Groups.TryGetValue(id, out var on) && !on) next.Groups.Remove(id);
                    else next.Groups[id] = false;
                    break;
                case "all":
                case "none":
                    var g = groups.FirstOrDefault(x => x.Id == id);
                    if (g != null) foreach (var it in g.Items) next.Items[it.Key] = action == "all";
                    if (action == "all") next.Groups.Remove(id);
                    break;
            }
            return next;
        }

        public static bool HasRegistered(JsonElement body) =>
            new[] { "daily_contents", "weekly_contents", "boss_contents" }
                .Any(list => J.Arr(body, list).Any(i => J.Flag(i, "registration_flag")));
    }
}
