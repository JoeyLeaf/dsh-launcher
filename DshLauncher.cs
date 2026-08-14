using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Drawing;
using System.Drawing.Drawing2D;
using System.Drawing.Text;
using System.IO;
using System.Runtime.InteropServices;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using System.Windows.Forms;

namespace DshLauncher
{
    internal static class Program
    {
        /// <summary>应用版本（与 GitHub Release 标签保持一致）。</summary>
        public const string Version = "0.1.0";

        private static Bitmap _iconBmp;
        private static Icon _icon;

        /// <summary>
        /// 运行时绘制的应用图标（32×32）：浅色圆角徽章 + 黑色鲸鱼 + 蓝色眼睛。
        /// 与 dsh 的鲸鱼一致，浅色徽章作为启动器的区分度。
        /// </summary>
        public static Icon AppIcon()
        {
            if (_icon == null)
            {
                _iconBmp = new Bitmap(32, 32);
                using (Graphics g = Graphics.FromImage(_iconBmp))
                {
                    g.SmoothingMode = SmoothingMode.AntiAlias;
                    g.TextRenderingHint = TextRenderingHint.AntiAlias;
                    g.Clear(Color.Transparent);

                    Rectangle badge = new Rectangle(1, 1, 30, 30);
                    using (GraphicsPath gp = Theme.RoundedRect(badge, 7))
                    using (SolidBrush fb = new SolidBrush(Color.FromArgb(238, 240, 245))) // 浅色底：黑鲸清晰
                    {
                        g.FillPath(fb, gp);
                    }
                    using (GraphicsPath gp = Theme.RoundedRect(badge, 7))
                    using (Pen pn = new Pen(Theme.Accent, 1.2f)) // 蓝色描边：品牌色区分
                    {
                        g.DrawPath(pn, gp);
                    }

                    // 官方黑色鲸鱼（浅底上清晰），约 72% 宽
                    SvgWhale.Draw(g, new RectangleF(4.5f, 7f, 23f, 18f), Color.FromArgb(20, 22, 29));
                }
                _icon = Icon.FromHandle(_iconBmp.GetHicon());
            }
            return _icon;
        }

        [STAThread]
        private static void Main(string[] args)
        {
            try { SetProcessDPIAware(); } catch { }

            string exeDir = AppDomain.CurrentDomain.BaseDirectory;

            if (args != null && args.Length > 0 && Array.IndexOf(args, "--selftest") >= 0)
            {
                int code = RunSelfTest(exeDir);
                Environment.Exit(code);
                return;
            }

            string shotMain = GetArg(args, "--shot");
            string shotSettings = GetArg(args, "--shot-settings");
            string shotIcon = GetArg(args, "--shot-icon");

            bool createdNew;
            using (Mutex m = new Mutex(true, "Global\\DshLauncher_SingleInstance", out createdNew))
            {
                if (!createdNew)
                {
                    try { EventWaitHandle.OpenExisting("Global\\DshLauncher_ShowSignal").Set(); } catch { }
                    return; // 单实例：通知已有窗口并退出
                }

                EventWaitHandle showSignal = new EventWaitHandle(false, EventResetMode.AutoReset, "Global\\DshLauncher_ShowSignal");
                Application.EnableVisualStyles();
                Application.SetCompatibleTextRenderingDefault(true);
                MainForm form = new MainForm();

                Thread signalThread = new Thread(delegate()
                {
                    while (true)
                    {
                        try { showSignal.WaitOne(); }
                        catch { break; }
                        try
                        {
                            form.BeginInvoke(new Action(delegate()
                            {
                                form.Show();
                                form.WindowState = FormWindowState.Normal;
                                form.Activate();
                            }));
                        }
                        catch { }
                    }
                });
                signalThread.IsBackground = true;
                signalThread.Start();

                if (shotMain != null || shotSettings != null)
                {
                    // 截图调试模式：--shot 主窗口，--shot-settings 设置对话框，--shot-icon 应用图标
                    form.Shown += delegate(object s, EventArgs e)
                    {
                        Thread t = new Thread(delegate()
                        {
                            try
                            {
                                Thread.Sleep(6500);
                                form.BeginInvoke(new Action(delegate()
                                {
                                    try
                                    {
                                        if (shotIcon != null && _iconBmp != null)
                                        {
                                            _iconBmp.Save(shotIcon, System.Drawing.Imaging.ImageFormat.Png);
                                        }
                                        if (shotMain != null) SaveWindowPng(form, shotMain);
                                        if (shotSettings != null)
                                        {
                                            using (SettingsForm sf = new SettingsForm(form.CurrentSettings))
                                            {
                                                sf.Shown += delegate(object s2, EventArgs e2)
                                                {
                                                    System.Windows.Forms.Timer tt = new System.Windows.Forms.Timer();
                                                    tt.Interval = 800;
                                                    tt.Tick += delegate(object ts, EventArgs te)
                                                    {
                                                        tt.Stop();
                                                        try { SaveWindowPng(sf, shotSettings); }
                                                        catch (Exception ex) { try { File.WriteAllText(Path.Combine(exeDir, "shot-error.txt"), ex.ToString()); } catch { } }
                                                        sf.Close();
                                                    };
                                                    tt.Start();
                                                };
                                                sf.ShowDialog(form);
                                            }
                                        }
                                    }
                                    catch (Exception ex)
                                    {
                                        try { File.WriteAllText(Path.Combine(exeDir, "shot-error.txt"), ex.ToString()); } catch { }
                                    }
                                    Application.Exit();
                                }));
                            }
                            catch { }
                        });
                        t.IsBackground = true;
                        t.Start();
                    };
                }

                Application.Run(form);
            }
        }

        private static string GetArg(string[] args, string name)
        {
            if (args == null) return null;
            for (int i = 0; i < args.Length - 1; i++)
            {
                if (string.Equals(args[i], name, StringComparison.OrdinalIgnoreCase)) return args[i + 1];
            }
            return null;
        }

        [DllImport("user32.dll")]
        private static extern bool GetWindowRect(IntPtr hWnd, out RECT rc);

