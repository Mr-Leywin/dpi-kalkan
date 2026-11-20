using System;
using System.Diagnostics;
using System.Drawing;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using System.Windows.Forms;
using static DPIKalkan.Theme;

namespace DPIKalkan
{
    /// <summary>"En iyi yöntemi bul" taramasını gösteren pencere.</summary>
    sealed class ScanDialog : Form
    {
        readonly ReportBox report;
        readonly ProgressLine bar;
        readonly Label lblStep, lblIntro;
        readonly CheckX chkAll;
        readonly FlatBtn bStart, bClose, bDiscord, bHelp;
        CancellationTokenSource cts;
        Scanner scanner;
        bool running, forceFull;

        public static void Run(IWin32Window owner, bool autoStart = false, bool full = false)
        {
            if (Actions.Scanning) return;
            using (var d = new ScanDialog())
            {
                d.forceFull = full;
                if (autoStart) d.Shown += async (s, e) => await d.StartScan();
                d.ShowDialog(owner);
            }
        }

        ScanDialog()
        {
            Text = "En İyi Yöntemi Bul – " + App.Name;
            FormBorderStyle = FormBorderStyle.FixedDialog;
            StartPosition = FormStartPosition.CenterParent;
            MaximizeBox = MinimizeBox = false;
            ShowInTaskbar = false;
            AutoScaleMode = AutoScaleMode.None;
            BackColor = Bg;
            Font = Body;
            ClientSize = new Size(D(720), D(600));
            Icon = IconArt.AppIcon(32);

            Controls.Add(UIx.L("En İyi Yöntemi Bul", H2, Theme.Text, 24, 16, 600, 30));
            lblIntro = UIx.L("Önce engelin türü (DNS mi, DPI mı) belirlenir; ardından yöntemler tek tek başlatılıp Discord sunucularına " +
                             "iki tur boyunca yeni bağlantılarla erişilip erişilemediği ve DNS yanıtının doğruluğu kontrol edilir. " +
                             "Yalnızca tüm testleri geçen yöntem seçilir. Tarama sırasında bağlantınız birkaç kez kısa süreliğine kesilebilir.",
                             Small, Muted, 25, 48, 670, 50);
            Controls.Add(lblIntro);

            chkAll = new CheckX { Text = "Tüm yöntemleri dene (2-3 dakika sürer; çalışanlar arasından en hızlısı seçilir)", Bounds = R(24, 100, 660, 26), BackColor = Bg, Checked = App.Settings.ScanAll };
            chkAll.CheckedChanged += (s, e) => { App.Settings.ScanAll = chkAll.Checked; App.Save(); };
            Controls.Add(chkAll);

            lblStep = UIx.L("Hazır.", Bold, Theme.Text, 24, 134, 670, 22);
            Controls.Add(lblStep);
            bar = new ProgressLine { Bounds = R(24, 160, 672, 6), BackColor = Bg };
            Controls.Add(bar);

            var card = new Card { Bounds = R(24, 178, 672, 352) };
            Controls.Add(card);
            report = new ReportBox { Bounds = R(14, 10, 648, 332) };
            card.Add(report);
            report.BackColor = Theme.Card;
            report.Muted("Başlat'a basın. Ortalama süre: hızlı taramada 20-40 saniye.");

            bStart = new FlatBtn { Text = "Taramayı Başlat", Glyph = GSearch, Kind = FlatBtn.Kinds.Primary, Bounds = R(24, 544, 180, 40) };
            bDiscord = new FlatBtn { Text = "Discord'u yeniden başlat", Glyph = GRefresh, Kind = FlatBtn.Kinds.Secondary, Bounds = R(214, 544, 210, 40), Visible = false };
            bHelp = new FlatBtn { Text = "Hata kodunu incele", Glyph = GHelp, Kind = FlatBtn.Kinds.Secondary, Bounds = R(214, 544, 190, 40), Visible = false };
            bClose = new FlatBtn { Text = "Kapat", Kind = FlatBtn.Kinds.Secondary, Bounds = R(576, 544, 120, 40) };
            Controls.AddRange(new Control[] { bStart, bDiscord, bHelp, bClose });
            bStart.Click += async (s, e) => await StartScan();
            bClose.Click += (s, e) => Close();
            bDiscord.Click += async (s, e) =>
            {
                bDiscord.Enabled = false;
                string msg = await Actions.RestartDiscordAsync();
                report.Out(msg, GreenText, Bold);
                bDiscord.Enabled = true;
            };
            bHelp.Click += (s, e) =>
            {
                string code = scanner?.FailCode;
                Close();
                MainForm.Instance.ShowFromTray();
                MainForm.Instance.Go<TroublePage>("trouble")?.ShowCode(code);
            };
        }

