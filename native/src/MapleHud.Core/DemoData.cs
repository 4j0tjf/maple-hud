using System.Collections.Generic;
using System.Linq;
using System.Text.Json;

namespace MapleHud.Core
{
    /// <summary>API 키가 없을 때 보여주는 예시 데이터 (scheduler/character-state 응답 형식)</summary>
    public static class DemoData
    {
        private static object C(string name, string type, int now, int max, int? quest = null, bool reg = true) => new Dictionary<string, object>
        {
            ["content_name"] = name,
            ["type"] = type,
            ["registration_flag"] = reg ? "true" : "false",
            ["now_count"] = now,
            ["max_count"] = max,
            ["quest_state"] = quest?.ToString()
        };

        private static object B(string name, string difficulty, string cycle, int order, bool done) => new Dictionary<string, object>
        {
            ["content_name"] = name,
            ["difficulty"] = difficulty,
            ["cycle"] = cycle,
            ["list_order_no"] = order,
            ["registration_flag"] = "true",
            ["complete_flag"] = done ? "true" : "false"
        };

        private sealed class Demo
        {
            public string Name, World, Cls, Exp;
            public int Level, BossClear, BossLimit;
            public object[] Daily, Weekly, Boss;
        }

        private static readonly Demo[] Characters =
        {
            new Demo
            {
                Name = "단풍잎소녀", World = "스카니아", Level = 287, Cls = "아크메이지(썬,콜)", Exp = "61.204",
                Daily = new[]
                {
                    C("몬스터파크", "contents", 2, 2), C("우르스", "contents", 1, 3),
                    C("세르니움 일일 퀘스트", "quest", 0, 0, 2), C("호텔 아르크스 일일 퀘스트", "quest", 0, 0, 2),
                    C("오디움 일일 퀘스트", "quest", 0, 0, 1), C("도원경 일일 퀘스트", "quest", 0, 0, 0),
                    C("아르테리아 일일 퀘스트", "quest", 0, 0, 0), C("카르시온 일일 퀘스트", "quest", 0, 0, 0)
                },
                Weekly = new[]
                {
                    C("에픽 던전 : 하이마운틴", "contents", 1, 1), C("무릉도장", "contents", 0, 1), C("길드 플래그 레이스", "contents", 0, 1)
                },
                Boss = new[]
                {
                    B("스우", "하드", "주간", 1, true), B("데미안", "하드", "주간", 2, true),
                    B("가디언 엔젤 슬라임", "카오스", "주간", 3, true), B("루시드", "하드", "주간", 4, true),
                    B("윌", "하드", "주간", 5, false), B("더스크", "카오스", "주간", 6, false),
                    B("진 힐라", "하드", "주간", 7, false), B("듄켈", "하드", "주간", 8, false),
                    B("선택받은 세렌", "하드", "주간", 9, false), B("감시자 칼로스", "노멀", "주간", 10, false),
                    B("검은 마법사", "하드", "월간", 11, false)
                },
                BossClear = 4, BossLimit = 12
            },
            new Demo
            {
                Name = "버섯왕자", World = "스카니아", Level = 275, Cls = "아델", Exp = "12.880",
                Daily = new[]
                {
                    C("몬스터파크", "contents", 2, 2), C("우르스", "contents", 3, 3),
                    C("세르니움 일일 퀘스트", "quest", 0, 0, 2), C("호텔 아르크스 일일 퀘스트", "quest", 0, 0, 2),
                    C("오디움 일일 퀘스트", "quest", 0, 0, 2)
                },
                Weekly = new[] { C("에픽 던전 : 하이마운틴", "contents", 0, 1), C("무릉도장", "contents", 1, 1) },
                Boss = new[]
                {
                    B("스우", "노멀", "주간", 1, true), B("데미안", "노멀", "주간", 2, true),
                    B("루시드", "노멀", "주간", 3, true), B("윌", "노멀", "주간", 4, true),
                    B("더스크", "노멀", "주간", 5, false), B("진 힐라", "노멀", "주간", 6, false),
                    B("듄켈", "노멀", "주간", 7, false)
                },
                BossClear = 4, BossLimit = 12
            },
            new Demo
            {
                Name = "슬라임대장", World = "스카니아", Level = 262, Cls = "비숍", Exp = "88.017",
                Daily = new[]
                {
                    C("몬스터파크", "contents", 2, 2), C("세르니움 일일 퀘스트", "quest", 0, 0, 2),
                    C("호텔 아르크스 일일 퀘스트", "quest", 0, 0, 2)
                },
                Weekly = new[] { C("무릉도장", "contents", 1, 1) },
                Boss = new[]
                {
                    B("스우", "노멀", "주간", 1, true), B("데미안", "노멀", "주간", 2, true),
                    B("가디언 엔젤 슬라임", "노멀", "주간", 3, true), B("루시드", "이지", "주간", 4, true)
                },
                BossClear = 4, BossLimit = 12
            }
        };

        public static List<CharState> Build(long fetchedAt)
        {
            return Characters.Select((d, i) => new CharState
            {
                Name = d.Name,
                Ocid = "demo-" + i,
                World = d.World,
                Cls = d.Cls,
                Level = d.Level,
                FetchedAt = fetchedAt,
                Basic = JsonSerializer.Serialize(new Dictionary<string, object>
                {
                    ["character_name"] = d.Name,
                    ["character_level"] = d.Level,
                    ["character_class"] = d.Cls,
                    ["world_name"] = d.World,
                    ["character_exp_rate"] = d.Exp,
                    ["character_image"] = ""
                }),
                Body = JsonSerializer.Serialize(new Dictionary<string, object>
                {
                    ["date"] = null,
                    ["character_name"] = d.Name,
                    ["world_name"] = d.World,
                    ["character_level"] = d.Level,
                    ["character_class"] = d.Cls,
                    ["daily_contents"] = d.Daily,
                    ["weekly_contents"] = d.Weekly,
                    ["boss_contents"] = d.Boss,
                    ["weekly_boss_clear_count"] = d.BossClear,
                    ["weekly_boss_clear_limit_count"] = d.BossLimit
                })
            }).ToList();
        }
    }
}
