using System;
using System.Collections.Generic;
using System.Drawing;
using System.Linq;
using System.Windows.Forms;
using static DPIKalkan.Theme;

namespace DPIKalkan
{
    /// <summary>Görünüm: hazır temalar, renk kodu girilebilen özel renk paleti ve arayüz düzeyi.</summary>
    sealed class AppearancePage : PageBase
    {
        public override string Key => "appearance";

        readonly ScrollArea scroll;
        readonly Dictionary<string, TextBox> hexBoxes = new Dictionary<string, TextBox>();
        readonly Timer applyTimer = new Timer { Interval = 450 };
        bool pendingApply;

        public AppearancePage()
        {
            Header("Görünüm", "Hazır bir tema seçin ya da her rengi kendiniz belirleyin. 'Sistem' teması Windows renklerini kullanır.");
            scroll = new ScrollArea { Bounds = R(0, 68, 820, 572) };
            Controls.Add(scroll);
            int y = 4;

            // ------------------------------------------------ arayüz düzeyi
            scroll.Controls.Add(UIx.L("ARAYÜZ DÜZEYİ", Caption, Muted, 30, y, 300, 20));
            y += 24;
            var lc = new Card { Bounds = R(28, y, 764, 96) };
            scroll.Controls.Add(lc);
            var seg = lc.Add(new Segmented("Basit", "Orta", "Detaylı") { Bounds = R(20, 16, 360, 36) });
            seg.SetSilently((int)App.Settings.UiLevel);
            var lvlDesc = lc.Add(UIx.L(LevelText(App.Settings.UiLevel), Small, Muted, 396, 12, 350, 76));
            seg.SelectedChanged += (s, e) =>
            {
                App.Settings.UiLevel = (UiLevel)seg.SelectedIndex;
                App.Save();
                App.Log("Arayüz düzeyi: " + MainForm.LevelName(App.Settings.UiLevel), LogLevel.Info);
                BeginInvoke(new Action(() => MainForm.Instance.Rebuild()));
            };
            lc.Add(UIx.L("Değişiklik hemen uygulanır.", Small, Muted, 20, 62, 360, 22));
            y += 96 + 22;

            // ------------------------------------------------ hazır temalar
            scroll.Controls.Add(UIx.L("HAZIR TEMALAR", Caption, Muted, 30, y, 300, 20));
            y += 24;
            var all = new List<Palette> { Palettes.System() };
            all.AddRange(Palettes.Presets);
            int cols = 4, tw = 182, th = 92, gap = 12;
            int rows = (all.Count + cols - 1) / cols;
            for (int i = 0; i < all.Count; i++)
            {
                var pal = all[i];
                var tile = new ThemeTile(pal, pal.Id == App.Settings.ThemeId && App.Settings.ThemeOverrides.Count == 0,
                                         pal.Id == App.Settings.ThemeId && App.Settings.ThemeOverrides.Count > 0)
                {
                    Bounds = R(28 + (i % cols) * (tw + gap), y + (i / cols) * (th + gap), tw, th), BackColor = Bg,
                };
                tile.Click += (s, e) => SelectTheme(pal.Id);
                scroll.Controls.Add(tile);
            }
            y += rows * (th + gap) + 14;

            // ------------------------------------------------ özel renkler
            scroll.Controls.Add(UIx.L("ÖZEL RENKLER", Caption, Muted, 30, y, 300, 20));
            scroll.Controls.Add(UIx.L("Renk kodu yazın (#2563EB, 2563EB, #FFF ya da 37,99,235) veya renk kutusuna tıklayıp seçin.", Small, Muted, 200, y, 590, 20));
            y += 24;
            int cardH = 16 + 6 * 44 + 8 + 38 + 16;
            var cc = new Card { Bounds = R(28, y, 764, cardH) };
            scroll.Controls.Add(cc);
            var cur = Current;
            for (int i = 0; i < Palette.Keys.Length; i++)
            {
                string key = Palette.Keys[i];
                int col = i % 2, row = i / 2;
                int x0 = 20 + col * 372, y0 = 16 + row * 44;
                var sw = cc.Add(new Swatch { Color = cur.Get(key), Bounds = R(x0, y0, 34, 30) });
                cc.Add(UIx.L(Palette.Labels[i], Body, Theme.Text, x0 + 44, y0 + 4, 200, 22, ContentAlignment.MiddleLeft));
                var tb = cc.Add(Theme.Style(new TextBox { Bounds = R(x0 + 248, y0 + 3, 96, 24), Font = Mono, Text = Palettes.Hex(cur.Get(key)), MaxLength = 20 }));
                hexBoxes[key] = tb;
                bool overridden = App.Settings.ThemeOverrides.Any(o => o.Key == key);
                if (overridden) tb.ForeColor = AccentInk;
                sw.Click += (s, e) =>
                {
                    using (var d = new ColorDialog { Color = sw.Color, FullOpen = true, AnyColor = true })
                        if (d.ShowDialog(FindForm()) == DialogResult.OK)
                        {
                            tb.Text = Palettes.Hex(d.Color);
                            SetColor(key, d.Color, true);
                        }
                };
                tb.KeyDown += (s, e) => { if (e.KeyCode == Keys.Enter) { e.SuppressKeyPress = true; Commit(key, tb, sw); } };
                tb.Leave += (s, e) => Commit(key, tb, sw);
                tb.TextChanged += (s, e) =>
                {
                    bool valid = Palettes.TryParseColor(tb.Text, out var c);
                    tb.BackColor = valid ? Input : Mix(Input, Red, 0.25f);
                    if (valid) sw.Color = c;
                };
            }
            int by = 16 + 6 * 44 + 8;
            var bReset = cc.Add(new FlatBtn { Text = "Özel renkleri sıfırla", Glyph = GUndo, Kind = FlatBtn.Kinds.Secondary, Bounds = R(20, by, 200, 38) });
            bReset.Enabled = App.Settings.ThemeOverrides.Count > 0;
            bReset.Click += (s, e) =>
            {
                App.Settings.ThemeOverrides.Clear();
                App.Save();
                MainForm.Instance.ApplyTheme();
            };
            var bCopy = cc.Add(new FlatBtn { Text = "Tema kodunu kopyala", Glyph = GCopy, Kind = FlatBtn.Kinds.Secondary, Bounds = R(230, by, 200, 38) });
            bCopy.Click += (s, e) =>
            {
                Clipboard.SetText(Current.ToCode());
                MessageBox.Show(FindForm(), "Tema kodu panoya kopyalandı. Arkadaşınız 'Tema kodunu yapıştır' ile aynı renkleri kullanabilir.", App.Name);
            };
            var bPaste = cc.Add(new FlatBtn { Text = "Tema kodunu yapıştır", Glyph = GPaste, Kind = FlatBtn.Kinds.Secondary, Bounds = R(440, by, 200, 38) });
            bPaste.Click += (s, e) => PasteCode();
            y += cardH + 20;

            scroll.Controls.Add(UIx.L("İpucu: Önce size en yakın hazır temayı seçin, sonra yalnızca değiştirmek istediğiniz renkleri düzenleyin. " +
                                      "Okunaklılık için düğme ve rozet yazı renkleri otomatik ayarlanır.", Small, Muted, 30, y, 760, 40));
            scroll.Controls.Add(new Panel { Bounds = R(0, y + 40, 10, 16), BackColor = Bg });

            applyTimer.Tick += (s, e) => { applyTimer.Stop(); if (pendingApply) { pendingApply = false; MainForm.Instance.ApplyTheme(); } };
            Track(applyTimer);
        }

