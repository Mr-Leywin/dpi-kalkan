using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Management;
using System.ServiceProcess;
using System.Text;
using System.Threading;

namespace DPIKalkan
{
    public enum EngineState { Stopped, Starting, Running, Stopping, Error }

    /// <summary>goodbyedpi.exe işlemini veya Windows hizmetini yönetir.</summary>
    public sealed class Engine
    {
        public const string ServiceName = "DPIKalkan";
        public const string LegacyServiceName = "GoodbyeDPI";

        readonly object gate = new object();
        Process proc;
        bool userStop = true;
        string consoleText = "";
        int loggedLines;
        readonly List<DateTime> restarts = new List<DateTime>();
        int polling;

        public EngineState State { get; private set; } = EngineState.Stopped;
        public string LastError { get; private set; } = "";
        public string LastErrorCode { get; private set; } = "";
        public DateTime? StartedAt { get; private set; }
        public Profile ActiveProfile { get; private set; }
        public bool RunningAsService { get; private set; }
        /// <summary>Otomatik tarama sırasında bildirim ve otomatik yeniden başlatmayı bastırır.</summary>
        public bool Quiet { get; set; }
        public int ExternalCount { get; private set; }
        public bool LegacyServiceExists { get; private set; }
        public bool LegacyServiceRunning { get; private set; }
        public bool ServiceInstalled { get; private set; }

        public event Action<EngineState, EngineState> StateChanged;

        public bool IsActive => State == EngineState.Running || State == EngineState.Starting;
        public bool HasConflict => ExternalCount > 0 || LegacyServiceExists;

        public static string Arch => Environment.Is64BitOperatingSystem ? "x86_64" : "x86";
        public static string BinDir => Path.Combine(App.BaseDir, "bin", Arch);
        public static string ExePath => Path.Combine(BinDir, "goodbyedpi.exe");

        // ------------------------------------------------------------------ durum

        void SetState(EngineState s, string error = null)
        {
            var old = State;
            State = s;
            if (error != null) LastError = error;
            else if (s == EngineState.Running || s == EngineState.Stopped) { LastError = ""; LastErrorCode = ""; }
            App.Post(() => StateChanged?.Invoke(old, s));
            App.RaiseStatus();
        }

        void Fail(string msg, string code)
        {
            StartedAt = null;
            LastErrorCode = code ?? "";
            App.Log((code != null ? "[" + code + "] " : "") + msg, LogLevel.Error);
            if (!string.IsNullOrEmpty(code) && !Quiet) App.Problems.Report(code, msg);
            SetState(EngineState.Error, msg);
        }

        /// <summary>Motorla ilgili eski sorun kayıtlarını temizler (başarılı başlatma sonrası).</summary>
        static void ClearEngineProblems()
        {
            App.Problems.ClearWhere(e => (e.Category == "Sürücü (WinDivert)" && e.Code != "DK-207") || e.Category == "GoodbyeDPI" ||
                                         e.Code == "DK-102" || e.Code == "DK-701" || e.Code == "DK-702" || e.Code == "DK-704" ||
                                         e.Code == "DK-301" || e.Code == "DK-302");
        }

        public static string[] RequiredFiles => new[] { "goodbyedpi.exe", "WinDivert.dll", Environment.Is64BitOperatingSystem ? "WinDivert64.sys" : "WinDivert32.sys" };

        public static string MissingFile()
        {
            foreach (var f in RequiredFiles)
                if (!File.Exists(Path.Combine(BinDir, f))) return f;
            return null;
        }

#if PREVIEW
        public void DebugSet(EngineState s, Profile p, string err = null)
        {
            ActiveProfile = p; StartedAt = s == EngineState.Running ? DateTime.Now.AddMinutes(-83) : (DateTime?)null;
            State = s; LastError = err ?? "";
        }
        public void DebugConflict(int ext, bool legacy) { ExternalCount = ext; LegacyServiceExists = legacy; }
#endif

