using System.Collections.Generic;
using System.Globalization;
using System.Text.Json;

namespace MapleHud.Core
{
    /// <summary>넥슨 API 응답은 같은 값이 숫자/문자열/불리언으로 섞여 올 수 있어서 너그럽게 읽는다</summary>
    internal static class J
    {
        public static bool TryProp(JsonElement e, string name, out JsonElement value)
        {
            if (e.ValueKind == JsonValueKind.Object && e.TryGetProperty(name, out value)) return true;
            value = default;
            return false;
        }

        public static string Str(JsonElement e, string name)
        {
            if (!TryProp(e, name, out var v)) return "";
            switch (v.ValueKind)
            {
                case JsonValueKind.String: return v.GetString() ?? "";
                case JsonValueKind.Number: return v.GetRawText();
                case JsonValueKind.True: return "true";
                case JsonValueKind.False: return "false";
                default: return "";
            }
        }

        /// <summary>null이면 null을 돌려준다 (quest_state처럼 "없음"과 "0"을 구분해야 할 때)</summary>
        public static string StrOrNull(JsonElement e, string name)
        {
            if (!TryProp(e, name, out var v) || v.ValueKind == JsonValueKind.Null || v.ValueKind == JsonValueKind.Undefined) return null;
            return Str(e, name);
        }

        public static double Num(JsonElement e, string name)
        {
            if (!TryProp(e, name, out var v)) return 0;
            if (v.ValueKind == JsonValueKind.Number && v.TryGetDouble(out var d)) return d;
            if (v.ValueKind == JsonValueKind.String &&
                double.TryParse(v.GetString(), NumberStyles.Float, CultureInfo.InvariantCulture, out d)) return d;
            return 0;
        }

        public static bool Flag(JsonElement e, string name)
        {
            if (!TryProp(e, name, out var v)) return false;
            switch (v.ValueKind)
            {
                case JsonValueKind.True: return true;
                case JsonValueKind.Number: return v.TryGetDouble(out var d) && d == 1;
                case JsonValueKind.String:
                    var s = v.GetString();
                    return s == "true" || s == "1";
                default: return false;
            }
        }

        public static IEnumerable<JsonElement> Arr(JsonElement e, string name)
        {
            if (TryProp(e, name, out var v) && v.ValueKind == JsonValueKind.Array)
            {
                foreach (var item in v.EnumerateArray()) yield return item;
            }
        }

        public static JsonElement Parse(string json)
        {
            if (string.IsNullOrEmpty(json)) return default;
            using (var doc = JsonDocument.Parse(json)) return doc.RootElement.Clone();
        }
    }
}
