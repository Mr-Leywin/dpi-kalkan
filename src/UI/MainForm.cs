using System;
using System.Collections.Generic;
using System.Drawing;
using System.IO;
using System.Linq;
using System.Threading.Tasks;
using System.Windows.Forms;
using Microsoft.Win32;
using static DPIKalkan.Theme;

namespace DPIKalkan
{
    sealed class MainForm : Form
    {
        public static MainForm Instance;

        Panel sidebar, topbar, host;
        readonly List<NavItem> navs = new List<NavItem>();
        readonly Dictionary<string, PageBase> pages = new Dictionary<string, PageBase>();
        SideStatus sideStatus;
        PageBase current;
        readonly ToolTip tips = new ToolTip();

        readonly NotifyIcon tray;
        readonly ContextMenuStrip trayMenu;
        ToolStripMenuItem miStatus, miToggle, miProfiles, miTest, miScan, miDiscord, miOpen, miExit;
        readonly Dictionary<EngineState, Icon> trayIcons = new Dictionary<EngineState, Icon>();
        readonly Timer pollTimer = new Timer { Interval = 3000 };
        readonly Timer themeTimer = new Timer { Interval = 600 };
        readonly Timer watchTimer = new Timer();
        readonly Timer netChangeTimer = new Timer { Interval = 10000 };
        bool exiting;

        [System.Runtime.InteropServices.DllImport("user32.dll")] static extern bool RegisterHotKey(IntPtr h, int id, uint mods, uint vk);
        [System.Runtime.InteropServices.DllImport("user32.dll")] static extern bool UnregisterHotKey(IntPtr h, int id);
        const int WM_HOTKEY = 0x0312, HotkeyId = 0x4B44;
        public const string HotkeyText = "Ctrl+Alt+K";

        /// <summary>Kenar çubuğundaki sayfalar ve hangi arayüz düzeyinde göründükleri.</summary>
        static readonly (string Key, string Title, string Glyph, UiLevel Min)[] NavDefs =
        {
            ("home", "Ana Sayfa", GHome, UiLevel.Basit),
            ("profiles", "Profiller", GProfiles, UiLevel.Orta),
            ("net", "İnternet Kontrolü", GNetwork, UiLevel.Orta),
            ("dns", "DNS Ayarları", GGlobe, UiLevel.Orta),
            ("sites", "Hedef Siteler", GList, UiLevel.Orta),
            ("proxy", "Proxy", GLink, UiLevel.Orta),
            ("trouble", "Sorun Giderme", GHelp, UiLevel.Basit),
            ("log", "Günlük", GLog, UiLevel.Detayli),
            ("appearance", "Görünüm", GColor, UiLevel.Basit),
            ("settings", "Ayarlar", GSettings, UiLevel.Basit),
            ("about", "Hakkında", GInfo, UiLevel.Basit),
        };

        public MainForm()
        {
            Instance = this;
            Text = App.Name;
            FormBorderStyle = FormBorderStyle.None;
            StartPosition = FormStartPosition.CenterScreen;
            AutoScaleMode = AutoScaleMode.None;
            ClientSize = new Size(D(1040), D(680));
            Font = Body;
            ShowInTaskbar = !App.Settings.HideFromTaskbar;
            Icon = IconArt.AppIcon(32);
            DoubleBuffered = true;
            KeyPreview = true;

            BuildChrome();

            // --- tepsi
            trayMenu = BuildTrayMenu();
            tray = new NotifyIcon { ContextMenuStrip = trayMenu, Visible = !App.Preview, Text = App.Name };
            tray.MouseClick += (s, e) => { if (e.Button == MouseButtons.Left) ShowFromTray(); };
            tray.BalloonTipClicked += (s, e) => ShowFromTray();

            App.Engine.StateChanged += OnEngineStateChanged;
            App.StatusChanged += RefreshChrome;
            pollTimer.Tick += (s, e) => Task.Run(() => App.Engine.Poll());
            pollTimer.Start();

            // Sistem teması seçiliyse Windows renk/tema değişikliklerini izle
            themeTimer.Tick += (s, e) => { themeTimer.Stop(); if (App.Settings.ThemeId == "system") ApplyTheme(); };
            SystemEvents.UserPreferenceChanged += OnUserPreferenceChanged;

            // Bağlantı bekçisi: belirli aralıklarla ve ağ değişince test et, gerekirse onar
            watchTimer.Tick += async (s, e) => await Actions.WatchCheckAsync("düzenli kontrol");
            ApplyWatchSetting();
            netChangeTimer.Tick += async (s, e) => { netChangeTimer.Stop(); await Actions.WatchCheckAsync("ağ değişti"); };
            if (!App.Preview) System.Net.NetworkInformation.NetworkChange.NetworkAddressChanged += OnNetworkChanged;

            Navigate("home");
            RefreshChrome();
        }

