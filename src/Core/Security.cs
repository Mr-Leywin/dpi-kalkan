using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Net;
using System.Net.Sockets;
using System.Security.AccessControl;
using System.Security.Cryptography;
using System.Security.Principal;
using System.Text;
using System.Text.RegularExpressions;

namespace DPIKalkan
{
    /// <summary>
    /// Kurulum klasörü korumalı mı? (Yalnızca Yöneticiler, SYSTEM ve TrustedInstaller yazabiliyor mu?)
    /// SYSTEM yetkisiyle çalışan arka plan hizmeti ve yönetici yetkili açılış görevi yalnızca korumalı kurulumda
    /// (ör. kurulum paketiyle Program Files'a) kullanılabilir. Taşınabilir (zip) sürüm uygulama modunda çalışır.
    /// Bu sınıf yalnızca okur; hiçbir izni değiştirmez.
    /// </summary>
    public static class InstallGuard
    {
        static readonly SecurityIdentifier[] Trusted =
        {
            new SecurityIdentifier(WellKnownSidType.LocalSystemSid, null),
            new SecurityIdentifier(WellKnownSidType.BuiltinAdministratorsSid, null),
            new SecurityIdentifier("S-1-5-80-956008885-3418522649-1831038044-1853292631-2271478464"),   // NT SERVICE\TrustedInstaller (tüm Windows'larda aynı)
        };

        // Klasörün ve içindeki dosyaların kendisi: yazma, ekleme, silme, öznitelik ve izin değiştirme
        // (FILE_WRITE_DATA | FILE_APPEND_DATA | FILE_WRITE_EA | FILE_DELETE_CHILD | FILE_WRITE_ATTRIBUTES | DELETE | WRITE_DAC | WRITE_OWNER | GENERIC_ALL | GENERIC_WRITE)
        const int StrictMask = 0x2 | 0x4 | 0x10 | 0x40 | 0x100 | 0x10000 | 0x40000 | 0x80000 | 0x10000000 | 0x40000000;
        // Üst klasörler: yeniden adlandırma/silme ve izin değiştirme yasak; yeni klasör oluşturmak serbest (ör. C:\)
        const int AncestorMask = 0x40 | 0x10000 | 0x40000 | 0x80000 | 0x10000000 | 0x40000000;

        public const string NeedInstall =
            "Bu özellik yalnızca kurulum paketiyle (Program Files'a) kurulan DPI Kalkan'da kullanılabilir.\n\n" +
            "Taşınabilir (zip) sürüm, yönetici izni olmadan değiştirilebilen bir klasörde çalışıyor. Buradaki dosyaları " +
            "SYSTEM yetkili bir hizmetle ya da izin sormadan yönetici olarak açılan bir görevle çalıştırmak, bilgisayardaki " +
            "zararlı bir programın yetki kazanmasına yol açabileceği için kapalıdır.\n\nKoruma uygulama modunda normal çalışır.";

        static readonly object Gate = new object();
        static bool? cached;
        static string cachedReason = "";

        /// <summary>Uygulamanın kendi klasörü korumalı mı (önbellekli).</summary>
        public static bool IsProtected => Evaluate(false);
        public static bool IsPortable => !IsProtected;

        /// <summary>Korumasızsa nedeni (kullanıcıya gösterilir).</summary>
        public static string Reason { get { Evaluate(false); return cachedReason; } }

        public static bool Evaluate(bool refresh)
        {
            lock (Gate)
            {
                if (cached == null || refresh)
                {
                    cached = Check(App.BaseDir, out cachedReason);
                    App.Log("Kurulum klasörü: " + (cached.Value ? "korumalı (kurulumlu sürüm)" : "taşınabilir – " + cachedReason), LogLevel.Debug);
                }
                return cached.Value;
            }
        }

