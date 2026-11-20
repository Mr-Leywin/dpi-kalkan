using System;
using System.Collections.Generic;
using System.Drawing;
using System.Drawing.Drawing2D;
using System.Linq;
using System.Windows.Forms;
using static DPIKalkan.Theme;

namespace DPIKalkan
{
    static class UIx
    {
        public static Label L(string text, Font font, Color fore, int x, int y, int w, int h,
                              ContentAlignment align = ContentAlignment.TopLeft)
        {
            return new Label
            {
                Text = text, Font = font, ForeColor = fore, AutoSize = false, UseMnemonic = false,
                Bounds = R(x, y, w, h), TextAlign = align, BackColor = Theme.Bg,
            };
        }

        public static Label Glyph(string glyph, Font font, Color fore, int x, int y, int w, int h) =>
            L(glyph, font, fore, x, y, w, h, ContentAlignment.MiddleCenter);

        /// <summary>ListView başlığını temaya uygun çizer.</summary>
        public static ListView StyleList(ListView lv)
        {
            Theme.Style(lv);
            lv.OwnerDraw = true;
            lv.DrawColumnHeader += (s, e) =>
            {
                using (var b = new SolidBrush(GraySoft)) e.Graphics.FillRectangle(b, e.Bounds);
                using (var p = new Pen(Divider)) e.Graphics.DrawLine(p, e.Bounds.Left, e.Bounds.Bottom - 1, e.Bounds.Right, e.Bounds.Bottom - 1);
                TextRenderer.DrawText(e.Graphics, e.Header.Text, Caption, Rectangle.Inflate(e.Bounds, -D(6), 0), Muted,
                    TextFormatFlags.VerticalCenter | TextFormatFlags.EndEllipsis | TextFormatFlags.NoPadding);
            };
            lv.DrawItem += (s, e) => e.DrawDefault = true;
            lv.DrawSubItem += (s, e) => e.DrawDefault = true;
            return lv;
        }
    }

    /// <summary>Yuvarlatılmış köşeli kart.</summary>
    class Card : Panel
    {
        Color fill = Theme.Card;
        public Color Fill { get => fill; set { fill = value; foreach (Control c in Controls) Retint(c); Invalidate(); } }
        public Color Stroke = Border;
        public int Radius = D(12);

        public Card()
        {
            SetStyle(ControlStyles.UserPaint | ControlStyles.AllPaintingInWmPaint |
                     ControlStyles.OptimizedDoubleBuffer | ControlStyles.ResizeRedraw, true);
            BackColor = Bg;
        }

        void Retint(Control c)
        {
            if (!(c is TextBoxBase) && !(c is ComboBox) && !(c is ListBox) && !(c is ListView) && !(c is NumericUpDown) && !(c is Card))
                c.BackColor = fill;
            else if (c is Card cc) cc.BackColor = fill;
        }

        public T Add<T>(T c) where T : Control
        {
            Retint(c);
            Controls.Add(c);
            return c;
        }

        protected override void OnPaint(PaintEventArgs e)
        {
            var g = e.Graphics;
            g.Clear(BackColor);
            HQ(g);
            var r = new RectangleF(0.5f, 0.5f, Width - 1.5f, Height - 1.5f);
            using (var p = Round(r, Radius))
            using (var b = new SolidBrush(fill))
            using (var pen = new Pen(Stroke))
            {
                g.FillPath(b, p);
                g.DrawPath(pen, p);
            }
        }
    }

    class FlatBtn : Control
    {
        public enum Kinds { Primary, Secondary, Danger, Success, Warning, Ghost }
        Kinds kind = Kinds.Secondary;
        string glyph;
        bool hover, down;
        public int Radius = D(8);

        public Kinds Kind { get => kind; set { kind = value; Invalidate(); } }
        public string Glyph { get => glyph; set { glyph = value; Invalidate(); } }

        public FlatBtn()
        {
            SetStyle(ControlStyles.UserPaint | ControlStyles.AllPaintingInWmPaint | ControlStyles.OptimizedDoubleBuffer |
                     ControlStyles.ResizeRedraw | ControlStyles.SupportsTransparentBackColor, true);
            Cursor = Cursors.Hand;
            Font = Bold;
            TabStop = true;
            BackColor = Bg;
        }

