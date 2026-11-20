using System;
using System.Collections.Generic;
using System.IO;
using System.IO.Compression;
using System.Linq;
using System.Net;
using System.Text;
using System.Text.RegularExpressions;

namespace DPIKalkan
{
    /// <summary>Destek paketi (kişisel bilgileri ayıklanmış) ve GitHub'da sorun bildirme.</summary>
    public static class Support
    {
        /// <summary>Paylaşılabilecek bilinen genel adresler (DNS sunucuları, Discord/Cloudflare, engel sayfası).</summary>
        static readonly string[] KeepIp =
        {
            "1.1.1.1", "1.0.0.1", "8.8.8.8", "8.8.4.4", "9.9.9.9", "9.9.9.10", "149.112.112.112", "149.112.112.10",
            "77.88.8.8", "77.88.8.1", "77.88.8.88", "94.140.14.14", "94.140.15.15", "94.140.14.140", "94.140.14.141",
            "208.67.222.222", "208.67.220.220", "208.67.222.123", "195.175.254.2", "127.0.0.1", "0.0.0.0",
        };

        /// <summary>Metindeki kişisel bilgileri gizler: IP adresleri, kullanıcı klasörü adı, e-posta.</summary>
        public static string Redact(string text)
        {
            if (string.IsNullOrEmpty(text)) return text;
            // C:\Users\<ad>\  →  C:\Users\<kullanıcı>\
            text = Regex.Replace(text, @"(?i)([a-z]:\\(?:Users|Kullanıcılar)\\)[^\\/""\r\n]+", "$1<kullanıcı>");
            // e-posta
            text = Regex.Replace(text, @"[\w.+-]+@[\w-]+\.[\w.-]+", "<e-posta>");
            // IPv4 (Discord/Cloudflare 162.159.x.x ve bilinen DNS'ler korunur)
            text = Regex.Replace(text, @"\b(\d{1,3})\.(\d{1,3})\.(\d{1,3})\.(\d{1,3})\b", m =>
            {
                string ip = m.Value;
                if (KeepIp.Contains(ip) || ip.StartsWith("162.159.") || ip.StartsWith("104.16.")) return ip;
                if (!m.Groups.Cast<Group>().Skip(1).All(g => int.TryParse(g.Value, out int v) && v <= 255)) return ip;   // sürüm numarası vb.
                if (ip.StartsWith("192.168.") || ip.StartsWith("10.") || Regex.IsMatch(ip, @"^172\.(1[6-9]|2\d|3[01])\.")) return "<yerel-ip>";
                return "<ip>";
            });
            // genel IPv6 adresleri (fe80 yerel ve bilinen DNS'ler korunur)
            text = Regex.Replace(text, @"(?<![\w:.])(?:[0-9a-fA-F]{0,4}:){2,7}[0-9a-fA-F]{0,4}(?![\w:])", m =>
            {
                string v = m.Value.ToLowerInvariant();
                if (v.StartsWith("fe80") || v.StartsWith("2a02:6b8::") || v.StartsWith("2a10:50c0::") || v.StartsWith("2620:fe::") ||
                    v.StartsWith("2620:119:") || v.StartsWith("2606:4700:") || v.StartsWith("2001:4860:")) return m.Value;
                return IPAddress.TryParse(m.Value, out _) ? "<ipv6>" : m.Value;
            });
            return text;
        }

        public static string SystemSummary()
        {
            var sb = new StringBuilder();
            sb.AppendLine("DPI Kalkan " + App.Version + " · Windows derleme " + SystemDns.WindowsBuild + (Environment.Is64BitOperatingSystem ? " (64 bit)" : " (32 bit)"));
            sb.AppendLine("Arayüz: " + App.Settings.UiLevel + " · Tema: " + App.Settings.ThemeId + " · Hizmet modu: " + (App.Settings.ServiceMode ? "açık" : "kapalı") +
                          " · Hedefli mod: " + (App.Settings.TargetedMode ? "açık" : "kapalı"));
            var eng = App.Engine;
            sb.AppendLine("Koruma: " + eng.State + (eng.LastErrorCode.Length > 0 ? " [" + eng.LastErrorCode + "]" : "") + " · Yöntem: " + (eng.ActiveProfile ?? App.SelectedProfile).Name);
            sb.AppendLine("Komut: goodbyedpi.exe " + (eng.ActiveProfile ?? App.SelectedProfile).BuildArgs(App.Settings.TargetedMode ? "<liste>" : null));
            var t = App.LastTest;
            if (t != null) sb.AppendLine("Son test: " + t.Count(x => x.Ok) + "/" + t.Count + (App.LastDnsOk == false ? ", DNS yanlış" : "") +
                                         (t.Any(x => !x.Ok) ? " · açılmayan: " + string.Join(", ", t.Where(x => !x.Ok).Select(x => x.Host + " " + x.Code)) : ""));
            var probs = App.Problems.Snapshot();
            sb.AppendLine("Algılanan sorunlar: " + (probs.Count == 0 ? "yok" : string.Join(", ", probs.Select(p => p.Code + " " + p.Info?.Title))));
            return Redact(sb.ToString());
        }

