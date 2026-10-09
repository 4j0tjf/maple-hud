using System;
using System.Drawing;
using System.Linq;
using System.Windows.Forms;
using MapleHud.Core;

namespace MapleHud
{
    /// <summary>
    /// 설정 창. 배치·패널·표시 설정은 바꾸는 즉시 HUD에 미리 보여주고,
    /// API 키처럼 다시 동기화가 필요한 설정은 저장할 때 적용한다.
    /// </summary>
    internal sealed class SettingsForm : Form
    {
        private readonly HudSettings _original;
        private readonly Action<HudSettings> _preview;
        private readonly bool _autoStartBefore;
        private bool _loading = true;
        private string _baseline;   // 처음 연 상태 (바뀐 내용이 있는지 비교용)
        private bool _decided;      // 저장/취소 버튼으로 닫음

        private readonly TextBox _apiKey = new TextBox { UseSystemPasswordChar = true };
        private readonly CheckBox _showKey = new CheckBox { Text = "보기", AutoSize = true };
        private readonly TextBox _characters = new TextBox();
        private readonly NumericUpDown _minLevel = Num(0, 300, 1);
        private readonly NumericUpDown _maxChars = Num(1, 20, 1);
        private readonly NumericUpDown _refresh = Num(5, 180, 1);
        private readonly TextBox _apiBase = new TextBox();
        private readonly CheckBox _demo = Check("데모 데이터로 보기");

        private readonly CheckBox _showAll = Check("스케줄러에 등록 안 된 항목도 표시");
        private readonly CheckBox _hideDone = Check("완료한 항목 숨기기");
        private readonly CheckBox _collapseDone = Check("모두 완료한 캐릭터 접기");
        private readonly CheckBox _showAvatar = Check("캐릭터 이미지·경험치 표시");
        private readonly CheckBox _showSeconds = Check("시계·카운트다운 초 단위 표시 (끄면 1분에 한 번만 다시 그림)");

        private readonly ComboBox _monitor = Combo();
        private readonly ComboBox _alignX = Combo("왼쪽", "가운데", "오른쪽");
        private readonly ComboBox _alignY = Combo("위", "가운데", "아래");
        private readonly NumericUpDown _offsetX = Num(0, HudSettings.MaxOffset, 2);
        private readonly NumericUpDown _offsetY = Num(0, HudSettings.MaxOffset, 2);
        private readonly NumericUpDown _columns = Num(1, 4, 1);
        private readonly NumericUpDown _cardWidth = Num(280, 720, 10);
        private readonly NumericUpDown _scale = Num(50, 250, 5);

        private readonly TrackBar _opacity = new TrackBar { Minimum = 0, Maximum = 100, TickFrequency = 10, AutoSize = false, Height = 28 };
        private readonly Label _opacityValue = new Label { AutoSize = true, Anchor = AnchorStyles.Left };
        private readonly Button _accent = new Button { Width = 90, FlatStyle = FlatStyle.Flat };
        private readonly CheckBox _topMost = Check("항상 다른 창 위에 표시");
        private readonly CheckBox _autoStart = Check("Windows 시작 시 자동 실행");

        private static readonly string[] AlignXValues = { "left", "center", "right" };
        private static readonly string[] AlignYValues = { "top", "center", "bottom" };
        private Screen[] _screens;

        public HudSettings Result { get; private set; }
        internal Button SaveButton { get; private set; }
        public bool AutoStartChecked => _autoStart.Checked;

        public SettingsForm(HudSettings current, bool autoStart, Icon icon, Action<HudSettings> preview)
        {
            _original = current.Clone();
            _preview = preview;
            _autoStartBefore = autoStart;
            Text = "Maple Scheduler HUD 설정";
            Icon = icon;
            Font = new Font("맑은 고딕", 9f);
            StartPosition = FormStartPosition.CenterScreen;
            AutoScaleMode = AutoScaleMode.Dpi;
            MinimumSize = new Size(480, 420);
            Size = new Size(520, 760);
            ShowInTaskbar = true;
            TopMost = true;

            var table = new TableLayoutPanel
            {
                ColumnCount = 2,
                Dock = DockStyle.Top,
                AutoSize = true,
                Padding = new Padding(16, 8, 16, 8)
            };
            table.ColumnStyles.Add(new ColumnStyle(SizeType.Absolute, 170));
            table.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100));

