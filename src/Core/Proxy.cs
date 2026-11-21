using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Net;
using System.Net.Security;
using System.Net.Sockets;
using System.Reflection;
using System.Runtime.InteropServices;
using System.Security.Authentication;
using System.Security.Cryptography;
using System.Text;
using System.Threading;
using System.Xml.Serialization;
using Microsoft.Win32;

namespace DPIKalkan
{
    public enum ProxyKind { Http, Socks5 }

    /// <summary>Kayıtlı bir proxy sunucusu. Şifre Windows DPAPI ile (yalnızca bu kullanıcı çözebilir) saklanır.</summary>
    public class ProxyEntry
    {
        public string Id { get; set; } = Guid.NewGuid().ToString("N");
        public string Name { get; set; } = "";
        public ProxyKind Kind { get; set; } = ProxyKind.Http;
        public string Host { get; set; } = "";
        public int Port { get; set; } = 8080;
        public string User { get; set; } = "";
        public string PassData { get; set; } = "";
        public string LastResult { get; set; } = "";
        public bool LastOk { get; set; }
        public DateTime? LastTest { get; set; }
        public int LastMs { get; set; }
        /// <summary>Yerel köprünün bu proxy için dinlediği port (127.0.0.1).</summary>
        public int LocalPort { get; set; }

        static readonly byte[] Entropy = Encoding.UTF8.GetBytes("DPIKalkan.Proxy");

        [XmlIgnore]
        public string Password
        {
            get
            {
                if (string.IsNullOrEmpty(PassData)) return "";
                try { return Encoding.UTF8.GetString(ProtectedData.Unprotect(Convert.FromBase64String(PassData), Entropy, DataProtectionScope.CurrentUser)); }
                catch { return ""; }
            }
            set
            {
                PassData = string.IsNullOrEmpty(value) ? ""
                    : Convert.ToBase64String(ProtectedData.Protect(Encoding.UTF8.GetBytes(value), Entropy, DataProtectionScope.CurrentUser));
            }
        }

        public string Address => (Host.Contains(":") && !Host.StartsWith("[") ? "[" + Host + "]" : Host) + ":" + Port;
        public string Scheme => Kind == ProxyKind.Http ? "http" : "socks5";
        public string KindLabel => Kind == ProxyKind.Http ? "HTTP" : "SOCKS5";
        public bool HasAuth => !string.IsNullOrEmpty(User);
        public string Title => string.IsNullOrWhiteSpace(Name) ? Address : Name;
    }

    public class ProxyBackup
    {
        public bool Enabled { get; set; }
        public string Server { get; set; } = "";
        public string Override { get; set; } = "";
        public string AutoConfigUrl { get; set; } = "";
    }

    public class ProxyTestResult
    {
        public bool Reach, Tunnel, Tls, Ok, KindFixed;
        public int ConnectMs, TotalMs;
        public string ExitIp = "", Country = "", Message = "", Code = "";
    }

    sealed class ProxyFail : Exception
    {
        public readonly string Code;
        public ProxyFail(string code, string msg) : base(msg) { Code = code; }
    }

    public static class ProxyTools
    {
        // ------------------------------------------------------------ ayrıştırma

