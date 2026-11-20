using System;
using System.Collections.Generic;
using System.Drawing;
using System.IO;
using System.Linq;
using System.Net;
using System.Windows.Forms;
using static DPIKalkan.Theme;

namespace DPIKalkan
{
    sealed class ProfilesPage : PageBase
    {
        public override string Key => "profiles";

        sealed class GroupHeader { public string Name; public override string ToString() => Name; }

        readonly ListBox list;
        readonly ReportBox detail;
        readonly FlatBtn bActivate, bNew, bCopy, bEdit, bDelete, bScript;
        readonly bool detailed;

        public ProfilesPage()
        {
            detailed = Level(UiLevel.Detayli);
            Header("Profiller", "Her profil farklı bir engel aşma yöntemidir. Seçtiğiniz profilin ne yaptığı sağda ayrıntılı açıklanır.");

            var card = AddCard(28, 76, 430, 474);
            list = new ListBox
            {
                Bounds = R(8, 8, 414, 458),
                DrawMode = DrawMode.OwnerDrawVariable,
                BorderStyle = BorderStyle.None,
                IntegralHeight = false,
                Font = Body,
            };
            card.Add(list);
            list.BackColor = Theme.Card;
            Theme.StyleScroll(list);
            list.MeasureItem += (s, e) => e.ItemHeight = list.Items[e.Index] is GroupHeader ? D(30) : D(detailed ? 70 : 56);
            list.DrawItem += DrawItem;
            list.SelectedIndexChanged += (s, e) =>
            {
                if (list.SelectedItem is GroupHeader && list.SelectedIndex + 1 < list.Items.Count) { list.SelectedIndex++; return; }
                UpdateButtons();
                ShowDetail();
            };
            list.DoubleClick += async (s, e) => { if (Selected != null) await Actions.SelectProfileAsync(Selected); };
            list.MouseMove += (s, e) =>
            {
                int i = list.IndexFromPoint(e.Location);
                list.Cursor = i >= 0 && list.Items[i] is Profile ? Cursors.Hand : Cursors.Default;
            };

            var dcard = AddCard(470, 76, 322, 474);
            detail = new ReportBox { Bounds = R(16, 12, 296, 450) };
            dcard.Add(detail);
            detail.BackColor = Theme.Card;

            int x = 28;
            FlatBtn B(string text, string glyph, FlatBtn.Kinds kind, int w)
            {
                var b = Btn(text, glyph, kind, x, 562, w, 40);
                x += w + 8;
                return b;
            }
            bActivate = B("Bu profili kullan", GCheck, FlatBtn.Kinds.Primary, 166);
            bNew = B("Yeni", GAdd, FlatBtn.Kinds.Secondary, 84);
            bCopy = B("Kopyala", GCopy, FlatBtn.Kinds.Secondary, 104);
            bEdit = B("Düzenle", GEdit, FlatBtn.Kinds.Secondary, 106);
            bDelete = B("Sil", GDelete, FlatBtn.Kinds.Secondary, 72);
            bScript = B("Paylaş", GChevron, FlatBtn.Kinds.Secondary, 112);

            bActivate.Click += async (s, e) => { if (Selected != null) await Actions.SelectProfileAsync(Selected); };
            bNew.Click += (s, e) => Edit(new Profile { Name = "Yeni Profil" }, true);
            bCopy.Click += (s, e) => { if (Selected != null) Edit(Selected.CloneAsCustom(), true); };
            bEdit.Click += (s, e) => { if (Selected != null) Edit(Selected, false); };
            bDelete.Click += (s, e) => DeleteSelected();
            bScript.Click += (s, e) => ShowShareMenu();

            OnStatus(() => { if (Visible) { list.Invalidate(); UpdateButtons(); } });
        }

        Profile Selected => list.SelectedItem as Profile;

        public override void OnActivated() => Reload();

        void Reload(string selectId = null)
        {
            string keep = selectId ?? Selected?.Id ?? App.Settings.SelectedProfileId;
            list.BeginUpdate();
            list.Items.Clear();
            string group = null;
            foreach (var p in App.AllProfiles.OrderBy(x => x.Priority))
            {
                if (p.Group != group) { group = p.Group; list.Items.Add(new GroupHeader { Name = group }); }
                list.Items.Add(p);
            }
            list.EndUpdate();
            for (int i = 0; i < list.Items.Count; i++)
                if (list.Items[i] is Profile pp && pp.Id == keep) { list.SelectedIndex = i; break; }
            UpdateButtons();
            ShowDetail();
        }