            var keyRow = new FlowLayoutPanel { AutoSize = true, WrapContents = false, Margin = new Padding(0), Dock = DockStyle.Fill };
            _apiKey.Width = 220;
            keyRow.Controls.Add(_apiKey);
            keyRow.Controls.Add(_showKey);
            _showKey.CheckedChanged += (s, e) => _apiKey.UseSystemPasswordChar = !_showKey.Checked;

            Section(table, "넥슨 Open API");
            Row(table, "API 키", keyRow);
            Row(table, "캐릭터 이름", _characters, "쉼표로 구분. 비우면 계정에서 스케줄러에 등록된 캐릭터를 자동으로 찾습니다.");
            Row(table, "자동 선택 최소 레벨", _minLevel);
            Row(table, "자동 선택 최대 캐릭터 수", _maxChars);
            Row(table, "갱신 주기 (분)", _refresh);
            Row(table, "프록시 주소", _apiBase, "보통 비워두세요.");
            Row(table, "", _demo);

            Section(table, "표시");
            foreach (var c in new[] { _showAll, _hideDone, _collapseDone, _showAvatar, _showSeconds }) Row(table, "", c);

            Section(table, "배치");
            Row(table, "모니터", _monitor);
            Row(table, "가로 위치", _alignX);
            Row(table, "세로 위치", _alignY);
            Row(table, "가로 여백 (px)", _offsetX);
            Row(table, "세로 여백 (px)", _offsetY, "HUD를 마우스로 끌어서 옮겨도 됩니다. 놓은 자리에서 가까운 모서리를 기준으로 맞춰집니다.");
            Row(table, "열 개수", _columns);
            Row(table, "카드 너비 (px)", _cardWidth);
            Row(table, "크기 (%)", _scale);

            Section(table, "패널");
            var opacityRow = new FlowLayoutPanel { AutoSize = true, WrapContents = false, Margin = new Padding(0), Dock = DockStyle.Fill };
            _opacity.Width = 220;
            opacityRow.Controls.Add(_opacity);
            opacityRow.Controls.Add(_opacityValue);
            Row(table, "배경 불투명도", opacityRow);
            Row(table, "강조 색상", _accent);
            Row(table, "", _topMost);

            Section(table, "시작");
            Row(table, "", _autoStart);

            var scroll = new Panel { Dock = DockStyle.Fill, AutoScroll = true };
            scroll.Controls.Add(table);
            // 휠로 창을 내리다가 마우스 아래 칸(모니터·위치·크기 등)의 값이 바뀌어 HUD가 엉뚱한 데로 가지 않게,
            // 입력 중이 아닌 칸에서는 휠을 설정 창 스크롤로 넘긴다
            foreach (var c in new Control[] { _minLevel, _maxChars, _refresh, _monitor, _alignX, _alignY, _offsetX, _offsetY, _columns, _cardWidth, _scale, _opacity })
            {
                c.MouseWheel += (s, e) =>
                {
                    if (((Control)s).ContainsFocus) return;
                    if (e is HandledMouseEventArgs h) h.Handled = true;
                    scroll.AutoScrollPosition = new Point(0, Math.Max(0, -scroll.AutoScrollPosition.Y - e.Delta / 2));
                };
            }

            var buttons = new FlowLayoutPanel
            {
                Dock = DockStyle.Bottom,
                FlowDirection = FlowDirection.RightToLeft,
                AutoSize = true,
                Padding = new Padding(12, 8, 12, 12)
            };
            var save = new Button { Text = "저장", Width = 90, Height = 30 };
            var cancel = new Button { Text = "취소", Width = 90, Height = 30 };
            // 모달이 아닌 창은 DialogResult만으로는 닫히지 않아서 직접 닫는다
            save.Click += (s, e) => Finish(DialogResult.OK);
            cancel.Click += (s, e) => Finish(DialogResult.Cancel);
            SaveButton = save;
            buttons.Controls.Add(save);
            buttons.Controls.Add(cancel);
            AcceptButton = save;
            CancelButton = cancel;

            Controls.Add(scroll);
            Controls.Add(buttons);