        static string LevelText(UiLevel l)
        {
            switch (l)
            {
                case UiLevel.Basit: return "Basit: yalnızca gerekenler. Tek düğmeyle başlat, otomatik ayarla, sorun bul. Yeni başlayanlar için.";
                case UiLevel.Orta: return "Orta: profiller, İnternet Kontrolü, DNS ayarları ve hedef siteler de görünür.";
                default: return "Detaylı: her şey. Günlük, ağ bilgisi, komut satırları, betik dışa aktarma ve tüm gelişmiş ayarlar.";
            }
        }

        void SelectTheme(string id)
        {
            if (App.Settings.ThemeOverrides.Count > 0 && id != App.Settings.ThemeId &&
                MessageBox.Show(FindForm(), "Özel renkleriniz yeni temanın üzerine uygulanmaya devam etsin mi?\n\n'Hayır' derseniz özel renkler silinir.",
                    App.Name, MessageBoxButtons.YesNo, MessageBoxIcon.Question) == DialogResult.No)
                App.Settings.ThemeOverrides.Clear();
            App.Settings.ThemeId = id;
            App.Save();
            App.Log("Tema: " + id, LogLevel.Info);
            MainForm.Instance.ApplyTheme();
        }

        void Commit(string key, TextBox tb, Swatch sw)
        {
            if (!Palettes.TryParseColor(tb.Text, out var c)) { tb.Text = Palettes.Hex(Current.Get(key)); return; }
            tb.Text = Palettes.Hex(c);
            if (c.ToArgb() == Current.Get(key).ToArgb()) return;
            SetColor(key, c, false);
        }

