using System;
using System.Collections.Generic;
using System.Linq;

namespace DPIKalkan
{
    public enum Severity { Info, Warning, Error }

    /// <summary>Hata kodu sayfasındaki "Düzelt" düğmelerinin yapabileceği işlemler.</summary>
    public enum FixKind
    {
        None, RestartAsAdmin, OpenAppFolder, ResetAll, ResolveConflicts, FlushDns, FindBest, OpenProfiles,
        OpenDnsPage, TestDnsServers, StartBfe, OpenWindowsSecurity, RestartDiscord, StartProtection, OpenLog,
        OpenSites, OpenProxySettings, OpenDateTime, DisableServiceMode, OpenHosts, SiteAnalysis, OpenSettings, OpenProxyPage
    }

    public sealed class ErrorInfo
    {
        public string Code, Category, Title, Summary;
        public Severity Severity = Severity.Error;
        public string[] Causes = new string[0];
        public string[] Steps = new string[0];
        public FixKind Fix, Fix2;
        public string Detect = "";

        public static string FixLabel(FixKind f)
        {
            switch (f)
            {
                case FixKind.RestartAsAdmin: return "Yönetici olarak yeniden aç";
                case FixKind.OpenAppFolder: return "Uygulama klasörünü aç";
                case FixKind.ResetAll: return "Tümünü sıfırla";
                case FixKind.ResolveConflicts: return "Çakışmaları gider";
                case FixKind.FlushDns: return "DNS önbelleğini temizle";
                case FixKind.FindBest: return "En iyi yöntemi bul";
                case FixKind.OpenProfiles: return "Profillere git";
                case FixKind.OpenDnsPage: return "DNS ayarlarına git";
                case FixKind.TestDnsServers: return "DNS sunucularını test et";
                case FixKind.StartBfe: return "BFE hizmetini başlat";
                case FixKind.OpenWindowsSecurity: return "Windows Güvenliği'ni aç";
                case FixKind.RestartDiscord: return "Discord'u yeniden başlat";
                case FixKind.StartProtection: return "Korumayı yeniden başlat";
                case FixKind.OpenLog: return "Günlüğü aç";
                case FixKind.OpenSites: return "Hedef sitelere git";
                case FixKind.OpenProxySettings: return "Proxy ayarlarını aç";
                case FixKind.OpenDateTime: return "Tarih/saat ayarlarını aç";
                case FixKind.DisableServiceMode: return "Uygulama moduna geç";
                case FixKind.OpenHosts: return "hosts dosyasını göster";
                case FixKind.SiteAnalysis: return "Site analizi yap";
                case FixKind.OpenSettings: return "Ayarlara git";
                case FixKind.OpenProxyPage: return "Proxy sayfasına git";
                default: return "";
            }
        }
    }

    /// <summary>Uygulamanın tanıdığı tüm hata/uyarı türleri ve çözüm yolları.</summary>
    public static class ErrorCatalog
    {
        public static readonly string[] Categories =
        {
            "Uygulama ve yetki", "Sürücü (WinDivert)", "Çakışma", "DNS", "Erişim engeli",
            "GoodbyeDPI", "Hizmet ve başlangıç", "Ağ bağlantısı", "Discord", "Proxy"
        };

        static ErrorInfo E(string code, string cat, Severity sev, string title, string summary,
                           string[] causes, string[] steps, FixKind fix = FixKind.None, FixKind fix2 = FixKind.None, string detect = "")
            => new ErrorInfo { Code = code, Category = cat, Severity = sev, Title = title, Summary = summary, Causes = causes, Steps = steps, Fix = fix, Fix2 = fix2, Detect = detect };