        [DllImport("user32.dll")]
        private static extern bool PrintWindow(IntPtr hWnd, IntPtr hdcBlt, uint nFlags);

        [StructLayout(LayoutKind.Sequential)]
        private struct RECT { public int L, T, R, B; }

        /// <summary>用 PrintWindow 精确截取窗口自身渲染（无遮挡 / 无位移干扰）。</summary>
        private static void SaveWindowPng(Form f, string path)
        {
            RECT r;
            GetWindowRect(f.Handle, out r);
            int w = r.R - r.L, h = r.B - r.T;
            if (w <= 0 || h <= 0) return;
            using (Bitmap bmp = new Bitmap(w, h))
            {
                using (Graphics g = Graphics.FromImage(bmp))
                {
                    IntPtr hdc = g.GetHdc();
                    try { PrintWindow(f.Handle, hdc, 2 /*PW_RENDERFULLCONTENT*/); }
                    finally { g.ReleaseHdc(hdc); }
                }
                bmp.Save(path, System.Drawing.Imaging.ImageFormat.Png);
            }
        }

        // ---------- 自检（--selftest）：无界面验证检测 / 启动 / 停止，写 selftest.log ----------

        private static int RunSelfTest(string dir)
        {
            string logFile = Path.Combine(dir, "selftest.log");
            Action<string> log = delegate(string m)
            {
                string line = "[" + DateTime.Now.ToString("HH:mm:ss") + "] " + m;
                try { File.AppendAllText(logFile, line + "\r\n", Encoding.UTF8); } catch { }
                try { Console.WriteLine(line); } catch { }
            };
            try { if (File.Exists(logFile)) File.Delete(logFile); } catch { }

            int failures = 0;
            try
            {
                string npm = DshService.NpmVersion();
                log("npm=" + (npm ?? "FAIL"));
                if (string.IsNullOrEmpty(npm)) failures++;

                string node = DshService.NodeVersion();
                log("node=" + (node ?? "FAIL"));
                if (string.IsNullOrEmpty(node)) failures++;

                string inst = DshService.InstalledDshVersion();
                log("installedDsh=" + (inst ?? "NONE"));
                if (string.IsNullOrEmpty(inst)) failures++;

                string latest = DshService.LatestDshVersion();
                log("latestDsh=" + (latest ?? "FAIL(network?)"));

                int p;
                bool r3080 = DshService.IsRunning(3080, out p);
                log("running3080=" + r3080 + (r3080 ? " pid=" + p : ""));
                if (!r3080) failures++;

                string logDir = Path.Combine(dir, "logs");
                try { Directory.CreateDirectory(logDir); } catch { }
                string webLog = Path.Combine(logDir, "dsh-web-selftest.log");

                log("== 启动测试（端口 3081，不影响 3080 主实例）==");
                bool startOk = DshService.Start(3081, dir, webLog, log);
                log("start3081=" + startOk);
                bool r3081 = DshService.IsRunning(3081, out p);
                log("running3081=" + r3081 + (r3081 ? " pid=" + p : ""));
                if (!startOk || !r3081) failures++;

                log("== 停止测试（端口 3081）==");
                bool stopOk = DshService.Stop(3081, log);
                log("stop3081=" + stopOk);
                bool still = DshService.IsRunning(3081, out p);
                log("running3081After=" + still);
                if (!stopOk || still) failures++;

                bool r3080b = DshService.IsRunning(3080, out p);
                log("running3080After=" + r3080b + (r3080b ? " pid=" + p : ""));
                if (!r3080b) failures++;
            }
            catch (Exception e)
            {
                log("SELFTEST EXCEPTION: " + e);
                failures++;
            }
            log(failures == 0 ? "SELFTEST OK" : "SELFTEST FAILED failures=" + failures);
            return failures == 0 ? 0 : 1;
        }

        [DllImport("user32.dll")]
        private static extern bool SetProcessDPIAware();
    }

    internal enum StatusKind { Running, Stopped, Checking }

    /// <summary>
    /// 主窗口：完全自绘（无子控件重叠 / 无 ClearType 毛边 / 无透明标签覆盖问题）。
    /// 标题栏、状态胶囊、信息卡片、按钮、日志全部由 OnPaint 统一渲染。
    /// </summary>
    internal class MainForm : Form
    {
        // ---------- 布局（96dpi 设计坐标，运行期按 DPI 缩放） ----------
        private const int WinW = 640, WinH = 600;
        private readonly float _s;

        private int P(float v) { return (int)(v * _s + 0.5f); }

        // ---------- 按钮模型 ----------
        private enum GlyphKind { None, Play, Stop, Restart, Open, Gear }

        private class Btn
        {
            public Rectangle Rect;
            public ButtonVariant Variant;
            public string Text = "";
            public GlyphKind Glyph = GlyphKind.None; // 图标按钮：非 None 时绘制图标而非文字
            public bool Enabled = true;
            public bool Hover;
            public bool Down;
            public Action OnClick;
        }

        private readonly List<Btn> _btns = new List<Btn>();
        private Btn _bToggle, _bRestart, _bOpen, _bRefresh, _bSettings, _bMin, _bClose;

        // ---------- 状态数据 ----------
        private readonly DshSettings _settings;
        private bool _busy;
        private bool _running;
        private bool _updateAvailable;
        private bool _realExit;
        private bool _balloonShown;
        private bool _npmOk = true;      // npm 是否存在（首次使用场景指引）
        private bool _nodeOk = true;
        private bool _dshInstalled = true; // dsh 是否已安装（从未跑过官方命令时为 false）
        private int _cardHover = -1;     // 悬停的卡片索引（0=npm 1=node 2=inst 3=latest），-1 无
        private int _cardDown = -1;
        private bool _chipHover;         // dsh 卡片"更新"徽标悬停
        private bool _chipDown;

        private string _npmVer = "…", _nodeVer = "…", _instVer = "…", _latestVer = "…";
        private StatusKind _kind = StatusKind.Checking;
        private string _statusMain = "检测中…";
        private string _statusSub = "";

