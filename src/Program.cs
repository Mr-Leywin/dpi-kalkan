using System;
using System.Collections.Generic;
using System.Drawing;
using System.IO;
using System.Linq;
using System.Net;
using System.Reflection;
using System.Runtime.Versioning;
using System.Threading;
using System.Windows.Forms;

[assembly: AssemblyTitle("DPI Kalkan")]
[assembly: AssemblyDescription("GoodbyeDPI kontrol paneli ve internet erişim kalkanı")]
[assembly: AssemblyProduct("DPI Kalkan")]
[assembly: AssemblyCopyright("GoodbyeDPI © ValdikSS · WinDivert © basil00")]
[assembly: AssemblyVersion("2.2.0.0")]
[assembly: AssemblyFileVersion("2.2.0.0")]
[assembly: TargetFramework(".NETFramework,Version=v4.8", FrameworkDisplayName = ".NET Framework 4.8")]

namespace DPIKalkan
{
    static class Program
    {
        [STAThread]
        static int Main(string[] args)
        {
#if PREVIEW
            App.Preview = true;
            int mi = Array.IndexOf(args, "--make-icon");
            if (mi >= 0) { IconArt.WriteIco(args[mi + 1], new[] { 16, 20, 24, 32, 40, 48, 64, 256 }); return 0; }
            int ei = Array.IndexOf(args, "--export-scripts");
            if (ei >= 0) { Scripts.ExportAll(args[ei + 1]); return 0; }
            int gi = Array.IndexOf(args, "--export-guide");
            if (gi >= 0) { Guide.Build(args[gi + 1], args[gi + 2]); return 0; }
            int ti = Array.IndexOf(args, "--selftest");
            if (ti >= 0) { File.WriteAllText(args[ti + 1], SelfTest.Run(args.Skip(ti + 2).ToArray()), System.Text.Encoding.UTF8); return 0; }
            App.DataDir = Path.Combine(Path.GetTempPath(), "dpikalkan_preview");
            try { Directory.Delete(App.DataDir, true); } catch { }
#endif
            bool createdNew;
            var mutex = new Mutex(true, @"Local\DPIKalkan_SingleInstance", out createdNew);
            var showEvent = new EventWaitHandle(false, EventResetMode.AutoReset, @"Local\DPIKalkan_Show");
            if (!createdNew && !App.Preview)
            {
                showEvent.Set();   // açık olan kopyayı öne getir
                return 0;
            }

            Application.EnableVisualStyles();
            Application.SetCompatibleTextRenderingDefault(false);
            using (var g = Graphics.FromHwnd(IntPtr.Zero)) Theme.Scale = g.DpiX / 96f;

            try { ServicePointManager.SecurityProtocol = SecurityProtocolType.SystemDefault; } catch { }
            ServicePointManager.DefaultConnectionLimit = 32;
            ServicePointManager.Expect100Continue = false;
            ServicePointManager.DnsRefreshTimeout = 0;   // .NET DNS önbelleği kapalı: her test güncel DNS yanıtını kullanır

            Application.ThreadException += (s, e) => App.Log("Beklenmeyen hata: " + e.Exception, LogLevel.Error);
            AppDomain.CurrentDomain.UnhandledException += (s, e) => App.Log("Kritik hata: " + e.ExceptionObject, LogLevel.Error);

            App.Init();
            Theme.Apply(Palettes.Resolve(App.Settings));

            if (!App.Preview && !App.IsAdmin())
            {
                MessageBox.Show("DPI Kalkan'ın ağ sürücüsünü yükleyebilmesi için yönetici olarak çalıştırılması gerekir (DK-101).\n\n" +
                                "Uygulamaya sağ tıklayıp 'Yönetici olarak çalıştır'ı seçin.",
                    App.Name, MessageBoxButtons.OK, MessageBoxIcon.Warning);
                return 1;
            }

            bool tray = args.Any(a => a.Equals("--tray", StringComparison.OrdinalIgnoreCase));
            bool scanAll = args.Any(a => a.Equals("--scan-all", StringComparison.OrdinalIgnoreCase));
            bool scan = scanAll || args.Any(a => a.Equals("--scan", StringComparison.OrdinalIgnoreCase));
            var form = new MainForm();
            App.UI = SynchronizationContext.Current;

#if PREVIEW
            int si = Array.IndexOf(args, "--screenshot");
            if (si >= 0) { Preview.Run(form, args[si + 1]); return 0; }
#endif

            var waiter = new Thread(() =>
            {
                while (showEvent.WaitOne())
                    try { form.BeginInvoke(new Action(form.ShowFromTray)); } catch { }
            }) { IsBackground = true };
            waiter.Start();

            if (tray && !scan) { var h = form.Handle; } else form.Show();
            form.BeginInvoke(new Action(() => form.AfterStartup(tray, scan, scanAll)));
            Application.Run(new ApplicationContext());
            GC.KeepAlive(mutex);
            return 0;
        }
    }

#if PREVIEW
    /// <summary>Yönetici gerektirmeyen birim testleri (derleme sırasında elle çalıştırılır).</summary>
    static class SelfTest
    {
        public static string Run(string[] proxies)
        {
            var sb = new System.Text.StringBuilder();
            void Check(string name, bool ok, string detail = "") => sb.AppendLine((ok ? "OK   " : "FAIL ") + name + (detail.Length > 0 ? "  → " + detail : ""));

            // proxy ayrıştırma
            foreach (var (text, kind, host, port, user) in new[]
            {
                ("185.12.34.56:8080", ProxyKind.Http, "185.12.34.56", 8080, ""),
                ("socks5://ali:gizli@1.2.3.4:1080", ProxyKind.Socks5, "1.2.3.4", 1080, "ali"),
                ("http://proxy.example.com:3128/", ProxyKind.Http, "proxy.example.com", 3128, ""),
                ("5.6.7.8:9000:veli:sifre", ProxyKind.Http, "5.6.7.8", 9000, "veli"),
                ("socks5h://[2001:db8::1]:1080", ProxyKind.Socks5, "2001:db8::1", 1080, ""),
            })
            {
                bool ok = ProxyTools.TryParse(text, ProxyKind.Http, out var p, out var err);
                Check("parse " + text, ok && p.Kind == kind && p.Host == host && p.Port == port && p.User == user,
                      ok ? p.Kind + " " + p.Host + " " + p.Port + " " + p.User + (p.HasAuth ? " pass=" + p.Password : "") : err);
            }
            Check("parse hatalı port reddedilir", !ProxyTools.TryParse("1.2.3.4:99999", ProxyKind.Http, out _, out _));
            Check("parse boş reddedilir", !ProxyTools.TryParse("  ", ProxyKind.Http, out _, out _));

            // profil kodu
            var src = Profiles.BuiltIns.First(x => x.Id == "tr-alt").CloneAsCustom();
            string code = ProfileCode.ToCode(src);
            bool back = ProfileCode.TryFromCode("Arkadaşım şunu yolladı: " + code + " deneyin", out var dst, out var perr);
            Check("profil kodu gidiş-dönüş", back && dst.BuildArgs(null) == src.BuildArgs(null) && dst.Id != src.Id, code.Length + " karakter " + perr);
            Check("bozuk profil kodu reddedilir", !ProfileCode.TryFromCode("DKP1:abc", out _, out _));

            // DPI imzası: -9 + farklı DNS aynı, -5 farklı
            string s9a = Scanner.DpiSignature(Profiles.BuiltIns.First(x => x.Id == "tr-onerilen"));
            string s9b = Scanner.DpiSignature(Profiles.BuiltIns.First(x => x.Id == "tr-quad9"));
            string s5 = Scanner.DpiSignature(Profiles.BuiltIns.First(x => x.Id == "tr-autottl"));
            Check("DPI imzası: -9 Yandex == -9 Quad9", s9a == s9b, "'" + s9a + "'");
            Check("DPI imzası: -9 != -5", s9a != s5, "'" + s5 + "'");

            // program bazlı proxy planları
            foreach (var (name, path) in AppProxy.Installed())
                sb.AppendLine("     bulundu: " + name + " → " + AppProxy.Detect(path) + "  (" + path + ")");
            var auth = new ProxyEntry { Kind = ProxyKind.Socks5, Host = "1.2.3.4", Port = 1080, User = "u", Password = "p", LocalPort = 18950 };
            var open = new ProxyEntry { Kind = ProxyKind.Http, Host = "5.6.7.8", Port = 3128 };
            var chrome = new ProxyApp { Name = "Chrome", ExePath = @"C:\X\chrome.exe", Mode = AppProxyMode.Chromium };
            var plan1 = AppProxy.Plan(chrome, open);
            Check("plan Chromium doğrudan", plan1.Args.Contains("--proxy-server=\"http://5.6.7.8:3128\"") && plan1.Args.Contains("--user-data-dir"), plan1.Args);
            var plan2 = AppProxy.Plan(chrome, auth);
            Check("plan Chromium şifreli → köprü", plan2.Args.Contains("http://127.0.0.1:18950"), plan2.Args);
            var env = new ProxyApp { Name = "Araç", ExePath = @"C:\X\tool.exe", Mode = AppProxyMode.Env };
            var plan3 = AppProxy.Plan(env, open);
            Check("plan ortam değişkeni", plan3.Target.EndsWith("cmd.exe") && plan3.Args.Contains("HTTPS_PROXY=http://5.6.7.8:3128") && plan3.WindowStyle == 7, plan3.Args.Substring(0, Math.Min(90, plan3.Args.Length)) + "…");
            var dc = new ProxyApp { Name = "Discord", ExePath = @"C:\Users\x\AppData\Local\Discord\Update.exe", Mode = AppProxyMode.Discord };
            var plan4 = AppProxy.Plan(dc, open);
            Check("plan Discord", plan4.Args == "--processStart Discord.exe --process-start-args \"--proxy-server=http://5.6.7.8:3128\"", plan4.Args);
            Check("köprü gereksinimi", AppProxy.NeedsBridge(chrome, auth) && !AppProxy.NeedsBridge(chrome, open) &&
                                       AppProxy.NeedsBridge(env, new ProxyEntry { Kind = ProxyKind.Socks5, Host = "h", Port = 1 }));
            Check("proxy listesi ayrıştırma", ProxyTools.ParseList("# yorum\n1.1.1.1:80\n\nbozuk\nsocks5://2.2.2.2:1080\n1.1.1.1:80", ProxyKind.Http, out int bad).Count == 2 && bad == 1);

            // canlı proxy testleri (isteğe bağlı): doğrudan ve yerel köprü üzerinden
            if (proxies.Contains("--speed"))
            {
                var sr = SpeedTest.Run(null, System.Threading.CancellationToken.None);
                Check("hız testi", sr.Error.Length == 0 && sr.PingMs > 0 && sr.DownMbps > 0 && sr.UpMbps > 0,
                      "gecikme " + sr.PingMs + " ms (sapma " + sr.JitterMs + "), indirme " + sr.DownMbps.ToString("0.0") + " Mbit/s (" + sr.DownBytes / 1048576 + " MB), yükleme " +
                      sr.UpMbps.ToString("0.0") + " Mbit/s " + sr.Error);
            }
            int bport = 18960;
            foreach (var u in proxies.Where(x => !x.StartsWith("--")))
            {
                if (!ProxyTools.TryParse(u, ProxyKind.Http, out var p, out var err)) { Check("proxy " + u, false, err); continue; }
                var r = ProxyTools.Test(p);
                Check("proxy test " + u, r.Ok, r.Ok ? r.TotalMs + " ms, çıkış " + r.ExitIp + " " + r.Country + (r.KindFixed ? " (tür düzeltildi → " + p.KindLabel + ")" : "") : r.Code + " " + r.Message);
                if (!r.Ok) continue;
                p.LocalPort = bport++;
                if (!ProxyBridge.Ensure(p, out var berr)) { Check("köprü aç " + u, false, berr); continue; }
                var viaHttp = new ProxyEntry { Kind = ProxyKind.Http, Host = "127.0.0.1", Port = p.LocalPort };
                var viaSocks = new ProxyEntry { Kind = ProxyKind.Socks5, Host = "127.0.0.1", Port = p.LocalPort };
                var r1 = ProxyTools.Test(viaHttp, exitInfo: false, log: false);
                var r2 = ProxyTools.Test(viaSocks, exitInfo: false, log: false);
                Check("köprü HTTP CONNECT → " + u, r1.Ok, r1.Ok ? r1.TotalMs + " ms" : r1.Code + " " + r1.Message);
                Check("köprü SOCKS5 → " + u, r2.Ok, r2.Ok ? r2.TotalMs + " ms" : r2.Code + " " + r2.Message);
                try
                {
                    var req = (HttpWebRequest)WebRequest.Create("http://example.com/");
                    req.Proxy = new WebProxy("http://127.0.0.1:" + p.LocalPort);
                    req.Timeout = 10000;
                    using (var resp = (HttpWebResponse)req.GetResponse())
                    using (var rd = new StreamReader(resp.GetResponseStream()))
                        Check("köprü şifresiz HTTP (example.com) → " + u, rd.ReadToEnd().Contains("Example Domain"), "HTTP " + (int)resp.StatusCode);
                }
                catch (Exception ex) { Check("köprü şifresiz HTTP (example.com) → " + u, false, ex.Message); }
                ProxyBridge.Stop(p.Id);
            }
            return sb.ToString();
        }
    }

