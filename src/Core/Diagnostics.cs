using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Management;
using System.ServiceProcess;
using System.Text;
using System.Threading.Tasks;

namespace DPIKalkan
{
    public enum CheckStatus { Ok, Info, Warn, Fail, Skip }

    public sealed class CheckItem
    {
        public string Title, Detail = "", Code = "";
        public CheckStatus Status;
    }

    /// <summary>"Sorun Bul" taraması: kurulumdan Discord'a kadar her şeyi sırayla kontrol eder.</summary>
    public static class Diagnostics
    {
        static readonly string[] OtherDpiTools = { "winws", "zapret", "ciadpi", "byedpi", "spoofdpi", "green-tunnel", "greentunnel", "dpitunnel-cli", "dpitunnel", "powertunnel" };

        public static List<string> Antivirus()
        {
            var list = new List<string>();
            try
            {
                using (var q = new ManagementObjectSearcher(@"root\SecurityCenter2", "SELECT displayName FROM AntiVirusProduct"))
                    foreach (ManagementObject mo in q.Get())
                        using (mo)
                        {
                            string n = mo["displayName"] as string;
                            if (!string.IsNullOrEmpty(n) && !list.Contains(n)) list.Add(n);
                        }
            }
            catch { }
            return list;
        }

        public static List<string> RunningOtherDpi()
        {
            var found = new List<string>();
            foreach (var n in OtherDpiTools)
            {
                var ps = Process.GetProcessesByName(n);
                if (ps.Length > 0) found.Add(n + ".exe");
                foreach (var p in ps) p.Dispose();
            }
            return found;
        }