        // ---------- 字体 ----------
        private Font _fTitle, _fTag, _fStatus, _fSub, _fCaption, _fValue, _fBtn, _fPill;

        // ---------- 控件 ----------
        private LogView _logView;
        private NotifyIcon _tray;
        private ContextMenuStrip _trayMenu;

        private readonly StringFormat _centerFmt = new StringFormat();

        public MainForm()
        {
            _settings = ConfigStore.Load();
            using (Graphics g = CreateGraphics()) _s = Math.Max(1f, g.DpiX / 96f);

            Text = "DSH 启动器";
            Icon = Program.AppIcon();
            FormBorderStyle = FormBorderStyle.None;
            StartPosition = FormStartPosition.CenterScreen;
            BackColor = Theme.Bg;
            AutoScaleMode = AutoScaleMode.None; // 全自绘，手动缩放
            ClientSize = new Size(P(WinW), P(WinH));
            DoubleBuffered = true;
            KeyPreview = true;

            _centerFmt.Alignment = StringAlignment.Center;
            _centerFmt.LineAlignment = StringAlignment.Center;

            InitFonts();
            InitButtons();
            InitLog();
            InitTray();
            UpdateButtons();

            if (_settings.AutoCheckOnStart) this.Shown += delegate(object s, EventArgs e) { RefreshAllAsync(); };
            else this.Shown += delegate(object s, EventArgs e) { RefreshStatusAsync(); };
        }

        // ---------- 初始化 ----------

        private void InitFonts()
        {
            _fTitle = Theme.Font(14f * _s, FontStyle.Bold);
            _fTag = Theme.Font(8.5f * _s, FontStyle.Regular);
            _fStatus = Theme.Font(13f * _s, FontStyle.Bold);
            _fSub = Theme.Font(9.5f * _s, FontStyle.Regular);
            _fCaption = Theme.Font(9f * _s, FontStyle.Regular);
            _fValue = Theme.Font(16f * _s, FontStyle.Bold);
            _fBtn = Theme.Font(10.5f * _s, FontStyle.Regular);
            _fPill = Theme.Font(9.5f * _s, FontStyle.Regular);
        }

        private void InitButtons()
        {
            // 主操作：启停（合并）/ 重启 / 打开页面 —— 图标按钮
            _bToggle = NewBtn("", ButtonVariant.Primary, 164, 294, 96, 44);
            _bToggle.Glyph = GlyphKind.Play;
            _bToggle.OnClick = delegate { if (_running) StopAsync(); else StartAsync(); };

            _bRestart = NewBtn("", ButtonVariant.Secondary, 272, 294, 96, 44);
            _bRestart.Glyph = GlyphKind.Restart;
            _bRestart.OnClick = delegate { RestartAsync(); };

            _bOpen = NewBtn("", ButtonVariant.Secondary, 380, 294, 96, 44);
            _bOpen.Glyph = GlyphKind.Open;
            _bOpen.OnClick = delegate { OpenUi(); };

            // 右上角：设置齿轮 / 最小化 / 关闭
            _bSettings = NewBtn("", ButtonVariant.Ghost, 498, 12, 34, 30);
            _bSettings.Glyph = GlyphKind.Gear;
            _bSettings.OnClick = delegate { OpenSettings(); };

            _bMin = NewBtn("—", ButtonVariant.Ghost, 540, 12, 34, 30);
            _bMin.OnClick = delegate { WindowState = FormWindowState.Minimized; }; // 最小化到任务栏

            _bClose = NewBtn("✕", ButtonVariant.Ghost, 586, 12, 34, 30);
            _bClose.OnClick = delegate { this.Close(); }; // 关闭 → 按设置进托盘

            _bRefresh = NewBtn("刷新", ButtonVariant.Ghost, 500, 74, 120, 30);
            _bRefresh.OnClick = delegate { RefreshAllAsync(); };
        }

        private Btn NewBtn(string text, ButtonVariant variant, float x, float y, float w, float h)
        {
            Btn b = new Btn();
            b.Text = text;
            b.Variant = variant;
            b.Rect = new Rectangle(P(x), P(y), P(w), P(h));
            _btns.Add(b);
            return b;
        }

        private void InitLog()
        {
            _logView = new LogView();
            _logView.Font = new Font("Consolas", 9f * _s);
            _logView.SetBounds(P(20), P(412), P(600), P(168));
            Controls.Add(_logView);
        }

        private void InitTray()
        {
            _trayMenu = new ContextMenuStrip();
            _trayMenu.BackColor = Theme.Bg;
            _trayMenu.ForeColor = Theme.Text;
            AddMenu("打开主界面", delegate { ShowWindow(); });
            AddMenu("启动 dsh", delegate { StartAsync(); });
            AddMenu("停止 dsh", delegate { StopAsync(); });
            AddMenu("打开 Web 界面", delegate { OpenUi(); });
            _trayMenu.Items.Add(new ToolStripSeparator());
            AddMenu("退出", delegate { ExitApp(); });

            _tray = new NotifyIcon();
            _tray.Icon = Program.AppIcon();
            _tray.Text = "DSH 启动器";
            _tray.ContextMenuStrip = _trayMenu;
            _tray.Visible = true;
            _tray.DoubleClick += delegate(object s, EventArgs e) { ShowWindow(); };
            _tray.Click += delegate(object s, EventArgs e)
            {
                MouseEventArgs me = e as MouseEventArgs;
                if (me != null && me.Button == MouseButtons.Left) ShowWindow();
            };
        }

        private void AddMenu(string text, EventHandler handler)
        {
            ToolStripMenuItem item = new ToolStripMenuItem(text);
            item.Click += handler;
            _trayMenu.Items.Add(item);
        }

        // ---------- 绘制 ----------

        protected override void OnPaint(PaintEventArgs e)
        {
            Graphics g = e.Graphics;
            g.SmoothingMode = SmoothingMode.AntiAlias;
            g.TextRenderingHint = TextRenderingHint.AntiAlias;
            g.Clear(Theme.Bg);

            DrawHeader(g);
            DrawStatusLine(g);
            DrawCards(g);
            foreach (Btn b in _btns) DrawButton(g, b);
        }