        void UpdateButtons()
        {
            var p = Selected;
            bool busy = Actions.Scanning || App.Engine.State == EngineState.Starting || App.Engine.State == EngineState.Stopping;
            bActivate.Enabled = p != null && !busy;
            bCopy.Enabled = p != null;
            bEdit.Enabled = p != null;
            bEdit.Text = p != null && p.BuiltIn ? "İncele" : "Düzenle";
            bDelete.Enabled = p != null && !p.BuiltIn;
            bScript.Enabled = true;
        }

        void ShowDetail()
        {
            var p = Selected;
            detail.Clear();
            if (p == null) { detail.Muted("Soldan bir profil seçin."); return; }
            detail.Out(p.Name, Theme.Text, H2);
            var tags = new List<string> { p.BuiltIn ? "Hazır profil" : "Özel profil" };
            if (p.Id == App.Settings.SelectedProfileId) tags.Add(App.Engine.State == EngineState.Running ? "şu an kullanılıyor" : "seçili");
            detail.Muted(string.Join(" · ", tags));
            detail.Blank();
            detail.Out(p.Description, Theme.Text, Body);
            detail.Blank();
            detail.Out("BU PROFİL NE YAPAR?", Muted, Caption);
            foreach (var (title, text) in ProfileHelp.Explain(p))
            {
                bool warn = title.Contains("YOK");
                detail.Out("• " + title, warn ? AmberText : Theme.Text, Bold);
                detail.Out(text, Muted, Small);
            }
            string when = ProfileHelp.WhenToUse(p);
            if (when.Length > 0)
            {
                detail.Blank();
                detail.Out("NE ZAMAN DENENİR?", Muted, Caption);
                detail.Out(when, Theme.Text, Small);
            }
            if (Level(UiLevel.Orta))
            {
                detail.Blank();
                detail.Out("KOMUT", Muted, Caption);
                detail.Out("goodbyedpi.exe " + p.BuildArgs(App.Settings.TargetedMode ? "…\\hedef-siteler.txt" : null), Muted, MonoSmall);
            }
            detail.SelectionStart = 0;
            detail.ScrollToCaret();
        }

        void Edit(Profile p, bool isNew)
        {
            DialogResult r;
            using (var dlg = new ProfileEditor(p))
                r = dlg.ShowDialog(FindForm());
            if (r == DialogResult.Retry) { Edit(p.CloneAsCustom(), true); return; }
            if (r != DialogResult.OK) return;
            if (isNew) App.Settings.CustomProfiles.Add(p);
            App.Save();
            App.Log("Profil kaydedildi: " + p.Name, LogLevel.Info);
            Reload(p.Id);
            App.RaiseStatus();
            if (!isNew && p.Id == App.Settings.SelectedProfileId && App.Engine.IsActive)
            {
                if (MessageBox.Show(FindForm(), "Bu profil şu an kullanılıyor. Değişiklikler uygulansın mı (koruma yeniden başlatılır)?",
                        App.Name, MessageBoxButtons.YesNo, MessageBoxIcon.Question) == DialogResult.Yes)
                    _ = Actions.StartAsync();
            }
        }

        void DeleteSelected()
        {
            var p = Selected;
            if (p == null || p.BuiltIn) return;
            if (MessageBox.Show(FindForm(), "'" + p.Name + "' profili silinsin mi?", App.Name,
                    MessageBoxButtons.YesNo, MessageBoxIcon.Question) != DialogResult.Yes) return;
            App.Settings.CustomProfiles.RemoveAll(x => x.Id == p.Id);
            if (App.Settings.SelectedProfileId == p.Id)
            {
                App.Settings.SelectedProfileId = Profiles.BuiltIns[0].Id;
                if (App.Engine.IsActive) _ = Actions.StartAsync();
            }
            App.Save();
            Reload();
            App.RaiseStatus();
        }

