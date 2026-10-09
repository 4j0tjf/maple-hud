using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Net.Http;
using System.Security.Cryptography;
using System.Text;
using SkiaSharp;

namespace MapleHud
{
    /// <summary>캐릭터 이미지를 받아서 메모리와 디스크에 보관한다 (다시 켜도 바로 보이도록)</summary>
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
            byte[] bytes = null;
            try
            {
                if (File.Exists(file)) bytes = File.ReadAllBytes(file);
                else
                {
                    bytes = await _http.GetByteArrayAsync(url).ConfigureAwait(true);
                    Directory.CreateDirectory(_dir);
                    File.WriteAllBytes(file, bytes);
                }
            }
            catch (Exception)
            {
                // 이미지를 못 받으면 이름 첫 글자로 대신한다 (다음 실행 때 다시 시도)
                bytes = null;
            }
            var img = bytes != null ? SKImage.FromEncodedData(bytes) : null;
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

        public void Dispose()
        {
            foreach (var img in _images.Values) img?.Dispose();
            _images.Clear();
        }
    }
}