        private void DrawHeader(Graphics g)
        {
            // 徽标：蓝色渐变底 + 白色鲸鱼 + 深蓝眼睛（与托盘图标同一鲸鱼、反色配色）
            Rectangle badge = new Rectangle(P(20), P(12), P(36), P(36));
            using (GraphicsPath gp = Theme.RoundedRect(badge, 10))
            using (LinearGradientBrush lg = new LinearGradientBrush(
                badge, Color.FromArgb(93, 125, 255), Color.FromArgb(77, 107, 254), 90f))
            {
                g.FillPath(lg, gp);
            }
            float bw = P(28f), bh = P(22f);
            float bx = P(20f) + (P(36f) - bw) / 2f;
            float by = P(12f) + (P(36f) - bh) / 2f;
            SvgWhale.Draw(g, new RectangleF(bx, by, bw, bh), Color.White);

            // 标题 / 副标题
            using (SolidBrush t = new SolidBrush(Theme.Text))
            {
                g.DrawString("DSH 启动器", _fTitle, t, P(66), P(10));
            }
            using (SolidBrush m = new SolidBrush(Theme.TextMuted))
            {
                g.DrawString("DeepSeek Harness · Web GUI 托管 · v" + Program.Version, _fTag, m, P(66), P(37));
            }

            DrawPill(g);
        }

        private void DrawPill(Graphics g)
        {
            Color dotColor = Theme.Green;
            string text = "运行中";
            if (_kind == StatusKind.Stopped) { dotColor = Theme.Red; text = "未运行"; }
            else if (_kind == StatusKind.Checking) { dotColor = Theme.Amber; text = "检测中"; }

            string full = "● " + text;
            SizeF sz = g.MeasureString(full, _fPill);
            int pillW = P((int)sz.Width + 22);
            int pillH = P(26);
            Rectangle pill = new Rectangle(P(490) - pillW - P(8), P(16), pillW, pillH); // 右边界在齿轮前

            using (GraphicsPath gp = Theme.RoundedRect(pill, pillH / 2))
            using (SolidBrush f = new SolidBrush(Theme.BgAlt))
            {
                g.FillPath(f, gp);
            }
            using (GraphicsPath gp = Theme.RoundedRect(pill, pillH / 2))
            using (Pen pn = new Pen(Theme.CardBorder))
            {
                g.DrawPath(pn, gp);
            }
            using (SolidBrush db = new SolidBrush(dotColor))
            {
                g.DrawString("●", _fPill, db, pill.X + P(10), pill.Y + P(4));
            }
            using (SolidBrush tb = new SolidBrush(Theme.Text))
            {
                g.DrawString(text, _fPill, tb, pill.X + P(26), pill.Y + P(4));
            }
        }

        private void DrawStatusLine(Graphics g)
        {
            Color dotColor = Theme.Green;
            if (_kind == StatusKind.Stopped) dotColor = Theme.Red;
            else if (_kind == StatusKind.Checking) dotColor = Theme.Amber;

            using (SolidBrush db = new SolidBrush(dotColor))
            {
                g.DrawString("●", _fStatus, db, P(18), P(70));
            }
            using (SolidBrush t = new SolidBrush(Theme.Text))
            {
                g.DrawString(_statusMain, _fStatus, t, P(44), P(68));
            }
            using (SolidBrush m = new SolidBrush(Theme.TextMuted))
            {
                g.DrawString(_statusSub, _fSub, m, P(44), P(94));
            }
        }

        private void DrawCards(Graphics g)
        {
            DrawCard(g, P(20), P(114), "npm 版本", _npmVer, !_npmOk ? "安装 Node.js" : null, _cardHover == 0);
            DrawCard(g, P(328), P(114), "node 版本", _nodeVer, !_nodeOk ? "安装 Node.js" : null, _cardHover == 1);
            string instLabel = null;
            if (!_dshInstalled) instLabel = (_npmOk && _nodeOk) ? "安装 dsh" : "需先安装 Node.js";
            DrawCard(g, P(20), P(198), "dsh 已装版本", _instVer, instLabel, _cardHover == 2);
            DrawCard(g, P(328), P(198), "dsh 最新版本", _latestVer, null, false);
            DrawUpdateChip(g); // 版本不一致时的"更新"徽标
        }

        /// <summary>"更新"徽标：dsh 已安装且存在新版本时，出现在 dsh 已装版本卡片右侧。</summary>
        private Rectangle UpdateChipRect()
        {
            return new Rectangle(P(20) + P(292) - P(66), P(198) + P(25), P(54), P(24));
        }

        private bool ChipActive()
        {
            return _dshInstalled && _updateAvailable;
        }

        private void DrawUpdateChip(Graphics g)
        {
            if (!ChipActive()) return;
            Rectangle r = UpdateChipRect();
            using (GraphicsPath gp = Theme.RoundedRect(r, 12))
            using (SolidBrush f = new SolidBrush(_chipHover ? Theme.Accent : Theme.BgAlt))
            {
                g.FillPath(f, gp);
            }
            using (GraphicsPath gp = Theme.RoundedRect(r, 12))
            using (Pen pn = new Pen(_chipHover ? Theme.Accent : Theme.CardBorder))
            {
                g.DrawPath(pn, gp);
            }
            using (SolidBrush t = new SolidBrush(_chipHover ? Color.White : Theme.Text))
            {
                g.DrawString("更新", _fCaption, t, r, _centerFmt);
            }
        }

