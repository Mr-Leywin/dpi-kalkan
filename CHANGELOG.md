# Değişiklik günlüğü

## [2.4.0] – 2026-11-21

Bu sürüm kapsamlı bir güvenlik denetiminin sonucudur. Artık iki biçimde dağıtılır: **kurulum paketi** (`Setup.exe`, önerilen) ve **taşınabilir zip**.

> **Taşınabilir sürüm kullananlar için önemli:** Arka plan hizmeti ve "Windows açılışında başlat" artık yalnızca kurulumlu sürümde kullanılabilir. Taşınabilir bir klasörden kurulmuş hizmet ve açılış görevi, 2.4.0 ilk açıldığında güvenlik için kaldırılır ve koruma uygulama modunda sürer. Açılışta otomatik başlaması için kurulum paketini kullanın.

### Güvenlik
- **Korumalı kurulum modeli:** SYSTEM yetkili arka plan hizmeti ve izin sormadan yönetici olarak açılan başlangıç görevi yalnızca yöneticilerin yazabildiği bir klasörde (Program Files) açılabilir. Herkesin yazabildiği bir klasörden bu yetkilerle çalışmak, zararlı bir programın yetki kazanmasına yol açabiliyordu (DK-705).
- **Dosya bütünlüğü:** GoodbyeDPI ve WinDivert dosyaları her başlatmada programa gömülü resmi SHA-256 özetleriyle doğrulanır ve başlatma bitene kadar yazmaya kapalı tutulur (DK-105).
- **Parametre beyaz listesi:** GoodbyeDPI yalnızca bilinen seçeneklerle, tam adlarıyla çalıştırılır. `--black` gibi kısaltmalar (GoodbyeDPI bunları `--blacklist` olarak kabul ediyordu) ve komut satırı karakterleri reddedilir. Profil kodları, ayar dosyaları, profil düzenleyici ve betik dışa aktarma bu denetimden geçer (DK-605).
- **Güncelleme güvenliği:**
  - Paket uygulamanın kendi veri klasörüne indirilir ve doğrulandığı aynı kilitli dosyadan açılır.
  - Yönlendirme sonrası adres denetlenir ve çalıştırılacak dosyanın özeti başlatmadan hemen önce yeniden doğrulanır.
  - `--apply-update` yolları doğrulanır.
- **Daha az saldırı yüzeyi:**
  - PowerShell kullanımı tamamen kaldırıldı.
  - Görev Zamanlayıcı kaydı geçici XML dosyası yerine doğrudan Windows arabirimiyle yapılır.
  - DLL arama sırası uygulama klasörü ve System32 ile sınırlandı; Windows araçları tam yoluyla çağrılır.
- **Program bazlı proxy:** Ortam değişkeni modunda komut satırına giren değerler denetlenir. Ayar dosyasındaki DNS yedek adresleri kullanılmadan önce doğrulanır.

### Kurulum & Güncelleme
- **Yeni kurulum paketi** (`DPI-Kalkan-x.y.z-Setup.exe`):
  - 64 bit Windows'ta Program Files'a kurar; Başlat menüsüne ve Program Ekle/Kaldır'a eklenir.
  - Yükseltmede ayarlar korunur; çalışan uygulama, GoodbyeDPI ve hizmet güvenle durdurulup yeniden başlatılır.
- **Kurulumlu sürüm kurulum paketiyle güncellenir:** Setup.exe indirilir, SHA-256 ve sürüm bilgisi doğrulanır, sessiz yükseltme yapılır ve uygulama kendini yeniden açar. Başarısız olursa önceki sürüm açılır. Taşınabilir sürüm zip ile güncellenmeye devam eder.
- **Temiz kaldırma:**
  - Ayarlar → **Temiz kaldır**, komut satırında `DPIKalkan.exe --uninstall [--quiet]`, kurulumlu sürümde Program Ekle/Kaldır.
  - Hizmet, açılış görevi, WinDivert kaydı, Windows proxy'si, DNS sunucuları ve şifreli DNS kayıtları DPI Kalkan'dan önceki hâline döner.
- **Doğrulanabilir sürümler:**
  - Her sürümde `SHA256SUMS.txt` ve GitHub derleme kaynağı kanıtı (build provenance) yayımlanır.
  - Derleme tekrarlanabilirdir: aynı koddan iki derleme aynı özeti verir.
  - GitHub Actions eylemleri commit SHA'sıyla sabitlendi.

### Değişiklikler
- **Şifreli DNS (DoH):**
  - Windows'un kendi aracıyla (`netsh`) açılır ve her sunucu için sonuç geri okunarak doğrulanır.
  - "Eski ayarlara dön" ve "Otomatik" seçimi şifreli DNS kayıtlarını da önceki hâline getirir.
  - Windows 10'da şifreli DNS desteklenmediği açıkça bildirilir.
