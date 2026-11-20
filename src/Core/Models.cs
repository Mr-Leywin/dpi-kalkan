using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Text;
using System.Xml.Serialization;

namespace DPIKalkan
{
    public enum DnsChoice { None, Yandex, AdGuard, Custom, AdGuardFree, Quad9, OpenDns, Cloudflare, Google }

    public enum UiLevel { Basit, Orta, Detayli }

    /// <summary>GoodbyeDPI'ın DNS yönlendirmesinde kullanılabilecek bir sunucu.</summary>
    public sealed class DnsProvider
    {
        public DnsChoice Id;
        public string Name, V4, V6, Short, Note;
        public int V4Port, V6Port;
        public bool StandardPort => V4Port == 53;
        public string Label => Name + " – " + V4 + ":" + V4Port;
    }

    public static class DnsProviders
    {
        public static readonly List<DnsProvider> All = new List<DnsProvider>
        {
            new DnsProvider { Id = DnsChoice.Yandex, Name = "Yandex", V4 = "77.88.8.8", V4Port = 1253, V6 = "2a02:6b8::feed:0ff", V6Port = 1253,
                Short = "Yandex (1253)",
                Note = "Eski 3/4 numaralı betiklerin kullandığı sunucu. Rusya'da olduğu için yanıtı biraz yavaştır (~100 ms) ama çok kararlıdır." },
            new DnsProvider { Id = DnsChoice.AdGuard, Name = "AdGuard", V4 = "94.140.14.14", V4Port = 5353, V6 = "2a10:50c0::ad1:ff", V6Port = 5353,
                Short = "AdGuard (5353)",
                Note = "Çok hızlı (~20 ms). Reklam ve izleyici alan adlarını da engeller; bazı sitelerde reklam alanları boş görünebilir." },
            new DnsProvider { Id = DnsChoice.AdGuardFree, Name = "AdGuard Filtresiz", V4 = "94.140.14.140", V4Port = 5353, V6 = "2a10:50c0::1:ff", V6Port = 5353,
                Short = "AdGuard filtresiz (5353)",
                Note = "AdGuard'ın hiçbir şeyi engellemeyen sürümü. Hızlıdır." },
            new DnsProvider { Id = DnsChoice.Quad9, Name = "Quad9", V4 = "9.9.9.9", V4Port = 9953, V6 = "2620:fe::fe", V6Port = 9953,
                Short = "Quad9 (9953)",
                Note = "İsviçre merkezli, gizlilik odaklı ve hızlı. Yalnızca zararlı yazılım sitelerini engeller." },
            new DnsProvider { Id = DnsChoice.OpenDns, Name = "OpenDNS", V4 = "208.67.222.222", V4Port = 5353, V6 = "2620:119:35::35", V6Port = 5353,
                Short = "OpenDNS (5353)",
                Note = "Cisco'nun DNS hizmeti. Hızlı ve kararlıdır." },
            new DnsProvider { Id = DnsChoice.Cloudflare, Name = "Cloudflare", V4 = "1.1.1.1", V4Port = 53, V6 = "2606:4700:4700::1111", V6Port = 53,
                Short = "Cloudflare (53)",
                Note = "Standart 53. portu kullanır. Servis sağlayıcınız 53. porttaki trafiğe müdahale ediyorsa işe yaramaz; tarama bunu kendisi kontrol eder." },
            new DnsProvider { Id = DnsChoice.Google, Name = "Google", V4 = "8.8.8.8", V4Port = 53, V6 = "2001:4860:4860::8888", V6Port = 53,
                Short = "Google (53)",
                Note = "Standart 53. portu kullanır. Türkiye'de geçmişte bu adrese yapılan sorgular yönlendirilmişti; tek başına güvenilir değildir." },
        };

        public static DnsProvider Get(DnsChoice c) => All.FirstOrDefault(p => p.Id == c);
    }

    /// <summary>Bir GoodbyeDPI çalışma yöntemi (parametre seti).</summary>
    public class Profile
    {
        public string Id { get; set; } = Guid.NewGuid().ToString("N");
        public string Name { get; set; } = "Yeni Profil";
        public string Description { get; set; } = "";
        [XmlIgnore] public bool BuiltIn { get; set; }
        [XmlIgnore] public string Group { get; set; } = "Özel";
        [XmlIgnore] public int Priority { get; set; } = 1000;