        private void DrawCard(Graphics g, int x, int y, string caption, string value, string installLabel, bool hover)
        {
            Rectangle r = new Rectangle(x, y, P(292), P(72));
            using (GraphicsPath gp = Theme.RoundedRect(r, 10))
            using (SolidBrush f = new SolidBrush(Theme.Card))
            {
                g.FillPath(f, gp);
            }
            using (GraphicsPath gp = Theme.RoundedRect(r, 10))
            using (Pen pn = new Pen(hover ? Theme.Accent : Theme.CardBorder, hover ? 2f : 1f))
            {
                g.DrawPath(pn, gp);
            }
            using (SolidBrush m = new SolidBrush(Theme.TextMuted))
            {
                g.DrawString(caption, _fCaption, m, x + P(16), y + P(10));
            }
            if (installLabel != null)
            {
                // 一键安装入口：可安装 = 强调色；被门槛挡住 = 灰色
                bool gated = installLabel == "需先安装 Node.js";
                Color c = gated ? Theme.TextMuted : (hover ? Color.White : Theme.Accent);
                using (SolidBrush t = new SolidBrush(c))
                {
                    g.DrawString(installLabel + (gated ? "" : " ▶"), _fValue, t, x + P(16), y + P(34));
                }
            }
            else
            {
                using (SolidBrush t = new SolidBrush(Theme.Text))
                {
                    g.DrawString(value, _fValue, t, x + P(16), y + P(36));
                }
            }
        }

        private void DrawButton(Graphics g, Btn b)
        {
            Theme.PaintButton(g, b.Rect, b.Variant, b.Text, _fBtn, b.Enabled, b.Hover, b.Down);
            if (b.Glyph != GlyphKind.None)
            {
                // 图标绘制：颜色跟随按钮前景色；挖孔颜色 = 按钮当前填充色
                Color fc = b.Enabled ? Theme.Text : Theme.TextMuted;
                Color hole = Theme.Disabled;
                if (b.Enabled)
                {
                    if (b.Variant == ButtonVariant.Primary) hole = (b.Down || b.Hover) ? Theme.AccentDark : Theme.Accent;
                    else if (b.Variant == ButtonVariant.Danger) hole = b.Down ? Color.FromArgb(56, 32, 34) : (b.Hover ? Color.FromArgb(66, 36, 38) : Color.FromArgb(46, 30, 33));
                    else hole = b.Down ? Theme.Disabled : (b.Hover ? Theme.Hover : Theme.BgAlt);
                }
                DrawGlyph(g, b, fc, hole);
            }
        }

        private void DrawGlyph(Graphics g, Btn b, Color fc, Color hole)
        {
            Rectangle r = b.Rect;
            float cx = r.X + r.Width / 2f;
            float cy = r.Y + r.Height / 2f;
            switch (b.Glyph)
            {
                case GlyphKind.Play: // ▶
                    {
                        float s = P(8f);
                        PointF[] tri = new PointF[] {
                            new PointF(cx - s * 0.8f, cy - s),
                            new PointF(cx - s * 0.8f, cy + s),
                            new PointF(cx + s * 1.1f, cy) };
                        using (SolidBrush br = new SolidBrush(fc)) g.FillPolygon(br, tri);
                        break;
                    }
                case GlyphKind.Stop: // ■
                    {
                        float s = P(8f);
                        RectangleF sq = new RectangleF(cx - s, cy - s, s * 2f, s * 2f);
                        using (GraphicsPath gp = Theme.RoundedRect(new Rectangle((int)sq.X, (int)sq.Y, (int)sq.Width, (int)sq.Height), P(3)))
                        using (SolidBrush br = new SolidBrush(fc)) g.FillPath(br, gp);
                        break;
                    }
                case GlyphKind.Restart: // ⟳
                    {
                        float r0 = P(9f);
                        RectangleF arc = new RectangleF(cx - r0, cy - r0, r0 * 2f, r0 * 2f);
                        using (Pen pen = new Pen(fc, P(2.4f)))
                        {
                            pen.StartCap = LineCap.Round;
                            pen.EndCap = LineCap.Round;
                            g.DrawArc(pen, arc, -60f, 270f);
                        }
                        // 箭头（弧末端 210°，沿顺时针切线方向）
                        float a1 = 210f * (float)Math.PI / 180f;
                        PointF tip = new PointF(cx + r0 * (float)Math.Cos(a1), cy + r0 * (float)Math.Sin(a1));
                        float ta = 120f * (float)Math.PI / 180f;
                        PointF d = new PointF((float)Math.Cos(ta), (float)Math.Sin(ta));
                        PointF side = new PointF(-d.Y, d.X);
                        float asz = P(4.2f);
                        PointF[] tri = new PointF[] {
                            tip,
                            new PointF(tip.X + d.X * asz + side.X * asz, tip.Y + d.Y * asz + side.Y * asz),
                            new PointF(tip.X + d.X * asz - side.X * asz, tip.Y + d.Y * asz - side.Y * asz) };
                        using (SolidBrush br = new SolidBrush(fc)) g.FillPolygon(br, tri);
                        break;
                    }
                case GlyphKind.Open: // ↗ 外链箭头
                    {
                        using (Pen pen = new Pen(fc, P(2.2f)))
                        {
                            pen.StartCap = LineCap.Round;
                            pen.EndCap = LineCap.Round;
                            g.DrawLine(pen, cx - P(8), cy + P(8), cx + P(4), cy - P(4));
                            g.DrawLine(pen, cx + P(4), cy - P(4), cx + P(9), cy - P(9));
                            g.DrawLine(pen, cx + P(4), cy - P(4), cx + P(1), cy - P(9));
                        }
                        break;
                    }
                case GlyphKind.Gear: // ⚙ 齿轮
                    {
                        float rr = P(7f);
                        float tW = P(3.2f), tL = P(4.5f);
                        using (SolidBrush br = new SolidBrush(fc))
                        {
                            for (int i = 0; i < 8; i++)
                            {
                                using (GraphicsPath tp = new GraphicsPath())
                                {
                                    RectangleF tooth = new RectangleF(cx - tW / 2f, cy - rr - tL, tW, tL + rr * 0.25f);
                                    tp.AddRectangle(tooth);
                                    using (Matrix m = new Matrix())
                                    {
                                        m.RotateAt(i * 45f, new PointF(cx, cy));
                                        tp.Transform(m);
                                        g.FillPath(br, tp);
                                    }
                                }
                            }
                            g.FillEllipse(br, cx - rr, cy - rr, rr * 2f, rr * 2f);
                        }
                        float hh = P(2.6f);
                        using (SolidBrush hb = new SolidBrush(hole))
                        {
                            g.FillEllipse(hb, cx - hh, cy - hh, hh * 2f, hh * 2f);
                        }
                        break;
                    }
            }
        }