- **Beklenmedik kapanış bildirimi:** Uygulama çöker ya da zorla kapatılırsa sonraki açılışta bildirilir; son hata `veri\son-cokme.txt` dosyasına yazılır (DK-106).
- **Antivirüs yönergeleri:**
  - Önce SHA-256 ile doğrulama ve Microsoft'a yanlış pozitif bildirimi öneriliyor; yeni **Yanlış pozitif bildir** düğmesi eklendi.
  - Antivirüs istisnası yalnızca son çare olarak anlatılır.
- **Sorun Bul:** "Dosya bütünlüğü" ve "Kurulum türü" denetimleri eklendi.
- **Tümünü sıfırla** artık DPI Kalkan'ın değiştirdiği proxy ve DNS ayarlarını da geri almayı soruyor.
- **Exe sürüm bilgileri** tamamlandı: yayıncı, telif ve ürün sürümü.
- Yedek `.cmd` betikleri yönetici iznini PowerShell'siz ister (sağ tık → Yönetici olarak çalıştır).
- Sessizce yutulan hatalar artık ayrıntılı günlüğe yazılır.

### Düzeltmeler
- Komut satırı araçları (`sc`, `netsh`, `schtasks`) yanıt vermezse uygulama süresiz bekleyebiliyordu; zaman aşımında artık sonlandırılıyor.
- Şifreli DNS ayarları DNS'i eski hâline döndürünce geri alınmıyordu.
- Uygulama çökerse GoodbyeDPI'ın açık kalabileceği durum (iş nesnesine bağlanamama) artık bildiriliyor.
- Yedek betiklerde profil adı ya da açıklaması satır sonu içerirse betiğe ek komut satırı girebiliyordu.

## [2.3.0] – 2026-11-20

### Yeni
- **Otomatik güncelleme:**
  - Günde bir kez GitHub'daki yeni sürüm denetlenir ve ana sayfada, tepsi menüsünde bildirilir.
  - "Güncelle" ile indirilir; SHA-256 ile doğrulanır ve ayarlar korunarak kurulur.
  - Hata olursa önceki sürüme dönülür. Ayarlar → Güncellemeler bölümünden yönetilir.
- **Destek paketi:** Günlük, geçmiş ve ayarlar tek bir zip dosyasına kaydedilir. IP adresleri, kullanıcı adı, e-posta ve proxy bilgileri gizlenir.
- **Sorun bildir:** GitHub'da sürüm, Windows ve hata kodu önceden doldurulmuş sorun formu açılır.
- **GitHub altyapısı:**
  - Her değişiklikte otomatik derleme ve birim testleri.
  - Etiketle otomatik sürüm yayımlama (SHA-256 dosyasıyla birlikte).
  - Sorun formları ve güvenlik politikası.

### Düzeltmeler
- Paketin içindeki klasör adı artık sürüm numarası içermiyor ("DPI Kalkan"); yerinde güncellemelerde klasör adı yanıltıcı olmuyor.

## [2.2.0] – 2026-11-20
- Program bazlı proxy: proxy yalnızca seçilen programlarda (Chrome, Edge, Opera, Firefox, Discord, Spotify…) kullanılır.
- Yerel proxy köprüsü: şifreli proxy'leri desteklemeyen programlar da kullanabilir.
- Toplu proxy testi ve proxy türünü (HTTP/SOCKS5) otomatik düzeltme.
- Hız testi, grafikli bağlantı geçmişi, ayarları dışa ve içe aktarma.

## [2.1.0] – 2026-11-20
- Otomatik onarım ve bağlantı bekçisi.
- Akıllı tarama: başarısız DPI yönteminin DNS'i farklı kopyaları atlanır.
- Proxy sayfası, profil paylaşma kodu, Ctrl+Alt+K kısayolu, Roblox/Wattpad hazır listeleri.

## [2.0.0] – 2026-11-20
- Doğru çalışan yöntem taraması: DNS doğrulamalı, iki turlu. 1.0'da DNS'siz yöntemler yanlışlıkla "çalışıyor" görünebiliyordu.
- 22 hazır yöntem ve 7 DNS sunucusu; her seçeneğin ayrıntılı açıklaması.
- Sorun Giderme: hata kodları ve tek tıkla düzeltme.
- İnternet Kontrolü, DNS Ayarları (Windows DNS + DoH).
- Tema sistemi ve Basit / Orta / Detaylı arayüz.

## [1.0.0] – 2026-11-20
- İlk sürüm: sistem tepsisinde çalışan GoodbyeDPI arayüzü.