        void OnNetworkChanged(object sender, EventArgs e)
        {
            try { BeginInvoke(new Action(() => { netChangeTimer.Stop(); netChangeTimer.Start(); })); } catch { }
        }

        public void ApplyWatchSetting()
        {
            watchTimer.Stop();
            int m = App.Settings.WatchMinutes;
            if (m > 0 && !App.Preview)
            {
                watchTimer.Interval = m * 60 * 1000;
                watchTimer.Start();
            }
        }

        public void ApplyHotkey()
        {
            if (!IsHandleCreated || App.Preview) return;
            UnregisterHotKey(Handle, HotkeyId);
            if (!App.Settings.Hotkey) return;
            // MOD_ALT | MOD_CONTROL | MOD_NOREPEAT
            if (!RegisterHotKey(Handle, HotkeyId, 0x0001 | 0x0002 | 0x4000, (uint)Keys.K))
                App.Log(HotkeyText + " kısayolu başka bir program tarafından kullanılıyor; kaydedilemedi.", LogLevel.Warn);
        }

        protected override void WndProc(ref Message m)
        {
            if (m.Msg == WM_HOTKEY && m.WParam.ToInt32() == HotkeyId)
            {
                bool wasActive = App.Engine.IsActive;
                _ = Actions.ToggleAsync();
                ShowBalloon(App.Name, wasActive ? "Koruma kapatılıyor (" + HotkeyText + ")." : "Koruma başlatılıyor (" + HotkeyText + ").", ToolTipIcon.Info, force: true);
                return;
            }
            base.WndProc(ref m);
        }

        void OnUserPreferenceChanged(object sender, UserPreferenceChangedEventArgs e)
        {
            if (App.Settings.ThemeId != "system") return;
            if (e.Category != UserPreferenceCategory.General && e.Category != UserPreferenceCategory.Color && e.Category != UserPreferenceCategory.VisualStyle) return;
            try { BeginInvoke(new Action(() => { themeTimer.Stop(); themeTimer.Start(); })); } catch { }
        }

        // ---------------------------------------------------------------- arayüz iskeleti

        void BuildChrome()
        {
            SuspendLayout();
            BackColor = Bg;

            sidebar = new Panel { Bounds = new Rectangle(0, 0, D(220), ClientSize.Height), BackColor = Sidebar };
            var logo = new LogoPanel { Bounds = new Rectangle(0, 0, D(220), D(84)) };
            logo.MouseDown += DragMouseDown;
            sidebar.Controls.Add(logo);

            navs.Clear();
            int y = D(92);
            foreach (var (key, title, glyph, min) in NavDefs)
            {
                if (App.Settings.UiLevel < min) continue;
                var n = new NavItem { Key = key, Text = title, Glyph = glyph, Bounds = new Rectangle(0, y, D(220), D(40)) };
                n.Click += (s, e) => Navigate(((NavItem)s).Key);
                navs.Add(n);
                sidebar.Controls.Add(n);
                y += D(42);
            }
            sideStatus = new SideStatus { Bounds = new Rectangle(D(12), ClientSize.Height - D(84), D(196), D(70)) };
            sideStatus.Click += (s, e) => Navigate("home");
            sidebar.Controls.Add(sideStatus);

            topbar = new Panel { Bounds = new Rectangle(D(220), 0, ClientSize.Width - D(220), D(40)), BackColor = Bg };
            topbar.MouseDown += DragMouseDown;
            var bClose = new ChromeBtn(GClose, true) { Bounds = new Rectangle(topbar.Width - D(46), 0, D(46), D(34)) };
            var bMin = new ChromeBtn(GMin, false) { Bounds = new Rectangle(topbar.Width - D(92), 0, D(46), D(34)) };
            bClose.Click += (s, e) => Close();
            bMin.Click += (s, e) => HideToTray();
            tips.SetToolTip(bMin, "Sistem tepsisine küçült");
            tips.SetToolTip(bClose, App.Settings.CloseToTray ? "Kapat (tepside çalışmaya devam eder)" : "Kapat");
            topbar.Controls.Add(bClose);
            topbar.Controls.Add(bMin);
            var level = new Label
            {
                Text = "Arayüz: " + LevelName(App.Settings.UiLevel), Font = Small, ForeColor = Muted, BackColor = Bg, AutoSize = false,
                TextAlign = ContentAlignment.MiddleRight, Bounds = new Rectangle(topbar.Width - D(300), 0, D(200), D(34)), Cursor = Cursors.Hand,
            };
            level.Click += (s, e) => Navigate("settings");
            tips.SetToolTip(level, "Basit / Orta / Detaylı arayüz arasında Ayarlar'dan geçiş yapabilirsiniz.");
            topbar.Controls.Add(level);

            host = new Panel { Bounds = new Rectangle(D(220), D(40), ClientSize.Width - D(220), ClientSize.Height - D(40)), BackColor = Bg };

            Controls.Add(host);
            Controls.Add(topbar);
            Controls.Add(sidebar);
            ResumeLayout();
        }

