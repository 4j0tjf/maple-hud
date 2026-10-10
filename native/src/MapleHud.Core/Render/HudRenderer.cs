using System;
using System.Collections.Generic;
using System.Linq;
using SkiaSharp;

namespace MapleHud.Core.Render
{
    /// <summary>클릭할 수 있는 영역 (패널 기준 CSS px 좌표). Clip이 있으면 그 안에서만 유효</summary>
    public sealed class HitRegion
    {
        public SKRect Rect;
        public SKRect? Clip;
        public string Action;   // toggle, edit, item, group, all, none, reset, refresh, settings
        public string Key;      // 캐릭터 key
        public string Id;       // 항목 key 또는 분류 id
        public string CardKey;  // 이 영역이 속한 카드 (마우스를 올리면 편집 버튼을 보여주기 위해)

        public bool Contains(float x, float y) => Rect.Contains(x, y) && (!Clip.HasValue || Clip.Value.Contains(x, y));
        public bool Same(HitRegion o) => o != null && o.Action == Action && o.Key == Key && o.Id == Id;
    }

    public sealed class RenderInput
    {
        public HudView View;
        public HudSettings Settings;
        public long Now;
        public float MaxHeight = 4000;        // 패널 최대 높이 (CSS px)
        public float Scroll;                  // 카드 영역 스크롤 (CSS px)
        public HitRegion Hover;               // 마우스가 올라간 영역
        public string HoverCard;              // 마우스가 올라간 카드
        public Func<string, SKImage> Avatar;  // 캐릭터 이미지 (아직 없으면 null)
        // 카드 영역 내용이 바뀔 때마다 올리는 번호. 같으면 지난번에 그린 카드 영역을 그대로 쓴다
        // (매초 바뀌는 시계·카운트다운만 다시 그려서 CPU를 아낀다). -1이면 항상 새로 그린다
        public int BodyVersion = -1;
    }

    public sealed class HudLayout
    {
        public float Width;
        public float Height;
        public float CardsTop;
        public float ViewportHeight;
        public float ContentHeight;
        public float MaxScroll;
        public List<HitRegion> Regions = new List<HitRegion>();

        public HitRegion HitTest(float x, float y)
        {
            for (int i = Regions.Count - 1; i >= 0; i--)
            {
                if (Regions[i].Contains(x, y)) return Regions[i];
            }
            return null;
        }

        public string CardAt(float x, float y)
        {
            foreach (var r in Regions)
            {
                if (r.Action == "card" && r.Contains(x, y)) return r.Key;
            }
            return null;
        }
    }

    /// <summary>
    /// HUD를 SkiaSharp로 그린다. 크기·색·여백은 웹 버전(hud.css)과 같게 맞췄다.
    /// 같은 코드로 크기만 재기(캔버스 없음)와 그리기를 모두 한다.
    /// </summary>
    public sealed class HudRenderer : IDisposable
    {
        private const float Pad = 14;
        private const float Gap = 10;

        private readonly FontSet _fonts;
        private readonly Dictionary<long, SKFont> _fontCache = new Dictionary<long, SKFont>();
        private readonly SKPaint _fill = new SKPaint { IsAntialias = true, Style = SKPaintStyle.Fill };
        private readonly SKPaint _stroke = new SKPaint { IsAntialias = true, Style = SKPaintStyle.Stroke, StrokeCap = SKStrokeCap.Round, StrokeJoin = SKStrokeJoin.Round };
        private readonly SKPaint _text = new SKPaint { IsAntialias = true };
        private readonly SKPaint _shadow = new SKPaint
        {
            IsAntialias = true,
            Color = new SKColor(0, 0, 0, 71),
            MaskFilter = SKMaskFilter.CreateBlur(SKBlurStyle.Normal, 1f)
        };

        // 그리는 동안의 상태
        private SKCanvas _c;
        private Theme _th;
        private RenderInput _in;
        private HudLayout _layout;
        private SKRect? _clip;
        private float _dy;   // 스크롤 이동량 (영역 좌표 보정용)
        private string _cardKey;

        // 카드 영역 캐시
        private SKImage _bodyImage;
        private List<HitRegion> _bodyRegions;
        private string _bodyKey;
        private List<float> _heights;
        private string _heightsKey;

        public HudRenderer(FontSet fonts)
        {
            _fonts = fonts;
        }

        public static float PanelWidth(HudSettings s) => s.CardWidth * s.Columns + Gap * (s.Columns - 1) + Pad * 2;

        public HudLayout Measure(RenderInput input) => Run(null, input);

        public HudLayout Draw(SKCanvas canvas, RenderInput input) => Run(canvas, input);

        /* ---------- 공통 도구 ---------- */

        private SKFont Font(int weight, float size)
        {
            long key = weight * 100000L + (long)(size * 100);
            if (!_fontCache.TryGetValue(key, out var f))
            {
                f = new SKFont(_fonts.ForWeight(weight), size)
                {
                    Edging = SKFontEdging.Antialias,
                    Subpixel = true,
                    Hinting = SKFontHinting.Slight
                };
                _fontCache[key] = f;
            }
            return f;
        }

        private float Width(string s, int weight, float size, float spacing = 0)
        {
            if (string.IsNullOrEmpty(s)) return 0;
            var w = Font(weight, size).MeasureText(s);
            return w + spacing * s.Length;
        }

        /// <summary>top부터 lineHeight 칸 안에 세로 가운데로 쓴다. 쓴 너비를 돌려준다</summary>
        private float Text(string s, float x, float top, float lineHeight, int weight, float size, SKColor color,
            float spacing = 0, bool shadow = true)
        {
            if (string.IsNullOrEmpty(s)) return 0;
            var font = Font(weight, size);
            var m = font.Metrics;
            float baseline = top + (lineHeight - (m.Descent - m.Ascent)) / 2 - m.Ascent;
            if (_c != null)
            {
                _text.Color = color;
                if (spacing == 0)
                {
                    if (shadow) _c.DrawText(s, x, baseline + 1, SKTextAlign.Left, font, _shadow);
                    _c.DrawText(s, x, baseline, SKTextAlign.Left, font, _text);
                }
                else
                {
                    float cx = x;
                    foreach (var ch in s)
                    {
                        var t = ch.ToString();
                        if (shadow) _c.DrawText(t, cx, baseline + 1, SKTextAlign.Left, font, _shadow);
                        _c.DrawText(t, cx, baseline, SKTextAlign.Left, font, _text);
                        cx += font.MeasureText(t) + spacing;
                    }
                }
            }
            return Width(s, weight, size, spacing);
        }

