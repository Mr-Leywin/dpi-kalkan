using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Net;
using System.Net.Sockets;
using System.Text;
using System.Threading;
using System.Threading.Tasks;

namespace DPIKalkan
{
    /// <summary>
    /// Yerel proxy köprüsü: 127.0.0.1 üzerinde şifresiz bir HTTP + SOCKS5 proxy açar ve bağlantıları seçili
    /// (gerekirse kullanıcı adı/şifreli) proxy'ye iletir. Böylece şifreli proxy'yi desteklemeyen programlar
    /// (Discord, tarayıcılar) da şifreli proxy kullanabilir. Yalnızca bu bilgisayardan erişilebilir.
    /// </summary>
    public static class ProxyBridge
    {
        sealed class Listener
        {
            public ProxyEntry P;
            public TcpListener L;
            public int Active;
            public long Total;
        }

        static readonly object gate = new object();
        static readonly Dictionary<string, Listener> map = new Dictionary<string, Listener>();

        public const int BasePort = 18901;

        /// <summary>Kayıtlı proxy'lere sabit yerel port atar.</summary>
        public static void AssignPorts(List<ProxyEntry> proxies)
        {
            var used = new HashSet<int>(proxies.Where(p => p.LocalPort > 0).Select(p => p.LocalPort));
            foreach (var p in proxies.Where(p => p.LocalPort <= 0))
            {
                int port = BasePort;
                while (used.Contains(port)) port++;
                p.LocalPort = port;
                used.Add(port);
            }
        }

        public static string LocalUrl(ProxyEntry p) => "http://127.0.0.1:" + p.LocalPort;

        public static bool IsRunning(ProxyEntry p)
        {
            lock (gate) return p != null && map.ContainsKey(p.Id);
        }

        public static int ActiveConnections
        {
            get { lock (gate) return map.Values.Sum(l => l.Active); }
        }

        public static int RunningCount
        {
            get { lock (gate) return map.Count; }
        }

        public static (int Active, long Total) Stats(ProxyEntry p)
        {
            lock (gate) return map.TryGetValue(p.Id, out var l) ? (l.Active, l.Total) : (0, 0);
        }

        /// <summary>Bu proxy için köprüyü başlatır (çalışıyorsa bir şey yapmaz).</summary>
        public static bool Ensure(ProxyEntry p, out string error)
        {
            error = "";
            lock (gate)
            {
                if (map.TryGetValue(p.Id, out var cur))
                {
                    cur.P = p;   // ayarlar değişmiş olabilir
                    return true;
                }
                if (p.LocalPort <= 0) AssignPorts(App.Settings?.Proxies ?? new List<ProxyEntry> { p });
                try
                {
                    var l = new TcpListener(IPAddress.Loopback, p.LocalPort);
                    l.Server.SetSocketOption(SocketOptionLevel.Socket, SocketOptionName.ExclusiveAddressUse, true);
                    l.Start(64);
                    var li = new Listener { P = p, L = l };
                    map[p.Id] = li;
                    _ = AcceptLoop(li);
                }
                catch (Exception ex)
                {
                    error = "Yerel köprü 127.0.0.1:" + p.LocalPort + " portunu açamadı: " + ex.Message;
                    App.Log(error, LogLevel.Warn);
                    return false;
                }
            }
            App.Log("Yerel proxy köprüsü açıldı: 127.0.0.1:" + p.LocalPort + " → " + p.KindLabel + " " + p.Address, LogLevel.Info);
            App.RaiseStatus();
            return true;
        }

        public static void Stop(string proxyId)
        {
            Listener l;
            lock (gate)
            {
                if (!map.TryGetValue(proxyId, out l)) return;
                map.Remove(proxyId);
            }
            try { l.L.Stop(); } catch { }
            App.Log("Yerel proxy köprüsü kapatıldı (port " + l.P.LocalPort + ").", LogLevel.Info);
            App.RaiseStatus();
        }

        public static void StopAll()
        {
            List<string> ids;
            lock (gate) ids = map.Keys.ToList();
            foreach (var id in ids) Stop(id);
        }

        /// <summary>Kayıtlı programların ihtiyaç duyduğu köprüleri başlatır, artık gerekmeyenleri kapatır.</summary>
        public static void Sync()
        {
            var s = App.Settings;
            var need = new HashSet<string>(s.ProxyApps
                .Select(a => (App: a, P: s.Proxies.FirstOrDefault(p => p.Id == a.ProxyId)))
                .Where(x => x.P != null && AppProxy.NeedsBridge(x.App, x.P))
                .Select(x => x.P.Id));
            foreach (var id in need) { var p = s.Proxies.First(x => x.Id == id); Ensure(p, out _); }
            List<string> running;
            lock (gate) running = map.Keys.ToList();
            foreach (var id in running.Where(id => !need.Contains(id) && !s.Proxies.Any(p => p.Id == id))) Stop(id);
        }