        // ------------------------------------------------------------------ başlat / durdur

        public bool Start(Profile p, bool forceApp = false)
        {
            if (App.Preview) return false;
            lock (gate)
            {
                bool asService = App.Settings.ServiceMode && !forceApp;
                if (asService && !InstallGuard.IsProtected)
                {
                    // SYSTEM hizmeti yalnızca korumalı (kurulumlu) klasörden çalıştırılır
                    App.Log("[DK-705] Taşınabilir sürümde arka plan hizmeti kullanılamaz; uygulama modunda başlatılıyor.", LogLevel.Warn);
                    App.Settings.ServiceMode = false;
                    App.Post(App.Save);
                    asService = false;
                }
                ActiveProfile = p;
                SetState(EngineState.Starting);
                userStop = true;
                StopInternal();
                int killed = KillExternalInternal(-1, -1);
                if (killed > 0) App.Log("Çakışan " + killed + " GoodbyeDPI işlemi/hizmeti kapatıldı.", LogLevel.Warn);

                string missing = MissingFile();
                if (missing != null)
                {
                    Fail(missing + " bulunamadı (" + BinDir + "). Antivirüs dosyayı karantinaya almış olabilir: karantinadan geri yükleyin ve " +
                         "dosyayı Microsoft'a yanlış pozitif olarak bildirin (DK-102); istisna eklemeyi yalnızca son çare olarak kullanın.", "DK-102");
                    return false;
                }

                if (!ArgPolicy.ValidateProfile(p, out string argErr))
                {
                    Fail("'" + p.Name + "' profili güvenli olmayan ya da tanınmayan bir parametre içeriyor: " + argErr, "DK-605");
                    return false;
                }

                if (!Quiet && App.Settings.CheckDnsOnStart && p.UsesDnsRedirect) CheckDnsTarget(p);

                string blacklist = null;
                if (App.Settings.TargetedMode)
                {
                    try { blacklist = App.WriteBlacklist(); }
                    catch (Exception ex) { App.Log("Hedef site listesi yazılamadı, tüm trafiğe uygulanacak: " + ex.Message, LogLevel.Warn); }
                }

                string args = p.BuildArgs(blacklist);
                App.Log("Başlatılıyor: " + p.Name + (asService ? " (arka plan hizmeti)" : ""), LogLevel.Info);
                App.Log("Komut: goodbyedpi.exe " + args, LogLevel.Debug);

                bool ok;
                // dosyalar doğrulanır ve başlatma bitene kadar yazmaya kapalı tutulur
                using (var hold = Integrity.VerifyAndLock(BinDir, Arch, RequiredFiles, out string intErr))
                {
                    if (hold == null)
                    {
                        Fail("GoodbyeDPI dosyaları doğrulanamadı: " + intErr + " Dosya bozulmuş ya da değiştirilmiş olabilir; " +
                             "DPI Kalkan'ı resmi sürüm sayfasından yeniden indirin.", "DK-105");
                        return false;
                    }
                    ok = asService ? StartService(args) : StartProcess(args);
                }
                if (!ok) return false;

                userStop = false;
                StartedAt = DateTime.Now;
                if (App.Settings.FlushDns) Native.FlushDns();
                if (!Quiet) ClearEngineProblems();
                SetState(EngineState.Running);
                App.Log("Koruma aktif: " + p.Name, LogLevel.Success);
                return true;
            }
        }

