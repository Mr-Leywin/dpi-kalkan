using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.IO.Compression;
using System.Linq;
using System.Net;
using System.Reflection;
using System.Security.Cryptography;
using System.ServiceProcess;
using System.Text;
using System.Text.RegularExpressions;
using System.Threading;
using System.Web.Script.Serialization;

namespace DPIKalkan
{
    public sealed class UpdateInfo
    {
        public Version Version;
        public string Tag = "", Name = "", Notes = "", HtmlUrl = "", AssetName = "", AssetUrl = "", Sha256 = "";
        public long Size;
        /// <summary>Kurulum paketi (DPI-Kalkan-x.y.z-Setup.exe); kurulumlu sürüm bununla güncellenir.</summary>
        public string SetupName = "", SetupUrl = "", SetupSha256 = "";
        public long SetupSize;
        public DateTime Published;
    }

    /// <summary>
    /// GitHub sürümleri üzerinden otomatik güncelleme. İndirilen paketin SHA-256 özeti GitHub'ın bildirdiği
    /// özetle karşılaştırılır; uyuşmazsa güncelleme yapılmaz. Kurulum, yeni sürümün kendi exe'si tarafından
    /// (--apply-update) eski uygulama kapandıktan sonra yapılır; hata olursa önceki sürüme geri dönülür.
    /// </summary>
    public static class Updater
    {
        public const string Owner = "Mr-Leywin", Repo = "dpi-kalkan";
        public static UpdateInfo Available { get; private set; }
#if PREVIEW
        internal static void DebugSetAvailable(UpdateInfo i) => Available = i;
#endif
        public static bool Checking { get; private set; }

        public static Version Current => Norm(Assembly.GetExecutingAssembly().GetName().Version);
        static Version Norm(Version v) => new Version(v.Major, v.Minor, Math.Max(0, v.Build));

        /// <summary>
        /// İndirme ve açma klasörü: uygulamanın kendi veri klasörü. Kurulumlu sürümde yalnızca yöneticiler yazabilir;
        /// taşınabilir sürümde uygulama klasörüyle aynı güven düzeyindedir (herkesin yazabildiği %TEMP% kullanılmaz).
        /// </summary>
        public static string UpdateRoot => Path.Combine(App.DataDir, "guncelleme");
        /// <summary>2.3.0'ın kullandığı eski konum: eski sürümden gelen güncellemeler buradan kurulur.</summary>
        public static string LegacyRoot => Path.Combine(Path.GetTempPath(), "DPIKalkan-guncelleme");

        static HttpWebRequest Req(string url, int timeout)
        {
            var r = (HttpWebRequest)WebRequest.Create(url);
            r.UserAgent = "DPIKalkan/" + App.Version + " (+https://github.com/" + Owner + "/" + Repo + ")";
            r.Timeout = timeout;
            r.ReadWriteTimeout = timeout;
            r.AutomaticDecompression = DecompressionMethods.GZip | DecompressionMethods.Deflate;
            return r;
        }

        /// <summary>GitHub'daki en son sürümü okur (yeni değilse de bilgiyi döndürür).</summary>
        public static UpdateInfo Check(out string error)
        {
            error = "";
            Checking = true;
            try
            {
                var r = Req("https://api.github.com/repos/" + Owner + "/" + Repo + "/releases/latest", 15000);
                r.Accept = "application/vnd.github+json";
                r.Headers["X-GitHub-Api-Version"] = "2022-11-28";
                string json;
                using (var resp = r.GetResponse())
                {
                    var u = resp.ResponseUri;
                    if (u == null || u.Scheme != "https" || u.Host != "api.github.com")
                        throw new InvalidOperationException("Sürüm bilgisi isteği beklenmeyen bir adrese yönlendirildi: " + u?.Host);
                    using (var sr = new StreamReader(resp.GetResponseStream(), Encoding.UTF8))
                        json = sr.ReadToEnd();
                }
                var info = Parse(json);
                if (info == null) { error = "Sürüm bilgisi okunamadı."; return null; }
                if (App.Settings != null) { App.Settings.LastUpdateCheck = DateTime.Now; App.Post(App.Save); }
                Available = IsNewer(info) && info.AssetUrl.Length > 0 ? info : null;
                App.Log("Güncelleme denetimi: en son sürüm " + info.Version + (Available != null ? " – yeni sürüm var." : " – güncelsiniz."), LogLevel.Info);
                App.RaiseStatus();
                return info;
            }
            catch (WebException ex) when (ex.Response is HttpWebResponse hr && (int)hr.StatusCode == 404)
            {
                error = "Henüz yayımlanmış sürüm yok.";
                return null;
            }
            catch (Exception ex)
            {
                error = "GitHub'a ulaşılamadı: " + ex.Message;
                App.Log("Güncelleme denetimi başarısız: " + ex.Message, LogLevel.Debug);
                return null;
            }
            finally { Checking = false; }
        }

