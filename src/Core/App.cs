using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Reflection;
using System.Security.Principal;
using System.Threading;
using System.Windows.Forms;

namespace DPIKalkan
{
    public enum LogLevel { Debug, Info, Success, Warn, Error, Output }

    public class LogEntry
    {
        public DateTime Time;
        public LogLevel Level;
        public string Text;
    }

    /// <summary>Uygulama genelindeki paylaşılan durum.</summary>
    public static class App
    {
        public const string Name = "DPI Kalkan";
        public const string RepoUrl = "https://github.com/Mr-Leywin/dpi-kalkan";
        public static readonly string Version = Assembly.GetExecutingAssembly().GetName().Version.ToString(3);

        public static string BaseDir = AppDomain.CurrentDomain.BaseDirectory;
        public static string DataDir = Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "veri");
        public static string LogFile => Path.Combine(DataDir, "gunluk.log");

        public static AppSettings Settings;
        public static Engine Engine;
        public static readonly ProblemCenter Problems = new ProblemCenter();
        public static bool Preview;
        public static SynchronizationContext UI;

        public static readonly List<LogEntry> LogBuffer = new List<LogEntry>();
        static readonly object LogLock = new object();

        public static event Action<LogEntry> LogAdded;
        /// <summary>Motor durumu, ayarlar veya test sonuçları değiştiğinde.</summary>
        public static event Action StatusChanged;

        public static List<HostResult> LastTest;
        public static DateTime? LastTestTime;
        /// <summary>Son testteki sistem DNS doğrulaması: null bilinmiyor, true doğru, false sahte/engelli.</summary>
        public static bool? LastDnsOk;

        /// <summary>Uygulama zip içinden ya da Temp klasöründen mi çalışıyor?</summary>
        public static bool RunningFromTemp
        {
            get
            {
                try
                {
                    const char S = '\\';
                    string b = Path.GetFullPath(BaseDir).TrimEnd(S) + S;
                    string t = Path.GetFullPath(Path.GetTempPath()).TrimEnd(S) + S;
                    return b.StartsWith(t, StringComparison.OrdinalIgnoreCase) ||
                           b.IndexOf(S + "Temp" + S, StringComparison.OrdinalIgnoreCase) >= 0 ||
                           b.IndexOf(".zip" + S, StringComparison.OrdinalIgnoreCase) >= 0;
                }
                catch { return false; }
            }
        }

        public static void Init()
        {
            try { Directory.CreateDirectory(DataDir); } catch { }
            try
            {
                var fi = new FileInfo(LogFile);
                if (fi.Exists && fi.Length > 2 * 1024 * 1024)
                {
                    string old = LogFile + ".eski";
                    if (File.Exists(old)) File.Delete(old);
                    File.Move(LogFile, old);
                }
            }
            catch { }
            Settings = SettingsStore.Load();
            History.Load();
            Engine = new Engine();
        }

        public static bool IsAdmin()
        {
            try
            {
                using (var id = WindowsIdentity.GetCurrent())
                    return new WindowsPrincipal(id).IsInRole(WindowsBuiltInRole.Administrator);
            }
            catch { return false; }
        }

        public static void Log(string text, LogLevel level = LogLevel.Info)
        {
            var e = new LogEntry { Time = DateTime.Now, Level = level, Text = text };
            lock (LogLock)
            {
                LogBuffer.Add(e);
                if (LogBuffer.Count > 4000) LogBuffer.RemoveRange(0, 1000);
                if (!Preview)
                {
                    try
                    {
                        File.AppendAllText(LogFile,
                            e.Time.ToString("yyyy-MM-dd HH:mm:ss") + " [" + level + "] " + text + Environment.NewLine);
                    }
                    catch { }
                }
            }
            Post(() => LogAdded?.Invoke(e));
        }

        public static LogEntry[] LogSnapshot() { lock (LogLock) return LogBuffer.ToArray(); }
        public static void ClearLog() { lock (LogLock) LogBuffer.Clear(); }

        public static void Post(Action a)
        {
            var ui = UI;
            if (ui == null) { try { a(); } catch { } return; }
            ui.Post(_ => { try { a(); } catch (Exception ex) { System.Diagnostics.Debug.WriteLine(ex); } }, null);
        }

        public static void RaiseStatus() => Post(() => StatusChanged?.Invoke());

        public static IEnumerable<Profile> AllProfiles => Profiles.BuiltIns.Concat(Settings.CustomProfiles);

        public static Profile SelectedProfile =>
            AllProfiles.FirstOrDefault(p => p.Id == Settings.SelectedProfileId) ?? Profiles.BuiltIns[0];

        public static Profile FindProfile(string id) => AllProfiles.FirstOrDefault(p => p.Id == id);

        public static void Save() => SettingsStore.Save(Settings);

        public static string BlacklistPath => Path.Combine(DataDir, "hedef-siteler.txt");

        public static string WriteBlacklist()
        {
            Directory.CreateDirectory(DataDir);
            File.WriteAllLines(BlacklistPath, Settings.TargetDomains);
            return BlacklistPath;
        }

        public static void Notify(string title, string text, ToolTipIcon icon = ToolTipIcon.Info)
        {
            Post(() => MainForm.Instance?.ShowBalloon(title, text, icon));
        }
    }
}