        // ================================================================ bağlantı işleme

        static async Task AcceptLoop(Listener l)
        {
            while (true)
            {
                TcpClient c;
                try { c = await l.L.AcceptTcpClientAsync().ConfigureAwait(false); }
                catch { return; }   // durduruldu
                _ = Task.Run(() => Handle(l, c));
            }
        }

        static async Task Handle(Listener l, TcpClient c)
        {
            Interlocked.Increment(ref l.Active);
            Interlocked.Increment(ref l.Total);
            TcpClient up = null;
            try
            {
                c.NoDelay = true;
                var cs = c.GetStream();
                var first = new byte[1];
                if (await cs.ReadAsync(first, 0, 1).ConfigureAwait(false) != 1) return;
                if (first[0] == 5) up = await HandleSocks(l.P, cs).ConfigureAwait(false);
                else up = await HandleHttp(l.P, cs, first[0]).ConfigureAwait(false);
                if (up == null) return;
                await Pump(c, up).ConfigureAwait(false);
            }
            catch { }
            finally
            {
                try { up?.Close(); } catch { }
                try { c.Close(); } catch { }
                Interlocked.Decrement(ref l.Active);
            }
        }

        static async Task<byte[]> ReadN(Stream s, int n)
        {
            var b = new byte[n];
            int got = 0;
            while (got < n)
            {
                int r = await s.ReadAsync(b, got, n - got).ConfigureAwait(false);
                if (r <= 0) throw new IOException("bağlantı kapandı");
                got += r;
            }
            return b;
        }

        /// <summary>Üst proxy üzerinden host:port'a tünel açar (eşzamanlı kodu iş parçacığında çalıştırır).</summary>
        static Task<(Stream S, TcpClient C)> Upstream(ProxyEntry p, string host, int port) => Task.Run(() =>
        {
            var s = ProxyTools.OpenTunnel(p, host, port, 15000, Stopwatch.StartNew(), null, out var client);
            client.ReceiveTimeout = client.SendTimeout = 0;
            client.NoDelay = true;
            return (s, client);
        });

        static async Task<TcpClient> HandleSocks(ProxyEntry p, Stream cs)
        {
            int n = (await ReadN(cs, 1).ConfigureAwait(false))[0];
            await ReadN(cs, n).ConfigureAwait(false);
            await cs.WriteAsync(new byte[] { 5, 0 }, 0, 2).ConfigureAwait(false);   // kimlik doğrulama yok (yalnızca yerel)
            var req = await ReadN(cs, 4).ConfigureAwait(false);
            string host;
            switch (req[3])
            {
                case 1: host = new IPAddress(await ReadN(cs, 4).ConfigureAwait(false)).ToString(); break;
                case 4: host = new IPAddress(await ReadN(cs, 16).ConfigureAwait(false)).ToString(); break;
                default:
                    int len = (await ReadN(cs, 1).ConfigureAwait(false))[0];
                    host = Encoding.ASCII.GetString(await ReadN(cs, len).ConfigureAwait(false));
                    break;
            }
            var pb = await ReadN(cs, 2).ConfigureAwait(false);
            int port = (pb[0] << 8) | pb[1];
            if (req[1] != 1)
            {
                await cs.WriteAsync(new byte[] { 5, 7, 0, 1, 0, 0, 0, 0, 0, 0 }, 0, 10).ConfigureAwait(false);
                return null;
            }
            try
            {
                var (s, c) = await Upstream(p, host, port).ConfigureAwait(false);
                await cs.WriteAsync(new byte[] { 5, 0, 0, 1, 0, 0, 0, 0, 0, 0 }, 0, 10).ConfigureAwait(false);
                return c;
            }
            catch
            {
                await cs.WriteAsync(new byte[] { 5, 5, 0, 1, 0, 0, 0, 0, 0, 0 }, 0, 10).ConfigureAwait(false);
                return null;
            }
        }