        public static async Task<List<CheckItem>> RunAsync(Action<CheckItem> onItem)
        {
            var items = new List<CheckItem>();
            void Add(string title, CheckStatus st, string detail, string code = "")
            {
                var it = new CheckItem { Title = title, Status = st, Detail = detail, Code = code };
                items.Add(it);
                if (!string.IsNullOrEmpty(code) && (st == CheckStatus.Fail || st == CheckStatus.Warn)) App.Problems.Report(code, detail);
                else if (!string.IsNullOrEmpty(code) && st == CheckStatus.Ok) App.Problems.Clear(code);
                App.Post(() => onItem?.Invoke(it));
            }
            var eng = App.Engine;

            // ------------------------------------------------ uygulama
            bool admin = App.IsAdmin();
            Add("Yönetici izni", admin ? CheckStatus.Ok : CheckStatus.Fail,
                admin ? "Uygulama yönetici olarak çalışıyor." : "Yönetici izni yok; sürücü yüklenemez.", "DK-101");

            bool temp = App.RunningFromTemp;
            Add("Uygulama klasörü", temp ? CheckStatus.Warn : CheckStatus.Ok,
                temp ? "Geçici bir klasörden çalışıyor: " + App.BaseDir : App.BaseDir, "DK-103");

            string missing = Engine.MissingFile();
            Add("GoodbyeDPI dosyaları", missing == null ? CheckStatus.Ok : CheckStatus.Fail,
                missing == null ? "goodbyedpi.exe ve WinDivert dosyaları yerinde." : missing + " bulunamadı.", "DK-102");

            await Task.Run(() =>
            {
                string bfe = "";
                bool bfeOk = false;
                try { using (var sc = new ServiceController("BFE")) { bfeOk = sc.Status == ServiceControllerStatus.Running; bfe = sc.Status.ToString(); } }
                catch (Exception ex) { bfe = ex.Message; }
                Add("Temel Filtreleme Altyapısı (BFE)", bfeOk ? CheckStatus.Ok : CheckStatus.Fail, bfeOk ? "Çalışıyor." : "Çalışmıyor (" + bfe + ").", "DK-205");
            });

            // ------------------------------------------------ çakışma
            await Task.Run(() => eng.Poll());
            Add("Çakışan GoodbyeDPI", eng.ExternalCount > 0 ? CheckStatus.Warn : CheckStatus.Ok,
                eng.ExternalCount > 0 ? eng.ExternalCount + " adet uygulama dışı goodbyedpi.exe çalışıyor." : "Yok.", "DK-301");
            Add("Eski GoodbyeDPI hizmeti", eng.LegacyServiceExists ? CheckStatus.Warn : CheckStatus.Ok,
                eng.LegacyServiceExists ? "'GoodbyeDPI' hizmeti kurulu." : "Yok.", "DK-302");

            var other = await Task.Run(() => RunningOtherDpi());
            Add("Diğer DPI araçları", other.Count > 0 ? CheckStatus.Warn : CheckStatus.Ok,
                other.Count > 0 ? "Çalışıyor: " + string.Join(", ", other) : "Yok.", "DK-303");

            var adapters = await Task.Run(() => NetTest.Adapters());
            var vpn = adapters.Where(a => a.IsVpn).Select(a => a.Description).ToList();
            var proxy = NetTest.SystemProxy();
            Add("VPN", vpn.Count > 0 ? CheckStatus.Warn : CheckStatus.Ok, vpn.Count > 0 ? "Etkin VPN bağdaştırıcısı: " + string.Join(", ", vpn) : "Etkin VPN yok.", "DK-304");
            var ourProxy = ProxyTools.ActiveSystemEntry();
            if (ourProxy != null)
                Add("Sistem proxy'si", CheckStatus.Info, "DPI Kalkan'ın Proxy sayfasından ayarlandı: " + ourProxy.Title + " (" + proxy.Server + ").");
            else
                Add("Sistem proxy'si", proxy.Enabled ? CheckStatus.Warn : CheckStatus.Ok, proxy.Enabled ? "Açık: " + proxy.Server : "Kapalı.", "DK-802");

            var av = await Task.Run(() => Antivirus());
            var thirdParty = av.Where(n => n.IndexOf("Windows Defender", StringComparison.OrdinalIgnoreCase) < 0 &&
                                           n.IndexOf("Microsoft Defender", StringComparison.OrdinalIgnoreCase) < 0).ToList();
            Add("Antivirüs", thirdParty.Count > 0 ? CheckStatus.Info : CheckStatus.Ok,
                av.Count == 0 ? "Bilgi alınamadı." : string.Join(", ", av) + (thirdParty.Count > 0 ? " – sürücü hatası alırsanız istisna ekleyin." : ""), "DK-207");
            if (thirdParty.Count > 0) App.Problems.Clear("DK-207");

            // ------------------------------------------------ ağ
            int inetMs = 0;
            bool inet = await Task.Run(() => NetTest.InternetOk(out inetMs));
            Add("İnternet bağlantısı", inet ? CheckStatus.Ok : CheckStatus.Fail, inet ? "Çalışıyor (" + inetMs + " ms)." : "Genel sunuculara bağlanılamıyor.", "DK-801");

            var skew = await Task.Run(() => NetTest.ClockSkewMinutes());
            if (skew.HasValue)
                Add("Bilgisayar saati", skew.Value > 5 ? CheckStatus.Fail : CheckStatus.Ok,
                    skew.Value > 5 ? "Saat " + Math.Round(skew.Value) + " dakika kaymış." : "Doğru.", "DK-803");
            else
                Add("Bilgisayar saati", CheckStatus.Skip, "Ölçülemedi.");

            var hosts = NetTest.HostsDiscordLines();
            Add("hosts dosyası", hosts.Count > 0 ? CheckStatus.Warn : CheckStatus.Ok,
                hosts.Count > 0 ? hosts.Count + " Discord kaydı: " + string.Join(" | ", hosts.Take(3)) : "Discord kaydı yok.", "DK-405");

            // ------------------------------------------------ DNS
            bool running = eng.State == EngineState.Running;
            var prof = running ? eng.ActiveProfile : App.SelectedProfile;
            if (inet)
            {
                var rep = await Task.Run(() => { Native.FlushDns(); return NetTest.CheckDns("discord.com"); });
                if (rep.Reference.Count == 0)
                    Add("DNS doğrulaması", CheckStatus.Skip, "Bağımsız DNS sunucularına ulaşılamadı.");
                else if (running && prof != null && prof.UsesDnsRedirect)
                    Add("DNS doğrulaması (koruma açık)", rep.Blocked ? CheckStatus.Fail : CheckStatus.Ok,
                        rep.Blocked ? "Yönlendirmeye rağmen yanlış adres: " + string.Join(", ", rep.System.Take(2)) : "Doğru adres alınıyor (" + rep.System.FirstOrDefault() + ").", "DK-403");
                else
                    Add(running ? "DNS doğrulaması (yönlendirme yok)" : "DNS doğrulaması (koruma kapalı)", rep.Blocked ? CheckStatus.Warn : CheckStatus.Ok,
                        rep.Blocked ? "Servis sağlayıcı DNS'i engelli: DNS yönlendirmeli profil gerekir." : "Sistem DNS'i doğru yanıt veriyor.", "DK-401");

                var target = prof?.DnsTarget;
                if (prof != null && target != null && System.Net.IPAddress.TryParse(target.Value.Addr, out _))
                {
                    var r = await Task.Run(() => NetTest.TimedQuery("discord.com", target.Value.Addr, target.Value.Port, 2500));
                    Add("Yönlendirilen DNS sunucusu", r.Ok ? CheckStatus.Ok : CheckStatus.Fail,
                        target.Value.Addr + ":" + target.Value.Port + (r.Ok ? " yanıt veriyor (" + r.Ms + " ms)." : " yanıt vermiyor."), "DK-402");
                }
                bool v6 = adapters.Any(a => a.HasGlobalV6 && !a.IsVpn);
                if (v6 && prof != null && prof.UsesDnsRedirect && !prof.UseRaw && prof.Dns == DnsChoice.Custom && string.IsNullOrWhiteSpace(prof.CustomDns6))
                    Add("IPv6 DNS", CheckStatus.Warn, "IPv6 bağlantınız var ama profil IPv6 DNS'i yönlendirmiyor.", "DK-404");
                else
                    Add("IPv6 DNS", CheckStatus.Ok, v6 ? "IPv6 sorguları da yönlendiriliyor." : "IPv6 bağlantısı yok.", "DK-404");
            }

            // ------------------------------------------------ koruma
            if (eng.State == EngineState.Error)
                Add("Koruma", CheckStatus.Fail, "Hata: " + eng.LastError, eng.LastErrorCode);
            else if (running)
                Add("Koruma", CheckStatus.Ok, "Çalışıyor: " + prof?.Name + (eng.RunningAsService ? " (hizmet)" : ""));
            else
                Add("Koruma", CheckStatus.Warn, "Koruma kapalı. Discord'a erişmek için başlatın.");

            if (eng.ServiceInstalled)
            {
                bool elsewhere = await Task.Run(() => Engine.ServicePointsElsewhere());
                Add("Arka plan hizmeti", elsewhere ? CheckStatus.Warn : CheckStatus.Ok,
                    elsewhere ? "Hizmet başka bir klasördeki GoodbyeDPI'ı çalıştırıyor." : "Kurulu ve bu klasörü kullanıyor.", "DK-704");
            }

            // ------------------------------------------------ discord
            if (inet)
            {
                var res = await Actions.QuickTestAsync();
                if (res != null)
                {
                    int ok = res.Count(x => x.Ok);
                    var fails = res.Where(x => !x.Ok).ToList();
                    string code = "";
                    if (fails.Count > 0)
                    {
                        if (App.Settings.TargetedMode && fails.Any(f => !Domains.Covered(f.Host, App.Settings.TargetDomains))) code = "DK-506";
                        else if (ok > 0) code = "DK-505";
                        else code = fails.GroupBy(f => f.Code).OrderByDescending(g => g.Count()).First().Key;
                    }
                    Add("Discord sunucuları", ok == res.Count ? CheckStatus.Ok : ok == 0 ? CheckStatus.Fail : CheckStatus.Warn,
                        ok + "/" + res.Count + " erişilebilir" + (fails.Count > 0 ? " – açılmayan: " + string.Join(", ", fails.Select(f => f.Host + " (" + f.Detail + ")")) : "."),
                        code);
                }
            }
            bool dRunning = DiscordHelper.IsRunning;
            if (dRunning && running && eng.StartedAt.HasValue && DiscordHelper.StartedBefore(eng.StartedAt.Value))
                Add("Discord uygulaması", CheckStatus.Info, "Discord korumadan önce açılmış; bağlanamıyorsa yeniden başlatın.", "DK-901");
            else
                Add("Discord uygulaması", CheckStatus.Ok, dRunning ? "Açık." : "Kapalı.");

            return items;
        }
    }