        private string Ellipsize(string s, float maxWidth, int weight, float size)
        {
            if (string.IsNullOrEmpty(s) || Width(s, weight, size) <= maxWidth) return s ?? "";
            int lo = 0, hi = s.Length;
            while (lo < hi)
            {
                int mid = (lo + hi + 1) / 2;
                if (Width(s.Substring(0, mid) + "…", weight, size) <= maxWidth) lo = mid;
                else hi = mid - 1;
            }
            return s.Substring(0, lo) + "…";
        }

        /// <summary>너비에 맞춰 줄바꿈 (공백에서 우선 자르고, 안 되면 글자 단위)</summary>
        private List<string> Wrap(string s, float maxWidth, int weight, float size)
        {
            var lines = new List<string>();
            var rest = s ?? "";
            while (rest.Length > 0)
            {
                if (Width(rest, weight, size) <= maxWidth)
                {
                    lines.Add(rest);
                    break;
                }
                int fit = 1;
                while (fit < rest.Length && Width(rest.Substring(0, fit + 1), weight, size) <= maxWidth) fit++;
                int space = rest.LastIndexOf(' ', Math.Min(fit, rest.Length - 1));
                int cut = space > 0 ? space : fit;
                lines.Add(rest.Substring(0, cut).TrimEnd());
                rest = rest.Substring(cut).TrimStart();
            }
            return lines;
        }

        private void RRect(SKRect r, float radius, SKColor? fill, SKColor? stroke = null, float strokeWidth = 1, SKShader shader = null)
        {
            if (_c == null) return;
            if (fill.HasValue || shader != null)
            {
                _fill.Color = fill ?? SKColors.White;
                _fill.Shader = shader;
                _c.DrawRoundRect(r, radius, radius, _fill);
                _fill.Shader = null;
            }
            if (stroke.HasValue)
            {
                _stroke.Color = stroke.Value;
                _stroke.StrokeWidth = strokeWidth;
                var inset = r;
                inset.Inflate(-strokeWidth / 2, -strokeWidth / 2);
                _c.DrawRoundRect(inset, radius, radius, _stroke);
            }
        }

        /// <summary>size x size 칸에 아이콘(viewBox 크기 box)을 그린다</summary>
        private void Icon(SKPath path, float x, float y, float size, float box, SKColor color, bool fill = false, float strokeWidth = 2)
        {
            if (_c == null) return;
            _c.Save();
            _c.Translate(x, y);
            _c.Scale(size / box);
            var p = fill ? _fill : _stroke;
            p.Color = color;
            p.Shader = null;
            if (!fill) p.StrokeWidth = strokeWidth;
            _c.DrawPath(path, p);
            _c.Restore();
        }

        private void Hit(SKRect r, string action, string key = null, string id = null)
        {
            if (_c == null) return;
            var shifted = r;
            shifted.Offset(0, -_dy);
            _layout.Regions.Add(new HitRegion { Rect = shifted, Clip = _clip, Action = action, Key = key, Id = id, CardKey = _cardKey });
        }

        private bool IsHover(string action, string key = null, string id = null) =>
            _in.Hover != null && _in.Hover.Action == action && _in.Hover.Key == key && _in.Hover.Id == id;

        /* ---------- 전체 ---------- */

        private HudLayout Run(SKCanvas canvas, RenderInput input)
        {
            _in = input;
            _th = new Theme(input.Settings.Accent, input.Settings.Opacity);
            _layout = new HudLayout();
            _clip = null;
            _dy = 0;
            _cardKey = null;

            var s = input.Settings;
            float w = PanelWidth(s);
            float inner = w - Pad * 2;

            // 1) 크기 재기
            _c = null;
            float headerH = Header(Pad, Pad, inner);
            float noticeH = Notices(Pad, Pad + headerH, inner);
            float top = Pad + headerH + noticeH;
            var heightsKey = input.BodyVersion < 0 ? null : input.BodyVersion + "|" + s.CardWidth + "|" + s.Columns;
            if (heightsKey == null || heightsKey != _heightsKey)
            {
                _heights = input.View.Cards.Select(card => Card(card, 0, 0, s.CardWidth)).ToList();
                _heightsKey = heightsKey;
            }
            var heights = _heights;
            float content = CardsHeight(heights, s.Columns);
            float viewport = Math.Max(60, Math.Min(content, input.MaxHeight - top - Pad));
            if (content == 0) viewport = 0;
            float emptyH = input.View.Cards.Count == 0 ? Empty(Pad, top, inner) : 0;
            float h = top + viewport + emptyH + Pad;

            _layout.Width = w;
            _layout.Height = h;
            _layout.CardsTop = top;
            _layout.ViewportHeight = viewport;
            _layout.ContentHeight = content;
            _layout.MaxScroll = Math.Max(0, content - viewport);
            float scroll = Math.Max(0, Math.Min(input.Scroll, _layout.MaxScroll));

            if (canvas == null) return _layout;

            // 2) 그리기
            _c = canvas;
            _layout.Regions.Clear();
            var panel = new SKRect(0, 0, w, h);
            RRect(panel, 18, new SKColor(13, 16, 26, _th.PanelAlpha), Theme.White(0.10));
            Header(Pad, Pad, inner);
            Notices(Pad, Pad + headerH, inner);

            if (input.View.Cards.Count == 0)
            {
                Empty(Pad, top, inner);
                return _layout;
            }

            var clip = new SKRect(Pad - 1, top, w - Pad + 1, top + viewport);
            float scale = canvas.TotalMatrix.ScaleX;
            var dev = SKRectI.Round(new SKRect(clip.Left * scale, clip.Top * scale, clip.Right * scale, clip.Bottom * scale));
            dev.Offset((int)Math.Round(canvas.TotalMatrix.TransX), (int)Math.Round(canvas.TotalMatrix.TransY));
            string bodyKey = input.BodyVersion < 0 ? null
                : string.Join("|", input.BodyVersion, dev.Left, dev.Top, dev.Width, dev.Height, scroll, _th.Accent, input.Settings.Opacity);
            if (bodyKey == null || bodyKey != _bodyKey || _bodyImage == null)
            {
                RenderBody(heights, clip, dev, scale, scroll, s);
                _bodyKey = bodyKey;
            }
            _layout.Regions.AddRange(_bodyRegions);
            // 카드 영역은 장치 픽셀에 맞춰 그대로 붙인다 (흐려지지 않게)
            canvas.Save();
            canvas.ResetMatrix();
            canvas.DrawImage(_bodyImage, dev.Left, dev.Top, new SKSamplingOptions(SKFilterMode.Nearest, SKMipmapMode.None), null);
            canvas.Restore();
            return _layout;
        }