        bool StartProcess(string args)
        {
            consoleText = "";
            loggedLines = 0;
            var psi = new ProcessStartInfo(ExePath, args)
            {
                UseShellExecute = false,
                CreateNoWindow = true,      // pencere yok ama konsol var → çıktıyı okuyabiliyoruz
                WorkingDirectory = BinDir,
            };
            var p = new Process { StartInfo = psi, EnableRaisingEvents = true };
            p.Exited += OnExited;
            try { p.Start(); }
            catch (Exception ex)
            {
                bool notFound = ex is System.ComponentModel.Win32Exception w && w.NativeErrorCode == 2;
                Fail("goodbyedpi.exe çalıştırılamadı: " + ex.Message + ". Antivirüs dosyayı engelliyor olabilir.", notFound ? "DK-102" : "DK-601");
                return false;
            }
            Native.AttachToKillJob(p.Handle);
            proc = p;
            RunningAsService = false;

            // "Filter activated" ya da hata mesajını bekle
            var sw = Stopwatch.StartNew();
            while (sw.ElapsedMilliseconds < 8000)
            {
                if (p.WaitForExit(250))
                {
                    proc = null;
                    Fail("GoodbyeDPI hemen kapandı (çıkış kodu " + SafeExitCode(p) + "). " + Explain(consoleText), Diagnose(consoleText).Code);
                    return false;
                }
                ReadConsoleIntoLog(p);
                if (consoleText.Contains("Filter activated")) return true;
                if (LooksLikeError(consoleText))
                {
                    proc = null;
                    try { p.Kill(); p.WaitForExit(2000); } catch (Exception ign) { App.Ignored("GoodbyeDPI kapatılamadı", ign); }
                    Fail("GoodbyeDPI başlatılamadı. " + Explain(consoleText), Diagnose(consoleText).Code);
                    return false;
                }
            }
            App.Log("[DK-604] GoodbyeDPI çalışıyor ancak durum mesajı okunamadı; çalıştığı varsayılıyor.", LogLevel.Warn);
            return true;
        }

        static bool LooksLikeError(string t) =>
            t.Contains("Error opening filter") || t.Contains("parameter error") || t.Contains("ERROR:") ||
            t.Contains("Unknown option") || t.Contains("unknown option") || t.Contains("requires an argument");

        void ReadConsoleIntoLog(Process p)
        {
            int pid;
            try { if (p.HasExited) return; pid = p.Id; } catch { return; }
            string t = ConsoleReader.Read(pid);
            if (t == null) return;
            consoleText = t;
            var lines = t.Split('\n');
            int count = lines.Length;
            while (count > 0 && lines[count - 1].Length == 0) count--;
            for (int i = loggedLines; i < count; i++)
                if (lines[i].Trim().Length > 0) App.Log(lines[i].TrimEnd(), LogLevel.Output);
            loggedLines = Math.Max(loggedLines, count);
        }

        /// <summary>DNS yönlendirmesinin hedef sunucusu yanıt veriyor mu? Vermiyorsa tüm internet durabilir.</summary>
        void CheckDnsTarget(Profile p)
        {
            var t = p.DnsTarget;
            if (t == null) return;
            if (!System.Net.IPAddress.TryParse(t.Value.Addr, out _)) return;
            var r = NetTest.TimedQuery("discord.com", t.Value.Addr, t.Value.Port, 2000);
            if (!r.Ok) r = NetTest.TimedQuery("discord.com", t.Value.Addr, t.Value.Port, 2500);
            if (r.Ok)
            {
                App.Problems.Clear("DK-402");
                App.Log("DNS sunucusu yanıt veriyor: " + t.Value.Addr + ":" + t.Value.Port + " (" + r.Ms + " ms)", LogLevel.Debug);
            }
            else
            {
                App.Log("[DK-402] DNS sunucusu yanıt vermiyor: " + t.Value.Addr + ":" + t.Value.Port +
                        ". Koruma açıkken siteler açılmayabilir; farklı DNS'li bir profil seçin.", LogLevel.Warn);
                App.Problems.Report("DK-402", t.Value.Addr + ":" + t.Value.Port);
            }
        }

        /// <summary>Kurulu hizmetin çalıştırdığı komut satırı (yoksa null).</summary>
        public static string ServicePath(string name)
        {
            try
            {
                using (var q = new ManagementObjectSearcher("SELECT PathName FROM Win32_Service WHERE Name='" + name + "'"))
                    foreach (ManagementObject mo in q.Get())
                        using (mo) return mo["PathName"] as string;
            }
            catch (Exception ign) { App.Ignored("Hizmet yolu okunamadı", ign); }
            return null;
        }