        void ShowShareMenu()
        {
            var p = Selected;
            var m = new ContextMenuStrip { Font = Body, ShowImageMargin = false };
            var copy = new ToolStripMenuItem("Profil kodunu kopyala (arkadaşına gönder)") { Enabled = p != null };
            copy.Click += (s, e) =>
            {
                Clipboard.SetText(ProfileCode.ToCode(p));
                MessageBox.Show(FindForm(), "'" + p.Name + "' profilinin kodu panoya kopyalandı.\n\nArkadaşınız Profiller → Paylaş → 'Koddan profil ekle' ile aynı profili kullanabilir.",
                    App.Name, MessageBoxButtons.OK, MessageBoxIcon.Information);
            };
            var paste = new ToolStripMenuItem("Koddan profil ekle (panodaki kod)");
            paste.Click += (s, e) => ImportCode();
            m.Items.Add(copy);
            m.Items.Add(paste);
            if (detailed)
            {
                m.Items.Add(new ToolStripSeparator());
                var cmd = new ToolStripMenuItem(".cmd betiği olarak kaydet…") { Enabled = p != null };
                cmd.Click += (s, e) => SaveScript();
                m.Items.Add(cmd);
            }
            m.Closed += (s, e) => BeginInvoke(new Action(m.Dispose));
            m.Show(bScript, new Point(0, bScript.Height));
        }

        void ImportCode()
        {
            string text = Clipboard.ContainsText() ? Clipboard.GetText() : "";
            if (!ProfileCode.TryFromCode(text, out var p, out var err))
            {
                MessageBox.Show(FindForm(), err + "\n\nÖnce arkadaşınızın gönderdiği 'DKP1:' ile başlayan kodu kopyalayın.", App.Name, MessageBoxButtons.OK, MessageBoxIcon.Warning);
                return;
            }
            if (App.AllProfiles.Any(x => x.Name == p.Name)) p.Name += " (içe aktarıldı)";
            if (MessageBox.Show(FindForm(), "Şu profil eklensin mi?\n\n" + p.Name + "\n" + p.Description + "\n\nKomut: goodbyedpi.exe " + p.BuildArgs(null),
                    App.Name, MessageBoxButtons.YesNo, MessageBoxIcon.Question) != DialogResult.Yes) return;
            App.Settings.CustomProfiles.Add(p);
            App.Save();
            App.Log("Profil koddan eklendi: " + p.Name, LogLevel.Success);
            Reload(p.Id);
            App.RaiseStatus();
        }

        void SaveScript()
        {
            var p = Selected;
            if (p == null) return;
            using (var d = new SaveFileDialog { FileName = Scripts.Slug(p.Name) + ".cmd", Filter = "Komut dosyası (*.cmd)|*.cmd" })
            {
                if (d.ShowDialog(FindForm()) != DialogResult.OK) return;
                try
                {
                    Scripts.Write(p, d.FileName, Engine.BinDir.Substring(0, Engine.BinDir.Length - Engine.Arch.Length - 1));
                    App.Log("Betik kaydedildi: " + d.FileName, LogLevel.Success);
                    MessageBox.Show(FindForm(), "Betik kaydedildi. Uygulama klasörünü taşırsanız betik çalışmaz.\n\n" + d.FileName, App.Name,
                        MessageBoxButtons.OK, MessageBoxIcon.Information);
                }
                catch (Exception ex) { MessageBox.Show(FindForm(), "Kaydedilemedi: " + ex.Message, App.Name, MessageBoxButtons.OK, MessageBoxIcon.Error); }
            }
        }