        protected override void OnMouseEnter(EventArgs e) { hover = true; Invalidate(); base.OnMouseEnter(e); }
        protected override void OnMouseLeave(EventArgs e) { hover = false; down = false; Invalidate(); base.OnMouseLeave(e); }
        protected override void OnMouseDown(MouseEventArgs e) { if (e.Button == MouseButtons.Left) { down = true; Invalidate(); } base.OnMouseDown(e); }
        protected override void OnMouseUp(MouseEventArgs e) { down = false; Invalidate(); base.OnMouseUp(e); }
        protected override void OnEnabledChanged(EventArgs e) { Cursor = Enabled ? Cursors.Hand : Cursors.Default; Invalidate(); base.OnEnabledChanged(e); }
        protected override void OnTextChanged(EventArgs e) { Invalidate(); base.OnTextChanged(e); }
        protected override void OnGotFocus(EventArgs e) { Invalidate(); base.OnGotFocus(e); }
        protected override void OnLostFocus(EventArgs e) { Invalidate(); base.OnLostFocus(e); }

        protected override void OnKeyDown(KeyEventArgs e)
        {
            if (e.KeyCode == Keys.Enter || e.KeyCode == Keys.Space) OnClick(EventArgs.Empty);
            base.OnKeyDown(e);
        }

        (Color bg, Color fg, Color border) Colors()
        {
            if (!Enabled) return (kind == Kinds.Ghost ? BackColor : GraySoft, Gray, Color.Empty);
            Color baseC;
            switch (kind)
            {
                case Kinds.Primary: baseC = Accent; break;
                case Kinds.Danger: baseC = Red; break;
                case Kinds.Success: baseC = Green; break;
                case Kinds.Warning: baseC = Amber; break;
                case Kinds.Ghost:
                    {
                        Color gb = down ? Mix(BackColor, Theme.Text, 0.12f) : hover ? Mix(BackColor, Theme.Text, 0.06f) : BackColor;
                        return (gb, Readable(Accent, BackColor, 3.0), Color.Empty);
                    }
                default:
                    {
                        Color sb = down ? Pressed : hover ? Hover : Theme.Card;
                        return (sb, Theme.Text, Border);
                    }
            }
            Color fgc = On(baseC);
            Color dark = Lum(baseC) > 0.5 ? Color.Black : (IsDark ? Color.White : Color.Black);
            Color bg = down ? Mix(baseC, dark, 0.20f) : hover ? Mix(baseC, dark, 0.10f) : baseC;
            return (bg, fgc, Color.Empty);
        }

        protected override void OnPaint(PaintEventArgs e)
        {
            var g = e.Graphics;
            g.Clear(BackColor);
            HQ(g);
            var (bg, fg, border) = Colors();
            var r = new RectangleF(0.5f, 0.5f, Width - 1.5f, Height - 1.5f);
            using (var p = Round(r, Radius))
            using (var b = new SolidBrush(bg))
            {
                g.FillPath(b, p);
                if (border != Color.Empty) using (var pen = new Pen(border)) g.DrawPath(pen, p);
                if (Focused && ShowFocusCues)
                    using (var fp = new Pen(Color.FromArgb(140, Accent), 2f)) g.DrawPath(fp, p);
            }

            var flags = TextFormatFlags.NoPadding | TextFormatFlags.SingleLine | TextFormatFlags.VerticalCenter;
            Size ts = TextRenderer.MeasureText(g, Text, Font, Size.Empty, flags);
            int gw = 0, gap = 0;
            Font gf = Height >= D(44) ? IconM : IconS;
            if (!string.IsNullOrEmpty(glyph))
            {
                gw = TextRenderer.MeasureText(g, glyph, gf, Size.Empty, flags).Width;
                gap = string.IsNullOrEmpty(Text) ? 0 : D(8);
            }
            int total = gw + gap + ts.Width;
            int x = Math.Max(D(6), (Width - total) / 2);
            if (gw > 0)
            {
                TextRenderer.DrawText(g, glyph, gf, new Rectangle(x, 0, gw, Height + D(1)), fg, bg, flags);
                x += gw + gap;
            }
            TextRenderer.DrawText(g, Text, Font, new Rectangle(x, 0, Math.Min(ts.Width + D(2), Width - x - D(4)), Height), fg, bg, flags | TextFormatFlags.EndEllipsis);
        }
    }

    class Toggle : Control
    {
        bool on;
        public event EventHandler CheckedChanged;

        public bool Checked
        {
            get => on;
            set { if (on == value) return; on = value; Invalidate(); CheckedChanged?.Invoke(this, EventArgs.Empty); }
        }