        public static string LevelName(UiLevel l) => l == UiLevel.Basit ? "Basit" : l == UiLevel.Orta ? "Orta" : "Detaylı";

        /// <summary>Tema ya da arayüz düzeyi değişince tüm arayüzü yeniden kurar.</summary>
        public void Rebuild()
        {
            string key = current?.Key ?? "home";
            SuspendLayout();
            current = null;
            foreach (var p in pages.Values) { p.Visible = false; p.Dispose(); }
            pages.Clear();
            var old = Controls.Cast<Control>().ToList();
            Controls.Clear();
            foreach (var c in old) c.Dispose();
            BuildChrome();
            trayMenu.Renderer = new MenuRenderer();
            ResumeLayout();
            Navigate(key);
            RefreshChrome();
        }

        public void ApplyTheme()
        {
            Theme.Apply(Palettes.Resolve(App.Settings));
            Rebuild();
        }

        protected override CreateParams CreateParams
        {
            get
            {
                var cp = base.CreateParams;
                cp.ClassStyle |= 0x20000;   // CS_DROPSHADOW
                return cp;
            }
        }

        protected override void OnHandleCreated(EventArgs e)
        {
            base.OnHandleCreated(e);
            Native.RoundCorners(Handle);
            ApplyHotkey();   // görev çubuğu ayarı değişince pencere tanıtıcısı yeniden oluşur
        }

        protected override void OnKeyDown(KeyEventArgs e)
        {
            if (e.KeyCode == Keys.Escape && !(ActiveControl is ComboBox cb && cb.DroppedDown)) { HideToTray(); e.Handled = true; }
            base.OnKeyDown(e);
        }

        void DragMouseDown(object sender, MouseEventArgs e)
        {
            if (e.Button == MouseButtons.Left) Native.DragWindow(Handle);
        }

        PageBase Create(string key)
        {
            switch (key)
            {
                case "home": return new HomePage();
                case "profiles": return new ProfilesPage();
                case "net": return new NetPage();
                case "dns": return new DnsPage();
                case "sites": return new SitesPage();
                case "proxy": return new ProxyPage();
                case "trouble": return new TroublePage();
                case "log": return new LogPage();
                case "appearance": return new AppearancePage();
                case "settings": return new SettingsPage();
                case "about": return new AboutPage();
            }
            return null;
        }

        /// <summary>Sayfaya geçer. Sayfa seçili arayüz düzeyinde menüde görünmese de açılabilir.</summary>
        public PageBase Navigate(string key)
        {
            if (!pages.TryGetValue(key, out var page))
            {
                page = Create(key);
                if (page == null) return null;
                page.Visible = false;
                pages[key] = page;
                host.Controls.Add(page);
            }
            foreach (var n in navs) n.Selected = n.Key == key;
            if (current != null && current != page) current.Visible = false;
            current = page;
            page.Visible = true;
            page.BringToFront();
            page.OnActivated();
            return page;
        }

        public T Page<T>() where T : PageBase => pages.Values.OfType<T>().FirstOrDefault();

        public T Go<T>(string key) where T : PageBase => Navigate(key) as T;

        // ---------------------------------------------------------------- tepsi