        /// <summary>"" (yok) veya "-1" … "-9"</summary>
        public string Mode { get; set; } = "-9";
        public DnsChoice Dns { get; set; } = DnsChoice.Yandex;
        public string CustomDns4 { get; set; } = "";
        public int CustomDns4Port { get; set; } = 53;
        public string CustomDns6 { get; set; } = "";
        public int CustomDns6Port { get; set; } = 53;
        /// <summary>"" kapalı, "auto" otomatik, aksi halde sayı.</summary>
        public string Ttl { get; set; } = "";
        public int HttpFrag { get; set; }
        public int HttpsFrag { get; set; }
        public bool PassiveDpi { get; set; }
        public bool HostReplace { get; set; }
        public bool HostRemoveSpace { get; set; }
        public bool HostMixCase { get; set; }
        public bool WrongChksum { get; set; }
        public bool WrongSeq { get; set; }
        public bool NativeFrag { get; set; }
        public bool ReverseFrag { get; set; }
        public bool BlockQuic { get; set; }
        public bool FragBySni { get; set; }
        public bool MaxPayload { get; set; }
        public bool AllowNoSni { get; set; }
        public int FakeGen { get; set; }
        public int FakeResend { get; set; }
        public string ExtraArgs { get; set; } = "";
        public bool UseRaw { get; set; }
        public string RawArgs { get; set; } = "";

        public bool UsesDnsRedirect =>
            UseRaw ? (RawArgs ?? "").Contains("--dns-addr")
                   : Dns == DnsChoice.Custom ? !string.IsNullOrWhiteSpace(CustomDns4) || !string.IsNullOrWhiteSpace(CustomDns6)
                   : Dns != DnsChoice.None;

        /// <summary>Yönlendirilen IPv4 DNS sunucusu (yoksa null).</summary>
        public (string Addr, int Port)? DnsTarget
        {
            get
            {
                if (UseRaw)
                {
                    var t = Tokens(RawArgs);
                    int i = t.IndexOf("--dns-addr");
                    if (i < 0 || i + 1 >= t.Count) return null;
                    int port = 53;
                    int j = t.IndexOf("--dns-port");
                    if (j >= 0 && j + 1 < t.Count) int.TryParse(t[j + 1], out port);
                    return (t[i + 1], port);
                }
                if (Dns == DnsChoice.Custom)
                    return string.IsNullOrWhiteSpace(CustomDns4) ? ((string, int)?)null : (CustomDns4.Trim(), CustomDns4Port);
                var p = DnsProviders.Get(Dns);
                return p == null ? ((string, int)?)null : (p.V4, p.V4Port);
            }
        }

        static List<string> Tokens(string s) =>
            (s ?? "").Split(new[] { ' ', '\t', '\r', '\n' }, StringSplitOptions.RemoveEmptyEntries).ToList();

        public string DnsLabel
        {
            get
            {
                if (UseRaw) return UsesDnsRedirect ? "Elle tanımlı" : "Kapalı";
                if (Dns == DnsChoice.Custom) return UsesDnsRedirect ? CustomDns4 + ":" + CustomDns4Port : "Kapalı";
                return DnsProviders.Get(Dns)?.Short ?? "Kapalı";
            }
        }