        /// <summary>Hizmet farklı (eski) bir klasördeki goodbyedpi.exe'yi mi gösteriyor?</summary>
        public static bool ServicePointsElsewhere()
        {
            string path = ServicePath(ServiceName);
            return path != null && path.IndexOf(ExePath, StringComparison.OrdinalIgnoreCase) < 0;
        }

        static int SafeExitCode(Process p)
        {
            try { return p.ExitCode; } catch { return -1; }
        }

        /// <summary>GoodbyeDPI çıktısından hata kodunu ve kısa açıklamayı çıkarır.</summary>
        public static (string Code, string Hint) Diagnose(string output)
        {
            string t = output ?? "";
            if (t.Contains("were not found"))
                return ("DK-202", "WinDivert sürücü dosyaları bulunamadı. Antivirüs dosyaları silmiş olabilir; karantinadan geri yükleyip yanlış pozitif olarak bildirin ya da paketi yeniden çıkarın.");
            if (t.Contains("incompatible version"))
                return ("DK-206", "Farklı sürümde bir WinDivert sürücüsü yüklü. 'Tümünü sıfırla' yapıp bilgisayarı yeniden başlatın.");
            if (t.Contains("blocked by security software"))
                return ("DK-204", "WinDivert sürücüsü antivirüs programı tarafından engellendi. Dosyaların resmi olduğunu Tam kontrolle doğrulayıp yanlış pozitif olarak bildirin; istisna yalnızca son çare.");
            if (t.Contains("Base Filtering Engine"))
                return ("DK-205", "'Temel Filtreleme Altyapısı' (BFE) Windows hizmeti kapalı.");
            if (t.Contains("digital signature"))
                return ("DK-203", "Sürücü imzası doğrulanamadı. Windows güncel değil ya da sürücü dosyası bozuk.");
            if (t.Contains("parameter error") || t.Contains("nknown option") || t.Contains("requires an argument") || t.Contains("Usage:"))
                return ("DK-603", "Profil parametrelerinde hata var. Profili düzenleyip kontrol edin.");
            if (t.Contains("Error opening filter") || t.Contains("WinDivert"))
                return ("DK-201", "WinDivert sürücüsü yüklenemedi. Antivirüs engelliyor ya da başka bir DPI aracı çalışıyor olabilir; 'Tümünü sıfırla' deneyin.");
            return ("DK-601", "Antivirüs GoodbyeDPI'ı engelliyor olabilir ya da başka bir DPI aracı çalışıyor olabilir. 'Tümünü sıfırla' deneyin.");
        }

        public static string Explain(string output)
        {
            string t = output ?? "";
            string hint = Diagnose(t).Hint;
            string last = string.Join(" | ", t.Split('\n').Select(l => l.Trim()).Where(l => l.Length > 0 && !l.StartsWith("GoodbyeDPI") && !l.StartsWith("http")).Skip(0).Reverse().Take(3).Reverse());
            return hint + (last.Length > 0 ? " (Çıktı: " + last + ")" : "");
        }

        void OnExited(object sender, EventArgs e)
        {
            lock (gate)
            {
                if (!ReferenceEquals(sender, proc)) return;
                var p = proc;
                proc = null;
                int code = SafeExitCode(p);
                if (userStop) return;
                RunningAsService = false;
                Fail("GoodbyeDPI beklenmedik şekilde kapandı (çıkış kodu " + code + "). " + Explain(consoleText), "DK-602");
                TryAutoRestart();
            }
        }