        private void RenderBody(List<float> heights, SKRect clip, SKRectI dev, float scale, float scroll, HudSettings s)
        {
            var outer = _c;
            var outerRegions = _layout.Regions;
            _layout.Regions = new List<HitRegion>();
            _bodyImage?.Dispose();
            using (var surface = SKSurface.Create(new SKImageInfo(Math.Max(1, dev.Width), Math.Max(1, dev.Height), SKColorType.Bgra8888, SKAlphaType.Premul)))
            {
                _c = surface.Canvas;
                _c.Clear(SKColors.Transparent);
                _c.Scale(scale);
                _c.Translate(-dev.Left / scale + (outer.TotalMatrix.TransX / scale), -dev.Top / scale + (outer.TotalMatrix.TransY / scale));
                bool more = _layout.MaxScroll > 0.5f;
                if (more) _c.SaveLayer(clip, null);
                _c.Save();
                _c.Translate(0, -scroll);
                _clip = clip;
                _dy = scroll;
                DrawCards(heights, Pad, clip.Top, s);
                _clip = null;
                _dy = 0;
                _c.Restore();
                if (more)
                {
                    // 아래에 내용이 더 있으면 끝을 흐리게
                    if (scroll < _layout.MaxScroll - 0.5f)
                    {
                        using (var mask = new SKPaint
                        {
                            BlendMode = SKBlendMode.DstIn,
                            Shader = SKShader.CreateLinearGradient(new SKPoint(0, clip.Bottom - 36), new SKPoint(0, clip.Bottom),
                                new[] { SKColors.Black, SKColors.Transparent }, null, SKShaderTileMode.Clamp)
                        })
                        {
                            _c.DrawRect(new SKRect(clip.Left, clip.Bottom - 36, clip.Right, clip.Bottom), mask);
                        }
                    }
                    _c.Restore();
                }
                _bodyImage = surface.Snapshot();
            }
            _bodyRegions = _layout.Regions;
            _layout.Regions = outerRegions;
            _c = outer;
        }

        private static float CardsHeight(List<float> heights, int cols)
        {
            float total = 0;
            for (int i = 0; i < heights.Count; i += cols)
            {
                total += heights.Skip(i).Take(cols).Max();
                if (i + cols < heights.Count) total += Gap;
            }
            return total;
        }

        private void DrawCards(List<float> heights, float x0, float y0, HudSettings s)
        {
            var cards = _in.View.Cards;
            float y = y0;
            for (int i = 0; i < cards.Count; i += s.Columns)
            {
                float rowH = heights.Skip(i).Take(s.Columns).Max();
                for (int j = 0; j < s.Columns && i + j < cards.Count; j++)
                {
                    float x = x0 + j * (s.CardWidth + Gap);
                    Card(cards[i + j], x, y, s.CardWidth);
                }
                y += rowH + Gap;
            }
        }

        /* ---------- 머리 ---------- */

        private float Header(float x, float y, float w)
        {
            var s = _in.Settings;
            var view = _in.View;
            float top = y + 2;

            // 제목 줄
            if (_c != null)
            {
                using (var glow = new SKPaint { IsAntialias = true, Color = Theme.A(_th.Accent, 0.55), MaskFilter = SKMaskFilter.CreateBlur(SKBlurStyle.Normal, 3) })
                {
                    _c.Save();
                    _c.Translate(x + 4, top + 5);
                    _c.Scale(18f / 24);
                    _c.DrawPath(Icons.Leaf, glow);
                    _c.Restore();
                }
                Icon(Icons.Leaf, x + 4, top + 5, 18, 24, _th.Accent, fill: true);
            }
            Text("MAPLE SCHEDULER", x + 30, top, 28, 800, 12, _th.Text, spacing: 12 * 0.14f);

            var clock = KstTime.FormatClock(_in.Now);
            string sec = s.ShowSeconds ? clock.Seconds : "";
            float secW = Width(sec, 400, 12);
            float timeW = Width(clock.Time, 700, 20);
            float right = x + w - 4;
            float sx = right - secW;
            float tx = sx - (sec.Length > 0 ? 2 : 0) - timeW;
            float dx = tx - 6 - Width(clock.Date, 400, 12);
            Text(clock.Date, dx, top + 3, 28, 400, 12, _th.Text2);
            Text(clock.Time, tx, top, 28, 700, 20, _th.Text);
            Text(sec, sx, top + 3, 28, 400, 12, _th.Text3);
            float yy = top + 28 + 10;

            // 초기화 카운트다운
            float boxW = (w - 12) / 3;
            var resets = new[]
            {
                new[] { "일일 초기화", "", KstTime.FormatCountdown(KstTime.NextReset(ResetKind.Daily, _in.Now) - _in.Now, s.ShowSeconds) },
                new[] { "주간 보스", "목", KstTime.FormatCountdown(KstTime.NextReset(ResetKind.WeeklyThu, _in.Now) - _in.Now, s.ShowSeconds) },
                new[] { "주간 콘텐츠", "월", KstTime.FormatCountdown(KstTime.NextReset(ResetKind.WeeklyMon, _in.Now) - _in.Now, s.ShowSeconds) }
            };
            for (int i = 0; i < 3; i++)
            {
                var r = new SKRect(x + i * (boxW + 6), yy, x + i * (boxW + 6) + boxW, yy + 48);
                RRect(r, 10, Theme.White(0.05), _th.Line);
                float kw = Text(resets[i][0], r.Left + 10, r.Top + 6, 15, 600, 10.5f, _th.Text3);
                Text(resets[i][1], r.Left + 10 + kw + 3, r.Top + 6, 15, 600, 10, Theme.A(_th.Accent, 0.85));
                Text(resets[i][2], r.Left + 10, r.Top + 22, 20, 700, 13.5f, _th.Text);
            }
            yy += 48 + 10;

            // 요약 + 동기화 상태
            float sumX = x + 4;
            foreach (var part in new[] { ("남은 일일", view.RemainDaily), ("주간", view.RemainWeekly), ("보스", view.RemainBoss) })
            {
                sumX += Text(part.Item1, sumX, yy, 24, 400, 12, _th.Text3) + 4;
                sumX += Text(part.Item2.ToString(), sumX, yy, 24, 700, 12, _th.Text) + 10;
            }

            float bx = x + w - 24;
            Button(new SKRect(bx, yy, bx + 24, yy + 24), "settings", GearIcon);
            bx -= 6 + 24;
            Button(new SKRect(bx, yy, bx + 24, yy + 24), "refresh", RefreshIcon);

            string syncText;
            bool error = view.Notice != null && view.Notice.Level == "error";
            if (view.Syncing) syncText = "동기화 중…";
            else if (view.Demo) syncText = "데모 모드";
            else if (view.LastSync > 0) syncText = KstTime.FormatAgo(_in.Now - view.LastSync) + " 동기화";
            else if (error) syncText = "동기화 실패";
            else syncText = "대기 중";
            float stw = Width(syncText, 400, 11.5f);
            float stx = bx - 6 - stw;
            Text(syncText, stx, yy, 24, 400, 11.5f, _th.Text3);
            if (_c != null)
            {
                var dot = view.Syncing ? _th.Prog : error ? _th.Danger : _th.Done;
                using (var glow = new SKPaint { IsAntialias = true, Color = Theme.A(dot, 0.7), MaskFilter = SKMaskFilter.CreateBlur(SKBlurStyle.Normal, 2.5f) })
                {
                    _c.DrawCircle(stx - 9.5f, yy + 12, 3.5f, glow);
                }
                _fill.Color = dot;
                _c.DrawCircle(stx - 9.5f, yy + 12, 3.5f, _fill);
            }
            yy += 24;
            return yy + 10 - y;
        }

