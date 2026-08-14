using System;
using System.Collections.Generic;
using System.Drawing;
using System.Drawing.Drawing2D;
using System.Globalization;
using System.Windows.Forms;

namespace DshLauncher
{
    /// <summary>全局主题：配色、字体、圆角路径、自定义控件。</summary>
    public static class Theme
    {
        // 调色板（深色）
        public static readonly Color Bg        = Color.FromArgb(21, 23, 28);
        public static readonly Color BgAlt     = Color.FromArgb(27, 30, 37);
        public static readonly Color Card      = Color.FromArgb(30, 33, 41);
        public static readonly Color CardBorder= Color.FromArgb(46, 51, 62);
        public static readonly Color Accent    = Color.FromArgb(77, 107, 254);
        public static readonly Color AccentDark= Color.FromArgb(58, 86, 224);
        public static readonly Color Text      = Color.FromArgb(232, 235, 242);
        public static readonly Color TextMuted = Color.FromArgb(148, 155, 168);
        public static readonly Color Green     = Color.FromArgb(63, 185, 80);
        public static readonly Color Red       = Color.FromArgb(248, 81, 73);
        public static readonly Color Amber     = Color.FromArgb(222, 165, 49);
        public static readonly Color LogBg     = Color.FromArgb(13, 15, 20);
        public static readonly Color LogText   = Color.FromArgb(178, 188, 203);
        public static readonly Color Hover     = Color.FromArgb(52, 57, 68);
        public static readonly Color Disabled  = Color.FromArgb(34, 37, 44);

        /// <summary>主题字体（微软雅黑 UI，缩放由 AutoScale 处理）。</summary>
        public static Font Font(float size, FontStyle style)
        {
            return new Font("Microsoft YaHei UI", size, style);
        }

        /// <summary>圆角矩形路径。</summary>
        public static GraphicsPath RoundedRect(Rectangle r, int radius)
        {
            GraphicsPath gp = new GraphicsPath();
            int d = radius * 2;
            if (d > r.Width) d = r.Width;
            if (d > r.Height) d = r.Height;
            gp.AddArc(r.X, r.Y, d, d, 180, 90);
            gp.AddArc(r.Right - d, r.Y, d, d, 270, 90);
            gp.AddArc(r.Right - d, r.Bottom - d, d, d, 0, 90);
            gp.AddArc(r.X, r.Bottom - d, d, d, 90, 90);
            gp.CloseFigure();
            return gp;
        }

        /// <summary>给控件设置圆角 Region（像素单位，需在缩放后调用）。</summary>
        public static void ApplyRegion(Control c, int radius)
        {
            Rectangle r = c.ClientRectangle;
            if (r.Width <= 0 || r.Height <= 0) return;
            using (GraphicsPath gp = RoundedRect(new Rectangle(0, 0, r.Width - 1, r.Height - 1), radius))
            {
                c.Region = new Region(gp);
            }
        }