            Fill(current, autoStart);
            Wire();
            _accent.Click += (s, e) => PickAccent();
            FormClosing += OnClosing;
            _baseline = Read().ToJson();
            _loading = false;
        }

        /* ---------- 화면 구성 ---------- */

        private static NumericUpDown Num(int min, int max, int step) =>
            new NumericUpDown { Minimum = min, Maximum = max, Increment = step, Width = 90 };

        private static CheckBox Check(string text) => new CheckBox { Text = text, AutoSize = true };

        private static ComboBox Combo(params string[] items)
        {
            var c = new ComboBox { DropDownStyle = ComboBoxStyle.DropDownList, Width = 220 };
            c.Items.AddRange(items.Cast<object>().ToArray());
            return c;
        }

        private static void Section(TableLayoutPanel t, string title)
        {
            var label = new Label
            {
                Text = title,
                AutoSize = true,
                Font = new Font("맑은 고딕", 10f, FontStyle.Bold),
                Margin = new Padding(0, t.RowCount == 0 ? 4 : 16, 0, 6)
            };
            t.Controls.Add(label, 0, t.RowCount);
            t.SetColumnSpan(label, 2);
            t.RowCount++;
        }

        private static void Row(TableLayoutPanel t, string label, Control control, string hint = null)
        {
            if (label.Length > 0)
            {
                t.Controls.Add(new Label { Text = label, AutoSize = true, Anchor = AnchorStyles.Left, Margin = new Padding(0, 6, 8, 4) }, 0, t.RowCount);
            }
            control.Margin = new Padding(0, 3, 0, 3);
            if (control is TextBox tb && tb.Width < 240) tb.Width = 280;
            t.Controls.Add(control, 1, t.RowCount);
            t.RowCount++;
            if (hint == null) return;
            t.Controls.Add(new Label { Text = hint, AutoSize = true, ForeColor = SystemColors.GrayText, MaximumSize = new Size(300, 0), Margin = new Padding(0, 0, 0, 4) }, 1, t.RowCount);
            t.RowCount++;
        }

        private void Fill(HudSettings s, bool autoStart)
        {
            _apiKey.Text = s.ApiKey;
            _characters.Text = s.Characters;
            _minLevel.Value = s.MinLevel;
            _maxChars.Value = s.MaxChars;
            _refresh.Value = s.RefreshMin;
            _apiBase.Text = s.ApiBase;
            _demo.Checked = s.Demo;
            _showAll.Checked = s.ShowAll;
            _hideDone.Checked = s.HideDone;
            _collapseDone.Checked = s.CollapseDone;
            _showAvatar.Checked = s.ShowAvatar;
            _showSeconds.Checked = s.ShowSeconds;

            _screens = Screen.AllScreens;
            _monitor.Items.Clear();
            for (int i = 0; i < _screens.Length; i++)
            {
                var sc = _screens[i];
                _monitor.Items.Add("모니터 " + (i + 1) + " (" + sc.Bounds.Width + "×" + sc.Bounds.Height + ")" + (sc.Primary ? " · 주 모니터" : ""));
            }
            var target = OverlayWindow.TargetScreen(s);
            _monitor.SelectedIndex = Math.Max(0, Array.FindIndex(_screens, sc => sc.DeviceName == target.DeviceName));
            _alignX.SelectedIndex = Math.Max(0, Array.IndexOf(AlignXValues, s.AlignX));
            _alignY.SelectedIndex = Math.Max(0, Array.IndexOf(AlignYValues, s.AlignY));
            _offsetX.Value = s.OffsetX;
            _offsetY.Value = s.OffsetY;
            _columns.Value = s.Columns;
            _cardWidth.Value = s.CardWidth;
            _scale.Value = s.Scale;
            _opacity.Value = s.Opacity;
            _opacityValue.Text = s.Opacity + "%";
            SetAccent(s.Accent);
            _topMost.Checked = s.AlwaysOnTop;
            _autoStart.Checked = autoStart;
        }

        private void Wire()
        {
            foreach (var c in new Control[] { _apiKey, _characters, _apiBase }) c.TextChanged += Changed;
            foreach (var n in new[] { _minLevel, _maxChars, _refresh, _offsetX, _offsetY, _columns, _cardWidth, _scale }) n.ValueChanged += Changed;
            foreach (var c in new[] { _demo, _showAll, _hideDone, _collapseDone, _showAvatar, _showSeconds, _topMost }) c.CheckedChanged += Changed;
            foreach (var c in new[] { _monitor, _alignX, _alignY }) c.SelectedIndexChanged += Changed;
            _opacity.ValueChanged += (s, e) =>
            {
                _opacityValue.Text = _opacity.Value + "%";
                Changed(s, e);
            };
        }

        private void Changed(object sender, EventArgs e)
        {
            if (_loading) return;
            _preview(Read());
        }

        private string _accentHex;

        private void SetAccent(string hex)
        {
            _accentHex = Colors.NormalizeHex(hex, "#ffb547");
            var c = ColorTranslator.FromHtml(_accentHex);
            _accent.BackColor = c;
            _accent.ForeColor = c.GetBrightness() > 0.6 ? Color.Black : Color.White;
            _accent.Text = _accentHex;
        }

        private void PickAccent()
        {
            using (var dlg = new ColorDialog { Color = ColorTranslator.FromHtml(_accentHex), FullOpen = true })
            {
                if (dlg.ShowDialog(this) != DialogResult.OK) return;
                SetAccent("#" + dlg.Color.R.ToString("x2") + dlg.Color.G.ToString("x2") + dlg.Color.B.ToString("x2"));
                Changed(this, EventArgs.Empty);
            }
        }

        private HudSettings Read()
        {
            var s = _original.Clone();
            s.ApiKey = _apiKey.Text.Trim();
            s.Characters = _characters.Text.Trim();
            s.MinLevel = (int)_minLevel.Value;
            s.MaxChars = (int)_maxChars.Value;
            s.RefreshMin = (int)_refresh.Value;
            s.ApiBase = _apiBase.Text.Trim();
            s.Demo = _demo.Checked;
            s.ShowAll = _showAll.Checked;
            s.HideDone = _hideDone.Checked;
            s.CollapseDone = _collapseDone.Checked;
            s.ShowAvatar = _showAvatar.Checked;
            s.ShowSeconds = _showSeconds.Checked;
            s.Monitor = _monitor.SelectedIndex >= 0 && _monitor.SelectedIndex < _screens.Length ? _screens[_monitor.SelectedIndex].DeviceName : "";
            s.AlignX = AlignXValues[Math.Max(0, _alignX.SelectedIndex)];
            s.AlignY = AlignYValues[Math.Max(0, _alignY.SelectedIndex)];
            s.OffsetX = (int)_offsetX.Value;
            s.OffsetY = (int)_offsetY.Value;
            s.Columns = (int)_columns.Value;
            s.CardWidth = (int)_cardWidth.Value;
            s.Scale = (int)_scale.Value;
            s.Opacity = _opacity.Value;
            s.Accent = _accentHex;
            s.AlwaysOnTop = _topMost.Checked;
            return HudSettings.Sanitize(s);
        }

        private void Finish(DialogResult result)
        {
            _decided = true;
            DialogResult = result;
            Close();
        }

        private bool Changed() => Read().ToJson() != _baseline || _autoStart.Checked != _autoStartBefore;

        private void OnClosing(object sender, FormClosingEventArgs e)
        {
            if (!_decided && e.CloseReason == CloseReason.UserClosing && Changed())
            {
                // 창의 X로 닫을 때 바꾼 내용이 있으면 저장할지 묻는다
                var answer = MessageBox.Show(this, "바꾼 설정을 저장할까요?", Text, MessageBoxButtons.YesNoCancel, MessageBoxIcon.Question);
                if (answer == DialogResult.Cancel)
                {
                    e.Cancel = true;
                    return;
                }
                DialogResult = answer == DialogResult.Yes ? DialogResult.OK : DialogResult.Cancel;
            }
            if (DialogResult == DialogResult.OK) Result = Read();
            else _preview(_original);   // 취소하면 미리보기를 되돌린다
        }

        /// <summary>
        /// HUD를 끌어서 옮겼을 때 위치 칸을 맞춘다. 옮긴 자리는 이미 저장됐으므로 취소해도 되돌리지 않는다.
        /// </summary>
        internal void SetPosition(HudSettings s)
        {
            bool dirty = Changed();
            _original.Monitor = s.Monitor;
            _original.AlignX = s.AlignX;
            _original.AlignY = s.AlignY;
            _original.OffsetX = s.OffsetX;
            _original.OffsetY = s.OffsetY;
            _loading = true;
            try
            {
                int monitor = Array.FindIndex(_screens, sc => sc.DeviceName == s.Monitor);
                if (monitor >= 0) _monitor.SelectedIndex = monitor;
                _alignX.SelectedIndex = Math.Max(0, Array.IndexOf(AlignXValues, s.AlignX));
                _alignY.SelectedIndex = Math.Max(0, Array.IndexOf(AlignYValues, s.AlignY));
                _offsetX.Value = Math.Min(_offsetX.Maximum, s.OffsetX);
                _offsetY.Value = Math.Min(_offsetY.Maximum, s.OffsetY);
            }
            finally
            {
                _loading = false;
            }
            if (!dirty) _baseline = Read().ToJson();
        }

        /// <summary>자체 점검용: API 입력란을 채운다</summary>
        internal void FillApiForTest(string apiKey, string characters, string apiBase)
        {
            _apiKey.Text = apiKey;
            _characters.Text = characters;
            _apiBase.Text = apiBase;
        }
    }
}
