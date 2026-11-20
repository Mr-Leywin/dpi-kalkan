using System;
using System.Collections.Generic;
using System.Drawing;
using System.Linq;
using System.Threading.Tasks;
using System.Windows.Forms;
using static DPIKalkan.Theme;

namespace DPIKalkan
{
    /// <summary>Sorun Giderme: tam kontrol (Sorunu Bul) ve hata kodları rehberi.</summary>
    sealed class TroublePage : PageBase
    {
        public override string Key => "trouble";

        readonly Segmented tabs;
        readonly Panel pCheck, pCodes;
        // sorunu bul
        readonly FlatBtn bRun;
        readonly Label runStatus;
        readonly ScrollArea rowsArea;
        int rowY;
        bool checking;
        // hata kodları
        readonly ThemedCombo cat;
        readonly TextBox search;
        readonly ListBox codes;
        readonly ReportBox info;
        readonly FlatBtn bFix1, bFix2, bCopy;

        public TroublePage()
        {
            Header("Sorun Giderme", "Çalışmama sebebini bulun. Her sorunun bir hata kodu, açıklaması ve çözüm adımları vardır.");
            tabs = new Segmented("Sorunu Bul", "Hata Kodları ve Çözümleri") { Bounds = R(28, 76, 420, 36), BackColor = Bg };
            Controls.Add(tabs);
            pCheck = new Panel { Bounds = R(0, 124, 820, 516), BackColor = Bg };
            pCodes = new Panel { Bounds = R(0, 124, 820, 516), BackColor = Bg, Visible = false };
            Controls.Add(pCheck);
            Controls.Add(pCodes);
            tabs.SelectedChanged += (s, e) => { pCheck.Visible = tabs.SelectedIndex == 0; pCodes.Visible = tabs.SelectedIndex == 1; };

            // ================================================ sorunu bul
            bRun = new FlatBtn { Text = "Tam kontrolü başlat", Glyph = GSearch, Kind = FlatBtn.Kinds.Primary, Bounds = R(28, 0, 220, 40), BackColor = Bg };
            pCheck.Controls.Add(bRun);
            bRun.Click += async (s, e) => await RunCheck();
            runStatus = UIx.L("Kurulumdan Discord'a kadar ~20 nokta kontrol edilir (10-20 saniye). Bulunan her sorun için 'Düzelt' düğmesi çıkar.",
                Small, Muted, 260, 0, 530, 40, ContentAlignment.MiddleLeft);
            pCheck.Controls.Add(runStatus);
            rowsArea = new ScrollArea { Bounds = R(18, 52, 790, 462) };
            pCheck.Controls.Add(rowsArea);

            // ================================================ hata kodları
            var left = new Card { Bounds = R(28, 0, 330, 512) };
            pCodes.Controls.Add(left);
            cat = left.Add(new ThemedCombo { Bounds = R(12, 12, 306, 26) });
            cat.Items.Add("Tüm kategoriler (" + ErrorCatalog.All.Count + ")");
            foreach (var c in ErrorCatalog.Categories) cat.Items.Add(c + " (" + ErrorCatalog.All.Count(e => e.Category == c) + ")");
            cat.SelectedIndex = 0;
            cat.SelectedIndexChanged += (s, e) => FillCodes();
            search = left.Add(Theme.Style(new TextBox { Bounds = R(12, 46, 306, 26), Font = Body }));
            SetCue(search, "Ara: kod ya da kelime (ör. DK-204, antivirüs)");
            search.TextChanged += (s, e) => FillCodes();
            codes = new ListBox
            {
                Bounds = R(6, 82, 318, 422), DrawMode = DrawMode.OwnerDrawFixed, ItemHeight = D(46),
                BorderStyle = BorderStyle.None, IntegralHeight = false, Font = Body,
            };
            left.Add(codes);
            codes.BackColor = Theme.Card;
            Theme.StyleScroll(codes);
            codes.DrawItem += DrawCode;
            codes.SelectedIndexChanged += (s, e) => ShowInfo(codes.SelectedItem as ErrorInfo);

            var right = new Card { Bounds = R(370, 0, 422, 512) };
            pCodes.Controls.Add(right);
            info = right.Add(new ReportBox { Bounds = R(16, 12, 390, 432) });
            info.BackColor = Theme.Card;
            bFix1 = right.Add(new FlatBtn { Kind = FlatBtn.Kinds.Primary, Bounds = R(16, 456, 180, 40), Font = Small });
            bFix2 = right.Add(new FlatBtn { Kind = FlatBtn.Kinds.Secondary, Bounds = R(202, 456, 172, 40), Font = Small });
            bCopy = right.Add(new FlatBtn { Glyph = GCopy, Kind = FlatBtn.Kinds.Ghost, Bounds = R(378, 456, 30, 40) });
            new ToolTip().SetToolTip(bCopy, "Açıklamayı kopyala (yardım isterken paylaşmak için)");
            bFix1.Click += async (s, e) => await RunFix(bFix1, (codes.SelectedItem as ErrorInfo)?.Fix ?? FixKind.None);
            bFix2.Click += async (s, e) => await RunFix(bFix2, (codes.SelectedItem as ErrorInfo)?.Fix2 ?? FixKind.None);
            bCopy.Click += (s, e) => { if (info.TextLength > 0) Clipboard.SetText(info.Text); };

            FillCodes();
            ShowProblems();
            OnStatus(() => { if (Visible && !checking && rowY == 0) ShowProblems(); codes.Invalidate(); });
        }