        /// <summary>
        /// "1.2.3.4:8080", "http://host:port", "socks5://kullanıcı:şifre@host:port", "host:port:kullanıcı:şifre"
        /// biçimlerini kabul eder.
        /// </summary>
        public static bool TryParse(string text, ProxyKind defaultKind, out ProxyEntry p, out string error)
        {
            p = null;
            error = "";
            string s = (text ?? "").Trim();
            if (s.Length == 0) { error = "Proxy adresi boş."; return false; }
            var kind = defaultKind;
            int i = s.IndexOf("://", StringComparison.Ordinal);
            if (i > 0)
            {
                string scheme = s.Substring(0, i).ToLowerInvariant();
                if (scheme.StartsWith("socks")) kind = ProxyKind.Socks5;
                else if (scheme == "http" || scheme == "https") kind = ProxyKind.Http;
                else { error = "Desteklenmeyen proxy türü: " + scheme + " (http ya da socks5 olmalı)."; return false; }
                s = s.Substring(i + 3);
            }
            s = s.TrimEnd('/');
            string user = "", pass = "";
            int at = s.LastIndexOf('@');
            if (at > 0)
            {
                string cred = s.Substring(0, at);
                s = s.Substring(at + 1);
                int c = cred.IndexOf(':');
                user = Uri.UnescapeDataString(c >= 0 ? cred.Substring(0, c) : cred);
                pass = c >= 0 ? Uri.UnescapeDataString(cred.Substring(c + 1)) : "";
            }
            string host;
            int port;
            if (s.StartsWith("["))
            {
                int e = s.IndexOf(']');
                if (e < 0 || e + 2 > s.Length || s[e + 1] != ':' || !int.TryParse(s.Substring(e + 2), out port)) { error = "Geçersiz IPv6 proxy adresi."; return false; }
                host = s.Substring(1, e - 1);
            }
            else
            {
                var parts = s.Split(new[] { ':', ' ', '\t' }, StringSplitOptions.RemoveEmptyEntries);
                if (parts.Length < 2 || !int.TryParse(parts[1], out port)) { error = "Adresi 'IP:port' biçiminde yazın (ör. 185.12.34.56:8080)."; return false; }
                host = parts[0];
                if (parts.Length >= 4 && user.Length == 0) { user = parts[2]; pass = parts[3]; }   // ip:port:kullanıcı:şifre
            }
            if (port < 1 || port > 65535) { error = "Port 1-65535 arasında olmalı."; return false; }
            if (Uri.CheckHostName(host) == UriHostNameType.Unknown) { error = "Geçersiz sunucu adı ya da IP: " + host; return false; }
            p = new ProxyEntry { Kind = kind, Host = host, Port = port, User = user, Password = pass };
            return true;
        }

        // ------------------------------------------------------------ test

        static int Remaining(Stopwatch sw, int timeout) => Math.Max(500, timeout - (int)sw.ElapsedMilliseconds);

        static byte[] ReadExact(Stream s, int n)
        {
            var b = new byte[n];
            int got = 0;
            while (got < n)
            {
                int r = s.Read(b, got, n - got);
                if (r <= 0) throw new ProxyFail("DK-805", "Proxy bağlantıyı kapattı.");
                got += r;
            }
            return b;
        }