        private void Button(SKRect r, string action, Action<SKRect, SKColor> icon)
        {
            bool hover = IsHover(action);
            RRect(r, 7, Theme.White(hover ? 0.12 : 0.05), _th.Line);
            icon(r, hover ? _th.Text : _th.Text2);
            Hit(r, action);
        }

        private void RefreshIcon(SKRect r, SKColor color)
        {
            if (_c == null) return;
            Icon(Icons.RefreshArc, r.Left + 5.5f, r.Top + 5.5f, 13, 24, color, strokeWidth: 2.2f);
            Icon(Icons.RefreshHead, r.Left + 5.5f, r.Top + 5.5f, 13, 24, color, strokeWidth: 2.2f);
        }

        private void GearIcon(SKRect r, SKColor color)
        {
            if (_c == null) return;
            Icon(Icons.GearRays, r.Left + 5.5f, r.Top + 5.5f, 13, 24, color, strokeWidth: 2);
            _stroke.Color = color;
            _stroke.StrokeWidth = 2 * 13f / 24;
            _c.DrawCircle(r.MidX, r.MidY, 3.2f * 13f / 24, _stroke);
        }

        // 캐릭터가 아직 없을 때: 처음 불러오는 중이면 진행 상황을 보여준다
        private float Empty(float x, float y, float w)
        {
            var view = _in.View;
            string main = view.Syncing ? "캐릭터 정보를 불러오는 중…" : "표시할 캐릭터가 없습니다";
            float mw = Width(main, 400, 13);
            Text(main, x + (w - mw) / 2, y, 26, 400, 13, view.Syncing ? _th.Text2 : _th.Text3);
            if (!view.Syncing || string.IsNullOrEmpty(view.Progress)) return 26;
            float pw = Width(view.Progress, 400, 11.5f);
            Text(view.Progress, x + (w - pw) / 2, y + 24, 18, 400, 11.5f, _th.Text3);
            return 26 + 18;
        }

        private float Notices(float x, float y, float w)
        {
            var list = new List<Notice>();
            if (_in.View.Flash != null) list.Add(_in.View.Flash);
            if (_in.View.Demo) list.Add(new Notice("info", "데모 데이터입니다. 오른쪽 위 설정(톱니바퀴) 버튼에서 넥슨 Open API 키를 입력하세요."));
            if (_in.View.Notice != null) list.Add(_in.View.Notice);
            float yy = y;
            foreach (var n in list)
            {
                bool info = n.Level == "info";
                bool ok = n.Level == "success";
                bool err = n.Level == "error";
                float textX = x + 10 + (info ? 0 : 21);
                var lines = Wrap(n.Text, w - (textX - x) - 10, 400, 12);
                float h = 16 + lines.Count * 17.4f;
                var r = new SKRect(x, yy, x + w, yy + h);
                RRect(r, 10, err ? new SKColor(255, 90, 90, 36) : ok ? Theme.A(_th.Done, 0.13) : Theme.A(_th.Accent, 0.12),
                    err ? new SKColor(255, 110, 110, 89) : ok ? Theme.A(_th.Done, 0.36) : Theme.A(_th.Accent, 0.28));
                if (ok)
                {
                    Icon(Icons.Check, x + 10, yy + 9.5f, 14, 16, _th.Done, strokeWidth: 2.2f);
                }
                else if (!info)
                {
                    Icon(Icons.WarnTriangle, x + 10, yy + 9.5f, 14, 16, err ? _th.Danger : _th.Text, strokeWidth: 1.5f);
                    Icon(Icons.WarnMark, x + 10, yy + 9.5f, 14, 16, err ? _th.Danger : _th.Text, strokeWidth: 1.6f);
                }
                for (int i = 0; i < lines.Count; i++) Text(lines[i], textX, yy + 8 + i * 17.4f, 17.4f, 400, 12, _th.Text);
                yy += h + 8;
            }
            return yy - y;
        }

        /* ---------- 카드 ---------- */

        private float Card(CardView card, float x, float y, float w)
        {
            _cardKey = card.Key;
            var m = card.Model;
            bool complete = card.Complete;
            bool collapsed = card.Collapsed && !card.Editing;

            // 머리 높이는 고정, 본문은 먼저 크기만 잰다
            float headH = 68;
            var canvas = _c;
            _c = null;
            float bodyH = collapsed ? 0 : Body(card, x, y + headH, w);
            _c = canvas;
            float h = headH + bodyH;
            if (_c == null)
            {
                _cardKey = null;
                return h;
            }

            var rect = new SKRect(x, y, x + w, y + h);
            Hit(rect, "card", card.Key);
            SKShader bg = complete
                ? SKShader.CreateLinearGradient(new SKPoint(0, y), new SKPoint(0, y + h),
                    new[] { Theme.A(_th.Done, 0.07), Theme.White(0.03) }, null, SKShaderTileMode.Clamp)
                : null;
            var border = card.Editing ? Theme.A(_th.Accent, 0.45) : complete ? Theme.A(_th.Done, 0.28) : _th.Line;
            RRect(rect, 14, complete ? (SKColor?)null : Theme.White(0.045), border, shader: bg);
            bg?.Dispose();
            if (IsHover("toggle", card.Key))
            {
                _c.Save();
                _c.ClipRoundRect(new SKRoundRect(rect, 14), SKClipOperation.Intersect, true);
                _fill.Color = Theme.White(0.04);
                _c.DrawRect(new SKRect(x, y, x + w, y + headH), _fill);
                _c.Restore();
            }
            Hit(new SKRect(x, y, x + w, y + headH), "toggle", card.Key);
            CardHead(card, x, y, w);
            if (!collapsed)
            {
                _fill.Color = _th.Line;
                _c.DrawRect(new SKRect(x + 1, y + headH, x + w - 1, y + headH + 1), _fill);
                Body(card, x, y + headH, w);
            }
            _cardKey = null;
            return h;
        }