    /// <summary>Profilleri bağımsız .cmd betikleri olarak dışa aktarır.</summary>
    public static class Scripts
    {
        public static string Ascii(string s)
        {
            var map = new Dictionary<char, string>
            {
                ['ç'] = "c", ['Ç'] = "C", ['ğ'] = "g", ['Ğ'] = "G", ['ı'] = "i", ['İ'] = "I", ['ö'] = "o", ['Ö'] = "O",
                ['ş'] = "s", ['Ş'] = "S", ['ü'] = "u", ['Ü'] = "U", ['–'] = "-", ['—'] = "-", ['’'] = "'", ['…'] = "...", ['→'] = "->", ['·'] = "-",
            };
            var sb = new StringBuilder();
            foreach (char c in s ?? "")
            {
                if (map.TryGetValue(c, out var r)) sb.Append(r);
                else if (c < 128) sb.Append(c);
            }
            return sb.ToString();
        }

        public static string Slug(string s)
        {
            var a = Ascii(s).ToLowerInvariant();
            var sb = new StringBuilder();
            foreach (char c in a)
                sb.Append(char.IsLetterOrDigit(c) ? c : '_');
            return string.Join("_", sb.ToString().Split(new[] { '_' }, StringSplitOptions.RemoveEmptyEntries));
        }

