using System;
using System.IO;
using System.Text.Json;
using System.Text.Json.Serialization;

namespace MapleHud.Core
{
    /// <summary>사용자 설정. %APPDATA%\MapleSchedulerHUD\settings.json 에 저장한다</summary>
    public sealed class HudSettings
    {
        // API
        public string ApiKey { get; set; } = "";
        public string Characters { get; set; } = "";     // 쉼표로 구분. 비우면 계정에서 자동 선택
        public int MinLevel { get; set; } = 200;
        public int MaxChars { get; set; } = 8;
        public int RefreshMin { get; set; } = 15;
        public string ApiBase { get; set; } = "";         // 프록시 주소 (보통 비워둔다)
        public bool Demo { get; set; }

        // 표시
        public bool ShowAll { get; set; }                 // 등록 안 된 항목도 표시
        public bool HideDone { get; set; }
        public bool CollapseDone { get; set; } = true;
        public bool ShowAvatar { get; set; } = true;
        public bool ShowSeconds { get; set; } = true;

        // 배치
        public int Columns { get; set; } = 1;
        public int CardWidth { get; set; } = 400;
        public int Scale { get; set; } = 100;             // %
        public string AlignX { get; set; } = "right";     // left, center, right
        public string AlignY { get; set; } = "top";       // top, center, bottom
        public int OffsetX { get; set; } = 48;
        public int OffsetY { get; set; } = 48;
        public string Monitor { get; set; } = "";         // 모니터 장치 이름. 비우면 주 모니터

        // 패널
        public int Opacity { get; set; } = 70;            // 패널 배경 불투명도 %
        public string Accent { get; set; } = "#ffb547";
        public bool AlwaysOnTop { get; set; }

        private static readonly JsonSerializerOptions Options = new JsonSerializerOptions
        {
            WriteIndented = true,
            PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
            DefaultIgnoreCondition = JsonIgnoreCondition.Never
        };

        public HudSettings Clone() => FromJson(ToJson());

        public string ToJson() => JsonSerializer.Serialize(this, Options);

        public static HudSettings FromJson(string json)
        {
            try
            {
                return Sanitize(JsonSerializer.Deserialize<HudSettings>(json, Options) ?? new HudSettings());
            }
            catch (JsonException)
            {
                return new HudSettings();
            }
        }

        public static HudSettings Load(string path)
        {
            try
            {
                return File.Exists(path) ? FromJson(File.ReadAllText(path)) : new HudSettings();
            }
            catch (IOException)
            {
                return new HudSettings();
            }
        }

        public void Save(string path)
        {
            Directory.CreateDirectory(Path.GetDirectoryName(path));
            var tmp = path + ".tmp";
            File.WriteAllText(tmp, ToJson());
            if (File.Exists(path)) File.Delete(path);
            File.Move(tmp, path);
        }

        /// <summary>모서리에서 떨어진 거리의 최댓값 (DIP). 아주 넓은 모니터에서 끌어 옮겨도 담기게 넉넉히 둔다</summary>
        public const int MaxOffset = 8000;

        /// <summary>범위를 벗어난 값을 바로잡는다 (직접 고친 설정 파일 대비)</summary>
        public static HudSettings Sanitize(HudSettings s)
        {
            s.ApiKey = (s.ApiKey ?? "").Trim();
            s.Characters = s.Characters ?? "";
            s.ApiBase = (s.ApiBase ?? "").Trim();
            s.MinLevel = Clamp(s.MinLevel, 0, 300);
            s.MaxChars = Clamp(s.MaxChars, 1, 20);
            s.RefreshMin = Clamp(s.RefreshMin, 5, 180);
            s.Columns = Clamp(s.Columns, 1, 4);
            s.CardWidth = Clamp(s.CardWidth, 280, 720);
            s.Scale = Clamp(s.Scale, 50, 250);
            s.OffsetX = Clamp(s.OffsetX, 0, MaxOffset);
            s.OffsetY = Clamp(s.OffsetY, 0, MaxOffset);
            s.Opacity = Clamp(s.Opacity, 0, 100);
            if (s.AlignX != "left" && s.AlignX != "center") s.AlignX = "right";
            if (s.AlignY != "center" && s.AlignY != "bottom") s.AlignY = "top";
            s.Accent = Colors.NormalizeHex(s.Accent, "#ffb547");
            s.Monitor = s.Monitor ?? "";
            return s;
        }

        private static int Clamp(int v, int min, int max) => Math.Max(min, Math.Min(max, v));

        /// <summary>바뀌면 캐릭터·스케줄러를 다시 받아와야 하는 설정인지</summary>
        public bool DataEquals(HudSettings o) =>
            ApiKey == o.ApiKey && Characters == o.Characters && MinLevel == o.MinLevel && MaxChars == o.MaxChars &&
            ApiBase == o.ApiBase && Demo == o.Demo && ShowAvatar == o.ShowAvatar;
    }

    public static class Colors
    {
        public static string NormalizeHex(string hex, string fallback)
        {
            var s = (hex ?? "").Trim().TrimStart('#');
            if (s.Length != 6) return fallback;
            foreach (var c in s)
            {
                if (!Uri.IsHexDigit(c)) return fallback;
            }
            return "#" + s.ToLowerInvariant();
        }
    }
}