        /// <summary>统一按钮绘制（主窗口与设置对话框共用）：圆角填充 + 边框 + 居中灰度抗锯齿文字。</summary>
        public static void PaintButton(Graphics g, Rectangle r, ButtonVariant variant, string text, Font font,
            bool enabled, bool hover, bool down)
        {
            g.SmoothingMode = SmoothingMode.AntiAlias;
            g.TextRenderingHint = System.Drawing.Text.TextRenderingHint.AntiAlias;

            Color fill;
            if (!enabled)
            {
                fill = Theme.Disabled;
            }
            else if (variant == ButtonVariant.Primary)
            {
                fill = (down || hover) ? Theme.AccentDark : Theme.Accent;
            }
            else if (variant == ButtonVariant.Danger)
            {
                fill = down ? Color.FromArgb(56, 32, 34) : (hover ? Color.FromArgb(66, 36, 38) : Color.FromArgb(46, 30, 33));
            }
            else
            {
                fill = down ? Theme.Disabled : (hover ? Theme.Hover : Theme.BgAlt);
            }

            Rectangle rr = new Rectangle(r.X, r.Y, r.Width - 1, r.Height - 1);
            using (GraphicsPath gp = Theme.RoundedRect(rr, 8))
            using (SolidBrush fb = new SolidBrush(fill))
            {
                g.FillPath(fb, gp);
            }
            if (enabled)
            {
                if (variant == ButtonVariant.Danger)
                {
                    using (GraphicsPath gp = Theme.RoundedRect(rr, 8))
                    using (Pen pn = new Pen(Theme.Red))
                    {
                        g.DrawPath(pn, gp);
                    }
                }
                else if (variant == ButtonVariant.Secondary)
                {
                    using (GraphicsPath gp = Theme.RoundedRect(rr, 8))
                    using (Pen pn = new Pen(Theme.CardBorder))
                    {
                        g.DrawPath(pn, gp);
                    }
                }
            }

            Color fc = enabled ? Theme.Text : Theme.TextMuted;
            using (StringFormat sf = new StringFormat())
            {
                sf.Alignment = StringAlignment.Center;
                sf.LineAlignment = StringAlignment.Center;
                using (SolidBrush tb = new SolidBrush(fc))
                {
                    g.DrawString(text, font, tb, r, sf);
                }
            }
        }
    }

    public enum ButtonVariant { Primary, Secondary, Danger, Ghost }

    /// <summary>抗锯齿文本标签：深色主题下强制灰度抗锯齿，避免 ClearType 亚像素彩色毛边。</summary>
    public class GLabel : Label
    {
        public GLabel()
        {
            SetStyle(ControlStyles.AllPaintingInWmPaint | ControlStyles.OptimizedDoubleBuffer, true);
        }

        protected override void OnPaint(PaintEventArgs e)
        {
            e.Graphics.TextRenderingHint = System.Drawing.Text.TextRenderingHint.AntiAlias;
            base.OnPaint(e);
        }
    }

    /// <summary>抗锯齿复选框（文字与勾选框在深色下保持一致渲染）。</summary>
    public class GCheckBox : CheckBox
    {
        protected override void OnPaint(PaintEventArgs e)
        {
            e.Graphics.TextRenderingHint = System.Drawing.Text.TextRenderingHint.AntiAlias;
            base.OnPaint(e);
        }
    }

    /// <summary>圆角扁平按钮，带 hover / 按下 / 禁用态。</summary>
    public class FlatButton : Button
    {
        private bool _hover;
        private bool _down;

        public ButtonVariant Variant { get; set; }

        public FlatButton()
        {
            FlatStyle = FlatStyle.Flat;
            FlatAppearance.BorderSize = 0;
            FlatAppearance.MouseOverBackColor = Color.Transparent;
            FlatAppearance.MouseDownBackColor = Color.Transparent;
            Cursor = Cursors.Hand;
            Font = Theme.Font(10f, FontStyle.Regular);
            ForeColor = Theme.Text;
            BackColor = Theme.Bg; // 圆角路径外的角落显示窗口背景色，hover 时不会出现"缺角"
            TabStop = false;
            SetStyle(ControlStyles.AllPaintingInWmPaint | ControlStyles.OptimizedDoubleBuffer | ControlStyles.UserPaint, true);
            UpdateStyles();
        }

        protected override void OnMouseEnter(EventArgs e) { _hover = true; Invalidate(); base.OnMouseEnter(e); }
        protected override void OnMouseLeave(EventArgs e) { _hover = false; _down = false; Invalidate(); base.OnMouseLeave(e); }
        protected override void OnMouseDown(MouseEventArgs e) { _down = true; Invalidate(); base.OnMouseDown(e); }
        protected override void OnMouseUp(MouseEventArgs e) { _down = false; Invalidate(); base.OnMouseUp(e); }