        void DrawItem(object sender, DrawItemEventArgs e)
        {
            if (e.Index < 0) return;
            var g = e.Graphics;
            HQ(g);
            var r = e.Bounds;
            using (var b = new SolidBrush(Theme.Card)) g.FillRectangle(b, r);
            if (list.Items[e.Index] is GroupHeader gh)
            {
                TextRenderer.DrawText(g, gh.Name.ToUpper(new System.Globalization.CultureInfo("tr-TR")), Caption,
                    new Rectangle(r.X + D(10), r.Y + D(8), r.Width - D(20), D(20)), Muted, Theme.Card, TextFormatFlags.NoPadding);
                return;
            }
            var p = (Profile)list.Items[e.Index];
            bool sel = (e.State & DrawItemState.Selected) != 0;
            bool active = p.Id == App.Settings.SelectedProfileId;
            Color bg = sel ? AccentSoft : Theme.Card;
            var inner = new RectangleF(r.X + D(2), r.Y + D(2), r.Width - D(5), r.Height - D(4));
            using (var path = Round(inner, D(8)))
            {
                using (var b = new SolidBrush(bg)) g.FillPath(b, path);
                if (sel) using (var pen = new Pen(AccentBorder)) g.DrawPath(pen, path);
            }

            bool running = active && App.Engine.State == EngineState.Running;
            Color dot = active ? (running ? Green : Accent) : Mix(Theme.Card, Theme.Text, 0.2f);
            using (var b = new SolidBrush(dot)) g.FillEllipse(b, r.X + D(14), r.Y + D(15), D(10), D(10));

            int x = r.X + D(34);
            var flags = TextFormatFlags.NoPadding | TextFormatFlags.SingleLine | TextFormatFlags.EndEllipsis;
            var nameSize = TextRenderer.MeasureText(g, p.Name, Bold, Size.Empty, TextFormatFlags.NoPadding | TextFormatFlags.SingleLine);
            int nameW = Math.Min(nameSize.Width, r.Width - D(170));
            TextRenderer.DrawText(g, p.Name, Bold, new Rectangle(x, r.Y + D(8), nameW + D(2), D(22)), Theme.Text, bg, flags);

            int bx = x + nameW + D(8);
            if (active) bx = Badge(g, running ? "ÇALIŞIYOR" : "SEÇİLİ", running ? GreenText : AccentInk, running ? GreenSoft : AccentSoft, bx, r.Y + D(9));
            if (!p.BuiltIn) bx = Badge(g, "ÖZEL", AccentInk, Mix(Theme.Card, Accent, 0.15f), bx, r.Y + D(9));
            if (!p.UsesDnsRedirect) Badge(g, "DNS YOK", AmberText, AmberSoft, bx, r.Y + D(9));

            TextRenderer.DrawText(g, p.Description, Small, new Rectangle(x, r.Y + D(30), r.Width - D(44), D(18)), Muted, bg, flags);
            if (detailed)
                TextRenderer.DrawText(g, p.BuildArgs(null), MonoSmall, new Rectangle(x, r.Y + D(48), r.Width - D(44), D(16)), Mix(Muted, bg, 0.2f), bg, flags);
        }

        static int Badge(Graphics g, string text, Color fg, Color bg, int x, int y)
        {
            var flags = TextFormatFlags.NoPadding | TextFormatFlags.SingleLine;
            var sz = TextRenderer.MeasureText(g, text, Caption, Size.Empty, flags);
            var rect = new RectangleF(x, y, sz.Width + D(12), D(18));
            using (var p = Round(rect, D(9)))
            using (var b = new SolidBrush(bg)) g.FillPath(b, p);
            TextRenderer.DrawText(g, text, Caption, new Rectangle(x + D(6), y, sz.Width + D(2), D(18)), fg, bg,
                flags | TextFormatFlags.VerticalCenter);
            return (int)rect.Right + D(5);
        }
    }

    /// <summary>Profil düzenleme penceresi: her seçeneğin açıklaması sağ panelde gösterilir.</summary>
    sealed class ProfileEditor : Form
    {
        readonly Profile p;
        readonly TextBox tName, tDesc, tExtra, tRaw, tPreview, tDns4, tDns6;
        readonly NumericUpDown nDns4Port, nDns6Port, nFake, nResend, nHttps, nHttp;
        readonly ThemedCombo cMode, cTtl, cDns;
        readonly CheckX chkChk, chkSeq, chkNative, chkReverse, chkQuic, chkSni, chkPayload, chkNoSni, chkPassive, chkHostR, chkHostS, chkHostM, chkRaw;
        readonly Label lDns4, lDns6;
        readonly ReportBox help;
        readonly List<(DnsChoice Id, string Label)> dnsItems = new List<(DnsChoice, string)>();
        string lastTopic;