        // ---------- 鼠标交互 ----------

        private Btn HitTest(Point pt)
        {
            for (int i = _btns.Count - 1; i >= 0; i--)
            {
                if (_btns[i].Rect.Contains(pt)) return _btns[i];
            }
            return null;
        }

        // 卡片命中：0=npm 1=node 2=dsh已装 3=dsh最新；非卡片返回 -1
        private int CardAt(Point pt)
        {
            Rectangle[] r = new Rectangle[] {
                new Rectangle(P(20), P(114), P(292), P(72)),
                new Rectangle(P(328), P(114), P(292), P(72)),
                new Rectangle(P(20), P(198), P(292), P(72)),
                new Rectangle(P(328), P(198), P(292), P(72)) };
            for (int i = 0; i < 4; i++) if (r[i].Contains(pt)) return i;
            return -1;
        }

        // 卡片是否处于"一键安装"可点状态
        private bool CardClickable(int idx)
        {
            switch (idx)
            {
                case 0: return !_npmOk;
                case 1: return !_nodeOk;
                case 2: return !_dshInstalled;
                default: return false;
            }
        }

        private void FireCard(int idx)
        {
            switch (idx)
            {
                case 0:
                case 1:
                    InstallNodeAsync();
                    break;
                case 2:
                    if (_npmOk && _nodeOk)
                    {
                        UpdateAsync(); // 安装/更新 dsh
                    }
                    else
                    {
                        AppendLog("请先安装 Node.js：点击「npm 版本」卡片的一键安装。");
                        RefreshStatusAsync();
                    }
                    break;
            }
        }

        protected override void OnMouseMove(MouseEventArgs e)
        {
            base.OnMouseMove(e);
            bool changed = false;
            bool hand = false;
            foreach (Btn b in _btns)
            {
                bool h = b.Enabled && b.Rect.Contains(e.Location);
                if (b.Hover != h) { b.Hover = h; changed = true; }
                if (h) hand = true;
            }
            // "更新"徽标悬停（优先于卡片安装逻辑，二者互斥）
            bool ch = ChipActive() && UpdateChipRect().Contains(e.Location);
            if (ch != _chipHover) { _chipHover = ch; changed = true; }
            if (ch) hand = true;

            int ci = CardAt(e.Location);
            bool cardHover = CardClickable(ci);
            if (cardHover != (_cardHover >= 0))
            {
                _cardHover = cardHover ? ci : -1;
                changed = true;
            }
            else if (cardHover && _cardHover != ci)
            {
                _cardHover = ci;
                changed = true;
            }
            if (cardHover) hand = true;
            if (changed) Invalidate();
            Cursor = hand ? Cursors.Hand : Cursors.Default;
        }

        protected override void OnMouseDown(MouseEventArgs e)
        {
            base.OnMouseDown(e);
            if (e.Button == MouseButtons.Left)
            {
                if (ChipActive() && UpdateChipRect().Contains(e.Location))
                {
                    _chipDown = true;
                    Invalidate();
                    return;
                }
                int ci = CardAt(e.Location);
                if (CardClickable(ci))
                {
                    _cardDown = ci;
                    Invalidate();
                    return; // 卡片区按下：不触发拖动
                }
                Btn hit = HitTest(e.Location);
                if (hit != null)
                {
                    hit.Down = true;
                    Invalidate();
                }
                else if (e.Y < P(64))
                {
                    NativeDrag(); // 标题栏拖动
                }
            }
        }

        protected override void OnMouseUp(MouseEventArgs e)
        {
            base.OnMouseUp(e);
            if (e.Button == MouseButtons.Left)
            {
                if (_chipDown)
                {
                    _chipDown = false;
                    if (ChipActive() && UpdateChipRect().Contains(e.Location)) UpdateAsync();
                    Invalidate();
                    return;
                }
                if (_cardDown >= 0)
                {
                    int d = _cardDown;
                    _cardDown = -1;
                    int ci = CardAt(e.Location);
                    if (ci == d && CardClickable(ci)) FireCard(d);
                    Invalidate();
                    return;
                }
                Btn hit = HitTest(e.Location);
                foreach (Btn b in _btns)
                {
                    if (b.Down)
                    {
                        b.Down = false;
                        if (hit == b && b.Enabled && b.OnClick != null) b.OnClick();
                    }
                }
                Invalidate();
            }
        }

        protected override void OnMouseLeave(EventArgs e)
        {
            base.OnMouseLeave(e);
            bool changed = false;
            foreach (Btn b in _btns)
            {
                if (b.Hover) { b.Hover = false; changed = true; }
                if (b.Down) { b.Down = false; changed = true; }
            }
            if (_cardHover >= 0 || _cardDown >= 0) { _cardHover = -1; _cardDown = -1; changed = true; }
            if (_chipHover || _chipDown) { _chipHover = false; _chipDown = false; changed = true; }
            if (changed) Invalidate();
            Cursor = Cursors.Default;
        }

        // ---------- 状态与按钮 ----------

        private void UpdateButtons()
        {
            bool idle = !_busy;
            // 启停合并：停止时=启动（蓝+▶），运行时=停止（红+■）
            _bToggle.Enabled = idle;
            _bToggle.Variant = _running ? ButtonVariant.Danger : ButtonVariant.Primary;
            _bToggle.Glyph = _running ? GlyphKind.Stop : GlyphKind.Play;
            _bRestart.Enabled = idle && _running;
            _bOpen.Enabled = idle && _running;
            _bRefresh.Enabled = idle;
            _bSettings.Enabled = idle;
            Invalidate();
        }

        private void SetBusy(bool busy)
        {
            _busy = busy;
            UpdateButtons();
        }

