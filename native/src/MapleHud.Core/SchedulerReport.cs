using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using System.Text;
using System.Text.Json;

namespace MapleHud.Core
{
    /// <summary>
    /// 문제 확인용: 넥슨 API가 돌려준 스케줄러 값과 HUD의 판정을 항목별로 나란히 적은 글.
    /// "완료했는데 안 바뀐다"가 API 쪽 갱신 지연인지 HUD 판정 문제인지 가릴 수 있다.
    /// </summary>
    public static class SchedulerReport
    {
        public static string Build(IEnumerable<CharState> chars, long now, long delay = Scheduler.DataDelayMs)
        {
            var sb = new StringBuilder();
            var clock = KstTime.FormatClock(now);
            sb.AppendLine("Maple Scheduler HUD · 스케줄러 원본 (" + clock.Date + " " + clock.Time + " KST)");
            sb.AppendLine("넥슨 API가 돌려준 값(왼쪽)과 HUD가 판정한 상태(→ 오른쪽)입니다.");
            sb.AppendLine("API 값이 아직 미완료면 넥슨 쪽에 반영되기 전입니다. 게임 접속을 종료하고 15분쯤 뒤에 다시 확인해 보세요.");
            foreach (var c in chars)
            {
                sb.AppendLine();
                sb.AppendLine("[" + c.Name + "]" + (c.Level > 0 ? " Lv." + c.Level : "") +
                    (string.IsNullOrEmpty(c.Cls) ? "" : " " + c.Cls) + (string.IsNullOrEmpty(c.World) ? "" : " · " + c.World));
                if (c.Error != null) sb.AppendLine("  오류: " + c.Error);
                if (c.Body == null)
                {
                    sb.AppendLine("  아직 받은 스케줄러가 없습니다");
                    continue;
                }
                var t = KstTime.FormatClock(c.FetchedAt);
                sb.AppendLine("  받은 시각: " + t.Date + " " + t.Time + " (" + KstTime.FormatAgo(now - c.FetchedAt) + ")");
                var body = J.Parse(c.Body);
                var date = J.StrOrNull(body, "date");
                if (date != null) sb.AppendLine("  데이터 기준일: " + date);

                // 판정은 사용자 선택과 상관없이 전체 항목으로
                var model = Scheduler.Normalize(body, c.FetchedAt, now, showAll: true, delay: delay);
                var verdict = model.Groups.SelectMany(g => g.Items).GroupBy(i => i.Key).ToDictionary(g => g.Key, g => g.First());
                Contents(sb, "일일", J.Arr(body, "daily_contents"), "daily", verdict);
                Contents(sb, "주간", J.Arr(body, "weekly_contents"), "weekly", verdict);
                Bosses(sb, J.Arr(body, "boss_contents"), verdict);
                sb.AppendLine("  주간 보스 클리어: " + Int(J.Num(body, "weekly_boss_clear_count")) + "/" + Int(J.Num(body, "weekly_boss_clear_limit_count")));
            }
            return sb.ToString();
        }

        private static void Contents(StringBuilder sb, string label, IEnumerable<JsonElement> items, string group, Dictionary<string, SchedItem> verdict)
        {
            var list = items.ToList();
            sb.AppendLine("  " + label + " (" + list.Count + ")");
            foreach (var it in list)
            {
                var name = J.Str(it, "content_name");
                var quest = J.StrOrNull(it, "quest_state");
                verdict.TryGetValue(group + ":" + name, out var v);
                sb.AppendLine("    " + name + "  |  " + (J.Flag(it, "registration_flag") ? "등록" : "미등록") +
                    " · " + J.Str(it, "type") + " · " + Int(J.Num(it, "now_count")) + "/" + Int(J.Num(it, "max_count")) +
                    " · quest_state=" + (quest ?? "없음") + "  →  " + State(v));
            }
        }

        private static void Bosses(StringBuilder sb, IEnumerable<JsonElement> items, Dictionary<string, SchedItem> verdict)
        {
            var list = items.ToList();
            sb.AppendLine("  보스 (" + list.Count + ")");
            foreach (var it in list)
            {
                var name = J.Str(it, "content_name");
                verdict.TryGetValue(Scheduler.BossKey(name, J.Str(it, "difficulty")), out var v);
                sb.AppendLine("    " + name + " " + J.Str(it, "difficulty") + "  |  " + (J.Flag(it, "registration_flag") ? "등록" : "미등록") +
                    " · " + J.Str(it, "cycle") + " · complete_flag=" + J.Str(it, "complete_flag") + "  →  " + State(v));
            }
        }

        private static string State(SchedItem v)
        {
            if (v == null) return "?";
            var s = v.Done ? "완료" : v.Progress ? "진행 중" : "미완료";
            // 초기화가 지난 뒤 받은 적이 없는 데이터는 HUD가 미리 미완료로 바꿔 보여준다
            if (v.Stale) s += " (초기화 뒤 아직 새 데이터 없음)";
            return s;
        }

        private static string Int(double v) => ((long)v).ToString(CultureInfo.InvariantCulture);
    }
}