        static readonly (string Value, string Label)[] Modes =
        {
            ("", "Yok (yalnızca aşağıdaki seçenekler)"),
            ("-1", "-1  Klasik, en uyumlu"), ("-2", "-2  Klasik, HTTPS için daha hızlı"),
            ("-3", "-3  Klasik, HTTP/HTTPS hızlı"), ("-4", "-4  Klasik, en hızlı (yalnız HTTP)"),
            ("-5", "-5  Modern: otomatik TTL"), ("-6", "-6  Modern: yanlış sıra numarası"),
            ("-7", "-7  Modern: yanlış sağlama toplamı"), ("-8", "-8  Modern: sıra + sağlama"),
            ("-9", "-9  Modern: tam (önerilen)"),
        };

        public ProfileEditor(Profile profile)
        {
            p = profile;
            Text = profile.BuiltIn ? "Hazır profil: " + profile.Name : "Profili Düzenle";
            FormBorderStyle = FormBorderStyle.FixedDialog;
            StartPosition = FormStartPosition.CenterParent;
            MaximizeBox = MinimizeBox = false;
            ShowInTaskbar = false;
            AutoScaleMode = AutoScaleMode.None;
            BackColor = Bg;
            Font = Body;
            ClientSize = new Size(D(960), D(690));
            Icon = IconArt.AppIcon(32);

            Label Lbl(string t, int x, int y, int w = 150, Font f = null, Color? c = null)
            {
                var l = UIx.L(t, f ?? Body, c ?? Theme.Text, x, y, w, 24, ContentAlignment.MiddleLeft);
                Controls.Add(l);
                return l;
            }
            T Add<T>(T c, string topic) where T : Control
            {
                Controls.Add(c);
                if (topic != null)
                {
                    c.Enter += (s, e) => ShowHelp(topic);
                    c.MouseEnter += (s, e) => ShowHelp(topic);
                }
                return c;
            }
            TextBox Tb(int x, int y, int w, string text, string topic, Font f = null) =>
                Add(Theme.Style(new TextBox { Bounds = R(x, y, w, 24), Text = text, Font = f ?? Body }), topic);
            NumericUpDown Num(int x, int y, int w, int min, int max, int val, string topic) =>
                Add(Theme.Style(new NumericUpDown { Bounds = R(x, y, w, 24), Minimum = min, Maximum = max, Value = Math.Max(min, Math.Min(max, val)) }), topic);
            CheckX Chk(string t, bool v, int x, int y, string topic) =>
                Add(new CheckX { Text = t, Checked = v, Bounds = R(x, y, 280, 24), BackColor = Bg }, topic);

            Lbl("Profil adı", 20, 16);
            tName = Tb(170, 16, 430, p.Name, "name");
            Lbl("Açıklama", 20, 48);
            tDesc = Tb(170, 48, 430, p.Description, "name");

            Lbl("YÖNTEM", 20, 86, 300, Caption, Muted);
            Lbl("Temel mod", 20, 108);
            cMode = Add(new ThemedCombo { Bounds = R(170, 108, 430, 24) }, "mode");
            foreach (var m in Modes) cMode.Items.Add(m.Label);
            cMode.SelectedIndex = Math.Max(0, Array.FindIndex(Modes, m => m.Value == (p.Mode ?? "")));
            cMode.SelectedIndexChanged += (s, e) => ShowHelp("mode:" + Modes[Math.Max(0, cMode.SelectedIndex)].Value);

            Lbl("Sahte paket TTL", 20, 140);
            cTtl = Add(new ThemedCombo { Bounds = R(170, 140, 160, 24) }, "ttl");
            cTtl.Items.Add("Kapalı");
            cTtl.Items.Add("Otomatik (--auto-ttl)");
            for (int i = 1; i <= 12; i++) cTtl.Items.Add("TTL " + i);
            cTtl.SelectedIndex = p.Ttl == "auto" ? 1 : int.TryParse(p.Ttl, out int ttl) && ttl >= 1 && ttl <= 12 ? ttl + 1 : 0;
            Lbl("HTTPS parça (-e)", 350, 140, 120);
            nHttps = Num(480, 140, 120, 0, 1000, p.HttpsFrag, "httpsfrag");

            Lbl("Sahte paket sayısı", 20, 172);
            nFake = Num(170, 172, 160, 0, 30, p.FakeGen, "fakegen");
            Lbl("Sahte tekrar", 350, 172, 120);
            nResend = Num(480, 172, 120, 1, 20, Math.Max(1, p.FakeResend), "fakeresend");

            Lbl("HTTP parça (-f)", 20, 204);
            nHttp = Num(170, 204, 160, 0, 1000, p.HttpFrag, "httpfrag");

            Lbl("DNS YÖNLENDİRME", 20, 244, 300, Caption, Muted);
            Lbl("DNS sunucusu", 20, 266);
            cDns = Add(new ThemedCombo { Bounds = R(170, 266, 430, 24) }, "dns");
            foreach (var d in DnsProviders.All) dnsItems.Add((d.Id, d.Name + " – " + d.V4 + ":" + d.V4Port + (d.Id == DnsChoice.Yandex ? " (önerilen)" : "")));
            dnsItems.Add((DnsChoice.Custom, "Özel sunucu…"));
            dnsItems.Add((DnsChoice.None, "Kapalı – servis sağlayıcının DNS'i (Discord açılmaz!)"));
            foreach (var d in dnsItems) cDns.Items.Add(d.Label);
            cDns.SelectedIndex = Math.Max(0, dnsItems.FindIndex(d => d.Id == p.Dns));

            lDns4 = Lbl("   IPv4 adres : port", 20, 298);
            tDns4 = Tb(170, 298, 300, p.CustomDns4, "dns:custom");
            nDns4Port = Num(480, 298, 120, 1, 65535, p.CustomDns4Port <= 0 ? 53 : p.CustomDns4Port, "dns:custom");
            lDns6 = Lbl("   IPv6 adres : port", 20, 328);
            tDns6 = Tb(170, 328, 300, p.CustomDns6, "dns:custom");
            nDns6Port = Num(480, 328, 120, 1, 65535, p.CustomDns6Port <= 0 ? 53 : p.CustomDns6Port, "dns:custom");

            Lbl("EK SEÇENEKLER", 20, 366, 300, Caption, Muted);
            chkSeq = Chk("Yanlış sıra numarası (--wrong-seq)", p.WrongSeq, 20, 388, "wrongseq");
            chkChk = Chk("Yanlış sağlama toplamı (--wrong-chksum)", p.WrongChksum, 20, 414, "wrongchk");
            chkNative = Chk("Yerel parçalama (--native-frag)", p.NativeFrag, 20, 440, "native");
            chkReverse = Chk("Ters parçalama (--reverse-frag)", p.ReverseFrag, 20, 466, "reverse");
            chkSni = Chk("Site adından böl (--frag-by-sni)", p.FragBySni, 20, 492, "sni");
            chkPayload = Chk("Büyük paketleri atla (--max-payload)", p.MaxPayload, 20, 518, "payload");
            chkQuic = Chk("QUIC/HTTP3 engelle (-q)", p.BlockQuic, 320, 388, "quic");
            chkNoSni = Chk("SNI'siz bağlantılara uygula", p.AllowNoSni, 320, 414, "nosni");
            chkPassive = Chk("Pasif DPI'ı engelle (-p)", p.PassiveDpi, 320, 440, "passive");
            chkHostR = Chk("Host → hoSt (-r)", p.HostReplace, 320, 466, "hostr");
            chkHostS = Chk("Host boşluğunu kaldır (-s)", p.HostRemoveSpace, 320, 492, "hosts");
            chkHostM = Chk("Host harflerini karıştır (-m)", p.HostMixCase, 320, 518, "hostm");

            Lbl("Ek parametreler", 20, 552);
            tExtra = Tb(170, 552, 430, p.ExtraArgs, "extra", Mono);
            chkRaw = Add(new CheckX { Text = "Komutu elle yaz (uzman – yukarıdaki seçenekler yok sayılır)", Checked = p.UseRaw, Bounds = R(20, 584, 580, 24), BackColor = Bg }, "raw");
            tRaw = Add(Theme.Style(new TextBox { Bounds = R(20, 612, 580, 60), Multiline = true, Font = Mono, Text = p.RawArgs, ScrollBars = ScrollBars.Vertical }), "raw");

            // --- sağ: açıklama paneli
            var hc = new Card { Bounds = R(620, 16, 320, 440) };
            Controls.Add(hc);
            help = new ReportBox { Bounds = R(14, 12, 292, 416) };
            hc.Add(help);
            help.BackColor = Theme.Card;

            Lbl("Komut önizleme", 620, 466, 300, Bold);
            tPreview = Add(Theme.Style(new TextBox { Bounds = R(620, 492, 320, 132), Multiline = true, ReadOnly = true, Font = Mono, ScrollBars = ScrollBars.Vertical }), "preview");

            var ok = new FlatBtn { Text = "Kaydet", Glyph = GSave, Kind = FlatBtn.Kinds.Primary, Bounds = R(620, 636, 150, 38), BackColor = Bg };
            var cancel = new FlatBtn { Text = "İptal", Kind = FlatBtn.Kinds.Secondary, Bounds = R(780, 636, 160, 38), BackColor = Bg };
            Controls.Add(ok); Controls.Add(cancel);
            ok.Click += (s, e) => Save();
            cancel.Click += (s, e) => { DialogResult = DialogResult.Cancel; Close(); };

            if (p.BuiltIn)
            {
                foreach (Control c in Controls)
                    if (!(c is Label) && !(c is Card) && c != cancel && c != tPreview && c != ok) c.Enabled = false;
                ok.Text = "Kopyasını düzenle";
                ok.Glyph = GCopy;
                cancel.Text = "Kapat";
            }

            foreach (Control c in Controls)
            {
                if (c is CheckX cb) cb.CheckedChanged += (s, e) => Sync();
                else if (c is ComboBox co) co.SelectedIndexChanged += (s, e) => Sync();
                else if (c is NumericUpDown nu) nu.ValueChanged += (s, e) => Sync();
                else if (c is TextBox tb && tb != tPreview) tb.TextChanged += (s, e) => Sync();
            }
            chkRaw.CheckedChanged += (s, e) =>
            {
                if (chkRaw.Checked && string.IsNullOrWhiteSpace(tRaw.Text)) tRaw.Text = Collect(new Profile()).BuildArgs(null);
            };
            Sync();
            ShowHelp("intro");
        }