        public void SetSilently(bool v) { on = v; Invalidate(); }

        public Toggle()
        {
            SetStyle(ControlStyles.UserPaint | ControlStyles.AllPaintingInWmPaint | ControlStyles.OptimizedDoubleBuffer |
                     ControlStyles.ResizeRedraw | ControlStyles.SupportsTransparentBackColor, true);
            Size = new Size(D(44), D(24));
            Cursor = Cursors.Hand;
            TabStop = true;
        }

        protected override void OnClick(EventArgs e) { if (Enabled) Checked = !Checked; base.OnClick(e); }
        protected override void OnEnabledChanged(EventArgs e) { Invalidate(); base.OnEnabledChanged(e); }
        protected override void OnKeyDown(KeyEventArgs e) { if (e.KeyCode == Keys.Space && Enabled) Checked = !Checked; base.OnKeyDown(e); }

        protected override void OnPaint(PaintEventArgs e)
        {
            var g = e.Graphics;
            g.Clear(BackColor);
            HQ(g);
            var r = new RectangleF(1, 1, Width - 2.5f, Height - 2.5f);
            Color track = on ? Accent : ToggleOff;
            if (!Enabled) track = Mix(track, BackColor, 0.5f);
            using (var p = Round(r, r.Height / 2))
            using (var b = new SolidBrush(track)) g.FillPath(b, p);
            float k = r.Height - D(6);
            float kx = on ? r.Right - k - D(3) : r.X + D(3);
            Color knob = on ? On(Accent) == Color.White ? Color.White : Color.FromArgb(30, 30, 30) : (IsDark ? Color.FromArgb(230, 230, 230) : Color.White);
            using (var sh = new SolidBrush(Color.FromArgb(40, 0, 0, 0))) g.FillEllipse(sh, kx, r.Y + D(3) + 1, k, k);
            using (var kb = new SolidBrush(knob)) g.FillEllipse(kb, kx, r.Y + D(3), k, k);
        }
    }

    /// <summary>Temaya uyan onay kutusu.</summary>
    class CheckX : Control
    {
        bool on, hover;
        public event EventHandler CheckedChanged;
        public bool Checked
        {
            get => on;
            set { if (on == value) return; on = value; Invalidate(); CheckedChanged?.Invoke(this, EventArgs.Empty); }
        }

        public CheckX()
        {
            SetStyle(ControlStyles.UserPaint | ControlStyles.AllPaintingInWmPaint | ControlStyles.OptimizedDoubleBuffer |
                     ControlStyles.ResizeRedraw | ControlStyles.Selectable, true);
            Cursor = Cursors.Hand;
            Font = Body;
            TabStop = true;
            Height = D(24);
        }

        protected override void OnClick(EventArgs e) { if (Enabled) { Focus(); Checked = !Checked; } base.OnClick(e); }
        protected override void OnKeyDown(KeyEventArgs e) { if (e.KeyCode == Keys.Space && Enabled) Checked = !Checked; base.OnKeyDown(e); }
        protected override void OnMouseEnter(EventArgs e) { hover = true; Invalidate(); base.OnMouseEnter(e); }
        protected override void OnMouseLeave(EventArgs e) { hover = false; Invalidate(); base.OnMouseLeave(e); }
        protected override void OnEnabledChanged(EventArgs e) { Invalidate(); base.OnEnabledChanged(e); }
        protected override void OnTextChanged(EventArgs e) { Invalidate(); base.OnTextChanged(e); }
        protected override void OnGotFocus(EventArgs e) { Invalidate(); base.OnGotFocus(e); }
        protected override void OnLostFocus(EventArgs e) { Invalidate(); base.OnLostFocus(e); }