        private void SetStatus(StatusKind kind, string main, string sub)
        {
            _kind = kind;
            _statusMain = main;
            _statusSub = sub;
            _running = kind == StatusKind.Running;
            UpdateButtons();
            Invalidate();
        }

        private void SetCard(string card, string value)
        {
            switch (card)
            {
                case "npm": _npmVer = value; break;
                case "node": _nodeVer = value; break;
                case "inst": _instVer = value; break;
                case "latest": _latestVer = value; break;
            }
            Invalidate();
        }

        private void AppendLog(string line)
        {
            if (InvokeRequired)
            {
                try { BeginInvoke(new Action<string>(AppendLog), line); } catch { }
                return;
            }
            _logView.Append("[" + DateTime.Now.ToString("HH:mm:ss") + "] " + line);
        }

        // ---------- 动作 ----------

        private async void RefreshAllAsync()
        {
            if (_busy) return;
            SetBusy(true);
            AppendLog("---- 检测环境 ----");
            SetStatus(StatusKind.Checking, "检测中…", "");
            try
            {
                Task<string> tNpm = Task.Run<string>(new Func<string>(DshService.NpmVersion));
                Task<string> tNode = Task.Run<string>(new Func<string>(DshService.NodeVersion));
                Task<string> tInst = Task.Run<string>(new Func<string>(DshService.InstalledDshVersion));
                Task<string> tLatest = Task.Run<string>(new Func<string>(DshService.LatestDshVersion));
                await Task.WhenAll(new Task[] { tNpm, tNode, tInst, tLatest });

                string npm = tNpm.Result;
                string node = tNode.Result;
                string inst = tInst.Result;
                string latest = tLatest.Result;

                SetCard("npm", string.IsNullOrEmpty(npm) ? "无法获取" : npm);
                SetCard("node", string.IsNullOrEmpty(node) ? "无法获取" : node);
                SetCard("inst", string.IsNullOrEmpty(inst) ? "未安装" : inst);
                SetCard("latest", string.IsNullOrEmpty(latest) ? "无法获取" : latest);
                _npmOk = !string.IsNullOrEmpty(npm);
                _nodeOk = !string.IsNullOrEmpty(node);
                _dshInstalled = !string.IsNullOrEmpty(inst);
                _updateAvailable = IsNewerAvailable(inst, latest);
                AppendLog("npm " + (npm ?? "?") + " · node " + (node ?? "?")
                    + " · dsh 已装 " + (inst ?? "未安装") + " · 最新 " + (latest ?? "无法获取")
                    + (_updateAvailable ? " —— 有新版本可更新" : ""));
                if (!_npmOk || !_nodeOk) AppendLog("提示：未检测到 npm / Node.js，请先安装 Node.js（https://nodejs.org）。");
                else if (!_dshInstalled) AppendLog("提示：未检测到 dsh，可点击「更新 dsh」一键安装。");
            }
            catch (Exception ex)
            {
                AppendLog("检测出错：" + ex.Message);
            }
            SetBusy(false);
            RefreshStatusAsync();
        }

        private async void RefreshStatusAsync()
        {
            int port = _settings.Port;
            try
            {
                StatusResult r = await Task.Run<StatusResult>(delegate
                {
                    int pid;
                    bool ok = DshService.IsRunning(port, out pid);
                    return new StatusResult(ok, pid);
                });
                if (r.Running)
                {
                    SetStatus(StatusKind.Running, "dsh 正在运行", "http://127.0.0.1:" + port + "  ·  PID " + r.Pid);
                }
                else
                {
                    // 首次使用场景的明确指引
                    string sub;
                    if (!_npmOk || !_nodeOk)
                    {
                        sub = "未检测到 npm / Node.js，请先安装（nodejs.org）";
                    }
                    else if (!_dshInstalled)
                    {
                        sub = "未检测到 dsh，可点击「更新 dsh」一键安装";
                    }
                    else
                    {
                        sub = "端口 " + port + " 空闲，可点击「启动」";
                    }
                    SetStatus(StatusKind.Stopped, "dsh 未运行", sub);
                }
            }
            catch (Exception ex)
            {
                AppendLog("状态检测出错：" + ex.Message);
            }
        }

        private async void StartAsync()
        {
            if (_busy) return;
            SetBusy(true);
            AppendLog("---- 启动 dsh ----");
            SetStatus(StatusKind.Checking, "启动中…", "正在拉起 dsh web，请稍候");
            bool ok = false;
            try
            {
                ok = await Task.Run<bool>(delegate { return DshService.Start(_settings.Port, WorkDir(), WebLogPath(), AppendLog); });
            }
            catch (Exception ex)
            {
                AppendLog("启动出错：" + ex.Message);
            }
            SetBusy(false);
            RefreshStatusAsync();
            if (ok && _settings.AutoOpenBrowser) OpenUi();
        }

        private async void StopAsync()
        {
            if (_busy) return;
            SetBusy(true);
            AppendLog("---- 停止 dsh ----");
            SetStatus(StatusKind.Checking, "停止中…", "正在结束 dsh 进程并释放端口");
            try
            {
                await Task.Run<bool>(delegate { return DshService.Stop(_settings.Port, AppendLog); });
            }
            catch (Exception ex)
            {
                AppendLog("停止出错：" + ex.Message);
            }
            SetBusy(false);
            RefreshStatusAsync();
        }

        private async void RestartAsync()
        {
            if (_busy) return;
            SetBusy(true);
            AppendLog("---- 重启 dsh ----");
            SetStatus(StatusKind.Checking, "重启中…", "停止 → 启动");
            try
            {
                bool stopped = await Task.Run<bool>(delegate { return DshService.Stop(_settings.Port, AppendLog); });
                if (!stopped)
                {
                    AppendLog("停止未完成，取消重启。");
                }
                else
                {
                    bool started = await Task.Run<bool>(delegate { return DshService.Start(_settings.Port, WorkDir(), WebLogPath(), AppendLog); });
                    if (started && _settings.AutoOpenBrowser) OpenUi();
                }
            }
            catch (Exception ex)
            {
                AppendLog("重启出错：" + ex.Message);
            }
            SetBusy(false);
            RefreshStatusAsync();
        }

