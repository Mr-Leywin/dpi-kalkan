using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Drawing;
using System.IO;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using System.Windows.Forms;
using static DPIKalkan.Theme;

namespace DPIKalkan
{
    sealed class LogPage : PageBase
    {
        public override string Key => "log";
        readonly RichTextBox box;
        readonly CheckX verbose, errorsOnly;

        public LogPage()
        {
            Header("Günlük", "Uygulamanın ve GoodbyeDPI'ın yaptığı her şey burada kayıt altına alınır. Hatalar [DK-xxx] koduyla yazılır.");

            verbose = new CheckX { Text = "Ayrıntılı (komutlar)", Bounds = R(28, 82, 170, 24), BackColor = Bg };
            errorsOnly = new CheckX { Text = "Yalnızca uyarı/hata", Bounds = R(204, 82, 170, 24), BackColor = Bg };
            Controls.Add(verbose);
            Controls.Add(errorsOnly);
            verbose.CheckedChanged += (s, e) => Rebuild();
            errorsOnly.CheckedChanged += (s, e) => Rebuild();

            int x = 792;
            FlatBtn B(string t, string g, int w)
            {
                x -= w;
                var b = Btn(t, g, FlatBtn.Kinds.Secondary, x, 76, w, 36);
                x -= 8;
                return b;
            }
            var bFolder = B("Klasör", GFolder, 96);
            var bSave = B("Kaydet", GSave, 96);
            var bCopy = B("Kopyala", GCopy, 102);
            var bClear = B("Temizle", GDelete, 102);

            box = new RichTextBox
            {
                Bounds = R(28, 124, 764, 500), ReadOnly = true, BorderStyle = BorderStyle.None,
                BackColor = LogBg, ForeColor = LogText, Font = Mono, DetectUrls = false, WordWrap = true,
            };
            Theme.StyleScroll(box);
            if (Lum(LogBg) < 0.4) box.HandleCreated += (s, e) => Native.ThemeWindow(box.Handle, "DarkMode_Explorer");
            Controls.Add(box);

            bClear.Click += (s, e) => { App.ClearLog(); box.Clear(); };
            bCopy.Click += (s, e) => { if (box.TextLength > 0) Clipboard.SetText(box.Text); };
            bSave.Click += (s, e) =>
            {
                using (var d = new SaveFileDialog { FileName = "dpikalkan-gunluk-" + DateTime.Now.ToString("yyyyMMdd-HHmm") + ".txt", Filter = "Metin dosyası|*.txt" })
                    if (d.ShowDialog(FindForm()) == DialogResult.OK) File.WriteAllText(d.FileName, box.Text, Encoding.UTF8);
            };
            bFolder.Click += (s, e) => Sys.OpenFolder(App.DataDir);

            OnLog(Append);
            Rebuild();
        }

        static Color ColorOf(LogLevel l)
        {
            switch (l)
            {
                case LogLevel.Success: return LogGreen;
                case LogLevel.Warn: return LogAmber;
                case LogLevel.Error: return LogRed;
                case LogLevel.Debug: return LogMuted;
                case LogLevel.Output: return LogBlue;
                default: return LogText;
            }
        }

        void Rebuild()
        {
            box.Clear();
            var items = App.LogSnapshot();
            foreach (var e in items.Skip(Math.Max(0, items.Length - 1500))) Append(e);
        }

        void Append(LogEntry e)
        {
            if (e.Level == LogLevel.Debug && !verbose.Checked) return;
            if (errorsOnly.Checked && e.Level != LogLevel.Warn && e.Level != LogLevel.Error) return;
            if (box.TextLength > 300000)
            {
                box.Select(0, box.TextLength / 2);
                box.SelectedText = "";
            }
            box.SelectionStart = box.TextLength;
            box.SelectionLength = 0;
            box.SelectionColor = LogMuted;
            box.AppendText(e.Time.ToString("HH:mm:ss") + "  ");
            box.SelectionColor = ColorOf(e.Level);
            string prefix = e.Level == LogLevel.Output ? "goodbyedpi │ " : "";
            box.AppendText(prefix + e.Text + "\n");
            if (Visible) box.ScrollToCaret();
        }

        public override void OnActivated()
        {
            box.SelectionStart = box.TextLength;
            box.ScrollToCaret();
        }
    }

