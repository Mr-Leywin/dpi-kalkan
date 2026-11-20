using System;
using System.Drawing;
using System.Linq;
using System.Threading.Tasks;
using System.Windows.Forms;
using static DPIKalkan.Theme;

namespace DPIKalkan
{
    sealed class HomePage : PageBase
    {
        public override string Key => "home";

        readonly bool simple;
        readonly Card hero, banner, cProfile, cConn, cSession, tip;
        readonly ShieldView shield;
        readonly Label lblState, lblSub, lblHint, bannerIcon, bannerTitle, bannerText;
        readonly FlatBtn btnToggle, btnTest, bannerFix, bannerMore, bDiscord, bAuto, bChange;
        readonly Label pName, pDesc, pCmd, connIcon, connTitle, connDetail, connDns, connTime, upVal;
        Label modeVal, dnsVal, targetVal, scanVal;
        readonly ThemedCombo pCombo;
        readonly Timer tick = new Timer { Interval = 1000 };
        DetectedProblem shownProblem;
        bool showingUpdate;
        static bool updateDismissed;
        string hintOverride;
        bool filling;

        public HomePage()
        {
            simple = App.Settings.UiLevel == UiLevel.Basit;

            // ------------------------------------------------ durum kartı
            hero = AddCard(28, 6, 764, 252);
            shield = hero.Add(new ShieldView { Bounds = R(22, 22, 208, 208) });
            lblState = hero.Add(UIx.L("Koruma Kapalı", Hero, Theme.Text, 250, 34, 490, 46));
            lblSub = hero.Add(UIx.L("", Body, Muted, 252, 84, 490, 44));
            btnToggle = hero.Add(new FlatBtn { Text = "Korumayı Başlat", Kind = FlatBtn.Kinds.Success, Glyph = GPlay, Bounds = R(252, 142, 232, 52), Font = Big });
            btnTest = hero.Add(new FlatBtn { Text = "Discord'u Test Et", Kind = FlatBtn.Kinds.Secondary, Glyph = GSpeed, Bounds = R(498, 142, 206, 52), Font = Big });
            lblHint = hero.Add(UIx.L("", Small, Muted, 252, 204, 490, 40));
            btnToggle.Click += async (s, e) => { hintOverride = null; await Actions.ToggleAsync(); };
            btnTest.Click += async (s, e) => { hintOverride = null; await Actions.QuickTestAsync(); };

            // ------------------------------------------------ sorun bandı
            banner = AddCard(28, 272, 764, 56);
            bannerIcon = banner.Add(UIx.Glyph(GWarn, IconM, Amber, 12, 0, 34, 56));
            bannerTitle = banner.Add(UIx.L("", Bold, Theme.Text, 48, 8, 440, 20));
            bannerText = banner.Add(UIx.L("", Small, Muted, 48, 28, 440, 22));
            bannerFix = banner.Add(new FlatBtn { Text = "Düzelt", Kind = FlatBtn.Kinds.Warning, Bounds = R(498, 11, 150, 34) });
            bannerMore = banner.Add(new FlatBtn { Text = "Ayrıntı", Glyph = GHelp, Kind = FlatBtn.Kinds.Ghost, Bounds = R(654, 11, 100, 34) });
            bannerFix.Click += async (s, e) =>
            {
                if (showingUpdate) { if (Updater.Available != null) UpdateDialog.ShowFor(FindForm(), Updater.Available); return; }
                var p = shownProblem;
                if (p?.Info == null) return;
                bannerFix.Enabled = false;
                string msg = await Fixes.Run(p.Info.Fix, FindForm());
                if (IsDisposed) return;
                bannerFix.Enabled = true;
                if (!string.IsNullOrEmpty(msg)) hintOverride = msg;
                if (p.Code == "DK-901" || p.Code == "DK-103") App.Problems.Clear(p.Code);
                UpdateAll();
            };
            bannerMore.Click += (s, e) =>
            {
                if (showingUpdate) { updateDismissed = true; UpdateAll(); return; }
                var p = shownProblem;
                MainForm.Instance.Go<TroublePage>("trouble")?.ShowCode(p?.Code);
            };
            banner.Visible = false;

            if (simple)
            {
                // ------------------------------------------------ BASİT: iki büyük kart
                cConn = AddCard(28, 272, 374, 210);
                cConn.Add(UIx.L("DISCORD ERİŞİMİ", Caption, Muted, 20, 18, 300, 18));
                connIcon = cConn.Add(UIx.Glyph(GInfo, IconL, Gray, 16, 42, 40, 40));
                connTitle = cConn.Add(UIx.L("Test edilmedi", H2, Theme.Text, 60, 44, 300, 36, ContentAlignment.MiddleLeft));
                connDetail = cConn.Add(UIx.L("", Small, Muted, 20, 90, 340, 40));
                connDns = cConn.Add(UIx.L("", Small, Muted, 20, 130, 200, 20));
                connTime = cConn.Add(UIx.L("", Small, Muted, 220, 130, 140, 20, ContentAlignment.TopRight));
                bDiscord = cConn.Add(new FlatBtn { Text = "Discord'u yeniden başlat", Glyph = GRefresh, Kind = FlatBtn.Kinds.Secondary, Bounds = R(20, 158, 230, 36) });

                cProfile = AddCard(418, 272, 374, 210);
                cProfile.Add(UIx.L("KULLANILAN YÖNTEM", Caption, Muted, 20, 18, 300, 18));
                pName = cProfile.Add(UIx.L("", H2, Theme.Text, 19, 38, 340, 28));
                pDesc = cProfile.Add(UIx.L("", Small, Muted, 20, 68, 336, 72));
                bAuto = cProfile.Add(new FlatBtn { Text = "Otomatik Ayarla", Glyph = GSearch, Kind = FlatBtn.Kinds.Primary, Bounds = R(20, 152, 178, 40) });
                bChange = cProfile.Add(new FlatBtn { Text = "Değiştir", Glyph = GChevron, Kind = FlatBtn.Kinds.Secondary, Bounds = R(208, 152, 146, 40) });
                bAuto.Click += (s, e) => ScanDialog.Run(FindForm());
                bChange.Click += (s, e) => ShowProfileMenu(bChange);

                tip = AddCard(28, 496, 764, 74);
                tip.Fill = AccentSoft;
                tip.Stroke = AccentBorder;
                tip.Add(UIx.Glyph(GHelp, IconL, AccentInk, 16, 0, 48, 74));
                tip.Add(UIx.L("Discord yine açılmıyor mu?", Bold, Theme.Text, 70, 15, 460, 22));
                tip.Add(UIx.L("'Sorunu bul' her şeyi kontrol eder; hatayı kodu ve çözümüyle birlikte gösterir.", Small, Muted, 70, 37, 460, 34));
                var bTrouble = tip.Add(new FlatBtn { Text = "Sorunu bul", Glyph = GSearch, Kind = FlatBtn.Kinds.Primary, Bounds = R(566, 18, 180, 38) });
                bTrouble.Click += async (s, e) => { var tp = MainForm.Instance.Go<TroublePage>("trouble"); if (tp != null) await tp.RunCheck(); };
            }
            else
            {
                // ------------------------------------------------ ORTA / DETAYLI: üç kart
                cProfile = AddCard(28, 272, 244, 220);
                cProfile.Add(UIx.L("AKTİF YÖNTEM", Caption, Muted, 20, 18, 200, 18));
                pName = cProfile.Add(UIx.L("", H2, Theme.Text, 19, 38, 212, 28));
                bool det = Level(UiLevel.Detayli);
                pDesc = cProfile.Add(UIx.L("", Small, Muted, 20, 68, 210, det ? 58 : 92));
                if (det) pCmd = cProfile.Add(UIx.L("", MonoSmall, Muted, 20, 128, 210, 36));
                pCombo = new ThemedCombo { Bounds = R(20, 172, 204, 28) };
                cProfile.Add(pCombo);
                pCombo.SelectedIndexChanged += async (s, e) =>
                {
                    if (filling || !(pCombo.SelectedItem is Profile it)) return;
                    await Actions.SelectProfileAsync(it);
                };

                cConn = AddCard(288, 272, 244, 220);
                cConn.Add(UIx.L("DISCORD ERİŞİMİ", Caption, Muted, 20, 18, 200, 18));
                connIcon = cConn.Add(UIx.Glyph(GInfo, IconL, Gray, 16, 42, 40, 40));
                connTitle = cConn.Add(UIx.L("Test edilmedi", H2, Theme.Text, 60, 44, 176, 36, ContentAlignment.MiddleLeft));
                connDetail = cConn.Add(UIx.L("", Small, Muted, 20, 88, 210, 48));
                connDns = cConn.Add(UIx.L("", Small, Muted, 20, 136, 210, 18));
                connTime = cConn.Add(UIx.L("", Small, Muted, 20, 154, 210, 18));
                var lnk = cConn.Add(new FlatBtn { Text = "İnternet kontrolü", Glyph = GArrow, Kind = FlatBtn.Kinds.Ghost, Bounds = R(12, 178, 170, 32) });
                lnk.Click += (s, e) => MainForm.Instance.Navigate("net");

                cSession = AddCard(548, 272, 244, 220);
                cSession.Add(UIx.L("OTURUM", Caption, Muted, 20, 18, 200, 18));
                upVal = cSession.Add(UIx.L("—", new Font("Segoe UI Semibold", 20f), Theme.Text, 18, 36, 210, 40));
                cSession.Add(UIx.L("çalışma süresi", Small, Muted, 20, 76, 210, 18));
                int y = 104;
                var rows = new (string, Action<Label>)[] { ("Mod", l => modeVal = l), ("DNS", l => dnsVal = l), ("Hedefli mod", l => targetVal = l), ("Son tarama", l => scanVal = l) };
                foreach (var (k, setter) in rows)
                {
                    cSession.Add(UIx.L(k, Small, Muted, 20, y, 84, 22, ContentAlignment.MiddleLeft));
                    var v = cSession.Add(UIx.L("", Bold, Theme.Text, 100, y, 134, 22, ContentAlignment.MiddleLeft));
                    setter(v);
                    y += 27;
                }

                tip = AddCard(28, 506, 764, 74);
                tip.Fill = AccentSoft;
                tip.Stroke = AccentBorder;
                tip.Add(UIx.Glyph(GShield, IconL, AccentInk, 16, 0, 48, 74));
                tip.Add(UIx.L("Discord yine açılmıyor mu?", Bold, Theme.Text, 70, 15, 300, 22));
                tip.Add(UIx.L("Tarama tüm yöntemleri dener, DNS'i doğrular ve çalışanı otomatik seçer.", Small, Muted, 70, 37, 300, 34));
                bDiscord = tip.Add(new FlatBtn { Text = "Discord'u yeniden başlat", Glyph = GRefresh, Kind = FlatBtn.Kinds.Secondary, Bounds = R(378, 18, 190, 38) });
                bAuto = tip.Add(new FlatBtn { Text = "En iyi yöntemi bul", Glyph = GSearch, Kind = FlatBtn.Kinds.Primary, Bounds = R(576, 18, 172, 38) });
                bAuto.Click += (s, e) => ScanDialog.Run(FindForm());
            }
            bDiscord.Click += async (s, e) =>
            {
                bDiscord.Enabled = false;
                hintOverride = await Actions.RestartDiscordAsync();
                if (IsDisposed) return;
                bDiscord.Enabled = true;
                UpdateAll();
            };

            tick.Tick += (s, e) => UpdateUptime();
            tick.Start();
            Track(tick);
            OnStatus(UpdateAll);
            UpdateAll();
        }