        protected override void OnPaint(PaintEventArgs pe)
        {
            Graphics g = pe.Graphics;
            g.SmoothingMode = SmoothingMode.AntiAlias;
            g.TextRenderingHint = System.Drawing.Text.TextRenderingHint.AntiAlias;
            Rectangle r = new Rectangle(0, 0, Width - 1, Height - 1);
            using (GraphicsPath gp = Theme.RoundedRect(r, 8))
            {
                Color fill;
                if (!Enabled)
                {
                    fill = Theme.Disabled;
                }
                else if (Variant == ButtonVariant.Primary)
                {
                    fill = _down ? Theme.AccentDark : (_hover ? Theme.AccentDark : Theme.Accent);
                }
                else if (Variant == ButtonVariant.Danger)
                {
                    fill = _down ? Color.FromArgb(56, 32, 34) : (_hover ? Color.FromArgb(66, 36, 38) : Color.FromArgb(46, 30, 33));
                }
                else
                {
                    fill = _down ? Theme.Disabled : (_hover ? Theme.Hover : Theme.BgAlt);
                }
                using (SolidBrush b = new SolidBrush(fill)) g.FillPath(b, gp);

                if (Enabled)
                {
                    if (Variant == ButtonVariant.Danger)
                    {
                        using (Pen p = new Pen(Theme.Red)) g.DrawPath(p, gp);
                    }
                    else if (Variant != ButtonVariant.Primary && Variant != ButtonVariant.Ghost)
                    {
                        using (Pen p = new Pen(Theme.CardBorder)) g.DrawPath(p, gp);
                    }
                }
            }
            Color fc = Enabled ? ForeColor : Theme.TextMuted;
            using (StringFormat sf = new StringFormat())
            {
                sf.Alignment = StringAlignment.Center;
                sf.LineAlignment = StringAlignment.Center;
                sf.Trimming = StringTrimming.EllipsisCharacter;
                sf.FormatFlags = StringFormatFlags.NoWrap;
                using (SolidBrush tb = new SolidBrush(fc))
                {
                    g.DrawString(Text, Font, tb, new RectangleF(0, 0, Width, Height), sf);
                }
            }
        }
    }

    /// <summary>信息卡片：小标题 + 大数值。</summary>
    public class Card : Panel
    {
        public Label CaptionLabel { get; private set; }
        public Label ValueLabel { get; private set; }

        public Card()
        {
            BackColor = Theme.Card;
            SetStyle(ControlStyles.AllPaintingInWmPaint | ControlStyles.OptimizedDoubleBuffer, true);

            CaptionLabel = new GLabel();
            CaptionLabel.AutoSize = false;
            CaptionLabel.Font = Theme.Font(9f, FontStyle.Regular);
            CaptionLabel.ForeColor = Theme.TextMuted;
            CaptionLabel.Text = "";

            ValueLabel = new GLabel();
            ValueLabel.AutoSize = false;
            ValueLabel.Font = Theme.Font(15f, FontStyle.Bold);
            ValueLabel.ForeColor = Theme.Text;
            ValueLabel.Text = "…";
            ValueLabel.TextAlign = ContentAlignment.MiddleLeft;

            Controls.Add(CaptionLabel);
            Controls.Add(ValueLabel);
        }

        protected override void OnResize(EventArgs e)
        {
            base.OnResize(e);
            CaptionLabel.SetBounds(16, 8, Width - 32, 18);
            ValueLabel.SetBounds(16, 26, Width - 32, 36); // 与标题标签不重叠，避免透明覆盖
        }

        protected override void OnPaint(PaintEventArgs pe)
        {
            base.OnPaint(pe);
            pe.Graphics.SmoothingMode = SmoothingMode.AntiAlias;
            Rectangle r = new Rectangle(0, 0, Width - 1, Height - 1);
            using (GraphicsPath gp = Theme.RoundedRect(r, 8))
            using (Pen p = new Pen(Theme.CardBorder))
            {
                pe.Graphics.DrawPath(p, gp);
            }
        }
    }