        void TryAutoRestart()
        {
            if (!App.Settings.AutoRestart || Quiet || ActiveProfile == null) return;
            var now = DateTime.Now;
            restarts.RemoveAll(t => (now - t).TotalMinutes > 5);
            if (restarts.Count >= 3)
            {
                App.Log("Otomatik yeniden başlatma sınırına ulaşıldı (5 dakikada 3 deneme). Lütfen günlüğü kontrol edin.", LogLevel.Warn);
                return;
            }
            restarts.Add(now);
            var prof = ActiveProfile;
            App.Log("5 saniye sonra otomatik olarak yeniden başlatılacak…", LogLevel.Warn);
            ThreadPool.QueueUserWorkItem(_ =>
            {
                Thread.Sleep(5000);
                bool go;
                lock (gate) go = State == EngineState.Error && proc == null && !Quiet;
                if (go) Start(prof);
            });
        }

        public void Stop(bool quiet = false)
        {
            if (App.Preview) return;
            lock (gate)
            {
                userStop = true;
                bool wasActive = State != EngineState.Stopped;
                if (wasActive) SetState(EngineState.Stopping);
                StopInternal();
                StartedAt = null;
                if (App.Settings.FlushDns) Native.FlushDns();
                SetState(EngineState.Stopped);
                if (wasActive && !quiet) App.Log("Koruma durduruldu.", LogLevel.Info);
            }
        }

        void StopInternal()
        {
            if (proc != null)
            {
                var p = proc;
                proc = null;
                try { if (!p.HasExited) { p.Kill(); p.WaitForExit(3000); } } catch (Exception ign) { App.Ignored("GoodbyeDPI kapatılamadı", ign); }
            }
            try
            {
                using (var sc = new ServiceController(ServiceName))
                {
                    sc.Refresh();
                    if (sc.Status != ServiceControllerStatus.Stopped)
                    {
                        if (sc.Status != ServiceControllerStatus.StopPending) sc.Stop();
                        sc.WaitForStatus(ServiceControllerStatus.Stopped, TimeSpan.FromSeconds(15));
                    }
                }
            }
            catch (InvalidOperationException) { /* hizmet yok */ }
            catch (System.ServiceProcess.TimeoutException) { App.Log("Hizmet zamanında durmadı.", LogLevel.Warn); }
            catch (Exception ex) { App.Log("Hizmet durdurulamadı: " + ex.Message, LogLevel.Warn); }
            RunningAsService = false;
        }

        /// <summary>Uygulamaya ait olmayan goodbyedpi işlemlerini ve eski hizmeti durdurur.</summary>
        int KillExternalInternal(int keepPid1, int keepPid2)
        {
            int n = 0;
            try
            {
                using (var sc = new ServiceController(LegacyServiceName))
                {
                    if (sc.Status != ServiceControllerStatus.Stopped)
                    {
                        App.Log("Eski 'GoodbyeDPI' hizmeti durduruluyor…", LogLevel.Warn);
                        sc.Stop();
                        sc.WaitForStatus(ServiceControllerStatus.Stopped, TimeSpan.FromSeconds(10));
                        n++;
                    }
                }
            }
            catch (InvalidOperationException) { }
            catch (Exception ex) { App.Log("Eski hizmet durdurulamadı: " + ex.Message, LogLevel.Warn); }

            foreach (var p in Process.GetProcessesByName("goodbyedpi"))
            {
                using (p)
                {
                    if (p.Id == keepPid1 || p.Id == keepPid2) continue;
                    try { p.Kill(); p.WaitForExit(3000); n++; }
                    catch (Exception ex) { App.Log("İşlem kapatılamadı (PID " + p.Id + "): " + ex.Message, LogLevel.Warn); }
                }
            }
            return n;
        }

