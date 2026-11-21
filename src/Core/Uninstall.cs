using System;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Text;
using System.Windows.Forms;

namespace DPIKalkan
{
    /// <summary>
    /// Temiz kaldırma: DPI Kalkan'ın Windows'ta yaptığı tüm değişiklikleri geri alır (hizmet, açılış görevi, WinDivert
    /// sürücü kaydı, Windows proxy ayarı, değiştirilen DNS sunucuları, yerel köprü, geçici dosyalar).
    /// Program klasörünü ve ayarları silmez. Komut satırı: DPIKalkan.exe --uninstall [--quiet]
    /// </summary>
    public static class Uninstaller
    {
        public static string Run()
        {
            var r = new StringBuilder();
            App.Log("Temiz kaldırma başlatıldı.", LogLevel.Info);

            // 1) koruma, hizmet ve sürücü kaydı
            try { App.Engine.RemoveForUninstall(r); }
            catch (Exception ex) { r.AppendLine("• Hizmet/sürücü temizlenemedi: " + ex.Message); }

            // 2) açılış görevi (yalnızca bu kuruluma ya da artık var olmayan bir klasöre aitse)
            try
            {
                string cmd = Startup.CommandPath();
                if (cmd != null)
                {
                    bool ours = string.Equals(Path.GetFullPath(cmd), Path.GetFullPath(Application.ExecutablePath), StringComparison.OrdinalIgnoreCase) || !File.Exists(cmd);
                    if (ours) { Startup.Disable(); r.AppendLine("• Windows açılış görevi kaldırıldı."); }
                    else r.AppendLine("• Açılış görevi başka bir DPI Kalkan kurulumuna ait olduğu için bırakıldı: " + cmd);
                }
            }
            catch (Exception ex) { r.AppendLine("• Açılış görevi kaldırılamadı: " + ex.Message); }

            // 3) yerel proxy köprüsü ve Windows proxy ayarı
            try { ProxyBridge.StopAll(); } catch (Exception ign) { App.Ignored("Yerel köprü durdurulamadı", ign); }
            try
            {
                if (App.Settings.SystemProxyBackup != null)
                {
                    ProxyTools.RestoreSystem();
                    r.AppendLine("• Windows proxy ayarı DPI Kalkan'dan önceki hâline döndürüldü.");
                }
            }
            catch (Exception ex) { r.AppendLine("• Windows proxy ayarı geri alınamadı: " + ex.Message); }

            // 4) DPI Kalkan'ın değiştirdiği DNS sunucuları
            try
            {
                if (App.Settings.DnsBackups.Count > 0 || App.Settings.DohBackups.Count > 0 || App.Settings.GlobalDohBackup != null)
                {
                    bool ok = SystemDns.Restore(out string rep, onlyBackups: true);
                    r.AppendLine(ok ? "• Değiştirilen DNS sunucuları eski hâline döndürüldü." : "• DNS sunucularının bir kısmı geri alınamadı:\n" + rep);
                }
            }
            catch (Exception ex) { r.AppendLine("• DNS ayarı geri alınamadı: " + ex.Message); }

            // 5) geçici dosyalar
            try
            {
                Updater.Cleanup();
                foreach (var f in Directory.GetFiles(Path.GetTempPath(), "DPIKalkan-*-proxy.lnk")) try { File.Delete(f); } catch (Exception ign) { App.Ignored("Geçici kısayol silinemedi", ign); }
                string prev = Path.Combine(Path.GetTempPath(), "dpikalkan_preview");
                if (Directory.Exists(prev)) try { Directory.Delete(prev, true); } catch (Exception ign) { App.Ignored("Önizleme klasörü silinemedi", ign); }
            }
            catch (Exception ign) { App.Ignored("Geçici dosyalar temizlenemedi", ign); }

            // 6) ayarlar: kalıcı sistem değişikliği kalmadığını kaydet
            App.Settings.ServiceMode = false;
            App.Settings.StartWithWindows = false;
            App.Save();
            Native.FlushDns();

            if (r.Length == 0) r.AppendLine("• Geri alınacak bir sistem değişikliği bulunamadı.");
            string profiles = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "DPI Kalkan");
            if (Directory.Exists(profiles))
                r.AppendLine("• Proxy için oluşturulan tarayıcı profilleri silinmedi (kişisel veri içerebilir): " + profiles);
            string text = r.ToString().TrimEnd();
            App.Log("Temiz kaldırma tamamlandı:\n" + text, LogLevel.Success);
            return text;
        }