        /// <summary>Günlük, geçmiş, ayarlar (şifreler ve proxy adresleri çıkarılmış) ve sistem özetini zip olarak kaydeder.</summary>
        public static void CreatePackage(string zipPath)
        {
            string tmp = Path.Combine(Path.GetTempPath(), "DPIKalkan-destek-" + Guid.NewGuid().ToString("N").Substring(0, 8));
            Directory.CreateDirectory(tmp);
            try
            {
                File.WriteAllText(Path.Combine(tmp, "ozet.txt"), SystemSummary(), Encoding.UTF8);
                if (File.Exists(App.LogFile))
                {
                    var lines = File.ReadAllLines(App.LogFile, Encoding.UTF8);
                    File.WriteAllText(Path.Combine(tmp, "gunluk.txt"), Redact(string.Join(Environment.NewLine, lines.Skip(Math.Max(0, lines.Length - 3000)))), Encoding.UTF8);
                }
                string hist = Path.Combine(App.DataDir, "gecmis.csv");
                if (File.Exists(hist)) File.WriteAllText(Path.Combine(tmp, "gecmis.csv"), Redact(File.ReadAllText(hist, Encoding.UTF8)), Encoding.UTF8);
                string upd = Path.Combine(App.DataDir, "guncelleme.log");
                if (File.Exists(upd)) File.WriteAllText(Path.Combine(tmp, "guncelleme.txt"), Redact(File.ReadAllText(upd, Encoding.UTF8)), Encoding.UTF8);

                // ayarlar: proxy bilgileri ve DNS yedekleri çıkarılır
                var copy = SettingsStore.Clone(App.Settings);
                foreach (var p in copy.Proxies) { p.Host = "<gizli>"; p.User = p.User.Length > 0 ? "<gizli>" : ""; p.PassData = ""; p.LastResult = Redact(p.LastResult); }
                foreach (var a in copy.ProxyApps) a.ExePath = Redact(a.ExePath);
                copy.DnsBackups.Clear();
                copy.SystemProxyBackup = null;
                SettingsStore.Export(copy, Path.Combine(tmp, "ayarlar.xml"));
                File.WriteAllText(Path.Combine(tmp, "OKUBENI.txt"),
                    "Bu paket DPI Kalkan tarafından oluşturuldu. IP adresleri, kullanıcı klasörü adı, e-posta, proxy adresleri ve şifreleri gizlendi.\r\n" +
                    "Paylaşmadan önce dosyaları açıp kontrol edebilirsiniz.\r\n", Encoding.UTF8);
                if (File.Exists(zipPath)) File.Delete(zipPath);
                ZipFile.CreateFromDirectory(tmp, zipPath);
                App.Log("Destek paketi oluşturuldu: " + zipPath, LogLevel.Success);
            }
            finally { try { Directory.Delete(tmp, true); } catch { } }
        }

        /// <summary>Bilgileri doldurulmuş "yeni sorun" sayfasının adresi (kullanıcı göndermeden önce görür ve düzenler).</summary>
        public static string IssueUrl()
        {
            var eng = App.Engine;
            var top = App.Problems.Top;
            string title = top != null ? top.Code + " – " + top.Info?.Title : eng.State == EngineState.Error ? "Koruma çalışmıyor" : "";
            string Q(string s) => Uri.EscapeDataString(s ?? "");
            string body = SystemSummary();
            if (body.Length > 1500) body = body.Substring(0, 1500);
            return "https://github.com/" + Updater.Owner + "/" + Updater.Repo + "/issues/new?template=hata.yml" +
                   "&title=" + Q(title) + "&surum=" + Q(App.Version) + "&windows=" + Q("Derleme " + SystemDns.WindowsBuild) +
                   "&kod=" + Q(top?.Code ?? eng.LastErrorCode) + "&ozet=" + Q(body);
        }
    }
}