        /// <summary>GitHub API yanıtını ayrıştırır.</summary>
        public static UpdateInfo Parse(string json)
        {
            var d = new JavaScriptSerializer { MaxJsonLength = 8 * 1024 * 1024 }.Deserialize<Dictionary<string, object>>(json);
            string S(Dictionary<string, object> o, string k) => o != null && o.TryGetValue(k, out var v) && v != null ? v.ToString() : "";
            string tag = S(d, "tag_name");
            if (!Version.TryParse(tag.TrimStart('v', 'V'), out var ver)) return null;
            if (d.TryGetValue("draft", out var dr) && dr is bool b1 && b1) return null;
            if (d.TryGetValue("prerelease", out var pr) && pr is bool b2 && b2) return null;
            var info = new UpdateInfo
            {
                Version = Norm(ver), Tag = tag, Name = S(d, "name"), Notes = S(d, "body"), HtmlUrl = S(d, "html_url"),
            };
            DateTime.TryParse(S(d, "published_at"), null, System.Globalization.DateTimeStyles.AdjustToUniversal, out info.Published);
            if (d.TryGetValue("assets", out var a) && a is System.Collections.ArrayList list)
            {
                foreach (Dictionary<string, object> asset in list.OfType<Dictionary<string, object>>())
                {
                    string name = S(asset, "name");
                    bool isZip = Regex.IsMatch(name, @"^DPI-Kalkan-[\d.]+\.zip$", RegexOptions.IgnoreCase);
                    bool isSetup = Regex.IsMatch(name, @"^DPI-Kalkan-[\d.]+-Setup\.exe$", RegexOptions.IgnoreCase);
                    if (!isZip && !isSetup) continue;
                    string digest = S(asset, "digest");
                    string sha = digest.StartsWith("sha256:", StringComparison.OrdinalIgnoreCase) ? digest.Substring(7).ToLowerInvariant() : "";
                    long.TryParse(S(asset, "size"), out long size);
                    if (isZip && info.AssetName.Length == 0)
                    { info.AssetName = name; info.AssetUrl = S(asset, "browser_download_url"); info.Size = size; info.Sha256 = sha; }
                    if (isSetup && info.SetupName.Length == 0)
                    { info.SetupName = name; info.SetupUrl = S(asset, "browser_download_url"); info.SetupSize = size; info.SetupSha256 = sha; }
                }
            }
            // GitHub özeti yoksa sürüm notlarındaki "SHA-256 (dosya adı): `özet`" satırları
            string FromNotes(string asset)
            {
                if (asset.Length == 0) return "";
                var m = Regex.Match(info.Notes, Regex.Escape(asset) + @"\)?\W{0,6}`([0-9a-fA-F]{64})`");
                return m.Success ? m.Groups[1].Value.ToLowerInvariant() : "";
            }
            if (info.Sha256.Length == 0) info.Sha256 = FromNotes(info.AssetName);
            if (info.Sha256.Length == 0)
            {
                var m = Regex.Match(info.Notes, @"SHA-?256[^`\n]*`([0-9a-fA-F]{64})`");   // 2.3.0 biçimi (tek özet)
                if (m.Success) info.Sha256 = m.Groups[1].Value.ToLowerInvariant();
            }
            if (info.SetupSha256.Length == 0) info.SetupSha256 = FromNotes(info.SetupName);
            return info;
        }

        public static bool IsNewer(UpdateInfo i) => i != null && i.Version > Current;