        /// <summary>Banttaki "Düzelt" düğmesi: çakışan işlemleri kapatır, eski hizmeti kaldırır.</summary>
        public void ResolveConflicts()
        {
            lock (gate)
            {
                int keep1 = -1, keep2 = -1;
                try { if (proc != null && !proc.HasExited) keep1 = proc.Id; } catch { }
                if (App.Settings.ServiceMode) keep2 = ServicePid(ServiceName);
                else
                {
                    // Hizmet modu kapalıyken kalmış kendi hizmetimizi de kaldır
                    var r0 = Sc("stop " + ServiceName);
                    var r1 = Sc("delete " + ServiceName);
                    if (r1.Code == 0) App.Log("Artık kullanılmayan DPI Kalkan hizmeti kaldırıldı.", LogLevel.Info);
                }
                int n = KillExternalInternal(keep1, keep2);
                var r = Sc("delete " + LegacyServiceName);
                if (r.Code == 0) App.Log("Eski 'GoodbyeDPI' hizmeti kaldırıldı (artık açılışta başlamayacak).", LogLevel.Success);
                App.Log("Çakışmalar giderildi (" + n + " işlem/hizmet kapatıldı).", LogLevel.Success);
                ExternalCount = 0;
                LegacyServiceExists = false;
                LegacyServiceRunning = false;
            }
            App.Problems.Clear("DK-301");
            App.Problems.Clear("DK-302");
            App.RaiseStatus();
            Poll();
        }

        // ------------------------------------------------------------------ hizmet modu

        bool StartService(string args)
        {
            if (!InstallGuard.IsProtected)
            {
                Fail("Arka plan hizmeti yalnızca kurulumlu sürümde kullanılabilir (" + InstallGuard.Reason + ")", "DK-705");
                return false;
            }
            string bin = "\"" + ExePath + "\" " + args;
            bool exists = ServiceExists(ServiceName);
            if (!exists)
            {
                var r = Sc("create " + ServiceName + " binPath= " + Esc(bin) + " start= auto DisplayName= " + Esc("DPI Kalkan"));
                if (r.Code != 0) { Fail("Arka plan hizmeti oluşturulamadı: " + r.Output, "DK-701"); return false; }
                Sc("description " + ServiceName + " " + Esc("DPI Kalkan – GoodbyeDPI ile erişim engellerini aşar."));
                Sc("failure " + ServiceName + " reset= 86400 actions= restart/5000/restart/15000/restart/60000");
                App.Log("Arka plan hizmeti kuruldu (Windows açılışında otomatik başlar).", LogLevel.Success);
            }
            else
            {
                var r = Sc("config " + ServiceName + " binPath= " + Esc(bin) + " start= auto");
                if (r.Code != 0) { Fail("Hizmet yapılandırılamadı: " + r.Output, "DK-701"); return false; }
            }
            ServiceInstalled = true;
            try
            {
                using (var sc = new ServiceController(ServiceName))
                {
                    sc.Refresh();
                    if (sc.Status != ServiceControllerStatus.Stopped)
                    {
                        sc.Stop();
                        sc.WaitForStatus(ServiceControllerStatus.Stopped, TimeSpan.FromSeconds(15));
                    }
                    sc.Start();
                    sc.WaitForStatus(ServiceControllerStatus.Running, TimeSpan.FromSeconds(15));
                    Thread.Sleep(2000);
                    sc.Refresh();
                    if (sc.Status != ServiceControllerStatus.Running)
                    {
                        Fail("Hizmet başladıktan hemen sonra durdu. Önce uygulama modunda deneyip hata kodunu görün.", "DK-702");
                        return false;
                    }
                }
            }
            catch (Exception ex)
            {
                Fail("Hizmet başlatılamadı: " + ex.Message + ". Önce uygulama modunda deneyip hata kodunu görün.", "DK-702");
                return false;
            }
            RunningAsService = true;
            return true;
        }

        public void UninstallService()
        {
            lock (gate)
            {
                bool was = RunningAsService;
                userStop = true;
                StopInternal();
                var r = Sc("delete " + ServiceName);
                if (r.Code == 0) App.Log("Arka plan hizmeti kaldırıldı.", LogLevel.Info);
                ServiceInstalled = false;
                if (was) { StartedAt = null; SetState(EngineState.Stopped); }
            }
            App.RaiseStatus();
        }