        public override void OnActivated() => UpdateAll();

        void ShowProfileMenu(Control anchor)
        {
            var m = new ContextMenuStrip { Font = Body, ShowImageMargin = true, ShowItemToolTips = true };
            string lastGroup = null;
            foreach (var p in App.AllProfiles.OrderBy(x => x.Priority))
            {
                if (p.Group == Profiles.GroupNoDns) continue;
                if (lastGroup != null && p.Group != lastGroup) m.Items.Add(new ToolStripSeparator());
                lastGroup = p.Group;
                var prof = p;
                var it = new ToolStripMenuItem(p.Name) { Checked = p.Id == App.Settings.SelectedProfileId, ToolTipText = p.Description };
                it.Click += async (s, e) => await Actions.SelectProfileAsync(prof);
                m.Items.Add(it);
            }
            m.Closed += (s, e) => BeginInvoke(new Action(m.Dispose));
            m.Show(anchor, new Point(0, anchor.Height));
        }

        void FillCombo()
        {
            if (pCombo == null) return;
            filling = true;
            pCombo.BeginUpdate();
            pCombo.Items.Clear();
            foreach (var p in App.AllProfiles.OrderBy(x => x.Priority)) pCombo.Items.Add(p);
            pCombo.SelectedIndex = pCombo.Items.Cast<Profile>().ToList().FindIndex(i => i.Id == App.Settings.SelectedProfileId);
            pCombo.EndUpdate();
            filling = false;
        }