        private void CardHead(CardView card, float x, float y, float w)
        {
            var m = card.Model;
            float ax = x + 12, ay = y + 11;
            Avatar(card, ax, ay);

            // 오른쪽: 편집 버튼, 집계 또는 ALL CLEAR
            float right = x + w - 12;
            if (m != null)
            {
                var er = new SKRect(right - 20, y + 22, right + 4, y + 46);
                bool on = card.Editing;
                bool hover = IsHover("edit", card.Key);
                bool cardHover = _in.HoverCard == card.Key;
                if (on) RRect(er, 7, _th.Accent);
                else if (hover) RRect(er, 7, Theme.White(0.1));
                var col = on ? _th.Dark : hover ? _th.Text : Theme.A(_th.Text2, cardHover ? 0.72 : 0.25);
                Icon(Icons.Pencil, er.Left + 5, er.Top + 5, 14, 16, col, strokeWidth: 1.5f);
                Icon(Icons.PencilLine, er.Left + 5, er.Top + 5, 14, 16, col, strokeWidth: 1.5f);
                Hit(er, "edit", card.Key);
                right = er.Left - 6;
            }

            float whoRight = right;
            if (card.Complete)
            {
                float bw = Width("ALL CLEAR", 800, 9.5f, 9.5f * 0.08f) + 16;
                var br = new SKRect(right - bw, y + 26, right, y + 42);
                RRect(br, 8, _th.Done);
                Text("ALL CLEAR", br.Left + 8, br.Top, 16, 800, 9.5f, _th.DoneDark, spacing: 9.5f * 0.08f, shadow: false);
                whoRight = br.Left - 10;
            }
            else if (m != null)
            {
                var rows = new[] { ("일일", m.CountDaily), ("주간", m.CountWeekly), ("보스", m.CountBoss) }.Where(r => r.Item2.Total > 0).ToList();
                float tw = rows.Count == 0 ? 0 : rows.Max(r => Width(r.Item1, 400, 11) + 5 + Width(r.Item2.Done + "/" + r.Item2.Total, 700, 11));
                float ty = y + 34 - rows.Count * 15.4f / 2;
                foreach (var r in rows)
                {
                    var val = r.Item2.Done + "/" + r.Item2.Total;
                    float vw = Width(val, 700, 11);
                    Text(val, right - vw, ty, 15.4f, 700, 11, r.Item2.Complete ? _th.Done : _th.Text);
                    float lw = Width(r.Item1, 400, 11);
                    Text(r.Item1, right - vw - 5 - lw, ty, 15.4f, 400, 11, _th.Text3);
                    ty += 15.4f;
                }
                whoRight = right - tw - 10;
            }

            // 가운데: 이름, 직업·월드, 진행 막대
            float wx = ax + 46 + 10;
            float avail = whoRight - wx;
            float nameY = y + 11;
            string lv = card.Level > 0 ? "Lv." + card.Level : "";
            string exp = card.Exp.Length > 0 ? card.Exp + "%" : "";
            float extras = (lv.Length > 0 ? Width(lv, 700, 12) + 6 : 0) + (exp.Length > 0 ? Width(exp, 400, 11) + 6 : 0) + (card.Error != null && m != null ? 19 : 0);
            var name = Ellipsize(card.Name, Math.Max(40, avail - extras), 800, 15);
            float cx = wx + Text(name, wx, nameY, 21, 800, 15, _th.Text) + 6;
            if (lv.Length > 0) cx += Text(lv, cx, nameY + 1.5f, 21, 700, 12, _th.Accent) + 6;
            if (exp.Length > 0) cx += Text(exp, cx, nameY + 2, 21, 400, 11, _th.Text3) + 6;
            if (card.Error != null && m != null)
            {
                Icon(Icons.WarnTriangle, cx, nameY + 4, 13, 16, _th.Danger, strokeWidth: 1.5f);
                Icon(Icons.WarnMark, cx, nameY + 4, 13, 16, _th.Danger, strokeWidth: 1.6f);
            }
            var meta = string.Join(" · ", new[] { card.Cls, card.World }.Where(v => !string.IsNullOrEmpty(v)));
            float tagW = m != null && m.Kept ? Width("내 설정", 700, 9.5f) + 10 : 0;
            float mw = Text(Ellipsize(meta, Math.Max(20, avail - (tagW > 0 ? tagW + 6 : 0)), 400, 11.5f), wx, nameY + 22, 16, 400, 11.5f, _th.Text2);
            if (tagW > 0)
            {
                // 표시 항목을 유지 중인 캐릭터
                var tr = new SKRect(wx + mw + (mw > 0 ? 6 : 0), nameY + 24, wx + mw + (mw > 0 ? 6 : 0) + tagW, nameY + 36);
                RRect(tr, 6, Theme.A(_th.Accent, 0.12), Theme.A(_th.Accent, 0.55));
                Text("내 설정", tr.Left + 5, tr.Top, 12, 700, 9.5f, _th.Accent, shadow: false);
            }
            if (m != null && _c != null)
            {
                var track = new SKRect(wx, nameY + 42, wx + avail, nameY + 46);
                RRect(track, 2, Theme.White(0.08));
                float pct = m.CountAll.Total > 0 ? (float)m.CountAll.Done / m.CountAll.Total : 0;
                if (pct > 0)
                {
                    var fillR = new SKRect(track.Left, track.Top, track.Left + track.Width * pct, track.Bottom);
                    var c1 = card.Complete ? _th.Done : _th.Accent;
                    using (var sh = SKShader.CreateLinearGradient(new SKPoint(fillR.Left, 0), new SKPoint(fillR.Right, 0),
                        new[] { Theme.A(c1, 0.85), c1 }, null, SKShaderTileMode.Clamp))
                    {
                        RRect(fillR, 2, null, shader: sh);
                    }
                }
            }
        }