    /// <summary>
    /// 日志视图：自绘 + 灰度抗锯齿，避免 EDIT 控件在深色下的 ClearType 彩色毛边。
    /// 支持滚轮回看，追加时自动滚动到最新。
    /// </summary>
    public class LogView : Control
    {
        private readonly System.Collections.Generic.List<string> _lines = new System.Collections.Generic.List<string>();
        private int _offset; // 0 = 显示最新
        private const int MaxLines = 1500;

        public LogView()
        {
            BackColor = Theme.LogBg;
            ForeColor = Theme.LogText;
            Font = new Font("Consolas", 9f);
            SetStyle(ControlStyles.AllPaintingInWmPaint | ControlStyles.OptimizedDoubleBuffer | ControlStyles.UserPaint | ControlStyles.ResizeRedraw, true);
            UpdateStyles();
            TabStop = false;
        }

        /// <summary>追加一行（须在 UI 线程调用）。</summary>
        public void Append(string text)
        {
            if (string.IsNullOrEmpty(text)) return;
            string[] parts = text.Replace("\r\n", "\n").Split('\n');
            foreach (string p in parts)
            {
                if (p.Trim().Length > 0) _lines.Add(p);
            }
            if (_lines.Count > MaxLines) _lines.RemoveRange(0, _lines.Count - MaxLines);
            _offset = 0; // 自动滚到最新
            Invalidate();
        }

        protected override void OnPaint(PaintEventArgs e)
        {
            Graphics g = e.Graphics;
            g.Clear(BackColor);
            g.TextRenderingHint = System.Drawing.Text.TextRenderingHint.AntiAlias;

            float lh = Font.GetHeight(g) + 2f;
            int lhInt = Math.Max(12, (int)lh);
            int visible = Math.Max(1, Height / lhInt);
            int total = _lines.Count;
            int start = Math.Max(0, total - visible - _offset);
            int y = 3;
            for (int i = start; i < Math.Min(total, start + visible); i++)
            {
                using (SolidBrush b = new SolidBrush(ForeColor))
                {
                    g.DrawString(_lines[i], Font, b, 4f, y);
                }
                y += lhInt;
            }

            using (Pen p = new Pen(Theme.CardBorder))
            {
                g.DrawRectangle(p, 0, 0, Width - 1, Height - 1);
            }
        }

        protected override void OnMouseWheel(MouseEventArgs e)
        {
            int lh = Math.Max(12, (int)(Font.GetHeight() + 2f));
            int visible = Math.Max(1, Height / lh);
            int maxOffset = Math.Max(0, _lines.Count - visible);
            _offset = Math.Max(0, Math.Min(maxOffset, _offset + e.Delta / 120));
            Invalidate();
            base.OnMouseWheel(e);
        }
    }