        /// <summary>Proxy üzerinden host:port'a bir tünel açar.</summary>
        internal static Stream OpenTunnel(ProxyEntry p, string host, int port, int timeout, Stopwatch sw, ProxyTestResult r, out TcpClient client)
        {
            client = new TcpClient();
            try
            {
                var t = client.ConnectAsync(p.Host, p.Port);
                if (!t.Wait(timeout)) throw new ProxyFail("DK-805", "Proxy sunucusu yanıt vermedi (zaman aşımı).");
            }
            catch (AggregateException ex)
            {
                var se = ex.InnerException as SocketException;
                throw new ProxyFail("DK-805", "Proxy sunucusuna bağlanılamadı" + (se != null ? " (" + se.SocketErrorCode + ")" : "") + ".");
            }
            if (r != null) { r.Reach = true; r.ConnectMs = (int)sw.ElapsedMilliseconds; }
            client.ReceiveTimeout = client.SendTimeout = Remaining(sw, timeout);
            var s = client.GetStream();

            if (p.Kind == ProxyKind.Http)
            {
                var req = new StringBuilder();
                req.Append("CONNECT " + host + ":" + port + " HTTP/1.1\r\nHost: " + host + ":" + port + "\r\n");
                if (p.HasAuth)
                    req.Append("Proxy-Authorization: Basic " + Convert.ToBase64String(Encoding.UTF8.GetBytes(p.User + ":" + p.Password)) + "\r\n");
                req.Append("User-Agent: " + NetTest.UserAgent + "\r\n\r\n");
                var bytes = Encoding.ASCII.GetBytes(req.ToString());
                s.Write(bytes, 0, bytes.Length);
                // yanıt başlıklarını oku
                var head = new StringBuilder();
                var one = new byte[1];
                while (!head.ToString().EndsWith("\r\n\r\n") && head.Length < 8192)
                {
                    if (s.Read(one, 0, 1) <= 0) throw new ProxyFail("DK-805", "Proxy bağlantıyı kapattı (HTTP proxy değil olabilir; SOCKS5 deneyin).");
                    head.Append((char)one[0]);
                }
                string status = head.ToString().Split('\n')[0].Trim();
                var parts = status.Split(' ');
                int code = parts.Length > 1 && int.TryParse(parts[1], out int c) ? c : 0;
                if (code == 407) throw new ProxyFail("DK-806", "Proxy kullanıcı adı/şifre istiyor ya da bilgiler yanlış (HTTP 407).");
                if (code != 200) throw new ProxyFail("DK-807", "Proxy " + host + " adresine tünel açmadı: " + status);
            }
            else
            {
                s.Write(p.HasAuth ? new byte[] { 5, 2, 0, 2 } : new byte[] { 5, 1, 0 }, 0, p.HasAuth ? 4 : 3);
                var hello = ReadExact(s, 2);
                if (hello[0] != 5) throw new ProxyFail("DK-805", "Bu sunucu SOCKS5 proxy değil (HTTP proxy olabilir).");
                if (hello[1] == 0xFF) throw new ProxyFail("DK-806", "SOCKS5 proxy kullanıcı adı/şifre istiyor.");
                if (hello[1] == 2)
                {
                    var u = Encoding.UTF8.GetBytes(p.User);
                    var pw = Encoding.UTF8.GetBytes(p.Password);
                    var auth = new List<byte> { 1, (byte)u.Length };
                    auth.AddRange(u);
                    auth.Add((byte)pw.Length);
                    auth.AddRange(pw);
                    s.Write(auth.ToArray(), 0, auth.Count);
                    var ar = ReadExact(s, 2);
                    if (ar[1] != 0) throw new ProxyFail("DK-806", "SOCKS5 kullanıcı adı/şifre kabul edilmedi.");
                }
                var hb = Encoding.ASCII.GetBytes(host);
                var conn = new List<byte> { 5, 1, 0 };
                if (IPAddress.TryParse(host, out var ip) && ip.AddressFamily == AddressFamily.InterNetwork) { conn.Add(1); conn.AddRange(ip.GetAddressBytes()); }
                else { conn.Add(3); conn.Add((byte)hb.Length); conn.AddRange(hb); }
                conn.Add((byte)(port >> 8)); conn.Add((byte)port);
                s.Write(conn.ToArray(), 0, conn.Count);
                var rep = ReadExact(s, 4);
                if (rep[1] != 0)
                {
                    string[] why = { "", "genel hata", "kurallarca yasak", "ağa ulaşılamıyor", "sunucuya ulaşılamıyor", "bağlantı reddedildi", "süre doldu", "komut desteklenmiyor", "adres türü desteklenmiyor" };
                    throw new ProxyFail("DK-807", "SOCKS5 proxy " + host + " adresine bağlanamadı: " + (rep[1] < why.Length ? why[rep[1]] : "kod " + rep[1]));
                }
                int alen = rep[3] == 1 ? 4 : rep[3] == 4 ? 16 : ReadExact(s, 1)[0];
                ReadExact(s, alen + 2);
            }
            if (r != null) r.Tunnel = true;
            return s;
        }

        /// <summary>Tek bir deneme: bağlantı, tünel ve hedefe şifreli bağlantı (kayıt/günlük yok).</summary>
        static ProxyTestResult TestOnce(ProxyEntry p, string target, int timeout)
        {
            var r = new ProxyTestResult();
            var sw = Stopwatch.StartNew();
            TcpClient client = null;
            try
            {
                var s = OpenTunnel(p, target, 443, timeout, sw, r, out client);
                using (var ssl = new SslStream(s, false, (a, b, c, e) => e == SslPolicyErrors.None))
                {
                    try { ssl.AuthenticateAsClient(target, null, SslProtocols.None, false); }
                    catch (AuthenticationException) { throw new ProxyFail("DK-807", "Proxy üzerinden " + target + " için geçerli sertifika alınamadı (proxy trafiğe müdahale ediyor olabilir)."); }
                    catch (IOException) { throw new ProxyFail("DK-807", "Proxy üzerinden şifreli bağlantı kesildi (proxy'nin bulunduğu ülkede de engel olabilir)."); }
                    r.Tls = true;
                }
                r.TotalMs = (int)sw.ElapsedMilliseconds;
                r.Ok = true;
                r.Message = target + " proxy üzerinden erişilebilir (" + r.TotalMs + " ms).";
            }
            catch (ProxyFail f) { r.Code = f.Code; r.Message = f.Message; }
            catch (IOException)
            {
                // Bağlandı ama el sıkışma sırasında kesildiyse büyük olasılıkla tür yanlış (HTTP ↔ SOCKS5)
                r.Code = r.Reach && !r.Tunnel ? "DK-805" : r.Reach ? "DK-807" : "DK-805";
                r.Message = r.Reach && !r.Tunnel ? "Proxy el sıkışmada bağlantıyı kesti (proxy türü yanlış olabilir)." : "Proxy yanıt vermedi ya da bağlantıyı kesti.";
            }
            catch (Exception ex) { r.Code = "DK-805"; r.Message = ex.Message; }
            finally { try { client?.Close(); } catch { } }
            return r;
        }

