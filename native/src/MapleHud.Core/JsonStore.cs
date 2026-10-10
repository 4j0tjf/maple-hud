using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text;
using System.Text.Json;

namespace MapleHud.Core
{
    public sealed class CachedJson
    {
        public long At;
        public string Data;   // 원본 JSON 텍스트
    }

    /// <summary>
    /// 작은 키-값 저장소. 값은 JSON 텍스트로 보관하고 바뀐 내용은 Flush()에서 파일 하나에 한꺼번에 쓴다.
    /// 앱은 두 개를 쓴다: API 캐시(store.json)와 사용자 설정(prefs.json, 캐릭터별 표시 항목·접기 상태).
    /// 파일을 쓸 때 직전 파일을 .bak으로 남기고, 읽다가 깨져 있으면 .bak에서 되살린다.
    /// </summary>
    public sealed class JsonStore
    {
        private readonly string _path;
        private readonly Dictionary<string, string> _data = new Dictionary<string, string>();
        private bool _dirty;

        /// <summary>파일이 깨져 있어서 백업에서 되살렸는지 (또는 둘 다 못 읽어 비우고 시작했는지)</summary>
        public bool Recovered { get; private set; }

        public JsonStore(string path)
        {
            _path = path;
            if (path == null || !File.Exists(path)) return;
            if (Load(path)) return;
            // 깨진 파일은 지우지 않고 옆에 남겨 두고(원인 확인용), 직전 백업에서 되살린다
            Recovered = true;
            try { File.Copy(path, path + ".broken", true); }
            catch (IOException) { }
            if (File.Exists(path + ".bak") && Load(path + ".bak")) _dirty = true;
        }

        private bool Load(string file)
        {
            _data.Clear();
            try
            {
                using (var doc = JsonDocument.Parse(File.ReadAllText(file)))
                {
                    if (doc.RootElement.ValueKind != JsonValueKind.Object) return false;
                    foreach (var p in doc.RootElement.EnumerateObject()) _data[p.Name] = p.Value.GetRawText();
                }
                return true;
            }
            catch (Exception e) when (e is IOException || e is JsonException || e is UnauthorizedAccessException)
            {
                _data.Clear();
                return false;
            }
        }

        /// <summary>JSON 텍스트인지 (API가 점검 페이지 같은 HTML을 돌려줘도 저장소를 깨뜨리지 않게)</summary>
        public static bool IsJson(string text)
        {
            if (string.IsNullOrWhiteSpace(text)) return false;
            try
            {
                using (JsonDocument.Parse(text)) return true;
            }
            catch (JsonException)
            {
                return false;
            }
        }

        public static JsonStore InMemory() => new JsonStore(null);

        public IEnumerable<string> Keys => _data.Keys;

        public string GetRaw(string key) => _data.TryGetValue(key, out var v) ? v : null;

        public void SetRaw(string key, string json)
        {
            if (!IsJson(json)) throw new ArgumentException("not JSON: " + key, nameof(json));
            _data[key] = json;
            _dirty = true;
        }

        public void Remove(string key)
        {
            if (_data.Remove(key)) _dirty = true;
        }

        public T Get<T>(string key) where T : class
        {
            var raw = GetRaw(key);
            if (raw == null) return null;
            try { return JsonSerializer.Deserialize<T>(raw); }
            catch (JsonException) { return null; }
        }

        public void Set<T>(string key, T value) => SetRaw(key, JsonSerializer.Serialize(value));

        /// <summary>{ at, data } 형태로 저장된 캐시. maxAge(ms) 안이면 돌려준다 (null이면 나이 상관없이)</summary>
        public CachedJson GetCached(string key, long? maxAge, long now)
        {
            var raw = GetRaw(key);
            if (raw == null) return null;
            try
            {
                using (var doc = JsonDocument.Parse(raw))
                {
                    var root = doc.RootElement;
                    if (!root.TryGetProperty("at", out var at) || at.ValueKind != JsonValueKind.Number) return null;
                    var entry = new CachedJson
                    {
                        At = at.GetInt64(),
                        Data = root.TryGetProperty("data", out var data) ? data.GetRawText() : "null"
                    };
                    if (maxAge.HasValue && now - entry.At > maxAge.Value) return null;
                    return entry;
                }
            }
            catch (JsonException)
            {
                return null;
            }
        }

        public CachedJson SetCached(string key, string dataJson, long now)
        {
            if (!IsJson(dataJson)) dataJson = "null";
            SetRaw(key, "{\"at\":" + now + ",\"data\":" + dataJson + "}");
            return new CachedJson { At = now, Data = dataJson };
        }

        /// <summary>{ at } 시각이 maxAge보다 오래된 캐시 항목을 지운다</summary>
        public int Prune(long maxAge, long now)
        {
            var stale = _data.Keys.Where(k => { var e = GetCached(k, null, now); return e != null && e.At < now - maxAge; }).ToList();
            foreach (var k in stale) Remove(k);
            return stale.Count;
        }

        public void Flush()
        {
            if (!_dirty || _path == null) return;
            var sb = new StringBuilder("{");
            bool first = true;
            foreach (var kv in _data)
            {
                if (!first) sb.Append(',');
                first = false;
                sb.Append(JsonSerializer.Serialize(kv.Key)).Append(':').Append(kv.Value);
            }
            sb.Append('}');
            Directory.CreateDirectory(Path.GetDirectoryName(_path));
            var tmp = _path + ".tmp";
            File.WriteAllText(tmp, sb.ToString());
            if (File.Exists(_path))
            {
                try
                {
                    // 바꿔치기하면서 직전 파일을 .bak으로 남긴다
                    File.Replace(tmp, _path, _path + ".bak", true);
                }
                catch (IOException)
                {
                    // 백신·동기화 프로그램이 잡고 있으면 바꿔치기가 안 될 때가 있다
                    File.Copy(_path, _path + ".bak", true);
                    File.Delete(_path);
                    File.Move(tmp, _path);
                }
            }
            else
            {
                File.Move(tmp, _path);
            }
            _dirty = false;
        }
    }
}