        /// <summary>
        /// Açılışta çağrılır. Korumasız (herkesin yazabildiği) bir klasörü gösteren DPI Kalkan hizmeti ya da açılış görevi
        /// varsa kaldırılır; bu kurulum korumalıysa ve kullanıcı özelliği açtıysa daha sonra bu klasöre yeniden yönlendirilir.
        /// Dönüş: kaldırılan hizmet çalışıyor muydu (koruma uygulama modunda sürdürülmeli mi).
        /// </summary>
        public static bool EnforceLocationRules(bool thisProtected, out string removed)
        {
            var sb = new StringBuilder();
            bool wasRunning = false;
            try
            {
                if (Engine.ServiceExists(Engine.ServiceName))
                {
                    string exe = Engine.ServiceExe(Engine.ServiceName);
                    string inst = exe == null ? null : Path.GetDirectoryName(Path.GetDirectoryName(Path.GetDirectoryName(exe)));
                    bool safe = inst != null && InstallGuard.Check(inst, out _);
                    bool repointLater = thisProtected && App.Settings.ServiceMode;
                    if (!safe && !repointLater)
                    {
                        try
                        {
                            using (var sc = new System.ServiceProcess.ServiceController(Engine.ServiceName))
                                wasRunning = sc.Status == System.ServiceProcess.ServiceControllerStatus.Running;
                        }
                        catch (Exception ign) { App.Ignored("Hizmet durumu okunamadı", ign); }
                        App.Engine.UninstallService();
                        sb.Append("arka plan hizmeti");
                        App.Log("[DK-705] Korumasız klasörü gösteren arka plan hizmeti kaldırıldı: " + exe, LogLevel.Warn);
                    }
                }
            }
            catch (Exception ex) { App.Log("Hizmet denetlenemedi: " + ex.Message, LogLevel.Warn); }
            try
            {
                string cmd = Startup.CommandPath();
                if (cmd != null)
                {
                    bool safe = File.Exists(cmd) && InstallGuard.Check(Path.GetDirectoryName(cmd), out _);
                    bool reRegisterLater = thisProtected && App.Settings.StartWithWindows;
                    if (!safe && !reRegisterLater)
                    {
                        Startup.Disable();
                        sb.Append(sb.Length > 0 ? " ve " : "").Append("Windows açılış görevi");
                        App.Log("[DK-705] Korumasız klasörü gösteren açılış görevi kaldırıldı: " + cmd, LogLevel.Warn);
                    }
                }
            }
            catch (Exception ex) { App.Log("Açılış görevi denetlenemedi: " + ex.Message, LogLevel.Warn); }
            if (!thisProtected && (App.Settings.ServiceMode || App.Settings.StartWithWindows))
            {
                App.Settings.ServiceMode = false;
                App.Settings.StartWithWindows = false;
                App.Save();
            }
            removed = sb.ToString();
            return wasRunning;
        }

