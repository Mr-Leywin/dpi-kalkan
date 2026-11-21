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
    /// <summary>Windows'un kendi komut satırı araçlarını (netsh, sc) System32'den, pencere açmadan çalıştırır.</summary>
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

        /// <summary>
        /// Programı pencere açmadan çalıştırır; çıktıyı eşzamansız okur (tampon dolup kilitlenmez) ve süre dolarsa
        /// işlemi sonlandırır. Dönüş: (çıkış kodu, çıktı); -1 başlatılamadı, -2 zaman aşımı.
        /// </summary>
        public static (int Code, string Output) Run(string file, string args, int timeoutMs = 20000)
        {
            try
            {
                var psi = new ProcessStartInfo(file, args)
                {
                    UseShellExecute = false, CreateNoWindow = true,
                    RedirectStandardOutput = true, RedirectStandardError = true,
                    StandardOutputEncoding = Oem, StandardErrorEncoding = Oem,
                    WorkingDirectory = Environment.SystemDirectory,
                };
                using (var p = Process.Start(psi))
                {
                    var o = p.StandardOutput.ReadToEndAsync();
                    var e = p.StandardError.ReadToEndAsync();
                    if (!p.WaitForExit(timeoutMs))
                    {
                        try { p.Kill(); } catch { }
                        try { p.WaitForExit(3000); } catch { }
                        App.Log(Path.GetFileName(file) + " " + args + " → " + (timeoutMs / 1000) + " sn içinde bitmedi, sonlandırıldı.", LogLevel.Warn);
                        return (-2, "zaman aşımı");
                    }
                    // akışlar alt işlemler tarafından açık tutulursa en fazla 5 sn bekle
                    string text = System.Threading.Tasks.Task.WaitAll(new System.Threading.Tasks.Task[] { o, e }, 5000) ? o.Result + e.Result
                                : (o.IsCompleted ? o.Result : "") + (e.IsCompleted ? e.Result : "");
                    App.Log(Path.GetFileName(file) + " " + args + " → " + p.ExitCode, LogLevel.Debug);
                    return (p.ExitCode, text.Trim());
                }
            }
            catch (Exception ex) { return (-1, ex.Message); }
        }

        public static string SystemExe(string name) => Path.Combine(Environment.SystemDirectory, name);

        /// <summary>Yükseltilmiş süreçten, programı normal kullanıcı yetkisiyle açar (explorer üzerinden).</summary>
        public static bool OpenAsUser(string path)
        {
            try
            {
                Process.Start(new ProcessStartInfo(Explorer, "\"" + path + "\"") { UseShellExecute = false });
                return true;
            }
            catch { return false; }
        }

        public static void OpenUri(string uri)
        {
            try { Process.Start(new ProcessStartInfo(uri) { UseShellExecute = true }); } catch (Exception ign) { App.Ignored("Adres açılamadı", ign); }
        }

        static string Explorer => Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.Windows), "explorer.exe");

        public static void OpenFolder(string dir)
        {
            try { Process.Start(new ProcessStartInfo(Explorer, "\"" + dir + "\"") { UseShellExecute = false }); } catch (Exception ign) { App.Ignored("Klasör açılamadı", ign); }
        }

        public static void SelectFile(string file)
        {
            try { Process.Start(new ProcessStartInfo(Explorer, "/select,\"" + file + "\"") { UseShellExecute = false }); } catch (Exception ign) { App.Ignored("Dosya gösterilemedi", ign); }
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
                    try { if (!p.HasExited) p.Kill(); } catch (Exception ign) { App.Ignored("Discord kapatılamadı", ign); }
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

        /// <summary>Windows 11 (derleme 22000+) şifreli DNS'i (DoH) ve 'netsh dns ... encryption' komutlarını destekler.</summary>
        public static bool DohSupported => DohSupportedOn(WindowsBuild);
        public static bool DohSupportedOn(int build) => build >= 22000;

        public static string DohUnsupportedMessage(int build) =>
            "Şifreli DNS (DoH) bu Windows sürümünde desteklenmiyor (derleme " + build + "; Windows 11 gerekir). " +
            "DNS sunucusu şifresiz olarak ayarlandı; Discord erişimi için bu da yeterlidir.";

        /// <summary>netsh komut satırına girebilecek güvenli bir DoH şablonu mu (https, boşluksuz, komut karakteri yok).</summary>
        internal static bool SafeTemplate(string t) =>
            !string.IsNullOrEmpty(t) && Uri.TryCreate(t, UriKind.Absolute, out var u) && u.Scheme == "https" && CmdLine.BareSafe(t) && !t.Contains(" ") && !t.Contains("\"");

        /// <summary>
        /// Bir DNS seçeneği için netsh komutları. Windows 11'de bilinen sunucular (1.1.1.1, 8.8.8.8, 9.9.9.9…) zaten kayıtlıdır
        /// (Auto-upgrade: no): kayıt varsa 'set', yoksa 'add' kullanılır (bu makinede canlı denendi: ikisi de çıkış kodu 0 ile
        /// Auto-upgrade'i yes yapar; 'delete' kaydı siler). Sonuç her zaman 'show encryption' ile okunup doğrulanır.
        /// </summary>
        internal static List<(string Ip, string Add, string Set)> DohCommands(SystemDnsChoice c)
        {
            var list = new List<(string, string, string)>();
            if (!SafeTemplate(c.Doh)) return list;
            foreach (var ip in new[] { c.V4a, c.V4b, c.V6a, c.V6b })
            {
                if (string.IsNullOrEmpty(ip) || !IPAddress.TryParse(ip, out _)) continue;
                string tail = " dohtemplate=" + c.Doh + " autoupgrade=yes udpfallback=no";
                list.Add((ip, "dns add encryption server=" + ip + tail, "dns set encryption server=" + ip + tail));
            }
            return list;
        }

        /// <summary>'netsh dns show encryption server=…' çıktısından okunan kayıt.</summary>
        internal sealed class DohEntry
        {
            public bool Exists;
            public string Template = "";
            public bool? AutoUpgrade, UdpFallback;
        }

        static bool? YesNo(string v)
        {
            v = (v ?? "").Trim().ToLowerInvariant();
            if (v == "yes" || v == "evet" || v == "enabled" || v == "etkin" || v == "açık") return true;
            if (v == "no" || v == "hayır" || v == "hayir" || v == "disabled" || v == "devre dışı" || v == "kapalı") return false;
            return null;
        }

        /// <summary>Tek bir sunucunun şifreleme kaydını ayrıştırır (kayıt yoksa netsh boş çıktı verir).</summary>
        internal static DohEntry ParseEncryption(string output)
        {
            var e = new DohEntry();
            foreach (var raw in (output ?? "").Split('\n'))
            {
                var m = System.Text.RegularExpressions.Regex.Match(raw.Trim(), @"^(.+?)\s+:\s*(.*)$");
                if (!m.Success) continue;
                string key = m.Groups[1].Value.ToLowerInvariant(), val = m.Groups[2].Value.Trim();
                e.Exists = true;
                if (val.StartsWith("https://", StringComparison.OrdinalIgnoreCase)) e.Template = val;
                else if (key.Contains("upgrade") || key.Contains("yükselt")) e.AutoUpgrade = YesNo(val);
                else if (key.Contains("fallback") || key.Contains("geri")) e.UdpFallback = YesNo(val);
            }
            return e;
        }

        static DohEntry QueryDoh(string netsh, string ip) => ParseEncryption(Sys.Run(netsh, "dns show encryption server=" + ip).Output);

        /// <summary>'netsh dns show global' çıktısındaki DoH değeri (enabled/disabled/auto; okunamazsa null).</summary>
        internal static string ParseGlobalDoh(string output)
        {
            foreach (var raw in (output ?? "").Split('\n'))
            {
                var m = System.Text.RegularExpressions.Regex.Match(raw.Trim(), @"^DoH[^:]*:\s*(\S+)", System.Text.RegularExpressions.RegexOptions.IgnoreCase);
                if (m.Success) return m.Groups[1].Value.ToLowerInvariant();
            }
            return null;
        }

        static string Short(string s)
        {
            s = (s ?? "").Replace("\r", " ").Replace("\n", " ").Trim();
            return s.Length > 160 ? s.Substring(0, 160) + "…" : s;
        }

        static string Fmt(bool? b) => b == null ? "okunamadı" : b.Value ? "yes" : "no";

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
                catch (Exception ign) { App.Ignored("Windows derleme numarası okunamadı", ign); }
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
            if (string.IsNullOrWhiteSpace(adapter) || adapter.IndexOf('"') >= 0 || CmdLine.HasControlChars(adapter))
            {
                log.AppendLine(fam + " → geçersiz bağdaştırıcı adı atlandı.");
                return false;
            }
            if (servers != null && servers.Count > 0)
            {
                // ayar dosyasından gelen adresler netsh'e verilmeden önce doğrulanır
                var family = v6 ? System.Net.Sockets.AddressFamily.InterNetworkV6 : System.Net.Sockets.AddressFamily.InterNetwork;
                var valid = servers.Where(s => s != null && s.IndexOf('%') < 0 && IPAddress.TryParse(s, out var ip) && ip.AddressFamily == family).ToList();
                if (valid.Count != servers.Count) log.AppendLine(fam + " → " + (servers.Count - valid.Count) + " geçersiz adres atlandı.");
                if (valid.Count == 0) return false;
                servers = valid;
            }
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
            if (!c.IsAuto && doh)
            {
                if (DohSupported) allOk &= EnableDoh(c, log);
                else
                {
                    log.AppendLine(DohUnsupportedMessage(WindowsBuild));
                    App.Log(DohUnsupportedMessage(WindowsBuild), LogLevel.Warn);
                }
            }
            else if (c.IsAuto && DohSupported) allOk &= RestoreDoh(log);   // otomatiğe dönünce şifreli DNS kayıtları da eski hâline
            Native.FlushDns();
            report = log.ToString().Trim();
            App.Log("Windows DNS ayarı: " + c.Name + (doh ? " (şifreli)" : "") + (allOk ? " uygulandı." : " – bazı adımlar başarısız."), allOk ? LogLevel.Success : LogLevel.Warn);
            return allOk;
        }

        /// <summary>
        /// Şifreli DNS'i Windows'un kendi aracıyla (netsh dns ... encryption) açar; başka bir betik yorumlayıcısı kullanılmaz.
        /// Yalnızca Windows 11'de çağrılır (Apply denetler).
        /// </summary>
        static bool EnableDoh(SystemDnsChoice c, StringBuilder log)
        {
            string netsh = Sys.SystemExe("netsh.exe");
            // DoH'a genel olarak izin ver (Windows 11'de varsayılan "enabled"; kapalıysa açılır ve eski değer yedeklenir)
            string global = ParseGlobalDoh(Sys.Run(netsh, "dns show global").Output);
            if (global != null && !global.StartsWith("enab"))
            {
                if (App.Settings.GlobalDohBackup == null) App.Settings.GlobalDohBackup = global;
                var g = Sys.Run(netsh, "dns set global doh=yes");
                if (g.Code != 0) g = Sys.Run(netsh, "dns add global doh=yes");
                if (g.Code != 0) { log.AppendLine("Şifreli DNS genel ayarı açılamadı: " + Short(g.Output)); App.Log("netsh dns global doh=yes: " + g.Output, LogLevel.Warn); }
            }
            var cmds = DohCommands(c);
            if (cmds.Count == 0)
            {
                log.AppendLine("Şifreli DNS (DoH) → bu seçenek için geçerli bir DoH adresi yok.");
                return false;
            }
            var done = new List<string>();
            var failed = new List<string>();
            foreach (var cmd in cmds)
            {
                var before = QueryDoh(netsh, cmd.Ip);
                if (!App.Settings.DohBackups.Any(b => b.Server == cmd.Ip))
                    App.Settings.DohBackups.Add(new DohBackup
                    {
                        Server = cmd.Ip, Existed = before.Exists, Template = before.Template,
                        AutoUpgrade = before.AutoUpgrade ?? false, UdpFallback = before.UdpFallback ?? false,
                    });
                var r = Sys.Run(netsh, before.Exists ? cmd.Set : cmd.Add);
                // netsh'in çıkış koduna güvenmek yerine sonuç her sunucu için geri okunur
                var after = QueryDoh(netsh, cmd.Ip);
                bool good = after.Exists && after.AutoUpgrade == true && after.UdpFallback == false &&
                            string.Equals(after.Template, c.Doh, StringComparison.OrdinalIgnoreCase);
                if (good) { done.Add(cmd.Ip); continue; }
                string why = r.Code != 0 ? "netsh hata verdi (" + r.Code + "): " + Short(r.Output)
                                         : "komut çalıştı ama doğrulanamadı (Auto-upgrade: " + Fmt(after.AutoUpgrade) + ", şablon: " + (after.Template.Length > 0 ? after.Template : "yok") + ")";
                failed.Add(cmd.Ip + " → " + why);
                App.Log("[DoH] " + cmd.Ip + ": " + why, LogLevel.Warn);
            }
            App.Save();
            if (failed.Count == 0)
                log.AppendLine("Şifreli DNS (DoH) → etkin ve doğrulandı: " + string.Join(", ", done) + " (Auto-upgrade: yes).");
            else
            {
                log.AppendLine("Şifreli DNS (DoH) → " + done.Count + "/" + cmds.Count + " sunucuda etkinleştirilebildi:");
                foreach (var f in failed) log.AppendLine("  " + f);
            }
            App.Log("Şifreli DNS: " + done.Count + "/" + cmds.Count + " sunucu doğrulandı (" + c.Name + ").", failed.Count == 0 ? LogLevel.Success : LogLevel.Warn);
            return failed.Count == 0;
        }

        /// <summary>
        /// DPI Kalkan'ın değiştirdiği şifreli DNS kayıtlarını eski hâline döndürür: önceden var olan kayıt eski şablon ve
        /// Auto-upgrade/UDP-fallback değerleriyle geri yazılır, DPI Kalkan'ın eklediği kayıt silinir. Sonuç geri okunarak doğrulanır.
        /// </summary>
        public static bool RestoreDoh(StringBuilder log)
        {
            var s = App.Settings;
            if (s.DohBackups.Count == 0 && s.GlobalDohBackup == null) return true;
            string netsh = Sys.SystemExe("netsh.exe");
            bool ok = true;
            foreach (var b in s.DohBackups.ToList())
            {
                if (!IPAddress.TryParse(b.Server ?? "", out _)) { s.DohBackups.Remove(b); continue; }
                (int Code, string Output) r;
                if (!b.Existed) r = Sys.Run(netsh, "dns delete encryption server=" + b.Server);
                else
                    r = Sys.Run(netsh, "dns set encryption server=" + b.Server + (SafeTemplate(b.Template) ? " dohtemplate=" + b.Template : "") +
                                       " autoupgrade=" + (b.AutoUpgrade ? "yes" : "no") + " udpfallback=" + (b.UdpFallback ? "yes" : "no"));
                var after = QueryDoh(netsh, b.Server);
                bool good = b.Existed ? after.Exists && after.AutoUpgrade == b.AutoUpgrade : !after.Exists;
                if (good) { s.DohBackups.Remove(b); continue; }
                ok = false;
                string why = b.Server + " → " + (r.Code != 0 ? "netsh hata verdi: " + Short(r.Output) : "geri alındı ama doğrulanamadı (Auto-upgrade: " + Fmt(after.AutoUpgrade) + ")");
                log.AppendLine("Şifreli DNS geri alınamadı: " + why);
                App.Log("[DoH] geri alma: " + why, LogLevel.Warn);
            }
            if (s.GlobalDohBackup != null)
            {
                string v = s.GlobalDohBackup.StartsWith("dis") ? "no" : s.GlobalDohBackup.StartsWith("auto") ? "auto" : "yes";
                var g = Sys.Run(netsh, "dns set global doh=" + v);
                if (g.Code == 0) s.GlobalDohBackup = null;
                else { ok = false; log.AppendLine("Şifreli DNS genel ayarı geri alınamadı: " + Short(g.Output)); }
            }
            App.Save();
            if (ok) log.AppendLine("Şifreli DNS (DoH) kayıtları eski hâline döndürüldü.");
            return ok;
        }

        /// <param name="onlyBackups">true: yalnızca DPI Kalkan'ın değiştirdiği (yedeği olan) bağdaştırıcılar geri alınır (kaldırma için).</param>
        public static bool Restore(out string report, bool onlyBackups = false)
        {
            var log = new StringBuilder();
            bool ok = true;
            var adapters = onlyBackups ? new List<AdapterInfo>() : TargetAdapters();
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
            ok &= RestoreDoh(log);   // şifreli DNS kayıtları da DPI Kalkan'dan önceki hâline
            App.Save();
            Native.FlushDns();
            report = log.ToString().Trim();
            App.Log("Windows DNS ayarları eski haline döndürüldü.", ok ? LogLevel.Success : LogLevel.Warn);
            return ok;
        }
    }
}