    sealed class SettingsPage : PageBase
    {
        public override string Key => "settings";
        readonly ScrollArea scroll;
        int y;
        Card cur;
        int curRows;
        readonly List<(ToggleRow Row, Func<bool> Get)> rows = new List<(ToggleRow, Func<bool>)>();
        ToggleRow rStartWin, rService;

        public SettingsPage()
        {
            Header("Ayarlar", null);
            scroll = new ScrollArea { Bounds = R(0, 48, 820, 592) };
            Controls.Add(scroll);
            y = 4;

            // ------------------------------------------------ arayüz
            Caption("ARAYÜZ");
            var lc = new Card { Bounds = R(28, y, 764, 120) };
            scroll.Controls.Add(lc);
            lc.Add(UIx.L("Arayüz düzeyi", Bold, Theme.Text, 20, 14, 300, 22));
            lc.Add(UIx.L("Basit: yalnızca gerekenler (önerilen). Orta: profiller, internet kontrolü, DNS. Detaylı: her şey, günlük ve uzman seçenekleri.",
                Small, Muted, 20, 36, 420, 40));
            var seg = lc.Add(new Segmented("Basit", "Orta", "Detaylı") { Bounds = R(456, 18, 288, 36) });
            seg.SetSilently((int)App.Settings.UiLevel);
            seg.SelectedChanged += (s, e) =>
            {
                App.Settings.UiLevel = (UiLevel)seg.SelectedIndex;
                App.Save();
                BeginInvoke(new Action(() => MainForm.Instance.Rebuild()));
            };
            var bLook = lc.Add(new FlatBtn { Text = "Renkler ve tema…", Glyph = GColor, Kind = FlatBtn.Kinds.Ghost, Bounds = R(12, 78, 190, 32) });
            bLook.Click += (s, e) => MainForm.Instance.Navigate("appearance");
            lc.Add(UIx.L("Tema: " + (Theme.Current?.Name ?? ""), Small, Muted, 210, 84, 530, 22));
            y += 120 + 22;

            // ------------------------------------------------ başlangıç
            Section("BAŞLANGIÇ", Level(UiLevel.Orta) ? 3 : 2);
            rStartWin = Row("Windows açılışında uygulamayı başlat",
                "Oturum açınca DPI Kalkan sistem tepsisinde, yönetici izni sormadan başlar.", () => App.Settings.StartWithWindows);
            Row("Uygulama açılınca korumayı otomatik başlat", "Seçili yöntemle koruma hemen devreye girer.",
                () => App.Settings.AutoStartProtection, v => App.Settings.AutoStartProtection = v);
            if (Level(UiLevel.Orta))
                rService = Row("Arka plan hizmeti modu",
                    "GoodbyeDPI Windows hizmeti olarak kurulur: oturum açılmadan başlar, uygulama kapalıyken de çalışır.", () => App.Settings.ServiceMode);
            End();

            // ------------------------------------------------ pencere
            Section("PENCERE VE BİLDİRİMLER", 4);
            Row("Kapat düğmesi sistem tepsisine küçültsün", "Kapalıysa pencereyi kapatmak uygulamadan çıkar.",
                () => App.Settings.CloseToTray, v => App.Settings.CloseToTray = v);
            Row("Görev çubuğunda gösterme", "Uygulama yalnızca saat yanındaki sistem tepsisinde görünür (Alt+Tab'da da görünmez).",
                () => App.Settings.HideFromTaskbar, v => { App.Settings.HideFromTaskbar = v; MainForm.Instance.ApplyTaskbarSetting(); });
            Row("Bildirimleri göster", "Koruma başladığında, durduğunda veya erişim sorunu olduğunda bildirim gösterir.",
                () => App.Settings.Notifications, v => App.Settings.Notifications = v);
            Row("Kısayol tuşu: " + MainForm.HotkeyText, MainForm.HotkeyText + " ile korumayı her yerden açıp kapatın (pencere gizliyken de çalışır).",
                () => App.Settings.Hotkey, v => { App.Settings.Hotkey = v; MainForm.Instance.ApplyHotkey(); });
            End();

            // ------------------------------------------------ koruma
            bool det = Level(UiLevel.Detayli);
            Section("KORUMA", Level(UiLevel.Orta) ? (det ? 7 : 6) : 3);
            Row("Erişim kesilirse çalışan yöntemi otomatik bul",
                "Koruma açıkken Discord'a erişilemezse (ör. servis sağlayıcı yöntem değiştirdiğinde) yöntemler kendiliğinden denenir ve çalışan seçilir.",
                () => App.Settings.AutoHeal, v => App.Settings.AutoHeal = v);
            if (Level(UiLevel.Orta))
                SegRow("Bağlantıyı düzenli kontrol et", "Belirli aralıklarla ve ağ değiştiğinde (ör. Wi-Fi değişimi) Discord erişimi sessizce test edilir.",
                    new[] { "Kapalı", "5 dk", "15 dk", "30 dk" }, new[] { 0, 5, 15, 30 },
                    () => App.Settings.WatchMinutes, v => { App.Settings.WatchMinutes = v; MainForm.Instance.ApplyWatchSetting(); });
            Row("Kapanırsa otomatik yeniden başlat", "GoodbyeDPI beklenmedik şekilde kapanırsa 5 saniye sonra yeniden başlatılır (DK-602).",
                () => App.Settings.AutoRestart, v => App.Settings.AutoRestart = v);
            Row("Discord'u yeniden başlatmayı öner", "Discord korumadan önce açıldıysa bağlanamayabilir; bu durumda uyarı ve düğme gösterilir (DK-901).",
                () => App.Settings.SuggestDiscordRestart, v => App.Settings.SuggestDiscordRestart = v);
            if (Level(UiLevel.Orta))
            {
                Row("Başlattıktan sonra bağlantıyı test et", "Koruma başlayınca Discord sunucularına erişim ve DNS yanıtı otomatik kontrol edilir.",
                    () => App.Settings.TestAfterStart, v => App.Settings.TestAfterStart = v);
                Row("Değişikliklerde DNS önbelleğini temizle", "Başlatma/durdurma sırasında eski (engelli) DNS kayıtlarının kullanılmasını önler.",
                    () => App.Settings.FlushDns, v => App.Settings.FlushDns = v);
            }
            if (det)
                Row("Başlatırken DNS sunucusunu doğrula", "Yönlendirilen DNS sunucusu yanıt vermiyorsa uyarır (DK-402). Kapatırsanız başlatma ~1 sn hızlanır.",
                    () => App.Settings.CheckDnsOnStart, v => App.Settings.CheckDnsOnStart = v);
            End();

            // ------------------------------------------------ onarım
            Caption("ONARIM");
            var c4 = new Card { Bounds = R(28, y, 764, 150) };
            scroll.Controls.Add(c4);
            y += 150 + 22;
            c4.Add(UIx.L("Sorun yaşıyorsanız önce Sorun Giderme → 'Tam kontrolü başlat'ı deneyin. 'Tümünü sıfırla', eski .cmd betiklerinin kurduğu " +
                         "GoodbyeDPI hizmetini ve WinDivert sürücüsünü de kaldırır (eski ayarlari_sifirla.cmd'nin daha kapsamlısı).",
                Small, Muted, 20, 14, 724, 40));
            var bFlush = c4.Add(new FlatBtn { Text = "DNS önbelleğini temizle", Glyph = GRefresh, Kind = FlatBtn.Kinds.Secondary, Bounds = R(20, 62, 228, 38) });
            var bKill = c4.Add(new FlatBtn { Text = "Çakışmaları gider", Glyph = GWrench, Kind = FlatBtn.Kinds.Secondary, Bounds = R(258, 62, 228, 38) });
            var bReset = c4.Add(new FlatBtn { Text = "Tümünü sıfırla", Glyph = GDelete, Kind = FlatBtn.Kinds.Danger, Bounds = R(496, 62, 228, 38) });
            var info = c4.Add(UIx.L("", Small, Muted, 20, 108, 724, 34));
            bFlush.Click += async (s, e) => info.Text = await Fixes.Run(FixKind.FlushDns, FindForm());
            bKill.Click += async (s, e) => { bKill.Enabled = false; info.Text = await Fixes.Run(FixKind.ResolveConflicts, FindForm()); bKill.Enabled = true; };
            bReset.Click += async (s, e) =>
            {
                bReset.Enabled = false;
                string m = await Fixes.Run(FixKind.ResetAll, FindForm());
                if (IsDisposed) return;
                if (m != null) info.Text = m;
                LoadValues();
                bReset.Enabled = true;
            };

            // ------------------------------------------------ güncellemeler
            Caption("GÜNCELLEMELER");
            var cu = new Card { Bounds = R(28, y, 764, 124) };
            scroll.Controls.Add(cu);
            y += 124 + 22;
            var rUpd = new ToggleRow("Güncellemeleri otomatik denetle",
                "Günde bir kez GitHub'daki yeni sürüm denetlenir; yeni sürüm varsa bildirilir. Kurulum her zaman sizin onayınızla ve SHA-256 doğrulamasıyla yapılır.",
                D(744)) { Location = new Point(D(10), D(6)), Divider = false };
            cu.Add(rUpd);
            rUpd.Switch.SetSilently(App.Settings.AutoUpdate);
            rUpd.Switch.CheckedChanged += (s, e) => { App.Settings.AutoUpdate = rUpd.Switch.Checked; App.Save(); };
            var bCheck = cu.Add(new FlatBtn { Text = "Şimdi denetle", Glyph = GRefresh, Kind = FlatBtn.Kinds.Secondary, Bounds = R(30, 74, 170, 36) });
            var updInfo = cu.Add(UIx.L("", Small, Muted, 212, 74, 530, 36, ContentAlignment.MiddleLeft));
            Action showUpd = () =>
            {
                var last = App.Settings.LastUpdateCheck;
                updInfo.Text = "Sürüm " + App.Version + " · Son denetim: " + (last.HasValue ? last.Value.ToString("dd.MM.yyyy HH:mm") : "hiç") +
                               (Updater.Available != null ? " · Yeni sürüm hazır: " + Updater.Available.Version : "");
                updInfo.ForeColor = Updater.Available != null ? AccentInk : Muted;
            };
            showUpd();
            bCheck.Click += async (s, e) =>
            {
                bCheck.Enabled = false;
                await UpdateDialog.CheckAndShow(FindForm(), manual: true);
                if (IsDisposed) return;
                bCheck.Enabled = true;
                showUpd();
            };

            // ------------------------------------------------ yedekleme
            if (Level(UiLevel.Orta))
            {
                Caption("YEDEKLEME");
                var cb = new Card { Bounds = R(28, y, 764, 110) };
                scroll.Controls.Add(cb);
                y += 110 + 22;
                cb.Add(UIx.L("Tüm ayarları (yöntemler, özel profiller, tema, proxy'ler, hedef siteler) tek bir dosyaya kaydedin; yeni bilgisayara ya da arkadaşınıza taşıyın. " +
                             "Proxy şifreleri yalnızca bu Windows hesabında çözülebildiği için başka bilgisayarda yeniden girilmelidir.", Small, Muted, 20, 12, 724, 34));
                var bExp = cb.Add(new FlatBtn { Text = "Ayarları dışa aktar…", Glyph = GSave, Kind = FlatBtn.Kinds.Secondary, Bounds = R(20, 54, 228, 38) });
                var bImp = cb.Add(new FlatBtn { Text = "Ayarları içe aktar…", Glyph = GFolder, Kind = FlatBtn.Kinds.Secondary, Bounds = R(258, 54, 228, 38) });
                bExp.Click += (s, e) =>
                {
                    using (var d = new SaveFileDialog { FileName = "DPI-Kalkan-ayarlar-" + DateTime.Now.ToString("yyyyMMdd") + ".xml", Filter = "DPI Kalkan ayarları (*.xml)|*.xml" })
                    {
                        if (d.ShowDialog(FindForm()) != DialogResult.OK) return;
                        try { SettingsStore.Export(App.Settings, d.FileName); App.Log("Ayarlar dışa aktarıldı: " + d.FileName, LogLevel.Success); }
                        catch (Exception ex) { MessageBox.Show(FindForm(), "Kaydedilemedi: " + ex.Message, App.Name); }
                    }
                };
                bImp.Click += async (s, e) =>
                {
                    using (var d = new OpenFileDialog { Filter = "DPI Kalkan ayarları (*.xml)|*.xml" })
                    {
                        if (d.ShowDialog(FindForm()) != DialogResult.OK) return;
                        AppSettings imported;
                        try { imported = SettingsStore.Import(d.FileName); }
                        catch (Exception ex) { MessageBox.Show(FindForm(), "Dosya okunamadı: " + ex.Message, App.Name, MessageBoxButtons.OK, MessageBoxIcon.Warning); return; }
                        if (MessageBox.Show(FindForm(), "Şu anki tüm ayarlar dosyadakilerle değiştirilecek. Devam edilsin mi?", App.Name,
                                MessageBoxButtons.YesNo, MessageBoxIcon.Question) != DialogResult.Yes) return;
                        bool wasActive = App.Engine.IsActive;
                        // bu bilgisayara özgü durumlar korunur
                        imported.DnsBackups = App.Settings.DnsBackups;
                        imported.SystemProxyBackup = App.Settings.SystemProxyBackup;
                        imported.SystemProxyId = App.Settings.SystemProxyId;
                        imported.ServiceMode = App.Settings.ServiceMode;
                        imported.StartWithWindows = App.Settings.StartWithWindows;
                        App.Settings = imported;
                        App.Save();
                        App.Log("Ayarlar içe aktarıldı: " + d.FileName, LogLevel.Success);
                        ProxyBridge.StopAll();
                        ProxyBridge.Sync();
                        MainForm.Instance.ApplyHotkey();
                        MainForm.Instance.ApplyWatchSetting();
                        MainForm.Instance.ApplyTheme();
                        if (wasActive) await Actions.StartAsync();
                    }
                };
            }

            // ------------------------------------------------ gelişmiş
            if (det)
            {
                Caption("GELİŞMİŞ");
                var c5 = new Card { Bounds = R(28, y, 764, 110) };
                scroll.Controls.Add(c5);
                y += 110 + 22;
                c5.Add(UIx.L("Tüm hazır yöntemleri, uygulama olmadan çalışan .cmd betikleri olarak bir klasöre kaydedebilirsiniz.", Small, Muted, 20, 14, 724, 22));
                var bExport = c5.Add(new FlatBtn { Text = "Betikleri dışa aktar…", Glyph = GSave, Kind = FlatBtn.Kinds.Secondary, Bounds = R(20, 46, 228, 38) });
                var bData = c5.Add(new FlatBtn { Text = "Ayar klasörünü aç", Glyph = GFolder, Kind = FlatBtn.Kinds.Secondary, Bounds = R(258, 46, 228, 38) });
                bData.Click += (s, e) => Sys.OpenFolder(App.DataDir);
                bExport.Click += (s, e) =>
                {
                    using (var d = new FolderBrowserDialog { Description = "Betiklerin kaydedileceği klasörü seçin" })
                    {
                        if (d.ShowDialog(FindForm()) != DialogResult.OK) return;
                        try
                        {
                            string dir = Path.Combine(d.SelectedPath, "DPI Kalkan betikleri");
                            Directory.CreateDirectory(dir);
                            string binRoot = Path.GetDirectoryName(Engine.BinDir);
                            int i = 0;
                            foreach (var p in App.AllProfiles.OrderBy(x => x.Priority))
                                Scripts.Write(p, Path.Combine(dir, (++i).ToString("00") + "_" + Scripts.Slug(p.Name) + ".cmd"), binRoot);
                            Sys.OpenFolder(dir);
                            App.Log(i + " betik dışa aktarıldı: " + dir, LogLevel.Success);
                        }
                        catch (Exception ex) { MessageBox.Show(FindForm(), "Dışa aktarılamadı: " + ex.Message, App.Name); }
                    }
                };
            }

            scroll.Controls.Add(new Panel { Bounds = R(0, y, 10, 10), BackColor = Bg });
            WireSpecial();
            LoadValues();
        }

