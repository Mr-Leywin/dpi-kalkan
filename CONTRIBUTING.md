# Katkıda bulunma

Katkılarınız memnuniyetle karşılanır: hata bildirimleri, öneriler, çeviri düzeltmeleri ya da kod.

## Hata bildirme
- Önce uygulamada **Sorun Giderme → Tam kontrolü başlat**'ı deneyin.
- Uygulamadaki **Sorun bildir** düğmesi, sürüm ve hata kodu doldurulmuş bir sorun formu açar.
- **Destek paketi** eklemek çözümü hızlandırır. Pakette IP adresleri, kullanıcı adı, e-posta ve proxy bilgileri gizlenir.
- Güvenlik açıklarını herkese açık sorun olarak değil, [SECURITY.md](SECURITY.md) dosyasındaki yolla bildirin.

## Kod katkısı
1. Depoyu çatallayın (fork) ve bir dal açın.
2. Derleyin ve testleri çalıştırın (gereksinimler: README → Kaynaktan derleme):
   ```powershell
   powershell -ExecutionPolicy Bypass -File build.ps1 -Preview
   build\DPIKalkan.preview.exe --selftest sonuc.txt
   ```
   `sonuc.txt` içinde `FAIL` satırı olmamalı. Arayüz değişikliklerini `build\screens` altındaki ekran görüntülerinden kontrol edin. Derleme ya da paketlemeyi değiştirdiyseniz `build.ps1 -VerifyReproducible` ile iki derlemenin aynı çıktığını doğrulayın.
3. Çekme isteği (pull request) açın. GitHub Actions derlemeyi ve testleri otomatik çalıştırır.

## Kurallar
- **Arayüz metinleri Türkçe olmalı.**
- **Renkleri doğrudan yazmayın:** `Theme` sınıfındaki renkleri kullanın, böylece tüm temalarda doğru görünür.
- **Yeni hata durumu:** `src/Core/Errors.cs` içindeki kataloğa bir `DK-xxx` kodu ekleyin.
- **`third_party/` klasörüne dokunmayın:** Buradaki GoodbyeDPI/WinDivert dosyaları değiştirilmez. Güncellenirlerse SHA-256 listesini (`third_party/goodbyedpi/README.md`) ve programa gömülü özetleri (`src/Core/Security.cs` → `Integrity.Known`) birlikte güncelleyin; testler gömülü özetleri `third_party` dosyalarıyla karşılaştırır.
- **Yeni GoodbyeDPI parametresi:** `src/Core/Security.cs` → `ArgPolicy` beyaz listesine tam adıyla ve değer aralığıyla ekleyin.
- **Betik yorumlayıcısı yok:** Uygulama PowerShell ya da benzerini çalıştırmaz; Windows araçlarını `Sys.Run` ile System32'den çağırın.
- **Kişisel bilgi eklemeyin:** IP adresi, kullanıcı adı ya da e-posta içeren test verisi, ekran görüntüsü veya günlük eklemeyin.