        static void FillExitInfo(ProxyEntry p, ProxyTestResult r)
        {
            TcpClient client = null;
            try
            {
                var sw2 = Stopwatch.StartNew();
                var s = OpenTunnel(p, "1.1.1.1", 443, 7000, sw2, null, out client);
                using (var ssl = new SslStream(s, false, (a, b, c, e) => true))
                {
                    ssl.AuthenticateAsClient("one.one.one.one", null, SslProtocols.None, false);
                    var req = Encoding.ASCII.GetBytes("GET /cdn-cgi/trace HTTP/1.1\r\nHost: one.one.one.one\r\nConnection: close\r\nUser-Agent: " + NetTest.UserAgent + "\r\n\r\n");
                    ssl.Write(req, 0, req.Length);
                    string body = new StreamReader(ssl, Encoding.ASCII).ReadToEnd();
                    foreach (var line in body.Split('\n'))
                    {
                        if (line.StartsWith("ip=")) r.ExitIp = line.Substring(3).Trim();
                        else if (line.StartsWith("loc=")) r.Country = line.Substring(4).Trim();
                    }
                }
            }
            catch (Exception ign) { App.Ignored("Proxy çıkış bilgisi okunamadı", ign); }
            finally { try { client?.Close(); } catch { } }
        }

        /// <summary>
        /// Proxy'yi dener: bağlantı, tünel, Discord'a şifreli bağlantı ve çıkış IP'si/ülkesi.
        /// Sunucuya bağlanıldığı hâlde el sıkışma başarısızsa diğer türü (HTTP ↔ SOCKS5) de dener ve doğru olanı kaydeder.
        /// </summary>
        public static ProxyTestResult Test(ProxyEntry p, string target = "discord.com", int timeout = 9000, bool exitInfo = true, bool log = true)
        {
            var r = TestOnce(p, target, timeout);
            if (!r.Ok && r.Reach && !r.Tunnel && r.Code == "DK-805")
            {
                var other = p.Kind == ProxyKind.Http ? ProxyKind.Socks5 : ProxyKind.Http;
                var q = new ProxyEntry { Kind = other, Host = p.Host, Port = p.Port, User = p.User, PassData = p.PassData };
                var r2 = TestOnce(q, target, timeout);
                if (r2.Ok || r2.Tunnel)
                {
                    r2.KindFixed = true;
                    r2.Message += " (Tür " + q.KindLabel + " olarak düzeltildi.)";
                    p.Kind = other;
                    r = r2;
                }
            }
            if (r.Ok && exitInfo) FillExitInfo(p, r);
            p.LastTest = DateTime.Now;
            p.LastOk = r.Ok;
            p.LastMs = r.Ok ? r.TotalMs : 0;
            p.LastResult = r.Ok ? "Çalışıyor · " + r.TotalMs + " ms" + (r.Country.Length > 0 ? " · " + r.Country : "") : r.Code + " " + r.Message;
            if (log)
                App.Log("Proxy testi (" + p.KindLabel + " " + p.Address + "): " + (r.Ok ? "çalışıyor, " + r.TotalMs + " ms, çıkış IP " + r.ExitIp + " " + r.Country : "[" + r.Code + "] " + r.Message) +
                        (r.KindFixed ? " – tür " + p.KindLabel + " olarak düzeltildi" : ""), r.Ok ? LogLevel.Success : LogLevel.Warn);
            return r;
        }

        /// <summary>Yapıştırılan proxy listesini ayrıştırır (her satırda bir proxy; yorum ve boş satırlar atlanır).</summary>
        public static List<ProxyEntry> ParseList(string text, ProxyKind defaultKind, out int invalid)
        {
            var list = new List<ProxyEntry>();
            invalid = 0;
            foreach (var raw in (text ?? "").Split(new[] { '\r', '\n', ',', ';' }, StringSplitOptions.RemoveEmptyEntries))
            {
                string line = raw.Trim();
                if (line.Length == 0 || line.StartsWith("#") || line.StartsWith("//")) continue;
                if (TryParse(line, defaultKind, out var p, out _))
                {
                    if (!list.Any(x => x.Host == p.Host && x.Port == p.Port)) list.Add(p);
                }
                else invalid++;
            }
            return list;
        }

