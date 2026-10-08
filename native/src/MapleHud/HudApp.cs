using System;
using System.Drawing;
using System.IO;
using System.Linq;
using System.Net.Http;
using System.Reflection;
using System.Windows.Forms;
using MapleHud.Core;
using MapleHud.Core.Render;

namespace MapleHud
{
    /// <summary>앱 전체: 동기화 엔진, HUD 창, 트레이, 설정 창을 묶고 1초 타이머를 돌린다</summary>
    internal sealed class HudApp : ApplicationContext
    {
        // MAPLEHUD_HOME이 있으면 그 아래에 모두 저장한다 (자체 점검·테스트용)
        private static readonly string Home = Environment.GetEnvironmentVariable("MAPLEHUD_HOME");
        private static readonly string DataDir = !string.IsNullOrEmpty(Home) ? Path.Combine(Home, "data")
            : Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData), "MapleSchedulerHUD");
        private static readonly string CacheDir = !string.IsNullOrEmpty(Home) ? Path.Combine(Home, "cache")
            : Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "MapleSchedulerHUD");
        private static string SettingsPath => Path.Combine(DataDir, "settings.json");

        private readonly HttpClient _http = NexonApi.CreateHttpClient();
        private readonly JsonStore _store;
        private readonly SyncEngine _engine;
        private readonly HudController _controller;
        private readonly FontSet _fonts;
        private readonly HudRenderer _renderer;
        private readonly AvatarCache _avatars;
        private readonly OverlayWindow _window;
        private readonly NotifyIcon _tray;
        private readonly Icon _icon;
        private readonly Timer _timer = new Timer();

        private HudSettings _settings;
        private HudView _view;
        private int _bodyVersion;
        private float _scroll;
        private long _lastDailyReset;
        private bool _idle;
        private SettingsForm _settingsForm;

        public HudApp()
        {
            _settings = HudSettings.Load(SettingsPath);
            _store = new JsonStore(Path.Combine(DataDir, "store.json"));
            // 한 달 넘게 안 쓴 캐시(더 이상 표시하지 않는 캐릭터 등)를 정리한다
            _store.Prune(31L * 24 * 3600 * 1000, KstTime.NowMs());

            _engine = new SyncEngine(_settings, _store, s => new NexonApi(_http, s.ApiKey, s.ApiBase));
            _controller = new HudController(_engine, _store, _settings);
            _fonts = FontSet.Load(Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "fonts"));
            _renderer = new HudRenderer(_fonts);
            _avatars = new AvatarCache(_http, Path.Combine(CacheDir, "avatars"));
            _icon = LoadIcon();

            _window = new OverlayWindow(_renderer, BuildInput);
            _window.SetTopMost(_settings.AlwaysOnTop);
            _window.RegionClicked += OnRegionClicked;
            _window.HoverChanged += () => Invalidate(body: true);
            _window.Scrolled += delta =>
            {
                _scroll = Math.Max(0, Math.Min(_scroll + delta, _window.CurrentLayout?.MaxScroll ?? 0));
                Invalidate(body: true);
            };
            _engine.Changed += () => Invalidate(body: true, rebuild: true);
            _avatars.Loaded += () => Invalidate(body: true);

            _tray = new NotifyIcon { Icon = _icon, Text = "Maple Scheduler HUD", Visible = true, ContextMenuStrip = BuildMenu() };
            _tray.MouseClick += (s, e) => { if (e.Button == MouseButtons.Left) _window.BringToFront2(); };

            _window.Show();
            Invalidate(body: true, rebuild: true);
            _lastDailyReset = KstTime.LastReset(ResetKind.Daily, KstTime.NowMs());

            _timer.Tick += OnTick;
            _timer.Interval = 1000;
            _timer.Start();
            _ = _engine.SyncAsync(false);
        }

        /* ---------- 화면 ---------- */

        private RenderInput BuildInput() => new RenderInput
        {
            View = _view,
            Settings = _settings,
            Now = KstTime.NowMs(),
            Scroll = _scroll,
            Avatar = _avatars.Get,
            BodyVersion = _bodyVersion
        };

        /// <summary>body: 카드 영역이 바뀜, rebuild: 데이터가 바뀌어 화면 모델을 다시 만듦</summary>
        private void Invalidate(bool body = false, bool rebuild = false)
        {
            if (rebuild || _view == null)
            {
                _controller.Settings = _settings;
                _view = _controller.Build();
            }
            if (body) _bodyVersion++;
            Present(force: body);
        }

        private void Present(bool force = false)
        {
            if (_idle && !force) return;
            var now = KstTime.NowMs();
            // 같은 내용이면 다시 그리지 않는다: 초 표시를 끄면 1분에 한 번만 그린다
            var frame = _bodyVersion + "|" + (_settings.ShowSeconds ? now / 1000 : now / 60000) + "|" + _engine.Syncing;
            _window.Present(frame, force);
        }

        private void OnTick(object sender, EventArgs e)
        {
            var now = KstTime.NowMs();
            // 다음 초가 바뀌는 순간에 맞춘다
            _timer.Interval = (int)(1000 - now % 1000) + 5;

            // 전체 화면 게임 중이면 그리지도, 갱신하지도 않는다. 끝나면 밀린 갱신을 한다
            bool idle = Native.FullscreenAppRunning();
            if (idle != _idle)
            {
                _idle = idle;
                if (!idle) Invalidate(body: true, rebuild: true);
            }
            _engine.Tick(_idle);
            if (_idle) return;

            // 자정이 지나면 완료 표시를 미리 해제해서 다시 그린다
            var reset = KstTime.LastReset(ResetKind.Daily, now);
            if (reset != _lastDailyReset)
            {
                _lastDailyReset = reset;
                Invalidate(body: true, rebuild: true);
                return;
            }
            Present();
        }

        private void OnRegionClicked(HitRegion r)
        {
            switch (r.Action)
            {
                case "refresh":
                    _engine.ManualRefresh();
                    return;
                case "settings":
                    ShowSettings();
                    return;
                case "toggle":
                    _controller.ToggleCollapse(r.Key);
                    break;
                case "edit":
                    _controller.ToggleEdit(r.Key);
                    break;
                default:
                    _controller.EditSelection(r.Key, r.Action, r.Id);
                    break;
            }
            _store.Flush();
            Invalidate(body: true, rebuild: true);
        }

        /* ---------- 설정 ---------- */

        private void ShowSettings()
        {
            if (_settingsForm != null)
            {
                _settingsForm.Activate();
                return;
            }
            var before = _settings.Clone();
            _settingsForm = new SettingsForm(_settings, AutoStart.Enabled, _icon, preview =>
            {
                // 미리보기는 화면 설정만 바꾼다 (API 관련은 저장할 때)
                var visual = preview.Clone();
                visual.ApiKey = before.ApiKey;
                visual.Characters = before.Characters;
                visual.MinLevel = before.MinLevel;
                visual.MaxChars = before.MaxChars;
                visual.ApiBase = before.ApiBase;
                visual.Demo = before.Demo;
                visual.ShowAvatar = before.ShowAvatar;
                visual.RefreshMin = before.RefreshMin;
                ApplySettings(visual, persist: false);
            });
            _settingsForm.FormClosed += (s, e) =>
            {
                var form = _settingsForm;
                _settingsForm = null;
                if (form.DialogResult != DialogResult.OK || form.Result == null) return;
                try
                {
                    AutoStart.Enabled = form.AutoStartChecked;
                }
                catch (Exception)
                {
                    // 레지스트리를 못 쓰는 환경이면 자동 실행만 건너뛴다
                }
                ApplySettings(form.Result, persist: true);
            };
            _settingsForm.Show();
            _settingsForm.Activate();
        }

        private void ApplySettings(HudSettings next, bool persist)
        {
            _settings = HudSettings.Sanitize(next.Clone());
            _window.SetTopMost(_settings.AlwaysOnTop);
            _engine.UpdateSettings(_settings);
            if (persist)
            {
                try { _settings.Save(SettingsPath); }
                catch (Exception e) { Log(e); }
            }
            _tray.ContextMenuStrip = BuildMenu();
            Invalidate(body: true, rebuild: true);
        }

        /* ---------- 트레이 ---------- */

        private ContextMenuStrip BuildMenu()
        {
            var menu = new ContextMenuStrip();
            menu.Items.Add(new ToolStripMenuItem("Maple Scheduler HUD") { Enabled = false });
            menu.Items.Add(new ToolStripSeparator());
            menu.Items.Add("지금 새로고침", null, (s, e) => _engine.ManualRefresh());
            menu.Items.Add("설정…", null, (s, e) => ShowSettings());
            menu.Items.Add("맨 앞으로 가져오기", null, (s, e) => _window.BringToFront2());
            menu.Items.Add(new ToolStripSeparator());

            var monitors = new ToolStripMenuItem("표시할 모니터");
            menu.Opening += (s, e) =>
            {
                monitors.DropDownItems.Clear();
                var screens = Screen.AllScreens;
                var current = OverlayWindow.TargetScreen(_settings);
                for (int i = 0; i < screens.Length; i++)
                {
                    var sc = screens[i];
                    var item = new ToolStripMenuItem("모니터 " + (i + 1) + " (" + sc.Bounds.Width + "×" + sc.Bounds.Height + ")" + (sc.Primary ? " · 주 모니터" : ""))
                    {
                        Checked = sc.DeviceName == current.DeviceName
                    };
                    item.Click += (s2, e2) =>
                    {
                        var next = _settings.Clone();
                        next.Monitor = sc.DeviceName;
                        ApplySettings(next, persist: true);
                    };
                    monitors.DropDownItems.Add(item);
                }
            };
            menu.Items.Add(monitors);

            var topMost = new ToolStripMenuItem("항상 다른 창 위에 표시") { Checked = _settings.AlwaysOnTop };
            topMost.Click += (s, e) =>
            {
                var next = _settings.Clone();
                next.AlwaysOnTop = !_settings.AlwaysOnTop;
                ApplySettings(next, persist: true);
            };
            menu.Items.Add(topMost);

            var auto = new ToolStripMenuItem("Windows 시작 시 자동 실행");
            menu.Opening += (s, e) => auto.Checked = SafeAutoStart();
            auto.Click += (s, e) =>
            {
                try { AutoStart.Enabled = !auto.Checked; }
                catch (Exception ex) { Log(ex); }
            };
            menu.Items.Add(auto);
            menu.Items.Add(new ToolStripSeparator());
            menu.Items.Add("종료", null, (s, e) => ExitThread());
            return menu;
        }

        private static bool SafeAutoStart()
        {
            try { return AutoStart.Enabled; }
            catch (Exception) { return false; }
        }

        public void BringToFront() => _window.BringToFront2();

        /// <summary>자체 점검용: 지금 화면을 PNG로 저장하고 요약을 돌려준다</summary>
        public string SelfTestReport(string pngPath)
        {
            var png = _window.Snapshot();
            if (png == null) throw new InvalidOperationException("nothing rendered");
            File.WriteAllBytes(pngPath, png);
            var layout = _window.CurrentLayout;
            return "panel=" + layout.Width + "x" + layout.Height + " regions=" + layout.Regions.Count +
                " edit=" + layout.Regions.Count(r => r.Action == "edit") + " cards=" + _view.Cards.Count +
                " demo=" + _view.Demo + " font=" + _fonts.Regular.FamilyName;
        }

        /* ---------- 마무리 ---------- */

        private static Icon LoadIcon()
        {
            using (var s = Assembly.GetExecutingAssembly().GetManifestResourceStream("app.ico"))
            {
                return s != null ? new Icon(s, SystemInformation.SmallIconSize) : SystemIcons.Application;
            }
        }

        public static void Log(Exception e)
        {
            try
            {
                Directory.CreateDirectory(CacheDir);
                File.AppendAllText(Path.Combine(CacheDir, "error.log"), DateTime.Now.ToString("s") + " " + e + Environment.NewLine);
            }
            catch (IOException)
            {
                // 로그도 못 쓰면 버린다
            }
        }

        protected override void ExitThreadCore()
        {
            _timer.Stop();
            _tray.Visible = false;
            _store.Flush();
            _tray.Dispose();
            _window.Close();
            _window.Dispose();
            _renderer.Dispose();
            _fonts.Dispose();
            _avatars.Dispose();
            _http.Dispose();
            base.ExitThreadCore();
        }
    }
}