        public static readonly List<ErrorInfo> All = new List<ErrorInfo>
        {
            // ------------------------------------------------------------ 1xx uygulama
            E("DK-101", "Uygulama ve yetki", Severity.Error, "Yönetici izni yok",
              "DPI Kalkan ağ sürücüsünü (WinDivert) yükleyebilmek için yönetici olarak çalışmalıdır.",
              new[] { "Uygulama normal kullanıcı olarak açıldı.", "Windows'un 'Bu uygulamanın değişiklik yapmasına izin verilsin mi?' sorusuna 'Hayır' denildi." },
              new[] { "Uygulamayı kapatın.", "DPIKalkan.exe'ye sağ tıklayıp 'Yönetici olarak çalıştır'ı seçin.",
                      "Ayarlar → 'Windows açılışında başlat'ı açarsanız sonraki açılışlarda izin sorulmadan yönetici olarak başlar." },
              FixKind.RestartAsAdmin, detect: "Uygulama açılırken ve Sorun Bul taramasında kontrol edilir."),
            E("DK-102", "Uygulama ve yetki", Severity.Error, "GoodbyeDPI dosyaları eksik",
              "bin klasöründe goodbyedpi.exe, WinDivert.dll veya WinDivert64.sys bulunamadı.",
              new[] { "Antivirüs programı dosyaları karantinaya aldı ya da sildi.", "Zip dosyası tam olarak çıkarılmadı.", "Dosyalar elle taşındı veya silindi." },
              new[] { "Antivirüsün karantina listesine bakın; DPI Kalkan dosyalarını geri yükleyin.",
                      "Antivirüste DPI Kalkan klasörünü istisnalara (dışlamalara) ekleyin.",
                      "Zip dosyasını yeniden, tamamen bir klasöre çıkarın ve uygulamayı oradan çalıştırın." },
              FixKind.OpenAppFolder, detect: "Koruma başlatılırken ve Sorun Bul taramasında dosyalar kontrol edilir."),
            E("DK-103", "Uygulama ve yetki", Severity.Warning, "Uygulama geçici bir klasörden çalışıyor",
              "Uygulama zip dosyasının içinden ya da Temp klasöründen çalıştırılmış. Ayarlar kaybolabilir; arka plan hizmeti ve açılış görevi bozulur.",
              new[] { "Zip dosyası çift tıklanarak açıldı ve uygulama doğrudan içinden başlatıldı.", "Uygulama İndirilenler/Temp gibi geçici bir yerde duruyor." },
              new[] { "Uygulamadan çıkın.", "Zip dosyasına sağ tıklayıp 'Tümünü ayıkla' deyin.",
                      "Klasörü kalıcı bir yere koyun (ör. C:\\Program Files\\DPI Kalkan ya da Belgeler).", "Uygulamayı yeni yerinden çalıştırın." },
              FixKind.OpenAppFolder, detect: "Uygulama açılırken klasör yolu kontrol edilir."),
            E("DK-104", "Uygulama ve yetki", Severity.Warning, "Ayarlar kaydedilemiyor",
              "Uygulama 'veri' klasörüne yazamıyor; yaptığınız değişiklikler kapatınca kaybolur.",
              new[] { "Klasör salt okunur ya da korumalı bir konumda.", "Disk dolu.", "Windows Güvenliği'nin 'Denetimli klasör erişimi' özelliği yazmayı engelliyor." },
              new[] { "Uygulamayı yazılabilir bir klasöre taşıyın.",
                      "Windows Güvenliği → Virüs ve tehdit koruması → Fidye yazılımı koruması → Denetimli klasör erişimi'nde DPI Kalkan'a izin verin.",
                      "Diskte yer açın." },
              FixKind.OpenAppFolder, FixKind.OpenWindowsSecurity),

            // ------------------------------------------------------------ 2xx sürücü
            E("DK-201", "Sürücü (WinDivert)", Severity.Error, "WinDivert sürücüsü yüklenemedi",
              "GoodbyeDPI'ın paketleri yakalamak için kullandığı ağ sürücüsü başlatılamadı.",
              new[] { "Antivirüs programı sürücüyü engelliyor.", "Başka bir programın yüklediği farklı sürüm WinDivert sürücüsü bellekte kalmış.",
                      "Windows bekleyen bir güncelleme için yeniden başlatılmayı bekliyor." },
              new[] { "Sorun Giderme → 'Tümünü sıfırla' düğmesine basın.", "Bilgisayarı yeniden başlatın.",
                      "Antivirüs programında DPI Kalkan klasörünü istisnalara ekleyin.", "Korumayı tekrar başlatın." },
              FixKind.ResetAll, FixKind.OpenLog, "GoodbyeDPI'ın 'Error opening filter' çıktısından algılanır."),
            E("DK-202", "Sürücü (WinDivert)", Severity.Error, "Sürücü dosyaları bulunamadı",
              "GoodbyeDPI, WinDivert.dll veya WinDivert64.sys dosyasını bulamadığını bildirdi.",
              new[] { "Antivirüs dosyayı sildi ya da karantinaya aldı.", "Paket eksik çıkarıldı." },
              new[] { "Antivirüs karantinasından dosyaları geri yükleyin ve klasörü istisnaya ekleyin.", "Zip'i yeniden çıkarın." },
              FixKind.OpenAppFolder, detect: "GoodbyeDPI çıktısındaki 'were not found' ifadesinden algılanır."),
            E("DK-203", "Sürücü (WinDivert)", Severity.Error, "Sürücü imzası doğrulanamadı",
              "Windows, WinDivert sürücüsünün dijital imzasını kabul etmedi.",
              new[] { "Windows çok eski ve SHA-2 imzalarını desteklemiyor.", "Sürücü dosyası bozulmuş.", "Sürücü imza zorlaması ayarlarıyla oynanmış." },
              new[] { "Windows Update ile tüm güncellemeleri yükleyin.", "Paketi yeniden indirip çıkarın.", "Bilgisayarı yeniden başlatın." },
              FixKind.OpenAppFolder, detect: "GoodbyeDPI çıktısındaki 'digital signature' ifadesinden algılanır."),
            E("DK-204", "Sürücü (WinDivert)", Severity.Error, "Sürücü güvenlik yazılımı tarafından engellendi",
              "Antivirüs programınız WinDivert sürücüsünün yüklenmesini engelledi. WinDivert bir virüs değildir; ağ paketlerini işlediği için 'RiskTool' olarak işaretlenir.",
              new[] { "Kaspersky, Norton, ESET, Bitdefender, Avast/AVG gibi antivirüsler WinDivert'i engelleyebilir." },
              new[] { "Antivirüs programınızı açın.",
                      "Kaspersky: Ayarlar → Tehditler ve İstisnalar → İstisnaları yönet → Ekle → DPI Kalkan klasörü.",
                      "Norton: Ayarlar → Antivirüs → Taramalar ve Riskler → Taramalardan Dışlanacak Öğeler → DPI Kalkan klasörü.",
                      "Karantinaya alınan dosya varsa geri yükleyin.", "Korumayı yeniden başlatın." },
              FixKind.OpenAppFolder, FixKind.OpenWindowsSecurity, "GoodbyeDPI çıktısındaki 'blocked by security software' ifadesinden algılanır."),
            E("DK-205", "Sürücü (WinDivert)", Severity.Error, "Temel Filtreleme Altyapısı (BFE) kapalı",
              "WinDivert'in çalışması için gereken 'Base Filtering Engine' Windows hizmeti çalışmıyor.",
              new[] { "Hizmet bir optimizasyon programı ya da kullanıcı tarafından devre dışı bırakıldı." },
              new[] { "Aşağıdaki 'BFE hizmetini başlat' düğmesini deneyin.",
                      "Olmazsa: Win+R → services.msc → 'Base Filtering Engine' → Başlangıç türü: Otomatik → Başlat.",
                      "Bilgisayarı yeniden başlatın." },
              FixKind.StartBfe, detect: "GoodbyeDPI çıktısından ve Sorun Bul taramasında hizmet durumundan algılanır."),
            E("DK-206", "Sürücü (WinDivert)", Severity.Error, "Uyumsuz WinDivert sürümü yüklü",
              "Bellekte başka bir programın yüklediği farklı sürümde WinDivert sürücüsü var.",
              new[] { "zapret, eski GoodbyeDPI sürümleri ya da WinDivert kullanan başka bir araç çalıştı." },
              new[] { "WinDivert kullanan diğer programları kapatın.", "'Tümünü sıfırla' düğmesine basın.", "Bilgisayarı yeniden başlatın." },
              FixKind.ResetAll, detect: "GoodbyeDPI çıktısındaki 'incompatible version' ifadesinden algılanır."),
            E("DK-207", "Sürücü (WinDivert)", Severity.Info, "Antivirüs programı tespit edildi",
              "Bilgisayarınızda üçüncü taraf bir antivirüs var. Koruma sorunsuz çalışıyorsa bir şey yapmanıza gerek yok; sürücü hatası alırsanız istisna ekleyin.",
              new[] { "Bazı antivirüsler WinDivert'i haber vermeden engeller ya da GoodbyeDPI'ı sonlandırır." },
              new[] { "DPI Kalkan klasörünü antivirüs istisnalarına ekleyin.", "Antivirüsün 'şifreli bağlantıları tara' (HTTPS tarama) özelliği sertifika hatalarına yol açıyorsa kapatın." },
              FixKind.OpenAppFolder, detect: "Sorun Bul taramasında Windows Güvenlik Merkezi'nden okunur."),

            // ------------------------------------------------------------ 3xx çakışma
            E("DK-301", "Çakışma", Severity.Warning, "Başka bir GoodbyeDPI çalışıyor",
              "Uygulama dışında çalışan bir goodbyedpi.exe var (ör. eski .cmd betikleri). İki GoodbyeDPI aynı anda çalışırsa bağlantılar bozulur.",
              new[] { "Eski 1-4 numaralı betiklerden biri çalıştırıldı.", "Başka bir GoodbyeDPI arayüzü kullanılıyor." },
              new[] { "'Çakışmaları gider' düğmesine basın; DPI Kalkan diğerlerini kapatıp devralır." },
              FixKind.ResolveConflicts, detect: "Her 3 saniyede bir çalışan işlemler kontrol edilir."),
            E("DK-302", "Çakışma", Severity.Warning, "Eski GoodbyeDPI hizmeti kurulu",
              "service_install betikleriyle kurulmuş 'GoodbyeDPI' Windows hizmeti var; bilgisayar açılınca kendiliğinden başlar ve DPI Kalkan ile çakışır.",
              new[] { "Daha önce bir service_install_*.cmd betiği çalıştırıldı." },
              new[] { "'Çakışmaları gider' düğmesine basın; eski hizmet kaldırılır." },
              FixKind.ResolveConflicts),
            E("DK-303", "Çakışma", Severity.Warning, "Başka bir DPI aşma aracı çalışıyor",
              "zapret (winws), ByeDPI, SpoofDPI, GreenTunnel gibi başka bir engel aşma programı açık. Aynı anda iki araç paketleri değiştirirse hiçbiri düzgün çalışmaz.",
              new[] { "Daha önce kurulan başka bir engel aşma aracı arka planda çalışıyor." },
              new[] { "Diğer aracı kapatın (görev yöneticisinden ya da kendi tepsi simgesinden).", "Gerekirse o aracın hizmetini kaldırın.", "DPI Kalkan korumasını yeniden başlatın." },
              FixKind.StartProtection, detect: "Sorun Bul taramasında çalışan işlemler kontrol edilir."),
            E("DK-304", "Çakışma", Severity.Warning, "VPN veya proxy etkin",
              "Bir VPN bağlantısı ya da sistem proxy'si açık. Trafik VPN/proxy üzerinden gidiyorsa GoodbyeDPI'a gerek yoktur, ikisi birlikte sorun çıkarabilir.",
              new[] { "VPN programı (WARP, Proton, Nord, WireGuard, OpenVPN…) bağlı.", "Windows proxy ayarı açık." },
              new[] { "VPN kullanacaksanız DPI Kalkan korumasını kapatın; DPI Kalkan kullanacaksanız VPN'i kapatın.",
                      "Proxy kullanmıyorsanız Ayarlar → Ağ ve İnternet → Proxy'den kapatın." },
              FixKind.OpenProxySettings, detect: "Sorun Bul taramasında ağ bağdaştırıcıları ve proxy ayarı kontrol edilir."),

            // ------------------------------------------------------------ 4xx DNS
            E("DK-401", "DNS", Severity.Warning, "DNS engeli var",
              "Servis sağlayıcınızın DNS sunucusu engelli siteler için sahte ya da boş adres döndürüyor. Türkiye'de Discord bu şekilde engelli; DNS yönlendirmesi olmadan Discord açılmaz.",
              new[] { "Servis sağlayıcı DNS düzeyinde erişim engeli uyguluyor." },
              new[] { "DNS yönlendirmesi içeren bir profil kullanın (ör. 'Türkiye – Önerilen').",
                      "Alternatif: 'DNS Ayarları' sayfasından Windows'un DNS sunucusunu değiştirin.",
                      "Ardından DNS önbelleğini temizleyip Discord'u yeniden başlatın." },
              FixKind.FindBest, FixKind.OpenDnsPage, "Koruma kapalıyken sistem DNS'inin yanıtı bağımsız DNS sunucularıyla karşılaştırılır."),
            E("DK-402", "DNS", Severity.Error, "Yönlendirilen DNS sunucusu yanıt vermiyor",
              "Profilin kullandığı DNS sunucusuna ulaşılamıyor. Bu durumda koruma açıkken hiçbir site açılmayabilir.",
              new[] { "DNS sunucusu geçici olarak erişilemez.", "Servis sağlayıcı ya da modem o portu engelliyor.", "Güvenlik duvarı UDP trafiğini engelliyor." },
              new[] { "İnternet Kontrolü → DNS Sunucuları testini çalıştırın.", "Yanıt veren bir DNS kullanan profile geçin (AdGuard, Quad9, OpenDNS).",
                      "'En iyi yöntemi bul' taraması yanıt vermeyen sunucuları otomatik atlar." },
              FixKind.TestDnsServers, FixKind.FindBest, "Koruma başlatılırken DNS sunucusuna deneme sorgusu gönderilir."),
            E("DK-403", "DNS", Severity.Warning, "Koruma açık ama DNS yanıtı hâlâ yanlış",
              "DNS yönlendirmesi açık olduğu hâlde Windows engelli adresi döndürüyor.",
              new[] { "Windows DNS önbelleğinde eski kayıt kaldı.", "Bağlantı IPv6 üzerinden servis sağlayıcının DNS'ine gidiyor.",
                      "VPN ya da başka bir DNS istemcisi araya giriyor.", "hosts dosyasında kayıt var." },
              new[] { "DNS önbelleğini temizleyin.", "Discord'u ve tarayıcıyı tamamen kapatıp yeniden açın.",
                      "Sorun sürerse 'DNS Ayarları' sayfasından Windows DNS'ini değiştirin." },
              FixKind.FlushDns, FixKind.OpenDnsPage, "Koruma başladıktan sonra sistem DNS yanıtı doğrulanır."),
            E("DK-404", "DNS", Severity.Warning, "IPv6 DNS sorguları yönlendirilmiyor",
              "Bağlantınız IPv6 kullanıyor ama seçili profil yalnızca IPv4 DNS sorgularını yönlendiriyor. Windows IPv6 üzerinden sahte yanıt alabilir.",
              new[] { "Özel profilde yalnızca IPv4 DNS adresi girildi." },
              new[] { "Hazır profillerden birini kullanın (hepsi IPv6'yı da yönlendirir) ya da özel profilinize IPv6 DNS adresi ekleyin." },
              FixKind.OpenProfiles),
            E("DK-405", "DNS", Severity.Warning, "hosts dosyasında Discord kaydı var",
              "Windows'un hosts dosyasında Discord alan adlarıyla ilgili kayıt bulundu. Bu kayıtlar DNS'i geçersiz kılar ve eski/yanlış adreslere yönlendirebilir.",
              new[] { "Daha önce denenen bir 'engel aşma' yöntemi hosts dosyasına adres ekledi." },
              new[] { "C:\\Windows\\System32\\drivers\\etc\\hosts dosyasını Not Defteri ile (yönetici olarak) açın.",
                      "İçinde 'discord' geçen satırları silin ya da başına # koyun ve kaydedin.", "DNS önbelleğini temizleyin." },
              FixKind.OpenHosts, FixKind.FlushDns, "Sorun Bul taramasında hosts dosyası okunur."),

            // ------------------------------------------------------------ 5xx erişim
            E("DK-501", "Erişim engeli", Severity.Error, "DPI engeli aşılamadı (bağlantı kesiliyor)",
              "Doğru sunucuya bağlanılıyor ama şifreli bağlantı kurulurken kesiliyor. Servis sağlayıcının DPI cihazı kullanılan yöntemi tanıyor.",
              new[] { "Servis sağlayıcı DPI ayarlarını değiştirdi.", "Seçili yöntem bu hatta etkisiz." },
              new[] { "'En iyi yöntemi bul' ile tüm yöntemleri otomatik deneyin.",
                      "Elle denemek için: Otomatik TTL, Tek Bayt Parçalama, Güçlendirilmiş Sahte Paket profilleri.",
                      "Hedefli mod açıksa kapatıp tekrar deneyin." },
              FixKind.FindBest, FixKind.OpenProfiles, "Bağlantı testinde TLS bağlantısının sıfırlanmasından algılanır."),
            E("DK-502", "Erişim engeli", Severity.Error, "Zaman aşımı (paketler düşürülüyor)",
              "Sunucudan yanıt gelmiyor. DPI cihazı paketleri sessizce düşürüyor ya da bağlantınız çok yavaş.",
              new[] { "Servis sağlayıcı bağlantıyı kesmek yerine paketleri düşürüyor.", "İnternet bağlantısı çok yavaş ya da kopuk." },
              new[] { "İnternet bağlantınızın çalıştığını kontrol edin.", "'En iyi yöntemi bul' taramasını çalıştırın." },
              FixKind.FindBest, FixKind.SiteAnalysis),
            E("DK-503", "Erişim engeli", Severity.Error, "IP engeli ya da sunucuya ulaşılamıyor",
              "Sitenin doğru IP adresine bile bağlanılamıyor. Bu bir IP engeli olabilir; GoodbyeDPI IP engelini aşamaz.",
              new[] { "Servis sağlayıcı sunucunun IP adresini engelledi.", "Sunucu geçici olarak kapalı." },
              new[] { "Bir süre sonra tekrar deneyin.", "Modemi yeniden başlatın.", "İnternet Kontrolü → Site Analizi ile durumu doğrulayın.",
                      "IP engeli sürüyorsa yalnızca VPN işe yarar." },
              FixKind.SiteAnalysis),
            E("DK-504", "Erişim engeli", Severity.Error, "Sertifika uyuşmazlığı",
              "Bağlanılan sunucu farklı bir sertifika gösterdi. DNS sahte adres veriyor ya da araya giren bir program (antivirüs HTTPS taraması, kurumsal proxy) var.",
              new[] { "DNS yönlendirmesi kapalı ya da çalışmıyor.", "Antivirüs şifreli bağlantıları tarıyor.", "Bilgisayar saati yanlış." },
              new[] { "DNS yönlendirmeli bir profil kullanın ve DNS önbelleğini temizleyin.",
                      "Antivirüsün 'şifreli bağlantıları tara' özelliğini kapatın.", "Bilgisayarın tarih ve saatini kontrol edin." },
              FixKind.FlushDns, FixKind.OpenDateTime),
            E("DK-505", "Erişim engeli", Severity.Warning, "Bazı sunucular açılıyor, bazıları açılmıyor",
              "Discord'un bir kısmı çalışıyor ama bazı sunuculara (resimler, güncelleme, sohbet bağlantısı) erişilemiyor.",
              new[] { "Hedefli moddaki site listesi eksik.", "Bazı sunucular farklı bir yöntemle engelleniyor." },
              new[] { "Hedefli mod açıksa 'Discord adreslerini ekle' ile listeyi tamamlayın ya da hedefli modu kapatın.",
                      "'En iyi yöntemi bul' taramasını 'tüm yöntemleri dene' seçeneğiyle çalıştırın." },
              FixKind.OpenSites, FixKind.FindBest),
            E("DK-506", "Erişim engeli", Severity.Warning, "Açılmayan site hedef listesinde yok",
              "Hedefli mod açık ve açılmayan sunucu listede değil; bu yüzden ona hiçbir yöntem uygulanmıyor.",
              new[] { "Hedef site listesine eklenmemiş bir alan adı kullanılıyor." },
              new[] { "Hedef Siteler sayfasından alan adını ekleyin ya da hedefli modu kapatın.", "Değişikliği uygulamak için korumayı yeniden başlatın." },
              FixKind.OpenSites),

            // ------------------------------------------------------------ 6xx goodbyedpi
            E("DK-601", "GoodbyeDPI", Severity.Error, "GoodbyeDPI başlatılamadı",
              "GoodbyeDPI açıldıktan hemen sonra kapandı ve tanınan bir hata mesajı vermedi.",
              new[] { "Antivirüs programı işlemi sonlandırdı.", "Sürücü yüklenemedi.", "Profil parametreleri hatalı." },
              new[] { "Günlük sayfasında GoodbyeDPI'ın son satırlarına bakın.", "Farklı bir profil deneyin.", "'Tümünü sıfırla' yapıp bilgisayarı yeniden başlatın." },
              FixKind.OpenLog, FixKind.ResetAll),
            E("DK-602", "GoodbyeDPI", Severity.Error, "GoodbyeDPI beklenmedik şekilde kapandı",
              "Koruma çalışırken GoodbyeDPI işlemi kapandı.",
              new[] { "Antivirüs programı işlemi sonlandırdı.", "Bilgisayar uykudan uyanırken sürücü sıfırlandı.", "Görev Yöneticisi'nden kapatıldı." },
              new[] { "Ayarlar'da 'Kapanırsa otomatik yeniden başlat' açık olsun.", "DPI Kalkan klasörünü antivirüs istisnalarına ekleyin.", "Korumayı yeniden başlatın." },
              FixKind.StartProtection, FixKind.OpenLog),
            E("DK-603", "GoodbyeDPI", Severity.Error, "Geçersiz profil parametresi",
              "GoodbyeDPI profildeki bir parametreyi tanımadı.",
              new[] { "Özel profilde yanlış yazılmış ya da bu GoodbyeDPI sürümünde olmayan bir seçenek var." },
              new[] { "Profiller sayfasından profili düzenleyin; 'Ek parametreler' ve elle yazılan komutu kontrol edin.", "Hazır bir profile geçin." },
              FixKind.OpenProfiles),
            E("DK-604", "GoodbyeDPI", Severity.Info, "Başlangıç mesajı okunamadı",
              "GoodbyeDPI çalışıyor ancak 'Filter activated' mesajı okunamadı. Genellikle zararsızdır.",
              new[] { "Bilgisayar çok yoğun ya da konsol çıktısı geç geldi." },
              new[] { "Bağlantı testi başarılıysa bir şey yapmanıza gerek yok." },
              FixKind.OpenLog),

            // ------------------------------------------------------------ 7xx hizmet
            E("DK-701", "Hizmet ve başlangıç", Severity.Error, "Arka plan hizmeti kurulamadı",
              "GoodbyeDPI Windows hizmeti olarak kurulamadı.",
              new[] { "Aynı adlı bozuk bir hizmet kaydı kaldı.", "Antivirüs hizmet oluşturmayı engelledi." },
              new[] { "'Tümünü sıfırla' düğmesine basın ve bilgisayarı yeniden başlatın.", "Ya da hizmet modunu kapatıp uygulama modunda kullanın." },
              FixKind.DisableServiceMode, FixKind.ResetAll),
            E("DK-702", "Hizmet ve başlangıç", Severity.Error, "Arka plan hizmeti başlatılamadı",
              "Hizmet kuruldu ama başlatılamadı ya da hemen durdu.",
              new[] { "Sürücü hatası (genellikle antivirüs).", "Uygulama klasörü taşındı." },
              new[] { "Önce uygulama modunda başlatıp hata kodunu görün.", "Sorun çözülünce hizmet modunu tekrar açın." },
              FixKind.DisableServiceMode, FixKind.OpenLog),
            E("DK-703", "Hizmet ve başlangıç", Severity.Warning, "Açılış görevi oluşturulamadı",
              "'Windows açılışında başlat' için Görev Zamanlayıcı görevi oluşturulamadı.",
              new[] { "Görev Zamanlayıcı hizmeti kapalı.", "Uygulama geçici bir klasörde." },
              new[] { "Uygulamayı kalıcı bir klasöre taşıyın.", "Ayarlar'dan seçeneği kapatıp yeniden açın." },
              FixKind.OpenSettings),
            E("DK-704", "Hizmet ve başlangıç", Severity.Warning, "Hizmet eski bir klasörü gösteriyor",
              "Kurulu DPI Kalkan hizmeti, uygulamanın şu anki klasöründen farklı bir yerdeki GoodbyeDPI'ı çalıştırıyor (uygulama taşınmış ya da güncellenmiş).",
              new[] { "Uygulama klasörü taşındı ya da yeni sürüm farklı bir klasöre çıkarıldı." },
              new[] { "'Korumayı yeniden başlat' düğmesine basın; hizmet yeni klasörü gösterecek şekilde güncellenir." },
              FixKind.StartProtection),

            // ------------------------------------------------------------ 8xx ağ
            E("DK-801", "Ağ bağlantısı", Severity.Error, "İnternet bağlantısı yok",
              "Hiçbir genel sunucuya (Cloudflare, Google, Quad9) bağlanılamıyor; sorun engelden değil bağlantınızdan kaynaklanıyor.",
              new[] { "Kablo/Wi-Fi bağlantısı kopuk.", "Modem ya da servis sağlayıcıda arıza var." },
              new[] { "Korumayı kapatıp internetin çalışıp çalışmadığına bakın.", "Modemi kapatıp 30 saniye sonra açın.", "Wi-Fi/kablo bağlantısını kontrol edin." },
              FixKind.None),
            E("DK-802", "Ağ bağlantısı", Severity.Warning, "Sistem proxy'si açık",
              "Windows'ta bir proxy sunucusu ayarlı. Proxy üzerinden giden trafik GoodbyeDPI'dan etkilenmeyebilir.",
              new[] { "Bir program ya da kullanıcı proxy ayarladı." },
              new[] { "Proxy kullanmıyorsanız Ayarlar → Ağ ve İnternet → Proxy'den kapatın." },
              FixKind.OpenProxySettings),
            E("DK-803", "Ağ bağlantısı", Severity.Error, "Bilgisayar saati yanlış",
              "Bilgisayarınızın saati gerçek saatten çok farklı. Şifreli (HTTPS) bağlantılar sertifika hatası verir.",
              new[] { "Saat dilimi yanlış ya da otomatik saat ayarı kapalı.", "Anakart pili bitmiş." },
              new[] { "Tarih ve saat ayarlarından 'Saati otomatik olarak ayarla'yı açın ve 'Şimdi eşitle'ye basın." },
              FixKind.OpenDateTime),

            // ------------------------------------------------------------ 9xx discord
            E("DK-901", "Discord", Severity.Info, "Discord korumadan önce açılmış",
              "Discord, engel varken açıldığı için eski bağlantıları ve DNS kayıtlarını tutuyor olabilir; 'Bağlanılıyor…' ekranında takılabilir.",
              new[] { "Discord bilgisayar açılışında DPI Kalkan'dan önce başladı.", "Koruma Discord açıkken başlatıldı." },
              new[] { "Discord'u tepsi simgesinden tamamen kapatıp yeniden açın (ya da aşağıdaki düğmeyi kullanın)." },
              FixKind.RestartDiscord, detect: "Koruma başladığında Discord'un açılış zamanı kontrol edilir."),
            E("DK-902", "Discord", Severity.Warning, "Discord güncelleme ekranında takılıyor",
              "Discord açılışta güncelleme sunucularına ulaşamıyor.",
              new[] { "updates.discord.com ya da dl.discordapp.net erişilemiyor.", "Hedefli modda bu adresler listede yok." },
              new[] { "Bağlantı testinde 'updates.discord.com' sunucusunun açık olduğunu kontrol edin.", "Hedefli modu kapatın ya da Discord adreslerini ekleyin.", "Discord'u yeniden başlatın." },
              FixKind.RestartDiscord, FixKind.OpenSites),
            E("DK-903", "Discord", Severity.Info, "Discord sesli sohbete bağlanamıyor",
              "Yazılı sohbet çalışıyor ama ses 'RTC bağlanıyor' durumunda kalıyor. Discord ses sunucuları UDP kullanır; GoodbyeDPI UDP trafiğini değiştirmez.",
              new[] { "Ses sunucusunun IP adresi engelli ya da yoldaki bir cihaz UDP'yi kısıtlıyor." },
              new[] { "Discord → Ayarlar → Ses ve Görüntü → 'Hizmet Kalitesi Yüksek Paket Önceliği'ni kapatın.",
                      "Sunucu ayarlarından ses bölgesini (Rotterdam, Frankfurt…) değiştirin.", "Sorun sürerse yalnızca VPN yardımcı olur." },
              FixKind.RestartDiscord),
            E("DK-904", "Discord", Severity.Warning, "Tarayıcıda açılıyor, uygulamada açılmıyor",
              "discord.com tarayıcıda açılıyor ama Discord uygulaması bağlanamıyor.",
              new[] { "Uygulama eski bağlantıları tutuyor.", "Discord'da proxy ayarı var.", "Discord güncel değil." },
              new[] { "Discord'u tamamen kapatıp yeniden açın.", "DNS önbelleğini temizleyin.", "Discord'u web'den yeniden indirip kurun." },
              FixKind.RestartDiscord, FixKind.FlushDns),

            // ------------------------------------------------------------ proxy
            E("DK-805", "Proxy", Severity.Error, "Proxy sunucusuna bağlanılamıyor",
              "Girilen proxy adresine bağlantı kurulamadı ya da sunucu proxy gibi yanıt vermedi.",
              new[] { "IP adresi ya da port yanlış yazıldı.", "Proxy sunucusu kapalı (ücretsiz proxy'ler sık sık kapanır).",
                      "Proxy türü yanlış seçildi (HTTP yerine SOCKS5 ya da tersi).", "Proxy'nin IP adresi de engelli." },
              new[] { "Adresi ve portu kontrol edin (ör. 185.12.34.56:8080).", "Türü değiştirip (HTTP ↔ SOCKS5) tekrar test edin.",
                      "Başka bir proxy deneyin.", "Proxy sağlayıcınızın verdiği bilgileri olduğu gibi 'Hızlı giriş' kutusuna yapıştırın." },
              FixKind.OpenProxyPage, detect: "Proxy sayfasındaki 'Test et' düğmesiyle algılanır."),
            E("DK-806", "Proxy", Severity.Error, "Proxy kullanıcı adı/şifre hatalı",
              "Proxy sunucusu kimlik doğrulama istiyor ve girilen bilgiler kabul edilmedi.",
              new[] { "Kullanıcı adı ya da şifre yanlış.", "Proxy aboneliğinin süresi dolmuş.", "Proxy yalnızca belirli IP adreslerine izin veriyor." },
              new[] { "Kullanıcı adı ve şifreyi kontrol edip yeniden girin.", "Proxy sağlayıcınızın panelinde IP izin listesine kendi IP'nizi ekleyin." },
              FixKind.OpenProxyPage),
            E("DK-807", "Proxy", Severity.Warning, "Proxy Discord'a ulaşamıyor",
              "Proxy çalışıyor ama üzerinden Discord'a şifreli bağlantı kurulamadı.",
              new[] { "Proxy'nin bulunduğu ülkede de Discord engelli.", "Proxy 443. porta tünel açmaya izin vermiyor.", "Proxy trafiği değiştiriyor (sertifika uyuşmuyor)." },
              new[] { "Başka bir ülkedeki proxy'yi deneyin.", "HTTPS/CONNECT destekleyen bir HTTP proxy ya da SOCKS5 proxy kullanın." },
              FixKind.OpenProxyPage),
        };

