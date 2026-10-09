using System;
using System.Drawing;
using System.Windows.Forms;
using MapleHud.Core;
using MapleHud.Core.Render;
using SkiaSharp;

namespace MapleHud
{
    /// <summary>
    /// HUD를 띄우는 투명 창.
    /// - 픽셀마다 투명도가 있는 레이어드 창(UpdateLayeredWindow)이라 패널 모양 그대로 반투명하게 보인다.
    /// - 클릭해도 활성화되지 않아서(WS_EX_NOACTIVATE) 다른 창 위로 튀어나오거나 포커스를 뺏지 않는다.
    /// - 작업 표시줄과 Alt+Tab에 나오지 않는다(WS_EX_TOOLWINDOW).
    /// </summary>
    internal sealed class OverlayWindow : Form
    {
        private readonly HudRenderer _renderer;
        private readonly Func<RenderInput> _input;

        private IntPtr _memDc;
        private IntPtr _bitmap;
        private IntPtr _oldBitmap;
        private IntPtr _bits;
        private int _bmpW, _bmpH;

        private HudLayout _layout;
        private float _scale = 1;
        private bool _topMost;
        private bool _presenting;
        private bool _presentAgain;

        /// <summary>클릭한 영역 (action, key, id)</summary>
        public event Action<HitRegion> RegionClicked;
        /// <summary>마우스가 올라간 영역이나 카드가 바뀜</summary>
        public event Action HoverChanged;
        public event Action<float> Scrolled;

        public HitRegion Hover { get; private set; }
        public string HoverCard { get; private set; }
        public HudLayout CurrentLayout => _layout;

        public OverlayWindow(HudRenderer renderer, Func<RenderInput> input)
        {
            _renderer = renderer;
            _input = input;
            FormBorderStyle = FormBorderStyle.None;
            ShowInTaskbar = false;
            StartPosition = FormStartPosition.Manual;
            Text = "Maple Scheduler HUD";
            AutoScaleMode = AutoScaleMode.None;
        }

        protected override bool ShowWithoutActivation => true;

        protected override CreateParams CreateParams
        {
            get
            {
                var cp = base.CreateParams;
                cp.Style = Native.WS_POPUP;
                cp.ExStyle |= Native.WS_EX_LAYERED | Native.WS_EX_TOOLWINDOW | Native.WS_EX_NOACTIVATE;
                if (_topMost) cp.ExStyle |= Native.WS_EX_TOPMOST;
                return cp;
            }
        }

        public void SetTopMost(bool topMost)
        {
            _topMost = topMost;
            if (!IsHandleCreated) return;
            Native.SetWindowPos(Handle, topMost ? Native.HWND_TOPMOST : Native.HWND_NOTOPMOST, 0, 0, 0, 0,
                Native.SWP_NOMOVE | Native.SWP_NOSIZE | Native.SWP_NOACTIVATE);
        }

        public void BringToFront2()
        {
            if (!IsHandleCreated) return;
            Native.SetWindowPos(Handle, _topMost ? Native.HWND_TOPMOST : Native.HWND_TOP, 0, 0, 0, 0,
                Native.SWP_NOMOVE | Native.SWP_NOSIZE | Native.SWP_NOACTIVATE | Native.SWP_SHOWWINDOW);
        }

        protected override void WndProc(ref Message m)
        {
            if (m.Msg == Native.WM_MOUSEACTIVATE)
            {
                m.Result = (IntPtr)Native.MA_NOACTIVATE;
                return;
            }
            base.WndProc(ref m);
            if (m.Msg == Native.WM_DPICHANGED || m.Msg == Native.WM_DISPLAYCHANGE) Present(force: true);
        }

        /* ---------- 그리기 ---------- */

        private string _lastFrame;

        /// <summary>
        /// HUD를 다시 그려 화면에 올린다. frameKey가 지난번과 같으면 건너뛴다.
        /// </summary>
        public void Present(string frameKey = null, bool force = false)
        {
            if (!IsHandleCreated) return;
            // 그리는 도중에 다시 그리라는 요청이 오면 겹쳐 그리지 않고, 끝난 뒤에 한 번 더 그린다
            // (겹쳐 그리면 쓰던 비트맵·캔버스가 중간에 바뀌어 앱이 죽는다)
            if (_presenting)
            {
                _presentAgain = true;
                return;
            }
            _presenting = true;
            try
            {
                PresentCore(frameKey, force);
            }
            finally
            {
                _presenting = false;
            }
            if (_presentAgain)
            {
                _presentAgain = false;
                if (!IsDisposed) BeginInvoke((Action)(() => Present(null, true)));
            }
        }