        public string BuildArgs(string blacklistPath)
        {
            var a = new List<string>();
            if (UseRaw)
            {
                if (!string.IsNullOrWhiteSpace(RawArgs)) a.Add(RawArgs.Trim());
            }
            else
            {
                if (!string.IsNullOrEmpty(Mode)) a.Add(Mode);
                if (HttpFrag > 0) a.Add("-f " + HttpFrag);
                if (HttpsFrag > 0) a.Add("-e " + HttpsFrag);
                if (PassiveDpi) a.Add("-p");
                if (HostReplace) a.Add("-r");
                if (HostRemoveSpace) a.Add("-s");
                if (HostMixCase) a.Add("-m");
                if (Ttl == "auto") a.Add("--auto-ttl");
                else if (int.TryParse(Ttl, out int ttl) && ttl > 0) a.Add("--set-ttl " + ttl);
                if (WrongChksum) a.Add("--wrong-chksum");
                if (WrongSeq) a.Add("--wrong-seq");
                if (NativeFrag) a.Add("--native-frag");
                if (ReverseFrag) a.Add("--reverse-frag");
                if (FragBySni) a.Add("--frag-by-sni");
                if (MaxPayload) a.Add("--max-payload");
                if (BlockQuic) a.Add("-q");
                if (FakeGen > 0) a.Add("--fake-gen " + Math.Min(30, FakeGen));
                if (FakeResend > 1) a.Add("--fake-resend " + Math.Min(20, FakeResend));
                if (Dns == DnsChoice.Custom)
                {
                    if (!string.IsNullOrWhiteSpace(CustomDns4))
                        a.Add("--dns-addr " + CustomDns4.Trim() + " --dns-port " + CustomDns4Port);
                    if (!string.IsNullOrWhiteSpace(CustomDns6))
                        a.Add("--dnsv6-addr " + CustomDns6.Trim() + " --dnsv6-port " + CustomDns6Port);
                }
                else
                {
                    var d = DnsProviders.Get(Dns);
                    if (d != null)
                        a.Add("--dns-addr " + d.V4 + " --dns-port " + d.V4Port + " --dnsv6-addr " + d.V6 + " --dnsv6-port " + d.V6Port);
                }
                if (!string.IsNullOrWhiteSpace(ExtraArgs)) a.Add(ExtraArgs.Trim());
            }
            if (blacklistPath != null)
            {
                a.Add("--blacklist \"" + blacklistPath + "\"");
                if (AllowNoSni && !UseRaw) a.Add("--allow-no-sni");
            }
            return string.Join(" ", a);
        }

        public Profile CloneAsCustom()
        {
            var p = (Profile)MemberwiseClone();
            p.Id = Guid.NewGuid().ToString("N");
            p.BuiltIn = false;
            p.Group = "Özel";
            p.Priority = 1000;
            p.Name = Name + " (kopya)";
            return p;
        }

        public override string ToString() => Name;
    }

    public static class Profiles
    {
        public const string GroupRecommended = "Önerilen";
        public const string GroupAlt = "Alternatif yöntemler";
        public const string GroupNoDns = "DNS yönlendirmesiz (eski)";

        static int order;
        static Profile B(string id, string group, string name, string desc, Action<Profile> cfg)
        {
            var p = new Profile { Id = id, Name = name, Description = desc, BuiltIn = true, Mode = "", Dns = DnsChoice.None, Group = group, Priority = ++order };
            cfg(p);
            return p;
        }

