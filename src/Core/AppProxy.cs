using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Reflection;
using System.Runtime.InteropServices;
using System.Text;
using System.Threading;

namespace DPIKalkan
{
    public enum AppProxyMode { Chromium, Discord, Firefox, Env }

    /// <summary>Yalnızca belirli bir programın proxy kullanması için kural.</summary>
    public class ProxyApp
    {
        public string Id { get; set; } = Guid.NewGuid().ToString("N");
        public string Name { get; set; } = "";
        public string ExePath { get; set; } = "";
        public string ExtraArgs { get; set; } = "";
        public string ProxyId { get; set; } = "";
        public AppProxyMode Mode { get; set; } = AppProxyMode.Env;
        /// <summary>Tarayıcılar için ayrı profil (tarayıcı açıkken de proxy'li pencere açılabilsin).</summary>
        public bool SeparateProfile { get; set; } = true;
        /// <summary>Açmadan önce programın açık kopyalarını kapat (tek kopya çalışan programlar için).</summary>
        public bool CloseRunning { get; set; } = true;

        public string ModeLabel
        {
            get
            {
                switch (Mode)
                {
                    case AppProxyMode.Discord: return "Discord (--proxy-server)";
                    case AppProxyMode.Chromium: return "Chromium/Electron (--proxy-server)";
                    case AppProxyMode.Firefox: return "Firefox (ayrı proxy profili)";
                    default: return "Ortam değişkenleri (HTTP_PROXY)";
                }
            }
        }
    }

