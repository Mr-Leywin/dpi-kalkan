using System;
using System.Collections.Generic;
using System.Linq;

namespace DPIKalkan
{
    /// <summary>Profil seçeneklerinin ayrıntılı, sade Türkçe açıklamaları.</summary>
    static class ProfileHelp
    {
        public sealed class Topic
        {
            public string Title, Text, Flag = "";
        }

        static Topic T(string flag, string title, string text) => new Topic { Flag = flag, Title = title, Text = text };

        public static readonly Dictionary<string, Topic> Topics = new Dictionary<string, Topic>
        {
            ["intro"] = T("", "Bu pencere nasıl kullanılır?",
                "Bir seçeneğin üzerine gelin ya da tıklayın; ne işe yaradığı burada açıklanır.\n\n" +
                "Servis sağlayıcınız Discord'u iki katmanda engeller:\n" +
                "1) DNS: 'discord.com nerede?' sorusuna yanlış cevap verir.\n" +
                "2) DPI: Bağlantının ilk paketindeki site adını (SNI) okuyup bağlantıyı keser.\n\n" +
                "DNS yönlendirmesi birinci katmanı, aşağıdaki DPI yöntemleri ikinci katmanı aşar. Türkiye'de ikisi birlikte gerekir."),
            ["name"] = T("", "Profil adı ve açıklama", "Profili listede tanımanızı sağlar. Komut satırına etkisi yoktur."),
            ["mode"] = T("-1 … -9", "Temel mod",
                "GoodbyeDPI'ın hazır yöntem paketlerinden birini seçer. Mod, aşağıdaki seçeneklerden birkaçını birden açar.\n\n" +
                "• Modern modlar (-5 … -9): Paketleri böler ve DPI cihazını kandırmak için 'sahte' paketler gönderir. Türkiye'de en iyi sonucu verirler.\n" +
                "• Klasik modlar (-1 … -4): Yalnızca paketleri böler ve HTTP başlıklarıyla oynar. Daha uyumludur ama güncel DPI sistemlerine karşı zayıftır.\n\n" +
                "Mod seçip ayrıca seçenek işaretlerseniz, seçenekler modun üzerine eklenir."),
            ["mode:"] = T("", "Mod yok", "Hiçbir hazır paket kullanılmaz; yalnızca aşağıda işaretlediğiniz seçenekler uygulanır. Yalnızca DNS yönlendirmesi istiyorsanız modu boş bırakın."),
            ["mode:-1"] = T("-1", "-1 · Klasik, en uyumlu", "= -p -r -s -f 2 -k 2 -n -e 2\nHTTP ve HTTPS paketlerini 2 baytlık parçalara böler, HTTP başlıklarında küçük değişiklikler yapar. Sahte paket göndermez. Her ağda çalışır ama en yavaş moddur ve sıkı DPI'ı aşamayabilir."),
            ["mode:-2"] = T("-2", "-2 · Klasik, HTTPS için daha hızlı", "= -p -r -s -f 2 -k 2 -n -e 40\n-1 ile aynı ama HTTPS paketleri 40 baytlık parçalara bölünür; bağlantı daha hızlı açılır."),
            ["mode:-3"] = T("-3", "-3 · Klasik, HTTP/HTTPS hızlı", "= -p -r -s -e 40\nHTTP kalıcı bağlantı parçalaması yapılmaz; daha da hızlıdır."),
            ["mode:-4"] = T("-4", "-4 · Klasik, en hızlı", "= -p -r -s\nYalnızca şifresiz HTTP başlık hileleri. HTTPS'e (Discord dahil) dokunmaz; tek başına Discord için yetersizdir. Eski 2 numaralı betik bunu TTL 5 sahte paketle birleştiriyordu."),
            ["mode:-5"] = T("-5", "-5 · Modern, otomatik TTL", "= -f 2 -e 2 --auto-ttl --reverse-frag --max-payload\nSahte paketlerin ömrünü (TTL) sunucuya olan mesafeye göre her bağlantıda otomatik ayarlar. Sabit TTL'li yöntemler zamanla bozulduğunda (eski 1 ve 2 numaralı betiklerde olduğu gibi) en dayanıklı seçenektir."),
            ["mode:-6"] = T("-6", "-6 · Modern, yanlış sıra numarası", "= -f 2 -e 2 --wrong-seq --reverse-frag --max-payload\nSahte paketler geçmiş bir TCP sıra numarasıyla gönderilir; sunucu onları yok sayar, DPI ise okuyup kandırılır."),
            ["mode:-7"] = T("-7", "-7 · Modern, yanlış sağlama toplamı", "= -f 2 -e 2 --wrong-chksum --reverse-frag --max-payload\nSahte paketler bozuk sağlama toplamıyla gönderilir. Bazı modemler ve sanal makineler bu paketleri yolda düşürdüğü için etkisiz kalabilir."),
            ["mode:-8"] = T("-8", "-8 · Modern, sıra + sağlama", "= -f 2 -e 2 --wrong-seq --wrong-chksum --reverse-frag --max-payload\n-6 ve -7'nin birlikte kullanılması. -9'dan tek farkı QUIC'i engellememesidir."),
            ["mode:-9"] = T("-9", "-9 · Modern, tam (varsayılan)", "= -f 2 -e 2 --wrong-seq --wrong-chksum --reverse-frag --max-payload -q\nGoodbyeDPI'ın varsayılanı ve en kapsamlı modu. Türkiye'de Discord için önerilen mod budur."),
            ["ttl"] = T("--set-ttl / --auto-ttl", "Sahte paket TTL (ömür)",
                "Sahte paket, servis sağlayıcının DPI cihazına ulaşmalı ama gerçek sunucuya ulaşmadan 'ölmelidir'. TTL, paketin kaç yönlendiriciden geçebileceğini belirler.\n\n" +
                "• Çok küçükse DPI cihazına bile ulaşmaz → etkisiz.\n• Çok büyükse sunucuya ulaşır → bağlantı bozulur.\n" +
                "• 'Otomatik' her bağlantıda mesafeyi ölçüp uygun değeri seçer; servis sağlayıcı ağ yapısını değiştirdiğinde bile çalışmaya devam eder.\n\n" +
                "Eski 1 numaralı betik TTL 3, 2 numaralı betik TTL 5 kullanıyordu; sabit değerler zamanla tutmaz hale gelebilir."),
            ["fakegen"] = T("--fake-gen", "Sahte paket sayısı",
                "Gerçek bağlantının önüne, içi rastgele doldurulmuş bu kadar sahte paket gönderilir. DPI cihazı sahte paketleri gerçek sanıp yanlış site adını okur.\n\n" +
                "0 = GoodbyeDPI'ın varsayılan tek sahte paketi. 3-10 arası sıkı DPI uygulayan hatlarda işe yarar; çok yüksek değer bağlantı açılışını yavaşlatır. En fazla 30."),
            ["fakeresend"] = T("--fake-resend", "Sahte paket tekrarı",
                "Her sahte paketi bu kadar kez gönderir. Wi-Fi ya da mobil hat gibi paket kaybı olan bağlantılarda, DPI cihazının sahte paketi mutlaka görmesini sağlar. 1 = tekrar yok."),
            ["dns"] = T("--dns-addr / --dns-port", "DNS yönlendirme",
                "Bilgisayarınızın yaptığı tüm DNS sorgularını (53. port) servis sağlayıcınızın sunucusu yerine seçilen sunucunun farklı bir portuna gönderir.\n\n" +
                "Türkiye'de Discord DNS düzeyinde de engelli olduğu için bu seçenek ZORUNLUDUR: kapalıysa Discord açılmaz (eski 1 ve 2 numaralı betiklerin çalışmama sebebi budur).\n\n" +
                "Servis sağlayıcılar yalnızca 53. portu denetlediği için 1253, 5353, 9953 gibi portlara giden sorgular engellenmez. Hazır seçeneklerin hepsi IPv6 sorgularını da yönlendirir."),
            ["dns:custom"] = T("--dns-addr / --dnsv6-addr", "Özel DNS sunucusu",
                "Kendi DNS sunucunuzu girin. Standart dışı bir port kullanan sunucu seçin; 53. port servis sağlayıcı tarafından yönlendirilebilir.\n\n" +
                "IPv6 bağlantınız varsa IPv6 adresini de girin; aksi halde Windows IPv6 üzerinden servis sağlayıcının DNS'ine sorup sahte yanıt alabilir (DK-404)."),
            ["httpsfrag"] = T("-e", "HTTPS parça boyutu",
                "Şifreli bağlantının ilk paketi (içinde site adı yazar) kaç baytlık parçalara bölünecek. DPI cihazı site adını tek parçada göremezse engelleyemez.\n\n" +
                "1-2: en etkili ama bağlantı açılışı biraz yavaşlar. 40: daha hızlı, bazı DPI'lar yakalayabilir. 0 = modun varsayılanı."),
            ["httpfrag"] = T("-f", "HTTP parça boyutu",
                "Şifresiz HTTP isteklerinin parça boyutu. Discord ve sitelerin neredeyse tamamı HTTPS kullandığı için etkisi küçüktür. 0 = modun varsayılanı."),
            ["wrongchk"] = T("--wrong-chksum", "Yanlış sağlama toplamı",
                "Sahte paketleri hatalı TCP sağlama toplamıyla gönderir. Gerçek sunucu bu paketleri çöpe atar; DPI cihazı ise çoğu zaman kontrol etmeden okur ve kandırılır.\n\n" +
                "Bazı modemler ve sanal makineler (VirtualBox/VMware NAT) hatalı paketleri yolda düşürdüğü için etkisiz kalabilir."),
            ["wrongseq"] = T("--wrong-seq", "Yanlış sıra numarası",
                "Sahte paketleri geçmişe ait bir TCP sıra/onay numarasıyla gönderir. Sunucu 'eski paket' diye yok sayar; DPI cihazı içeriğe bakıp yanlış site adını okur. Genellikle en güvenilir sahte paket yöntemidir."),
            ["native"] = T("--native-frag", "Yerel parçalama",
                "İlk paketi gerçekten ayrı ayrı küçük TCP paketleri olarak gönderir. Pencere boyutunu küçültmediği için bağlantı hızını düşürmez."),
            ["reverse"] = T("--reverse-frag", "Ters sıralı parçalama",
                "Parçaları ters sırayla gönderir (önce son parça). DPI cihazları ters gelen parçaları birleştiremez ve site adını okuyamaz. Çok nadiren bazı siteler ters sırayı işleyemez; sorun olursa kapatıp 'Yerel parçalama'yı kullanın."),
            ["quic"] = T("-q", "QUIC/HTTP3 engelle",
                "Tarayıcıların kullandığı QUIC/HTTP3 (UDP 443) trafiğini engeller. Tarayıcı normal HTTPS'e döner ve GoodbyeDPI bu trafiği işleyebilir. Önerilir; Discord sesli sohbetini etkilemez."),
            ["sni"] = T("--frag-by-sni", "Site adından böl (SNI)",
                "İlk paketi tam olarak site adının başladığı yerden böler; site adı iki ayrı pakete düşer. -e değerinden bağımsız, daha hedefli bir bölmedir."),
            ["payload"] = T("--max-payload", "Büyük paketleri atla",
                "1200 bayttan büyük veri taşıyan paketleri işlemez (dosya indirme, video akışı). İşlemci kullanımını azaltır; engel aşmayı etkilemez. Modern modlarda zaten açıktır."),
            ["nosni"] = T("--allow-no-sni", "Site adı okunamayanlara da uygula",
                "Yalnızca 'Hedefli mod' açıkken önemlidir: site adı okunamayan bağlantılara (ör. şifreli SNI/ECH kullananlar) da yöntemi uygular."),
            ["passive"] = T("-p", "Pasif DPI'ı engelle",
                "Servis sağlayıcının araya soktuğu sahte 'yönlendirme' (HTTP 302) ve bağlantı sıfırlama paketlerini yakalayıp düşürür. Klasik modlarda açıktır."),
            ["hostr"] = T("-r", "Host → hoSt",
                "Şifresiz HTTP isteklerinde 'Host' başlığını 'hoSt' yazar; eski DPI sistemleri başlığı tanıyamaz. HTTPS'e etkisi yoktur."),
            ["hosts"] = T("-s", "Host boşluğunu kaldır",
                "HTTP 'Host:' başlığındaki boşluğu kaldırır. Yalnızca şifresiz HTTP."),
            ["hostm"] = T("-m", "Host harflerini karıştır",
                "Site adındaki büyük/küçük harfleri karıştırır (dIsCoRd.CoM). Yalnızca şifresiz HTTP."),
            ["extra"] = T("", "Ek parametreler",
                "Yukarıda olmayan GoodbyeDPI seçeneklerini elle eklemek için. Örnekler:\n" +
                "• --min-ttl 3 : sahte paketi yalnızca en az 3 atlama uzaktaki sunuculara gönder\n" +
                "• --fake-from-hex 1603… : sahte paket içeriğini kendiniz belirleyin\n" +
                "• --port 8443 : ek TCP portunda da parçala\n\n" +
                "--blacklist eklemeyin; bunun için 'Hedef Siteler' sayfasını kullanın."),
            ["raw"] = T("", "Komutu elle yaz (uzman)",
                "Tüm GoodbyeDPI komut satırını kendiniz yazarsınız; yukarıdaki seçenekler yok sayılır. Yazım hatası olursa koruma 'DK-603 Geçersiz profil parametresi' hatasıyla başlamaz."),
            ["preview"] = T("", "Komut önizleme", "DPI Kalkan'ın bu profil için çalıştıracağı tam komut. Bu satırı bir .cmd dosyasına koyarak uygulamasız da kullanabilirsiniz."),
        };