        private async void UpdateAsync()
        {
            if (_busy) return;
            SetBusy(true);
            AppendLog("---- 更新 dsh ----");
            bool ok = false;
            try
            {
                ok = await Task.Run<bool>(delegate { return DshService.Update(WorkDir(), UpdateLogPath(), AppendLog); });
            }
            catch (Exception ex)
            {
                AppendLog("更新出错：" + ex.Message);
            }
            AppendLog(ok ? "更新流程结束，重新检测版本…" : "更新未完成。");
            try
            {
                Task<string> tInst = Task.Run<string>(new Func<string>(DshService.InstalledDshVersion));
                Task<string> tLatest = Task.Run<string>(new Func<string>(DshService.LatestDshVersion));
                await Task.WhenAll(new Task[] { tInst, tLatest });
                string inst = tInst.Result;
                string latest = tLatest.Result;
                SetCard("inst", string.IsNullOrEmpty(inst) ? "未安装" : inst);
                SetCard("latest", string.IsNullOrEmpty(latest) ? "无法获取" : latest);
                _dshInstalled = !string.IsNullOrEmpty(inst);
                _updateAvailable = IsNewerAvailable(inst, latest);
                if (_updateAvailable) AppendLog("仍有新版本可用，可再次点击「更新 dsh」。");
                else if (!string.IsNullOrEmpty(inst)) AppendLog("已是最新版本：" + inst + "。");
                else AppendLog("未检测到已安装版本。");
            }
            catch (Exception ex)
            {
                AppendLog("版本检测出错：" + ex.Message);
            }
            SetBusy(false);
            RefreshStatusAsync();
        }

        /// <summary>一键安装便携 Node.js（含 npm）：自动下载最新 LTS 并解压到本地，免管理员。</summary>
        private async void InstallNodeAsync()
        {
            if (_busy) return;
            SetBusy(true);
            AppendLog("---- 安装 Node.js（便携版）----");
            SetStatus(StatusKind.Checking, "正在下载 Node.js…", "最新 LTS，约 30MB，请稍候");
            bool ok = false;
            try
            {
                ok = await Task.Run<bool>(delegate { return DshService.InstallPortableNode(AppendLog); });
            }
            catch (Exception ex)
            {
                AppendLog("安装 Node.js 出错：" + ex.Message);
            }
            AppendLog(ok ? "Node.js 安装完成，重新检测环境…" : "Node.js 安装未完成，请检查网络后重试。");
            SetBusy(false);
            RefreshAllAsync();
        }

        private void OpenUi()
        {
            try
            {
                Process.Start("http://127.0.0.1:" + _settings.Port);
            }
            catch (Exception ex)
            {
                AppendLog("打开浏览器失败：" + ex.Message);
            }
        }

        private void OpenSettings()
        {
            using (SettingsForm f = new SettingsForm(_settings))
            {
                if (f.ShowDialog(this) == DialogResult.OK)
                {
                    _settings.Port = f.Result.Port;
                    _settings.AutoOpenBrowser = f.Result.AutoOpenBrowser;
                    _settings.MinimizeToTray = f.Result.MinimizeToTray;
                    _settings.AutoCheckOnStart = f.Result.AutoCheckOnStart;
                    ConfigStore.Save(_settings);
                    AppendLog("设置已保存：端口 " + _settings.Port
                        + " · 自动开浏览器 " + (_settings.AutoOpenBrowser ? "开" : "关")
                        + " · 关闭最小化 " + (_settings.MinimizeToTray ? "开" : "关"));
                    RefreshStatusAsync();
                }
            }
        }

        // ---------- 托盘 / 窗口行为 ----------

        private void ShowBalloonOnce()
        {
            if (_balloonShown) return;
            _balloonShown = true;
            try { _tray.ShowBalloonTip(2000, "DSH 启动器", "已最小化到托盘，双击图标可重新打开。", ToolTipIcon.Info); } catch { }
        }

        private void ShowWindow()
        {
            if (InvokeRequired)
            {
                try { BeginInvoke(new Action(ShowWindow)); } catch { }
                return;
            }
            Show();
            WindowState = FormWindowState.Normal;
            Activate();
            BringToFront();
        }

        private void ExitApp()
        {
            _realExit = true;
            _tray.Visible = false;
            Application.Exit();
        }

        protected override void OnFormClosing(FormClosingEventArgs e)
        {
            if (!_realExit && _settings.MinimizeToTray && e.CloseReason == CloseReason.UserClosing)
            {
                e.Cancel = true;
                Hide();
                ShowBalloonOnce();
                return;
            }
            if (!_realExit && _tray != null) _tray.Visible = false;
            base.OnFormClosing(e);
        }

        protected override void OnLoad(EventArgs e)
        {
            base.OnLoad(e);
            Theme.ApplyRegion(this, 14);
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

        // ---------- 工具 ----------

        public DshSettings CurrentSettings
        {
            get { return _settings; }
        }

        private static string WorkDir()
        {
            return AppDomain.CurrentDomain.BaseDirectory;
        }

        private static string WebLogPath()
        {
            string dir = Path.Combine(WorkDir(), "logs");
            try { Directory.CreateDirectory(dir); } catch { }
            return Path.Combine(dir, "dsh-web.log");
        }

        private static string UpdateLogPath()
        {
            string dir = Path.Combine(WorkDir(), "logs");
            try { Directory.CreateDirectory(dir); } catch { }
            return Path.Combine(dir, "update.log");
        }

        private static bool IsNewerAvailable(string installed, string latest)
        {
            if (string.IsNullOrEmpty(latest)) return false;
            if (string.IsNullOrEmpty(installed)) return true;
            return !string.Equals(installed.Trim(), latest.Trim(), StringComparison.OrdinalIgnoreCase);
        }

        private class StatusResult
        {
            public bool Running;
            public int Pid;
            public StatusResult(bool running, int pid) { Running = running; Pid = pid; }
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