        /// <summary>Birçok proxy'yi aynı anda (en fazla 24 paralel) test eder.</summary>
        public static List<(ProxyEntry P, ProxyTestResult R)> BulkTest(List<ProxyEntry> items, Action<int, int> progress, CancellationToken ct)
        {
            var results = new (ProxyEntry, ProxyTestResult)[items.Count];
            int done = 0;
            var opts = new System.Threading.Tasks.ParallelOptions { MaxDegreeOfParallelism = 24, CancellationToken = ct };
            try
            {
                System.Threading.Tasks.Parallel.For(0, items.Count, opts, i =>
                {
                    var r = Test(items[i], "discord.com", 7000, exitInfo: false, log: false);
                    results[i] = (items[i], r);
                    progress?.Invoke(Interlocked.Increment(ref done), items.Count);
                });
            }
            catch (OperationCanceledException) { }
            var list = results.Where(x => x.Item1 != null).ToList();
            foreach (var x in list.Where(x => x.Item2.Ok)) FillExitInfo(x.Item1, x.Item2);
            App.Log("Toplu proxy testi: " + list.Count(x => x.Item2.Ok) + "/" + items.Count + " proxy çalışıyor.", LogLevel.Info);
            return list;
        }

        // ------------------------------------------------------------ Windows proxy'si

        const string InetKey = @"Software\Microsoft\Windows\CurrentVersion\Internet Settings";

        [DllImport("wininet.dll", SetLastError = true)]
        static extern bool InternetSetOption(IntPtr h, int option, IntPtr buffer, int len);

        static void NotifyChanged()
        {
            try
            {
                InternetSetOption(IntPtr.Zero, 39, IntPtr.Zero, 0);   // INTERNET_OPTION_SETTINGS_CHANGED
                InternetSetOption(IntPtr.Zero, 37, IntPtr.Zero, 0);   // INTERNET_OPTION_REFRESH
            }
            catch (Exception ign) { App.Ignored("Windows proxy değişikliği bildirilemedi", ign); }
        }

        public static (bool Enabled, string Server) CurrentSystem() => NetTest.SystemProxy();

        public static ProxyEntry ActiveSystemEntry()
        {
            var id = App.Settings.SystemProxyId;
            if (string.IsNullOrEmpty(id)) return null;
            var cur = CurrentSystem();
            if (!cur.Enabled) return null;
            return App.Settings.Proxies.FirstOrDefault(x => x.Id == id);
        }

        public static void ApplySystem(ProxyEntry p)
        {
            using (var k = Registry.CurrentUser.CreateSubKey(InetKey))
            {
                if (App.Settings.SystemProxyBackup == null)
                    App.Settings.SystemProxyBackup = new ProxyBackup
                    {
                        Enabled = Convert.ToInt32(k.GetValue("ProxyEnable", 0)) != 0,
                        Server = k.GetValue("ProxyServer", "") as string ?? "",
                        Override = k.GetValue("ProxyOverride", "") as string ?? "",
                        AutoConfigUrl = k.GetValue("AutoConfigURL", "") as string ?? "",
                    };
                k.SetValue("ProxyEnable", 1, RegistryValueKind.DWord);
                k.SetValue("ProxyServer", p.Kind == ProxyKind.Http ? p.Address : "socks=" + p.Address, RegistryValueKind.String);
                k.SetValue("ProxyOverride", "<local>;localhost;127.*;10.*;172.16.*;192.168.*", RegistryValueKind.String);
                if (k.GetValue("AutoConfigURL") != null) k.DeleteValue("AutoConfigURL", false);
            }
            App.Settings.SystemProxyId = p.Id;
            App.Save();
            NotifyChanged();
            App.Problems.Clear("DK-802");
            App.Log("Windows proxy'si ayarlandı: " + p.KindLabel + " " + p.Address, LogLevel.Success);
        }