        protected override void OnHandleCreated(EventArgs e)
        {
            base.OnHandleCreated(e);
            Native.DarkTitle(Handle, IsDark);
        }

        void ShowHelp(string key)
        {
            if (key == lastTopic) return;
            lastTopic = key;
            var t = ProfileHelp.Get(key);
            help.Clear();
            help.Out(t.Title, Theme.Text, Big);
            if (!string.IsNullOrEmpty(t.Flag)) help.Out(t.Flag, AccentInk, Mono);
            help.Blank();
            help.Out(t.Text, Theme.Text, Body);
            if (key == "dns" && cDns != null && cDns.SelectedIndex >= 0)
            {
                var prov = DnsProviders.Get(dnsItems[cDns.SelectedIndex].Id);
                if (prov != null) { help.Blank(); help.Out("Seçili: " + prov.Name, Theme.Text, Bold); help.Out(prov.Note, Muted, Small); }
            }
            if (key == "intro" || key == "name")
            {
                help.Blank();
                help.Out("BU PROFİLİN ÖZETİ", Muted, Caption);
                foreach (var (title, text) in ProfileHelp.Explain(Collect(new Profile())))
                    help.Out("• " + title, title.Contains("YOK") ? AmberText : Theme.Text, Small);
            }
            help.SelectionStart = 0;
            help.ScrollToCaret();
        }