        protected override void OnPaint(PaintEventArgs e)
        {
            var g = e.Graphics;
            g.Clear(BackColor);
            HQ(g);
            float s = D(17);
            var box = new RectangleF(D(1), (Height - s) / 2f, s, s);
            Color fill = on ? Accent : Input;
            Color stroke = on ? Accent : hover ? Accent : Mix(Border, Theme.Text, 0.25f);
            if (!Enabled) { fill = on ? Mix(Accent, BackColor, 0.5f) : GraySoft; stroke = Mix(stroke, BackColor, 0.5f); }
            using (var p = Round(box, D(4)))
            using (var b = new SolidBrush(fill))
            using (var pen = new Pen(stroke, 1.2f))
            {
                g.FillPath(b, p);
                g.DrawPath(pen, p);
                if (Focused && ShowFocusCues) using (var fp = new Pen(Color.FromArgb(120, Accent), 2f)) g.DrawPath(fp, p);
            }
            if (on)
                using (var pen = new Pen(On(Accent), Math.Max(1.6f, s * 0.12f)) { StartCap = LineCap.Round, EndCap = LineCap.Round, LineJoin = LineJoin.Round })
                    g.DrawLines(pen, new[]
                    {
                        new PointF(box.X + s * .25f, box.Y + s * .52f), new PointF(box.X + s * .43f, box.Y + s * .70f), new PointF(box.X + s * .76f, box.Y + s * .32f)
                    });
            TextRenderer.DrawText(g, Text, Font, new Rectangle((int)box.Right + D(8), 0, Width - (int)box.Right - D(8), Height),
                Enabled ? Theme.Text : Gray, BackColor, TextFormatFlags.VerticalCenter | TextFormatFlags.EndEllipsis | TextFormatFlags.NoPadding);
        }
    }

    /// <summary>Seçeneklerden birini seçtiren düğme grubu (sekme çubuğu olarak da kullanılır).</summary>
    class Segmented : Control
    {
        public readonly List<string> Items = new List<string>();
        int sel, hover = -1;
        public event EventHandler SelectedChanged;
        public int SelectedIndex
        {
            get => sel;
            set { if (sel == value) return; sel = value; Invalidate(); SelectedChanged?.Invoke(this, EventArgs.Empty); }
        }
        public void SetSilently(int i) { sel = i; Invalidate(); }

        public Segmented(params string[] items)
        {
            Items.AddRange(items);
            SetStyle(ControlStyles.UserPaint | ControlStyles.AllPaintingInWmPaint | ControlStyles.OptimizedDoubleBuffer | ControlStyles.ResizeRedraw, true);
            Font = Bold;
            Height = D(36);
            Cursor = Cursors.Hand;
        }

        int Hit(Point p) => Items.Count == 0 ? -1 : Math.Max(0, Math.Min(Items.Count - 1, (p.X - D(3)) * Items.Count / Math.Max(1, Width - D(6))));
        protected override void OnMouseMove(MouseEventArgs e) { int h = Hit(e.Location); if (h != hover) { hover = h; Invalidate(); } base.OnMouseMove(e); }
        protected override void OnMouseLeave(EventArgs e) { hover = -1; Invalidate(); base.OnMouseLeave(e); }
        protected override void OnMouseClick(MouseEventArgs e) { if (Enabled) SelectedIndex = Hit(e.Location); base.OnMouseClick(e); }

        protected override void OnPaint(PaintEventArgs e)
        {
            var g = e.Graphics;
            g.Clear(BackColor);
            HQ(g);
            var outer = new RectangleF(0.5f, 0.5f, Width - 1.5f, Height - 1.5f);
            Color track = Mix(BackColor, Theme.Text, IsDark ? 0.10f : 0.06f);
            using (var p = Round(outer, D(9))) using (var b = new SolidBrush(track)) g.FillPath(b, p);
            float w = (Width - D(6)) / (float)Math.Max(1, Items.Count);
            for (int i = 0; i < Items.Count; i++)
            {
                var r = new RectangleF(D(3) + i * w, D(3), w, Height - D(6));
                Color bg = track, fg = Muted;
                if (i == sel) { bg = Accent; fg = On(Accent); }
                else if (i == hover) { bg = Mix(track, Theme.Text, 0.07f); fg = Theme.Text; }
                if (bg != track)
                    using (var p = Round(r, D(7))) using (var b = new SolidBrush(bg)) g.FillPath(b, p);
                TextRenderer.DrawText(g, Items[i], Font, Rectangle.Round(r), Enabled ? fg : Gray, bg,
                    TextFormatFlags.HorizontalCenter | TextFormatFlags.VerticalCenter | TextFormatFlags.EndEllipsis | TextFormatFlags.NoPadding);
            }
        }
    }

    /// <summary>Koyu temada da doğru görünen açılır liste.</summary>
    class ThemedCombo : ComboBox
    {
        public ThemedCombo()
        {
            DropDownStyle = ComboBoxStyle.DropDownList;
            DrawMode = DrawMode.OwnerDrawFixed;
            ItemHeight = D(22);
            IntegralHeight = false;
            MaxDropDownItems = 16;
            Font = Body;
            Theme.Style(this);
        }

