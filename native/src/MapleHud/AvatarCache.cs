using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Net.Http;
using System.Security.Cryptography;
using System.Text;
using System.Threading.Tasks;
using SkiaSharp;

namespace MapleHud
{
    /// <summary>
    /// 캐릭터 이미지를 받아서 메모리와 디스크에 보관한다 (다시 켜도 바로 보이도록).
    /// Get은 HUD를 그리는 도중에 불리므로 Loaded는 항상 그리기가 끝난 뒤(UI 스레드의 다음 차례)에 알린다.
    /// </summary>
    internal sealed class AvatarCache : IDisposable
    {
        private readonly HttpClient _http;
        private readonly string _dir;
        private readonly Dictionary<string, SKImage> _images = new Dictionary<string, SKImage>();
        private readonly HashSet<string> _loading = new HashSet<string>();

        public event Action Loaded;

        public AvatarCache(HttpClient http, string dir)
        {
            _http = http;
            _dir = dir;
        }

        public int LoadedCount => _images.Values.Count(i => i != null);

        public SKImage Get(string url)
        {
            if (string.IsNullOrEmpty(url)) return null;
            if (_images.TryGetValue(url, out var img)) return img;
            if (_loading.Add(url)) Load(url);
            return null;
        }

        private async void Load(string url)
        {
            var file = Path.Combine(_dir, Hash(url) + ".png");
            SKImage img = null;
            try
            {
                // 디스크에 있어도 바로 돌려주지 않는다: 그리는 중에 Loaded가 불리면 그리기가 겹쳐 앱이 죽는다
                var bytes = await Task.Run(() => File.Exists(file) ? File.ReadAllBytes(file) : null).ConfigureAwait(true);
                if (bytes == null)
                {
                    bytes = await _http.GetByteArrayAsync(url).ConfigureAwait(true);
                    Directory.CreateDirectory(_dir);
                    File.WriteAllBytes(file, bytes);
                }
                img = SKImage.FromEncodedData(bytes);
            }
            catch (Exception)
            {
                // 이미지를 못 받으면 이름 첫 글자로 대신한다 (다음 실행 때 다시 시도)
                img = null;
            }
            if (_disposed)
            {
                img?.Dispose();
                return;
            }
            _images[url] = img;
            if (img != null) Loaded?.Invoke();
        }

        internal static string Hash(string s)
        {
            using (var sha = SHA1.Create())
            {
                var b = sha.ComputeHash(Encoding.UTF8.GetBytes(s));
                return BitConverter.ToString(b).Replace("-", "").ToLowerInvariant();
            }
        }

        private bool _disposed;

        public void Dispose()
        {
            _disposed = true;
            foreach (var img in _images.Values) img?.Dispose();
            _images.Clear();
        }
    }
}