        Profile Collect(Profile t)
        {
            t.Name = tName.Text.Trim();
            t.Description = tDesc.Text.Trim();
            t.Mode = Modes[Math.Max(0, cMode.SelectedIndex)].Value;
            t.Ttl = cTtl.SelectedIndex <= 0 ? "" : cTtl.SelectedIndex == 1 ? "auto" : (cTtl.SelectedIndex - 1).ToString();
            t.FakeGen = (int)nFake.Value;
            t.FakeResend = (int)nResend.Value;
            t.HttpsFrag = (int)nHttps.Value;
            t.HttpFrag = (int)nHttp.Value;
            t.Dns = dnsItems[Math.Max(0, cDns.SelectedIndex)].Id;
            t.CustomDns4 = tDns4.Text.Trim();
            t.CustomDns4Port = (int)nDns4Port.Value;
            t.CustomDns6 = tDns6.Text.Trim();
            t.CustomDns6Port = (int)nDns6Port.Value;
            t.WrongChksum = chkChk.Checked;
            t.WrongSeq = chkSeq.Checked;
            t.NativeFrag = chkNative.Checked;
            t.ReverseFrag = chkReverse.Checked;
            t.BlockQuic = chkQuic.Checked;
            t.FragBySni = chkSni.Checked;
            t.MaxPayload = chkPayload.Checked;
            t.AllowNoSni = chkNoSni.Checked;
            t.PassiveDpi = chkPassive.Checked;
            t.HostReplace = chkHostR.Checked;
            t.HostRemoveSpace = chkHostS.Checked;
            t.HostMixCase = chkHostM.Checked;
            t.ExtraArgs = tExtra.Text.Trim();
            t.UseRaw = chkRaw.Checked;
            t.RawArgs = tRaw.Text.Trim();
            return t;
        }