        public static Topic Get(string key) => Topics.TryGetValue(key, out var t) ? t : Topics["intro"];

        static readonly Dictionary<string, string[]> ModeFlags = new Dictionary<string, string[]>
        {
            ["-1"] = new[] { "passive", "hostr", "hosts", "httpfrag2", "httpsfrag2" },
            ["-2"] = new[] { "passive", "hostr", "hosts", "httpfrag2", "httpsfrag40" },
            ["-3"] = new[] { "passive", "hostr", "hosts", "httpsfrag40" },
            ["-4"] = new[] { "passive", "hostr", "hosts" },
            ["-5"] = new[] { "httpfrag2", "httpsfrag2", "autottl", "reverse", "payload" },
            ["-6"] = new[] { "httpfrag2", "httpsfrag2", "wrongseq", "reverse", "payload" },
            ["-7"] = new[] { "httpfrag2", "httpsfrag2", "wrongchk", "reverse", "payload" },
            ["-8"] = new[] { "httpfrag2", "httpsfrag2", "wrongseq", "wrongchk", "reverse", "payload" },
            ["-9"] = new[] { "httpfrag2", "httpsfrag2", "wrongseq", "wrongchk", "reverse", "payload", "quic" },
        };

        /// <summary>Profilin yaptığı her şeyi sade dille madde madde açıklar.</summary>
        public static List<(string Title, string Text)> Explain(Profile p)
        {
            var list = new List<(string, string)>();
            if (p.UseRaw)
            {
                list.Add(("Elle yazılmış komut", "Bu profil komut satırını doğrudan kullanır: " + p.RawArgs));
                list.Add((p.UsesDnsRedirect ? "DNS yönlendirme: var" : "DNS yönlendirme: YOK",
                          p.UsesDnsRedirect ? "Komutta --dns-addr bulunuyor." : "Türkiye'de Discord DNS ile de engelli olduğu için bu profil tek başına Discord'u açmayabilir."));
                return list;
            }
            var flags = new HashSet<string>();
            if (!string.IsNullOrEmpty(p.Mode) && ModeFlags.TryGetValue(p.Mode, out var mf)) foreach (var f in mf) flags.Add(f);
            if (p.WrongChksum) flags.Add("wrongchk");
            if (p.WrongSeq) flags.Add("wrongseq");
            if (p.NativeFrag) flags.Add("native");
            if (p.ReverseFrag) flags.Add("reverse");
            if (p.BlockQuic) flags.Add("quic");
            if (p.FragBySni) flags.Add("sni");
            if (p.MaxPayload) flags.Add("payload");
            if (p.PassiveDpi) flags.Add("passive");
            if (p.HostReplace) flags.Add("hostr");
            if (p.HostRemoveSpace) flags.Add("hosts");
            if (p.HostMixCase) flags.Add("hostm");

            // DNS
            if (p.UsesDnsRedirect)
            {
                var prov = DnsProviders.Get(p.Dns);
                list.Add(("DNS yönlendirme → " + p.DnsLabel,
                    "DNS sorguları servis sağlayıcı yerine " + (prov != null ? prov.Name + " sunucusuna (" + prov.V4 + ", port " + prov.V4Port + ")" : "özel sunucuya") +
                    " gider. DNS engelini aşar." + (prov != null ? " " + prov.Note : "")));
            }
            else
                list.Add(("DNS yönlendirme: YOK",
                    "Sorgular servis sağlayıcının DNS'ine gider. Türkiye'de Discord DNS ile de engelli olduğu için bu profil tek başına Discord'u AÇMAZ " +
                    "(Windows DNS'ini 'DNS Ayarları' sayfasından değiştirmediyseniz)."));

            if (!string.IsNullOrEmpty(p.Mode))
                list.Add(("Temel mod " + p.Mode, Get("mode:" + p.Mode).Text.Split('\n').Last()));

            // parçalama
            int https = p.HttpsFrag > 0 ? p.HttpsFrag : flags.Contains("httpsfrag2") ? 2 : flags.Contains("httpsfrag40") ? 40 : 0;
            if (https > 0)
                list.Add(("HTTPS ilk paketi " + https + " baytlık parçalara bölünür",
                    "Site adı (SNI) tek parçada görünmez; DPI cihazı hangi siteye gittiğinizi okuyamaz." + (https <= 2 ? " Küçük parça en etkili seçenektir." : "")));
            if (flags.Contains("sni")) list.Add(("Site adının başından bölme", Get("sni").Text));
            if (flags.Contains("reverse")) list.Add(("Parçalar ters sırayla gönderilir", "DPI cihazı ters gelen parçaları birleştiremez."));
            else if (flags.Contains("native")) list.Add(("Parçalar normal sırayla gönderilir", Get("native").Text));

            // sahte paketler
            bool fake = flags.Contains("wrongseq") || flags.Contains("wrongchk") || flags.Contains("autottl") || p.Ttl.Length > 0 || p.FakeGen > 0;
            if (fake)
            {
                var how = new List<string>();
                if (flags.Contains("autottl") || p.Ttl == "auto") how.Add("ömrü (TTL) otomatik ayarlanır");
                else if (int.TryParse(p.Ttl, out int ttl)) how.Add("ömrü sabit TTL " + ttl);
                if (flags.Contains("wrongseq")) how.Add("geçmiş sıra numarasıyla");
                if (flags.Contains("wrongchk")) how.Add("bozuk sağlama toplamıyla");
                string count = p.FakeGen > 0 ? "Sahte paket: " + p.FakeGen + " adet rastgele" : "Sahte paket gönderilir";
                list.Add((count + (p.FakeResend > 1 ? ", her biri " + p.FakeResend + " kez" : ""),
                    "Gerçek bağlantıdan önce DPI cihazını kandıran sahte paket gönderilir (" + string.Join(", ", how) + "). Sunucu sahte paketi yok sayar."));
            }
            if (flags.Contains("quic")) list.Add(("QUIC engellenir", "Tarayıcılar HTTP3 yerine normal HTTPS kullanır; GoodbyeDPI bu trafiği işleyebilir."));
            if (flags.Contains("passive")) list.Add(("Pasif DPI paketleri düşürülür", Get("passive").Text));
            if (flags.Contains("hostr") || flags.Contains("hosts") || flags.Contains("hostm"))
                list.Add(("HTTP başlık hileleri", "Yalnızca şifresiz HTTP sitelerinde etkilidir; Discord için önemsizdir."));
            if (flags.Contains("payload")) list.Add(("Büyük paketler atlanır", "İndirme ve videolarda işlemci kullanımını azaltır."));
            if (!string.IsNullOrWhiteSpace(p.ExtraArgs)) list.Add(("Ek parametreler", p.ExtraArgs));
            if (!fake && https == 0 && !flags.Contains("sni") && !flags.Contains("hostr"))
                list.Add(("DPI yöntemi yok", "Bu profil yalnızca DNS'i yönlendirir. Servis sağlayıcı site adına göre de engelliyorsa yetmez."));
            return list;
        }