        void SetColor(string key, Color c, bool now)
        {
            var list = App.Settings.ThemeOverrides;
            list.RemoveAll(o => o.Key == key);
            if (c.ToArgb() != Palettes.Base(App.Settings.ThemeId).Get(key).ToArgb())
                list.Add(new ThemeColor { Key = key, Hex = Palettes.Hex(c) });
            App.Save();
            pendingApply = true;
            applyTimer.Stop();
            applyTimer.Interval = now ? 50 : 450;
            applyTimer.Start();
        }

        void PasteCode()
        {
            string code = Clipboard.ContainsText() ? Clipboard.GetText() : "";
            var basePal = Palettes.Base(App.Settings.ThemeId);
            if (!Palette.TryParseCode(code, basePal, out var p))
            {
                MessageBox.Show(FindForm(), "Panoda geçerli bir tema kodu yok. Kod 'DKT1:' ile başlamalı.", App.Name, MessageBoxButtons.OK, MessageBoxIcon.Warning);
                return;
            }
            App.Settings.ThemeOverrides.Clear();
            foreach (var k in Palette.Keys)
                if (p.Get(k).ToArgb() != basePal.Get(k).ToArgb())
                    App.Settings.ThemeOverrides.Add(new ThemeColor { Key = k, Hex = Palettes.Hex(p.Get(k)) });
            App.Save();
            MainForm.Instance.ApplyTheme();
        }

        /// <summary>Tıklanabilir renk kutusu.</summary>
        sealed class Swatch : Control
        {
            Color color;
            public Color Color { get => color; set { color = value; Invalidate(); } }
            public Swatch()
            {
                SetStyle(ControlStyles.UserPaint | ControlStyles.AllPaintingInWmPaint | ControlStyles.OptimizedDoubleBuffer, true);
                Cursor = Cursors.Hand;
            }
            protected override void OnPaint(PaintEventArgs e)
            {
                var g = e.Graphics;
                g.Clear(BackColor);
                HQ(g);
                using (var p = Round(new RectangleF(0.5f, 0.5f, Width - 1.5f, Height - 1.5f), D(6)))
                using (var b = new SolidBrush(color))
                using (var pen = new Pen(Mix(Border, Theme.Text, 0.25f)))
                {
                    g.FillPath(b, p);
                    g.DrawPath(pen, p);
                }
            }
        }