        public static readonly List<Profile> BuiltIns = new List<Profile>
        {
            B("tr-onerilen", GroupRecommended, "Türkiye – Önerilen",
              "Discord için ilk tercih. En kapsamlı mod (-9) + Yandex DNS yönlendirmesi. Eski 3 ve 4 numaralı betiklerle aynı ayar.",
              p => { p.Mode = "-9"; p.Dns = DnsChoice.Yandex; }),
            B("tr-adguard", GroupRecommended, "Türkiye – AdGuard DNS",
              "-9 modu + AdGuard DNS (port 5353). Yandex'ten çok daha hızlı yanıt verir; reklam alan adlarını da engeller.",
              p => { p.Mode = "-9"; p.Dns = DnsChoice.AdGuard; }),
            B("tr-quad9", GroupRecommended, "Türkiye – Quad9 DNS",
              "-9 modu + Quad9 DNS (port 9953). Hızlı ve gizlilik odaklı, reklam filtresi yok.",
              p => { p.Mode = "-9"; p.Dns = DnsChoice.Quad9; }),
            B("tr-opendns", GroupRecommended, "Türkiye – OpenDNS",
              "-9 modu + OpenDNS (port 5353). Diğer DNS sunucuları yavaşsa ya da yanıt vermiyorsa dene.",
              p => { p.Mode = "-9"; p.Dns = DnsChoice.OpenDns; }),

            B("tr-autottl", GroupAlt, "Otomatik TTL (-5)",
              "Sahte paketlerin TTL değerini sunucuya olan mesafeye göre otomatik ayarlar. Sabit TTL'li yöntemler çalışmayı bıraktıysa dene.",
              p => { p.Mode = "-5"; p.Dns = DnsChoice.Yandex; }),
            B("tr-eski1-dns", GroupAlt, "Eski Betik 1 + DNS (TTL 3)",
              "1_bunu_dene.cmd'nin DNS yönlendirmesi eklenmiş hali: yalnızca TTL 3 sahte paket + Yandex DNS.",
              p => { p.Ttl = "3"; p.Dns = DnsChoice.Yandex; }),
            B("tr-eski2-dns", GroupAlt, "Eski Betik 2 + DNS (-4, TTL 5)",
              "2_bunu_dene.cmd'nin DNS yönlendirmesi eklenmiş hali: -4 modu + TTL 5 sahte paket + Yandex DNS.",
              p => { p.Mode = "-4"; p.Ttl = "5"; p.Dns = DnsChoice.Yandex; }),
            B("tr-wrongseq", GroupAlt, "Yanlış Sıra Numarası (-6)",
              "Sahte paketleri geçmiş bir TCP sıra numarasıyla gönderir. Bazı modemlerde -9'dan daha kararlı çalışır.",
              p => { p.Mode = "-6"; p.Dns = DnsChoice.Yandex; }),
            B("tr-wrongchk", GroupAlt, "Yanlış Sağlama Toplamı (-7)",
              "Sahte paketleri hatalı TCP sağlama toplamıyla gönderir. Sanal makinelerde ve bazı modemlerde çalışmayabilir.",
              p => { p.Mode = "-7"; p.Dns = DnsChoice.Yandex; }),
            B("tr-mode8", GroupAlt, "Sıra + Sağlama (-8)",
              "-6 ve -7 yöntemlerini birlikte kullanır; QUIC engellemesi yoktur (-9'dan tek farkı).",
              p => { p.Mode = "-8"; p.Dns = DnsChoice.Yandex; }),
            B("tr-fakegen", GroupAlt, "Güçlendirilmiş Sahte Paket",
              "-9 modu + 5 adet rastgele sahte paket. Sıkı DPI uygulayan servis sağlayıcılar için.",
              p => { p.Mode = "-9"; p.FakeGen = 5; p.Dns = DnsChoice.Yandex; }),
            B("tr-resend", GroupAlt, "Tekrarlı Sahte Paket",
              "-9 modu + 3 sahte paket, her biri 3 kez gönderilir. Paket kaybı olan (Wi-Fi, mobil) hatlarda daha güvenilirdir.",
              p => { p.Mode = "-9"; p.FakeGen = 3; p.FakeResend = 3; p.Dns = DnsChoice.Yandex; }),
            B("tr-e1", GroupAlt, "Tek Bayt Parçalama",
              "-9 modu ama ilk HTTPS paketi 1 baytlık parçalara bölünür. DPI site adını hiç okuyamaz; bağlantı açılışı biraz yavaşlar.",
              p => { p.Mode = "-9"; p.HttpsFrag = 1; p.Dns = DnsChoice.Yandex; }),
            B("tr-sni", GroupAlt, "Site Adından Bölme (SNI)",
              "-9 modu + paketi tam site adının (SNI) başladığı yerden böler. Site adı iki ayrı pakete düşer.",
              p => { p.Mode = "-9"; p.FragBySni = true; p.Dns = DnsChoice.Yandex; }),
            B("tr-native", GroupAlt, "Düz Sıralı Parçalama",
              "-9 ile aynı ama parçalar ters değil normal sırayla gönderilir. Ters sıralı parçaları kabul etmeyen ağlar için.",
              p => { p.HttpFrag = 2; p.HttpsFrag = 2; p.NativeFrag = true; p.WrongSeq = true; p.WrongChksum = true; p.MaxPayload = true; p.BlockQuic = true; p.Dns = DnsChoice.Yandex; }),
            B("tr-alt", GroupAlt, "Alternatif (-5 + 1 bayt + sahte)",
              "Otomatik TTL + 1 baytlık parçalama + 5 sahte paket + QUIC engeli. GoodbyeDPI'ın 'ALT' betiklerindeki yöntem.",
              p => { p.Mode = "-5"; p.HttpsFrag = 1; p.BlockQuic = true; p.FakeGen = 5; p.Dns = DnsChoice.Yandex; }),
            B("tr-uyumlu", GroupAlt, "En Uyumlu Klasik (-1)",
              "Sahte paket kullanmayan klasik yöntem: paketleri 2 baytlık parçalara böler. Yavaş ama her ağda çalışır.",
              p => { p.Mode = "-1"; p.Dns = DnsChoice.Yandex; }),
            B("tr-klasik2", GroupAlt, "Klasik Hızlı (-2)",
              "-1'in daha hızlı hali: HTTPS 40 baytlık parçalara bölünür.",
              p => { p.Mode = "-2"; p.Dns = DnsChoice.Yandex; }),
            B("dns-only", GroupAlt, "Yalnızca DNS Yönlendirme",
              "Hiçbir DPI hilesi uygulamaz, yalnızca DNS'i yönlendirir. Servis sağlayıcınız sadece DNS ile engelliyorsa en hafif seçenek.",
              p => { p.Dns = DnsChoice.Yandex; }),

            B("eski-1", GroupNoDns, "Eski Betik 1 (TTL 3, DNS'siz)",
              "1_bunu_dene.cmd ile birebir aynı. DNS yönlendirmesi olmadığı için Türkiye'de Discord'u açmaz; karşılaştırma için.",
              p => { p.Ttl = "3"; }),
            B("eski-2", GroupNoDns, "Eski Betik 2 (-4, TTL 5, DNS'siz)",
              "2_bunu_dene.cmd ile birebir aynı. DNS yönlendirmesi olmadığı için Türkiye'de Discord'u açmaz; karşılaştırma için.",
              p => { p.Mode = "-4"; p.Ttl = "5"; }),
            B("sade", GroupNoDns, "Sade (-9, DNS'siz)",
              "Yalnızca -9 modu. Windows DNS'ini 'DNS Ayarları' sayfasından değiştirdiyseniz yeterli olabilir.",
              p => { p.Mode = "-9"; }),
        };
    }