        public static ErrorInfo Get(string code) => All.FirstOrDefault(e => e.Code == code);
    }

    public sealed class DetectedProblem
    {
        public string Code;
        public string Detail;
        public DateTime Time;
        public ErrorInfo Info => ErrorCatalog.Get(Code);
    }

    /// <summary>Çalışma sırasında algılanan sorunlar (ana sayfa bandı ve Sorun Giderme sayfası için).</summary>
    public sealed class ProblemCenter
    {
        readonly object gate = new object();
        readonly List<DetectedProblem> items = new List<DetectedProblem>();

        public void Report(string code, string detail = null)
        {
            if (ErrorCatalog.Get(code) == null) return;
            lock (gate)
            {
                items.RemoveAll(p => p.Code == code);
                items.Add(new DetectedProblem { Code = code, Detail = detail ?? "", Time = DateTime.Now });
            }
            App.RaiseStatus();
        }

        public void Clear(string code)
        {
            bool removed;
            lock (gate) removed = items.RemoveAll(p => p.Code == code) > 0;
            if (removed) App.RaiseStatus();
        }

        public void ClearWhere(Func<ErrorInfo, bool> pred)
        {
            bool removed;
            lock (gate) removed = items.RemoveAll(p => p.Info != null && pred(p.Info)) > 0;
            if (removed) App.RaiseStatus();
        }

        public List<DetectedProblem> Snapshot()
        {
            lock (gate) return items.OrderByDescending(p => (int)(p.Info?.Severity ?? Severity.Info)).ThenByDescending(p => p.Time).ToList();
        }

        public DetectedProblem Top => Snapshot().FirstOrDefault();
    }
}
