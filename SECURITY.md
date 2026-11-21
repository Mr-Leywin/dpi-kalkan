# Güvenlik politikası

## Desteklenen sürümler

Güvenlik düzeltmeleri yalnızca [son sürüme](https://github.com/Mr-Leywin/dpi-kalkan/releases/latest) yapılır. Uygulamanın otomatik güncellemesini açık tutmanız önerilir.

## Güvenlik açığı bildirme

Bir güvenlik açığı bulduysanız lütfen **herkese açık sorun kaydı (issue) açmayın**. GitHub'ın gizli bildirim özelliğini kullanın:
**Security → Report a vulnerability** – https://github.com/Mr-Leywin/dpi-kalkan/security/advisories/new

Bildirime şunları eklemeniz çözümü hızlandırır: etkilenen sürüm, Windows sürümü, açığın nasıl tetiklendiği (adım adım), beklenen ve gerçekleşen davranış. Bildiriminiz en kısa sürede incelenir; düzeltme yayımlanana kadar ayrıntılar gizli tutulur.

**Kapsam dışı:**
- GoodbyeDPI ya da WinDivert'in kendisindeki açıklar: lütfen [GoodbyeDPI](https://github.com/ValdikSS/GoodbyeDPI) ve [WinDivert](https://github.com/basil00/WinDivert) projelerine bildirin.
- Antivirüs programlarının GoodbyeDPI/WinDivert'i "HackTool/RiskTool" olarak işaretlemesi (bkz. README → Antivirüs, SmartScreen ve doğrulama).

## Güvenlik modeli

### Korumalı kurulum
- DPI Kalkan, ağ sürücüsü (WinDivert) için yönetici yetkisiyle çalışır. Discord ve program bazlı proxy ile açılan programları ise normal kullanıcı yetkisiyle başlatır.
- **SYSTEM yetkili arka plan hizmeti** ve **izin sormadan yönetici olarak açılan başlangıç görevi** yalnızca kurulum klasörü korumalıysa kullanılabilir: klasörün, alt klasörlerinin, dosyaların ve tüm üst klasörlerin yalnızca Yöneticiler, SYSTEM ve TrustedInstaller tarafından değiştirilebilmesi gerekir (kurulum paketiyle Program Files). Uygulama bu denetimi yalnızca okuyarak yapar; hiçbir izni değiştirmez.
- Taşınabilir (zip) sürüm uygulama modunda çalışır. Korumasız bir klasörü gösteren DPI Kalkan hizmeti ya da başlangıç görevi açılışta kaldırılır.

### Bütünlük
- Birlikte dağıtılan GoodbyeDPI 0.2.3rc3 ve WinDivert dosyaları resmi sürümden değiştirilmeden alınmıştır (özetler: `third_party/goodbyedpi/README.md`). Koruma her başlatıldığında bu dosyalar programa gömülü SHA-256 özetleriyle karşılaştırılır ve başlatma bitene kadar yazmaya kapalı tutulur; uyuşmazsa koruma başlatılmaz (DK-105).
- GoodbyeDPI parametreleri beyaz listeden geçer: yalnızca bilinen seçenekler tam adlarıyla kabul edilir (GoodbyeDPI'ın ayrıştırıcısı `--black` gibi kısaltmaları da kabul ettiği için), değerler sayı/IP aralığına göre denetlenir, komut satırı karakterleri reddedilir. Profil kodları, ayar dosyaları ve dışa aktarılan betikler aynı denetimden geçer.
- Uygulama PowerShell ya da başka bir betik yorumlayıcısı çalıştırmaz. Windows araçları (`sc`, `netsh`) System32'den tam yoluyla, zaman aşımıyla çağrılır. DLL arama sırası uygulama klasörü ve System32 ile sınırlıdır.

### Güncelleme
- Yalnızca bu deponun GitHub sürümlerinden ve HTTPS üzerinden indirilir; yönlendirme sonrası varılan adres de denetlenir.
- İndirilen dosyanın SHA-256 özeti GitHub'ın bildirdiği özetle karşılaştırılır; uyuşmazsa ya da özet yoksa kurulum yapılmaz. Zip paketi doğrulandığı aynı kilitli dosyadan açılır; çalıştırılacak dosyanın özeti başlatmadan hemen önce yeniden denetlenir.
- İndirme klasörü uygulamanın kendi `veri\guncelleme` klasörüdür (kurulumlu sürümde yalnızca yöneticiler yazabilir).
- Kod imzalama sertifikası alındığında güncellemenin yayıncı imzası da (Authenticode) denetlenecektir.

### Derleme ve doğrulama
- Sürümler GitHub Actions'ta derlenir. Eylemler commit SHA'sıyla, araçlar (Inno Setup, .NET SDK) sürüm ve özetleriyle sabitlenmiştir.
- Derleme tekrarlanabilirdir: aynı koddan iki derleme aynı SHA-256'yı verir (CI her derlemede bunu denetler).
- Her sürümde `SHA256SUMS.txt` ve GitHub derleme kaynağı kanıtı (build provenance) yayımlanır:
  ```powershell
  gh attestation verify .\DPI-Kalkan-<sürüm>-Setup.exe -R Mr-Leywin/dpi-kalkan
  ```

### Diğer
- Yerel proxy köprüsü yalnızca `127.0.0.1` adresini dinler; ağdaki diğer cihazlardan erişilemez.
- Proxy şifreleri Windows DPAPI ile yalnızca aynı Windows kullanıcısının çözebileceği şekilde saklanır.
- Destek paketi IP adreslerini, kullanıcı klasörü adını, hesap adlarını, e-posta adreslerini ve proxy bilgilerini gizler.
- Temiz kaldırma (`--uninstall`) hizmeti, başlangıç görevini, WinDivert kaydını, Windows proxy'sini, DNS sunucularını ve şifreli DNS kayıtlarını DPI Kalkan'dan önceki hâline döndürür.