    /// <summary>
    /// 官方 DeepSeek 鲸鱼剪影：直接使用 dsh-web-frontend 自带 favicon.svg 的 path 数据，
    /// 运行时用轻量 SVG path 解析器转成 GraphicsPath，任意尺寸 / 任意颜色渲染。
    /// 保持零依赖（不嵌入图片文件，只嵌入路径字符串）。
    /// </summary>
    public static class SvgWhale
    {
        public const string PathData =
            "M48.8354 10.0479C48.3232 9.79199 48.1025 10.2798 47.8032 10.5278C47.7007 10.6079 47.6143 10.7119 47.5273 10.8076C46.7793 11.624 45.9048 12.1597 44.7622 12.0957C43.0923 12 41.666 12.5356 40.4058 13.8398C40.1377 12.2319 39.2476 11.272 37.8926 10.6558C37.1836 10.3359 36.4668 10.0156 35.9702 9.31982C35.6235 8.82373 35.5293 8.27197 35.356 7.72754C35.2456 7.3999 35.1353 7.06396 34.7651 7.00781C34.3633 6.94385 34.2056 7.2876 34.0479 7.57568C33.418 8.75195 33.1733 10.0479 33.1973 11.3599C33.2524 14.312 34.4736 16.6641 36.8999 18.3359C37.1758 18.5278 37.2466 18.7197 37.1597 19C36.9946 19.5757 36.7974 20.1357 36.624 20.7119C36.5137 21.0801 36.3486 21.1597 35.9624 21C34.6309 20.4321 33.481 19.5918 32.4644 18.5757C30.7393 16.8721 29.1792 14.9917 27.2334 13.52C26.7764 13.1758 26.3193 12.856 25.8467 12.5518C23.8618 10.584 26.1069 8.96777 26.627 8.77588C27.1704 8.57568 26.8159 7.8877 25.0591 7.896C23.3022 7.90381 21.6953 8.50391 19.647 9.30371C19.3477 9.42383 19.0322 9.51172 18.7095 9.58398C16.8501 9.22363 14.9199 9.14355 12.9033 9.37598C9.10596 9.80762 6.07275 11.6396 3.84326 14.7681C1.16455 18.5278 0.53418 22.7998 1.30664 27.2559C2.11768 31.9521 4.46582 35.8398 8.07373 38.8799C11.8159 42.0322 16.1255 43.5762 21.041 43.2803C24.0269 43.104 27.3516 42.6963 31.1016 39.4561C32.0469 39.936 33.0396 40.1279 34.686 40.272C35.9546 40.3921 37.1758 40.208 38.1211 40.0078C39.6021 39.688 39.4995 38.2881 38.9639 38.0322C34.623 35.9678 35.5762 36.8081 34.71 36.1279C36.9155 33.4639 40.2402 30.6958 41.54 21.728C41.6426 21.0161 41.5557 20.5679 41.54 19.9917C41.5322 19.6396 41.6108 19.5039 42.0049 19.4639C43.0923 19.3359 44.1479 19.0317 45.1167 18.4878C47.9292 16.9199 49.064 14.3438 49.3315 11.2559C49.3711 10.7837 49.3237 10.2959 48.8354 10.0479ZM24.3262 37.8398C20.1196 34.4639 18.0791 33.3521 17.2358 33.3999C16.4482 33.4482 16.5898 34.3682 16.7632 34.9678C16.9443 35.5601 17.1812 35.9683 17.5117 36.4878C17.7402 36.832 17.8979 37.3442 17.2832 37.728C15.9282 38.584 13.5728 37.4399 13.4624 37.3838C10.7207 35.7358 8.42822 33.5601 6.81348 30.584C5.25342 27.7197 4.34766 24.6479 4.19775 21.3677C4.1582 20.5757 4.38672 20.2959 5.15869 20.1519C6.17529 19.96 7.22314 19.9199 8.23926 20.0718C12.5327 20.7119 16.1885 22.6719 19.2529 25.7759C21.002 27.5439 22.3252 29.6558 23.6885 31.7202C25.1377 33.9121 26.6978 36 28.6831 37.7119C29.3843 38.312 29.9434 38.7681 30.479 39.104C28.8643 39.2881 26.1699 39.3281 24.3262 37.8398ZM26.3433 24.6001C26.3433 24.248 26.6191 23.9678 26.9658 23.9678C27.0444 23.9678 27.1152 23.9839 27.1782 24.0078C27.2651 24.04 27.3438 24.0879 27.4067 24.1602C27.5171 24.272 27.5801 24.4321 27.5801 24.6001C27.5801 24.9521 27.3042 25.2319 26.9575 25.2319C26.6108 25.2319 26.3433 24.9521 26.3433 24.6001ZM32.6064 27.8799C32.2046 28.0479 31.8027 28.1919 31.4165 28.208C30.8179 28.2397 30.1641 27.9922 29.8096 27.688C29.2583 27.2158 28.8643 26.9521 28.6987 26.1279C28.6279 25.7759 28.6675 25.2319 28.7305 24.9199C28.8721 24.248 28.7144 23.8159 28.2495 23.4238C27.8716 23.104 27.3911 23.0161 26.8633 23.0161C26.666 23.0161 26.4849 22.9277 26.3511 22.856C26.1304 22.7441 25.9492 22.4639 26.1226 22.1201C26.1777 22.0078 26.4458 21.7358 26.5088 21.688C27.2256 21.272 28.0527 21.4077 28.8169 21.7197C29.5259 22.0161 30.0615 22.5601 30.834 23.3281C31.6216 24.2559 31.7632 24.5117 32.2124 25.208C32.5669 25.752 32.8901 26.312 33.1104 26.9521C33.2446 27.3521 33.0713 27.6802 32.6064 27.8799Z";