    /// <summary>Profili arkadaşlarla paylaşmak için kısa metin kodu (DKP1:…).</summary>
    public static class ProfileCode
    {
        static readonly XmlSerializer Ser = new XmlSerializer(typeof(Profile));

        public static string ToCode(Profile p)
        {
            using (var ms = new MemoryStream())
            {
                using (var z = new System.IO.Compression.DeflateStream(ms, System.IO.Compression.CompressionLevel.Optimal, true))
                using (var w = new StreamWriter(z, new UTF8Encoding(false)))
                    Ser.Serialize(w, p);
                return "DKP1:" + Convert.ToBase64String(ms.ToArray());
            }
        }

        public static bool TryFromCode(string code, out Profile p, out string error)
        {
            p = null;
            error = "";
            code = (code ?? "").Trim();
            int i = code.IndexOf("DKP1:", StringComparison.OrdinalIgnoreCase);
            if (i < 0) { error = "Metin 'DKP1:' ile başlayan bir profil kodu içermiyor."; return false; }
            string b64 = new string(code.Substring(i + 5).TakeWhile(c => char.IsLetterOrDigit(c) || c == '+' || c == '/' || c == '=').ToArray());
            try
            {
                using (var ms = new MemoryStream(Convert.FromBase64String(b64)))
                using (var z = new System.IO.Compression.DeflateStream(ms, System.IO.Compression.CompressionMode.Decompress))
                using (var r = new StreamReader(z, Encoding.UTF8))
                    p = (Profile)Ser.Deserialize(r);
            }
            catch { error = "Profil kodu bozuk ya da eksik kopyalanmış."; return false; }
            if ((p.RawArgs ?? "").Contains("--blacklist") || (p.ExtraArgs ?? "").Contains("--blacklist"))
            { error = "Kod güvenli olmayan bir parametre (--blacklist) içeriyor."; p = null; return false; }
            p.Id = Guid.NewGuid().ToString("N");
            p.BuiltIn = false;
            p.Group = "Özel";
            p.Priority = 1000;
            if (string.IsNullOrWhiteSpace(p.Name)) p.Name = "İçe aktarılan profil";
            return true;
        }
    }

    public class ThemeColor
    {
        public string Key { get; set; }
        public string Hex { get; set; }
    }

    public class DnsBackup
    {
        public string Adapter { get; set; }
        public bool V4Dhcp { get; set; }
        public List<string> V4 { get; set; } = new List<string>();
        public bool V6Dhcp { get; set; }
        public List<string> V6 { get; set; } = new List<string>();
    }