        /// <summary>Klasör, alt klasörleri, uygulama/GoodbyeDPI dosyaları, veri klasörü ve tüm üst klasörler denetlenir.</summary>
        public static bool Check(string baseDir, out string reason)
        {
            reason = "";
            try
            {
                string b = Path.GetFullPath(baseDir).TrimEnd('\\');
                if (b.StartsWith(@"\\")) { reason = "Uygulama bir ağ klasöründe."; return false; }
                if (!CheckObject(b, true, StrictMask, out reason)) return false;
                string exe = Path.Combine(b, "DPIKalkan.exe");
                if (File.Exists(exe) && !CheckObject(exe, false, StrictMask, out reason)) return false;
                foreach (var sub in new[] { "bin", @"bin\x86", @"bin\x86_64", "veri" })
                {
                    string d = Path.Combine(b, sub);
                    if (!Directory.Exists(d)) continue;
                    if (!CheckObject(d, true, StrictMask, out reason)) return false;
                    if (sub == "bin") continue;
                    foreach (var f in Directory.GetFiles(d))
                        if (!CheckObject(f, false, StrictMask, out reason)) return false;
                }
                for (var p = Directory.GetParent(b); p != null; p = p.Parent)
                    if (!CheckObject(p.FullName, true, AncestorMask, out reason)) return false;
                return true;
            }
            catch (Exception ex)
            {
                reason = "Klasör izinleri okunamadı: " + ex.Message;
                return false;
            }
        }

        static bool CheckObject(string path, bool dir, int mask, out string reason)
        {
            reason = "";
            const AccessControlSections s = AccessControlSections.Access | AccessControlSections.Owner;
            FileSystemSecurity sec = dir ? (FileSystemSecurity)Directory.GetAccessControl(path, s) : File.GetAccessControl(path, s);
            var owner = sec.GetOwner(typeof(SecurityIdentifier)) as SecurityIdentifier;
            if (owner == null || !Trusted.Contains(owner))
            {
                reason = "'" + path + "' sahibi " + Name(owner) + " (yönetici değil).";
                return false;
            }
            foreach (FileSystemAccessRule r in sec.GetAccessRules(true, true, typeof(SecurityIdentifier)))
            {
                if (r.AccessControlType != AccessControlType.Allow) continue;
                if ((r.PropagationFlags & PropagationFlags.InheritOnly) != 0) continue;   // yalnızca yeni alt öğelere uygulanır
                var sid = r.IdentityReference as SecurityIdentifier;
                if (sid != null && Trusted.Contains(sid)) continue;
                if (((int)r.FileSystemRights & mask) != 0)
                {
                    reason = Name(sid) + " şunu değiştirebiliyor: " + path;
                    return false;
                }
            }
            return true;
        }

        /// <summary>Hesap adı: yalnızca genel gruplar adıyla yazılır; kişisel hesap adı günlüğe/destek paketine girmesin diye gizlenir.</summary>
        static string Name(SecurityIdentifier sid)
        {
            if (sid == null) return "?";
            foreach (var t in new[] { WellKnownSidType.BuiltinUsersSid, WellKnownSidType.AuthenticatedUserSid, WellKnownSidType.WorldSid,
                                      WellKnownSidType.InteractiveSid, WellKnownSidType.CreatorOwnerSid, WellKnownSidType.BuiltinGuestsSid,
                                      WellKnownSidType.LocalServiceSid, WellKnownSidType.NetworkServiceSid })
                if (sid.IsWellKnown(t))
                {
                    try { return sid.Translate(typeof(NTAccount)).Value; } catch { return sid.Value; }
                }
            return "bir kullanıcı hesabı";
        }
    }