        protected override void OnDrawItem(DrawItemEventArgs e)
        {
            if (e.Index < 0) return;
            bool sel = (e.State & DrawItemState.Selected) != 0;
            bool edit = (e.State & DrawItemState.ComboBoxEdit) != 0;
            Color bg = sel && !edit ? Accent : Input;
            Color fg = sel && !edit ? On(Accent) : Theme.Text;
            using (var b = new SolidBrush(bg)) e.Graphics.FillRectangle(b, e.Bounds);
            TextRenderer.DrawText(e.Graphics, GetItemText(Items[e.Index]), Font, Rectangle.Inflate(e.Bounds, -D(4), 0), fg, bg,
                TextFormatFlags.VerticalCenter | TextFormatFlags.EndEllipsis | TextFormatFlags.NoPadding);
        }
    }

    /// <summary>Renkli satırlarla rapor gösteren salt okunur metin kutusu.</summary>
    class ReportBox : RichTextBox
    {
        public ReportBox()
        {
            ReadOnly = true;
            BorderStyle = BorderStyle.None;
            DetectUrls = false;
            ScrollBars = RichTextBoxScrollBars.Vertical;
            Font = Body;
            BackColor = Theme.Card;
            ForeColor = Theme.Text;
            Theme.StyleScroll(this);
            Cursor = Cursors.Default;
        }

        public void Out(string text, Color? color = null, Font font = null)
        {
            SelectionStart = TextLength;
            SelectionLength = 0;
            SelectionColor = color ?? Theme.Text;
            SelectionFont = font ?? Body;
            AppendText(text + "\n");
            ScrollToCaret();
        }

        public void Head(string text) => Out(text, Theme.Text, Big);
        public void Muted(string text) => Out(text, Theme.Muted, Small);
        public void Good(string text) => Out(text, GreenText, Bold);
        public void Warn(string text) => Out(text, AmberText);
        public void Bad(string text) => Out(text, RedText, Bold);

        public void Kv(string key, string value, Color? valueColor = null)
        {
            SelectionStart = TextLength;
            SelectionColor = Theme.Muted;
            SelectionFont = Body;
            AppendText(key + ": ");
            SelectionColor = valueColor ?? Theme.Text;
            SelectionFont = Bold;
            AppendText(value + "\n");
        }

        public void Blank() => Out("", null, Small);
    }

    /// <summary>İnce ilerleme çubuğu.</summary>
    class ProgressLine : Control
    {
        float value;
        public float Value { get => value; set { this.value = Math.Max(0, Math.Min(1, value)); Invalidate(); } }
        public ProgressLine()
        {
            SetStyle(ControlStyles.UserPaint | ControlStyles.AllPaintingInWmPaint | ControlStyles.OptimizedDoubleBuffer | ControlStyles.ResizeRedraw, true);
            Height = D(6);
        }
        protected override void OnPaint(PaintEventArgs e)
        {
            var g = e.Graphics;
            g.Clear(BackColor);
            HQ(g);
            var r = new RectangleF(0, 0, Width - 1, Height - 1);
            using (var p = Round(r, Height / 2f)) using (var b = new SolidBrush(GraySoft)) g.FillPath(b, p);
            if (value > 0)
                using (var p = Round(new RectangleF(0, 0, Math.Max(Height, (Width - 1) * value), Height - 1), Height / 2f))
                using (var b = new SolidBrush(Accent)) g.FillPath(b, p);
        }
    }

    class NavItem : Control
    {
        public string Glyph;
        public string Key;
        bool sel, hover;
        public bool Selected { get => sel; set { sel = value; Invalidate(); } }
        public int Badge;   // sorun sayısı

        public NavItem()
        {
            SetStyle(ControlStyles.UserPaint | ControlStyles.AllPaintingInWmPaint | ControlStyles.OptimizedDoubleBuffer |
                     ControlStyles.ResizeRedraw, true);
            Cursor = Cursors.Hand;
            Font = Body;
            Height = D(40);
        }

        protected override void OnMouseEnter(EventArgs e) { hover = true; Invalidate(); base.OnMouseEnter(e); }
        protected override void OnMouseLeave(EventArgs e) { hover = false; Invalidate(); base.OnMouseLeave(e); }

