using System;
using System.Diagnostics;
using System.IO;
using System.Text;

namespace DPIKalkan
{
    /// <summary>
    /// Beklenmedik kapanış işareti. Uygulama açılınca veri\calisiyor.isaret yazılır, düzgün kapanışta silinir.
    /// Sonraki açılışta dosya hâlâ duruyorsa önceki çalışma çökmüş, zorla kapatılmış ya da elektrik kesilmiştir:
    /// kullanıcıya "Son kapanış beklenmedikti" bildirimi (DK-106) ve günlüğe erişim sunulur.
    /// Kendi araçlarımız (güncelleme, kurulum, kaldırma) uygulamayı kapattığında işareti siler; yanlış uyarı çıkmaz.
    /// </summary>
    public static class CrashMarker
    {
        const string MarkerName = "calisiyor.isaret", CrashName = "son-cokme.txt";

        static string Marker(string dataDir) => Path.Combine(dataDir, MarkerName);
        public static string CrashFile => Path.Combine(App.DataDir, CrashName);

        /// <summary>Önceki çalışma beklenmedik şekilde sona erdiyse kısa açıklama (yoksa null).</summary>
        public static string PreviousUnexpected { get; private set; }

        public static void Begin()
        {
            if (App.Preview) return;
            PreviousUnexpected = null;
            try
            {
                string m = Marker(App.DataDir);
                if (File.Exists(m))
                {
                    string info = File.ReadAllText(m, Encoding.UTF8).Trim();
                    string crash = null;
                    if (File.Exists(CrashFile) && File.GetLastWriteTimeUtc(CrashFile) >= File.GetLastWriteTimeUtc(m))
                        crash = FirstLine(File.ReadAllText(CrashFile, Encoding.UTF8));
                    PreviousUnexpected = (info.Length > 0 ? info : "önceki çalışma") + (crash != null ? " · son hata: " + crash : "");
                    App.Log("[DK-106] Son kapanış beklenmedikti (" + PreviousUnexpected + ").", LogLevel.Warn);
                }
                Directory.CreateDirectory(App.DataDir);
                File.WriteAllText(m, "sürüm " + App.Version + ", başlangıç " + DateTime.Now.ToString("dd.MM.yyyy HH:mm") +
                                     ", işlem " + Process.GetCurrentProcess().Id, Encoding.UTF8);
            }
            catch (Exception ex) { App.Ignored("Çalışma işareti yazılamadı", ex); }
        }

        /// <summary>Düzgün kapanış: işareti siler.</summary>
        public static void End()
        {
            if (App.Preview) return;
            Clear(App.DataDir);
        }

        /// <summary>Başka bir klasördeki (ör. güncellenen kurulum) işareti siler; uygulamayı bilerek kapatan araçlar çağırır.</summary>
        public static void Clear(string dataDir)
        {
            try { string m = Marker(dataDir); if (File.Exists(m)) File.Delete(m); }
            catch (Exception ex) { App.Ignored("Çalışma işareti silinemedi", ex); }
        }

        /// <summary>Yakalanmamış hatanın ayrıntısını veri\son-cokme.txt dosyasına yazar.</summary>
        public static void Record(Exception ex, bool fatal)
        {
            if (App.Preview || ex == null) return;
            try
            {
                File.WriteAllText(CrashFile, (fatal ? "Kritik hata (uygulama kapandı): " : "Beklenmeyen hata: ") + ex.GetType().Name + ": " + ex.Message +
                                             Environment.NewLine + DateTime.Now.ToString("dd.MM.yyyy HH:mm:ss") + " · sürüm " + App.Version +
                                             Environment.NewLine + Environment.NewLine + ex, Encoding.UTF8);
            }
            catch { /* çökme sırasında yazılamazsa yapılacak bir şey yok */ }
        }

        static string FirstLine(string s)
        {
            string l = (s ?? "").Split('\n')[0].Trim();
            return l.Length > 160 ? l.Substring(0, 160) + "…" : l;
        }
    }
}