        /// <summary>Kullanıcı bu sürümü atlamadıysa ve otomatik denetim açıksa günde bir denetler.</summary>
        public static bool DueForCheck =>
            App.Settings.AutoUpdate && (!App.Settings.LastUpdateCheck.HasValue || (DateTime.Now - App.Settings.LastUpdateCheck.Value).TotalHours >= 20);

        public static bool Skipped(UpdateInfo i) => i != null && App.Settings.SkipVersion == i.Version.ToString();

        internal static bool TrustedUrl(string url) =>
            Uri.TryCreate(url, UriKind.Absolute, out var u) && u.Scheme == "https" &&
            (u.Host == "github.com" || u.Host.EndsWith(".githubusercontent.com"));

        /// <summary>İndirilip açılmış, kurulmaya hazır paket.</summary>
        public sealed class Prepared
        {
            public string Root;       // zip: içinde DPIKalkan.exe olan klasör
            public string SetupPath;  // kurulum paketi: indirilen Setup.exe
            public string ExeSha256;  // başlatılacak dosyanın özeti (başlatmadan hemen önce yeniden doğrulanır)
        }

        /// <summary>
        /// Paketi indirir, SHA-256 özetini doğrular ve AYNI dosya tutamacından (yazmaya kapalı) açar; böylece doğrulama
        /// ile açma arasında paket değiştirilemez. Yönlendirme sonrası varılan adres de GitHub olmalıdır.
        /// </summary>
        public static Prepared DownloadAndExtract(UpdateInfo i, Action<long, long> progress, CancellationToken ct)
        {
            if (!TrustedUrl(i.AssetUrl)) throw new InvalidOperationException("Güncelleme adresi GitHub dışında: " + i.AssetUrl);
            if (i.Sha256.Length != 64) throw new InvalidOperationException("Bu sürüm için doğrulama özeti (SHA-256) yok; güvenlik nedeniyle güncelleme yapılmadı.");
            Directory.CreateDirectory(UpdateRoot);
            string zipPath = Path.Combine(UpdateRoot, Path.GetFileName(i.AssetName));
            string dir = Path.Combine(UpdateRoot, "v" + i.Version);
            if (Directory.Exists(dir)) Directory.Delete(dir, true);
            Dictionary<string, string> hashes;
            try
            {
                using (var fs = new FileStream(zipPath, FileMode.Create, FileAccess.ReadWrite, FileShare.None))
                {
                    DownloadTo(i.AssetUrl, fs, i.Sha256, i.Size, progress, ct);
                    App.Log("Güncelleme indirildi ve doğrulandı: " + i.AssetName + " (SHA-256 " + i.Sha256.Substring(0, 16) + "…)", LogLevel.Success);
                    fs.Flush();
                    fs.Position = 0;
                    using (var za = new ZipArchive(fs, ZipArchiveMode.Read, true))
                        hashes = ExtractSafe(za, dir);
                }
            }
            catch
            {
                try { File.Delete(zipPath); } catch (Exception ign) { App.Ignored("Geçici güncelleme dosyası silinemedi", ign); }
                try { if (Directory.Exists(dir)) Directory.Delete(dir, true); } catch (Exception ign) { App.Ignored("Geçici güncelleme klasörü silinemedi", ign); }
                throw;
            }
            try { File.Delete(zipPath); } catch (Exception ign) { App.Ignored("İndirilen zip silinemedi", ign); }

            string exe = Directory.GetFiles(dir, "DPIKalkan.exe", SearchOption.AllDirectories).OrderBy(f => f.Length).FirstOrDefault();
            if (exe == null) throw new InvalidDataException("Pakette DPIKalkan.exe yok.");
            string root = Path.GetDirectoryName(exe);
            foreach (var req in new[] { @"bin\x86_64\goodbyedpi.exe", @"bin\x86_64\WinDivert.dll", @"bin\x86\goodbyedpi.exe" })
                if (!File.Exists(Path.Combine(root, req))) throw new InvalidDataException("Paket eksik: " + req);
            var fv = FileVersionInfo.GetVersionInfo(exe);
            if (!Version.TryParse(fv.FileVersion, out var exeVer) || Norm(exeVer) != i.Version)
                throw new InvalidDataException("Paketteki uygulama sürümü (" + fv.FileVersion + ") beklenen sürümle (" + i.Version + ") uyuşmuyor.");
            return new Prepared { Root = root, ExeSha256 = hashes[Path.GetFullPath(exe)] };
        }