        void Sync()
        {
            if (tPreview == null) return;
            var tmp = Collect(new Profile());
            tPreview.Text = "goodbyedpi.exe " + tmp.BuildArgs(App.Settings.TargetedMode ? "…\\hedef-siteler.txt" : null);
            if (p.BuiltIn) return;
            bool raw = chkRaw.Checked;
            bool custom = dnsItems[Math.Max(0, cDns.SelectedIndex)].Id == DnsChoice.Custom;
            foreach (Control c in new Control[] { cMode, cTtl, nFake, nResend, nHttps, nHttp, cDns, chkChk, chkSeq, chkNative, chkReverse, chkQuic, chkSni, chkPayload, chkPassive, chkHostR, chkHostS, chkHostM, tExtra })
                c.Enabled = !raw;
            foreach (Control c in new Control[] { tDns4, tDns6, nDns4Port, nDns6Port, lDns4, lDns6 })
                c.Enabled = !raw && custom;
            tRaw.Enabled = raw;
        }

        void Save()
        {
            if (p.BuiltIn) { DialogResult = DialogResult.Retry; Close(); return; }
            var tmp = Collect(new Profile());
            if (tmp.Name.Length == 0) { Warn("Profil adı boş olamaz."); return; }
            if (!tmp.UseRaw && tmp.Dns == DnsChoice.Custom)
            {
                if (tmp.CustomDns4.Length > 0 && !(IPAddress.TryParse(tmp.CustomDns4, out var a4) && a4.AddressFamily == System.Net.Sockets.AddressFamily.InterNetwork))
                { Warn("Geçerli bir IPv4 DNS adresi girin (ör. 94.140.14.14)."); return; }
                if (tmp.CustomDns6.Length > 0 && !(IPAddress.TryParse(tmp.CustomDns6, out var a6) && a6.AddressFamily == System.Net.Sockets.AddressFamily.InterNetworkV6))
                { Warn("Geçerli bir IPv6 DNS adresi girin."); return; }
                if (tmp.CustomDns4.Length == 0 && tmp.CustomDns6.Length == 0) { Warn("Özel DNS için en az bir adres girin."); return; }
            }
            if (tmp.BuildArgs(null).Trim().Length == 0) { Warn("Profil hiçbir yöntem içermiyor. En azından bir temel mod ya da DNS seçin."); return; }
            if ((tmp.UseRaw ? tmp.RawArgs : tmp.ExtraArgs).Contains("--blacklist"))
            { Warn("--blacklist parametresini elle eklemeyin; 'Hedef Siteler' sayfasındaki hedefli modu kullanın."); return; }
            if (!tmp.UsesDnsRedirect &&
                MessageBox.Show(this, "Bu profilde DNS yönlendirmesi yok. Türkiye'de Discord DNS ile de engelli olduğu için bu profille Discord açılmayabilir.\n\nYine de kaydedilsin mi?",
                    App.Name, MessageBoxButtons.YesNo, MessageBoxIcon.Warning) != DialogResult.Yes) return;
            Collect(p);
            DialogResult = DialogResult.OK;
            Close();
        }

        void Warn(string m) => MessageBox.Show(this, m, App.Name, MessageBoxButtons.OK, MessageBoxIcon.Warning);
    }
}