        private void Avatar(CardView card, float x, float y)
        {
            if (_c == null) return;
            var r = new SKRect(x, y, x + 46, y + 46);
            var rr = new SKRoundRect(r, 12);
            using (var sh = SKShader.CreateRadialGradient(new SKPoint(r.MidX, r.Top + 46 * 0.35f), 46 * 0.7f,
                new[] { Theme.A(_th.Accent, 0.35), Theme.A(_th.Accent, 0) }, null, SKShaderTileMode.Clamp))
            {
                RRect(r, 12, Theme.White(0.06));
                RRect(r, 12, null, _th.Line, shader: sh);
            }
            var img = !string.IsNullOrEmpty(card.ImageUrl) ? _in.Avatar?.Invoke(card.ImageUrl) : null;
            if (img != null)
            {
                _c.Save();
                _c.ClipRoundRect(rr, SKClipOperation.Intersect, true);
                // 96px 원본에서 상반신이 보이도록 (웹 버전: 92px, translate(-50%, -54%))
                var dest = SKRect.Create(r.MidX - 46, r.MidY - 92 * 0.54f, 92, 92);
                _c.DrawImage(img, dest, new SKSamplingOptions(SKFilterMode.Linear, SKMipmapMode.None));
                _c.Restore();
                return;
            }
            var letter = string.IsNullOrEmpty(card.Name) ? "?" : card.Name.Substring(0, 1);
            float lw = Width(letter, 800, 19);
            Text(letter, r.MidX - lw / 2, r.Top, 46, 800, 19, Theme.White(0.9));
        }

        /* ---------- 카드 본문 ---------- */

        private float Body(CardView card, float x, float y, float w)
        {
            var m = card.Model;
            float ix = x + 12, iw = w - 24;
            float yy = y + 2;
            if (m != null && card.Editing) yy = Editor(card, ix, yy, iw);
            else if (m != null)
            {
                bool hint = m.ShowingAll && !_in.Settings.ShowAll && m.RegisteredCount == 0 && !m.Customized;
                if (hint) yy = Hint("인게임 스케줄러에 등록된 항목이 없어 전체 항목을 표시합니다", ix, yy, iw);
                if (m.Kept && m.HiddenNew > 0)
                    yy = Hint("인게임 스케줄러에 새로 등록된 항목 " + m.HiddenNew + "개는 내 설정을 유지하느라 숨겨 두었습니다. 연필 버튼에서 켤 수 있습니다", ix, yy, iw);
                float start = yy;
                yy = Section("일일", m.Daily, m.CountDaily, ix, yy, iw, null, false);
                yy = Section("주간", m.Weekly, m.CountWeekly, ix, yy, iw, null, false);
                var note = m.BossLimit > 0 ? "주간 클리어 " + m.BossClear + "/" + m.BossLimit : null;
                yy = Section("보스", m.Boss, m.CountBoss, ix, yy, iw, note, true);
                if (yy == start) yy = Message("표시할 항목이 없습니다. 연필 버튼으로 고르세요", ix, yy, iw, _th.Text3);
            }
            else if (card.Error != null)
            {
                yy = Message(card.Error, ix, yy, iw, new SKColor(255, 177, 177), warn: true);
            }
            else
            {
                // 불러오는 중
                yy += 12;
                foreach (var frac in new[] { 1f, 0.8f, 0.6f })
                {
                    RRect(new SKRect(ix, yy, ix + iw * frac, yy + 18), 9, Theme.White(0.08));
                    yy += 18 + 7;
                }
                yy -= 7;
            }
            return yy + 12 - y;
        }

        private float Hint(string text, float x, float y, float w)
        {
            var lines = Wrap(text, w - 16, 400, 11.5f);
            float h = 12 + lines.Count * 16;
            RRect(new SKRect(x, y + 10, x + w, y + 10 + h), 8, Theme.White(0.04));
            for (int i = 0; i < lines.Count; i++) Text(lines[i], x + 8, y + 16 + i * 16, 16, 400, 11.5f, _th.Text3);
            return y + 10 + h;
        }

        private float Message(string text, float x, float y, float w, SKColor color, bool warn = false)
        {
            float tx = x + (warn ? 19 : 0);
            var lines = Wrap(text, w - (tx - x), 400, 11.5f);
            if (warn)
            {
                Icon(Icons.WarnTriangle, x, y + 12, 13, 16, color, strokeWidth: 1.5f);
                Icon(Icons.WarnMark, x, y + 12, 13, 16, color, strokeWidth: 1.6f);
            }
            for (int i = 0; i < lines.Count; i++) Text(lines[i], tx, y + 10 + i * 16, 16, 400, 11.5f, color);
            return y + 10 + lines.Count * 16;
        }

        private float Section(string title, List<SchedItem> items, Tally count, float x, float y, float w, string note, bool boss)
        {
            if (items.Count == 0) return y;
            float yy = y + 10;
            float tx = x + Text(title, x, yy, 15.4f, 800, 11, _th.Text2, spacing: 11 * 0.06f) + 6;
            var countColor = count.Complete ? _th.Done : _th.Accent;
            tx += Text(count.Done.ToString(), tx, yy, 15.4f, 700, 11, countColor);
            Text("/" + count.Total, tx, yy, 15.4f, 400, 11, _th.Text3);
            if (note != null)
            {
                float nw = Width(note, 400, 10.5f);
                Text(note, x + w - nw, yy, 15.4f, 400, 10.5f, _th.Text3);
            }
            yy += 15.4f + 6;

            var shown = _in.Settings.HideDone ? items.Where(i => !i.Done).ToList() : items;
            if (shown.Count == 0)
            {
                Icon(Icons.Check, x, yy + 2, 12, 16, _th.Done, strokeWidth: 2.2f);
                Text("모두 완료", x + 17, yy, 16, 400, 11.5f, _th.Done);
                return yy + 16;
            }
            return LayoutChips(shown.Select(it => new ChipSpec(it, boss, false)).ToList(), x, yy, w);
        }

        /* ---------- 칩 ---------- */

        private sealed class ChipSpec
        {
            public SchedItem Item;
            public bool Boss;
            public bool Pick;          // 편집 화면용 선택 칩
            public string CardKey;
            public ChipSpec(SchedItem item, bool boss, bool pick, string cardKey = null) { Item = item; Boss = boss; Pick = pick; CardKey = cardKey; }
        }

        private const float ChipH = 21.5f;