        protected override void OnHandleCreated(EventArgs e)
        {
            base.OnHandleCreated(e);
            Native.DarkTitle(Handle, IsDark);
        }

        protected override void OnFormClosing(FormClosingEventArgs e)
        {
            if (running)
            {
                e.Cancel = true;
                cts?.Cancel();
                lblStep.Text = "İptal ediliyor, mevcut adım bitince kapanacak…";
                bClose.Enabled = false;
                closeWhenDone = true;
                return;
            }
            base.OnFormClosing(e);
        }

        bool closeWhenDone;

        async Task StartScan()
        {
            if (running || Actions.Scanning) return;
            running = true;
            bStart.Enabled = false;
            chkAll.Enabled = false;
            bDiscord.Visible = bHelp.Visible = false;
            bClose.Text = "İptal";
            report.Clear();
            cts = new CancellationTokenSource();
            scanner = new Scanner { Full = chkAll.Checked || forceFull, Hosts = App.Settings.TestHosts.ToList() };
            scanner.Line += (text, kind) =>
            {
                if (IsDisposed) return;
                switch (kind)
                {
                    case ScanLineKind.Step: report.Out(text, Theme.Text, Bold); break;
                    case ScanLineKind.Good: report.Out(text, GreenText); break;
                    case ScanLineKind.Warn: report.Out(text, AmberText); break;
                    case ScanLineKind.Bad: report.Out(text, RedText); break;
                    case ScanLineKind.Muted: report.Out(text, Muted, Small); break;
                    default: report.Out(text); break;
                }
            };
            scanner.Progress += (done, total, label) =>
            {
                if (IsDisposed) return;
                bar.Value = total == 0 ? 0 : done / (float)total;
                lblStep.Text = done >= total && total > 0 ? "Tamamlandı." : "Deneniyor: " + label + "  (" + (done + 1) + "/" + total + ")";
            };
            var sw = Stopwatch.StartNew();
            try { await scanner.RunAsync(cts.Token); }
            catch (Exception ex) { report.Bad("Hata: " + ex.Message); }
            running = false;
            if (IsDisposed) return;
            bar.Value = 1;
            lblStep.Text = (scanner.Best != null ? "Bitti: " + scanner.Best.Name : "Bitti") + "  ·  " + (int)sw.Elapsed.TotalSeconds + " sn";
            bClose.Text = "Kapat";
            bClose.Enabled = true;
            bStart.Enabled = true;
            bStart.Text = "Tekrar Tara";
            chkAll.Enabled = true;
            if (scanner.Best != null)
            {
                report.Blank();
                if (DiscordHelper.IsRunning)
                {
                    report.Out("Discord açık: yeni ayarın etkili olması için Discord'u yeniden başlatmanız önerilir.", AccentInk, Bold);
                    bDiscord.Visible = true;
                }
                else report.Muted("Discord'u şimdi açabilirsiniz.");
            }
            else if (!string.IsNullOrEmpty(scanner.FailCode))
            {
                var info = ErrorCatalog.Get(scanner.FailCode);
                report.Blank();
                report.Out("Ne yapmalı? (" + scanner.FailCode + " – " + info?.Title + ")", Theme.Text, Bold);
                if (info != null) foreach (var st in info.Steps) report.Out("  • " + st, Theme.Text, Small);
                bHelp.Visible = true;
            }
            if (closeWhenDone) Close();
        }
    }

