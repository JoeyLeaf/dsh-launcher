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
        public int UiScale = 100; // 界面缩放百分比：85 / 100 / 115
        public string TrustedHosts = ""; // dsh --trusted-host 信任域名列表（逗号分隔），公网/隧道域名访问需要
        public bool ProxyEnabled = true; // dsh 联网走代理（如 Clash），false 则直连
        public string ProxyUrl = "http://127.0.0.1:7890"; // 代理地址（HTTP(S)_PROXY）
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
                if (map.TryGetValue("uiScale", out v) && v is double)
                {
                    int sc = (int)(double)v;
                    if (sc == 85 || sc == 100 || sc == 115) s.UiScale = sc;
                }
                if (map.TryGetValue("trustedHosts", out v) && v is string)
                {
                    string t = (string)v;
                    if (t != null) s.TrustedHosts = t;
                }
                if (map.TryGetValue("proxyEnabled", out v) && v is bool) s.ProxyEnabled = (bool)v;
                // 键缺失时保留默认（老配置升级后仍走默认代理，行为与旧版一致）；
                // 显式存了空串则按直连处理，不回退默认。
                if (map.TryGetValue("proxyUrl", out v) && v is string) s.ProxyUrl = (string)v;
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
                    + ",\n  \"language\": \"" + s.Language + "\""
                    + ",\n  \"uiScale\": " + s.UiScale.ToString(CultureInfo.InvariantCulture)
                    + ",\n  \"trustedHosts\": \"" + JsonMini.Escape(s.TrustedHosts) + "\""
                    + ",\n  \"proxyEnabled\": " + Bool(s.ProxyEnabled)
                    + ",\n  \"proxyUrl\": \"" + JsonMini.Escape(s.ProxyUrl)
                    + "\"\n}";
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

        /// <summary>把字符串转义为 JSON 字符串字面量（仅需处理引号与反斜杠，够配置场景用）。</summary>
        public static string Escape(string s)
        {
            if (string.IsNullOrEmpty(s)) return "";
            string r = s.Replace("\\", "\\\\").Replace("\"", "\\\"");
            r = r.Replace("\r", "\\r").Replace("\n", "\\n").Replace("\t", "\\t");
            return r;
        }
    }

    /// <summary>
    /// 设置对话框：与主窗口同一套自绘风格（深色、圆角、灰度抗锯齿文字）。
    /// 本版改进：
    ///  - 动态窗高：「信任域名 / 代理」子区展开收起带动画，窗口高度随内容变化，不再固定高窗 + 大留白；
    ///  - 分组标题：常规 / 网络 / 语言与外观，分隔线语义清晰；
    ///  - 端口行：输入框 + 实时 URL 预览，非法输入红描边 + 红提示（不再静默改值无感知）；
    ///  - 行悬停背景 + 行首 Lucide 图标（勾选时图标变强调色）；
    ///  - 键盘：Esc 取消、Enter 保存；
    ///  - 缩放实时预览：切换小/中/大立即重建本窗；
    ///  - 标题鲸鱼徽标 + 版本号；1px 外边框与 14px 圆角与主窗口统一。
    /// </summary>
    public class SettingsForm : Form
    {
        private const int WinW = 480;
        private const int PadX = 24;
        private const int ContentW = WinW - PadX * 2; // 432
        private const int RowH = 26;      // 复选框行高（统一）
        private const int BoxH = 34;      // 输入容器高
        private const int ChipH = 26;
        private const int BtnW = 88, BtnH = 36;
        private const int SlotH = 80;     // 展开子区的设计高度：标签 16 + 输入框 34(+10) + 提示 14
        private const int ScaleCapDesignH = 900; // 最大布局设计高度（全部展开约 884），用于屏幕适配钳制

        private float _s = 1f;

        private int P(float v) { return (int)(v * _s + 0.5f); }

        private class CheckItem
        {
            public Rectangle Rect;
            public string Text = "";
            public string Icon = ""; // Lucide 路径；空则不画
            public bool Checked;
            public bool Hover;
        }

        private class BtnItem
        {
            public Rectangle Rect;
            public string Text = "";
            public ButtonVariant Variant;
            public bool Hover;
            public bool Down;
            public bool IconX; // true = 绘制 Lucide X 图标而非文字
            public Action OnClick;
        }

        private class ChipItem
        {
            public Rectangle Rect;
            public string Text = "";
            public string Value = "";
            public bool Hover;
        }

        /// <summary>展开动画系数（0=收起，1=展开），140ms smoothstep 补间。</summary>
        private class Animation
        {
            public double Value, From, Target;
            public long Start;
            public bool Active;

            public void Init(double v) { Value = v; Target = v; }

            public void SetTarget(double t)
            {
                Target = t;
                if (Math.Abs(Value - t) < 0.001) { Active = false; return; }
                From = Value;
                Start = Environment.TickCount;
                Active = true;
            }

            public double Step(long now)
            {
                if (!Active) return Value;
                double k = (now - Start) / 140.0;
                if (k >= 1) { Active = false; return Value = Target; }
                double e = k * k * (3.0 - 2.0 * k);
                return Value = From + (Target - From) * e;
            }
        }

        private readonly List<CheckItem> _checks = new List<CheckItem>();
        private readonly List<ChipItem> _langChips = new List<ChipItem>();
        private readonly List<ChipItem> _scaleChips = new List<ChipItem>();

        private readonly BtnItem _bClose = new BtnItem();
        private readonly BtnItem _bCancel = new BtnItem();
        private readonly BtnItem _bSave = new BtnItem();
        private readonly List<BtnItem> _btns = new List<BtnItem>();

        private TextBox _portBox;
        private TextBox _trustedBox;
        private TextBox _proxyBox;
        private readonly DshSettings _working;
        private Font _fTitle, _fHead, _fLabel, _fSmall, _fBtn, _fUrl, _fTag;
        private readonly StringFormat _fmtCenter = new StringFormat();
        private readonly StringFormat _fmtNear = new StringFormat();
        private readonly StringFormat _fmtFar = new StringFormat();
        private readonly StringFormat _fmtLabelEllipsis = new StringFormat();
        private string _lang;
        private int _scale = 100;

        // 布局结果（缩放后像素）
        private Rectangle _rBadge, _rTitle, _rClose, _rDivider1;
        private Rectangle[] _rRow = new Rectangle[5];
        private Rectangle _rSec1, _rPortLabel, _rPortBox, _rPortText, _rPortUrl, _rPortHint;
        private Rectangle _rDivider2, _rSec2, _rDivider3, _rSec3;
        private Rectangle _rTrustSlot, _rTrustLabel, _rTrustBox, _rTrustText, _rTrustHint;
        private Rectangle _rProxySlot, _rProxyLabel, _rProxyBox, _rProxyText, _rProxyHint;
        private Rectangle _rLangLabel, _rScaleLabel, _rCancel, _rSave;
        private int _clientH;

        private readonly Animation _tAnim = new Animation();
        private readonly Animation _pAnim = new Animation();
        private System.Windows.Forms.Timer _animTimer;

        public DshSettings Result { get; private set; }

        public SettingsForm(DshSettings current)
        {
            _working = new DshSettings();
            _working.Port = current.Port;
            _working.AutoOpenBrowser = current.AutoOpenBrowser;
            _working.MinimizeToTray = current.MinimizeToTray;
            _working.AutoCheckOnStart = current.AutoCheckOnStart;
            _working.Language = current.Language;
            _working.TrustedHosts = current.TrustedHosts;
            _working.ProxyEnabled = current.ProxyEnabled;
            _working.ProxyUrl = current.ProxyUrl;
            _lang = current.Language;
            _scale = current.UiScale;

            FormBorderStyle = FormBorderStyle.None;
            StartPosition = FormStartPosition.CenterParent;
            BackColor = Theme.Bg;
            AutoScaleMode = AutoScaleMode.None; // 全自绘，手动缩放
            DoubleBuffered = true;
            ShowInTaskbar = false;
            KeyPreview = true; // Esc 取消 / Enter 保存
            Text = Lang.T("settings_title");

            InitTextFormats();
            InitScale(current.UiScale);
            InitFonts();
            InitControls();

            _tAnim.Init(!string.IsNullOrWhiteSpace(current.TrustedHosts) ? 1 : 0);
            _pAnim.Init(current.ProxyEnabled ? 1 : 0);

            ComputeLayout();
            ApplyLayout();
        }

        // ---------- 缩放 / 字体 ----------

        /// <summary>与主窗口同一套缩放规则：固定设计尺寸 + 用户缩放，超出屏幕时钳制。</summary>
        private void InitScale(int uiScale)
        {
            _s = uiScale / 100f;
            try
            {
                Rectangle wa = Screen.PrimaryScreen.WorkingArea;
                float m = Math.Min(wa.Width / (float)WinW, wa.Height / (float)ScaleCapDesignH);
                if (_s > m) _s = m; // 不超过屏幕（按最大布局高度兜底）
            }
            catch { }
            if (_s < 0.7f) _s = 0.7f;
        }

        private void InitTextFormats()
        {
            _fmtCenter.Alignment = StringAlignment.Center;
            _fmtCenter.LineAlignment = StringAlignment.Center;
            _fmtNear.Alignment = StringAlignment.Near;
            _fmtNear.LineAlignment = StringAlignment.Center;
            _fmtFar.Alignment = StringAlignment.Far;
            _fmtFar.LineAlignment = StringAlignment.Center;
            _fmtLabelEllipsis.Alignment = StringAlignment.Near;
            _fmtLabelEllipsis.LineAlignment = StringAlignment.Center;
            _fmtLabelEllipsis.Trimming = StringTrimming.EllipsisCharacter; // 超长行标签尾部省略号，避免硬截断
        }

        private void InitFonts()
        {
            DisposeFonts();
            _fTitle = Theme.Font(14f * _s, FontStyle.Bold);
            _fHead = Theme.Font(9.5f * _s, FontStyle.Bold);
            _fLabel = Theme.Font(10f * _s, FontStyle.Regular);
            _fSmall = Theme.Font(8.5f * _s, FontStyle.Regular);
            _fBtn = Theme.Font(10.5f * _s, FontStyle.Regular);
            _fUrl = Theme.Font(9.5f * _s, FontStyle.Regular);
            _fTag = Theme.Font(8.5f * _s, FontStyle.Regular);
        }

        private void DisposeFonts()
        {
            if (_fTitle != null) _fTitle.Dispose();
            if (_fHead != null) _fHead.Dispose();
            if (_fLabel != null) _fLabel.Dispose();
            if (_fSmall != null) _fSmall.Dispose();
            if (_fBtn != null) _fBtn.Dispose();
            if (_fUrl != null) _fUrl.Dispose();
            if (_fTag != null) _fTag.Dispose();
            _fTitle = _fHead = _fLabel = _fSmall = _fBtn = _fUrl = _fTag = null;
        }

        // ---------- 控件 ----------

        private void InitControls()
        {
            // 端口输入（唯一真实控件，其余全自绘）：无边框 TextBox 嵌入自绘圆角容器
            _portBox = new TextBox();
            _portBox.BackColor = Theme.BgAlt;
            _portBox.ForeColor = Theme.Text;
            _portBox.BorderStyle = BorderStyle.None;
            _portBox.Font = Theme.Font(10.5f * _s, FontStyle.Regular);
            _portBox.MaxLength = 5;
            _portBox.Text = _working.Port.ToString(CultureInfo.InvariantCulture);
            _portBox.KeyPress += delegate(object s, KeyPressEventArgs e)
            {
                if (!char.IsDigit(e.KeyChar) && !char.IsControl(e.KeyChar)) e.Handled = true;
            };
            _portBox.TextChanged += delegate { Invalidate(); }; // 实时 URL 预览 + 非法反馈
            Controls.Add(_portBox);

            // 复选框行（行首带图标；_checks[3] 信任域名开关、_checks[4] 代理开关为一级开关）
            _checks.Add(MakeCheck(Lang.T("settings_auto_open"), Lucide.ExternalLink, _working.AutoOpenBrowser));
            _checks.Add(MakeCheck(Lang.T("settings_min_tray"), Lucide.Monitor, _working.MinimizeToTray));
            _checks.Add(MakeCheck(Lang.T("settings_auto_check"), Lucide.RotateCw, _working.AutoCheckOnStart));
            _checks.Add(MakeCheck(Lang.T("settings_trusted_switch"), Lucide.Globe,
                !string.IsNullOrWhiteSpace(_working.TrustedHosts)));
            _checks.Add(MakeCheck(Lang.T("settings_proxy_enable"), Lucide.Wifi, _working.ProxyEnabled));

            // 信任域名输入框（二级：仅开关勾选时可见）
            _trustedBox = MakeTextBox();
            _trustedBox.Font = Theme.Font(10f * _s, FontStyle.Regular);
            _trustedBox.Text = _working.TrustedHosts;
            Controls.Add(_trustedBox);

            // 代理地址输入框（二级）
            _proxyBox = MakeTextBox();
            _proxyBox.Font = Theme.Font(10f * _s, FontStyle.Regular);
            _proxyBox.Text = _working.ProxyUrl;
            Controls.Add(_proxyBox);

            // 语言胶囊
            ChipItem zh = new ChipItem();
            zh.Text = Lang.T("settings_lang_zh");
            zh.Value = Lang.Zh;
            ChipItem en = new ChipItem();
            en.Text = Lang.T("settings_lang_en");
            en.Value = Lang.En;
            _langChips.Add(zh);
            _langChips.Add(en);

            // 缩放胶囊
            ChipItem sc1 = new ChipItem(); sc1.Text = Lang.T("scale_small"); sc1.Value = "85";
            ChipItem sc2 = new ChipItem(); sc2.Text = Lang.T("scale_medium"); sc2.Value = "100";
            ChipItem sc3 = new ChipItem(); sc3.Text = Lang.T("scale_large"); sc3.Value = "115";
            _scaleChips.Add(sc1);
            _scaleChips.Add(sc2);
            _scaleChips.Add(sc3);

            _bClose.IconX = true;
            _bClose.Variant = ButtonVariant.Ghost;
            _bClose.OnClick = delegate { DialogResult = DialogResult.Cancel; Close(); };

            _bCancel.Text = Lang.T("settings_cancel");
            _bCancel.Variant = ButtonVariant.Secondary;
            _bCancel.OnClick = delegate { DialogResult = DialogResult.Cancel; Close(); };

            _bSave.Text = Lang.T("settings_save");
            _bSave.Variant = ButtonVariant.Primary;
            _bSave.OnClick = delegate { SaveAndClose(); };

            _btns.Add(_bClose);
            _btns.Add(_bCancel);
            _btns.Add(_bSave);
        }

        private CheckItem MakeCheck(string text, string icon, bool on)
        {
            CheckItem c = new CheckItem();
            c.Text = text;
            c.Icon = icon;
            c.Checked = on;
            return c;
        }

        private TextBox MakeTextBox()
        {
            TextBox box = new TextBox();
            box.BackColor = Theme.BgAlt;
            box.ForeColor = Theme.Text;
            box.BorderStyle = BorderStyle.None;
            box.Enter += delegate { Invalidate(); }; // 聚焦时容器描边变强调色
            box.Leave += delegate { Invalidate(); };
            return box;
        }

        // ---------- 布局 ----------

        /// <summary>按当前 _s 与展开动画系数重算全部几何（存储缩放后像素）。</summary>
        private void ComputeLayout()
        {
            _rBadge = new Rectangle(P(PadX), P(12), P(36), P(36));
            _rTitle = new Rectangle(P(72), P(12), P(300), P(36));
            _rClose = new Rectangle(P(WinW - 56), P(12), P(32), P(30));
            _rDivider1 = new Rectangle(P(PadX), P(60), P(ContentW), 1);

            int y = P(76);
            _rSec1 = new Rectangle(P(PadX), y, P(ContentW), P(16));
            y += P(30);
            _rPortLabel = new Rectangle(P(PadX), y, P(300), P(16));
            y += P(24);
            _rPortBox = new Rectangle(P(PadX), y, P(140), P(BoxH));
            _rPortText = new Rectangle(P(PadX) + P(12), y + P(7), P(116), P(20));
            _rPortUrl = new Rectangle(P(PadX + 164), y, P(WinW - PadX - PadX - 164), P(BoxH));
            y += P(BoxH + 8);
            _rPortHint = new Rectangle(P(PadX), y, P(ContentW), P(14));
            y += P(26);

            int rowY = y;
            for (int i = 0; i < 3; i++)
            {
                _rRow[i] = new Rectangle(P(PadX), rowY, P(ContentW), P(RowH));
                rowY += P(RowH + 8);
            }
            y = rowY - P(8) + P(28); // 去末行间距，换分组间距
            _rDivider2 = new Rectangle(P(PadX), y, P(ContentW), 1);
            y += P(18);
            _rSec2 = new Rectangle(P(PadX), y, P(ContentW), P(16));
            y += P(30);

            _rRow[3] = new Rectangle(P(PadX), y, P(ContentW), P(RowH));
            y += P(RowH);

            double t = _tAnim.Value;
            _rTrustSlot = new Rectangle(P(PadX), y, P(ContentW), P((float)(SlotH * t)));
            _rTrustLabel = new Rectangle(P(PadX), y, P(ContentW), P(16));
            _rTrustBox = new Rectangle(P(PadX), y + P(26), P(ContentW), P(BoxH));
            _rTrustText = new Rectangle(P(PadX) + P(12), y + P(33), P(ContentW) - P(24), P(20));
            _rTrustHint = new Rectangle(P(PadX), y + P(64), P(ContentW), P(14));
            y += P((float)(SlotH * t));

            _rRow[4] = new Rectangle(P(PadX), y, P(ContentW), P(RowH));
            y += P(RowH) + P(12);

            double p = _pAnim.Value;
            _rProxySlot = new Rectangle(P(PadX), y, P(ContentW), P((float)(SlotH * p)));
            _rProxyLabel = new Rectangle(P(PadX), y, P(ContentW), P(16));
            _rProxyBox = new Rectangle(P(PadX), y + P(26), P(ContentW), P(BoxH));
            _rProxyText = new Rectangle(P(PadX) + P(12), y + P(33), P(ContentW) - P(24), P(20));
            _rProxyHint = new Rectangle(P(PadX), y + P(64), P(ContentW), P(14));
            y += P((float)(SlotH * p)) + P(14);

            _rDivider3 = new Rectangle(P(PadX), y, P(ContentW), 1);
            y += P(18);
            _rSec3 = new Rectangle(P(PadX), y, P(ContentW), P(16));
            y += P(30);

            _rLangLabel = new Rectangle(P(PadX), y, P(70), P(ChipH));
            _langChips[0].Rect = new Rectangle(P(100), y, P(84), P(ChipH));
            _langChips[1].Rect = new Rectangle(P(192), y, P(100), P(ChipH));
            y += P(ChipH + 12);

            _rScaleLabel = new Rectangle(P(PadX), y, P(70), P(ChipH));
            _scaleChips[0].Rect = new Rectangle(P(100), y, P(88), P(ChipH));
            _scaleChips[1].Rect = new Rectangle(P(196), y, P(88), P(ChipH));
            _scaleChips[2].Rect = new Rectangle(P(292), y, P(88), P(ChipH));
            y += P(ChipH + 28);

            _rSave = new Rectangle(P(WinW - PadX - BtnW), y, P(BtnW), P(BtnH));
            _rCancel = new Rectangle(_rSave.X - P(24) - P(BtnW), y, P(BtnW), P(BtnH));
            _clientH = y + P(BtnH) + P(14);

            // 挂载到各条目的活动矩形（鼠标命中检测直接用）
            for (int i = 0; i < _checks.Count && i < 5; i++) _checks[i].Rect = _rRow[i];
            _bClose.Rect = _rClose;
            _bCancel.Rect = _rCancel;
            _bSave.Rect = _rSave;
        }

        /// <summary>应用当前布局：窗高、输入框位置与展开显隐、圆角 Region。</summary>
        private void ApplyLayout()
        {
            Size sz = new Size(P(WinW), _clientH);
            if (ClientSize != sz) ClientSize = sz;
            _portBox.SetBounds(_rPortText.X, _rPortText.Y, _rPortText.Width, _rPortText.Height);
            SetSectionBox(_trustedBox, _rTrustText, _tAnim.Value);
            SetSectionBox(_proxyBox, _rProxyText, _pAnim.Value);
            try { Theme.ApplyRegion(this, 14); } catch { }
            Invalidate();
        }

        /// <summary>二级输入框随展开动画"生长"显示（高度不超出裁切区），收起时隐藏。</summary>
        private void SetSectionBox(TextBox box, Rectangle textR, double anim)
        {
            double hh = SlotH * anim - 26; // 输入框在槽位内偏移 26，槽位起始前为标签
            if (hh <= 0)
            {
                if (box.Visible) box.Visible = false;
                return;
            }
            if (hh > 20) hh = 20;
            if (!box.Visible) box.Visible = true;
            box.SetBounds(textR.X, textR.Y, textR.Width, P((float)hh));
        }

        // ---------- 动画 ----------

        private void StartAnim()
        {
            if (_animTimer == null)
            {
                _animTimer = new System.Windows.Forms.Timer();
                _animTimer.Interval = 16;
                _animTimer.Tick += delegate(object s, EventArgs e) { AnimTick(); };
            }
            if (!_animTimer.Enabled) _animTimer.Start();
        }

        private void AnimTick()
        {
            long now = Environment.TickCount;
            _tAnim.Step(now);
            _pAnim.Step(now);
            ComputeLayout();
            ApplyLayout();
            if (!_tAnim.Active && !_pAnim.Active && _animTimer != null) _animTimer.Stop();
        }

        // ---------- 绘制 ----------

        protected override void OnPaint(PaintEventArgs e)
        {
            Graphics g = e.Graphics;
            g.SmoothingMode = SmoothingMode.AntiAlias;
            g.TextRenderingHint = TextRenderingHint.AntiAlias;
            g.Clear(Theme.Bg);

            // 头部：徽标 + 标题 + 版本
            DrawBadge(g);
            using (SolidBrush t = new SolidBrush(Theme.Text))
            {
                g.DrawString(Lang.T("settings_title"), _fTitle, t, _rTitle, _fmtNear);
            }
            float tw = g.MeasureString(Lang.T("settings_title"), _fTitle).Width;
            using (SolidBrush m = new SolidBrush(Theme.TextMuted))
            {
                g.DrawString("v" + Program.Version, _fTag, m,
                    new RectangleF(_rTitle.X + tw + P(10f), _rTitle.Y, P(120f), _rTitle.Height), _fmtNear);
            }

            DrawDivider(g, _rDivider1);
            DrawHead(g, _rSec1, Lang.T("sec_general"));

            // 端口行：输入框 + 实时 URL 预览 + 提示（非法时红描边 / 红提示）
            int port;
            bool valid = TryGetPort(out port);
            using (SolidBrush t = new SolidBrush(Theme.Text))
            {
                g.DrawString(Lang.T("settings_port"), _fLabel, t, _rPortLabel, _fmtNear);
            }
            DrawInputBox(g, _rPortBox, _portBox, valid ? (_portBox.Focused ? Theme.Accent : Theme.CardBorder) : Theme.Red);
            using (SolidBrush ub = new SolidBrush(valid ? Theme.TextMuted : Theme.Red))
            {
                string url = valid
                    ? "http://127.0.0.1:" + port.ToString(CultureInfo.InvariantCulture)
                    : Lang.T("port_invalid");
                g.DrawString(url, _fUrl, ub, _rPortUrl, _fmtFar);
            }
            using (SolidBrush hb = new SolidBrush(valid ? Theme.TextMuted : Theme.Red))
            {
                g.DrawString(Lang.T("settings_port_hint"), _fSmall, hb, _rPortHint, _fmtNear);
            }

            foreach (CheckItem c in _checks) DrawCheck(g, c);

            DrawDivider(g, _rDivider2);
            DrawHead(g, _rSec2, Lang.T("sec_network"));
            DrawSection(g, _rTrustSlot, _trustedBox, _rTrustLabel, _rTrustBox, _rTrustHint,
                "settings_trusted", "settings_trusted_hint");

            DrawSection(g, _rProxySlot, _proxyBox, _rProxyLabel, _rProxyBox, _rProxyHint,
                "settings_proxy", "settings_proxy_hint");

            DrawDivider(g, _rDivider3);
            DrawHead(g, _rSec3, Lang.T("sec_appearance"));

            using (SolidBrush t = new SolidBrush(Theme.Text))
            {
                g.DrawString(Lang.T("settings_lang"), _fLabel, t, _rLangLabel, _fmtNear);
                g.DrawString(Lang.T("settings_scale"), _fLabel, t, _rScaleLabel, _fmtNear);
            }
            DrawChips(g, _langChips, _lang);
            DrawChips(g, _scaleChips, _scale.ToString(CultureInfo.InvariantCulture));

            DrawBtn(g, _bCancel);
            DrawBtn(g, _bSave);
            DrawBtn(g, _bClose);

            // 1px 外边框：为无框窗口的边缘定形（深浅桌面下都清晰）
            using (GraphicsPath gp = Theme.RoundedRect(new Rectangle(0, 0, ClientSize.Width - 1, ClientSize.Height - 1), 14))
            using (Pen pn = new Pen(Theme.CardBorder))
            {
                g.DrawPath(pn, gp);
            }
        }

        /// <summary>标题徽标：与主窗口同一套「蓝色渐变底 + 白色鲸鱼」。</summary>
        private void DrawBadge(Graphics g)
        {
            Rectangle badge = _rBadge;
            using (GraphicsPath gp = Theme.RoundedRect(badge, 10))
            using (LinearGradientBrush lg = new LinearGradientBrush(
                badge, Color.FromArgb(93, 125, 255), Theme.Accent, 90f))
            {
                g.FillPath(lg, gp);
            }
            float bw = P(28f), bh = P(22f);
            float bx = badge.X + (badge.Width - bw) / 2f;
            float by = badge.Y + (badge.Height - bh) / 2f;
            SvgWhale.Draw(g, new RectangleF(bx, by, bw, bh), Color.White);
        }

        private void DrawDivider(Graphics g, Rectangle r)
        {
            using (Pen p = new Pen(Theme.Divider))
            {
                g.DrawLine(p, r.X, r.Y, r.X + r.Width, r.Y);
            }
        }

        private void DrawHead(Graphics g, Rectangle r, string text)
        {
            using (SolidBrush t = new SolidBrush(Theme.TextMuted))
            {
                g.DrawString(text, _fHead, t, r, _fmtNear);
            }
        }

        /// <summary>二级展开区（信任 / 代理）：按槽位高度裁切，展开动画时自上而下渐显。</summary>
        private void DrawSection(Graphics g, Rectangle slot, TextBox box,
            Rectangle label, Rectangle boxR, Rectangle hint, string labelKey, string hintKey)
        {
            if (slot.Height <= 0) return;
            GraphicsState st = g.Save();
            g.SetClip(new RectangleF(slot.X - 1, slot.Y - 1, slot.Width + 2, slot.Height + 2));
            using (SolidBrush t = new SolidBrush(Theme.Text))
            {
                g.DrawString(Lang.T(labelKey), _fLabel, t, label, _fmtNear);
            }
            DrawInputBox(g, boxR, box, box.Focused ? Theme.Accent : Theme.CardBorder);
            using (SolidBrush m = new SolidBrush(Theme.TextMuted))
            {
                g.DrawString(Lang.T(hintKey), _fSmall, m, hint, _fmtNear);
            }
            g.Restore(st);
        }

        /// <summary>通用输入框容器：圆角底 + 边框（边框色由调用方决定：聚焦=强调色，非法=红）。</summary>
        private void DrawInputBox(Graphics g, Rectangle r, TextBox box, Color border)
        {
            Rectangle rr = new Rectangle(r.X, r.Y, r.Width - 1, r.Height - 1);
            using (GraphicsPath gp = Theme.RoundedRect(rr, 8))
            using (SolidBrush f = new SolidBrush(Theme.BgAlt))
            {
                g.FillPath(f, gp);
            }
            using (GraphicsPath gp = Theme.RoundedRect(rr, 8))
            using (Pen pn = new Pen(border, box.Focused ? 1.4f : 1f))
            {
                g.DrawPath(pn, gp);
            }
        }

        private void DrawBtn(Graphics g, BtnItem b)
        {
            Theme.PaintButton(g, b.Rect, b.Variant, b.IconX ? "" : b.Text, _fBtn, true, b.Hover, b.Down);
            if (b.IconX)
            {
                Color fc = b.Hover ? Theme.Text : Theme.TextMuted;
                float size = P(14);
                RectangleF bounds = new RectangleF(
                    b.Rect.X + (b.Rect.Width - size) / 2f, b.Rect.Y + (b.Rect.Height - size) / 2f, size, size);
                Lucide.Draw(g, Lucide.X, bounds, fc, false);
            }
        }

        private void DrawChips(Graphics g, List<ChipItem> chips, string selected)
        {
            foreach (ChipItem c in chips)
            {
                bool sel = c.Value == selected;
                Rectangle r = c.Rect;
                Rectangle rr = new Rectangle(r.X, r.Y, r.Width - 1, r.Height - 1);
                using (GraphicsPath gp = Theme.RoundedRect(rr, 8))
                using (SolidBrush f = new SolidBrush(sel ? Theme.Accent : (c.Hover ? Theme.Hover : Theme.BgAlt)))
                {
                    g.FillPath(f, gp);
                }
                using (GraphicsPath gp = Theme.RoundedRect(rr, 8))
                using (Pen pn = new Pen(sel ? Theme.Accent : Theme.CardBorder))
                {
                    g.DrawPath(pn, gp);
                }
                using (SolidBrush t = new SolidBrush(sel ? Color.White : Theme.Text))
                {
                    g.DrawString(c.Text, _fLabel, t, r, _fmtCenter);
                }
            }
        }

        private void DrawCheck(Graphics g, CheckItem c)
        {
            Rectangle r = c.Rect;
            // 行悬停背景：整行可点提示（复选框本身不再独行其色）
            if (c.Hover)
            {
                using (GraphicsPath gp = Theme.RoundedRect(new Rectangle(r.X, r.Y, r.Width - 1, r.Height - 1), 8))
                using (SolidBrush f = new SolidBrush(Theme.Hover))
                {
                    g.FillPath(f, gp);
                }
            }
            int boxX = r.X + P(32f); // 留给行首图标的位
            if (!string.IsNullOrEmpty(c.Icon))
            {
                int isz = P(14f);
                Rectangle ibr = new Rectangle(r.X + P(8f), r.Y + (r.Height - isz) / 2, isz, isz);
                Lucide.Draw(g, c.Icon,
                    new RectangleF((float)ibr.X, (float)ibr.Y, (float)ibr.Width, (float)ibr.Height),
                    c.Checked ? Theme.Accent : Theme.TextMuted, false);
            }
            int cb = P(17f);
            Rectangle box = new Rectangle(boxX, r.Y + (r.Height - cb) / 2, cb, cb);
            Rectangle br = new Rectangle(box.X, box.Y, box.Width - 1, box.Height - 1);
            using (GraphicsPath gp = Theme.RoundedRect(br, 5))
            using (SolidBrush f = new SolidBrush(c.Checked ? Theme.Accent : Theme.BgAlt))
            {
                g.FillPath(f, gp);
            }
            using (GraphicsPath gp = Theme.RoundedRect(br, 5))
            using (Pen pn = new Pen(c.Checked ? Theme.Accent : (c.Hover ? Theme.Accent : Theme.CardBorder)))
            {
                g.DrawPath(pn, gp);
            }
            if (c.Checked)
            {
                using (Pen p = new Pen(Color.White, P(1.8f)))
                {
                    p.StartCap = LineCap.Round;
                    p.EndCap = LineCap.Round;
                    g.DrawLines(p, new Point[] {
                        new Point(box.X + P(4f), box.Y + P(9f)),
                        new Point(box.X + P(7f), box.Y + P(12f)),
                        new Point(box.X + P(13f), box.Y + P(5f)) });
                }
            }
            int tx = boxX + cb + P(9f);
            using (SolidBrush t = new SolidBrush(Theme.Text))
            {
                g.DrawString(c.Text, _fLabel, t,
                    new Rectangle(tx, r.Y, r.Width - (tx - r.X) + P(8f), r.Height), _fmtLabelEllipsis);
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
                bool h = c.Rect.Contains(e.Location);
                if (c.Hover != h) { c.Hover = h; changed = true; }
                if (h) hand = true;
            }
            foreach (ChipItem c in _langChips)
            {
                bool h = c.Rect.Contains(e.Location);
                if (c.Hover != h) { c.Hover = h; changed = true; }
                if (h) hand = true;
            }
            foreach (ChipItem c in _scaleChips)
            {
                bool h = c.Rect.Contains(e.Location);
                if (c.Hover != h) { c.Hover = h; changed = true; }
                if (h) hand = true;
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
            foreach (ChipItem c in _scaleChips)
            {
                if (c.Rect.Contains(e.Location))
                {
                    int v;
                    if (int.TryParse(c.Value, NumberStyles.None, CultureInfo.InvariantCulture, out v))
                    {
                        _scale = v;
                        RebuildScale(); // 缩放实时预览：立即按新系数重建本窗
                    }
                    return;
                }
            }
            foreach (CheckItem c in _checks)
            {
                if (c.Rect.Contains(e.Location))
                {
                    c.Checked = !c.Checked;
                    ToggleSection(c, c.Checked); // 一级开关带动画展开/收起二级填写区
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
            if (e.Y < P(60)) NativeDrag();
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
            foreach (CheckItem c in _checks) { if (c.Hover) { c.Hover = false; changed = true; } }
            foreach (ChipItem c in _langChips) { if (c.Hover) { c.Hover = false; changed = true; } }
            foreach (ChipItem c in _scaleChips) { if (c.Hover) { c.Hover = false; changed = true; } }
            if (changed) Invalidate();
            Cursor = Cursors.Default;
        }

        /// <summary>信任 / 代理一级开关 → 对应二级区动画目标（_checks[3]=信任域名，_checks[4]=代理）。</summary>
        private void ToggleSection(CheckItem c, bool on)
        {
            int idx = _checks.IndexOf(c);
            double target = on ? 1 : 0;
            if (idx == 3) _tAnim.SetTarget(target);
            else if (idx == 4) _pAnim.SetTarget(target);
            else return;
            StartAnim();
        }

        /// <summary>换缩放档后重建：系数 → 字体 → 布局 → 窗高与控件位置。</summary>
        private void RebuildScale()
        {
            InitScale(_scale);
            InitFonts();
            _portBox.Font = Theme.Font(10.5f * _s, FontStyle.Regular);
            _trustedBox.Font = Theme.Font(10f * _s, FontStyle.Regular);
            _proxyBox.Font = Theme.Font(10f * _s, FontStyle.Regular);
            ComputeLayout();
            ApplyLayout();
        }

        // ---------- 键盘 ----------

        protected override void OnKeyDown(KeyEventArgs e)
        {
            base.OnKeyDown(e);
            if (e.KeyCode == Keys.Escape)
            {
                e.Handled = true;
                DialogResult = DialogResult.Cancel;
                Close();
            }
            else if (e.KeyCode == Keys.Enter)
            {
                e.Handled = true;
                e.SuppressKeyPress = true;
                SaveAndClose();
            }
        }

        // ---------- 保存 ----------

        private bool TryGetPort(out int port)
        {
            port = 0;
            if (!int.TryParse(_portBox.Text, NumberStyles.None, CultureInfo.InvariantCulture, out port)) return false;
            return port >= 1 && port <= 65535;
        }

        private void SaveAndClose()
        {
            int port;
            if (!TryGetPort(out port)) port = 3080; // 已做红描边 + 红提示反馈，此处兜底
            _working.Port = port;
            _working.AutoOpenBrowser = _checks[0].Checked;
            _working.MinimizeToTray = _checks[1].Checked;
            _working.AutoCheckOnStart = _checks[2].Checked;
            // 信任域名：仅开关（_checks[3]）勾选时启用，否则清空（公网访问关闭）
            bool trustedOn = _checks[3].Checked;
            _working.TrustedHosts = trustedOn && _trustedBox.Text != null ? _trustedBox.Text.Trim() : "";
            // 代理：开关（_checks[4]）控制是否注入，地址始终保留文本
            // （关闭再打开不会丢配置；仅当 ProxyEnabled=false 时启动直连）
            bool proxyOn = _checks[4].Checked;
            _working.ProxyEnabled = proxyOn;
            _working.ProxyUrl = _proxyBox.Text == null ? "" : _proxyBox.Text.Trim();
            _working.Language = _lang;
            _working.UiScale = _scale;
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
            Theme.ApplyRegion(this, 14);
        }

        protected override void OnResize(EventArgs e)
        {
            base.OnResize(e);
            try { Theme.ApplyRegion(this, 14); } catch { }
        }

        protected override void OnFormClosed(FormClosedEventArgs e)
        {
            if (_animTimer != null)
            {
                _animTimer.Stop();
                _animTimer.Dispose();
                _animTimer = null;
            }
            base.OnFormClosed(e);
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
