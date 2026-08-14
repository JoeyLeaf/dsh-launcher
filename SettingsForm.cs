using System;
using System.Collections.Generic;
using System.Drawing;
using System.Drawing.Drawing2D;
using System.Drawing.Text;
using System.Globalization;
using System.IO;
using System.Runtime.InteropServices;
using System.Windows.Forms;

namespace DshLauncher
{
    /// <summary>应用设置（可扩展：以后加项只需在此加字段 + 对话框加一行）。</summary>
    public class DshSettings
    {
        public int Port = 3080;
        public bool AutoOpenBrowser = false;
        public bool MinimizeToTray = true;
        public bool AutoCheckOnStart = true;
        public string Language = Lang.Zh;
    }

    /// <summary>配置读写：%APPDATA%\DshLauncher\config.json（避免 OneDrive 同步干扰）。</summary>
    public static class ConfigStore
    {
        private static string Dir
        {
            get { return Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData), "DshLauncher"); }
        }

        private static string FilePath
        {
            get { return Path.Combine(Dir, "config.json"); }
        }

        public static DshSettings Load()
        {
            DshSettings s = new DshSettings();
            try
            {
                if (!File.Exists(FilePath)) return s;
                string text = File.ReadAllText(FilePath);
                Dictionary<string, object> map = JsonMini.ParseObject(text);
                object v;
                if (map.TryGetValue("port", out v) && v is double)
                {
                    int p = (int)(double)v;
                    if (p >= 1 && p <= 65535) s.Port = p;
                }
                if (map.TryGetValue("autoOpenBrowser", out v) && v is bool) s.AutoOpenBrowser = (bool)v;
                if (map.TryGetValue("minimizeToTray", out v) && v is bool) s.MinimizeToTray = (bool)v;
                if (map.TryGetValue("autoCheckOnStart", out v) && v is bool) s.AutoCheckOnStart = (bool)v;
                if (map.TryGetValue("language", out v) && v is string)
                {
                    string l = (string)v;
                    if (l == Lang.En || l == Lang.Zh) s.Language = l;
                }
            }
            catch { }
            return s;
        }

        public static void Save(DshSettings s)
        {
            try
            {
                Directory.CreateDirectory(Dir);
                string json = "{\n  \"port\": " + s.Port.ToString(CultureInfo.InvariantCulture)
                    + ",\n  \"autoOpenBrowser\": " + Bool(s.AutoOpenBrowser)
                    + ",\n  \"minimizeToTray\": " + Bool(s.MinimizeToTray)
                    + ",\n  \"autoCheckOnStart\": " + Bool(s.AutoCheckOnStart)
                    + ",\n  \"language\": \"" + s.Language + "\"\n}";
                File.WriteAllText(FilePath, json);
            }
            catch { }
        }

        private static string Bool(bool b) { return b ? "true" : "false"; }
    }

    /// <summary>极简 JSON 解析（仅顶层扁平对象，够配置用）。</summary>
    internal static class JsonMini
    {
        public static Dictionary<string, object> ParseObject(string text)
        {
            Dictionary<string, object> dict = new Dictionary<string, object>();
            if (string.IsNullOrEmpty(text)) return dict;
            int i = 0;
            SkipWs(text, ref i);
            if (i >= text.Length || text[i] != '{') return dict;
            i++;
            while (true)
            {
                SkipWs(text, ref i);
                if (i >= text.Length) break;
                if (text[i] == '}') break;
                if (text[i] == ',') { i++; continue; }
                if (text[i] != '"') { i++; continue; }
                int start = ++i;
                while (i < text.Length && text[i] != '"') i++;
                string key = text.Substring(start, i - start);
                i++;
                SkipWs(text, ref i);
                if (i < text.Length && text[i] == ':') i++;
                object val = ParseValue(text, ref i);
                if (!dict.ContainsKey(key)) dict[key] = val;
            }
            return dict;
        }

        private static object ParseValue(string text, ref int i)
        {
            SkipWs(text, ref i);
            if (i >= text.Length) return null;
            char c = text[i];
            if (c == '"')
            {
                int start = ++i;
                while (i < text.Length && text[i] != '"') i++;
                string v = text.Substring(start, i - start);
                i++;
                return v;
            }
            if (c == 't') { i += 4; return true; }
            if (c == 'f') { i += 5; return false; }
            if (c == 'n') { i += 4; return null; }
            int ns = i;
            while (i < text.Length && (char.IsDigit(text[i]) || text[i] == '-' || text[i] == '.')) i++;
            double d;
            if (double.TryParse(text.Substring(ns, i - ns), NumberStyles.Float, CultureInfo.InvariantCulture, out d)) return d;
            return null;
        }

        private static void SkipWs(string text, ref int i)
        {
            while (i < text.Length && char.IsWhiteSpace(text[i])) i++;
        }
    }

    /// <summary>
    /// 设置对话框：与主窗口同一套自绘风格（深色、圆角、灰度抗锯齿文字）。
    /// 除端口输入框外全部自绘（标题、复选框、按钮、提示）。
    /// </summary>
    public class SettingsForm : Form
    {
        private const int WinW = 400, WinH = 400;
        private readonly float _s;

        private int P(float v) { return (int)(v * _s + 0.5f); }

        private class CheckItem
        {
            public Rectangle Rect;
            public string Text = "";
            public bool Checked;
        }

        private class BtnItem
        {
            public Rectangle Rect;
            public string Text = "";
            public ButtonVariant Variant;
            public bool Hover;
            public bool Down;
            public Action OnClick;
        }

        private class ChipItem
        {
            public Rectangle Rect;
            public string Text = "";
            public string Value = "";
        }

        private readonly List<CheckItem> _checks = new List<CheckItem>();
        private readonly List<BtnItem> _btns = new List<BtnItem>();
        private readonly List<ChipItem> _langChips = new List<ChipItem>();

        private TextBox _portBox;
        private readonly DshSettings _working;
        private Font _fTitle, _fLabel, _fSmall, _fBtn;
        private readonly StringFormat _centerFmt = new StringFormat();
        private string _lang;

        public DshSettings Result { get; private set; }

        public SettingsForm(DshSettings current)
        {
            _working = new DshSettings();
            _working.Port = current.Port;
            _working.AutoOpenBrowser = current.AutoOpenBrowser;
            _working.MinimizeToTray = current.MinimizeToTray;
            _working.AutoCheckOnStart = current.AutoCheckOnStart;
            _working.Language = current.Language;
            _lang = current.Language;

            using (Graphics g = CreateGraphics()) _s = Math.Max(1f, g.DpiX / 96f);

            FormBorderStyle = FormBorderStyle.None;
            StartPosition = FormStartPosition.CenterParent;
            BackColor = Theme.Bg;
            AutoScaleMode = AutoScaleMode.None; // 全自绘，手动缩放
            ClientSize = new Size(P(WinW), P(WinH));
            DoubleBuffered = true;
            ShowInTaskbar = false;
            Text = Lang.T("settings_title");

            _fTitle = Theme.Font(14f * _s, FontStyle.Bold);
            _fLabel = Theme.Font(10f * _s, FontStyle.Regular);
            _fSmall = Theme.Font(8.5f * _s, FontStyle.Regular);
            _fBtn = Theme.Font(10.5f * _s, FontStyle.Regular);
            _centerFmt.Alignment = StringAlignment.Center;
            _centerFmt.LineAlignment = StringAlignment.Center;

            // 端口输入（唯一真实控件，其余全自绘）
            _portBox = new TextBox();
            _portBox.BackColor = Theme.BgAlt;
            _portBox.ForeColor = Theme.Text;
            _portBox.BorderStyle = BorderStyle.FixedSingle;
            _portBox.Font = Theme.Font(11f * _s, FontStyle.Regular);
            _portBox.MaxLength = 5;
            _portBox.Text = _working.Port.ToString(CultureInfo.InvariantCulture);
            _portBox.KeyPress += delegate(object s, KeyPressEventArgs e)
            {
                if (!char.IsDigit(e.KeyChar) && !char.IsControl(e.KeyChar)) e.Handled = true;
            };
            _portBox.SetBounds(P(100), P(60), P(120), P(30));
            Controls.Add(_portBox);

            // 复选框
            CheckItem c1 = new CheckItem();
            c1.Text = Lang.T("settings_auto_open");
            c1.Checked = _working.AutoOpenBrowser;
            c1.Rect = new Rectangle(P(20), P(138), P(360), P(30));
            _checks.Add(c1);

            CheckItem c2 = new CheckItem();
            c2.Text = Lang.T("settings_min_tray");
            c2.Checked = _working.MinimizeToTray;
            c2.Rect = new Rectangle(P(20), P(178), P(360), P(30));
            _checks.Add(c2);

            CheckItem c3 = new CheckItem();
            c3.Text = Lang.T("settings_auto_check");
            c3.Checked = _working.AutoCheckOnStart;
            c3.Rect = new Rectangle(P(20), P(218), P(360), P(30));
            _checks.Add(c3);

            // 语言选择
            ChipItem zh = new ChipItem();
            zh.Text = Lang.T("settings_lang_zh");
            zh.Value = Lang.Zh;
            zh.Rect = new Rectangle(P(96), P(252), P(72), P(28));
            _langChips.Add(zh);

            ChipItem en = new ChipItem();
            en.Text = Lang.T("settings_lang_en");
            en.Value = Lang.En;
            en.Rect = new Rectangle(P(176), P(252), P(84), P(28));
            _langChips.Add(en);

            // 按钮
            BtnItem closeX = new BtnItem();
            closeX.Text = "✕";
            closeX.Variant = ButtonVariant.Ghost;
            closeX.Rect = new Rectangle(P(354), P(10), P(34), P(30));
            closeX.OnClick = delegate { DialogResult = DialogResult.Cancel; Close(); };
            _btns.Add(closeX);

            BtnItem cancel = new BtnItem();
            cancel.Text = Lang.T("settings_cancel");
            cancel.Variant = ButtonVariant.Ghost;
            cancel.Rect = new Rectangle(P(190), P(346), P(80), P(38));
            cancel.OnClick = delegate { DialogResult = DialogResult.Cancel; Close(); };
            _btns.Add(cancel);

            BtnItem save = new BtnItem();
            save.Text = Lang.T("settings_save");
            save.Variant = ButtonVariant.Primary;
            save.Rect = new Rectangle(P(286), P(346), P(80), P(38));
            save.OnClick = delegate { SaveAndClose(); };
            _btns.Add(save);
        }

        // ---------- 绘制 ----------

        protected override void OnPaint(PaintEventArgs e)
        {
            Graphics g = e.Graphics;
            g.SmoothingMode = SmoothingMode.AntiAlias;
            g.TextRenderingHint = TextRenderingHint.AntiAlias;
            g.Clear(Theme.Bg);

            using (SolidBrush t = new SolidBrush(Theme.Text))
            {
                g.DrawString(Lang.T("settings_title"), _fTitle, t, P(20), P(16));
                g.DrawString(Lang.T("settings_port"), _fLabel, t, P(20), P(64));
            }
            using (SolidBrush m = new SolidBrush(Theme.TextMuted))
            {
                g.DrawString(Lang.T("settings_port_hint"), _fSmall, m, P(20), P(98));
                g.DrawString(Lang.T("settings_lang"), _fLabel, m, P(20), P(256));
                g.DrawString(Lang.T("settings_tip"), _fSmall, m, P(20), P(296));
            }

            foreach (CheckItem c in _checks) DrawCheck(g, c);
            DrawLangChips(g);
            foreach (BtnItem b in _btns) Theme.PaintButton(g, b.Rect, b.Variant, b.Text, _fBtn, true, b.Hover, b.Down);
        }

        private void DrawLangChips(Graphics g)
        {
            foreach (ChipItem c in _langChips)
            {
                bool sel = c.Value == _lang;
                Rectangle r = c.Rect;
                using (GraphicsPath gp = Theme.RoundedRect(r, 14))
                using (SolidBrush f = new SolidBrush(sel ? Theme.Accent : Theme.BgAlt))
                {
                    g.FillPath(f, gp);
                }
                using (GraphicsPath gp = Theme.RoundedRect(r, 14))
                using (Pen pn = new Pen(sel ? Theme.Accent : Theme.CardBorder))
                {
                    g.DrawPath(pn, gp);
                }
                using (SolidBrush t = new SolidBrush(sel ? Color.White : Theme.Text))
                {
                    g.DrawString(c.Text, _fLabel, t, r, _centerFmt);
                }
            }
        }

        private void DrawCheck(Graphics g, CheckItem c)
        {
            Rectangle box = new Rectangle(c.Rect.X, c.Rect.Y + P(6), P(16), P(16));
            using (GraphicsPath gp = Theme.RoundedRect(box, 4))
            using (SolidBrush f = new SolidBrush(Theme.BgAlt))
            {
                g.FillPath(f, gp);
            }
            using (GraphicsPath gp = Theme.RoundedRect(box, 4))
            using (Pen pn = new Pen(Theme.CardBorder))
            {
                g.DrawPath(pn, gp);
            }
            if (c.Checked)
            {
                using (Pen p = new Pen(Theme.Accent, P(1.8f)))
                {
                    p.StartCap = LineCap.Round;
                    p.EndCap = LineCap.Round;
                    g.DrawLines(p, new Point[] {
                        new Point(box.X + P(3), box.Y + P(8)),
                        new Point(box.X + P(7), box.Y + P(12)),
                        new Point(box.X + P(13), box.Y + P(4)) });
                }
            }
            using (SolidBrush t = new SolidBrush(Theme.Text))
            {
                g.DrawString(c.Text, _fLabel, t, c.Rect.X + P(26), c.Rect.Y + P(5));
            }
        }

        // ---------- 鼠标交互 ----------

        protected override void OnMouseMove(MouseEventArgs e)
        {
            base.OnMouseMove(e);
            bool changed = false;
            bool hand = false;
            foreach (BtnItem b in _btns)
            {
                bool h = b.Rect.Contains(e.Location);
                if (b.Hover != h) { b.Hover = h; changed = true; }
                if (h) hand = true;
            }
            foreach (CheckItem c in _checks)
            {
                if (c.Rect.Contains(e.Location)) hand = true;
            }
            foreach (ChipItem c in _langChips)
            {
                if (c.Rect.Contains(e.Location)) hand = true;
            }
            if (changed) Invalidate();
            Cursor = hand ? Cursors.Hand : Cursors.Default;
        }

        protected override void OnMouseDown(MouseEventArgs e)
        {
            base.OnMouseDown(e);
            if (e.Button != MouseButtons.Left) return;

            foreach (ChipItem c in _langChips)
            {
                if (c.Rect.Contains(e.Location))
                {
                    _lang = c.Value;
                    Invalidate();
                    return;
                }
            }
            foreach (CheckItem c in _checks)
            {
                if (c.Rect.Contains(e.Location))
                {
                    c.Checked = !c.Checked;
                    Invalidate();
                    return;
                }
            }
            foreach (BtnItem b in _btns)
            {
                if (b.Rect.Contains(e.Location))
                {
                    b.Down = true;
                    Invalidate();
                    return;
                }
            }
            if (e.Y < P(50)) NativeDrag();
        }

        protected override void OnMouseUp(MouseEventArgs e)
        {
            base.OnMouseUp(e);
            if (e.Button != MouseButtons.Left) return;
            BtnItem hit = null;
            foreach (BtnItem b in _btns)
            {
                if (b.Down)
                {
                    b.Down = false;
                    if (b.Rect.Contains(e.Location)) hit = b;
                }
            }
            if (hit != null && hit.OnClick != null) hit.OnClick();
            Invalidate();
        }

        protected override void OnMouseLeave(EventArgs e)
        {
            base.OnMouseLeave(e);
            bool changed = false;
            foreach (BtnItem b in _btns)
            {
                if (b.Hover) { b.Hover = false; changed = true; }
                if (b.Down) { b.Down = false; changed = true; }
            }
            if (changed) Invalidate();
            Cursor = Cursors.Default;
        }

        // ---------- 保存 ----------

        private void SaveAndClose()
        {
            int port;
            if (!int.TryParse(_portBox.Text, NumberStyles.None, CultureInfo.InvariantCulture, out port) || port < 1 || port > 65535)
            {
                port = 3080;
            }
            _working.Port = port;
            _working.AutoOpenBrowser = _checks[0].Checked;
            _working.MinimizeToTray = _checks[1].Checked;
            _working.AutoCheckOnStart = _checks[2].Checked;
            _working.Language = _lang;
            Result = _working;
            DialogResult = DialogResult.OK;
            Close();
        }

        // ---------- 窗口行为 ----------

        protected override void OnShown(EventArgs e)
        {
            base.OnShown(e);
            try { _portBox.Focus(); _portBox.SelectAll(); } catch { }
        }

        protected override void OnLoad(EventArgs e)
        {
            base.OnLoad(e);
            Theme.ApplyRegion(this, 12);
        }

        protected override CreateParams CreateParams
        {
            get
            {
                CreateParams cp = base.CreateParams;
                cp.ClassStyle |= 0x20000; // CS_DROPSHADOW
                return cp;
            }
        }

        private void NativeDrag()
        {
            ReleaseCapture();
            SendMessage(this.Handle, 0xA1, (IntPtr)2, IntPtr.Zero);
        }

        [DllImport("user32.dll")]
        private static extern bool ReleaseCapture();

        [DllImport("user32.dll")]
        private static extern IntPtr SendMessage(IntPtr hWnd, int Msg, IntPtr wParam, IntPtr lParam);
    }
}