        [System.Runtime.InteropServices.DllImport("user32.dll", CharSet = System.Runtime.InteropServices.CharSet.Unicode)]
        static extern IntPtr SendMessage(IntPtr h, int msg, IntPtr w, string l);
        static void SetCue(TextBox tb, string text)
        {
            tb.HandleCreated += (s, e) => SendMessage(tb.Handle, 0x1501, (IntPtr)1, text);
            if (tb.IsHandleCreated) SendMessage(tb.Handle, 0x1501, (IntPtr)1, text);
        }

        public override void OnActivated()
        {
            if (!checking && rowY == 0) ShowProblems();
        }

        /// <summary>Hata kodları sekmesini açıp verilen kodu seçer.</summary>
        public void ShowCode(string code)
        {
            tabs.SelectedIndex = 1;
            cat.SelectedIndex = 0;
            search.Text = "";
            FillCodes();
            if (string.IsNullOrEmpty(code)) return;
            for (int i = 0; i < codes.Items.Count; i++)
                if (((ErrorInfo)codes.Items[i]).Code == code) { codes.SelectedIndex = i; codes.TopIndex = Math.Max(0, i - 2); break; }
        }

        // ------------------------------------------------ sorunu bul

        void ClearRows()
        {
            var old = rowsArea.Controls.Cast<Control>().ToList();
            rowsArea.Controls.Clear();
            foreach (var c in old) c.Dispose();
            rowsArea.AutoScrollPosition = Point.Empty;
            rowY = 0;
        }

        void ShowProblems()
        {
            ClearRows();
            var list = App.Problems.Snapshot();
            if (list.Count == 0)
            {
                var c = AddRowCard(70);
                c.Add(UIx.Glyph(GCheck, IconL, GreenText, 14, 0, 44, 70));
                c.Add(UIx.L("Şu an algılanmış bir sorun yok", Bold, Theme.Text, 64, 14, 640, 22));
                c.Add(UIx.L("Discord açılmıyorsa 'Tam kontrolü başlat'a basın ya da 'Hata Kodları' sekmesinden belirtiye göre çözüm arayın.", Small, Muted, 64, 36, 660, 30));
                rowY = 0;   // yer tutucu
                return;
            }
            var h = UIx.L("ALGILANAN SORUNLAR (" + list.Count + ")", Caption, Muted, 12, 0, 400, 20);
            rowsArea.Controls.Add(h);
            rowY = 24;
            foreach (var p in list)
            {
                var e = p.Info;
                var st = e.Severity == Severity.Error ? CheckStatus.Fail : e.Severity == Severity.Warning ? CheckStatus.Warn : CheckStatus.Info;
                AddCheckRow(new CheckItem { Title = e.Title, Status = st, Code = p.Code, Detail = (p.Detail.Length > 0 ? p.Detail + " · " : "") + p.Time.ToString("HH:mm") });
            }
            rowY = 0;   // tam kontrol başlayınca temizlenir
        }

        Card AddRowCard(int h)
        {
            var c = new Card { Bounds = new Rectangle(D(10), D(rowY) + rowsArea.AutoScrollPosition.Y, D(744), D(h)), BackColor = Bg };
            rowsArea.Controls.Add(c);
            rowY += h + 8;
            return c;
        }