        static async Task<TcpClient> HandleHttp(ProxyEntry p, Stream cs, byte firstByte)
        {
            // istek başlıklarını oku
            var buf = new List<byte> { firstByte };
            var tmp = new byte[4096];
            int headEnd = -1;
            while (headEnd < 0)
            {
                if (buf.Count > 65536) return null;
                int r = await cs.ReadAsync(tmp, 0, tmp.Length).ConfigureAwait(false);
                if (r <= 0) return null;
                buf.AddRange(tmp.Take(r));
                for (int i = Math.Max(0, buf.Count - r - 3); i + 3 < buf.Count; i++)
                    if (buf[i] == '\r' && buf[i + 1] == '\n' && buf[i + 2] == '\r' && buf[i + 3] == '\n') { headEnd = i + 4; break; }
            }
            string head = Encoding.ASCII.GetString(buf.Take(headEnd).ToArray());
            byte[] rest = buf.Skip(headEnd).ToArray();
            var lines = head.Split(new[] { "\r\n" }, StringSplitOptions.None).ToList();
            var parts = lines[0].Split(' ');
            if (parts.Length < 3) return null;
            string method = parts[0], target = parts[1], version = parts[2];

            async Task Reply(string status)
            {
                var b = Encoding.ASCII.GetBytes(version + " " + status + "\r\nConnection: close\r\nContent-Length: 0\r\n\r\n");
                try { await cs.WriteAsync(b, 0, b.Length).ConfigureAwait(false); } catch { }
            }

            if (method.Equals("CONNECT", StringComparison.OrdinalIgnoreCase))
            {
                int colon = target.LastIndexOf(':');
                string host = colon > 0 ? target.Substring(0, colon).Trim('[', ']') : target;
                int port = colon > 0 && int.TryParse(target.Substring(colon + 1), out int pp) ? pp : 443;
                try
                {
                    var (s, c) = await Upstream(p, host, port).ConfigureAwait(false);
                    var ok = Encoding.ASCII.GetBytes(version + " 200 Connection established\r\n\r\n");
                    await cs.WriteAsync(ok, 0, ok.Length).ConfigureAwait(false);
                    if (rest.Length > 0) await s.WriteAsync(rest, 0, rest.Length).ConfigureAwait(false);
                    return c;
                }
                catch (Exception ex)
                {
                    await Reply(ex is ProxyFail pf && pf.Code == "DK-806" ? "407 Upstream Auth Failed" : "502 Bad Gateway").ConfigureAwait(false);
                    return null;
                }
            }

            // Şifresiz HTTP isteği (GET http://site/yol HTTP/1.1)
            if (!Uri.TryCreate(target, UriKind.Absolute, out var uri) || uri.Scheme != "http") { await Reply("400 Bad Request").ConfigureAwait(false); return null; }
            var headers = lines.Skip(1).Where(h => h.Length > 0 &&
                !h.StartsWith("Proxy-Connection:", StringComparison.OrdinalIgnoreCase) &&
                !h.StartsWith("Proxy-Authorization:", StringComparison.OrdinalIgnoreCase) &&
                !h.StartsWith("Connection:", StringComparison.OrdinalIgnoreCase)).ToList();
            headers.Add("Connection: close");
            try
            {
                TcpClient c;
                Stream s;
                string firstLine;
                if (p.Kind == ProxyKind.Http)
                {
                    // HTTP üst proxy: isteği olduğu gibi ilet, kimlik bilgisini ekle
                    c = new TcpClient();
                    var t = c.ConnectAsync(p.Host, p.Port);
                    if (await Task.WhenAny(t, Task.Delay(15000)).ConfigureAwait(false) != t || !c.Connected) throw new IOException("üst proxy yanıt vermedi");
                    s = c.GetStream();
                    firstLine = method + " " + target + " " + version;
                    if (p.HasAuth) headers.Add("Proxy-Authorization: Basic " + Convert.ToBase64String(Encoding.UTF8.GetBytes(p.User + ":" + p.Password)));
                }
                else
                {
                    (s, c) = await Upstream(p, uri.Host, uri.Port).ConfigureAwait(false);
                    firstLine = method + " " + uri.PathAndQuery + " " + version;
                }
                var outHead = Encoding.ASCII.GetBytes(firstLine + "\r\n" + string.Join("\r\n", headers) + "\r\n\r\n");
                await s.WriteAsync(outHead, 0, outHead.Length).ConfigureAwait(false);
                if (rest.Length > 0) await s.WriteAsync(rest, 0, rest.Length).ConfigureAwait(false);
                return c;
            }
            catch
            {
                await Reply("502 Bad Gateway").ConfigureAwait(false);
                return null;
            }
        }

        /// <summary>İki bağlantı arasında veriyi iki yönlü aktarır.</summary>
        static async Task Pump(TcpClient a, TcpClient b)
        {
            var sa = a.GetStream();
            var sb = b.GetStream();
            async Task Copy(Stream from, Stream to, TcpClient toClient)
            {
                try { await from.CopyToAsync(to, 32768).ConfigureAwait(false); } catch { }
                try { toClient.Client.Shutdown(SocketShutdown.Send); } catch { }
            }
            var t1 = Copy(sa, sb, b);
            var t2 = Copy(sb, sa, a);
            var first = await Task.WhenAny(t1, t2).ConfigureAwait(false);
            // diğer yön en fazla 30 sn daha açık kalır
            await Task.WhenAny(first == t1 ? t2 : t1, Task.Delay(30000)).ConfigureAwait(false);
        }
    }
}