        sealed class MenuColors : ProfessionalColorTable
        {
            public override Color ToolStripDropDownBackground => Theme.Card;
            public override Color ImageMarginGradientBegin => Theme.Card;
            public override Color ImageMarginGradientMiddle => Theme.Card;
            public override Color ImageMarginGradientEnd => Theme.Card;
            public override Color MenuBorder => Theme.Border;
            public override Color MenuItemBorder => Theme.AccentBorder;
            public override Color MenuItemSelected => Theme.AccentSoft;
            public override Color MenuItemSelectedGradientBegin => Theme.AccentSoft;
            public override Color MenuItemSelectedGradientEnd => Theme.AccentSoft;
            public override Color SeparatorDark => Theme.Divider;
            public override Color SeparatorLight => Theme.Divider;
            public override Color CheckBackground => Theme.AccentSoft;
            public override Color CheckSelectedBackground => Theme.AccentSoft;
            public override Color CheckPressedBackground => Theme.AccentSoft;
        }

        sealed class MenuRenderer : ToolStripProfessionalRenderer
        {
            public MenuRenderer() : base(new MenuColors()) { RoundedEdges = false; }
            protected override void OnRenderItemText(ToolStripItemTextRenderEventArgs e)
            {
                e.TextColor = e.Item.Enabled ? Theme.Text : Theme.Muted;
                base.OnRenderItemText(e);
            }
            protected override void OnRenderArrow(ToolStripArrowRenderEventArgs e)
            {
                e.ArrowColor = Theme.Muted;
                base.OnRenderArrow(e);
            }
            protected override void OnRenderItemCheck(ToolStripItemImageRenderEventArgs e)
            {
                var r = e.ImageRectangle;
                TextRenderer.DrawText(e.Graphics, GCheck, IconS, r, Theme.AccentInk, TextFormatFlags.HorizontalCenter | TextFormatFlags.VerticalCenter);
            }
        }

        ContextMenuStrip BuildTrayMenu()
        {
            var m = new ContextMenuStrip { Font = Body, ShowImageMargin = true, Renderer = new MenuRenderer() };
            miStatus = new ToolStripMenuItem("Durum") { Enabled = false, Font = Bold };
            miToggle = new ToolStripMenuItem("Korumayı başlat", null, async (s, e) => await Actions.ToggleAsync());
            miProfiles = new ToolStripMenuItem("Yöntem (profil)");
            miTest = new ToolStripMenuItem("Discord bağlantısını test et", null, async (s, e) =>
            {
                var r = await Actions.QuickTestAsync();
                int ok = r?.Count(x => x.Ok) ?? 0, total = r?.Count ?? 0;
                ShowBalloon("Bağlantı testi", ok + "/" + total + " sunucu erişilebilir." + (App.LastDnsOk == false ? " DNS yanıtı yanlış!" : ""),
                    ok == total ? ToolTipIcon.Info : ToolTipIcon.Warning, force: true);
            });
            miScan = new ToolStripMenuItem("En iyi yöntemi bul…", null, (s, e) => { ShowFromTray(); ScanDialog.Run(this); });
            miDiscord = new ToolStripMenuItem("Discord'u yeniden başlat", null, async (s, e) =>
            {
                string msg = await Actions.RestartDiscordAsync();
                ShowBalloon("Discord", msg, ToolTipIcon.Info, force: true);
            });
            miOpen = new ToolStripMenuItem("Pencereyi aç", null, (s, e) => ShowFromTray()) { Font = Bold };
            miExit = new ToolStripMenuItem("Çıkış", null, (s, e) => ExitApp());
            m.Items.AddRange(new ToolStripItem[]
            {
                miStatus, new ToolStripSeparator(), miToggle, miProfiles, miTest, miScan, miDiscord, new ToolStripSeparator(), miOpen, miExit
            });
            m.Opening += (s, e) =>
            {
                miProfiles.DropDownItems.Clear();
                string lastGroup = null;
                foreach (var p in App.AllProfiles.OrderBy(x => x.Priority))
                {
                    if (lastGroup != null && p.Group != lastGroup) miProfiles.DropDownItems.Add(new ToolStripSeparator());
                    lastGroup = p.Group;
                    var prof = p;
                    var it = new ToolStripMenuItem(p.Name) { Checked = p.Id == App.Settings.SelectedProfileId };
                    it.Click += async (s2, e2) => await Actions.SelectProfileAsync(prof);
                    miProfiles.DropDownItems.Add(it);
                }
                if (miProfiles.DropDown is ToolStripDropDownMenu dd) dd.Renderer = new MenuRenderer();
                miTest.Enabled = !Actions.Testing && !Actions.Scanning;
                miScan.Enabled = !Actions.Scanning;
                miDiscord.Visible = DiscordHelper.IsRunning;
            };
            return m;
        }