        void Caption(string t)
        {
            scroll.Controls.Add(UIx.L(t, Theme.Caption, Muted, 30, y, 300, 20));
            y += 24;
        }

        void Section(string title, int count)
        {
            Caption(title);
            cur = new Card { Bounds = R(28, y, 764, 64 * count + 12) };
            scroll.Controls.Add(cur);
            curRows = 0;
            y += 64 * count + 12 + 22;
        }

        void End() { }

        /// <summary>Başlık + açıklama + seçenek grubu satırı.</summary>
        void SegRow(string title, string desc, string[] items, int[] values, Func<int> get, Action<int> set)
        {
            var row = new Panel { Bounds = new Rectangle(D(10), D(6 + 64 * curRows), D(744), D(64)), BackColor = Theme.Card };
            row.Controls.Add(new Label { Text = title, Font = Bold, ForeColor = Theme.Text, BackColor = Theme.Card, AutoSize = false, UseMnemonic = false,
                Bounds = new Rectangle(D(20), D(10), D(440), D(22)) });
            row.Controls.Add(new Label { Text = desc, Font = Small, ForeColor = Muted, BackColor = Theme.Card, AutoSize = false, UseMnemonic = false,
                Bounds = new Rectangle(D(20), D(31), D(440), D(32)) });
            var seg = new Segmented(items) { Bounds = new Rectangle(D(470), D(14), D(258), D(34)), BackColor = Theme.Card, Font = Small };
            int idx = Array.IndexOf(values, get());
            seg.SetSilently(idx < 0 ? 0 : idx);
            seg.SelectedChanged += (s, e) => { set(values[seg.SelectedIndex]); App.Save(); };
            row.Controls.Add(seg);
            row.Paint += (s, e) => { using (var pen = new Pen(Theme.Divider)) e.Graphics.DrawLine(pen, D(20), row.Height - 1, row.Width - D(20), row.Height - 1); };
            cur.Controls.Add(row);
            curRows++;
        }