        public void ResetAll()
        {
            lock (gate)
            {
                userStop = true;
                SetState(EngineState.Stopping);
                StopInternal();
                KillExternalInternal(-1, -1);
                foreach (var n in new[] { ServiceName, LegacyServiceName, "WinDivert", "WinDivert14" })
                {
                    Sc("stop " + n);
                    var r = Sc("delete " + n);
                    if (r.Code == 0) App.Log("Hizmet kaldırıldı: " + n, LogLevel.Info);
                }
                Native.FlushDns();
                StartedAt = null;
                ServiceInstalled = false;
                ExternalCount = 0;
                LegacyServiceExists = LegacyServiceRunning = false;
                SetState(EngineState.Stopped);
                App.Log("Sıfırlama tamamlandı. Sorun sürerse bilgisayarı yeniden başlatın.", LogLevel.Success);
            }
        }

        // ------------------------------------------------------------------ periyodik kontrol

        public void Poll()
        {
            if (App.Preview) return;
            if (Interlocked.Exchange(ref polling, 1) == 1) return;
            try
            {
                bool legacyExists = false, legacyRunning = false, ourExists = false, ourRunning = false;
                foreach (var s in ServiceController.GetServices())
                {
                    using (s)
                    {
                        if (s.ServiceName.Equals(LegacyServiceName, StringComparison.OrdinalIgnoreCase))
                        {
                            legacyExists = true;
                            legacyRunning = s.Status != ServiceControllerStatus.Stopped;
                        }
                        else if (s.ServiceName.Equals(ServiceName, StringComparison.OrdinalIgnoreCase))
                        {
                            ourExists = true;
                            ourRunning = s.Status == ServiceControllerStatus.Running;
                        }
                    }
                }
                int total = 0;
                foreach (var p in Process.GetProcessesByName("goodbyedpi")) { total++; p.Dispose(); }

                bool changed = legacyExists != LegacyServiceExists || legacyRunning != LegacyServiceRunning || ourExists != ServiceInstalled;
                LegacyServiceExists = legacyExists;
                LegacyServiceRunning = legacyRunning;
                ServiceInstalled = ourExists;

                if (Monitor.TryEnter(gate))
                {
                    try
                    {
                        int mine = (proc != null ? 1 : 0) + (App.Settings.ServiceMode && ourRunning ? 1 : 0);
                        int ext = Math.Max(0, total - mine);
                        if (ext != ExternalCount) { ExternalCount = ext; changed = true; }

                        if (App.Settings.ServiceMode && proc == null &&
                            State != EngineState.Starting && State != EngineState.Stopping)
                        {
                            if (ourRunning && State != EngineState.Running)
                            {
                                ActiveProfile = ActiveProfile ?? App.SelectedProfile;
                                StartedAt = StartedAt ?? DateTime.Now;
                                RunningAsService = true;
                                userStop = false;
                                SetState(EngineState.Running);
                            }
                            else if (!ourRunning && State == EngineState.Running && !userStop)
                            {
                                RunningAsService = false;
                                Fail("Arka plan hizmeti durdu.", "DK-702");
                            }
                        }
                        if (proc != null) ReadConsoleIntoLog(proc);
                    }
                    finally { Monitor.Exit(gate); }
                }
                if (changed)
                {
                    if (ExternalCount > 0) App.Problems.Report("DK-301", ExternalCount + " işlem"); else App.Problems.Clear("DK-301");
                    if (LegacyServiceExists) App.Problems.Report("DK-302"); else App.Problems.Clear("DK-302");
                    App.RaiseStatus();
                }
            }
            catch (Exception ex) { App.Ignored("Durum denetimi", ex); }
            finally { Interlocked.Exchange(ref polling, 0); }
        }

        // ------------------------------------------------------------------ yardımcılar

        public static bool ServiceExists(string name)
        {
            try
            {
                foreach (var s in ServiceController.GetServices())
                    using (s)
                        if (s.ServiceName.Equals(name, StringComparison.OrdinalIgnoreCase)) return true;
            }
            catch (Exception ign) { App.Ignored("Hizmet listesi okunamadı", ign); }
            return false;
        }