        Icon TrayIcon(EngineState s)
        {
            if (s == EngineState.Stopping) s = EngineState.Starting;
            if (!trayIcons.TryGetValue(s, out var ic))
            {
                ic = IconArt.StateIcon(s, SystemInformation.SmallIconSize.Width);
                trayIcons[s] = ic;
            }
            return ic;
        }

        public static string StateText(EngineState s)
        {
            switch (s)
            {
                case EngineState.Running: return "Koruma aktif";
                case EngineState.Starting: return "Başlatılıyor…";
                case EngineState.Stopping: return "Durduruluyor…";
                case EngineState.Error: return "Hata – koruma çalışmıyor";
                default: return "Koruma kapalı";
            }
        }

        void RefreshChrome()
        {
            if (IsDisposed) return;
            var st = App.Engine.State;
            var prof = App.Engine.ActiveProfile ?? App.SelectedProfile;
            string t = App.Name + " – " + StateText(st);
            if (st == EngineState.Running) t += "\n" + prof.Name;
            tray.Text = t.Length > 63 ? t.Substring(0, 63) : t;
            tray.Icon = TrayIcon(st);
            miStatus.Text = StateText(st) + (st == EngineState.Error && App.Engine.LastErrorCode.Length > 0 ? " (" + App.Engine.LastErrorCode + ")" : "");
            bool active = st == EngineState.Running || st == EngineState.Starting;
            miToggle.Text = active ? "Korumayı durdur" : "Korumayı başlat";
            miToggle.Enabled = st != EngineState.Stopping && !Actions.Scanning;
            sideStatus?.Invalidate();
            int issues = App.Problems.Snapshot().Count(p => p.Info != null && p.Info.Severity != Severity.Info);
            foreach (var n in navs.Where(n => n.Key == "trouble")) { if (n.Badge != issues) { n.Badge = issues; n.Invalidate(); } }
        }

        void OnEngineStateChanged(EngineState old, EngineState now)
        {
            RefreshChrome();
            if (App.Engine.Quiet || !App.Settings.Notifications) return;
            if (now == EngineState.Running && old == EngineState.Starting && !Visible)
                ShowBalloon("Koruma aktif", "Yöntem: " + (App.Engine.ActiveProfile?.Name ?? ""), ToolTipIcon.Info);
            else if (now == EngineState.Error && !Visible)
            {
                string err = App.Engine.LastError ?? "";
                if (err.Length > 200) err = err.Substring(0, 197) + "…";
                string code = App.Engine.LastErrorCode;
                ShowBalloon("Koruma durdu" + (code.Length > 0 ? " – " + code : ""), err, ToolTipIcon.Error);
            }
        }

        public void ShowBalloon(string title, string text, ToolTipIcon icon, bool force = false)
        {
            if (App.Preview) return;
            if (!force && !App.Settings.Notifications) return;
            if (string.IsNullOrEmpty(text)) text = " ";
            tray.ShowBalloonTip(4000, title, text, icon);
        }

        // ---------------------------------------------------------------- pencere

        public void ShowFromTray()
        {
            if (!Visible) Show();
            if (WindowState == FormWindowState.Minimized) WindowState = FormWindowState.Normal;
            Activate();
            Native.SetForegroundWindow(Handle);
            current?.OnActivated();
        }

        public void HideToTray()
        {
            Hide();
            if (!App.Settings.TrayHintShown)
            {
                App.Settings.TrayHintShown = true;
                App.Save();
                ShowBalloon(App.Name + " arka planda çalışıyor",
                    "Pencereyi açmak için saat yanındaki kalkan simgesine tıklayın. Sağ tık menüsünden hızlıca başlatıp durdurabilirsiniz.",
                    ToolTipIcon.Info, force: true);
            }
        }

        public void ApplyTaskbarSetting()
        {
            ShowInTaskbar = !App.Settings.HideFromTaskbar;
        }