        static IEnumerable<string> Wrap(string text, int width)
        {
            var words = text.Split(' ');
            var line = new StringBuilder();
            foreach (var w in words)
            {
                if (line.Length + w.Length + 1 > width && line.Length > 0) { yield return line.ToString(); line.Clear(); }
                if (line.Length > 0) line.Append(' ');
                line.Append(w);
            }
            if (line.Length > 0) yield return line.ToString();
        }

        /// <param name="binRef">goodbyedpi klasörü; göreli ise betiğin konumuna göre (%~dp0).</param>
        public static string Build(Profile p, string binRef)
        {
            var sb = new StringBuilder();
            sb.AppendLine("@ECHO OFF");
            sb.AppendLine("REM ================================================================");
            sb.AppendLine("REM  DPI Kalkan - \"" + Ascii(p.Name) + "\" yontemi");
            foreach (var l in Wrap(Ascii(p.Description), 62)) sb.AppendLine("REM  " + l);
            sb.AppendLine("REM  DNS yonlendirme: " + Ascii(p.DnsLabel));
            sb.AppendLine("REM  Calistirinca yonetici izni istenir. Durdurmak icin acilan");
            sb.AppendLine("REM  GoodbyeDPI penceresini kapatin ya da durdur.cmd'yi calistirin.");
            sb.AppendLine("REM  Ayni anda yalnizca BIR betik calistirin.");
            sb.AppendLine("REM ================================================================");
            sb.AppendLine("PUSHD \"%~dp0\"");
            sb.AppendLine("set _arch=x86");
            sb.AppendLine("IF \"%PROCESSOR_ARCHITECTURE%\"==\"AMD64\" (set _arch=x86_64)");
            sb.AppendLine("IF DEFINED PROCESSOR_ARCHITEW6432 (set _arch=x86_64)");
            sb.AppendLine("PUSHD \"" + binRef + "\\%_arch%\"");
            sb.AppendLine("start \"\" goodbyedpi.exe " + p.BuildArgs(null));
            sb.AppendLine("POPD");
            sb.AppendLine("POPD");
            return sb.ToString();
        }

        public static void Write(Profile p, string path, string binRef) =>
            File.WriteAllText(path, Build(p, binRef).Replace("\n", "\r\n").Replace("\r\r\n", "\r\n"), Encoding.ASCII);