    /// <summary>Hata kodlarındaki "Düzelt" düğmelerinin işlemleri.</summary>
    static class Fixes
    {
        public static async Task<string> Run(FixKind f, IWin32Window owner)
        {
            var mf = MainForm.Instance;
            switch (f)
            {
                case FixKind.RestartAsAdmin:
                    try
                    {
                        Process.Start(new ProcessStartInfo(Application.ExecutablePath) { Verb = "runas", UseShellExecute = true });
                        mf.ExitApp(false);
                    }
                    catch { return "İzin verilmedi."; }
                    return null;
                case FixKind.OpenAppFolder: Sys.OpenFolder(App.BaseDir); return "Klasör açıldı: " + App.BaseDir;
                case FixKind.ResetAll:
                    if (MessageBox.Show(owner, "Koruma durdurulacak; GoodbyeDPI/DPI Kalkan hizmetleri ve WinDivert sürücü kaydı kaldırılacak.\n\nDevam edilsin mi?",
                            App.Name, MessageBoxButtons.YesNo, MessageBoxIcon.Warning) != DialogResult.Yes) return null;
                    await Task.Run(() => App.Engine.ResetAll());
                    App.Settings.ServiceMode = false;
                    App.Save();
                    return "Sıfırlama tamamlandı. Sorun sürerse bilgisayarı yeniden başlatın.";
                case FixKind.ResolveConflicts:
                    await Task.Run(() => App.Engine.ResolveConflicts());
                    if (!App.Engine.IsActive) await Actions.StartAsync();
                    return "Çakışmalar giderildi.";
                case FixKind.FlushDns:
                    Native.FlushDns();
                    App.Log("DNS önbelleği temizlendi.", LogLevel.Info);
                    return "DNS önbelleği temizlendi. Discord ve tarayıcıyı yeniden başlatın.";
                case FixKind.FindBest: ScanDialog.Run(owner); return null;
                case FixKind.OpenProfiles: mf.Navigate("profiles"); return null;
                case FixKind.OpenDnsPage: mf.Navigate("dns"); return null;
                case FixKind.TestDnsServers: mf.Go<NetPage>("net")?.ShowTab(2, true); return null;
                case FixKind.SiteAnalysis: mf.Go<NetPage>("net")?.ShowTab(1, false); return null;
                case FixKind.StartBfe:
                    {
                        var r = await Task.Run(() =>
                        {
                            Engine.Sc("config BFE start= auto");
                            return Engine.Sc("start BFE");
                        });
                        return r.Code == 0 || r.Output.Contains("1056") ? "BFE hizmeti başlatıldı." : "BFE başlatılamadı: " + r.Output;
                    }
                case FixKind.OpenWindowsSecurity: Sys.OpenUri("windowsdefender://threat/"); return null;
                case FixKind.RestartDiscord: return await Actions.RestartDiscordAsync();
                case FixKind.StartProtection: await Actions.StartAsync(); return App.Engine.State == EngineState.Running ? "Koruma yeniden başlatıldı." : App.Engine.LastError;
                case FixKind.OpenLog: mf.Navigate("log"); return null;
                case FixKind.OpenSites: mf.Navigate("sites"); return null;
                case FixKind.OpenProxySettings: Sys.OpenUri("ms-settings:network-proxy"); return null;
                case FixKind.OpenDateTime: Sys.OpenUri("ms-settings:dateandtime"); return null;
                case FixKind.OpenSettings: mf.Navigate("settings"); return null;
                case FixKind.OpenProxyPage: mf.Navigate("proxy"); return null;
                case FixKind.OpenHosts: Sys.SelectFile(NetTest.HostsPath); return "hosts dosyası Gezgin'de seçildi. Not Defteri'ni yönetici olarak açıp düzenleyin.";
                case FixKind.DisableServiceMode:
                    App.Settings.ServiceMode = false;
                    App.Save();
                    await Task.Run(() => App.Engine.UninstallService());
                    await Actions.StartAsync();
                    return "Uygulama moduna geçildi.";
            }
            return null;
        }
    }
}