        /// <summary>Tema önizleme kutusu: kenar çubuğu, kart ve vurgu rengini küçük ölçekte gösterir.</summary>
        sealed class ThemeTile : Control
        {
            readonly Palette p;
            readonly bool selected, modified;
            bool hover;
            public ThemeTile(Palette p, bool selected, bool modified)
            {
                this.p = p;
                this.selected = selected;
                this.modified = modified;
                SetStyle(ControlStyles.UserPaint | ControlStyles.AllPaintingInWmPaint | ControlStyles.OptimizedDoubleBuffer | ControlStyles.ResizeRedraw, true);
                Cursor = Cursors.Hand;
            }
            protected override void OnMouseEnter(EventArgs e) { hover = true; Invalidate(); base.OnMouseEnter(e); }
            protected override void OnMouseLeave(EventArgs e) { hover = false; Invalidate(); base.OnMouseLeave(e); }
            protected override void OnPaint(PaintEventArgs e)
            {
                var g = e.Graphics;
                g.Clear(BackColor);
                HQ(g);
                var outer = new RectangleF(1, 1, Width - 3, Height - 3);
                using (var path = Round(outer, D(10)))
                {
                    using (var b = new SolidBrush(p.Bg)) g.FillPath(b, path);
                    var st = g.Save();
                    g.SetClip(path);
                    float sideW = Width * 0.26f;
                    using (var b = new SolidBrush(p.Sidebar)) g.FillRectangle(b, outer.X, outer.Y, sideW, outer.Height);
                    using (var b = new SolidBrush(Mix(p.Sidebar, p.SidebarText, 0.5f)))
                        for (int i = 0; i < 3; i++) g.FillRectangle(b, outer.X + D(8), outer.Y + D(12) + i * D(10), sideW - D(16), D(4));
                    var card = new RectangleF(outer.X + sideW + D(8), outer.Y + D(8), outer.Width - sideW - D(16), D(38));
                    using (var cp = Round(card, D(5)))
                    using (var b = new SolidBrush(p.Card))
                    using (var pen = new Pen(p.Border)) { g.FillPath(b, cp); g.DrawPath(pen, cp); }
                    using (var b = new SolidBrush(p.Text)) g.FillRectangle(b, card.X + D(7), card.Y + D(8), card.Width * 0.55f, D(5));
                    using (var b = new SolidBrush(p.Muted)) g.FillRectangle(b, card.X + D(7), card.Y + D(18), card.Width * 0.75f, D(4));
                    using (var cp = Round(new RectangleF(card.X + D(7), card.Y + D(27), D(28), D(7)), D(3)))
                    using (var b = new SolidBrush(p.Accent)) g.FillPath(b, cp);
                    float dx = card.X + D(40);
                    foreach (var c in new[] { p.Green, p.Amber, p.Red })
                    {
                        using (var b = new SolidBrush(c)) g.FillEllipse(b, dx, card.Y + D(27), D(7), D(7));
                        dx += D(10);
                    }
                    g.Restore(st);
                    using (var pen = new Pen(selected || modified ? Accent : hover ? Mix(Border, Theme.Text, 0.35f) : Border, selected || modified ? 2.5f : 1f))
                        g.DrawPath(pen, path);
                }
                string name = p.Name + (modified ? " ✎" : "");
                var nameRect = new Rectangle((int)(Width * 0.26f) + D(8), Height - D(36), Width - (int)(Width * 0.26f) - D(12), D(30));
                TextRenderer.DrawText(g, name, selected || modified ? Bold : Small, nameRect, p.Text, p.Bg,
                    TextFormatFlags.VerticalCenter | TextFormatFlags.EndEllipsis | TextFormatFlags.NoPadding);
                if (selected || modified)
                {
                    var badge = new RectangleF(Width - D(26), D(6), D(18), D(18));
                    using (var b = new SolidBrush(Accent)) g.FillEllipse(b, badge);
                    TextRenderer.DrawText(g, GCheck, new Font(IconFontName, 7f), Rectangle.Round(badge), On(Accent),
                        TextFormatFlags.HorizontalCenter | TextFormatFlags.VerticalCenter | TextFormatFlags.NoPadding);
                }
            }
        }
    }
}