    static class Preview
    {
        [System.Runtime.InteropServices.DllImport("user32.dll")]
        static extern bool PrintWindow(IntPtr hwnd, IntPtr hdc, uint flags);

        static void Pump()
        {
            for (int i = 0; i < 8; i++) { Application.DoEvents(); Thread.Sleep(30); }
        }

        static void Shot(Form f, string path)
        {
            Pump();
            using (var bmp = new Bitmap(f.Width, f.Height))
            {
                using (var g = Graphics.FromImage(bmp))
                {
                    IntPtr hdc = g.GetHdc();
                    PrintWindow(f.Handle, hdc, 2);
                    g.ReleaseHdc(hdc);
                }
                bmp.Save(path);
            }
        }

        static void Theme(MainForm form, string id, UiLevel level)
        {
            App.Settings.ThemeId = id;
            App.Settings.UiLevel = level;
            DPIKalkan.Theme.Apply(Palettes.Resolve(App.Settings));
            form.Rebuild();
            Pump();
        }

        static void Fake()
        {
            App.LastTest = AppSettings.DefaultTestHosts.Select((h, i) => new HostResult
            { Host = h, Ok = true, Ms = 180 + i * 37, Ip = "162.159.13" + i + ".23" + i, Detail = "Erişilebilir (HTTP 200)" }).ToList();
            App.LastTestTime = DateTime.Now;
            App.LastDnsOk = true;
        }