        /// <summary>
        /// --prepare-upgrade: kurulum paketi bu klasördeki dosyaları değiştirmeden önce çağırır. Bu klasörden çalışan DPI Kalkan'ı
        /// ve GoodbyeDPI'ı kapatır, hizmet bu klasörü gösteriyorsa durdurur. Ayarlara, proxy/DNS'e ve görev kaydına dokunmaz.
        /// Dönüş: 10 hizmet çalışıyordu (kurulumdan sonra yeniden başlatılmalı), 0 çalışmıyordu, 1 yönetici izni yok.
        /// </summary>
        public static int PrepareUpgrade()
        {
            if (!App.IsAdmin()) return 1;
            string self = Process.GetCurrentProcess().MainModule.FileName;
            string dir = Path.GetDirectoryName(self).TrimEnd('\\') + "\\";
            int me = Process.GetCurrentProcess().Id;
            var log = new StringBuilder();
            foreach (var p in Process.GetProcessesByName("DPIKalkan"))
                using (p)
                {
                    try
                    {
                        if (p.Id != me && p.MainModule.FileName.StartsWith(dir, StringComparison.OrdinalIgnoreCase))
                        {
                            if (!p.WaitForExit(3000)) { p.Kill(); p.WaitForExit(5000); }
                            log.AppendLine("Uygulama kapatıldı (PID " + p.Id + ").");
                        }
                    }
                    catch (Exception ex) { log.AppendLine("Uygulama kapatılamadı: " + ex.Message); }
                }
            bool wasRunning = false;
            try
            {
                using (var sc = new System.ServiceProcess.ServiceController(Engine.ServiceName))
                {
                    string exe = Engine.ServiceExe(Engine.ServiceName);
                    if (exe != null && Path.GetFullPath(exe).StartsWith(dir, StringComparison.OrdinalIgnoreCase) &&
                        sc.Status != System.ServiceProcess.ServiceControllerStatus.Stopped)
                    {
                        wasRunning = true;
                        if (sc.Status != System.ServiceProcess.ServiceControllerStatus.StopPending) sc.Stop();
                        sc.WaitForStatus(System.ServiceProcess.ServiceControllerStatus.Stopped, TimeSpan.FromSeconds(30));
                        log.AppendLine("Arka plan hizmeti durduruldu.");
                    }
                }
            }
            catch (InvalidOperationException) { /* hizmet yok */ }
            catch (Exception ex) { log.AppendLine("Hizmet durdurulamadı: " + ex.Message); }
            foreach (var p in Process.GetProcessesByName("goodbyedpi"))
                using (p)
                {
                    try { if (p.MainModule.FileName.StartsWith(dir, StringComparison.OrdinalIgnoreCase)) { p.Kill(); p.WaitForExit(3000); log.AppendLine("goodbyedpi kapatıldı."); } }
                    catch (Exception ex) { log.AppendLine("goodbyedpi kapatılamadı: " + ex.Message); }
                }
            string data = Path.Combine(dir, "veri");
            CrashMarker.Clear(data);
            try
            {
                Directory.CreateDirectory(data);
                File.AppendAllText(Path.Combine(data, "guncelleme.log"), DateTime.Now.ToString("yyyy-MM-dd HH:mm:ss") + "  Kurulum paketi öncesi hazırlık: " +
                                   (log.Length > 0 ? log.ToString().Replace(Environment.NewLine, " ") : "çalışan bir şey yok.") + Environment.NewLine, Encoding.UTF8);
            }
            catch (Exception ign) { App.Ignored("Hazırlık günlüğü yazılamadı", ign); }
            return wasRunning ? 10 : 0;
        }

        /// <summary>--uninstall: çalışan kopyayı kapatır, temizler. 0 başarılı, 1 yönetici izni yok.</summary>
        public static int RunCli(bool quiet)
        {
            if (!App.IsAdmin()) return 1;
            string self = Process.GetCurrentProcess().MainModule.FileName;
            int me = Process.GetCurrentProcess().Id;
            foreach (var p in Process.GetProcessesByName("DPIKalkan"))
                using (p)
                {
                    try
                    {
                        if (p.Id != me && string.Equals(p.MainModule.FileName, self, StringComparison.OrdinalIgnoreCase))
                        { p.Kill(); p.WaitForExit(5000); }
                    }
                    catch (Exception ign) { App.Ignored("Çalışan kopya kapatılamadı", ign); }
                }
            CrashMarker.Clear(App.DataDir);   // kapatılan kopya için "beklenmedik kapanış" uyarısı çıkmasın
            App.Init();
            string report = Run();
            if (!quiet)
                MessageBox.Show("DPI Kalkan'ın Windows'ta yaptığı değişiklikler geri alındı:\n\n" + report +
                                "\n\nArtık program klasörünü silebilirsiniz.", App.Name, MessageBoxButtons.OK, MessageBoxIcon.Information);
            return 0;
        }
    }
}