        static int ServicePid(string name)
        {
            try
            {
                using (var q = new ManagementObjectSearcher("SELECT ProcessId FROM Win32_Service WHERE Name='" + name + "'"))
                    foreach (ManagementObject mo in q.Get())
                        using (mo) return Convert.ToInt32(mo["ProcessId"]);
            }
            catch (Exception ign) { App.Ignored("Hizmet işlem numarası okunamadı", ign); }
            return -1;
        }

        public static (int Code, string Output) Sc(string args) => Sys.Run(Sys.SystemExe("sc.exe"), args, 30000);

        /// <summary>Hizmetin komut satırındaki exe yolu (tırnaklı ya da tırnaksız).</summary>
        public static string ServiceExe(string name)
        {
            string cmd = ServicePath(name);
            if (string.IsNullOrWhiteSpace(cmd)) return null;
            cmd = cmd.Trim();
            if (cmd.StartsWith("\"")) { int e = cmd.IndexOf('"', 1); return e > 1 ? cmd.Substring(1, e - 1) : null; }
            int x = cmd.IndexOf(".exe", StringComparison.OrdinalIgnoreCase);
            return x > 0 ? cmd.Substring(0, x + 4) : cmd;
        }

        /// <summary>
        /// Kaldırma için: yalnızca DPI Kalkan'a ait olanları temizler (kendi hizmeti, bu klasördeki goodbyedpi işlemleri,
        /// WinDivert sürücü kaydı). Başka programların GoodbyeDPI hizmetine dokunmaz.
        /// </summary>
        public void RemoveForUninstall(StringBuilder report)
        {
            lock (gate)
            {
                userStop = true;
                StopInternal();
                string own = BinDir.TrimEnd('\\') + "\\";
                foreach (var p in Process.GetProcessesByName("goodbyedpi"))
                    using (p)
                    {
                        try
                        {
                            if (p.MainModule.FileName.StartsWith(own, StringComparison.OrdinalIgnoreCase)) { p.Kill(); p.WaitForExit(3000); }
                        }
                        catch (Exception ign) { App.Ignored("goodbyedpi kapatılamadı", ign); }
                    }
                if (ServiceExists(ServiceName))
                {
                    Sc("stop " + ServiceName);
                    var r = Sc("delete " + ServiceName);
                    report.AppendLine(r.Code == 0 ? "• Arka plan hizmeti (DPIKalkan) kaldırıldı." : "• Arka plan hizmeti kaldırılamadı: " + r.Output);
                }
                var others = Process.GetProcessesByName("goodbyedpi");
                bool otherDpi = others.Length > 0;
                foreach (var o in others) o.Dispose();
                foreach (var n in new[] { "WinDivert", "WinDivert14" })
                {
                    if (!ServiceExists(n)) continue;
                    if (otherDpi) { report.AppendLine("• " + n + " sürücü kaydı başka bir GoodbyeDPI çalıştığı için bırakıldı."); continue; }
                    Sc("stop " + n);
                    var r = Sc("delete " + n);
                    if (r.Code == 0) report.AppendLine("• " + n + " sürücü kaydı kaldırıldı.");
                }
                StartedAt = null;
                ServiceInstalled = false;
                RunningAsService = false;
                SetState(EngineState.Stopped);
            }
        }

        /// <summary>CommandLineToArgvW kurallarına göre tek bir argüman olarak tırnaklar.</summary>
        public static string Esc(string arg)
        {
            var sb = new StringBuilder("\"");
            int bs = 0;
            foreach (char c in arg)
            {
                if (c == '\\') { bs++; continue; }
                if (c == '"') sb.Append('\\', bs * 2 + 1);
                else sb.Append('\\', bs);
                sb.Append(c);
                bs = 0;
            }
            sb.Append('\\', bs * 2);
            sb.Append('"');
            return sb.ToString();
        }
    }
}