        protected override void OnPaint(PaintEventArgs e)
        {
            var g = e.Graphics;
            g.Clear(Sidebar);
            HQ(g);
            Color bg = sel ? SidebarSel : hover ? SidebarHover : Sidebar;
            if (bg != Sidebar)
            {
                using (var p = Round(new RectangleF(D(10), D(2), Width - D(20), Height - D(4)), D(8)))
                using (var b = new SolidBrush(bg)) g.FillPath(b, p);
            }
            if (sel)
            {
                using (var p = Round(new RectangleF(D(10), D(11), D(3), Height - D(22)), D(2)))
                using (var b = new SolidBrush(Readable(Accent, Sidebar, 2.5))) g.FillPath(b, p);
            }
            Color fg = sel ? SidebarText : Mix(SidebarText, Sidebar, 0.12f);
            var flags = TextFormatFlags.NoPadding | TextFormatFlags.SingleLine | TextFormatFlags.VerticalCenter;
            TextRenderer.DrawText(g, Glyph, IconS, new Rectangle(D(24), 0, D(24), Height), fg, bg, flags | TextFormatFlags.HorizontalCenter);
            TextRenderer.DrawText(g, Text, sel ? Bold : Body, new Rectangle(D(58), 0, Width - D(60), Height), fg, bg, flags);
            if (Badge > 0)
            {
                string t = Badge > 9 ? "9+" : Badge.ToString();
                var bx = new RectangleF(Width - D(44), (Height - D(18)) / 2f, D(22), D(18));
                using (var p = Round(bx, D(9))) using (var b = new SolidBrush(Amber)) g.FillPath(b, p);
                TextRenderer.DrawText(g, t, Caption, Rectangle.Round(bx), On(Amber), Amber,
                    TextFormatFlags.HorizontalCenter | TextFormatFlags.VerticalCenter | TextFormatFlags.NoPadding);
            }
        }
    }

    /// <summary>Ana sayfadaki büyük, durum renginde, nabız animasyonlu kalkan.</summary>
    class ShieldView : Control
    {
        EngineState state = EngineState.Stopped;
        readonly Timer timer = new Timer { Interval = 40 };
        float phase;

        public EngineState State
        {
            get => state;
            set
            {
                state = value;
                timer.Enabled = value == EngineState.Running || value == EngineState.Starting || value == EngineState.Stopping;
                Invalidate();
            }
        }

        public ShieldView()
        {
            SetStyle(ControlStyles.UserPaint | ControlStyles.AllPaintingInWmPaint | ControlStyles.OptimizedDoubleBuffer |
                     ControlStyles.ResizeRedraw | ControlStyles.SupportsTransparentBackColor, true);
            timer.Tick += (s, e) => { phase = (phase + (state == EngineState.Running ? 0.008f : 0.02f)) % 1f; Invalidate(); };
        }

        protected override void Dispose(bool disposing) { if (disposing) timer.Dispose(); base.Dispose(disposing); }

        protected override void OnPaint(PaintEventArgs e)
        {
            var g = e.Graphics;
            g.Clear(BackColor);
            HQ(g);
            float cx = Width / 2f, cy = Height / 2f;
            float R = Math.Min(Width, Height) / 2f - D(2);
            var (top, bottom, glyph) = IconArt.StateLook(state);
            Color accent = bottom;

            if (timer.Enabled)
            {
                for (int k = 0; k < 2; k++)
                {
                    float p = (phase + k * 0.5f) % 1f;
                    float rr = R * 0.66f + p * R * 0.34f;
                    int a = (int)((1 - p) * 70);
                    using (var pen = new Pen(Color.FromArgb(a, accent), D(2)))
                        g.DrawEllipse(pen, cx - rr, cy - rr, rr * 2, rr * 2);
                }
            }
            float r0 = R * 0.70f;
            using (var b = new SolidBrush(Color.FromArgb(IsDark ? 40 : 28, accent))) g.FillEllipse(b, cx - r0, cy - r0, r0 * 2, r0 * 2);
            float r1 = R * 0.58f;
            using (var b = new SolidBrush(Color.FromArgb(IsDark ? 40 : 30, accent))) g.FillEllipse(b, cx - r1, cy - r1, r1 * 2, r1 * 2);

            float sw = R * 0.78f, sh = R * 0.90f;
            var rect = new RectangleF(cx - sw / 2, cy - sh / 2 + D(2), sw, sh);
            using (var shadowPath = IconArt.ShieldPath(new RectangleF(rect.X, rect.Y + D(4), rect.Width, rect.Height)))
            using (var sb = new SolidBrush(Color.FromArgb(35, 0, 0, 0)))
                g.FillPath(sb, shadowPath);
            IconArt.DrawShield(g, rect, top, bottom, glyph);
        }
    }