        private static GraphicsPath _base;

        private static GraphicsPath Base()
        {
            if (_base == null) _base = Parse(PathData);
            return _base;
        }

        /// <summary>解析 SVG path 数据（支持 M / C / Z，C 后连续三元组）。</summary>
        private static GraphicsPath Parse(string d)
        {
            List<string> t = Tokenize(d);
            GraphicsPath p = new GraphicsPath();
            p.FillMode = FillMode.Winding;
            int i = 0;
            float cx = 0, cy = 0;
            while (i < t.Count)
            {
                string tok = t[i];
                if (tok == "M")
                {
                    i++;
                    if (i + 1 >= t.Count) break;
                    cx = F(t[i++]);
                    cy = F(t[i++]);
                    p.StartFigure();
                }
                else if (tok == "C")
                {
                    i++;
                    while (i + 5 < t.Count)
                    {
                        float x1 = F(t[i]), y1 = F(t[i + 1]), x2 = F(t[i + 2]), y2 = F(t[i + 3]), x = F(t[i + 4]), y = F(t[i + 5]);
                        i += 6;
                        p.AddBezier(cx, cy, x1, y1, x2, y2, x, y);
                        cx = x;
                        cy = y;
                        if (i >= t.Count || IsLetter(t[i])) break;
                    }
                }
                else if (tok == "Z")
                {
                    p.CloseFigure();
                    i++;
                }
                else i++;
            }
            return p;
        }

        private static float F(string s)
        {
            float v;
            float.TryParse(s, NumberStyles.Float, CultureInfo.InvariantCulture, out v);
            return v;
        }

        private static bool IsLetter(string s)
        {
            return s.Length == 1 && ((s[0] >= 'A' && s[0] <= 'Z') || (s[0] >= 'a' && s[0] <= 'z'));
        }

        private static List<string> Tokenize(string d)
        {
            List<string> tokens = new List<string>();
            int i = 0;
            while (i < d.Length)
            {
                char c = d[i];
                if (char.IsWhiteSpace(c) || c == ',') { i++; continue; }
                if ((c >= 'A' && c <= 'Z') || (c >= 'a' && c <= 'z')) { tokens.Add(c.ToString()); i++; continue; }
                int start = i;
                if (c == '-' || c == '+') i++;
                while (i < d.Length && (char.IsDigit(d[i]) || d[i] == '.' || d[i] == 'e' || d[i] == 'E'
                    || ((d[i] == '-' || d[i] == '+') && i > start && (d[i - 1] == 'e' || d[i - 1] == 'E')))) i++;
                if (i == start) i++;
                tokens.Add(d.Substring(start, i - start));
            }
            return tokens;
        }

        /// <summary>在目标矩形内等比绘制官方鲸鱼。</summary>
        public static void Draw(Graphics g, RectangleF target, Color body)
        {
            GraphicsPath p = (GraphicsPath)Base().Clone();
            RectangleF b = p.GetBounds();
            float s = Math.Min(target.Width / b.Width, target.Height / b.Height);
            // 直接用构造参数组合缩放+平移（x'=s·x+tx），绕开 Matrix.Translate/Scale 的
            // Prepend/Append 顺序语义坑（实测两种重载都会组合出错误顺序）。
            float tx = target.X + (target.Width - b.Width * s) / 2f - b.X * s;
            float ty = target.Y + (target.Height - b.Height * s) / 2f - b.Y * s;
            Matrix m = new Matrix(s, 0f, 0f, s, tx, ty);
            p.Transform(m);
            using (SolidBrush brush = new SolidBrush(body))
            {
                g.FillPath(brush, p);
            }
            p.Dispose();
        }
    }
}