    /// <summary>Seçili programı proxy ile başlatma: türe göre komut satırı, profil ya da ortam değişkenleri.</summary>
    public static class AppProxy
    {
        static string Local => Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData);
        static string Roaming => Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData);
        static string PF => Environment.GetFolderPath(Environment.SpecialFolder.ProgramFiles);
        static string PF86 => Environment.GetFolderPath(Environment.SpecialFolder.ProgramFilesX86);

        /// <summary>Programların ayrı proxy profilleri (kullanıcının yazabileceği bir yerde).</summary>
        public static string ProfileRoot => Path.Combine(Local, "DPI Kalkan", "profiller");

        /// <summary>Bilinen programların olası kurulum yerleri.</summary>
        public static List<(string Name, string Path)> Installed()
        {
            var c = new List<(string, string)>
            {
                ("Discord", Path.Combine(Local, "Discord", "Update.exe")),
                ("Discord PTB", Path.Combine(Local, "DiscordPTB", "Update.exe")),
                ("Google Chrome", Path.Combine(PF, @"Google\Chrome\Application\chrome.exe")),
                ("Google Chrome", Path.Combine(PF86, @"Google\Chrome\Application\chrome.exe")),
                ("Google Chrome", Path.Combine(Local, @"Google\Chrome\Application\chrome.exe")),
                ("Microsoft Edge", Path.Combine(PF86, @"Microsoft\Edge\Application\msedge.exe")),
                ("Microsoft Edge", Path.Combine(PF, @"Microsoft\Edge\Application\msedge.exe")),
                ("Brave", Path.Combine(PF, @"BraveSoftware\Brave-Browser\Application\brave.exe")),
                ("Brave", Path.Combine(Local, @"BraveSoftware\Brave-Browser\Application\brave.exe")),
                ("Opera", Path.Combine(Local, @"Programs\Opera\opera.exe")),
                ("Opera GX", Path.Combine(Local, @"Programs\Opera GX\opera.exe")),
                ("Vivaldi", Path.Combine(Local, @"Vivaldi\Application\vivaldi.exe")),
                ("Yandex Browser", Path.Combine(Local, @"Yandex\YandexBrowser\Application\browser.exe")),
                ("Mozilla Firefox", Path.Combine(PF, @"Mozilla Firefox\firefox.exe")),
                ("Mozilla Firefox", Path.Combine(PF86, @"Mozilla Firefox\firefox.exe")),
                ("Spotify", Path.Combine(Roaming, @"Spotify\Spotify.exe")),
                ("Slack", Path.Combine(Local, @"slack\slack.exe")),
                ("Visual Studio Code", Path.Combine(Local, @"Programs\Microsoft VS Code\Code.exe")),
            };
            return c.Where(x => File.Exists(x.Item2)).GroupBy(x => x.Item1).Select(g => g.First()).ToList();
        }

        /// <summary>Programın proxy'yi nasıl alacağını tahmin eder.</summary>
        public static AppProxyMode Detect(string exe)
        {
            string name = Path.GetFileName(exe ?? "").ToLowerInvariant();
            string dir = Path.GetDirectoryName(exe ?? "") ?? "";
            if (name == "update.exe" && dir.IndexOf("discord", StringComparison.OrdinalIgnoreCase) >= 0) return AppProxyMode.Discord;
            if (name.StartsWith("discord")) return AppProxyMode.Discord;
            if (name == "firefox.exe" || name == "librewolf.exe" || name == "waterfox.exe") return AppProxyMode.Firefox;
            string[] chromium = { "chrome.exe", "msedge.exe", "brave.exe", "opera.exe", "vivaldi.exe", "browser.exe", "spotify.exe", "slack.exe", "code.exe", "chromium.exe" };
            if (chromium.Contains(name)) return AppProxyMode.Chromium;
            // Electron/Chromium tabanlı programlar bu dosyaları yanlarında taşır
            try
            {
                if (File.Exists(Path.Combine(dir, "resources.pak")) || File.Exists(Path.Combine(dir, "chrome_100_percent.pak")) ||
                    File.Exists(Path.Combine(dir, "v8_context_snapshot.bin")) || File.Exists(Path.Combine(dir, "icudtl.dat")))
                    return AppProxyMode.Chromium;
                // sürüm klasörlü kurulumlar (ör. Opera, Edge: Application\<sürüm>\resources.pak)
                if (Directory.Exists(dir) && Directory.GetDirectories(dir).Any(d => File.Exists(Path.Combine(d, "resources.pak"))))
                    return AppProxyMode.Chromium;
            }
            catch { }
            return AppProxyMode.Env;
        }

        static bool IsBrowser(ProxyApp a)
        {
            string n = Path.GetFileName(a.ExePath).ToLowerInvariant();
            return a.Mode == AppProxyMode.Firefox || new[] { "chrome.exe", "msedge.exe", "brave.exe", "opera.exe", "vivaldi.exe", "browser.exe", "chromium.exe" }.Contains(n);
        }

        /// <summary>Bu kural yerel köprü gerektiriyor mu? (şifreli proxy ya da SOCKS5'i doğrudan desteklemeyen yöntem)</summary>
        public static bool NeedsBridge(ProxyApp a, ProxyEntry p) =>
            p.HasAuth || (a.Mode == AppProxyMode.Env && p.Kind == ProxyKind.Socks5);

        /// <summary>Programa verilecek proxy adresi.</summary>
        public static string ProxyUrl(ProxyApp a, ProxyEntry p) => NeedsBridge(a, p) ? ProxyBridge.LocalUrl(p) : p.Scheme + "://" + p.Address;

        static string ProfileDir(ProxyApp a) => Path.Combine(ProfileRoot, Scripts.Slug(a.Name.Length > 0 ? a.Name : Path.GetFileNameWithoutExtension(a.ExePath)) + "-" + a.Id.Substring(0, 6));

        /// <summary>Başlatma planı: hedef dosya, parametreler, çalışma klasörü, pencere durumu.</summary>
        public static (string Target, string Args, string WorkDir, int WindowStyle) Plan(ProxyApp a, ProxyEntry p)
        {
            string url = ProxyUrl(a, p);
            string extra = string.IsNullOrWhiteSpace(a.ExtraArgs) ? "" : " " + a.ExtraArgs.Trim();
            string exe = a.ExePath;
            string wd = Path.GetDirectoryName(exe);
            switch (a.Mode)
            {
                case AppProxyMode.Discord:
                    {
                        string upd = exe;
                        string flavor = "Discord";
                        if (!Path.GetFileName(exe).Equals("Update.exe", StringComparison.OrdinalIgnoreCase))
                        {
                            // ...\Discord\app-1.0.x\Discord.exe → ...\Discord\Update.exe
                            flavor = Path.GetFileNameWithoutExtension(exe);
                            var root = Directory.GetParent(wd)?.FullName ?? wd;
                            upd = Path.Combine(root, "Update.exe");
                            wd = root;
                        }
                        else
                        {
                            string folder = Path.GetFileName(wd);
                            flavor = folder.Equals("DiscordPTB", StringComparison.OrdinalIgnoreCase) ? "DiscordPTB"
                                   : folder.Equals("DiscordCanary", StringComparison.OrdinalIgnoreCase) ? "DiscordCanary" : "Discord";
                        }
                        return (upd, "--processStart " + flavor + ".exe --process-start-args \"--proxy-server=" + url + extra.Replace("\"", "") + "\"", wd, 1);
                    }
                case AppProxyMode.Chromium:
                    {
                        string args = "--proxy-server=\"" + url + "\"";
                        if (a.SeparateProfile && IsBrowser(a)) args += " --user-data-dir=\"" + ProfileDir(a) + "\" --no-first-run";
                        return (exe, args + extra, wd, 1);
                    }
                case AppProxyMode.Firefox:
                    {
                        string dir = ProfileDir(a);
                        WriteFirefoxPrefs(dir, a, p);
                        return (exe, "-no-remote -profile \"" + dir + "\"" + extra, wd, 1);
                    }
                default:
                    {
                        // ortam değişkenleriyle başlat (cmd simge durumunda açılıp hemen kapanır)
                        string cmd = Path.Combine(Environment.SystemDirectory, "cmd.exe");
                        var sb = new StringBuilder("/c ");
                        foreach (var v in new[] { "HTTP_PROXY", "HTTPS_PROXY", "ALL_PROXY", "http_proxy", "https_proxy", "all_proxy" })
                            sb.Append("set \"" + v + "=" + url + "\" && ");
                        sb.Append("set \"NO_PROXY=localhost,127.0.0.1\" && start \"\" \"" + exe + "\"" + extra);
                        return (cmd, sb.ToString(), wd, 7);
                    }
            }
        }

        static void WriteFirefoxPrefs(string dir, ProxyApp a, ProxyEntry p)
        {
            Directory.CreateDirectory(dir);
            bool bridge = NeedsBridge(a, p);
            string host = bridge ? "127.0.0.1" : p.Host;
            int port = bridge ? p.LocalPort : p.Port;
            bool socks = !bridge && p.Kind == ProxyKind.Socks5;
            var sb = new StringBuilder();
            sb.AppendLine("// DPI Kalkan tarafından yazıldı – bu profil yalnızca proxy ile çalışır");
            sb.AppendLine("user_pref(\"network.proxy.type\", 1);");
            if (socks)
            {
                sb.AppendLine("user_pref(\"network.proxy.socks\", \"" + host + "\");");
                sb.AppendLine("user_pref(\"network.proxy.socks_port\", " + port + ");");
                sb.AppendLine("user_pref(\"network.proxy.socks_version\", 5);");
                sb.AppendLine("user_pref(\"network.proxy.socks_remote_dns\", true);");
                sb.AppendLine("user_pref(\"network.proxy.http\", \"\");");
                sb.AppendLine("user_pref(\"network.proxy.share_proxy_settings\", false);");
            }
            else
            {
                sb.AppendLine("user_pref(\"network.proxy.http\", \"" + host + "\");");
                sb.AppendLine("user_pref(\"network.proxy.http_port\", " + port + ");");
                sb.AppendLine("user_pref(\"network.proxy.ssl\", \"" + host + "\");");
                sb.AppendLine("user_pref(\"network.proxy.ssl_port\", " + port + ");");
                sb.AppendLine("user_pref(\"network.proxy.share_proxy_settings\", true);");
                sb.AppendLine("user_pref(\"network.proxy.socks\", \"\");");
            }
            sb.AppendLine("user_pref(\"network.proxy.no_proxies_on\", \"localhost, 127.0.0.1\");");
            sb.AppendLine("user_pref(\"network.trr.mode\", 0);");
            sb.AppendLine("user_pref(\"browser.shell.checkDefaultBrowser\", false);");
            sb.AppendLine("user_pref(\"browser.aboutwelcome.enabled\", false);");
            File.WriteAllText(Path.Combine(dir, "user.js"), sb.ToString(), new UTF8Encoding(false));
        }

        /// <summary>Kural için .lnk kısayolu oluşturur.</summary>
        public static string CreateShortcut(ProxyApp a, ProxyEntry p, string path)
        {
            var (target, args, wd, style) = Plan(a, p);
            if (!File.Exists(target)) throw new FileNotFoundException("Program bulunamadı: " + target);
            var t = Type.GetTypeFromProgID("WScript.Shell");
            object sh = Activator.CreateInstance(t);
            try
            {
                object lnk = t.InvokeMember("CreateShortcut", BindingFlags.InvokeMethod, null, sh, new object[] { path });
                var lt = lnk.GetType();
                void Set(string prop, object v) => lt.InvokeMember(prop, BindingFlags.SetProperty, null, lnk, new[] { v });
                Set("TargetPath", target);
                Set("Arguments", args);
                Set("WorkingDirectory", wd ?? "");
                Set("WindowStyle", style);
                Set("IconLocation", a.Mode == AppProxyMode.Discord ? target + ",0" : a.ExePath + ",0");
                Set("Description", a.Name + " – " + p.KindLabel + " proxy " + p.Address + " (DPI Kalkan)");
                lt.InvokeMember("Save", BindingFlags.InvokeMethod, null, lnk, null);
                Marshal.FinalReleaseComObject(lnk);
            }
            finally { Marshal.FinalReleaseComObject(sh); }
            return path;
        }

        static IEnumerable<Process> RunningOf(ProxyApp a)
        {
            string name = a.Mode == AppProxyMode.Discord
                ? (Path.GetFileName(a.ExePath).Equals("Update.exe", StringComparison.OrdinalIgnoreCase) ? Path.GetFileName(Path.GetDirectoryName(a.ExePath)) : Path.GetFileNameWithoutExtension(a.ExePath))
                : Path.GetFileNameWithoutExtension(a.ExePath);
            return Process.GetProcessesByName(name);
        }

        /// <summary>Programı proxy ile, normal kullanıcı yetkisiyle başlatır.</summary>
        public static bool Launch(ProxyApp a, ProxyEntry p, out string message)
        {
            try
            {
                if (!File.Exists(a.ExePath)) { message = "Program bulunamadı: " + a.ExePath; return false; }
                if (NeedsBridge(a, p) && !ProxyBridge.Ensure(p, out var err)) { message = err; return false; }
                bool browserProfile = a.SeparateProfile && IsBrowser(a);
                if (a.CloseRunning && !browserProfile && a.Mode != AppProxyMode.Firefox)
                {
                    var procs = RunningOf(a).ToList();
                    foreach (var pr in procs) using (pr) { try { pr.Kill(); } catch { } }
                    if (procs.Count > 0) Thread.Sleep(1200);
                }
                string lnk = Path.Combine(Path.GetTempPath(), "DPIKalkan-" + Scripts.Slug(a.Name) + "-proxy.lnk");
                CreateShortcut(a, p, lnk);
                bool ok = Sys.OpenAsUser(lnk);
                message = ok ? a.Name + " " + p.KindLabel + " proxy (" + p.Address + ") ile açılıyor" + (NeedsBridge(a, p) ? " – yerel köprü üzerinden." : ".")
                             : a.Name + " başlatılamadı.";
                App.Log(message, ok ? LogLevel.Success : LogLevel.Warn);
                return ok;
            }
            catch (Exception ex)
            {
                message = a.Name + " proxy ile açılamadı: " + ex.Message;
                App.Log(message, LogLevel.Warn);
                return false;
            }
        }
    }
}
