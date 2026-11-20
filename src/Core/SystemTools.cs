using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Net;
using System.Text;
using System.Threading;
using Microsoft.Win32;

namespace DPIKalkan
{
    /// <summary>Komut satırı araçlarını (netsh, sc, powershell) pencere açmadan çalıştırır.</summary>
    public static class Sys
    {
        static Encoding Oem
        {
            get
            {
                try { return Encoding.GetEncoding(CultureInfo.CurrentCulture.TextInfo.OEMCodePage); }
                catch { return Encoding.Default; }
            }
        }

        public static (int Code, string Output) Run(string file, string args, int timeoutMs = 20000)
        {
            try
            {
                var psi = new ProcessStartInfo(file, args)
                {
                    UseShellExecute = false, CreateNoWindow = true,
                    RedirectStandardOutput = true, RedirectStandardError = true,
                    StandardOutputEncoding = Oem, StandardErrorEncoding = Oem,
                };
                using (var p = Process.Start(psi))
                {
                    var err = p.StandardError.ReadToEndAsync();
                    string o = p.StandardOutput.ReadToEnd();
                    if (!p.WaitForExit(timeoutMs)) { try { p.Kill(); } catch { } return (-2, "zaman aşımı"); }
                    o += err.Result;
                    App.Log(Path.GetFileName(file) + " " + args + " → " + p.ExitCode, LogLevel.Debug);
                    return (p.ExitCode, o.Trim());
                }
            }
            catch (Exception ex) { return (-1, ex.Message); }
        }

        public static string SystemExe(string name) => Path.Combine(Environment.SystemDirectory, name);

        public static (int Code, string Output) PowerShell(string script, int timeoutMs = 30000) =>
            Run(Path.Combine(Environment.SystemDirectory, @"WindowsPowerShell\v1.0\powershell.exe"),
                "-NoProfile -NonInteractive -ExecutionPolicy Bypass -Command \"" + script.Replace("\"", "\\\"") + "\"", timeoutMs);