        /// <summary>
        /// Adresi indirir, verilen akışa yazarken SHA-256 hesaplar; yönlendirme sonrası adres GitHub değilse ya da özet
        /// uyuşmazsa hata verir (çağıran dosyayı siler).
        /// </summary>
        static void DownloadTo(string url, Stream dst, string expectedSha, long size, Action<long, long> progress, CancellationToken ct)
        {
            var r = Req(url, 30000);
            using (var resp = r.GetResponse())
            {
                if (!TrustedUrl(resp.ResponseUri?.AbsoluteUri))
                    throw new InvalidOperationException("Güncelleme isteği GitHub dışına yönlendirildi: " + resp.ResponseUri?.Host);
                using (var src = resp.GetResponseStream())
                using (var sha = SHA256.Create())
                {
                    long total = resp.ContentLength > 0 ? resp.ContentLength : size, got = 0;
                    var buf = new byte[81920];
                    int n;
                    while ((n = src.Read(buf, 0, buf.Length)) > 0)
                    {
                        ct.ThrowIfCancellationRequested();
                        dst.Write(buf, 0, n);
                        sha.TransformBlock(buf, 0, n, null, 0);
                        got += n;
                        progress?.Invoke(got, total);
                        if (got > 200L * 1024 * 1024) throw new InvalidOperationException("Paket beklenenden çok büyük.");
                    }
                    sha.TransformFinalBlock(new byte[0], 0, 0);
                    string hash = Integrity.Hex(sha.Hash);
                    if (hash != expectedSha)
                        throw new InvalidDataException("İndirilen dosyanın özeti uyuşmuyor (beklenen " + expectedSha.Substring(0, 12) + "…, gelen " + hash.Substring(0, 12) + "…). Güncelleme iptal edildi.");
                }
            }
            dst.Flush();
        }

        // ================================================================ kurulumlu sürüm (Inno Setup)

        /// <summary>Kurulum paketinin uygulama kimliği (installer\DPIKalkan.iss içindeki AppId ile aynı).</summary>
        public const string InnoAppId = "{DD5D79D5-C88D-4B57-A5C9-1C586148DC98}";