    public class AppSettings
    {
        public int Version { get; set; } = 2;
        public string SelectedProfileId { get; set; } = "tr-onerilen";
        public List<Profile> CustomProfiles { get; set; } = new List<Profile>();
        public bool ServiceMode { get; set; }
        public bool StartWithWindows { get; set; }
        public bool AutoStartProtection { get; set; } = true;
        public bool CloseToTray { get; set; } = true;
        public bool HideFromTaskbar { get; set; } = true;
        public bool AutoRestart { get; set; } = true;
        public bool Notifications { get; set; } = true;
        public bool FlushDns { get; set; } = true;
        public bool TestAfterStart { get; set; } = true;
        public bool SuggestDiscordRestart { get; set; } = true;
        public bool CheckDnsOnStart { get; set; } = true;
        public bool ScanAll { get; set; }
        public bool TargetedMode { get; set; }
        public bool TrayHintShown { get; set; }
        public UiLevel UiLevel { get; set; } = UiLevel.Basit;
        public string ThemeId { get; set; } = "system";
        public List<ThemeColor> ThemeOverrides { get; set; } = new List<ThemeColor>();
        public DateTime? LastScanTime { get; set; }
        public string LastScanResult { get; set; } = "";
        public List<DnsBackup> DnsBackups { get; set; } = new List<DnsBackup>();
        /// <summary>Erişim kesilince çalışan yöntemi kendiliğinden bul.</summary>
        public bool AutoHeal { get; set; } = true;
        /// <summary>Bağlantı bekçisi aralığı (dakika, 0 = kapalı).</summary>
        public int WatchMinutes { get; set; } = 15;
        public bool Hotkey { get; set; }
        public List<ProxyEntry> Proxies { get; set; } = new List<ProxyEntry>();
        public List<ProxyApp> ProxyApps { get; set; } = new List<ProxyApp>();
        public ProxyBackup SystemProxyBackup { get; set; }
        public string SystemProxyId { get; set; } = "";
        public List<string> TargetDomains { get; set; } = new List<string>();
        public List<string> TestHosts { get; set; } = new List<string>();

        public static readonly string[] DiscordDomains =
        {
            "discord.com", "discord.gg", "discordapp.com", "discordapp.net", "discord.media",
            "discordcdn.com", "discord.dev", "discord.new", "discord.gift", "discordstatus.com",
            "dis.gd", "discord.co"
        };

        public static readonly string[] DefaultTestHosts =
        {
            "discord.com", "gateway.discord.gg", "cdn.discordapp.com", "media.discordapp.net", "updates.discord.com", "discordapp.com"
        };

        static readonly string[] OldDefaultTestHosts =
        {
            "discord.com", "gateway.discord.gg", "cdn.discordapp.com", "media.discordapp.net"
        };

        public void Normalize()
        {
            if (CustomProfiles == null) CustomProfiles = new List<Profile>();
            if (ThemeOverrides == null) ThemeOverrides = new List<ThemeColor>();
            if (DnsBackups == null) DnsBackups = new List<DnsBackup>();
            if (Proxies == null) Proxies = new List<ProxyEntry>();
            if (ProxyApps == null) ProxyApps = new List<ProxyApp>();
            ProxyBridge.AssignPorts(Proxies);
            ProxyApps.RemoveAll(a => a == null || string.IsNullOrEmpty(a.ExePath));
            if (SystemProxyId == null) SystemProxyId = "";
            if (WatchMinutes < 0) WatchMinutes = 0;
            if (string.IsNullOrEmpty(ThemeId)) ThemeId = "system";
            if (TargetDomains == null || TargetDomains.Count == 0) TargetDomains = DiscordDomains.ToList();
            if (TestHosts == null || TestHosts.Count == 0 || TestHosts.SequenceEqual(OldDefaultTestHosts)) TestHosts = DefaultTestHosts.ToList();
            TargetDomains = TargetDomains.Select(d => d.Trim().ToLowerInvariant()).Where(d => d.Length > 0).Distinct().ToList();
            TestHosts = TestHosts.Select(d => d.Trim()).Where(d => d.Length > 0).Distinct(StringComparer.OrdinalIgnoreCase).ToList();
            ThemeOverrides = ThemeOverrides.Where(o => o != null && !string.IsNullOrEmpty(o.Key) && !string.IsNullOrEmpty(o.Hex))
                                           .GroupBy(o => o.Key).Select(g => g.Last()).ToList();
            foreach (var p in CustomProfiles) { p.BuiltIn = false; p.Group = "Özel"; p.Priority = 1000; }
            if (Version < 2)
            {
                // 1.0 → 2.0: eski DNS'siz profiller seçiliyse önerilene dön
                if (SelectedProfileId == "eski-1" || SelectedProfileId == "eski-2" || SelectedProfileId == "sade")
                    SelectedProfileId = "tr-onerilen";
                Version = 2;
            }
        }
    }