        protected override void OnFormClosing(FormClosingEventArgs e)
        {
            if (!exiting && e.CloseReason == CloseReason.UserClosing)
            {
                e.Cancel = true;
                if (App.Settings.CloseToTray) HideToTray();
                else ExitApp();
                return;
            }
            if (!exiting)
            {
                // Windows kapanıyor / oturum kapanıyor
                exiting = true;
                tray.Visible = false;
                if (!App.Engine.RunningAsService) App.Engine.Stop(true);
            }
            base.OnFormClosing(e);
        }

        public void ExitApp(bool ask = true)
        {
            if (ask && App.Engine.IsActive && !App.Engine.RunningAsService)
            {
                var r = MessageBox.Show(Visible ? this : null,
                    "Uygulamadan çıkarsanız koruma durur ve engelli sitelere erişim kesilir.\n\n" +
                    "Arka planda çalışmaya devam etmesi için pencereyi kapatmanız (tepsiye küçültmeniz) yeterli.\n\nYine de çıkılsın mı?",
                    App.Name, MessageBoxButtons.YesNo, MessageBoxIcon.Question, MessageBoxDefaultButton.Button2);
                if (r != DialogResult.Yes) return;
            }
            exiting = true;
            pollTimer.Stop();
            tray.Visible = false;
            if (!App.Engine.RunningAsService) App.Engine.Stop();
            ProxyBridge.StopAll();
            App.Log("Uygulama kapatıldı.", LogLevel.Info);
            Application.Exit();
        }

        public async void AfterStartup(bool fromTray, bool scan, bool scanAll = false)
        {
            App.Log(App.Name + " " + App.Version + " başlatıldı (" + LevelName(App.Settings.UiLevel) + " arayüz, tema: " + Theme.Current?.Name + ").", LogLevel.Info);
            if (App.Preview) return;
            string missing = Engine.MissingFile();
            if (missing != null) App.Log("[DK-102] " + missing + " bulunamadı: " + Engine.BinDir, LogLevel.Error);
            if (App.RunningFromTemp)
            {
                App.Problems.Report("DK-103", App.BaseDir);
                App.Log("[DK-103] Uygulama geçici bir klasörden çalışıyor: " + App.BaseDir, LogLevel.Warn);
            }

            if (App.Settings.StartWithWindows)
                _ = Task.Run(() =>
                {
                    if (!Startup.Enable(out string err))
                    {
                        App.Log("[DK-703] Açılış görevi güncellenemedi: " + err, LogLevel.Warn);
                        App.Problems.Report("DK-703", err);
                    }
                });

            try { ProxyBridge.Sync(); } catch (Exception ex) { App.Log("Yerel proxy köprüsü başlatılamadı: " + ex.Message, LogLevel.Warn); }
            await Task.Run(() => App.Engine.Poll());
            bool repointed = false;
            if (App.Engine.ServiceInstalled && App.Settings.ServiceMode && await Task.Run(() => Engine.ServicePointsElsewhere()))
            {
                App.Log("Hizmet eski bir klasörü gösteriyor; bu klasöre güncelleniyor.", LogLevel.Warn);
                await Actions.StartAsync();
                repointed = true;
            }
            if (scan)
            {
                // komut satırından istenen tarama (ör. DPIKalkan.exe --scan)
                ShowFromTray();
                ScanDialog.Run(this, autoStart: true, full: scanAll);
            }
            else if (repointed) { }
            else if (App.Settings.AutoStartProtection && App.Engine.State != EngineState.Running)
            {
                if (App.Engine.ExternalCount > 0)
                    App.Log("Uygulama dışında çalışan GoodbyeDPI bulundu; devralınıyor.", LogLevel.Warn);
                await Actions.StartAsync();
            }
            else if (App.Engine.State == EngineState.Running && App.Settings.TestAfterStart)
                await Actions.QuickTestAsync();
        }

        protected override void Dispose(bool disposing)
        {
            if (disposing)
            {
                SystemEvents.UserPreferenceChanged -= OnUserPreferenceChanged;
                System.Net.NetworkInformation.NetworkChange.NetworkAddressChanged -= OnNetworkChanged;
                watchTimer.Dispose();
                netChangeTimer.Dispose();
                tray.Visible = false;
                tray.Dispose();
                pollTimer.Dispose();
                themeTimer.Dispose();
            }
            base.Dispose(disposing);
        }

        // ---------------------------------------------------------------- iç bileşenler