    /// <summary>
    /// GoodbyeDPI/WinDivert dosyalarının resmi 0.2.3rc3 sürümüyle aynı olduğunu doğrular (SHA-256, programa gömülü).
    /// Dosyalar doğrulama ile başlatma arasında değiştirilemesin diye yazmaya kapalı olarak açık tutulur.
    /// </summary>
    public static class Integrity
    {
        /// <summary>third_party\goodbyedpi\README.md ile aynı (ValdikSS'in resmi goodbyedpi-0.2.3rc3-2.zip paketi).</summary>
        public static readonly Dictionary<string, string> Known = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase)
        {
            [@"x86_64\goodbyedpi.exe"] = "8d412b094bb9c137ff25ba9a794d1122ecc84bb776debff6c249723a13cc31cd",
            [@"x86_64\WinDivert.dll"] = "6110bfa44667405179c3e15e12af1b62037e447ed59b054b19042032995e6c7e",
            [@"x86_64\WinDivert64.sys"] = "e69b5ba3f0cd6cfb2983e442636e7f0b342b61b15264b0328317d4559c82cf50",
            [@"x86\goodbyedpi.exe"] = "66e202c9fce9e769e2bc791b7fd6f56f21eab59f607f4ed0724e0c68c430dd1f",
            [@"x86\WinDivert.dll"] = "625ffdd95bfabff32d0e8a95beabcd303c01c8bba73b90402d4e84d6e15dd8e5",
            [@"x86\WinDivert32.sys"] = "29ca5ceb59c9c6993a349e82b1fd46078e6f8a302764153ab84fa22e382fcdca",
            [@"x86\WinDivert64.sys"] = "e69b5ba3f0cd6cfb2983e442636e7f0b342b61b15264b0328317d4559c82cf50",
        };

        public static string Hex(byte[] b) => BitConverter.ToString(b).Replace("-", "").ToLowerInvariant();

        public static string Sha256(Stream s)
        {
            s.Position = 0;
            using (var h = SHA256.Create()) return Hex(h.ComputeHash(s));
        }

        public static string Sha256File(string path)
        {
            using (var fs = new FileStream(path, FileMode.Open, FileAccess.Read, FileShare.Read)) return Sha256(fs);
        }

        public sealed class Hold : IDisposable
        {
            internal readonly List<FileStream> Streams = new List<FileStream>();
            public void Dispose() { foreach (var s in Streams) try { s.Dispose(); } catch { } Streams.Clear(); }
        }

        /// <summary>
        /// bin\&lt;mimari&gt; içindeki dosyaları doğrular. Başarılıysa exe ve dll yazmaya kapalı açık tutulur (Dispose edilene kadar);
        /// başarısızsa null döner. .sys dosyası sürücü yüklemesini etkilememek için yalnızca doğrulanıp kapatılır.
        /// </summary>
        public static Hold VerifyAndLock(string binDir, string arch, IEnumerable<string> files, out string error)
        {
            error = "";
            var hold = new Hold();
            try
            {
                foreach (var name in files)
                {
                    string rel = arch + "\\" + name;
                    if (!Known.TryGetValue(rel, out var expected)) { error = rel + " için bilinen özet yok."; hold.Dispose(); return null; }
                    var fs = new FileStream(Path.Combine(binDir, name), FileMode.Open, FileAccess.Read, FileShare.Read);
                    string got = Sha256(fs);
                    if (got != expected)
                    {
                        fs.Dispose();
                        error = name + " resmi sürümle aynı değil (SHA-256 " + got.Substring(0, 12) + "…, beklenen " + expected.Substring(0, 12) + "…).";
                        hold.Dispose();
                        return null;
                    }
                    if (name.EndsWith(".sys", StringComparison.OrdinalIgnoreCase)) fs.Dispose();
                    else hold.Streams.Add(fs);
                }
                return hold;
            }
            catch (Exception ex)
            {
                hold.Dispose();
                error = ex is FileNotFoundException f ? Path.GetFileName(f.FileName) + " bulunamadı." : ex.Message;
                return null;
            }
        }