    public static class SettingsStore
    {
        static readonly XmlSerializer Ser = new XmlSerializer(typeof(AppSettings));

        public static string FilePath => Path.Combine(App.DataDir, "ayarlar.xml");
        public static bool LastSaveFailed { get; private set; }

        public static AppSettings Load()
        {
            AppSettings s = null;
            try
            {
                if (File.Exists(FilePath))
                {
                    using (var fs = File.OpenRead(FilePath))
                        s = (AppSettings)Ser.Deserialize(fs);
                }
            }
            catch (Exception ex)
            {
                try { File.Copy(FilePath, FilePath + ".bozuk", true); } catch { }
                App.Log("Ayar dosyası okunamadı, varsayılanlar kullanılıyor: " + ex.Message, LogLevel.Warn);
            }
            if (s == null) s = new AppSettings();
            s.Normalize();
            return s;
        }

        /// <summary>Ayarları başka bir dosyaya yedekler (başka bilgisayara taşımak ya da paylaşmak için).</summary>
        public static void Export(AppSettings s, string path)
        {
            using (var fs = File.Create(path)) Ser.Serialize(fs, s);
        }

        /// <summary>Yedek dosyasını okur ve doğrular.</summary>
        public static AppSettings Import(string path)
        {
            AppSettings s;
            using (var fs = File.OpenRead(path)) s = (AppSettings)Ser.Deserialize(fs);
            s.Normalize();
            foreach (var p in s.CustomProfiles)
                if ((p.RawArgs ?? "").Contains("--blacklist") || (p.ExtraArgs ?? "").Contains("--blacklist"))
                    throw new InvalidDataException("Yedekteki '" + p.Name + "' profili güvenli olmayan bir parametre içeriyor.");
            return s;
        }

        public static void Save(AppSettings s)
        {
            if (App.Preview) return;
            try
            {
                Directory.CreateDirectory(App.DataDir);
                string tmp = FilePath + ".tmp";
                using (var fs = File.Create(tmp))
                    Ser.Serialize(fs, s);
                if (File.Exists(FilePath)) File.Replace(tmp, FilePath, null);
                else File.Move(tmp, FilePath);
                if (LastSaveFailed) App.Problems.Clear("DK-104");
                LastSaveFailed = false;
            }
            catch (Exception ex)
            {
                LastSaveFailed = true;
                App.Log("Ayarlar kaydedilemedi: " + ex.Message, LogLevel.Error);
                App.Problems.Report("DK-104", ex.Message);
            }
        }
    }

    public static class Domains
    {
        public static string Clean(string input)
        {
            if (input == null) return null;
            string s = input.Trim().ToLowerInvariant();
            int i = s.IndexOf("://", StringComparison.Ordinal);
            if (i >= 0) s = s.Substring(i + 3);
            i = s.IndexOfAny(new[] { '/', '?', '#', ':' });
            if (i >= 0) s = s.Substring(0, i);
            if (s.StartsWith("*.")) s = s.Substring(2);
            s = s.Trim('.');
            if (s.Length < 3 || !s.Contains(".")) return null;
            foreach (char c in s)
                if (!(char.IsLetterOrDigit(c) || c == '-' || c == '.')) return null;
            try { s = new IdnMapping().GetAscii(s); } catch { return null; }
            return s;
        }

        public static string HostOf(string hostOrUrl)
        {
            if (string.IsNullOrEmpty(hostOrUrl)) return hostOrUrl;
            if (hostOrUrl.Contains("://")) { try { return new Uri(hostOrUrl).Host; } catch { } }
            return hostOrUrl;
        }

        /// <summary>Alan adı hedef listesindeki bir alan adının kendisi ya da alt alan adı mı?</summary>
        public static bool Covered(string host, IEnumerable<string> list)
        {
            host = (HostOf(host) ?? "").ToLowerInvariant();
            return list.Any(d => host == d || host.EndsWith("." + d));
        }
    }
}
