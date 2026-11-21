# Üçüncü taraf bileşenler / Third-party notices

DPI Kalkan, aşağıdaki açık kaynak bileşenleri **değiştirmeden** birlikte dağıtır. DPI Kalkan'ın kendi kaynak kodu MIT lisanslıdır (Copyright (c) 2026 Osman Barış MEDİNE (Mr Leywin); bkz. `LICENSE`); bu bileşenler kendi lisanslarına tabidir. Lisans metinlerinin tamamı `third_party/goodbyedpi/licenses/` klasöründe ve dağıtılan paketin `lisanslar/` klasöründedir.

DPI Kalkan bu projelerin geliştiricileriyle bağlantılı değildir ve onlar tarafından desteklenmez.

DPI Kalkan bundles the following open-source components **unmodified**. DPI Kalkan's own source code is MIT-licensed; these components remain under their own licenses. DPI Kalkan is not affiliated with or endorsed by their authors.

| Bileşen / Component | Sürüm | Lisans | Telif / Copyright | Kaynak kod / Source |
|---|---|---|---|---|
| GoodbyeDPI (`goodbyedpi.exe`) | 0.2.3rc3 | Apache License 2.0 | © ValdikSS | https://github.com/ValdikSS/GoodbyeDPI |
| WinDivert (`WinDivert.dll`, `WinDivert32.sys`, `WinDivert64.sys`) | 2.2 (GoodbyeDPI 0.2.3rc3 ile gelen) | GNU LGPL v3 (ya da GPL v2) | © basil00 | https://github.com/basil00/WinDivert |
| uthash (goodbyedpi.exe içinde) | – | BSD (revised) | © Troy D. Hanson | https://github.com/troydhanson/uthash |
| getline (goodbyedpi.exe içinde) | – | BSD 2-Clause | © The NetBSD Foundation | https://github.com/ValdikSS/GoodbyeDPI |

## Notlar / Notes

- Dosyalar GoodbyeDPI'ın resmi `0.2.3rc3` sürümündeki `goodbyedpi-0.2.3rc3-2.zip` paketinden alınmıştır ve SHA-256 özetleri `third_party/goodbyedpi/README.md` dosyasında listelenmiştir.
- **WinDivert (LGPL):** WinDivert ayrı dosyalar (DLL ve sürücü) olarak, değiştirilmeden dağıtılır; kullanıcılar bunları uyumlu başka bir WinDivert sürümüyle değiştirebilir. WinDivert'in kaynak kodu yukarıdaki bağlantıdadır.
- **GoodbyeDPI (Apache 2.0):** Dosyalar değiştirilmemiştir. DPI Kalkan, GoodbyeDPI'ı ayrı bir işlem olarak komut satırı parametreleriyle çalıştırır.
- "GoodbyeDPI" ve "WinDivert" adları yalnızca uyumluluğu belirtmek için kullanılmıştır.
- Discord, Discord Inc.'in ticari markasıdır; DPI Kalkan Discord ile bağlantılı değildir.