        sealed class LogoPanel : Control
        {
            readonly Bitmap logo = IconArt.AppBitmap(D(36));
            public LogoPanel()
            {
                SetStyle(ControlStyles.UserPaint | ControlStyles.AllPaintingInWmPaint | ControlStyles.OptimizedDoubleBuffer, true);
            }
            protected override void Dispose(bool disposing) { if (disposing) logo.Dispose(); base.Dispose(disposing); }
            protected override void OnPaint(PaintEventArgs e)
            {
                var g = e.Graphics;
                g.Clear(Sidebar);
                g.DrawImage(logo, D(20), D(24));
                using (var f = new Font("Segoe UI Semibold", 13f))
                    TextRenderer.DrawText(g, App.Name, f, new Point(D(64), D(21)), SidebarText, Sidebar);
                TextRenderer.DrawText(g, "İnternet erişim kalkanı", Small, new Point(D(65), D(45)), SidebarMuted, Sidebar);
            }
        }

        sealed class SideStatus : Control
        {
            public SideStatus()
            {
                SetStyle(ControlStyles.UserPaint | ControlStyles.AllPaintingInWmPaint | ControlStyles.OptimizedDoubleBuffer | ControlStyles.ResizeRedraw, true);
                Cursor = Cursors.Hand;
            }
            protected override void OnPaint(PaintEventArgs e)
            {
                var g = e.Graphics;
                g.Clear(Sidebar);
                HQ(g);
                var bg = SidebarBox;
                using (var p = Round(new RectangleF(0, 0, Width - 1, Height - 1), D(10)))
                using (var b = new SolidBrush(bg)) g.FillPath(b, p);
                var st = App.Engine.State;
                Color c = st == EngineState.Running ? Color.FromArgb(52, 211, 153)
                        : st == EngineState.Error ? Color.FromArgb(248, 113, 113)
                        : st == EngineState.Stopped ? Mix(SidebarText, Sidebar, 0.5f) : Color.FromArgb(251, 191, 36);
                float d = D(10);
                using (var halo = new SolidBrush(Color.FromArgb(60, c))) g.FillEllipse(halo, D(14) - D(3), D(17) - D(3), d + D(6), d + D(6));
                using (var b = new SolidBrush(c)) g.FillEllipse(b, D(14), D(17), d, d);
                TextRenderer.DrawText(g, StateText(st), Bold, new Point(D(32), D(12)), SidebarText, bg);
                var prof = App.Engine.ActiveProfile ?? App.SelectedProfile;
                string sub = st == EngineState.Running
                    ? prof.Name + (App.Engine.RunningAsService ? " · hizmet" : "")
                    : st == EngineState.Error && App.Engine.LastErrorCode.Length > 0 ? "Hata kodu " + App.Engine.LastErrorCode
                    : "Yöntem: " + prof.Name;
                TextRenderer.DrawText(g, sub, Small, new Rectangle(D(14), D(38), Width - D(20), D(20)), SidebarMuted, bg,
                    TextFormatFlags.EndEllipsis | TextFormatFlags.NoPadding);
            }
        }

        sealed class ChromeBtn : Control
        {
            readonly string glyph;
            readonly bool isClose;
            bool hover;
            public ChromeBtn(string glyph, bool isClose)
            {
                this.glyph = glyph;
                this.isClose = isClose;
                SetStyle(ControlStyles.UserPaint | ControlStyles.AllPaintingInWmPaint | ControlStyles.OptimizedDoubleBuffer, true);
            }
            protected override void OnMouseEnter(EventArgs e) { hover = true; Invalidate(); base.OnMouseEnter(e); }
            protected override void OnMouseLeave(EventArgs e) { hover = false; Invalidate(); base.OnMouseLeave(e); }
            protected override void OnPaint(PaintEventArgs e)
            {
                Color bg = hover ? (isClose ? Color.FromArgb(232, 17, 35) : Mix(Bg, Theme.Text, 0.08f)) : Bg;
                Color fg = hover && isClose ? Color.White : Muted;
                e.Graphics.Clear(bg);
                using (var f = new Font(IconFontName, 8f))
                    TextRenderer.DrawText(e.Graphics, glyph, f, ClientRectangle, fg, bg,
                        TextFormatFlags.HorizontalCenter | TextFormatFlags.VerticalCenter | TextFormatFlags.NoPadding);
            }
        }
    }
}