        void AddCheckRow(CheckItem it)
        {
            var c = AddRowCard(58);
            string g; Color col;
            switch (it.Status)
            {
                case CheckStatus.Ok: g = GCheck; col = GreenText; break;
                case CheckStatus.Warn: g = GWarn; col = AmberText; break;
                case CheckStatus.Fail: g = GCancel; col = RedText; break;
                case CheckStatus.Skip: g = GInfo; col = Muted; break;
                default: g = GInfo; col = AccentInk; break;
            }
            if (it.Status == CheckStatus.Fail) { c.Fill = RedSoft; c.Stroke = RedBorder; }
            else if (it.Status == CheckStatus.Warn) { c.Fill = AmberSoft; c.Stroke = AmberBorder; }
            c.Add(UIx.Glyph(g, IconM, col, 10, 0, 40, 58));
            var info = string.IsNullOrEmpty(it.Code) ? null : ErrorCatalog.Get(it.Code);
            bool problem = it.Status == CheckStatus.Fail || it.Status == CheckStatus.Warn || (it.Status == CheckStatus.Info && info != null && it.Code == "DK-901");
            int textW = problem && info != null ? 420 : 680;
            c.Add(UIx.L(it.Title + (problem && info != null ? "  ·  " + it.Code : ""), Bold, Theme.Text, 52, 8, textW, 22));
            c.Add(UIx.L(it.Detail, Small, Muted, 52, 29, textW, 24));
            if (problem && info != null)
            {
                var more = c.Add(new FlatBtn { Text = "Çözüm", Glyph = GHelp, Kind = FlatBtn.Kinds.Ghost, Bounds = R(480, 12, 96, 34) });
                more.Click += (s, e) => ShowCode(it.Code);
                if (info.Fix != FixKind.None)
                {
                    var fix = c.Add(new FlatBtn { Text = ErrorInfo.FixLabel(info.Fix), Kind = it.Status == CheckStatus.Fail ? FlatBtn.Kinds.Danger : FlatBtn.Kinds.Secondary, Bounds = R(582, 12, 152, 34), Font = Small });
                    fix.Click += async (s, e) => await RunFix(fix, info.Fix);
                }
            }
        }

        public async Task RunCheck()
        {
            tabs.SelectedIndex = 0;
            if (checking) return;
            checking = true;
            bRun.Enabled = false;
            runStatus.Text = "Kontrol ediliyor…";
            runStatus.ForeColor = Muted;
            ClearRows();
            rowY = 0;
            List<CheckItem> items = null;
            try
            {
                items = await Diagnostics.RunAsync(it => { if (!IsDisposed) AddCheckRow(it); });
            }
            catch (Exception ex) { runStatus.Text = "Hata: " + ex.Message; }
            checking = false;
            if (IsDisposed) return;
            bRun.Enabled = true;
            bRun.Text = "Tekrar kontrol et";
            if (items != null)
            {
                int fail = items.Count(i => i.Status == CheckStatus.Fail), warn = items.Count(i => i.Status == CheckStatus.Warn);
                runStatus.Text = fail + warn == 0 ? "✓ Sorun bulunamadı. Her şey yolunda görünüyor." :
                    "✗ " + fail + " hata, " + warn + " uyarı bulundu. Her satırdaki düğme sorunu çözer; 'Çözüm' ayrıntılı adımları gösterir.";
                runStatus.ForeColor = fail > 0 ? RedText : warn > 0 ? AmberText : GreenText;
            }
        }

        async Task RunFix(FlatBtn b, FixKind f)
        {
            if (f == FixKind.None) return;
            b.Enabled = false;
            string msg = await Fixes.Run(f, FindForm());
            if (IsDisposed) return;
            b.Enabled = true;
            if (!string.IsNullOrEmpty(msg)) MessageBox.Show(FindForm(), msg, App.Name, MessageBoxButtons.OK, MessageBoxIcon.Information);
        }

        // ------------------------------------------------ hata kodları

        void FillCodes()
        {
            string q = (search.Text ?? "").Trim().ToLower(new System.Globalization.CultureInfo("tr-TR"));
            string category = cat.SelectedIndex > 0 ? ErrorCatalog.Categories[cat.SelectedIndex - 1] : null;
            var keep = codes.SelectedItem as ErrorInfo;
            codes.BeginUpdate();
            codes.Items.Clear();
            var tr = new System.Globalization.CultureInfo("tr-TR");
            foreach (var e in ErrorCatalog.All)
            {
                if (category != null && e.Category != category) continue;
                if (q.Length > 0)
                {
                    string hay = (e.Code + " " + e.Title + " " + e.Summary + " " + e.Category + " " + string.Join(" ", e.Causes)).ToLower(tr);
                    if (!q.Split(' ').All(w => hay.Contains(w))) continue;
                }
                codes.Items.Add(e);
            }
            codes.EndUpdate();
            int idx = keep != null ? codes.Items.IndexOf(keep) : -1;
            if (idx < 0 && codes.Items.Count > 0) idx = 0;
            codes.SelectedIndex = idx;
            if (idx < 0) ShowInfo(null);
        }

