using System;
using System.Drawing;
using System.Text.RegularExpressions;
using System.Threading;
using System.Threading.Tasks;
using System.Windows.Forms;
using static DPIKalkan.Theme;

namespace DPIKalkan
{
    /// <summary>Yeni sürüm penceresi: notlar, indirme (SHA-256 doğrulamalı) ve kurulum.</summary>
    sealed class UpdateDialog : Form
    {
        readonly UpdateInfo info;
        readonly ReportBox notes;
        readonly ProgressLine bar;
        readonly Label status;
        readonly FlatBtn bUpdate, bSkip, bLater;
        CancellationTokenSource cts;
        bool busy;

        /// <summary>Güncelleme denetler; yeni sürüm varsa pencereyi açar, yoksa (elle denetimde) bilgi verir.</summary>
        public static async Task CheckAndShow(IWin32Window owner, bool manual)
        {
            if (Updater.Checking) return;
            string err = null;
            var i = await Task.Run(() => Updater.Check(out err));
            if (i == null)
            {
                if (manual) MessageBox.Show(owner, "Güncelleme denetlenemedi.\n\n" + err, App.Name, MessageBoxButtons.OK, MessageBoxIcon.Warning);
                return;
            }
            if (!Updater.IsNewer(i))
            {
                if (manual) MessageBox.Show(owner, "DPI Kalkan güncel (" + App.Version + ").", App.Name, MessageBoxButtons.OK, MessageBoxIcon.Information);
                return;
            }
            if (!manual && Updater.Skipped(i)) return;
            if (!manual)
            {
                MainForm.Instance?.ShowBalloon("Yeni sürüm: " + i.Version, "DPI Kalkan " + i.Version + " yayımlandı. Ana sayfadaki 'Güncelle' düğmesiyle kurabilirsiniz.", ToolTipIcon.Info);
                return;
            }
            ShowFor(owner, i);
        }

        public static void ShowFor(IWin32Window owner, UpdateInfo i)
        {
            using (var d = new UpdateDialog(i)) d.ShowDialog(owner);
        }

        UpdateDialog(UpdateInfo i)
        {
            info = i;
            Text = "Güncelleme – " + App.Name;
            FormBorderStyle = FormBorderStyle.FixedDialog;
            StartPosition = FormStartPosition.CenterParent;
            MaximizeBox = MinimizeBox = false;
            ShowInTaskbar = false;
            AutoScaleMode = AutoScaleMode.None;
            BackColor = Bg;
            Font = Body;
            ClientSize = new Size(D(600), D(520));
            Icon = IconArt.AppIcon(32);

            Controls.Add(UIx.L("Yeni sürüm: DPI Kalkan " + i.Version, H2, Theme.Text, 24, 16, 550, 30));
            Controls.Add(UIx.L("Şu anki sürüm " + App.Version + (i.Published > DateTime.MinValue ? "  ·  Yayın: " + i.Published.ToLocalTime().ToString("dd.MM.yyyy") : "") +
                               (i.Size > 0 ? "  ·  " + (i.Size / 1024) + " KB" : "") + "  ·  SHA-256 ile doğrulanır", Small, Muted, 25, 48, 550, 20));
            var card = new Card { Bounds = R(24, 78, 552, 330) };
            Controls.Add(card);
            notes = card.Add(new ReportBox { Bounds = R(16, 12, 520, 306) });
            notes.BackColor = Theme.Card;
            notes.Out("YENİLİKLER", Muted, Caption);
            foreach (var raw in (i.Notes ?? "").Replace("\r", "").Split('\n'))
            {
                string line = raw.Trim();
                if (line.StartsWith("**SHA-256") || line.StartsWith("SHA-256")) continue;
                bool head = line.StartsWith("#");
                line = Regex.Replace(line, @"^#+\s*", "");
                line = line.Replace("**", "").Replace("`", "");
                line = Regex.Replace(line, @"^[-*]\s+", "•  ");
                line = Regex.Replace(line, @"\[([^\]]+)\]\([^)]+\)", "$1");
                if (head) notes.Out(line, Theme.Text, Bold); else notes.Out(line, Theme.Text, Small);
            }
            notes.SelectionStart = 0;

            status = UIx.L("Güncelleme indirilir, doğrulanır ve uygulama yeni sürümle yeniden açılır. Ayarlarınız korunur; koruma birkaç saniye kesilir.",
                Small, Muted, 24, 414, 552, 34);
            Controls.Add(status);
            bar = new ProgressLine { Bounds = R(24, 450, 552, 6), BackColor = Bg, Visible = false };
            Controls.Add(bar);

            bUpdate = new FlatBtn { Text = "Şimdi güncelle", Glyph = GRefresh, Kind = FlatBtn.Kinds.Primary, Bounds = R(24, 466, 190, 40) };
            bSkip = new FlatBtn { Text = "Bu sürümü atla", Kind = FlatBtn.Kinds.Secondary, Bounds = R(222, 466, 160, 40) };
            bLater = new FlatBtn { Text = "Sonra", Kind = FlatBtn.Kinds.Secondary, Bounds = R(456, 466, 120, 40) };
            Controls.AddRange(new Control[] { bUpdate, bSkip, bLater });
            bUpdate.Click += async (s, e) => await DoUpdate();
            bSkip.Click += (s, e) =>
            {
                App.Settings.SkipVersion = i.Version.ToString();
                App.Save();
                App.Log("Güncelleme atlandı: " + i.Version, LogLevel.Info);
                Close();
            };
            bLater.Click += (s, e) => Close();
        }

        protected override void OnHandleCreated(EventArgs e)
        {
            base.OnHandleCreated(e);
            Native.DarkTitle(Handle, IsDark);
        }

        protected override void OnFormClosing(FormClosingEventArgs e)
        {
            if (busy) { cts?.Cancel(); e.Cancel = true; return; }
            base.OnFormClosing(e);
        }

        async Task DoUpdate()
        {
            busy = true;
            bUpdate.Enabled = bSkip.Enabled = false;
            bLater.Text = "İptal";
            bar.Visible = true;
            status.ForeColor = Muted;
            cts = new CancellationTokenSource();
            try
            {
                status.Text = "İndiriliyor…";
                string zip = await Task.Run(() => Updater.Download(info, (got, total) =>
                {
                    try
                    {
                        BeginInvoke(new Action(() =>
                        {
                            if (IsDisposed) return;
                            bar.Value = total > 0 ? got / (float)total : 0;
                            status.Text = "İndiriliyor… " + (got / 1024) + " / " + (total / 1024) + " KB";
                        }));
                    }
                    catch { }
                }, cts.Token));
                status.Text = "Doğrulandı (SHA-256). Paket açılıyor…";
                string root = await Task.Run(() => Updater.Extract(zip, info.Version));
                status.Text = "Kuruluyor… Uygulama birkaç saniye içinde yeniden açılacak.";
                status.ForeColor = GreenText;
                await Task.Delay(600);
                Updater.StartApply(root);
                busy = false;
                MainForm.Instance.ExitApp(false);
            }
            catch (OperationCanceledException)
            {
                busy = false;
                status.Text = "İptal edildi.";
                Reset();
            }
            catch (Exception ex)
            {
                busy = false;
                App.Log("Güncelleme başarısız: " + ex.Message, LogLevel.Error);
                status.Text = "Güncelleme yapılamadı: " + ex.Message;
                status.ForeColor = RedText;
                Reset();
            }
        }

        void Reset()
        {
            if (IsDisposed) return;
            bUpdate.Enabled = bSkip.Enabled = true;
            bLater.Text = "Kapat";
            bar.Visible = false;
        }
    }
}
