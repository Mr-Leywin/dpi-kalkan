# Güvenlik

## Güvenlik açığı bildirme

Bir güvenlik açığı bulduysanız lütfen **herkese açık sorun kaydı açmayın**. Bunun yerine GitHub'ın gizli bildirim özelliğini kullanın:
**Security → Report a vulnerability** (https://github.com/Mr-Leywin/dpi-kalkan/security/advisories/new).

## Uygulamanın güvenlik önlemleri

- **Otomatik güncelleme** yalnızca bu deponun GitHub sürümlerinden ve HTTPS üzerinden indirir. İndirilen paketin SHA-256 özeti GitHub'ın bildirdiği özetle karşılaştırılır; uyuşmazsa ya da özet yoksa kurulum yapılmaz. Kurulum her zaman kullanıcının onayıyla yapılır; hata olursa önceki sürüme dönülür.
- **Yerel proxy köprüsü** yalnızca `127.0.0.1` adresini dinler; ağdaki diğer cihazlardan erişilemez.
- **Proxy şifreleri** Windows DPAPI ile yalnızca aynı Windows kullanıcısının çözebileceği şekilde saklanır.
- **Destek paketi**, IP adreslerini, kullanıcı klasörü adını, e-posta adreslerini ve proxy bilgilerini gizler.
- Uygulama, ağ sürücüsü (WinDivert) için yönetici yetkisiyle çalışır. Program bazlı proxy ve Discord gibi kullanıcı programlarını ise normal kullanıcı yetkisiyle başlatır.
- Birlikte dağıtılan GoodbyeDPI/WinDivert dosyaları resmi sürümden değiştirilmeden alınmıştır; SHA-256 özetleri `third_party/goodbyedpi/README.md` dosyasındadır.
