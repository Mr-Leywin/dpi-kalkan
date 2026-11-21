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

// Exe sürüm bilgisi (Windows'ta Özellikler → Ayrıntılar). Yayıncı yalnızca bu projedir; GoodbyeDPI (ValdikSS) ve
// WinDivert (basil00) atıfları lisanslar klasöründe, Hakkında sayfasında ve README'de yer alır.
// Sürüm tek yerden gelir: üç değer aynı olmalı (build.ps1 denetler ve app.manifest'i buna eşitler).
[assembly: AssemblyTitle("DPI Kalkan")]                                     // FileDescription
[assembly: AssemblyDescription("GoodbyeDPI kontrol paneli ve internet erişim kalkanı")]   // Comments
[assembly: AssemblyCompany(DPIKalkan.App.Publisher)]                       // CompanyName: Osman Barış MEDİNE (Mr Leywin)
[assembly: AssemblyProduct("DPI Kalkan")]                                   // ProductName
[assembly: AssemblyCopyright(DPIKalkan.App.Copyright)]                     // LegalCopyright: Copyright (c) 2026 Osman Barış MEDİNE (Mr Leywin)
[assembly: AssemblyTrademark("")]
[assembly: System.Resources.NeutralResourcesLanguage("tr-TR")]
[assembly: AssemblyVersion("2.4.0.0")]
[assembly: AssemblyFileVersion("2.4.0.0")]
[assembly: AssemblyInformationalVersion("2.4.0")]                          // ProductVersion
[assembly: TargetFramework(".NETFramework,Version=v4.8", FrameworkDisplayName = ".NET Framework 4.8")]
// P/Invoke ile çağrılan tüm DLL'ler (kernel32, user32, dnsapi, dwmapi, uxtheme, wininet) yalnızca System32'den yüklenir
[assembly: System.Runtime.InteropServices.DefaultDllImportSearchPaths(System.Runtime.InteropServices.DllImportSearchPath.System32)]