        public static void RestoreSystem()
        {
            var b = App.Settings.SystemProxyBackup ?? new ProxyBackup();
            using (var k = Registry.CurrentUser.CreateSubKey(InetKey))
            {
                k.SetValue("ProxyEnable", b.Enabled ? 1 : 0, RegistryValueKind.DWord);
                if (b.Server.Length > 0) k.SetValue("ProxyServer", b.Server); else k.DeleteValue("ProxyServer", false);
                if (b.Override.Length > 0) k.SetValue("ProxyOverride", b.Override); else k.DeleteValue("ProxyOverride", false);
                if (b.AutoConfigUrl.Length > 0) k.SetValue("AutoConfigURL", b.AutoConfigUrl);
            }
            App.Settings.SystemProxyBackup = null;
            App.Settings.SystemProxyId = "";
            App.Save();
            NotifyChanged();
            App.Log("Windows proxy ayarı eski haline döndürüldü.", LogLevel.Info);
        }

        // ------------------------------------------------------------ Discord'u proxy ile aç

        public static string ChromiumArg(ProxyEntry p) => "--proxy-server=" + p.Scheme + "://" + p.Address;

        static string DiscordUpdater(out string flavor)
        {
            flavor = "Discord";
            string local = Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData);
            foreach (var f in new[] { "Discord", "DiscordPTB", "DiscordCanary" })
            {
                string upd = Path.Combine(local, f, "Update.exe");
                if (File.Exists(upd)) { flavor = f; return upd; }
            }
            return null;
        }

        /// <summary>Discord'u başlatan, proxy parametreli bir .lnk kısayolu oluşturur.</summary>
        public static string CreateShortcut(ProxyEntry p, string path)
        {
            string upd = DiscordUpdater(out string flavor);
            if (upd == null) throw new InvalidOperationException("Discord kurulu bulunamadı (%LOCALAPPDATA%\\Discord\\Update.exe yok).");
            var t = Type.GetTypeFromProgID("WScript.Shell");
            object sh = Activator.CreateInstance(t);
            try
            {
                object lnk = t.InvokeMember("CreateShortcut", BindingFlags.InvokeMethod, null, sh, new object[] { path });
                var lt = lnk.GetType();
                lt.InvokeMember("TargetPath", BindingFlags.SetProperty, null, lnk, new object[] { upd });
                lt.InvokeMember("Arguments", BindingFlags.SetProperty, null, lnk,
                    new object[] { "--processStart " + flavor + ".exe --process-start-args \"" + ChromiumArg(p) + "\"" });
                lt.InvokeMember("WorkingDirectory", BindingFlags.SetProperty, null, lnk, new object[] { Path.GetDirectoryName(upd) });
                string ico = Path.Combine(Path.GetDirectoryName(upd), "app.ico");
                if (File.Exists(ico)) lt.InvokeMember("IconLocation", BindingFlags.SetProperty, null, lnk, new object[] { ico });
                lt.InvokeMember("Description", BindingFlags.SetProperty, null, lnk, new object[] { "Discord – " + p.KindLabel + " proxy " + p.Address });
                lt.InvokeMember("Save", BindingFlags.InvokeMethod, null, lnk, null);
                Marshal.FinalReleaseComObject(lnk);
            }
            finally { Marshal.FinalReleaseComObject(sh); }
            return path;
        }

        /// <summary>Discord'u kapatır ve proxy ile (normal kullanıcı yetkisiyle) yeniden açar.</summary>
        public static bool LaunchDiscord(ProxyEntry p, out string message)
        {
            try
            {
                string lnk = Path.Combine(Path.GetTempPath(), "DPIKalkan-Discord-Proxy.lnk");
                CreateShortcut(p, lnk);
                foreach (var d in DiscordHelper.Processes()) using (d) { try { d.Kill(); } catch (Exception ign) { App.Ignored("Discord kapatılamadı", ign); } }
                foreach (var d in DiscordHelper.Processes()) using (d) { try { d.WaitForExit(4000); } catch { } }
                Thread.Sleep(800);
                bool ok = Sys.OpenAsUser(lnk);
                message = ok ? "Discord " + p.KindLabel + " proxy (" + p.Address + ") ile açılıyor." : "Discord başlatılamadı.";
                App.Log(message, ok ? LogLevel.Success : LogLevel.Warn);
                return ok;
            }
            catch (Exception ex)
            {
                message = ex.Message;
                App.Log("Discord proxy ile açılamadı: " + ex.Message, LogLevel.Warn);
                return false;
            }
        }
    }
}