        public static void Run(MainForm form, string dir)
        {
            Directory.CreateDirectory(dir);
            form.StartPosition = FormStartPosition.Manual;
            form.Location = new Point(-5000, 0);
            form.Show();
            Pump();

            var p = App.SelectedProfile;
            Fake();
            App.Log("DPI Kalkan " + App.Version + " başlatıldı.", LogLevel.Info);
            App.Log("Komut: goodbyedpi.exe " + p.BuildArgs(null), LogLevel.Debug);
            App.Log("GoodbyeDPI v0.2.3rc3: Passive DPI blocker and Active DPI circumvention utility", LogLevel.Output);
            App.Log("Filter activated, GoodbyeDPI is now running!", LogLevel.Output);
            App.Log("Koruma aktif: " + p.Name, LogLevel.Success);
            App.Log("Bağlantı testi: 6/6 site erişilebilir, DNS doğru.", LogLevel.Success);
            App.Log("[DK-402] DNS sunucusu yanıt vermiyor: 77.88.8.8:1253", LogLevel.Warn);
            App.Engine.DebugSet(EngineState.Running, p);

            // --- Basit arayüz, sistem teması
            foreach (var (themeId, tag) in new[] { ("system", "sistem"), ("light", "acik"), ("dracula", "dracula"), ("solarized-light", "solarized"), ("contrast", "kontrast") })
            {
                Theme(form, themeId, UiLevel.Basit);
                App.RaiseStatus();
                form.Navigate("home");
                Shot(form, Path.Combine(dir, "a_basit_home_" + tag + ".png"));
            }
            Theme(form, "system", UiLevel.Basit);
            App.Problems.Report("DK-901");
            App.RaiseStatus();
            form.Navigate("home"); Shot(form, Path.Combine(dir, "a_basit_home_banner.png"));
            App.Problems.Clear("DK-901");

            App.Engine.DebugSet(EngineState.Error, p, "GoodbyeDPI başlatılamadı. WinDivert sürücüsü antivirüs programı tarafından engellendi.");
            App.Problems.Report("DK-204", "test");
            App.RaiseStatus();
            form.Navigate("home"); Shot(form, Path.Combine(dir, "a_basit_home_error.png"));
            var tp = form.Go<TroublePage>("trouble");
            Shot(form, Path.Combine(dir, "a_basit_trouble.png"));
            tp.ShowCode("DK-204");
            Shot(form, Path.Combine(dir, "a_basit_trouble_codes.png"));
            App.Problems.Clear("DK-204");
            App.Engine.DebugSet(EngineState.Running, p);
            App.RaiseStatus();
            foreach (var key in new[] { "appearance", "settings", "about" })
            {
                form.Navigate(key);
                Shot(form, Path.Combine(dir, "a_basit_" + key + ".png"));
            }

            // --- Orta arayüz
            Theme(form, "system", UiLevel.Orta);
            App.RaiseStatus();
            App.Settings.Proxies.Add(new ProxyEntry { Name = "Almanya SOCKS5", Kind = ProxyKind.Socks5, Host = "203.0.113.10", Port = 1080, LastOk = true, LastTest = DateTime.Now, LastResult = "Çalışıyor · 84 ms · DE" });
            App.Settings.Proxies.Add(new ProxyEntry { Name = "İş yeri HTTP", Kind = ProxyKind.Http, Host = "198.51.100.7", Port = 3128, User = "ali", LastOk = false, LastTest = DateTime.Now, LastResult = "DK-805 Proxy sunucusu yanıt vermedi." });
            ProxyBridge.AssignPorts(App.Settings.Proxies);
            foreach (var (nm, path) in AppProxy.Installed().Take(3))
                App.Settings.ProxyApps.Add(new ProxyApp { Name = nm, ExePath = path, Mode = AppProxy.Detect(path), ProxyId = App.Settings.Proxies[App.Settings.ProxyApps.Count % 2].Id });
            var rnd = new Random(7);
            for (int i = 0; i < 46; i++)
            {
                bool ok = i != 30 && i != 31;
                History.Add(new HistoryEntry { Time = DateTime.Now.AddMinutes(-15 * (46 - i)), Kind = "test", Profile = i < 31 ? "Türkiye – Önerilen" : "Otomatik TTL (-5)",
                    Ok = ok ? 6 : i == 30 ? 1 : 0, Total = 6, Ms = ok ? 120 + rnd.Next(0, 160) : 0, DnsOk = true, Running = i != 12, Note = ok ? "" : "discord.com DK-501" });
                if (i == 31) History.Add(new HistoryEntry { Time = DateTime.Now.AddMinutes(-15 * (46 - i) + 1), Kind = "onarım", Profile = "Otomatik TTL (-5)", Ok = 1, Total = 1, Running = true, Note = "Yeni yöntem: Otomatik TTL (-5)" });
            }
            foreach (var key in new[] { "home", "profiles", "net", "dns", "sites", "proxy", "settings" })
            {
                form.Navigate(key);
                Shot(form, Path.Combine(dir, "b_orta_" + key + ".png"));
            }
            var np = form.Go<NetPage>("net");
            np.ShowTab(1, false); Shot(form, Path.Combine(dir, "b_orta_net_site.png"));
            np.ShowTab(2, false); Shot(form, Path.Combine(dir, "b_orta_net_dns.png"));
            np.ShowTab(3, false); Shot(form, Path.Combine(dir, "b_orta_net_speed.png"));
            np.ShowTab(4, false); Shot(form, Path.Combine(dir, "b_orta_net_history.png"));
            form.Go<ProxyPage>("proxy").ShowApps(); Shot(form, Path.Combine(dir, "b_orta_proxy_apps.png"));

            // --- Detaylı arayüz, açık tema
            Theme(form, "light", UiLevel.Detayli);
            App.RaiseStatus();
            foreach (var key in new[] { "home", "profiles", "log", "settings", "net" })
            {
                form.Navigate(key);
                Shot(form, Path.Combine(dir, "c_detayli_" + key + ".png"));
            }

            // --- pencereler
            Theme(form, "system", UiLevel.Orta);
            var ed = new ProfileEditor(Profiles.BuiltIns[4].CloneAsCustom());
            ed.StartPosition = FormStartPosition.Manual;
            ed.Location = new Point(-5000, 0);
            ed.Show();
            Shot(ed, Path.Combine(dir, "d_editor.png"));
            ed.Close();

            // simgeler + glif tablosu
            var glyphs = typeof(DPIKalkan.Theme).GetFields(BindingFlags.Public | BindingFlags.Static)
                .Where(f => f.IsLiteral && f.Name.StartsWith("G")).ToList();
            using (var bmp = new Bitmap(900, 460))
            using (var g = Graphics.FromImage(bmp))
            {
                g.Clear(Color.White);
                int x = 10;
                foreach (EngineState s in Enum.GetValues(typeof(EngineState)))
                {
                    foreach (int sz in new[] { 16, 24, 32, 64 })
                    {
                        using (var ic = IconArt.StateIcon(s, sz)) g.DrawIcon(ic, new Rectangle(x, 10, sz, sz));
                        x += sz + 8;
                    }
                    x += 10;
                }
                int gx = 10, gy = 110;
                using (var f = new Font(DPIKalkan.Theme.IconFontName, 16f))
                    foreach (var fi in glyphs)
                    {
                        TextRenderer.DrawText(g, (string)fi.GetValue(null), f, new Point(gx, gy), Color.Black);
                        TextRenderer.DrawText(g, fi.Name, new Font("Segoe UI", 8f), new Point(gx + 30, gy + 6), Color.DimGray);
                        gx += 125;
                        if (gx > 800) { gx = 10; gy += 40; }
                    }
                bmp.Save(Path.Combine(dir, "e_icons.png"));
            }
            form.Dispose();
        }
    }
#endif
}