        // "OO 일일 퀘스트"의 꼬리말은 섹션 이름과 겹쳐서 줄인다
        private static string ShortName(string name)
        {
            var s = System.Text.RegularExpressions.Regex.Replace(name ?? "", @"\s*(일일|주간)\s*퀘스트\s*$", "");
            return s.Length > 0 ? s : name;
        }

        private float LayoutChips(List<ChipSpec> specs, float x, float y, float w)
        {
            float cx = x, cy = y;
            foreach (var spec in specs)
            {
                float cw = ChipWidth(spec, w);
                if (cx > x && cx + cw > x + w)
                {
                    cx = x;
                    cy += ChipH + 5;
                }
                DrawChip(spec, cx, cy, Math.Min(cw, w));
                cx += cw + 5;
            }
            return cy + ChipH;
        }

        private string ChipLabel(ChipSpec s) => s.Boss || s.Item.IsBoss ? s.Item.Name : ShortName(s.Item.Name);

        private float ChipExtrasWidth(ChipSpec s)
        {
            var it = s.Item;
            float wsum = 0;
            if (it.IsBoss && it.Difficulty.Length > 0) wsum += 5 + Width(it.Difficulty, 800, 10) + 10;
            else if (!s.Pick && !it.Quest && it.Max > 1) wsum += 5 + Width(it.Now + "/" + it.Max, 700, 10.5f);
            if (!s.Pick && it.IsBoss && (it.Reset == ResetKind.Monthly || it.Reset == ResetKind.Daily)) wsum += 5 + Width("월", 700, 10.5f) + 8;
            if (s.Pick && it.Registered) wsum += 5 + 9;
            return wsum;
        }

        private float ChipWidth(ChipSpec s, float max)
        {
            return Math.Min(max, 5 + 13 + 5 + Width(ChipLabel(s), 600, 11.5f) + ChipExtrasWidth(s) + 9);
        }

        private void DrawChip(ChipSpec s, float x, float y, float w)
        {
            var it = s.Item;
            var r = new SKRect(x, y, x + w, y + ChipH);
            if (s.Pick) Hit(r, "item", s.CardKey, it.Key);
            if (_c == null) return;

            bool on = s.Pick ? it.Visible : false;
            string state = s.Pick ? (on ? "on" : "off") : it.Done ? "done" : it.Progress ? "prog" : "todo";
            bool hover = s.Pick && IsHover("item", s.CardKey, it.Key);
            SKColor fill, border, text;
            switch (state)
            {
                case "todo": fill = Theme.A(_th.Accent, 0.10); border = Theme.A(_th.Accent, 0.32); text = _th.Text; break;
                case "prog": fill = new SKColor(124, 199, 255, 26); border = new SKColor(124, 199, 255, 89); text = _th.Text; break;
                case "done": fill = Theme.White(0.035); border = Theme.White(0.06); text = _th.Text3; break;
                case "on": fill = Theme.A(_th.Accent, 0.16); border = Theme.A(_th.Accent, 0.55); text = _th.Text; break;
                default: fill = SKColors.Transparent; border = Theme.White(0.14); text = _th.Text3; break;
            }
            if (hover) border = Theme.A(_th.Accent, 0.8);
            RRect(r, ChipH / 2, fill);
            if (state == "off") DashedRoundRect(r, border);
            else RRect(r, ChipH / 2, null, border);

            // 상태 점
            float dx = x + 5 + 6.5f, dy = y + ChipH / 2;
            switch (state)
            {
                case "done":
                    _fill.Color = Theme.A(_th.Done, 0.9);
                    _c.DrawCircle(dx, dy, 6.5f, _fill);
                    Icon(Icons.Check, dx - 4.5f, dy - 4.5f, 9, 16, _th.DoneDark, strokeWidth: 2.2f);
                    break;
                case "on":
                    _fill.Color = _th.Accent;
                    _c.DrawCircle(dx, dy, 6.5f, _fill);
                    Icon(Icons.Check, dx - 4.5f, dy - 4.5f, 9, 16, _th.Dark, strokeWidth: 2.2f);
                    break;
                case "prog":
                    _fill.Color = _th.Prog;
                    _c.DrawArc(new SKRect(dx - 5.75f, dy - 5.75f, dx + 5.75f, dy + 5.75f), -90, 180, true, _fill);
                    _stroke.Color = _th.Prog;
                    _stroke.StrokeWidth = 1.5f;
                    _c.DrawCircle(dx, dy, 5.75f, _stroke);
                    break;
                case "off":
                    _stroke.Color = Theme.White(0.25);
                    _stroke.StrokeWidth = 1.5f;
                    _c.DrawCircle(dx, dy, 5.75f, _stroke);
                    break;
                default:
                    _stroke.Color = Theme.A(_th.Accent, 0.9);
                    _stroke.StrokeWidth = 1.5f;
                    _c.DrawCircle(dx, dy, 5.75f, _stroke);
                    break;
            }

            // 이름 (+ 꼬리표)
            float lx = x + 5 + 13 + 5;
            float labelMax = w - (lx - x) - ChipExtrasWidth(s) - 9;
            var label = Ellipsize(ChipLabel(s), Math.Max(10, labelMax), 600, 11.5f);
            float lw = Text(label, lx, y, ChipH, 600, 11.5f, text);
            if (state == "done")
            {
                _fill.Color = Theme.White(0.28);
                _c.DrawRect(new SKRect(lx, y + ChipH / 2, lx + lw, y + ChipH / 2 + 1), _fill);
            }
            float ex = lx + lw;
            bool dim = state == "done" || state == "off";
            if (it.IsBoss && it.Difficulty.Length > 0)
            {
                var bg = Theme.Difficulty(it.Difficulty, out var dt);
                float bw = Width(it.Difficulty, 800, 10) + 10;
                var br = new SKRect(ex + 5, y + (ChipH - 15) / 2, ex + 5 + bw, y + (ChipH + 15) / 2);
                RRect(br, 5, dim ? bg.WithAlpha(102) : bg);
                Text(it.Difficulty, br.Left + 5, br.Top, 15, 800, 10, dim ? dt.WithAlpha(140) : dt, shadow: false);
                ex = br.Right;
            }
            else if (!s.Pick && !it.Quest && it.Max > 1)
            {
                ex += 5 + Text(it.Now + "/" + it.Max, ex + 5, y, ChipH, 700, 10.5f, state == "done" ? _th.Text3 : _th.Text2);
            }
            if (!s.Pick && it.IsBoss && (it.Reset == ResetKind.Monthly || it.Reset == ResetKind.Daily))
            {
                var t = it.Reset == ResetKind.Monthly ? "월" : "일";
                float bw = Width(t, 700, 10.5f) + 8;
                var br = new SKRect(ex + 5, y + (ChipH - 15) / 2, ex + 5 + bw, y + (ChipH + 15) / 2);
                RRect(br, 4, Theme.White(0.10));
                Text(t, br.Left + 4, br.Top, 15, 700, 10.5f, _th.Text2, shadow: false);
                ex = br.Right;
            }
            if (s.Pick && it.Registered)
            {
                Icon(Icons.Star, ex + 5, y + (ChipH - 9) / 2, 9, 16, _th.Accent, fill: true);
            }
        }