        void DrawCode(object sender, DrawItemEventArgs e)
        {
            if (e.Index < 0) return;
            var it = (ErrorInfo)codes.Items[e.Index];
            var g = e.Graphics;
            HQ(g);
            bool sel = (e.State & DrawItemState.Selected) != 0;
            var r = e.Bounds;
            using (var b = new SolidBrush(Theme.Card)) g.FillRectangle(b, r);
            Color bg = sel ? AccentSoft : Theme.Card;
            using (var p = Round(new RectangleF(r.X + 2, r.Y + 2, r.Width - 5, r.Height - 4), D(8)))
            {
                using (var b = new SolidBrush(bg)) g.FillPath(b, p);
                if (sel) using (var pen = new Pen(AccentBorder)) g.DrawPath(pen, p);
            }
            Color sc = SevColor(it.Severity);
            Color soft = Mix(bg, sc, IsDark ? 0.28f : 0.16f);
            Color ink = Readable(sc, soft, 4.0);
            var badge = new RectangleF(r.X + D(10), r.Y + D(12), D(62), D(22));
            using (var p = Round(badge, D(6))) using (var b = new SolidBrush(soft)) g.FillPath(b, p);
            TextRenderer.DrawText(g, it.Code, Caption, Rectangle.Round(badge), ink, soft, TextFormatFlags.HorizontalCenter | TextFormatFlags.VerticalCenter | TextFormatFlags.NoPadding);
            bool active = App.Problems.Snapshot().Any(p => p.Code == it.Code);
            TextRenderer.DrawText(g, it.Title, Bold, new Rectangle(r.X + D(82), r.Y + D(5), r.Width - D(92), D(20)), Theme.Text, bg, TextFormatFlags.NoPadding | TextFormatFlags.EndEllipsis);
            TextRenderer.DrawText(g, it.Category + (active ? "  ·  ŞU AN ALGILANDI" : ""), Small, new Rectangle(r.X + D(82), r.Y + D(25), r.Width - D(92), D(18)),
                active ? AmberText : Muted, bg, TextFormatFlags.NoPadding | TextFormatFlags.EndEllipsis);
        }

        void ShowInfo(ErrorInfo e)
        {
            info.Clear();
            bFix1.Visible = bFix2.Visible = false;
            if (e == null) { info.Muted("Aramanıza uyan hata kodu yok."); return; }
            string sev = e.Severity == Severity.Error ? "Hata" : e.Severity == Severity.Warning ? "Uyarı" : "Bilgi";
            info.Out(e.Code + " · " + sev, Readable(SevColor(e.Severity), Theme.Card, 3.5), Bold);
            info.Out(e.Title, Theme.Text, H2);
            info.Muted("Kategori: " + e.Category);
            var det = App.Problems.Snapshot().FirstOrDefault(p => p.Code == e.Code);
            if (det != null) info.Out("Bu sorun " + det.Time.ToString("HH:mm") + "'da algılandı" + (det.Detail.Length > 0 ? ": " + det.Detail : "."), AmberText, Small);
            info.Blank();
            info.Out(e.Summary, Theme.Text, Body);
            if (e.Causes.Length > 0)
            {
                info.Blank();
                info.Out("OLASI NEDENLER", Muted, Caption);
                foreach (var c in e.Causes) info.Out("•  " + c, Theme.Text, Small);
            }
            if (e.Steps.Length > 0)
            {
                info.Blank();
                info.Out("ÇÖZÜM ADIMLARI", Muted, Caption);
                int i = 0;
                foreach (var s in e.Steps) info.Out((++i) + ".  " + s, Theme.Text, Body);
            }
            if (!string.IsNullOrEmpty(e.Detect))
            {
                info.Blank();
                info.Out("Nasıl algılanır: " + e.Detect, Muted, Small);
            }
            info.SelectionStart = 0;
            info.ScrollToCaret();
            if (e.Fix != FixKind.None) { bFix1.Text = ErrorInfo.FixLabel(e.Fix); bFix1.Visible = true; }
            if (e.Fix2 != FixKind.None) { bFix2.Text = ErrorInfo.FixLabel(e.Fix2); bFix2.Visible = true; }
        }
    }
}