        /// <summary>Inno Setup'ın kaldırma kaydındaki kurulum klasörü (kurulu değilse null).</summary>
        internal static string InnoInstallLocation()
        {
            try
            {
                using (var hk = Microsoft.Win32.RegistryKey.OpenBaseKey(Microsoft.Win32.RegistryHive.LocalMachine, Microsoft.Win32.RegistryView.Registry64))
                using (var k = hk.OpenSubKey(@"SOFTWARE\Microsoft\Windows\CurrentVersion\Uninstall\" + InnoAppId + "_is1"))
                    return k?.GetValue("InstallLocation") as string;
            }
            catch (Exception ex) { App.Ignored("Kurulum kaydı okunamadı", ex); return null; }
        }

        /// <summary>Verilen klasör kurulum paketiyle kurulmuş ve korumalı mı?</summary>
        internal static bool IsInnoInstall(string dir, string installLocation, bool protectedDir)
        {
            if (string.IsNullOrEmpty(installLocation) || string.IsNullOrEmpty(dir) || !protectedDir) return false;
            try
            {
                return string.Equals(Path.GetFullPath(installLocation).TrimEnd('\\'), Path.GetFullPath(dir).TrimEnd('\\'), StringComparison.OrdinalIgnoreCase);
            }
            catch { return false; }
        }

        /// <summary>Bu kopya kurulum paketiyle mi kuruldu? (Öyleyse güncelleme de kurulum paketiyle yapılır.)</summary>
        public static bool InstalledMode => IsInnoInstall(App.BaseDir, InnoInstallLocation(), InstallGuard.IsProtected);

        /// <summary>Bu sürüm için hangi güncelleme yolu kullanılacak: kurulumlu sürümde Setup.exe (varsa), aksi hâlde zip.</summary>
        public static bool UseSetup(UpdateInfo i, bool installedMode) =>
            installedMode && i != null && i.SetupUrl.Length > 0 && i.SetupSha256.Length == 64;

        /// <summary>
        /// Kurulum paketini veri\guncelleme klasörüne indirir ve doğrular (SHA-256, ürün adı ve sürüm). Kurulumlu sürümde
        /// bu klasöre yalnızca yöneticiler yazabilir.
        /// </summary>
        public static Prepared DownloadSetup(UpdateInfo i, Action<long, long> progress, CancellationToken ct)
        {
            if (!TrustedUrl(i.SetupUrl)) throw new InvalidOperationException("Kurulum paketinin adresi GitHub dışında: " + i.SetupUrl);
            if (i.SetupSha256.Length != 64) throw new InvalidOperationException("Kurulum paketi için doğrulama özeti (SHA-256) yok; güvenlik nedeniyle güncelleme yapılmadı.");
            Directory.CreateDirectory(UpdateRoot);
            string path = Path.Combine(UpdateRoot, Path.GetFileName(i.SetupName));
            try
            {
                using (var fs = new FileStream(path, FileMode.Create, FileAccess.Write, FileShare.None))
                    DownloadTo(i.SetupUrl, fs, i.SetupSha256, i.SetupSize, progress, ct);
                var fv = FileVersionInfo.GetVersionInfo(path);
                if ((fv.ProductName ?? "").Trim() != "DPI Kalkan" || !Version.TryParse((fv.FileVersion ?? "").Trim(), out var sv) || Norm(sv) != i.Version)
                    throw new InvalidDataException("Kurulum paketinin ürün/sürüm bilgisi beklenenle uyuşmuyor (" + (fv.ProductName ?? "").Trim() + " " + (fv.FileVersion ?? "").Trim() + ").");
            }
            catch
            {
                try { File.Delete(path); } catch (Exception ex) { App.Ignored("İndirilen kurulum paketi silinemedi", ex); }
                throw;
            }
            App.Log("Kurulum paketi indirildi ve doğrulandı: " + i.SetupName + " (SHA-256 " + i.SetupSha256.Substring(0, 16) + "…)", LogLevel.Success);
            return new Prepared { SetupPath = path, ExeSha256 = i.SetupSha256 };
        }

        /// <summary>Kurulum paketini sessiz yükseltme olarak başlatır; ardından bu uygulama kapanmalıdır.</summary>
        public static void StartSetup(Prepared prep)
        {
            using (var hold = new FileStream(prep.SetupPath, FileMode.Open, FileAccess.Read, FileShare.Read))
            {
                if (Integrity.Sha256(hold) != prep.ExeSha256)
                    throw new InvalidDataException("Kurulum paketi indirildikten sonra değişmiş; güvenlik nedeniyle kurulum iptal edildi.");
                if (!CheckPublisher(prep.SetupPath, out string why))
                    throw new InvalidDataException("Kurulum paketinin yayıncısı doğrulanamadı: " + why);
                string log = Path.Combine(App.DataDir, "kurulum.log");
                // /DKUPDATED ve /DKRESUME kurulum betiğinde okunur: yükseltmeden sonra uygulama "güncellendi" bilgisiyle yeniden açılır
                var psi = new ProcessStartInfo(prep.SetupPath,
                    "/VERYSILENT /SUPPRESSMSGBOXES /NORESTART /SP- /NOCANCEL /LOG=\"" + log + "\" /DKUPDATED=" + App.Version +
                    " /DKRESUME=" + (App.Engine != null && App.Engine.IsActive ? "1" : "0"))
                { UseShellExecute = false, WorkingDirectory = UpdateRoot };
                Process.Start(psi);
            }
            App.Log("Kurulum paketi sessiz yükseltme için başlatıldı; uygulama yeniden açılacak.", LogLevel.Info);
        }

        /// <summary>Zip girdilerini klasör dışına çıkmadan açar; her dosyanın SHA-256 özetini döndürür.</summary>
        static Dictionary<string, string> ExtractSafe(ZipArchive za, string dir)
        {
            string full = Path.GetFullPath(dir).TrimEnd('\\') + "\\";
            Directory.CreateDirectory(full);
            var hashes = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
            long totalBytes = 0;
            foreach (var e in za.Entries)
            {
                string name = e.FullName.Replace('/', '\\');
                string target = Path.GetFullPath(Path.Combine(full, name));
                if (!target.StartsWith(full, StringComparison.OrdinalIgnoreCase))
                    throw new InvalidDataException("Pakette klasör dışına çıkan bir girdi var: " + e.FullName);
                if (name.EndsWith("\\")) { Directory.CreateDirectory(target); continue; }
                totalBytes += e.Length;
                if (totalBytes > 300L * 1024 * 1024) throw new InvalidDataException("Paketin açılmış boyutu beklenenden çok büyük.");
                Directory.CreateDirectory(Path.GetDirectoryName(target));
                using (var es = e.Open())
                using (var outF = new FileStream(target, FileMode.CreateNew, FileAccess.Write, FileShare.None))
                using (var sha = SHA256.Create())
                using (var cs = new CryptoStream(outF, sha, CryptoStreamMode.Write))
                {
                    es.CopyTo(cs);
                    cs.FlushFinalBlock();
                    hashes[target] = Integrity.Hex(sha.Hash);
                }
            }
            return hashes;
        }

        /// <summary>
        /// Yayıncı (Authenticode) denetimi için ayrılmış nokta. Kod imzalama sertifikası alındığında burada WinVerifyTrust ile
        /// imza doğrulanacak ve imzalayan sertifikanın konu adı/parmak izi beklenen değerle karşılaştırılacak; imzasız ya da
        /// başka yayıncılı paket reddedilecek. Sertifika olmadığı için şimdilik her zaman kabul eder.
        /// </summary>
        internal static bool CheckPublisher(string exePath, out string reason)
        {
            reason = "";
            return true;
        }

        /// <summary>Yeni sürümün exe'sini kurulum için başlatır; ardından bu uygulama kapanmalıdır.</summary>
        public static void StartApply(Prepared prep)
        {
            string dst = App.BaseDir.TrimEnd('\\');
            string exe = Path.Combine(prep.Root, "DPIKalkan.exe");
            // exe yazmaya kapalı açık tutulur: özet doğrulaması ile başlatma arasında değiştirilemez
            using (var hold = new FileStream(exe, FileMode.Open, FileAccess.Read, FileShare.Read))
            {
                if (Integrity.Sha256(hold) != prep.ExeSha256)
                    throw new InvalidDataException("Güncelleme dosyası açıldıktan sonra değişmiş; güvenlik nedeniyle kurulum iptal edildi.");
                if (!CheckPublisher(exe, out string why))
                    throw new InvalidDataException("Güncellemenin yayıncısı doğrulanamadı: " + why);
                var psi = new ProcessStartInfo(exe,
                    "--apply-update \"" + prep.Root.TrimEnd('\\') + "\" \"" + dst + "\" " + Process.GetCurrentProcess().Id + " " + App.Version + " " + (App.Engine.IsActive ? "1" : "0"))
                { UseShellExecute = false, WorkingDirectory = prep.Root };
                Process.Start(psi);
            }
            App.Log("Güncelleme kurulumu başlatıldı; uygulama yeniden açılacak.", LogLevel.Info);
        }

        /// <summary>
        /// --apply-update yollarını doğrular: hedef bir DPI Kalkan klasörü olmalı; kaynak hedefin güncelleme klasöründe
        /// (ya da 2.3.0'ın kullandığı eski geçici klasörde) olmalı ve güncelleyici kaynak klasörden çalışmalı.
        /// </summary>
        internal static bool ValidateApplyArgs(string src, string dst, string selfExe, out string error)
        {
            error = "";
            try
            {
                string N(string p) => Path.GetFullPath(p).TrimEnd('\\');
                src = N(src);
                dst = N(dst);
                string dstExe = Path.Combine(dst, "DPIKalkan.exe");
                if (!File.Exists(dstExe)) { error = "Hedef klasörde DPI Kalkan yok: " + dst; return false; }
                if (FileVersionInfo.GetVersionInfo(dstExe).ProductName != "DPI Kalkan") { error = "Hedefteki program DPI Kalkan değil."; return false; }
                var roots = new[] { N(Path.Combine(dst, "veri", "guncelleme")) + "\\", N(LegacyRoot) + "\\" };
                if (!roots.Any(r => (src + "\\").StartsWith(r, StringComparison.OrdinalIgnoreCase) && src.Length + 1 > r.Length))
                { error = "Kaynak klasör güncelleme klasöründe değil: " + src; return false; }
                if (!N(selfExe).StartsWith(src + "\\", StringComparison.OrdinalIgnoreCase))
                { error = "Güncelleyici kaynak klasörden çalışmıyor."; return false; }
                return true;
            }
            catch (Exception ex) { error = ex.Message; return false; }
        }

        // ================================================================ kurulum (yeni exe içinde çalışır)

        static void CopyDir(string src, string dst, Func<string, bool> skipTop, Action<string> log)
        {
            Directory.CreateDirectory(dst);
            foreach (var d in Directory.GetDirectories(src))
            {
                string name = Path.GetFileName(d);
                if (skipTop != null && skipTop(name)) continue;
                CopyDir(d, Path.Combine(dst, name), null, log);
            }
            foreach (var f in Directory.GetFiles(src))
            {
                string name = Path.GetFileName(f);
                if (skipTop != null && skipTop(name)) continue;
                string target = Path.Combine(dst, name);
                for (int attempt = 1; ; attempt++)
                {
                    try { File.Copy(f, target, true); break; }
                    catch (IOException) when (attempt < 10) { Thread.Sleep(500); }
                    catch (UnauthorizedAccessException) when (attempt < 10) { Thread.Sleep(500); }
                }
            }
        }

        /// <summary>
        /// --apply-update kaynak hedef pid eskiSürüm: eski uygulamanın kapanmasını bekler, hizmeti durdurur,
        /// hedefi yedekler, dosyaları kopyalar (veri klasörü hariç) ve yeni sürümü başlatır.
        /// </summary>
        public static int Apply(string src, string dst, int pid, string oldVer, bool resume)
        {
            string self = Process.GetCurrentProcess().MainModule.FileName;
            if (!ValidateApplyArgs(src, dst, self, out string err))
            {
                // hiçbir dosyaya dokunulmadı; geçerli bir DPI Kalkan klasörüyse eski sürüm yeniden açılır
                try
                {
                    string d = Path.GetFullPath(dst);
                    if (File.Exists(Path.Combine(d, "veri", "ayarlar.xml")) || Directory.Exists(Path.Combine(d, "veri")))
                        File.AppendAllText(Path.Combine(d, "veri", "guncelleme.log"), DateTime.Now.ToString("yyyy-MM-dd HH:mm:ss") + "  REDDEDİLDİ: " + err + Environment.NewLine, Encoding.UTF8);
                    string old = Path.Combine(d, "DPIKalkan.exe");
                    if (File.Exists(old) && FileVersionInfo.GetVersionInfo(old).ProductName == "DPI Kalkan")
                        Process.Start(new ProcessStartInfo(old, "--tray --update-failed" + (resume ? " --resume" : "")) { UseShellExecute = false, WorkingDirectory = d });
                }
                catch (Exception ign) { try { File.AppendAllText(Path.Combine(Path.GetFullPath(dst), "veri", "guncelleme.log"), "Eski sürüm yeniden açılamadı: " + ign.Message + Environment.NewLine, Encoding.UTF8); } catch { /* günlük de yazılamıyorsa yapılacak bir şey yok */ } }
                return 2;
            }
            return Apply(src, dst, pid, oldVer, resume, true);
        }

        /// <param name="live">false: yalnızca dosya işlemleri (test için) – hizmete dokunmaz, uygulamayı başlatmaz.</param>
        internal static int Apply(string src, string dst, int pid, string oldVer, bool resume, bool live)
        {
            string dataDir = Path.Combine(dst, "veri");
            string log = Path.Combine(dataDir, "guncelleme.log");
            void L(string m)
            {
                try { Directory.CreateDirectory(dataDir); File.AppendAllText(log, DateTime.Now.ToString("yyyy-MM-dd HH:mm:ss") + "  " + m + Environment.NewLine, Encoding.UTF8); }
                catch { /* güncelleme günlüğü yazılamıyorsa kuruluma devam edilir */ }
            }
            string bak = Path.Combine(dataDir, "onceki-surum");
            string dstExe = Path.Combine(dst, "DPIKalkan.exe");
            bool backedUp = false;
            try
            {
                L("Güncelleme başladı: " + oldVer + " → " + Current + "  (" + src + " → " + dst + ")");
                if (string.Equals(Path.GetFullPath(src).TrimEnd('\\'), Path.GetFullPath(dst).TrimEnd('\\'), StringComparison.OrdinalIgnoreCase))
                    throw new InvalidOperationException("Kaynak ve hedef aynı klasör.");

                // 1) eski uygulamanın kapanmasını bekle
                try
                {
                    using (var p = Process.GetProcessById(pid))
                    {
                        // işlem numarası başka bir programa geçmişse ona dokunma
                        if (p.ProcessName.Equals("DPIKalkan", StringComparison.OrdinalIgnoreCase) &&
                            !p.WaitForExit(20000))
                        {
                            L("Eski uygulama kapanmadı; sonlandırılıyor.");
                            p.Kill();
                            p.WaitForExit(5000);
                            CrashMarker.Clear(dataDir);   // bilerek kapatıldı: "beklenmedik kapanış" sayılmasın
                        }
                    }
                }
                catch (ArgumentException) { /* zaten kapanmış */ }

                // 2) hizmeti durdur ve bu klasördeki goodbyedpi işlemlerini kapat
                if (live) try
                {
                    using (var sc = new ServiceController(Engine.ServiceName))
                    {
                        if (sc.Status != ServiceControllerStatus.Stopped)
                        {
                            sc.Stop();
                            sc.WaitForStatus(ServiceControllerStatus.Stopped, TimeSpan.FromSeconds(15));
                            L("Arka plan hizmeti durduruldu.");
                        }
                    }
                }
                catch (InvalidOperationException) { }
                if (live) foreach (var p in Process.GetProcessesByName("goodbyedpi"))
                    using (p)
                    {
                        try
                        {
                            if (p.MainModule.FileName.StartsWith(dst.TrimEnd(Path.DirectorySeparatorChar) + Path.DirectorySeparatorChar, StringComparison.OrdinalIgnoreCase)) { p.Kill(); p.WaitForExit(3000); L("goodbyedpi kapatıldı (PID " + p.Id + ")."); }
                        }
                        catch (Exception ign) { L("goodbyedpi kapatılamadı: " + ign.Message); }
                    }
                Thread.Sleep(800);

                // 3) yedek (veri hariç)
                if (File.Exists(dstExe))
                {
                    if (Directory.Exists(bak)) Directory.Delete(bak, true);
                    CopyDir(dst, bak, n => n.Equals("veri", StringComparison.OrdinalIgnoreCase), L);
                    backedUp = true;
                    L("Önceki sürüm yedeklendi: " + bak);
                }

                // 4) yeni dosyaları kopyala (veri hariç)
                CopyDir(src, dst, n => n.Equals("veri", StringComparison.OrdinalIgnoreCase), L);
                L("Dosyalar kopyalandı.");

                // 5) yeni sürümü başlat
                if (live) Process.Start(new ProcessStartInfo(dstExe, "--tray --updated " + oldVer + (resume ? " --resume" : "")) { UseShellExecute = false, WorkingDirectory = dst });
                L("Yeni sürüm başlatıldı. Güncelleme tamamlandı.");
                return 0;
            }
            catch (Exception ex)
            {
                L("HATA: " + ex.Message);
                if (backedUp)
                {
                    try { CopyDir(bak, dst, null, L); L("Önceki sürüme geri dönüldü."); }
                    catch (Exception ex2) { L("Geri dönülemedi: " + ex2.Message); }
                }
                try { if (live && File.Exists(dstExe)) Process.Start(new ProcessStartInfo(dstExe, "--tray --update-failed" + (resume ? " --resume" : "")) { UseShellExecute = false, WorkingDirectory = dst }); }
                catch (Exception ign) { L("Eski sürüm yeniden açılamadı: " + ign.Message); }
                return 1;
            }
        }

        /// <summary>Başarılı güncellemeden sonra geçici dosyaları temizler.</summary>
        public static void Cleanup()
        {
            foreach (var d in new[] { UpdateRoot, LegacyRoot })
                try { if (Directory.Exists(d)) Directory.Delete(d, true); } catch (Exception ign) { App.Ignored("Güncelleme klasörü silinemedi", ign); }
        }
    }
}