        /// <summary>Bir bin kökündeki (x86 ve x86_64) tüm bilinen dosyaları doğrular (test ve paket denetimi için).</summary>
        public static bool VerifyTree(string binRoot, out string error)
        {
            error = "";
            foreach (var kv in Known)
            {
                string f = Path.Combine(binRoot, kv.Key);
                if (!File.Exists(f)) { error = kv.Key + " yok"; return false; }
                if (Sha256File(f) != kv.Value) { error = kv.Key + " özeti uyuşmuyor"; return false; }
            }
            return true;
        }
    }

    /// <summary>
    /// GoodbyeDPI parametreleri için beyaz liste. Yalnızca 0.2.3rc3'ün bilinen seçenekleri, tam adlarıyla kabul edilir
    /// (GoodbyeDPI'ın getopt_long ayrıştırıcısı "--black" gibi kısaltmaları da kabul ettiği için kısaltmalar reddedilir).
    /// Değerler sayı aralığı, IP adresi ya da onaltılık metin olarak denetlenir; komut satırı özel karakterleri geçemez.
    /// </summary>
    public static class ArgPolicy
    {
        enum K { Flag, Int, Ip4, Ip6, Hex, OptAutoTtl, OptInt }

        sealed class Spec
        {
            public K Kind;
            public int Min, Max;
        }

        static Spec F() => new Spec { Kind = K.Flag };
        static Spec I(int min, int max) => new Spec { Kind = K.Int, Min = min, Max = max };

        static readonly Dictionary<string, Spec> Opts = new Dictionary<string, Spec>(StringComparer.Ordinal)
        {
            ["-p"] = F(), ["-q"] = F(), ["-r"] = F(), ["-s"] = F(), ["-a"] = F(), ["-m"] = F(), ["-n"] = F(), ["-w"] = F(),
            ["-1"] = F(), ["-2"] = F(), ["-3"] = F(), ["-4"] = F(), ["-5"] = F(), ["-6"] = F(), ["-7"] = F(), ["-8"] = F(), ["-9"] = F(),
            ["-f"] = I(1, 65535), ["-k"] = I(1, 65535), ["-e"] = I(1, 65535),
            ["--port"] = I(1, 65535), ["--ip-id"] = I(0, 65535),
            ["--dns-addr"] = new Spec { Kind = K.Ip4 }, ["--dns-port"] = I(1, 65535),
            ["--dnsv6-addr"] = new Spec { Kind = K.Ip6 }, ["--dnsv6-port"] = I(1, 65535),
            ["--dns-verb"] = F(), ["--allow-no-sni"] = F(), ["--frag-by-sni"] = F(),
            ["--set-ttl"] = I(1, 255), ["--auto-ttl"] = new Spec { Kind = K.OptAutoTtl }, ["--min-ttl"] = I(1, 255),
            ["--wrong-chksum"] = F(), ["--wrong-seq"] = F(), ["--native-frag"] = F(), ["--reverse-frag"] = F(),
            ["--fake-from-hex"] = new Spec { Kind = K.Hex }, ["--fake-gen"] = I(1, 30), ["--fake-resend"] = I(1, 100),
            ["--max-payload"] = new Spec { Kind = K.OptInt, Min = 0, Max = 65535 },
        };

        static bool AllowedChar(char c) =>
            c == ' ' || c == '-' || c == '.' || c == ':' || c == '=' ||
            (c >= '0' && c <= '9') || (c >= 'a' && c <= 'z') || (c >= 'A' && c <= 'Z');

        /// <summary>Kullanıcıdan/koddan gelen parametreleri (BuildArgs(null) çıktısı, --blacklist hariç) denetler.</summary>
        public static bool Validate(string args, out string error)
        {
            error = "";
            args = args ?? "";
            if (args.Length > 4000) { error = "Parametreler çok uzun."; return false; }
            foreach (char c in args)
                if (!AllowedChar(c))
                {
                    error = "İzin verilmeyen karakter: " + (c < 32 ? "(kontrol karakteri)" : "'" + c + "'") + ".";
                    return false;
                }
            var t = args.Split(new[] { ' ' }, StringSplitOptions.RemoveEmptyEntries);
            for (int i = 0; i < t.Length; i++)
            {
                string tok = t[i], name = tok, val = null;
                if (!tok.StartsWith("-")) { error = "Beklenmeyen değer: " + tok; return false; }
                int eq = tok.IndexOf('=');
                if (eq > 0)
                {
                    if (!tok.StartsWith("--")) { error = "Kısa seçeneklerde '=' kullanılamaz: " + tok; return false; }
                    name = tok.Substring(0, eq);
                    val = tok.Substring(eq + 1);
                }
                if (name.Length >= 3 && "--blacklist".StartsWith(name, StringComparison.Ordinal))
                {
                    error = "--blacklist (ya da kısaltması) elle eklenemez; Hedef Siteler sayfasındaki hedefli modu kullanın.";
                    return false;
                }
                if (!Opts.TryGetValue(name, out var s))
                {
                    error = "Bilinmeyen ya da kısaltılmış parametre: " + name + " (seçenekleri tam adıyla ve ayrı ayrı yazın).";
                    return false;
                }
                switch (s.Kind)
                {
                    case K.Flag:
                        if (val != null) { error = name + " değer almaz."; return false; }
                        break;
                    case K.OptAutoTtl:
                        if (val != null && !Regex.IsMatch(val, @"^\d{1,3}-\d{1,3}-\d{1,3}$")) { error = name + " değeri a1-a2-m biçiminde olmalı (ör. 1-4-10)."; return false; }
                        break;
                    case K.OptInt:
                        if (val != null && !IntOk(val, s.Min, s.Max)) { error = name + " değeri " + s.Min + "–" + s.Max + " arasında bir sayı olmalı."; return false; }
                        break;
                    default:
                        if (val == null)
                        {
                            if (i + 1 >= t.Length) { error = name + " için değer eksik."; return false; }
                            val = t[++i];
                        }
                        if (!ValueOk(s, val)) { error = name + " için geçersiz değer: " + val; return false; }
                        break;
                }
            }
            return true;
        }

        static bool IntOk(string v, int min, int max) =>
            v.Length > 0 && v.Length <= 6 && v.All(char.IsDigit) && int.TryParse(v, out int n) && n >= min && n <= max;

        static bool ValueOk(Spec s, string v)
        {
            switch (s.Kind)
            {
                case K.Int: return IntOk(v, s.Min, s.Max);
                case K.Ip4: return Regex.IsMatch(v, @"^\d{1,3}(\.\d{1,3}){3}$") && IPAddress.TryParse(v, out var a4) && a4.AddressFamily == AddressFamily.InterNetwork;
                case K.Ip6: return v.Contains(":") && IPAddress.TryParse(v, out var a6) && a6.AddressFamily == AddressFamily.InterNetworkV6;
                case K.Hex: return v.Length >= 2 && v.Length <= 4096 && v.Length % 2 == 0 && Regex.IsMatch(v, "^[0-9a-fA-F]+$");
                default: return false;
            }
        }

        /// <summary>Profilin ürettiği parametreleri denetler.</summary>
        public static bool ValidateProfile(Profile p, out string error)
        {
            if (p == null) { error = "Profil yok."; return false; }
            return Validate(p.BuildArgs(null), out error);
        }

        /// <summary>Dışarıdan gelen ad/açıklama metinlerinden kontrol karakterlerini atar ve uzunluğu sınırlar.</summary>
        public static string CleanText(string s, int max)
        {
            var sb = new StringBuilder();
            foreach (char c in s ?? "")
                sb.Append(char.IsControl(c) ? ' ' : c);
            string r = Regex.Replace(sb.ToString(), @"\s{2,}", " ").Trim();
            return r.Length > max ? r.Substring(0, max) : r;
        }
    }

    /// <summary>cmd.exe komut satırına giren değerlerin güvenlik denetimleri.</summary>
    public static class CmdLine
    {
        static bool HasControl(string s) => (s ?? "").Any(char.IsControl);

        /// <summary>Tırnak içinde kullanılacak değer: tırnak, yüzde (değişken açılımı), ünlem ve kontrol karakteri olamaz.</summary>
        public static bool QuotedSafe(string s) => s != null && s.IndexOfAny(new[] { '"', '%', '!' }) < 0 && !HasControl(s);

        /// <summary>Tırnaksız kullanılacak değer: komut ayırıcıları ve yönlendirmeler olamaz.</summary>
        public static bool BareSafe(string s) => s != null && s.IndexOfAny(new[] { '&', '|', '<', '>', '^', '%', '!' }) < 0 && !HasControl(s);

        /// <summary>.cmd betiklerindeki REM satırları için: yalnızca güvenli yazdırılabilir karakterler kalır.</summary>
        public static string RemText(string s)
        {
            var sb = new StringBuilder();
            foreach (char c in s ?? "")
                sb.Append(char.IsLetterOrDigit(c) || " -_.,:;()'/+[]#".IndexOf(c) >= 0 ? c : ' ');
            return Regex.Replace(sb.ToString(), @" {2,}", " ").Trim();
        }

        public static bool HasControlChars(string s) => HasControl(s);
    }
}