        public static void ExportAll(string dir)
        {
            Directory.CreateDirectory(dir);
            int i = 0;
            foreach (var p in Profiles.BuiltIns)
            {
                i++;
                string name = i.ToString("00") + "_" + Slug(p.Name) + ".cmd";
                Write(p, Path.Combine(dir, name), "..\\bin");
            }
            File.WriteAllText(Path.Combine(dir, "durdur.cmd"),
                "@ECHO OFF\r\nREM Calisan tum GoodbyeDPI islemlerini kapatir (yonetici izni ister).\r\n" +
                "powershell -NoProfile -Command \"Start-Process taskkill -ArgumentList '/F /IM goodbyedpi.exe' -Verb RunAs -WindowStyle Hidden\"\r\n", Encoding.ASCII);
            File.WriteAllText(Path.Combine(dir, "sifirla.cmd"),
                "@ECHO OFF\r\nREM GoodbyeDPI hizmetlerini ve WinDivert surucu kaydini kaldirir (eski ayarlari_sifirla.cmd).\r\n" +
                "net session >NUL 2>&1 || (powershell -NoProfile -Command \"Start-Process '%~f0' -Verb RunAs\" & exit /b)\r\n" +
                "taskkill /F /IM goodbyedpi.exe >NUL 2>&1\r\n" +
                "for %%s in (GoodbyeDPI DPIKalkan WinDivert WinDivert14) do (sc stop %%s >NUL 2>&1 & sc delete %%s >NUL 2>&1)\r\n" +
                "ipconfig /flushdns >NUL\r\necho Sifirlama tamamlandi. Bilgisayari yeniden baslatmaniz onerilir.\r\npause\r\n", Encoding.ASCII);
            var readme = new StringBuilder();
            readme.AppendLine("DPI KALKAN - YEDEK BETIKLER");
            readme.AppendLine("===========================");
            readme.AppendLine();
            readme.AppendLine("Bu klasordeki .cmd dosyalari DPI Kalkan uygulamasi OLMADAN GoodbyeDPI'i");
            readme.AppendLine("dogrudan calistirir. Uygulama acilmiyorsa ya da bir yontemi elle denemek");
            readme.AppendLine("istiyorsaniz kullanin. Uygulamayi kullaniyorsaniz bunlara gerek yoktur.");
            readme.AppendLine();
            readme.AppendLine("Kullanim:");
            readme.AppendLine("  1) DPI Kalkan uygulamasi aciksa once tepsiden 'Cikis' deyin.");
            readme.AppendLine("  2) Bir betige cift tiklayin, yonetici iznini onaylayin.");
            readme.AppendLine("  3) Acilan GoodbyeDPI penceresi acik kaldigi surece calisir.");
            readme.AppendLine("  4) Baska bir betik denemeden once pencereyi kapatin ya da durdur.cmd calistirin.");
            readme.AppendLine();
            readme.AppendLine("Siralama: 01'den baslayarak deneyin. Turkiye'de Discord DNS ile de");
            readme.AppendLine("engellendigi icin 'DNS'siz' yazan son uc betik Discord'u ACMAZ; yalnizca");
            readme.AppendLine("karsilastirma icin vardir.");
            readme.AppendLine();
            i = 0;
            foreach (var p in Profiles.BuiltIns)
            {
                i++;
                readme.AppendLine(i.ToString("00") + "  " + Ascii(p.Name));
                foreach (var l in Wrap(Ascii(p.Description), 70)) readme.AppendLine("    " + l);
            }
            readme.AppendLine();
            readme.AppendLine("durdur.cmd  : calisan tum GoodbyeDPI islemlerini kapatir.");
            readme.AppendLine("sifirla.cmd : hizmetleri ve surucu kaydini temizler (sorun olursa).");
            File.WriteAllText(Path.Combine(dir, "BETIKLER_OKUBENI.txt"), readme.ToString().Replace("\n", "\r\n").Replace("\r\r\n", "\r\n"), Encoding.ASCII);
        }
    }
}