        private void PresentCore(string frameKey, bool force)
        {
            var input = _input();
            var s = input.Settings;
            var screen = TargetScreen(s);
            var wa = screen.WorkingArea;
            float dpiScale = Native.DpiFor(Handle) / 96f;
            if (!Visible) dpiScale = ScreenDpi(screen) / 96f;
            _scale = dpiScale * s.Scale / 100f;
            int offX = (int)Math.Round(s.OffsetX * dpiScale), offY = (int)Math.Round(s.OffsetY * dpiScale);
            input.MaxHeight = Math.Max(160, (wa.Height - 2 * offY) / _scale);
            input.Hover = Hover;
            input.HoverCard = HoverCard;

            var key = frameKey + "|" + _scale + "|" + wa;
            if (!force && frameKey != null && key == _lastFrame) return;
            _lastFrame = key;

            var layout = _renderer.Measure(input);
            int w = (int)Math.Ceiling(layout.Width * _scale);
            int h = (int)Math.Ceiling(layout.Height * _scale);
            EnsureBitmap(w, h);

            var info = new SKImageInfo(_bmpW, _bmpH, SKColorType.Bgra8888, SKAlphaType.Premul);
            using (var surface = SKSurface.Create(info, _bits, _bmpW * 4))
            {
                var canvas = surface.Canvas;
                canvas.Clear(SKColors.Transparent);
                canvas.Scale(_scale);
                _layout = _renderer.Draw(canvas, input);
                canvas.Flush();
            }

            int x = s.AlignX == "left" ? wa.Left + offX : s.AlignX == "center" ? wa.Left + (wa.Width - w) / 2 : wa.Right - offX - w;
            int y = s.AlignY == "top" ? wa.Top + offY : s.AlignY == "center" ? wa.Top + (wa.Height - h) / 2 : wa.Bottom - offY - h;

            var dst = new Native.POINT(x, y);
            var size = new Native.SIZE(w, h);
            var src = new Native.POINT(0, 0);
            var blend = new Native.BLENDFUNCTION
            {
                BlendOp = Native.AC_SRC_OVER,
                SourceConstantAlpha = 255,
                AlphaFormat = Native.AC_SRC_ALPHA
            };
            var screenDc = Native.GetDC(IntPtr.Zero);
            try
            {
                Native.UpdateLayeredWindow(Handle, screenDc, ref dst, ref size, _memDc, ref src, 0, ref blend, Native.ULW_ALPHA);
            }
            finally
            {
                Native.ReleaseDC(IntPtr.Zero, screenDc);
            }
        }

        /// <summary>테스트용: 마지막으로 그린 픽셀을 PNG로</summary>
        public byte[] Snapshot()
        {
            if (_bits == IntPtr.Zero) return null;
            var info = new SKImageInfo(_bmpW, _bmpH, SKColorType.Bgra8888, SKAlphaType.Premul);
            using (var img = SKImage.FromPixelCopy(info, _bits, _bmpW * 4))
            using (var data = img.Encode(SKEncodedImageFormat.Png, 100)) return data.ToArray();
        }

        private void EnsureBitmap(int w, int h)
        {
            w = Math.Max(1, w);
            h = Math.Max(1, h);
            // 크기가 줄어든 정도로는 새로 만들지 않는다 (접기/펼치기마다 다시 할당하지 않도록)
            if (_bitmap != IntPtr.Zero && _bmpW == w && _bmpH == h) return;
            FreeBitmap();
            var screenDc = Native.GetDC(IntPtr.Zero);
            _memDc = Native.CreateCompatibleDC(screenDc);
            Native.ReleaseDC(IntPtr.Zero, screenDc);
            var bmi = new Native.BITMAPINFOHEADER
            {
                biSize = System.Runtime.InteropServices.Marshal.SizeOf(typeof(Native.BITMAPINFOHEADER)),
                biWidth = w,
                biHeight = -h,   // 위에서 아래로
                biPlanes = 1,
                biBitCount = 32
            };
            _bitmap = Native.CreateDIBSection(_memDc, ref bmi, 0, out _bits, IntPtr.Zero, 0);
            _oldBitmap = Native.SelectObject(_memDc, _bitmap);
            _bmpW = w;
            _bmpH = h;
        }

        private void FreeBitmap()
        {
            if (_memDc != IntPtr.Zero)
            {
                Native.SelectObject(_memDc, _oldBitmap);
                Native.DeleteDC(_memDc);
                _memDc = IntPtr.Zero;
            }
            if (_bitmap != IntPtr.Zero)
            {
                Native.DeleteObject(_bitmap);
                _bitmap = IntPtr.Zero;
            }
            _bits = IntPtr.Zero;
        }

        public static Screen TargetScreen(HudSettings s)
        {
            foreach (var sc in Screen.AllScreens)
            {
                if (sc.DeviceName == s.Monitor) return sc;
            }
            return Screen.PrimaryScreen;
        }

        private static int ScreenDpi(Screen screen)
        {
            using (var g = Graphics.FromHwnd(IntPtr.Zero)) return (int)g.DpiX;
        }

        /* ---------- 마우스 ---------- */

        private PointF ToCss(Point p) => new PointF(p.X / _scale, p.Y / _scale);

        protected override void OnMouseMove(MouseEventArgs e)
        {
            base.OnMouseMove(e);
            if (_layout == null) return;
            var p = ToCss(e.Location);
            var hit = _layout.HitTest(p.X, p.Y);
            if (hit != null && hit.Action == "card") hit = null;
            var card = _layout.CardAt(p.X, p.Y);
            bool changed = !(hit == null ? Hover == null : hit.Same(Hover)) || card != HoverCard;
            Hover = hit;
            HoverCard = card;
            Cursor = hit != null ? Cursors.Hand : Cursors.Default;
            if (changed) HoverChanged?.Invoke();
        }

        protected override void OnMouseLeave(EventArgs e)
        {
            base.OnMouseLeave(e);
            if (Hover == null && HoverCard == null) return;
            Hover = null;
            HoverCard = null;
            HoverChanged?.Invoke();
        }

        protected override void OnMouseUp(MouseEventArgs e)
        {
            base.OnMouseUp(e);
            if (e.Button != MouseButtons.Left || _layout == null) return;
            var p = ToCss(e.Location);
            var hit = _layout.HitTest(p.X, p.Y);
            if (hit != null && hit.Action != "card") RegionClicked?.Invoke(hit);
        }

        protected override void OnMouseWheel(MouseEventArgs e)
        {
            base.OnMouseWheel(e);
            if (_layout == null || _layout.MaxScroll <= 0) return;
            Scrolled?.Invoke(-e.Delta / 120f * 60f);
        }

        protected override void Dispose(bool disposing)
        {
            FreeBitmap();
            base.Dispose(disposing);
        }
    }
}