namespace DPIKalkan
{
    static class Program
    {
        [STAThread]
        static int Main(string[] args)
        {
            Native.HardenDllSearch();
            // Güncelleme kurulumu: yeni sürümün exe'si tarafından, tek kopya kilidi ve yönetici denetiminden önce çalışır
            int ui = Array.IndexOf(args, "--apply-update");
            if (ui >= 0)
            {
                if (args.Length >= ui + 5 && int.TryParse(args[ui + 3], out int oldPid))
                    return Updater.Apply(args[ui + 1], args[ui + 2], oldPid, args[ui + 4], args.Length > ui + 5 && args[ui + 5] == "1");
                return 2;
            }
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
            // Temiz kaldırma (kurulum paketinin kaldırıcısı da bunu çağırır): çalışan kopyayı kapatır, sistem değişikliklerini geri alır
            if (args.Contains("--uninstall") && !App.Preview)
                return Uninstaller.RunCli(quiet: args.Contains("--quiet"));
            // Kurulum paketi dosyaları değiştirmeden önce: bu klasördeki uygulamayı, GoodbyeDPI'ı ve hizmeti durdurur (ayarlara dokunmaz)
            if (args.Contains("--prepare-upgrade") && !App.Preview)
                return Uninstaller.PrepareUpgrade();

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

            Application.ThreadException += (s, e) => { App.Log("Beklenmeyen hata: " + e.Exception, LogLevel.Error); CrashMarker.Record(e.Exception, fatal: false); };
            AppDomain.CurrentDomain.UnhandledException += (s, e) => { App.Log("Kritik hata: " + e.ExceptionObject, LogLevel.Error); CrashMarker.Record(e.ExceptionObject as Exception, fatal: true); };

            App.Init();
            Theme.Apply(Palettes.Resolve(App.Settings));

            if (!App.Preview && !App.IsAdmin())
            {
                MessageBox.Show("DPI Kalkan'ın ağ sürücüsünü yükleyebilmesi için yönetici olarak çalıştırılması gerekir (DK-101).\n\n" +
                                "Uygulamaya sağ tıklayıp 'Yönetici olarak çalıştır'ı seçin.",
                    App.Name, MessageBoxButtons.OK, MessageBoxIcon.Warning);
                return 1;
            }

            CrashMarker.Begin();   // önceki çalışma düzgün kapanmadıysa AfterStartup bildirir
            bool tray = args.Any(a => a.Equals("--tray", StringComparison.OrdinalIgnoreCase));
            bool scanAll = args.Any(a => a.Equals("--scan-all", StringComparison.OrdinalIgnoreCase));
            bool scan = scanAll || args.Any(a => a.Equals("--scan", StringComparison.OrdinalIgnoreCase));
            int ufi = Array.IndexOf(args, "--updated");
            string updatedFrom = ufi >= 0 && args.Length > ufi + 1 ? args[ufi + 1] : null;
            bool updateFailed = args.Contains("--update-failed");
            bool resume = args.Contains("--resume");
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
            form.BeginInvoke(new Action(() => form.AfterStartup(tray, scan, scanAll, updatedFrom, updateFailed, resume)));
            Application.Run(new ApplicationContext());
            CrashMarker.End();
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
            // güncelleme: GitHub yanıtının ayrıştırılması
            string relJson = "{\"tag_name\":\"v9.8.7\",\"name\":\"DPI Kalkan 9.8.7\",\"draft\":false,\"prerelease\":false,\"body\":\"## Yenilikler\\n- a\",\"html_url\":\"https://github.com/x/y\"," +
                             "\"published_at\":\"2026-11-20T10:00:00Z\",\"assets\":[{\"name\":\"DPI-Kalkan-9.8.7.zip.sha256\",\"browser_download_url\":\"https://github.com/a\",\"size\":80,\"digest\":null}," +
                             "{\"name\":\"DPI-Kalkan-9.8.7.zip\",\"browser_download_url\":\"https://github.com/x/y/releases/download/v9.8.7/DPI-Kalkan-9.8.7.zip\",\"size\":564400," +
                             "\"digest\":\"sha256:086556D50BFC1DD3D1B03DF0CADFDE01FA172343BBFAE6224D66A756CA93DB51\"}]}";
            var ui1 = Updater.Parse(relJson);
            Check("güncelleme: sürüm ve paket ayrıştırma", ui1 != null && ui1.Version == new Version(9, 8, 7) && ui1.AssetName == "DPI-Kalkan-9.8.7.zip" &&
                  ui1.Sha256 == "086556d50bfc1dd3d1b03df0cadfde01fa172343bbfae6224d66a756ca93db51" && Updater.IsNewer(ui1), ui1?.Version + " " + ui1?.AssetName);
            var ui2 = Updater.Parse(relJson.Replace("\"digest\":\"sha256:086556D50BFC1DD3D1B03DF0CADFDE01FA172343BBFAE6224D66A756CA93DB51\"", "\"digest\":null")
                                           .Replace("- a", "**SHA-256** (DPI-Kalkan-9.8.7.zip): `" + new string('a', 64) + "`"));
            Check("güncelleme: özet sürüm notundan okunur", ui2 != null && ui2.Sha256 == new string('a', 64));
            Check("güncelleme: ön sürüm yok sayılır", Updater.Parse(relJson.Replace("\"prerelease\":false", "\"prerelease\":true")) == null);
            Check("güncelleme: eski sürüm yeni sayılmaz", !Updater.IsNewer(Updater.Parse(relJson.Replace("9.8.7", "1.0.0"))));

            // destek paketi: kişisel bilgi gizleme
            string raw = @"C:\Users\ahmet\AppData\Local\Discord\Update.exe çıkış IP 203.0.113.45 TR · DNS 77.88.8.8:1253 · discord 162.159.135.232 · modem 192.168.1.1 · v6 2a00:1d34:abcd::5 fe80::1 · ali@example.com · 17:46:13";
            string red = Support.Redact(raw);
            Check("gizleme: kullanıcı klasörü", red.Contains(@"C:\Users\<kullanıcı>\AppData") && !red.Contains("ahmet"), red);
            Check("gizleme: genel IP gizlenir, DNS/Discord kalır", red.Contains("<ip>") && !red.Contains("203.0.113") && red.Contains("77.88.8.8") && red.Contains("162.159.135.232"));
            Check("gizleme: yerel IP, IPv6, e-posta", red.Contains("<yerel-ip>") && red.Contains("<ipv6>") && red.Contains("fe80::1") && red.Contains("<e-posta>") && red.Contains("17:46:13"));

            // güncelleme kurulumu (dosya işlemleri; hizmete dokunmaz)
            {
                string t = Path.Combine(Path.GetTempPath(), "dpk-apply-test-" + Guid.NewGuid().ToString("N").Substring(0, 6));
                string srcDir = Path.Combine(t, "yeni"), dstDir = Path.Combine(t, "kurulu");
                Directory.CreateDirectory(Path.Combine(srcDir, "bin", "x86_64"));
                Directory.CreateDirectory(Path.Combine(dstDir, "veri"));
                Directory.CreateDirectory(Path.Combine(dstDir, "bin", "x86_64"));
                File.WriteAllText(Path.Combine(srcDir, "DPIKalkan.exe"), "yeni");
                File.WriteAllText(Path.Combine(srcDir, "bin", "x86_64", "goodbyedpi.exe"), "yeni-gdpi");
                File.WriteAllText(Path.Combine(dstDir, "DPIKalkan.exe"), "eski");
                File.WriteAllText(Path.Combine(dstDir, "bin", "x86_64", "goodbyedpi.exe"), "eski-gdpi");
                File.WriteAllText(Path.Combine(dstDir, "veri", "ayarlar.xml"), "kullanıcı-ayarı");
                int rc = Updater.Apply(srcDir, dstDir, 999999, "1.0.0", false, false);
                bool okCopy = File.ReadAllText(Path.Combine(dstDir, "DPIKalkan.exe")) == "yeni" && File.ReadAllText(Path.Combine(dstDir, "bin", "x86_64", "goodbyedpi.exe")) == "yeni-gdpi";
                bool okKeep = File.ReadAllText(Path.Combine(dstDir, "veri", "ayarlar.xml")) == "kullanıcı-ayarı";
                bool okBak = File.ReadAllText(Path.Combine(dstDir, "veri", "onceki-surum", "DPIKalkan.exe")) == "eski";
                Check("güncelleme kurulumu: kopyalama, ayarların korunması, yedek", rc == 0 && okCopy && okKeep && okBak, "rc=" + rc);
                int rc2 = Updater.Apply(dstDir, dstDir, 999999, "1.0.0", false, false);
                Check("güncelleme kurulumu: aynı klasör reddedilir", rc2 == 1);
                try { Directory.Delete(t, true); } catch { }
            }

            SecurityTests(Check);
            Phase2Tests(Check);

            if (proxies.Contains("--update-live"))
            {
                string err;
                var li = Updater.Check(out err);
                Check("GitHub'dan sürüm bilgisi", li != null && li.AssetUrl.Length > 0 && li.Sha256.Length == 64, li != null ? li.Tag + " " + li.AssetName + " sha " + li.Sha256.Substring(0, 12) : err);
                if (li != null)
                {
                    try
                    {
                        var prep = Updater.DownloadAndExtract(li, null, System.Threading.CancellationToken.None);
                        string exe = Path.Combine(prep.Root, "DPIKalkan.exe");
                        Check("GitHub'dan indirme + SHA-256 + aynı tutamaçtan açma", File.Exists(exe) && Integrity.Sha256File(exe) == prep.ExeSha256 &&
                              prep.Root.StartsWith(Updater.UpdateRoot, StringComparison.OrdinalIgnoreCase), prep.Root);
                        Check("güncelleme: indirilen zip açıldıktan sonra silinir", !Directory.GetFiles(Updater.UpdateRoot, "*.zip").Any());
                        File.AppendAllText(exe, "x");   // açıldıktan sonra değiştirilmiş gibi
                        try { Updater.StartApply(prep); Check("güncelleme: açıldıktan sonra değişen exe başlatılmaz", false); }
                        catch (InvalidDataException) { Check("güncelleme: açıldıktan sonra değişen exe başlatılmaz", true); }
                    }
                    catch (Exception ex) { Check("GitHub'dan indirme + SHA-256 + aynı tutamaçtan açma", false, ex.Message); }
                    var badInfo = new UpdateInfo { Version = li.Version, AssetName = li.AssetName, AssetUrl = li.AssetUrl, Sha256 = new string('0', 64) };
                    try { Updater.DownloadAndExtract(badInfo, null, System.Threading.CancellationToken.None); Check("yanlış özet reddedilir", false); }
                    catch (InvalidDataException) { Check("yanlış özet reddedilir (dosya ve klasör silinir)", !Directory.GetFiles(Updater.UpdateRoot).Any() && !Directory.Exists(Path.Combine(Updater.UpdateRoot, "v" + li.Version))); }
                    var evil = new UpdateInfo { Version = li.Version, AssetName = "x.zip", AssetUrl = "https://example.com/x.zip", Sha256 = new string('0', 64) };
                    try { Updater.DownloadAndExtract(evil, null, System.Threading.CancellationToken.None); Check("GitHub dışı adres reddedilir", false); }
                    catch (InvalidOperationException) { Check("GitHub dışı adres reddedilir", true); }
                    Updater.Cleanup();
                }
            }

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

        /// <summary>Goodbyedpi.exe'yi yönetici istemeden (RunAsInvoker) çalıştırır: yalnızca parametre ayrıştırmasını görmek için.</summary>
        static string RunUnelevated(string exe, string args)
        {
            var psi = new System.Diagnostics.ProcessStartInfo(exe, args)
            {
                UseShellExecute = false, CreateNoWindow = true, RedirectStandardOutput = true, RedirectStandardError = true,
                WorkingDirectory = Path.GetDirectoryName(exe),
            };
            psi.EnvironmentVariables["__COMPAT_LAYER"] = "RunAsInvoker";
            using (var p = System.Diagnostics.Process.Start(psi))
            {
                var o = p.StandardOutput.ReadToEndAsync();
                var e = p.StandardError.ReadToEndAsync();
                if (!p.WaitForExit(10000)) { try { p.Kill(); } catch { } return "zaman aşımı"; }
                return o.Result + e.Result;
            }
        }

        static void Phase2Tests(Action<string, bool, string> Check)
        {
            string repo = Path.GetFullPath(Path.Combine(App.BaseDir, ".."));
            string ver = App.Version, ver4 = ver + ".0";

            // ---------------------------------------------------------------- exe sürüm bilgisi ve manifest
            var self = System.Diagnostics.FileVersionInfo.GetVersionInfo(System.Diagnostics.Process.GetCurrentProcess().MainModule.FileName);
            Check("metadata: yayıncı, ürün, açıklama, telif", self.CompanyName == "Osman Barış MEDİNE (Mr Leywin)" && self.ProductName == "DPI Kalkan" && self.FileDescription == "DPI Kalkan" &&
                  self.LegalCopyright == "Copyright (c) 2026 Osman Barış MEDİNE (Mr Leywin)" && App.Copyright == "Copyright (c) 2026 Osman Barış MEDİNE (Mr Leywin)", self.CompanyName + " | " + self.ProductName + " | " + self.FileDescription + " | " + self.LegalCopyright);
            Check("metadata: sürümler tutarlı", self.FileVersion == ver4 && self.ProductVersion == ver, self.FileVersion + " / " + self.ProductVersion);
            string pkgExe = Path.Combine(App.BaseDir, @"paket\DPI Kalkan\DPIKalkan.exe");
            if (File.Exists(pkgExe))
            {
                var pv = System.Diagnostics.FileVersionInfo.GetVersionInfo(pkgExe);
                string text = System.Text.Encoding.UTF8.GetString(File.ReadAllBytes(pkgExe));
                var m = System.Text.RegularExpressions.Regex.Match(text, "<assemblyIdentity\\s+version=\"([\\d.]+)\"\\s+name=\"DPIKalkan.app\"");
                Check("metadata: paketteki exe sürümü ve gömülü manifest sürümü eşit", pv.FileVersion == ver4 && m.Success && m.Groups[1].Value == ver4 &&
                      text.Contains("requireAdministrator"), pv.FileVersion + " / manifest " + (m.Success ? m.Groups[1].Value : "yok"));
            }
            else Check("metadata: paketteki exe (build.ps1 çalıştırılmadı)", false, pkgExe);

            // ---------------------------------------------------------------- betik yorumlayıcısı (PS) hiçbir yerde yok
            string needle = "power" + "shell";
            var hits = Directory.GetFiles(Path.Combine(repo, "src"), "*.cs", SearchOption.AllDirectories)
                .Where(f => File.ReadAllText(f).IndexOf(needle, StringComparison.OrdinalIgnoreCase) >= 0).Select(Path.GetFileName).ToList();
            string scripts = Path.Combine(App.BaseDir, @"paket\DPI Kalkan\betikler");
            if (Directory.Exists(scripts))
                hits.AddRange(Directory.GetFiles(scripts).Where(f => File.ReadAllText(f).IndexOf(needle, StringComparison.OrdinalIgnoreCase) >= 0).Select(Path.GetFileName));
            Check("kaynak kodda ve betiklerde " + "Power" + "Shell geçmiyor", hits.Count == 0, hits.Count == 0 ? "" : string.Join(", ", hits));

            // ---------------------------------------------------------------- şifreli DNS: netsh ve Windows 10 davranışı
            Check("DoH: Windows 10 (19045) desteklenmiyor, Windows 11 (22000+) destekleniyor",
                  !SystemDns.DohSupportedOn(19045) && SystemDns.DohSupportedOn(22000) && SystemDns.DohSupportedOn(26100), "bu bilgisayar: " + SystemDns.WindowsBuild);
            string msg10 = SystemDns.DohUnsupportedMessage(19045);
            Check("DoH: Windows 10'da anlaşılır uyarı", msg10.Contains("Windows 11") && msg10.Contains("19045") && msg10.Contains("şifresiz"), msg10);
            var cf = SystemDns.Choices.First(c => c.Id == "cloudflare");
            var cmds = SystemDns.DohCommands(cf);
            Check("DoH: netsh komutları (4 adres, add + set)", cmds.Count == 4 &&
                  cmds.All(c => c.Add.StartsWith("dns add encryption server=") && c.Set.StartsWith("dns set encryption server=") &&
                                c.Add.EndsWith(" dohtemplate=https://cloudflare-dns.com/dns-query autoupgrade=yes udpfallback=no")), cmds.Count > 0 ? cmds[0].Add : "");
            Check("DoH: tüm DNS seçenekleri için komut üretilir", SystemDns.Choices.Where(c => !c.IsAuto).All(c => SystemDns.DohCommands(c).Count == 4), "");
            var evilChoice = new SystemDnsChoice { Id = "x", V4a = "1.1.1.1", Doh = "https://x.example/dns-query & calc" };
            Check("DoH: güvenli olmayan şablon komuta girmez", SystemDns.DohCommands(evilChoice).Count == 0, "");
            // bu makinedeki gerçek 'netsh dns show encryption server=…' çıktıları (Windows 11 26300)
            string shownNo = "\r\nEncryption settings for 1.1.1.1 \r\n----------------------------------------------------------------------\r\n" +
                             "DNS-over-HTTPS template     : https://cloudflare-dns.com/dns-query\r\nAuto-upgrade                : no\r\nUDP-fallback                : no\r\n";
            var pNo = SystemDns.ParseEncryption(shownNo);
            var pYes = SystemDns.ParseEncryption(shownNo.Replace("Auto-upgrade                : no", "Auto-upgrade                : yes"));
            var pV6 = SystemDns.ParseEncryption(shownNo.Replace("1.1.1.1", "2606:4700:4700::1111"));
            var pNone = SystemDns.ParseEncryption("\r\n");
            Check("DoH: netsh çıktısı ayrıştırılır (yerleşik kayıt Auto-upgrade: no → yes, kayıt yok, IPv6 başlık)",
                  pNo.Exists && pNo.AutoUpgrade == false && pNo.UdpFallback == false && pNo.Template == "https://cloudflare-dns.com/dns-query" &&
                  pYes.AutoUpgrade == true && pV6.Exists && pV6.Template.Length > 0 && !pNone.Exists && pNone.AutoUpgrade == null, "");
            Check("DoH: genel ayar okunur", SystemDns.ParseGlobalDoh("\r\nDoH settings                : enabled\r\n\r\nDoT settings                : enabled\r\n") == "enabled" &&
                  SystemDns.ParseGlobalDoh("") == null, "");
            Check("DoH: komutlarda sunucu adresi ayrı tutulur (geri okuma ve yedek için)", cmds.All(c => c.Add.Contains("server=" + c.Ip + " ")) && cmds[0].Ip == cf.V4a, "");

            // ---------------------------------------------------------------- kurulum paketiyle güncelleme
            string relJson = "{\"tag_name\":\"v9.8.7\",\"draft\":false,\"prerelease\":false,\"body\":\"x\",\"assets\":[" +
                "{\"name\":\"DPI-Kalkan-9.8.7.zip\",\"browser_download_url\":\"https://github.com/x/y/releases/download/v9.8.7/DPI-Kalkan-9.8.7.zip\",\"size\":1,\"digest\":\"sha256:" + new string('a', 64) + "\"}," +
                "{\"name\":\"DPI-Kalkan-9.8.7-Setup.exe\",\"browser_download_url\":\"https://github.com/x/y/releases/download/v9.8.7/DPI-Kalkan-9.8.7-Setup.exe\",\"size\":2,\"digest\":\"sha256:" + new string('b', 64) + "\"}," +
                "{\"name\":\"SHA256SUMS.txt\",\"browser_download_url\":\"https://github.com/x/y/s\",\"size\":3,\"digest\":null}]}";
            var ri = Updater.Parse(relJson);
            Check("güncelleme: zip ve Setup.exe ayrı ayrı ayrıştırılır", ri.AssetName == "DPI-Kalkan-9.8.7.zip" && ri.Sha256 == new string('a', 64) &&
                  ri.SetupName == "DPI-Kalkan-9.8.7-Setup.exe" && ri.SetupSha256 == new string('b', 64), ri.SetupName);
            string notes = "**SHA-256** (DPI-Kalkan-9.8.7.zip): `" + new string('c', 64) + "`\\n\\n**SHA-256** (DPI-Kalkan-9.8.7-Setup.exe): `" + new string('d', 64) + "`";
            var rn = Updater.Parse(relJson.Replace("\"digest\":\"sha256:" + new string('a', 64) + "\"", "\"digest\":null")
                                          .Replace("\"digest\":\"sha256:" + new string('b', 64) + "\"", "\"digest\":null").Replace("\"body\":\"x\"", "\"body\":\"" + notes + "\""));
            Check("güncelleme: özetler sürüm notundan dosya adına göre okunur", rn.Sha256 == new string('c', 64) && rn.SetupSha256 == new string('d', 64), rn.Sha256.Substring(0, 4) + " " + rn.SetupSha256.Substring(0, 4));
            Check("güncelleme: kurulumlu sürüm Setup.exe, taşınabilir zip kullanır", Updater.UseSetup(ri, true) && !Updater.UseSetup(ri, false) &&
                  !Updater.UseSetup(Updater.Parse(relJson.Replace("-Setup.exe", "-Kurulum.exe")), true), "");
            Check("güncelleme: kurulum kaydı yalnızca aynı ve korumalı klasörde geçerli",
                  Updater.IsInnoInstall(@"C:\Program Files\DPI Kalkan\", @"C:\Program Files\DPI Kalkan\", true) &&
                  !Updater.IsInnoInstall(@"C:\Program Files\DPI Kalkan", @"C:\Program Files\DPI Kalkan\", false) &&
                  !Updater.IsInnoInstall(@"C:\Users\x\Desktop\DPI Kalkan", @"C:\Program Files\DPI Kalkan\", true) &&
                  !Updater.IsInnoInstall(@"C:\Program Files\DPI Kalkan", null, true), "");

            string iss = Path.Combine(repo, @"installer\DPIKalkan.iss");
            if (File.Exists(iss))
            {
                string s = File.ReadAllText(iss);
                Check("kurulum betiği: AppId uygulamadakiyle aynı, yükseltme/kaldırma kancaları var",
                      s.Contains("AppId={" + Updater.InnoAppId) && s.Contains("--prepare-upgrade") && s.Contains("--uninstall --quiet") &&
                      s.Contains("PrivilegesRequired=admin") && s.Contains("ArchitecturesAllowed=x64compatible") && s.Contains("{param:DKUPDATED|}"), "");
                // requireAdministrator manifestli exe yetkisiz başlatılırsa CreateProcess 740 verir: tüm başlatmalar yönetici kimliğiyle
                var codeLines = s.Split('\n').Select(l => l.Trim()).Where(l => !l.StartsWith(";") && !l.StartsWith("{") && !l.StartsWith("//")).ToList();
                var launches = codeLines.Where(l => l.StartsWith("Filename:") && l.Contains("{#AppExe}")).ToList();
                Check("kurulum betiği: DPIKalkan.exe her yerde runascurrentuser ile başlatılır (740 hatası yok)",
                      launches.Count == 3 && launches.All(l => l.Contains("runascurrentuser")) &&
                      !codeLines.Any(l => l.Contains("runasoriginaluser") || l.Contains("ExecAsOriginalUser")), launches.Count + " başlatma");
            }
            string setup = Path.Combine(repo, "dist", "DPI-Kalkan-" + ver + "-Setup.exe");
            if (File.Exists(setup))
            {
                var sv = System.Diagnostics.FileVersionInfo.GetVersionInfo(setup);
                string spn = (sv.ProductName ?? "").Trim(), sfv = (sv.FileVersion ?? "").Trim(), sco = (sv.CompanyName ?? "").Trim();
                // Inno Setup sürüm dizgilerini boşlukla doldurur; güncelleyici Trim ile karşılaştırır
                Check("kurulum paketi: sürüm bilgisi güncelleyicinin beklediği gibi", spn == "DPI Kalkan" && sfv == ver4 && sco == "Osman Barış MEDİNE (Mr Leywin)" && (sv.LegalCopyright ?? "").Trim() == "Copyright (c) 2026 Osman Barış MEDİNE (Mr Leywin)",
                      spn + " " + sfv + " " + sco + " (dolgulu: " + (sv.ProductName != spn) + ")");
            }

            // ---------------------------------------------------------------- SHA256SUMS listesi gerçek dosyalarla aynı
            string sumsFile = Path.Combine(repo, "dist", "DPI-Kalkan-" + ver + ".SHA256SUMS.txt");
            if (File.Exists(sumsFile))
            {
                var bad = new List<string>();
                int n = 0;
                foreach (var line in File.ReadAllLines(sumsFile).Where(l => l.Length > 66))
                {
                    string h = line.Substring(0, 64), name = line.Substring(66);
                    string path = name.StartsWith("DPI Kalkan/") ? Path.Combine(App.BaseDir, "paket", name.Replace('/', '\\')) : Path.Combine(repo, "dist", name);
                    n++;
                    if (!File.Exists(path) || Integrity.Sha256File(path) != h) bad.Add(name);
                }
                Check("SHA256SUMS: tüm satırlar dosyalarla eşleşir", bad.Count == 0 && n >= 9, n + " satır " + string.Join(", ", bad));
            }
            else Check("SHA256SUMS dosyası (build.ps1 çalıştırılmadı)", false, sumsFile);

            // ---------------------------------------------------------------- beklenmedik kapanış işareti
            {
                bool oldPreview = App.Preview;
                string oldData = App.DataDir;
                string t = Path.Combine(Path.GetTempPath(), "dpk-crash-" + Guid.NewGuid().ToString("N").Substring(0, 6));
                try
                {
                    App.Preview = false;
                    App.DataDir = t;
                    CrashMarker.Begin();
                    bool clean = CrashMarker.PreviousUnexpected == null;
                    CrashMarker.End();
                    CrashMarker.Begin();
                    bool stillClean = CrashMarker.PreviousUnexpected == null;     // düzgün kapanış sonrası uyarı yok
                    CrashMarker.Record(new InvalidOperationException("deneme çökmesi"), fatal: true);
                    CrashMarker.Begin();                                             // End çağrılmadan yeniden açılış = çökme
                    string prev = CrashMarker.PreviousUnexpected;
                    CrashMarker.Clear(t);
                    CrashMarker.Begin();
                    bool clearedOk = CrashMarker.PreviousUnexpected == null;        // araçlarımız bilerek kapattıysa uyarı yok
                    CrashMarker.End();
                    Check("çökme işareti: düzgün kapanışta uyarı yok, çökmede DK-106 ve son hata", clean && stillClean && prev != null && prev.Contains("deneme çökmesi") && clearedOk, prev ?? "");
                }
                finally
                {
                    App.Preview = oldPreview;
                    App.DataDir = oldData;
                    try { Directory.Delete(t, true); } catch { }
                }
            }

            // ---------------------------------------------------------------- antivirüs metinleri
            var avSteps = ErrorCatalog.All.SelectMany(e => e.Steps).Where(s => s.IndexOf("istisna", StringComparison.OrdinalIgnoreCase) >= 0).ToList();
            Check("antivirüs: istisna yalnızca 'son çare' olarak öneriliyor", avSteps.Count > 0 && avSteps.All(s => s.Contains("Son çare")), avSteps.Count + " adım");
            var fp = new[] { "DK-102", "DK-202", "DK-204" }.Select(ErrorCatalog.Get).ToList();
            Check("antivirüs: yanlış pozitif bildirimi ve SHA-256 doğrulaması önce geliyor", fp.All(e => e.Fix == FixKind.ReportFalsePositive || e.Fix2 == FixKind.ReportFalsePositive) &&
                  fp.All(e => e.Steps.Any(s => s.Contains("wdsi")) && e.Steps.Any(s => s.Contains("SHA-256"))) &&
                  ErrorInfo.FixLabel(FixKind.ReportFalsePositive).Length > 0 && ErrorCatalog.Get("DK-106") != null, "");
        }

        static void SecurityTests(Action<string, bool, string> Check)
        {
            // ---------------------------------------------------------------- parametre beyaz listesi
            var bad = Profiles.BuiltIns.Where(x => !ArgPolicy.ValidateProfile(x, out _)).Select(x => x.Id).ToList();
            Check("beyaz liste: tüm hazır profiller geçer", bad.Count == 0, bad.Count == 0 ? Profiles.BuiltIns.Count + " profil" : string.Join(", ", bad));
            foreach (var a in new[] { "--blacklist x.txt", "--black x.txt", "--bl x", "--blacklist=x", "-5 & calc", "-9 --dns-addr 1.1.1.1&calc",
                                      "--dns-ad 1.1.1.1", "--set-ttl 999", "--set-ttl -5", "--zzz", "-e", "-9 \"x\"", "-9 %PATH%", "-9\r\nstart calc",
                                      "--dns-addr 1.1.1", "--dnsv6-addr 1.1.1.1", "-pqr", "-f2", "--wrong-seq=1", "--fake-from-hex 12z4", "-9 x", "-9 \\\\host\\share" })
                Check("beyaz liste reddeder: " + a.Replace("\r\n", "⏎"), !ArgPolicy.Validate(a, out _), "");
            foreach (var a in new[] { "-9", "-5 --set-ttl 5 --dns-addr 77.88.8.8 --dns-port 1253 --dnsv6-addr 2a02:6b8::feed:0ff --dnsv6-port 1253",
                                      "--auto-ttl=1-4-10 --max-payload=1200 --auto-ttl --max-payload", "-e 2 --fake-from-hex 1234abcd --fake-gen 5 --fake-resend 2 -q" })
                Check("beyaz liste kabul eder: " + a, ArgPolicy.Validate(a, out var ge), ge);

            // ---------------------------------------------------------------- GoodbyeDPI'ın getopt_long davranışı (beyaz listenin gerekçesi)
            string tp = Path.GetFullPath(Path.Combine(App.BaseDir, @"..\third_party\goodbyedpi"));
            string gdpi = Path.Combine(tp, "x86_64", "goodbyedpi.exe");
            if (File.Exists(gdpi))
            {
                string nofile = @"C:\dpk-yok\yok.txt";
                string full = RunUnelevated(gdpi, "--blacklist " + nofile), abbr = RunUnelevated(gdpi, "--black " + nofile), unk = RunUnelevated(gdpi, "--zzz");
                Check("getopt_long: '--black' kısaltması GoodbyeDPI'da --blacklist olarak çalışıyor (eski Contains denetimi atlatılabiliyordu)",
                      full.Contains("Can't load blacklist") && abbr.Contains("Can't load blacklist") && unk.Contains("Usage"), abbr.Trim().Split('\n').Last().Trim());
            }
            else Check("getopt_long testi (third_party bulunamadı)", false, tp);

            // ---------------------------------------------------------------- profil kodu ve betik güvenliği
            var basep = Profiles.BuiltIns.First(x => x.Id == "tr-alt").CloneAsCustom();
            var ev = basep.CloneAsCustom();
            ev.UseRaw = true; ev.RawArgs = "-9 --black C:\\x.txt";
            Check("profil kodu: kısaltılmış --blacklist reddedilir", !ProfileCode.TryFromCode(ProfileCode.ToCode(ev), out _, out _), "");
            ev.RawArgs = "-9 & calc";
            Check("profil kodu: komut karakteri reddedilir", !ProfileCode.TryFromCode(ProfileCode.ToCode(ev), out _, out _), "");
            ev.RawArgs = "-9"; ev.Name = "Ad\r\nstart calc"; ev.Description = "a\nb";
            bool okc = ProfileCode.TryFromCode(ProfileCode.ToCode(ev), out var cleaned, out var cerr);
            Check("profil kodu: ad/açıklamadaki satır sonları temizlenir", okc && !cleaned.Name.Contains("\n") && !cleaned.Description.Contains("\n"), cerr);
            var raw = basep.CloneAsCustom(); raw.UseRaw = true; raw.RawArgs = "-9"; raw.Name = "x\r\nstart calc ^"; raw.Description = "%PATH% & del\r\nstart y";
            string script = Scripts.Build(raw, "..\\bin");
            Check("betik: REM satırlarından komut sızmaz", script.Split('\n').Count(l => l.TrimStart().StartsWith("start ")) == 1 && !script.Contains("%PATH%") && !script.Contains("^"), "");
            raw.RawArgs = "-9 & calc";
            try { Scripts.Build(raw, "..\\bin"); Check("betik: güvenli olmayan profil betiğe dönüşmez", false, ""); }
            catch (InvalidDataException) { Check("betik: güvenli olmayan profil betiğe dönüşmez", true, ""); }

            // ---------------------------------------------------------------- program bazlı proxy: cmd satırı
            var openP = new ProxyEntry { Kind = ProxyKind.Http, Host = "5.6.7.8", Port = 3128 };
            bool Throws(Action a) { try { a(); return false; } catch (ArgumentException) { return true; } }
            Check("ortam modu: '& calc' ek parametresi reddedilir", Throws(() => AppProxy.Plan(new ProxyApp { Name = "A", ExePath = @"C:\X\t.exe", Mode = AppProxyMode.Env, ExtraArgs = "& calc" }, openP)), "");
            Check("ortam modu: yolda % reddedilir", Throws(() => AppProxy.Plan(new ProxyApp { Name = "A", ExePath = @"C:\X\%PATH%\t.exe", Mode = AppProxyMode.Env }, openP)), "");
            Check("ortam modu: adreste tırnak reddedilir", Throws(() => AppProxy.Plan(new ProxyApp { Name = "A", ExePath = @"C:\X\t.exe", Mode = AppProxyMode.Env },
                                                                     new ProxyEntry { Kind = ProxyKind.Http, Host = "1.2.3.4\"&calc&\"", Port = 80 })), "");
            Check("ortam modu: parantezli normal yol kabul edilir", !Throws(() => AppProxy.Plan(new ProxyApp { Name = "A", ExePath = @"C:\Program Files (x86)\X\t.exe", Mode = AppProxyMode.Env, ExtraArgs = "--verbose" }, openP)), "");

            // ---------------------------------------------------------------- dosya bütünlüğü ve kilit
            foreach (var root in new[] { tp, Path.Combine(App.BaseDir, @"paket\DPI Kalkan\bin") })
                if (Directory.Exists(root)) Check("gömülü SHA-256 özetleri eşleşir: " + root.Substring(Math.Max(0, root.Length - 30)), Integrity.VerifyTree(root, out var ie), ie);
            if (File.Exists(gdpi))
            {
                string t = Path.Combine(Path.GetTempPath(), "dpk-int-" + Guid.NewGuid().ToString("N").Substring(0, 6));
                Directory.CreateDirectory(t);
                foreach (var f in new[] { "goodbyedpi.exe", "WinDivert.dll", "WinDivert64.sys" }) File.Copy(Path.Combine(tp, "x86_64", f), Path.Combine(t, f));
                var files = new[] { "goodbyedpi.exe", "WinDivert.dll", "WinDivert64.sys" };
                using (var h = Integrity.VerifyAndLock(t, "x86_64", files, out var e1))
                {
                    Check("bütünlük: resmi dosyalar doğrulanır", h != null, e1);
                    bool writeBlocked;
                    try { using (new FileStream(Path.Combine(t, "goodbyedpi.exe"), FileMode.Open, FileAccess.Write, FileShare.ReadWrite)) { } writeBlocked = false; }
                    catch (IOException) { writeBlocked = true; }
                    Check("bütünlük: doğrulama sonrası exe yazmaya kapalı", writeBlocked, "");
                    string outp = RunUnelevated(Path.Combine(t, "goodbyedpi.exe"), "--zzz");
                    Check("bütünlük: kilit açıkken GoodbyeDPI ve WinDivert.dll yüklenebiliyor", outp.Contains("Usage"), outp.Length > 0 ? "" : "çıktı yok");
                }
                File.AppendAllText(Path.Combine(t, "WinDivert.dll"), "x");
                using (var h2 = Integrity.VerifyAndLock(t, "x86_64", files, out var e2))
                    Check("bütünlük: değiştirilmiş WinDivert.dll reddedilir", h2 == null && e2.Contains("WinDivert.dll"), e2);
                try { Directory.Delete(t, true); } catch { }
            }

            // ---------------------------------------------------------------- korumalı klasör denetimi
            Check("korumalı klasör: Program Files", InstallGuard.Check(Environment.GetFolderPath(Environment.SpecialFolder.ProgramFiles), out var r1), r1);
            Check("korumalı klasör: System32", InstallGuard.Check(Environment.SystemDirectory, out var r2), r2);
            Check("korumasız klasör: Temp", !InstallGuard.Check(Path.GetTempPath(), out var r3), r3);
            {
                string ud = Path.Combine(Path.GetTempPath(), "dpk-tasinabilir-" + Guid.NewGuid().ToString("N").Substring(0, 6));
                Directory.CreateDirectory(Path.Combine(ud, "bin", "x86_64"));
                Check("korumasız klasör: kullanıcının yazabildiği yeni klasör", !InstallGuard.Check(ud, out var r4), r4);
                try { Directory.Delete(ud, true); } catch { }
            }
            Check("korumasız klasör: olmayan klasör", !InstallGuard.Check(@"C:\dpk-yok-" + Guid.NewGuid().ToString("N"), out var r5), r5);

            // ---------------------------------------------------------------- güvenli süreç yardımcısı
            var sw = System.Diagnostics.Stopwatch.StartNew();
            var pr = Sys.Run(Sys.SystemExe("ping.exe"), "-n 30 127.0.0.1", 1500);
            Check("süreç yardımcısı: süre dolunca sonlandırır", pr.Code == -2 && sw.ElapsedMilliseconds < 7000, sw.ElapsedMilliseconds + " ms");
            var pr2 = Sys.Run(Sys.SystemExe("cmd.exe"), "/c for /l %i in (1,1,1500) do @(echo cikti%i & echo hata%i 1>&2)", 30000);
            Check("süreç yardımcısı: büyük stdout+stderr kilitlenmeden okunur", pr2.Code == 0 && pr2.Output.Contains("cikti1500") && pr2.Output.Contains("hata1500"), pr2.Output.Length + " karakter");

            // ---------------------------------------------------------------- Görev Zamanlayıcı (COM) ve DLL arama kısıtlaması
            Check("Görev Zamanlayıcı COM bağlantısı", Startup.Probe(out var se), se + (Startup.IsEnabled() ? " (görev: " + Startup.CommandPath() + ")" : " (görev yok)"));
            Check("DLL kısıtlıyken WMI çalışır", Engine.ServicePath("Winmgmt") != null, "");
            bool zipOk;
            try
            {
                using (var ms = new MemoryStream())
                {
                    using (var za = new System.IO.Compression.ZipArchive(ms, System.IO.Compression.ZipArchiveMode.Create, true))
                    using (var w = new StreamWriter(za.CreateEntry("a.txt").Open())) w.Write(new string('x', 5000));
                    ms.Position = 0;
                    using (var za = new System.IO.Compression.ZipArchive(ms, System.IO.Compression.ZipArchiveMode.Read))
                    using (var rd = new StreamReader(za.Entries[0].Open())) zipOk = rd.ReadToEnd().Length == 5000;
                }
            }
            catch { zipOk = false; }
            Check("DLL kısıtlıyken zip (clrcompression) çalışır", zipOk, "");

            // ---------------------------------------------------------------- güncelleme yolları
            Check("güncelleme: güvenilen adresler", Updater.TrustedUrl("https://github.com/x") && Updater.TrustedUrl("https://objects.githubusercontent.com/x") &&
                  !Updater.TrustedUrl("http://github.com/x") && !Updater.TrustedUrl("https://github.com.evil.example/x") && !Updater.TrustedUrl("https://evilgithubusercontent.com/x"), "");
            {
                string t = Path.Combine(Path.GetTempPath(), "dpk-val-" + Guid.NewGuid().ToString("N").Substring(0, 6));
                string dst = Path.Combine(t, "kurulu");
                Directory.CreateDirectory(dst);
                File.Copy(System.Diagnostics.Process.GetCurrentProcess().MainModule.FileName, Path.Combine(dst, "DPIKalkan.exe"));
                string src = Path.Combine(dst, "veri", "guncelleme", "v9.9.9", "DPI Kalkan");
                string legacy = Path.Combine(Updater.LegacyRoot, "v9.9.9", "DPI Kalkan");
                Check("güncelleme yolu: veri\\guncelleme kabul", Updater.ValidateApplyArgs(src, dst, Path.Combine(src, "DPIKalkan.exe"), out var v1), v1);
                Check("güncelleme yolu: 2.3.0'ın eski Temp klasörü kabul", Updater.ValidateApplyArgs(legacy, dst, Path.Combine(legacy, "DPIKalkan.exe"), out var v2), v2);
                Check("güncelleme yolu: başka klasör reddedilir", !Updater.ValidateApplyArgs(Path.Combine(t, "baska"), dst, Path.Combine(t, "baska", "DPIKalkan.exe"), out _), "");
                Check("güncelleme yolu: ..\\ ile kaçış reddedilir", !Updater.ValidateApplyArgs(Path.Combine(dst, @"veri\guncelleme\..\..\x"), dst, Path.Combine(dst, @"x\DPIKalkan.exe"), out _), "");
                Check("güncelleme yolu: kaynak dışından çalışan güncelleyici reddedilir", !Updater.ValidateApplyArgs(src, dst, Path.Combine(t, "DPIKalkan.exe"), out _), "");
                Check("güncelleme yolu: DPI Kalkan olmayan hedef reddedilir", !Updater.ValidateApplyArgs(src, t, Path.Combine(src, "DPIKalkan.exe"), out _), "");
                try { Directory.Delete(t, true); } catch { }
            }
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

            // --- güncelleme bandı ve Ayarlar'ın alt bölümleri
            Theme(form, "system", UiLevel.Orta);
            Updater.DebugSetAvailable(new UpdateInfo { Version = new Version(2, 4, 0), Notes = "## Yenilikler\n- Örnek" });
            App.RaiseStatus();
            form.Navigate("home"); Shot(form, Path.Combine(dir, "f_home_update.png"));
            var sp = form.Navigate("settings");
            var area = sp.Controls.OfType<ScrollArea>().FirstOrDefault();
            if (area != null)
                foreach (int yy in new[] { 900, 1500 })
                {
                    area.AutoScrollPosition = new Point(0, DPIKalkan.Theme.D(yy));
                    Shot(form, Path.Combine(dir, "f_settings_" + yy + ".png"));
                }
            Updater.DebugSetAvailable(null);
            form.Go<TroublePage>("trouble"); Shot(form, Path.Combine(dir, "f_trouble.png"));

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