        public static string WhenToUse(Profile p)
        {
            switch (p.Id)
            {
                case "tr-onerilen": return "İlk tercih. Çalışıyorsa başka bir şeye gerek yok.";
                case "tr-adguard": case "tr-quad9": case "tr-opendns": return "Önerilen profil çalışıyor ama sayfalar yavaş açılıyorsa ya da Yandex DNS yanıt vermiyorsa.";
                case "tr-autottl": return "-9 modu bağlantıları kesiyorsa ya da sabit TTL'li eski betikler bir anda çalışmaz olduysa.";
                case "tr-eski1-dns": case "tr-eski2-dns": return "Eskiden bu betikler sizde çalışıyorduysa; artık DNS yönlendirmesi de var.";
                case "tr-e1": case "tr-sni": case "tr-alt": return "Önerilen profil 'bağlantı kesildi' (DK-501) hatası veriyorsa.";
                case "tr-fakegen": case "tr-resend": return "Wi-Fi/mobil hatlarda ya da önerilen profil kararsız çalışıyorsa.";
                case "tr-wrongchk": case "tr-wrongseq": case "tr-mode8": case "tr-native": return "Modeminiz bazı sahte paketleri düşürüyorsa farklı bir kandırma yöntemi denemek için.";
                case "tr-uyumlu": case "tr-klasik2": return "Modern yöntemlerin hiçbiri çalışmıyorsa son çare.";
                case "dns-only": return "Engel yalnızca DNS kaynaklıysa (Site Analizi 'YALNIZCA DNS ENGELİ' diyorsa).";
                case "eski-1": case "eski-2": case "sade": return "Yalnızca karşılaştırma için ya da Windows DNS'ini değiştirdiyseniz.";
            }
            return p.BuiltIn ? "" : "Kendi oluşturduğunuz profil.";
        }
    }
}
