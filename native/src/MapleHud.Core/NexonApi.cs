using System;
using System.Collections.Generic;
using System.Linq;
using System.Net;
using System.Net.Http;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;

namespace MapleHud.Core
{
    public sealed class ApiException : Exception
    {
        public string Code { get; }
        public int Status { get; }

        private static readonly Dictionary<string, string> Messages = new Dictionary<string, string>
        {
            ["OPENAPI00001"] = "넥슨 API 서버 내부 오류",
            ["OPENAPI00002"] = "조회 권한이 없습니다 (API 키를 만든 계정의 캐릭터인지 확인)",
            ["OPENAPI00003"] = "유효하지 않은 캐릭터 식별자입니다",
            ["OPENAPI00004"] = "요청 값이 올바르지 않습니다 (캐릭터명 확인)",
            ["OPENAPI00005"] = "API 키가 올바르지 않습니다",
            ["OPENAPI00006"] = "잘못된 API 경로입니다",
            ["OPENAPI00007"] = "API 호출 한도를 초과했습니다",
            ["OPENAPI00009"] = "데이터 준비 중입니다",
            ["OPENAPI00010"] = "게임 점검 중입니다",
            ["OPENAPI00011"] = "API 점검 중입니다",
            ["NETWORK"] = "네트워크 오류 (인터넷 연결을 확인하세요)",
            ["TIMEOUT"] = "응답 시간 초과"
        };

        public ApiException(string code, string detail = null, int status = 0)
            : base(Messages.TryGetValue(code, out var msg) ? msg : (string.IsNullOrEmpty(detail) ? code : detail))
        {
            Code = code;
            Status = status;
        }

        // 이 오류들은 모든 캐릭터에 똑같이 실패하므로 동기화를 멈추고 전체 알림으로 띄운다
        public bool IsFatal =>
            Code == "OPENAPI00005" || Code == "OPENAPI00006" || Code == "OPENAPI00007" ||
            Code == "OPENAPI00010" || Code == "OPENAPI00011" || Code == "NETWORK";
    }

    /// <summary>
    /// 넥슨 Open API (메이플스토리) 클라이언트.
    /// 요청은 한 번에 하나씩, 간격을 둬서 보낸다 (개발 키 초당 호출 제한).
    /// </summary>
    public sealed class NexonApi
    {
        public const string DefaultBase = "https://open.api.nexon.com";
        private const int MinGapMs = 260;
        private static readonly TimeSpan Timeout = TimeSpan.FromSeconds(12);

        private static readonly SemaphoreSlim Gate = new SemaphoreSlim(1, 1);
        private static long _lastAt;

        private readonly HttpClient _http;
        private readonly string _apiKey;
        private readonly string _base;

        public NexonApi(HttpClient http, string apiKey, string baseUrl)
        {
            _http = http;
            _apiKey = apiKey ?? "";
            _base = string.IsNullOrWhiteSpace(baseUrl) ? DefaultBase : baseUrl.Trim().TrimEnd('/');
        }

        public Task<string> CharacterListAsync() => RequestAsync("character/list", null);

        public async Task<string> OcidAsync(string name)
        {
            var json = await RequestAsync("id", new Dictionary<string, string> { ["character_name"] = name }).ConfigureAwait(true);
            return J.Str(J.Parse(json), "ocid");
        }

        public Task<string> BasicAsync(string ocid) =>
            RequestAsync("character/basic", new Dictionary<string, string> { ["ocid"] = ocid });

        public Task<string> SchedulerAsync(string ocid) =>
            RequestAsync("scheduler/character-state", new Dictionary<string, string> { ["ocid"] = ocid });

        public string BuildUrl(string path, IDictionary<string, string> query)
        {
            var url = _base + "/maplestory/v1/" + path;
            if (query == null || query.Count == 0) return url;
            return url + "?" + string.Join("&", query.Where(q => !string.IsNullOrEmpty(q.Value))
                .Select(q => Uri.EscapeDataString(q.Key) + "=" + Uri.EscapeDataString(q.Value)));
        }

        private async Task<string> RequestAsync(string path, IDictionary<string, string> query)
        {
            var url = BuildUrl(path, query);
            for (int attempt = 0; ; attempt++)
            {
                try
                {
                    return await SendOnceAsync(url).ConfigureAwait(true);
                }
                catch (ApiException e) when ((e.Code == "OPENAPI00007" || e.Status == 429) && attempt < 2)
                {
                    // 호출 한도 초과는 잠시 기다렸다가 다시 시도
                    await Task.Delay(1500 * (attempt + 1)).ConfigureAwait(true);
                }
            }
        }

        private async Task<string> SendOnceAsync(string url)
        {
            await Gate.WaitAsync().ConfigureAwait(true);
            try
            {
                var wait = Interlocked.Read(ref _lastAt) + MinGapMs - KstTime.NowMs();
                if (wait > 0) await Task.Delay((int)wait).ConfigureAwait(true);
                Interlocked.Exchange(ref _lastAt, KstTime.NowMs());

                using (var cts = new CancellationTokenSource(Timeout))
                using (var req = new HttpRequestMessage(HttpMethod.Get, url))
                {
                    req.Headers.TryAddWithoutValidation("accept", "application/json");
                    if (_apiKey.Length > 0) req.Headers.TryAddWithoutValidation("x-nxopen-api-key", _apiKey);
                    HttpResponseMessage res;
                    try
                    {
                        res = await _http.SendAsync(req, cts.Token).ConfigureAwait(true);
                    }
                    catch (TaskCanceledException)
                    {
                        throw new ApiException("TIMEOUT");
                    }
                    catch (HttpRequestException e)
                    {
                        throw new ApiException("NETWORK", e.Message);
                    }
                    using (res)
                    {
                        var text = await res.Content.ReadAsStringAsync().ConfigureAwait(true);
                        if (res.IsSuccessStatusCode) return text;
                        string code = "HTTP" + (int)res.StatusCode, message = null;
                        try
                        {
                            var err = J.Parse(text);
                            if (J.TryProp(err, "error", out var e))
                            {
                                code = J.Str(e, "name") is var n && n.Length > 0 ? n : code;
                                message = J.Str(e, "message");
                            }
                        }
                        catch (JsonException)
                        {
                            // 본문이 JSON이 아니면 HTTP 상태만 쓴다
                        }
                        throw new ApiException(code, message, (int)res.StatusCode);
                    }
                }
            }
            finally
            {
                Gate.Release();
            }
        }

        /// <summary>앱 전체에서 하나만 쓴다 (소켓 재사용)</summary>
        public static HttpClient CreateHttpClient()
        {
            ServicePointManager.SecurityProtocol |= SecurityProtocolType.Tls12;
            var handler = new HttpClientHandler { AutomaticDecompression = DecompressionMethods.GZip | DecompressionMethods.Deflate };
            return new HttpClient(handler) { Timeout = System.Threading.Timeout.InfiniteTimeSpan };
        }
    }
}