        ToggleRow Row(string title, string desc, Func<bool> get, Action<bool> set = null)
        {
            var r = new ToggleRow(title, desc, D(744)) { Location = new Point(D(10), D(6 + 64 * curRows)) };
            cur.Add(r);
            curRows++;
            r.Divider = true;
            rows.Add((r, get));
            if (set != null)
                r.Switch.CheckedChanged += (s, e) => { set(r.Switch.Checked); App.Save(); App.RaiseStatus(); };
            return r;
        }

        void LoadValues()
        {
            foreach (var (r, get) in rows) r.Switch.SetSilently(get());
        }

        public override void OnActivated() => LoadValues();

        void WireSpecial()
        {
            rStartWin.Switch.CheckedChanged += async (s, e) =>
            {
                bool want = rStartWin.Switch.Checked;
                if (want && App.RunningFromTemp)
                {
                    MessageBox.Show(FindForm(), "Uygulama geçici bir klasörden çalışıyor (DK-103). Önce zip'i kalıcı bir klasöre çıkarın, sonra bu seçeneği açın.",
                        App.Name, MessageBoxButtons.OK, MessageBoxIcon.Warning);
                    rStartWin.Switch.SetSilently(false);
                    return;
                }
                rStartWin.Switch.Enabled = false;
                string err = null;
                bool ok = await Task.Run(() =>
                {
                    if (want) return Startup.Enable(out err);
                    Startup.Disable();
                    return true;
                });
                if (IsDisposed) return;
                rStartWin.Switch.Enabled = true;
                if (!ok)
                {
                    rStartWin.Switch.SetSilently(false);
                    App.Problems.Report("DK-703", err);
                    MessageBox.Show(FindForm(), "Açılış görevi oluşturulamadı (DK-703):\n" + err, App.Name, MessageBoxButtons.OK, MessageBoxIcon.Error);
                    return;
                }
                App.Problems.Clear("DK-703");
                App.Settings.StartWithWindows = want;
                App.Save();
            };

            if (rService == null) return;
            rService.Switch.CheckedChanged += async (s, e) =>
            {
                bool want = rService.Switch.Checked;
                if (want && MessageBox.Show(FindForm(),
                        "GoodbyeDPI 'DPI Kalkan' adıyla Windows hizmeti olarak kurulacak ve bilgisayar açılınca otomatik başlayacak.\n\n" +
                        "Uygulamayı kapatsanız bile koruma çalışmaya devam eder. Koruma şimdi bu modda yeniden başlatılsın mı?",
                        App.Name, MessageBoxButtons.YesNo, MessageBoxIcon.Question) != DialogResult.Yes)
                {
                    rService.Switch.SetSilently(false);
                    return;
                }
                rService.Switch.Enabled = false;
                bool wasActive = App.Engine.IsActive;
                App.Settings.ServiceMode = want;
                App.Save();
                if (want)
                {
                    await Actions.StartAsync();
                    if (App.Engine.State != EngineState.Running)
                    {
                        App.Settings.ServiceMode = false;
                        App.Save();
                        await Task.Run(() => App.Engine.UninstallService());
                        rService.Switch.SetSilently(false);
                        MessageBox.Show(FindForm(), "Hizmet modu başlatılamadı, uygulama moduna dönüldü.\n\n" + App.Engine.LastErrorCode + " " + App.Engine.LastError,
                            App.Name, MessageBoxButtons.OK, MessageBoxIcon.Warning);
                        if (wasActive) await Actions.StartAsync();
                    }
                }
                else
                {
                    await Task.Run(() => App.Engine.UninstallService());
                    if (wasActive) await Actions.StartAsync();
                }
                if (IsDisposed) return;
                rService.Switch.Enabled = true;
                App.RaiseStatus();
            };
        }
    }