        void Arrange(bool showBanner)
        {
            if (banner.Visible == showBanner) return;
            banner.Visible = showBanner;
            int dy = showBanner ? 70 : 0;
            if (simple)
            {
                cConn.Top = cProfile.Top = D(272 + dy);
                tip.Top = D(496 + dy);
            }
            else
            {
                cProfile.Top = cConn.Top = cSession.Top = D(272 + dy);
                tip.Top = D(506 + dy);
            }
            tip.Visible = !showBanner;
        }

        void UpdateAll()
        {
            var e = App.Engine;
            var st = e.State;
            var prof = (st == EngineState.Running ? e.ActiveProfile : null) ?? App.SelectedProfile;
            shield.State = st;

            switch (st)
            {
                case EngineState.Running:
                    lblState.Text = "Koruma Aktif";
                    lblState.ForeColor = GreenText;
                    lblSub.Text = "Erişim engelleri aşılıyor. Discord ve engelli siteler şu an kullanılabilir olmalı.";
                    break;
                case EngineState.Starting:
                    lblState.Text = "Başlatılıyor…";
                    lblState.ForeColor = AmberText;
                    lblSub.Text = "GoodbyeDPI ve WinDivert sürücüsü yükleniyor.";
                    break;
                case EngineState.Stopping:
                    lblState.Text = "Durduruluyor…";
                    lblState.ForeColor = AmberText;
                    lblSub.Text = "";
                    break;
                case EngineState.Error:
                    lblState.Text = "Koruma Çalışmıyor";
                    lblState.ForeColor = RedText;
                    lblSub.Text = (e.LastErrorCode.Length > 0 ? "Hata kodu " + e.LastErrorCode + ": " : "") + Trim(e.LastError, 210);
                    break;
                default:
                    lblState.Text = "Koruma Kapalı";
                    lblState.ForeColor = Theme.Text;
                    lblSub.Text = "Engellenen sitelere ve Discord'a erişmek için korumayı başlatın.";
                    break;
            }
            lblSub.Font = st == EngineState.Error ? Small : Body;
            lblSub.Height = D(st == EngineState.Error ? 52 : 44);
            bool active = st == EngineState.Running || st == EngineState.Starting;
            btnToggle.Text = active ? "Korumayı Durdur" : st == EngineState.Error ? "Tekrar Dene" : "Korumayı Başlat";
            btnToggle.Glyph = active ? GStop : st == EngineState.Error ? GRefresh : GPlay;
            btnToggle.Kind = active ? FlatBtn.Kinds.Danger : FlatBtn.Kinds.Success;
            btnToggle.Enabled = st != EngineState.Stopping && !Actions.Scanning;
            btnTest.Enabled = !Actions.Testing && !Actions.Scanning;
            btnTest.Text = Actions.Testing ? "Test ediliyor…" : "Discord'u Test Et";
            if (bAuto != null) bAuto.Enabled = !Actions.Scanning;

            lblHint.Text = hintOverride ?? (st == EngineState.Error
                    ? "Aşağıdaki 'Ayrıntı' düğmesi hatanın nedenini ve çözümünü gösterir."
                    : Actions.Scanning ? "Otomatik yöntem taraması sürüyor…"
                    : "İpucu: Pencereyi kapatınca uygulama sistem tepsisinde çalışmaya devam eder.");

            // ------------------------------------------------ sorun bandı
            var problem = App.Problems.Snapshot().FirstOrDefault(p => p.Info != null && (p.Info.Severity != Severity.Info || p.Code == "DK-901"));
            if (Actions.Scanning) problem = null;
            shownProblem = problem;
            if (problem != null)
            {
                var info = problem.Info;
                banner.Fill = info.Severity == Severity.Error ? RedSoft : info.Severity == Severity.Warning ? AmberSoft : AccentSoft;
                banner.Stroke = info.Severity == Severity.Error ? RedBorder : info.Severity == Severity.Warning ? AmberBorder : AccentBorder;
                bannerIcon.Text = info.Severity == Severity.Info ? GInfo : GWarn;
                bannerIcon.ForeColor = info.Severity == Severity.Error ? RedText : info.Severity == Severity.Warning ? AmberText : AccentInk;
                bannerTitle.Text = problem.Code + " · " + info.Title;
                bannerText.Text = Trim(info.Summary, 120);
                bannerFix.Visible = info.Fix != FixKind.None;
                bannerFix.Text = info.Code == "DK-301" || info.Code == "DK-302" ? "Devral ve Düzelt" : ErrorInfo.FixLabel(info.Fix);
                bannerFix.Kind = info.Severity == Severity.Error ? FlatBtn.Kinds.Danger : info.Severity == Severity.Warning ? FlatBtn.Kinds.Warning : FlatBtn.Kinds.Primary;
                int fw = Math.Min(220, Math.Max(130, (int)(TextRenderer.MeasureText(bannerFix.Text, Bold).Width / Theme.Scale) + 40));
                bannerFix.Bounds = R(648 - fw, 11, fw, 34);
                bannerTitle.Width = bannerText.Width = D(648 - fw - 58);
            }
            // sorun yoksa ve yeni sürüm varsa güncelleme bandı
            var upd = Updater.Available;
            showingUpdate = problem == null && upd != null && !updateDismissed && !Updater.Skipped(upd) && !Actions.Scanning;
            if (showingUpdate)
            {
                banner.Fill = AccentSoft;
                banner.Stroke = AccentBorder;
                bannerIcon.Text = GRefresh;
                bannerIcon.ForeColor = AccentInk;
                bannerTitle.Text = "Yeni sürüm hazır: DPI Kalkan " + upd.Version;
                bannerText.Text = "Yenilikleri görmek ve tek tıkla kurmak için 'Güncelle'ye basın. Ayarlarınız korunur.";
                bannerFix.Visible = true;
                bannerFix.Text = "Güncelle";
                bannerFix.Kind = FlatBtn.Kinds.Primary;
                bannerFix.Bounds = R(518, 11, 130, 34);
                bannerMore.Text = "Sonra";
                bannerMore.Glyph = null;
                bannerTitle.Width = bannerText.Width = D(460);
            }
            else
            {
                bannerMore.Text = "Ayrıntı";
                bannerMore.Glyph = GHelp;
            }
            Arrange(problem != null || showingUpdate);

            // ------------------------------------------------ yöntem kartı
            if (pCombo != null && !pCombo.DroppedDown) FillCombo();
            pName.Text = prof.Name;
            pDesc.Text = prof.Description;
            if (pCmd != null) pCmd.Text = "goodbyedpi " + Trim(prof.BuildArgs(App.Settings.TargetedMode ? "…" : null), 90);
            if (pCombo != null) pCombo.Enabled = st != EngineState.Starting && st != EngineState.Stopping && !Actions.Scanning;
            if (bChange != null) bChange.Enabled = !Actions.Scanning;

            // ------------------------------------------------ bağlantı kartı
            var t = App.LastTest;
            if (Actions.Testing)
            {
                connIcon.Text = GRefresh; connIcon.ForeColor = AccentInk;
                connTitle.Text = "Test ediliyor…"; connTitle.ForeColor = Theme.Text;
            }
            else if (t == null || t.Count == 0)
            {
                connIcon.Text = GInfo; connIcon.ForeColor = Gray;
                connTitle.Text = "Test edilmedi"; connTitle.ForeColor = Theme.Text;
                connDetail.Text = "Discord sunucularına erişilip erişilemediğini görmek için test edin.";
            }
            else
            {
                int ok = t.Count(r => r.Ok);
                int ms = ok > 0 ? (int)t.Where(r => r.Ok).Average(r => r.Ms) : 0;
                bool dnsBad = App.LastDnsOk == false;
                if (ok == t.Count && !dnsBad) { connIcon.Text = GCheck; connIcon.ForeColor = GreenText; connTitle.Text = "Erişilebilir"; connTitle.ForeColor = GreenText; }
                else if (ok == 0) { connIcon.Text = GCancel; connIcon.ForeColor = RedText; connTitle.Text = "Erişim yok"; connTitle.ForeColor = RedText; }
                else { connIcon.Text = GWarn; connIcon.ForeColor = AmberText; connTitle.Text = dnsBad && ok == t.Count ? "DNS sorunu" : "Kısmi erişim"; connTitle.ForeColor = AmberText; }
                connDetail.Text = ok + "/" + t.Count + " sunucu yanıt verdi" + (ok > 0 ? " · ort. " + ms + " ms" : "") +
                                  (ok < t.Count ? "\nAçılmayan: " + string.Join(", ", t.Where(r => !r.Ok).Select(r => r.Host)) : "");
                connTime.Text = "Son test: " + App.LastTestTime?.ToString("HH:mm:ss");
            }
            connDns.Text = App.LastDnsOk == true ? "DNS yanıtı: doğru ✓" : App.LastDnsOk == false ? "DNS yanıtı: YANLIŞ" : "";
            connDns.ForeColor = App.LastDnsOk == false ? RedText : App.LastDnsOk == true ? GreenText : Muted;
            bool discord = DiscordHelper.IsRunning;
            bDiscord.Visible = discord || simple;
            bDiscord.Text = discord ? "Discord'u yeniden başlat" : "Discord'u aç";

            // ------------------------------------------------ oturum kartı
            if (cSession != null)
            {
                modeVal.Text = e.RunningAsService || (App.Settings.ServiceMode && st != EngineState.Running) ? "Arka plan hizmeti" : "Uygulama";
                dnsVal.Text = prof.DnsLabel;
                dnsVal.ForeColor = prof.UsesDnsRedirect ? Theme.Text : AmberText;
                targetVal.Text = App.Settings.TargetedMode ? "Açık (" + App.Settings.TargetDomains.Count + " site)" : "Kapalı (tümü)";
                scanVal.Text = App.Settings.LastScanTime.HasValue ? App.Settings.LastScanTime.Value.ToString("dd.MM HH:mm") : "Hiç";
            }
            UpdateUptime();
        }

        void UpdateUptime()
        {
            if (!Visible || upVal == null) return;
            var s = App.Engine.StartedAt;
            if (App.Engine.State == EngineState.Running && s.HasValue)
            {
                var d = DateTime.Now - s.Value;
                upVal.Text = ((int)d.TotalHours).ToString("00") + ":" + d.Minutes.ToString("00") + ":" + d.Seconds.ToString("00");
                upVal.ForeColor = Theme.Text;
            }
            else
            {
                upVal.Text = "—";
                upVal.ForeColor = Muted;
            }
        }

        static string Trim(string s, int n)
        {
            s = s ?? "";
            return s.Length <= n ? s : s.Substring(0, n - 1) + "…";
        }
    }
}