        private void DashedRoundRect(SKRect r, SKColor color)
        {
            using (var dash = SKPathEffect.CreateDash(new[] { 3f, 2.5f }, 0))
            {
                _stroke.Color = color;
                _stroke.StrokeWidth = 1;
                _stroke.PathEffect = dash;
                var inset = r;
                inset.Inflate(-0.5f, -0.5f);
                _c.DrawRoundRect(inset, inset.Height / 2, inset.Height / 2, _stroke);
                _stroke.PathEffect = null;
            }
        }

        /* ---------- 표시 항목 편집 ---------- */

        private float Editor(CardView card, float x, float y, float w)
        {
            var m = card.Model;
            float yy = y + 10;
            // 위: 지금 방식, API 불러오기, 유지하기(바꾼 것이 있을 때), 완료
            float bx = x + w;
            bx -= LinkButton("완료", bx, yy, "edit", card.Key, null, primary: true) + 6;
            if (m.Customized && !m.Kept) bx -= LinkButton("유지하기", bx, yy, "keep", card.Key, null, accent: true) + 6;
            bx -= LinkButton("API 불러오기", bx, yy, "reload", card.Key, null) + 6;
            string mode = m.Kept ? "내 설정 유지 중" : m.Customized ? "내가 바꿈" : "인게임 등록 기준";
            if (bx - x > 40) Text(Ellipsize(mode, bx - x - 4, 700, 11), x, yy, 22, 700, 11, m.Kept ? _th.Accent : _th.Text2);
            yy += 22 + 4;

            // 지금 방식 설명
            string about = m.Kept
                ? "인게임 스케줄러 등록이 바뀌어도 지금 고른 대로 보여줍니다. API 불러오기를 누르면 인게임 등록 기준으로 돌아갑니다."
                : m.Customized
                    ? "바꾼 항목은 저장됩니다. 나머지는 인게임 등록을 따릅니다. 유지하기를 누르면 전부 지금 그대로 고정합니다."
                    : "인게임 스케줄러에 등록된 항목을 보여줍니다. 표시할 항목을 누르세요.";
            foreach (var line in Wrap(about, w, 400, 11))
            {
                Text(line, x, yy, 16, 400, 11, _th.Text3);
                yy += 16;
            }
            Icon(Icons.Star, x, yy + 3.5f, 9, 16, _th.Accent, fill: true);
            Text("인게임 스케줄러 등록", x + 12, yy, 16, 400, 11, _th.Text3);
            yy += 16 + 4;

            if (m.Groups.Count == 0) return Message("스케줄러 항목이 없습니다", x, yy, w, _th.Text3);
            foreach (var g in m.Groups)
            {
                yy += 8;
                if (_c != null)
                {
                    _fill.Color = _th.Line;
                    _c.DrawRect(new SKRect(x, yy, x + w, yy + 1), _fill);
                }
                yy += 8;
                // 분류 머리: 스위치, 이름, 개수, 전체 선택/해제
                var sw = new SKRect(x, yy + 3, x + 28, yy + 19);
                Switch(sw, g.On);
                Hit(new SKRect(sw.Left - 2, sw.Top - 3, sw.Right + 2, sw.Bottom + 3), "group", card.Key, g.Id);
                float tx = sw.Right + 7;
                tx += Text(g.Label, tx, yy, 22, 800, 11.5f, g.On ? _th.Text : _th.Text3) + 7;
                int shown = g.On ? g.Items.Count(i => i.Visible) : 0;
                Text(shown + "/" + g.Items.Count, tx, yy, 22, 700, 11, g.On ? _th.Accent : _th.Text3);
                float rx = x + w;
                if (g.On)
                {
                    rx -= LinkButton("전체 해제", rx, yy, "none", card.Key, g.Id) + 6;
                    LinkButton("전체 선택", rx, yy, "all", card.Key, g.Id);
                }
                else
                {
                    float ow = Width("숨김", 400, 11);
                    Text("숨김", rx - ow, yy, 22, 400, 11, _th.Text3);
                }
                yy += 22;
                if (g.On)
                {
                    yy += 7;
                    yy = LayoutChips(g.Items.Select(it => new ChipSpec(it, it.IsBoss, true, card.Key)).ToList(), x, yy, w);
                }
            }
            return yy + 2;
        }

        /// <summary>오른쪽 끝(right)에 붙는 작은 버튼. 너비를 돌려준다</summary>
        private float LinkButton(string label, float right, float y, string action, string key, string id, bool primary = false, bool accent = false)
        {
            float bw = Width(label, 600, 11) + 18;
            var r = new SKRect(right - bw, y, right, y + 22);
            bool hover = IsHover(action, key, id);
            if (primary) RRect(r, 7, _th.Accent, _th.Accent);
            else if (accent) RRect(r, 7, Theme.A(_th.Accent, hover ? 0.24 : 0.12), Theme.A(_th.Accent, 0.6));
            else RRect(r, 7, Theme.White(hover ? 0.10 : 0.05), _th.Line);
            var color = primary ? _th.Dark : accent ? _th.Accent : hover ? _th.Text : _th.Text2;
            Text(label, r.Left + 9, y, 22, 600, 11, color, shadow: !primary);
            Hit(r, action, key, id);
            return bw;
        }

        private void Switch(SKRect r, bool on)
        {
            if (_c == null) return;
            RRect(r, 8, on ? _th.Accent : Theme.White(0.18));
            _fill.Color = SKColors.White;
            _c.DrawCircle(on ? r.Right - 8 : r.Left + 8, r.MidY, 6, _fill);
        }

        public void Dispose()
        {
            _bodyImage?.Dispose();
            foreach (var f in _fontCache.Values) f.Dispose();
            _fontCache.Clear();
            _fill.Dispose();
            _stroke.Dispose();
            _text.Dispose();
            _shadow.Dispose();
        }
    }
}
