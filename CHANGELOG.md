# Değişiklik günlüğü

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