    sealed class AboutPage : PageBase
    {
        public override string Key => "about";

        public AboutPage()
        {
            Header("Hakkında", null);
            var c = AddCard(28, 52, 764, 226);
            var logo = new PictureBox { Bounds = R(28, 28, 96, 96), Image = IconArt.AppBitmap(D(96)), SizeMode = PictureBoxSizeMode.Zoom };
            c.Add(logo);
            c.Add(UIx.L(App.Name, Hero, Theme.Text, 146, 24, 400, 44));
            c.Add(UIx.L("Sürüm " + App.Version + "  ·  GoodbyeDPI 0.2.3rc3  ·  WinDivert 2.2", Body, Muted, 149, 70, 560, 22));
            c.Add(UIx.L("GoodbyeDPI için kolay kullanımlı internet erişim kalkanı. Servis sağlayıcıların DNS ve Derin Paket İnceleme (DPI) ile " +
                        "uyguladığı engelleri aşar. VPN değildir: trafiğiniz şifrelenmez ve IP adresiniz değişmez.",
                Body, Theme.Text, 149, 96, 590, 64));
            int bx = 146;
            FlatBtn B(string t, string g, int w, Action a)
            {
                var b = c.Add(new FlatBtn { Text = t, Glyph = g, Kind = FlatBtn.Kinds.Secondary, Bounds = R(bx, 170, w, 36) });
                b.Click += (s, e) => a();
                bx += w + 8;
                return b;
            }
            B("Kullanım rehberi", GHelp, 156, () =>
            {
                string guide = Path.Combine(App.BaseDir, "Rehber.html");
                if (File.Exists(guide)) Sys.OpenAsUser(guide); else MainForm.Instance.Navigate("trouble");
            });
            B("Proje sayfası", GLink, 140, () => Sys.OpenAsUser(App.RepoUrl));
            B("Yedek betikler", GFolder, 140, () =>
            {
                string dir = Path.Combine(App.BaseDir, "betikler");
                Sys.OpenFolder(Directory.Exists(dir) ? dir : App.BaseDir);
            });
            B("Lisanslar", GInfo, 118, () =>
            {
                string dir = Path.Combine(App.BaseDir, "lisanslar");
                Sys.OpenFolder(Directory.Exists(dir) ? dir : App.BaseDir);
            });

            var c2 = AddCard(28, 292, 764, 336);
            var r = c2.Add(new ReportBox { Bounds = R(20, 14, 724, 310) });
            r.BackColor = Theme.Card;
            r.Out("2.3'TE YENİ", Muted, Theme.Caption);
            foreach (var line in new[]
            {
                "Otomatik güncelleme: yeni sürüm GitHub'dan SHA-256 doğrulamasıyla indirilir ve ayarlar korunarak kurulur (Ayarlar → Güncellemeler).",
                "Sorun Giderme: kişisel bilgileri gizlenmiş destek paketi ve GitHub'da tek tıkla sorun bildirme.",
            })
                r.Out("•  " + line, Theme.Text, Small);
            r.Blank();
            r.Out("2.2'DE YENİ", Muted, Theme.Caption);
            foreach (var line in new[]
            {
                "Program bazlı proxy: proxy'yi yalnızca seçtiğiniz programlarda (Chrome, Edge, Opera, Firefox, Discord, Spotify…) kullanın; masaüstü kısayolu oluşturun.",
                "Yerel proxy köprüsü: şifreli (kullanıcı adı/şifreli) proxy'leri, şifreyi desteklemeyen programlar da kullanabilir.",
                "Toplu proxy testi (listeyi yapıştır, çalışanları ekle) ve proxy türünü (HTTP/SOCKS5) otomatik düzeltme.",
                "İnternet Kontrolü: hız testi ve grafikli bağlantı geçmişi. Ayarlar: tüm ayarları dışa/içe aktarma.",
            })
                r.Out("•  " + line, Theme.Text, Small);
            r.Blank();
            r.Out("2.1'DE YENİ", Muted, Theme.Caption);
            foreach (var line in new[]
            {
                "Otomatik onarım: Discord'a erişilemezse çalışan yöntem kendiliğinden bulunur. Bağlantı bekçisi düzenli aralıklarla ve ağ değişince kontrol eder.",
                "Daha akıllı tarama: başarısız olan DPI yönteminin farklı DNS'li kopyaları atlanır (ör. -9 çalışmıyorsa doğrudan -5'e geçilir).",
                "Proxy sayfası: HTTP/SOCKS5 proxy kaydetme, test (çıkış IP'si ve ülke), Windows proxy'si, Discord'u proxy ile açma ve kısayol.",
                "Profil paylaşma kodu, " + MainForm.HotkeyText + " kısayol tuşu, Roblox/Wattpad hazır site listeleri.",
            })
                r.Out("•  " + line, Theme.Text, Small);
            r.Blank();
            r.Out("2.0'DA YENİ", Muted, Theme.Caption);
            foreach (var line in new[]
            {
                "Doğru çalışan yöntem taraması: DNS önbelleği atlanır, DNS yanıtı doğrulanır, her yöntem iki tur test edilir (1.0'da DNS'siz yöntemler yanlışlıkla 'çalışıyor' görünebiliyordu).",
                Profiles.BuiltIns.Count + " hazır yöntem, " + DnsProviders.All.Count + " DNS sunucusu (Yandex, AdGuard, Quad9, OpenDNS, Cloudflare, Google) ve eski betiklerin DNS'li sürümleri.",
                "Sorun Giderme: 20+ noktalı tam kontrol, " + ErrorCatalog.Categories.Length + " kategoride " + ErrorCatalog.All.Count + " hata kodu ve tek tıkla düzeltme.",
                "İnternet Kontrolü: site analizi (DNS/TCP/TLS), DNS sunucu testi, ağ bilgisi. DNS Ayarları: Windows DNS ve şifreli DNS (DoH).",
                "Görünüm: " + (Palettes.Presets.Count + 1) + " hazır tema, renk kodu girilebilen özel palet, tema kodu paylaşma. Basit / Orta / Detaylı arayüz.",
            })
                r.Out("•  " + line, Theme.Text, Small);
            r.Blank();
            r.Out("NASIL ÇALIŞIR?", Muted, Theme.Caption);
            r.Out("Servis sağlayıcınız Discord'u iki yolla engeller: DNS yanıtlarını değiştirerek ve bağlantıdaki site adını (SNI) okuyup bağlantıyı keserek. " +
                  "DNS yönlendirmesi sorguları engelsiz bir DNS sunucusuna gönderir; GoodbyeDPI ise paketleri parçalayıp sahte paketler göndererek DPI cihazının site adını okumasını engeller.",
                Theme.Text, Small);
            r.Blank();
            r.Out("Antivirüs uyarısı: WinDivert bir ağ sürücüsüdür; bazı antivirüsler (Kaspersky, Norton…) onu 'RiskTool' olarak işaretleyebilir. Bu bir virüs değildir; " +
                  "uygulama klasörünü istisnalara ekleyebilirsiniz (DK-204).", Theme.Text, Small);
            r.Blank();
            r.Out("Teşekkürler: GoodbyeDPI – ValdikSS (Apache 2.0) · WinDivert – basil00 (LGPLv3) · uthash, getline.", Muted, Small);
            r.SelectionStart = 0;
        }
    }
}