    /// <summary>Başlık + açıklama + anahtar satırı (Ayarlar sayfası).</summary>
    class ToggleRow : Panel
    {
        public readonly Toggle Switch = new Toggle();
        public ToggleRow(string title, string desc, int width)
        {
            SetStyle(ControlStyles.OptimizedDoubleBuffer | ControlStyles.AllPaintingInWmPaint | ControlStyles.ResizeRedraw, true);
            Size = new Size(width, D(64));
            BackColor = Theme.Card;
            var t = new Label { Text = title, Font = Bold, ForeColor = Theme.Text, AutoSize = false, UseMnemonic = false, BackColor = Theme.Card,
                Bounds = new Rectangle(D(20), D(10), width - D(100), D(22)) };
            var d = new Label { Text = desc, Font = Small, ForeColor = Muted, AutoSize = false, UseMnemonic = false, BackColor = Theme.Card,
                Bounds = new Rectangle(D(20), D(31), width - D(100), D(32)) };
            Switch.Location = new Point(width - D(66), (Height - Switch.Height) / 2);
            Switch.BackColor = Theme.Card;
            Controls.Add(t); Controls.Add(d); Controls.Add(Switch);
            t.Click += (s, e) => { if (Switch.Enabled) Switch.Checked = !Switch.Checked; };
        }

        public bool Divider = true;

        protected override void OnPaint(PaintEventArgs e)
        {
            base.OnPaint(e);
            if (Divider)
                using (var pen = new Pen(Theme.Divider))
                    e.Graphics.DrawLine(pen, D(20), Height - 1, Width - D(20), Height - 1);
        }
    }

    abstract class PageBase : Panel
    {
        public abstract string Key { get; }
        public virtual void OnActivated() { }
        readonly List<Action> unsub = new List<Action>();

        protected PageBase()
        {
            SetStyle(ControlStyles.OptimizedDoubleBuffer | ControlStyles.AllPaintingInWmPaint, true);
            BackColor = Bg;
            Dock = DockStyle.Fill;
        }

        protected static bool Level(UiLevel min) => App.Settings.UiLevel >= min;

        /// <summary>Durum değişikliği aboneliği (sayfa yok edilince otomatik kaldırılır).</summary>
        protected void OnStatus(Action a)
        {
            Action safe = () => { if (!IsDisposed) a(); };
            App.StatusChanged += safe;
            unsub.Add(() => App.StatusChanged -= safe);
        }

        protected void OnLog(Action<LogEntry> a)
        {
            Action<LogEntry> safe = e => { if (!IsDisposed) a(e); };
            App.LogAdded += safe;
            unsub.Add(() => App.LogAdded -= safe);
        }

        protected void Track(IDisposable d) => unsub.Add(d.Dispose);

        protected override void Dispose(bool disposing)
        {
            if (disposing) { foreach (var u in unsub) { try { u(); } catch { } } unsub.Clear(); }
            base.Dispose(disposing);
        }

        protected void Header(string title, string subtitle)
        {
            Controls.Add(UIx.L(title, H1, Theme.Text, 28, 2, 600, 36));
            if (subtitle != null) Controls.Add(UIx.L(subtitle, Body, Muted, 29, 40, 760, 22));
        }

        protected Card AddCard(int x, int y, int w, int h)
        {
            var c = new Card { Bounds = R(x, y, w, h) };
            Controls.Add(c);
            return c;
        }

        protected FlatBtn Btn(string text, string glyph, FlatBtn.Kinds kind, int x, int y, int w, int h = 38)
        {
            var b = new FlatBtn { Text = text, Glyph = glyph, Kind = kind, Bounds = R(x, y, w, h), BackColor = Bg };
            Controls.Add(b);
            return b;
        }
    }

    /// <summary>Sayfa içinde kaydırılabilir alan.</summary>
    class ScrollArea : Panel
    {
        public ScrollArea()
        {
            AutoScroll = true;
            BackColor = Bg;
            SetStyle(ControlStyles.OptimizedDoubleBuffer, true);
            Theme.StyleScroll(this);
        }

        protected override Point ScrollToControl(Control activeControl) => DisplayRectangle.Location;   // odakta zıplamayı önle
    }
}