        /// <summary>Yükseltilmiş süreçten, programı normal kullanıcı yetkisiyle açar (explorer üzerinden).</summary>
        public static bool OpenAsUser(string path)
        {
            try
            {
                Process.Start(new ProcessStartInfo(Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.Windows), "explorer.exe"), "\"" + path + "\"")
                { UseShellExecute = false });
                return true;
            }
            catch { return false; }
        }

        public static void OpenUri(string uri)
        {
            try { Process.Start(new ProcessStartInfo(uri) { UseShellExecute = true }); } catch { }
        }

        public static void OpenFolder(string dir)
        {
            try { Process.Start("explorer.exe", "\"" + dir + "\""); } catch { }
        }

        public static void SelectFile(string file)
        {
            try { Process.Start("explorer.exe", "/select,\"" + file + "\""); } catch { }
        }
    }

    // ==================================================================== Discord

    public static class DiscordHelper
    {
        static readonly string[] Names = { "Discord", "DiscordPTB", "DiscordCanary" };

        public static List<Process> Processes() => Names.SelectMany(n => Process.GetProcessesByName(n)).ToList();

        public static bool IsRunning
        {
            get
            {
                var l = Processes();
                bool r = l.Count > 0;
                foreach (var p in l) p.Dispose();
                return r;
            }
        }

        public static DateTime? EarliestStart()
        {
            DateTime? min = null;
            foreach (var p in Processes())
                using (p)
                {
                    try { var t = p.StartTime; if (min == null || t < min) min = t; } catch { }
                }
            return min;
        }

        /// <summary>Discord, verilen zamandan önce mi açılmış?</summary>
        public static bool StartedBefore(DateTime t)
        {
            var s = EarliestStart();
            return s.HasValue && s.Value < t.AddSeconds(-3);
        }

        static string ShortcutFor(string name)
        {
            string prog = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.Programs), "Discord Inc");
            string file = name == "DiscordPTB" ? "Discord PTB.lnk" : name == "DiscordCanary" ? "Discord Canary.lnk" : "Discord.lnk";
            foreach (var f in new[] { Path.Combine(prog, file), Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.DesktopDirectory), file) })
                if (File.Exists(f)) return f;
            return null;
        }

        /// <summary>Discord'u tamamen kapatıp normal kullanıcı yetkisiyle yeniden açar.</summary>
        public static bool Restart(out string message)
        {
            string flavor = "Discord", exe = null;
            var procs = Processes();
            foreach (var p in procs)
            {
                try { if (exe == null) { exe = p.MainModule.FileName; flavor = p.ProcessName; } } catch { }
            }
            foreach (var p in procs)
                using (p)
                {
                    try { if (!p.HasExited) p.Kill(); } catch { }
                }
            foreach (var p in Processes()) using (p) { try { p.WaitForExit(4000); } catch { } }
            Thread.Sleep(800);

            string launch = ShortcutFor(flavor);
            if (launch == null)
            {
                string upd = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), flavor, "Update.exe");
                if (exe != null && File.Exists(exe)) launch = exe;
                else if (File.Exists(upd))
                {
                    // en yeni app-* klasöründeki Discord.exe
                    var dir = Directory.GetDirectories(Path.GetDirectoryName(upd), "app-*").OrderByDescending(d => d).FirstOrDefault();
                    if (dir != null && File.Exists(Path.Combine(dir, flavor + ".exe"))) launch = Path.Combine(dir, flavor + ".exe");
                }
            }
            if (launch == null)
            {
                message = procs.Count > 0 ? "Discord kapatıldı ama başlatma kısayolu bulunamadı; Discord'u kendiniz açın." : "Discord kurulu görünmüyor.";
                App.Log(message, LogLevel.Warn);
                return procs.Count > 0;
            }
            bool ok = Sys.OpenAsUser(launch);
            message = ok ? (procs.Count > 0 ? "Discord yeniden başlatıldı." : "Discord başlatıldı.") : "Discord başlatılamadı.";
            App.Log(message, ok ? LogLevel.Success : LogLevel.Warn);
            if (ok) App.Problems.Clear("DK-901");
            return ok;
        }
    }

    // ==================================================================== Windows DNS ayarı

    public sealed class SystemDnsChoice
    {
        public string Id, Name, V4a, V4b, V6a, V6b, Doh, Note;
        public bool IsAuto => Id == "auto";
    }

    public static class SystemDns
    {
        public static readonly List<SystemDnsChoice> Choices = new List<SystemDnsChoice>
        {
            new SystemDnsChoice { Id = "auto", Name = "Otomatik (servis sağlayıcının DNS'i)", Note = "Windows varsayılanı. Türkiye'de Discord bu DNS'te engelli." },
            new SystemDnsChoice { Id = "cloudflare", Name = "Cloudflare", V4a = "1.1.1.1", V4b = "1.0.0.1", V6a = "2606:4700:4700::1111", V6b = "2606:4700:4700::1001",
                Doh = "https://cloudflare-dns.com/dns-query", Note = "En hızlı genel DNS. Şifreli DNS (DoH) ile önerilir." },
            new SystemDnsChoice { Id = "google", Name = "Google", V4a = "8.8.8.8", V4b = "8.8.4.4", V6a = "2001:4860:4860::8888", V6b = "2001:4860:4860::8844",
                Doh = "https://dns.google/dns-query", Note = "Yaygın ve kararlı. Şifreli DNS ile kullanın." },
            new SystemDnsChoice { Id = "quad9", Name = "Quad9", V4a = "9.9.9.9", V4b = "149.112.112.112", V6a = "2620:fe::fe", V6b = "2620:fe::9",
                Doh = "https://dns.quad9.net/dns-query", Note = "Gizlilik odaklı, zararlı siteleri engeller." },
            new SystemDnsChoice { Id = "adguard", Name = "AdGuard", V4a = "94.140.14.14", V4b = "94.140.15.15", V6a = "2a10:50c0::ad1:ff", V6b = "2a10:50c0::ad2:ff",
                Doh = "https://dns.adguard-dns.com/dns-query", Note = "Reklam ve izleyicileri de engeller." },
            new SystemDnsChoice { Id = "opendns", Name = "OpenDNS", V4a = "208.67.222.222", V4b = "208.67.220.220", V6a = "2620:119:35::35", V6b = "2620:119:53::53",
                Doh = "https://doh.opendns.com/dns-query", Note = "Cisco'nun DNS hizmeti." },
        };

        /// <summary>Windows 11 (derleme 22000+) şifreli DNS'i (DoH) destekler.</summary>
        public static bool DohSupported => WindowsBuild >= 22000;

        /// <summary>Gerçek Windows derleme numarası (uyumluluk katmanından etkilenmez).</summary>
        public static int WindowsBuild
        {
            get
            {
                try
                {
                    using (var k = Registry.LocalMachine.OpenSubKey(@"SOFTWARE\Microsoft\Windows NT\CurrentVersion"))
                        if (int.TryParse(k?.GetValue("CurrentBuildNumber") as string, out int b)) return b;
                }
                catch { }
                return Environment.OSVersion.Version.Build;
            }
        }

        public static List<AdapterInfo> TargetAdapters() =>
            NetTest.Adapters().Where(a => !a.IsVpn && (a.Gateways.Count > 0 || a.V4.Count > 0)).ToList();

        static string Reg(bool v6) => v6 ? @"SYSTEM\CurrentControlSet\Services\Tcpip6\Parameters\Interfaces\" : @"SYSTEM\CurrentControlSet\Services\Tcpip\Parameters\Interfaces\";

        /// <summary>Bağdaştırıcıda elle girilmiş DNS sunucuları (boşsa otomatik/DHCP).</summary>
        public static List<string> StaticServers(string adapterId, bool v6)
        {
            try
            {
                using (var k = Registry.LocalMachine.OpenSubKey(Reg(v6) + adapterId))
                {
                    string s = k?.GetValue("NameServer", "") as string ?? "";
                    return s.Split(new[] { ',', ' ' }, StringSplitOptions.RemoveEmptyEntries).ToList();
                }
            }
            catch { return new List<string>(); }
        }

        /// <summary>Şu anki ayarın hangi seçeneğe karşılık geldiği ("auto", sağlayıcı kimliği ya da "custom").</summary>
        public static string CurrentChoiceId(AdapterInfo a)
        {
            var v4 = StaticServers(a.Id, false);
            if (v4.Count == 0) return "auto";
            var c = Choices.FirstOrDefault(x => !x.IsAuto && v4[0] == x.V4a);
            return c?.Id ?? "custom";
        }

        static void Backup(AdapterInfo a)
        {
            var list = App.Settings.DnsBackups;
            if (list.Any(b => b.Adapter == a.Name)) return;
            var v4 = StaticServers(a.Id, false);
            var v6 = StaticServers(a.Id, true);
            list.Add(new DnsBackup { Adapter = a.Name, V4Dhcp = v4.Count == 0, V4 = v4, V6Dhcp = v6.Count == 0, V6 = v6 });
            App.Save();
        }

        static string Q(string s) => "\"" + s + "\"";

        static bool SetServers(string adapter, bool v6, IList<string> servers, StringBuilder log)
        {
            string fam = v6 ? "ipv6" : "ipv4";
            string netsh = Sys.SystemExe("netsh.exe");
            if (servers == null || servers.Count == 0)
            {
                var r = Sys.Run(netsh, "interface " + fam + " set dnsservers name=" + Q(adapter) + " source=dhcp");
                log.AppendLine(fam + " → otomatik: " + (r.Code == 0 ? "tamam" : r.Output));
                return r.Code == 0;
            }
            var r1 = Sys.Run(netsh, "interface " + fam + " set dnsservers name=" + Q(adapter) + " source=static address=" + servers[0] + " register=primary validate=no");
            bool ok = r1.Code == 0;
            for (int i = 1; i < servers.Count && ok; i++)
                Sys.Run(netsh, "interface " + fam + " add dnsservers name=" + Q(adapter) + " address=" + servers[i] + " index=" + (i + 1) + " validate=no");
            log.AppendLine(fam + " → " + string.Join(", ", servers) + ": " + (ok ? "tamam" : r1.Output));
            return ok;
        }

        public static bool Apply(SystemDnsChoice c, bool doh, IEnumerable<AdapterInfo> adapters, out string report)
        {
            var log = new StringBuilder();
            bool allOk = true;
            foreach (var a in adapters)
            {
                Backup(a);
                log.AppendLine("■ " + a.Name);
                if (c.IsAuto)
                {
                    allOk &= SetServers(a.Name, false, null, log);
                    SetServers(a.Name, true, null, log);
                }
                else
                {
                    allOk &= SetServers(a.Name, false, new[] { c.V4a, c.V4b }, log);
                    if (a.V6.Count > 0 || a.HasGlobalV6) SetServers(a.Name, true, new[] { c.V6a, c.V6b }, log);
                }
            }
            if (!c.IsAuto && doh && DohSupported) allOk &= EnableDoh(c, log);
            Native.FlushDns();
            report = log.ToString().Trim();
            App.Log("Windows DNS ayarı: " + c.Name + (doh ? " (şifreli)" : "") + (allOk ? " uygulandı." : " – bazı adımlar başarısız."), allOk ? LogLevel.Success : LogLevel.Warn);
            return allOk;
        }

        static bool EnableDoh(SystemDnsChoice c, StringBuilder log)
        {
            var sb = new StringBuilder("$ErrorActionPreference='Stop';");
            foreach (var ip in new[] { c.V4a, c.V4b, c.V6a, c.V6b })
            {
                sb.Append("if (Get-DnsClientDohServerAddress -ServerAddress '" + ip + "' -ErrorAction SilentlyContinue) { ")
                  .Append("Set-DnsClientDohServerAddress -ServerAddress '" + ip + "' -DohTemplate '" + c.Doh + "' -AllowFallbackToUdp $false -AutoUpgrade $true } else { ")
                  .Append("Add-DnsClientDohServerAddress -ServerAddress '" + ip + "' -DohTemplate '" + c.Doh + "' -AllowFallbackToUdp $false -AutoUpgrade $true };");
            }
            var r = Sys.PowerShell(sb.ToString());
            try
            {
                using (var k = Registry.LocalMachine.CreateSubKey(@"SYSTEM\CurrentControlSet\Services\Dnscache\Parameters"))
                    k?.SetValue("EnableAutoDoh", 2, RegistryValueKind.DWord);
            }
            catch { }
            log.AppendLine("Şifreli DNS (DoH) → " + (r.Code == 0 ? "etkin" : "etkinleştirilemedi: " + r.Output));
            return r.Code == 0;
        }

        public static bool Restore(out string report)
        {
            var log = new StringBuilder();
            bool ok = true;
            var adapters = TargetAdapters();
            foreach (var b in App.Settings.DnsBackups.ToList())
            {
                log.AppendLine("■ " + b.Adapter);
                ok &= SetServers(b.Adapter, false, b.V4Dhcp ? null : b.V4, log);
                SetServers(b.Adapter, true, b.V6Dhcp ? null : b.V6, log);
            }
            foreach (var a in adapters.Where(a => !App.Settings.DnsBackups.Any(b => b.Adapter == a.Name)))
            {
                log.AppendLine("■ " + a.Name);
                ok &= SetServers(a.Name, false, null, log);
                SetServers(a.Name, true, null, log);
            }
            App.Settings.DnsBackups.Clear();
            App.Save();
            Native.FlushDns();
            report = log.ToString().Trim();
            App.Log("Windows DNS ayarları eski haline döndürüldü.", ok ? LogLevel.Success : LogLevel.Warn);
            return ok;
        }
    }
}
