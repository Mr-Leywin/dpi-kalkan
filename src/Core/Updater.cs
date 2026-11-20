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

        public static string TempRoot => Path.Combine(Path.GetTempPath(), "DPIKalkan-guncelleme");

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
                using (var sr = new StreamReader(resp.GetResponseStream(), Encoding.UTF8))
                    json = sr.ReadToEnd();
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
                    if (!Regex.IsMatch(name, @"^DPI-Kalkan-[\d.]+\.zip$", RegexOptions.IgnoreCase)) continue;
                    info.AssetName = name;
                    info.AssetUrl = S(asset, "browser_download_url");
                    long.TryParse(S(asset, "size"), out info.Size);
                    string digest = S(asset, "digest");
                    if (digest.StartsWith("sha256:", StringComparison.OrdinalIgnoreCase)) info.Sha256 = digest.Substring(7).ToLowerInvariant();
                    break;
                }
            }
            if (info.Sha256.Length == 0)
            {
                // Sürüm notlarındaki "SHA-256 … `özet`" satırı
                var m = Regex.Match(info.Notes, @"SHA-?256[^`\n]*`([0-9a-fA-F]{64})`");
                if (m.Success) info.Sha256 = m.Groups[1].Value.ToLowerInvariant();
            }
            return info;
        }

        public static bool IsNewer(UpdateInfo i) => i != null && i.Version > Current;

        /// <summary>Kullanıcı bu sürümü atlamadıysa ve otomatik denetim açıksa günde bir denetler.</summary>
        public static bool DueForCheck =>
            App.Settings.AutoUpdate && (!App.Settings.LastUpdateCheck.HasValue || (DateTime.Now - App.Settings.LastUpdateCheck.Value).TotalHours >= 20);

        public static bool Skipped(UpdateInfo i) => i != null && App.Settings.SkipVersion == i.Version.ToString();

        static bool TrustedUrl(string url) =>
            Uri.TryCreate(url, UriKind.Absolute, out var u) && u.Scheme == "https" &&
            (u.Host == "github.com" || u.Host.EndsWith(".githubusercontent.com"));

        /// <summary>Paketi indirir ve SHA-256 özetini doğrular. Doğrulanamazsa dosyayı siler ve hata verir.</summary>
        public static string Download(UpdateInfo i, Action<long, long> progress, CancellationToken ct)
        {
            if (!TrustedUrl(i.AssetUrl)) throw new InvalidOperationException("Güncelleme adresi GitHub dışında: " + i.AssetUrl);
            if (i.Sha256.Length != 64) throw new InvalidOperationException("Bu sürüm için doğrulama özeti (SHA-256) yok; güvenlik nedeniyle güncelleme yapılmadı.");
            Directory.CreateDirectory(TempRoot);
            string file = Path.Combine(TempRoot, Path.GetFileName(i.AssetName));
            var r = Req(i.AssetUrl, 30000);
            using (var resp = r.GetResponse())
            using (var src = resp.GetResponseStream())
            using (var dst = File.Create(file))
            using (var sha = SHA256.Create())
            {
                long total = resp.ContentLength > 0 ? resp.ContentLength : i.Size, got = 0;
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
                string hash = BitConverter.ToString(sha.Hash).Replace("-", "").ToLowerInvariant();
                if (hash != i.Sha256)
                {
                    dst.Close();
                    try { File.Delete(file); } catch { }
                    throw new InvalidDataException("İndirilen paketin özeti uyuşmuyor (beklenen " + i.Sha256.Substring(0, 12) + "…, gelen " + hash.Substring(0, 12) + "…). Güncelleme iptal edildi.");
                }
            }
            App.Log("Güncelleme indirildi ve doğrulandı: " + i.AssetName + " (SHA-256 " + i.Sha256.Substring(0, 16) + "…)", LogLevel.Success);
            return file;
        }

        /// <summary>Paketi açar ve içindeki uygulama klasörünü döndürür.</summary>
        public static string Extract(string zip, Version v)
        {
            string dir = Path.Combine(TempRoot, "v" + v);
            if (Directory.Exists(dir)) Directory.Delete(dir, true);
            ZipFile.ExtractToDirectory(zip, dir);   // .NET 4.6.1+: klasör dışına çıkan girdileri reddeder
            string exe = Directory.GetFiles(dir, "DPIKalkan.exe", SearchOption.AllDirectories).OrderBy(f => f.Length).FirstOrDefault();
            if (exe == null) throw new InvalidDataException("Pakette DPIKalkan.exe yok.");
            string root = Path.GetDirectoryName(exe);
            foreach (var req in new[] { @"bin\x86_64\goodbyedpi.exe", @"bin\x86_64\WinDivert.dll", @"bin\x86\goodbyedpi.exe" })
                if (!File.Exists(Path.Combine(root, req))) throw new InvalidDataException("Paket eksik: " + req);
            var fv = FileVersionInfo.GetVersionInfo(exe);
            if (!Version.TryParse(fv.FileVersion, out var exeVer) || Norm(exeVer) != v)
                throw new InvalidDataException("Paketteki uygulama sürümü (" + fv.FileVersion + ") beklenen sürümle (" + v + ") uyuşmuyor.");
            return root;
        }

        /// <summary>Yeni sürümün exe'sini kurulum için başlatır; ardından bu uygulama kapanmalıdır.</summary>
        public static void StartApply(string newRoot)
        {
            string dst = App.BaseDir.TrimEnd('\\');
            var psi = new ProcessStartInfo(Path.Combine(newRoot, "DPIKalkan.exe"),
                "--apply-update \"" + newRoot.TrimEnd('\\') + "\" \"" + dst + "\" " + Process.GetCurrentProcess().Id + " " + App.Version + " " + (App.Engine.IsActive ? "1" : "0"))
            { UseShellExecute = false, WorkingDirectory = newRoot };
            Process.Start(psi);
            App.Log("Güncelleme kurulumu başlatıldı; uygulama yeniden açılacak.", LogLevel.Info);
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
        public static int Apply(string src, string dst, int pid, string oldVer, bool resume) => Apply(src, dst, pid, oldVer, resume, true);

        /// <param name="live">false: yalnızca dosya işlemleri (test için) – hizmete dokunmaz, uygulamayı başlatmaz.</param>
        internal static int Apply(string src, string dst, int pid, string oldVer, bool resume, bool live)
        {
            string dataDir = Path.Combine(dst, "veri");
            string log = Path.Combine(dataDir, "guncelleme.log");
            void L(string m)
            {
                try { Directory.CreateDirectory(dataDir); File.AppendAllText(log, DateTime.Now.ToString("yyyy-MM-dd HH:mm:ss") + "  " + m + Environment.NewLine, Encoding.UTF8); }
                catch { }
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
                            !p.WaitForExit(20000)) { L("Eski uygulama kapanmadı; sonlandırılıyor."); p.Kill(); p.WaitForExit(5000); }
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
                        catch { }
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
                catch { }
                return 1;
            }
        }

        /// <summary>Başarılı güncellemeden sonra geçici dosyaları temizler.</summary>
        public static void Cleanup()
        {
            try { if (Directory.Exists(TempRoot)) Directory.Delete(TempRoot, true); } catch { }
        }
    }
}
