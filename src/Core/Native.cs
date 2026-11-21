using System;
using System.Runtime.InteropServices;

namespace DPIKalkan
{
    static class Native
    {
        [DllImport("kernel32.dll", SetLastError = true)]
        static extern bool SetDefaultDllDirectories(uint directoryFlags);

        [DllImport("kernel32.dll", CharSet = CharSet.Unicode, SetLastError = true)]
        static extern bool SetDllDirectory(string path);

        /// <summary>
        /// DLL arama sırasını kısıtlar: geçerli çalışma klasörü ve PATH klasörleri aranmaz, yalnızca uygulama klasörü
        /// (kurulumlu sürümde yönetici korumalı) ve System32 aranır. Main'in ilk satırında çağrılır.
        /// </summary>
        public static void HardenDllSearch()
        {
            const uint LOAD_LIBRARY_SEARCH_APPLICATION_DIR = 0x200, LOAD_LIBRARY_SEARCH_SYSTEM32 = 0x800;
            try { if (SetDefaultDllDirectories(LOAD_LIBRARY_SEARCH_APPLICATION_DIR | LOAD_LIBRARY_SEARCH_SYSTEM32)) return; } catch { }
            try { SetDllDirectory(""); } catch { }   // eski sistemler: en azından geçerli klasörü çıkar
        }

        [DllImport("dnsapi.dll", EntryPoint = "DnsFlushResolverCache")]
        static extern bool DnsFlushResolverCacheNative();

        public static bool FlushDns()
        {
            try { return DnsFlushResolverCacheNative(); } catch { return false; }
        }

        [DllImport("user32.dll")] public static extern bool ReleaseCapture();
        [DllImport("user32.dll")] public static extern IntPtr SendMessage(IntPtr hWnd, int msg, IntPtr wParam, IntPtr lParam);
        [DllImport("user32.dll")] public static extern bool SetForegroundWindow(IntPtr hWnd);
        [DllImport("user32.dll")] public static extern bool DestroyIcon(IntPtr hIcon);

        [DllImport("dwmapi.dll")]
        static extern int DwmSetWindowAttribute(IntPtr hwnd, int attr, ref int value, int size);

        public static void RoundCorners(IntPtr hwnd)
        {
            try { int v = 2; DwmSetWindowAttribute(hwnd, 33, ref v, 4); } catch { }
        }

        /// <summary>Başlık çubuğunu koyu/açık yapar (Windows 10 1809+).</summary>
        public static void DarkTitle(IntPtr hwnd, bool dark)
        {
            try
            {
                int v = dark ? 1 : 0;
                if (DwmSetWindowAttribute(hwnd, 20, ref v, 4) != 0) DwmSetWindowAttribute(hwnd, 19, ref v, 4);
            }
            catch { }
        }

        [DllImport("uxtheme.dll", CharSet = CharSet.Unicode)]
        static extern int SetWindowTheme(IntPtr hwnd, string app, string idList);

        /// <summary>Kaydırma çubuklarını ve standart denetimleri koyu temaya uydurur.</summary>
        public static void ThemeWindow(IntPtr hwnd, string name)
        {
            try { SetWindowTheme(hwnd, name, null); } catch { }
        }

        public static void DragWindow(IntPtr hwnd)
        {
            ReleaseCapture();
            SendMessage(hwnd, 0xA1, (IntPtr)2, IntPtr.Zero);
        }

        // ---- Job object: uygulama kapanır/çökerse goodbyedpi.exe de kapansın ----
        [StructLayout(LayoutKind.Sequential)]
        struct JOBOBJECT_BASIC_LIMIT_INFORMATION
        {
            public long PerProcessUserTimeLimit;
            public long PerJobUserTimeLimit;
            public uint LimitFlags;
            public UIntPtr MinimumWorkingSetSize;
            public UIntPtr MaximumWorkingSetSize;
            public uint ActiveProcessLimit;
            public UIntPtr Affinity;
            public uint PriorityClass;
            public uint SchedulingClass;
        }

        [StructLayout(LayoutKind.Sequential)]
        struct IO_COUNTERS
        {
            public ulong ReadOperationCount, WriteOperationCount, OtherOperationCount;
            public ulong ReadTransferCount, WriteTransferCount, OtherTransferCount;
        }

        [StructLayout(LayoutKind.Sequential)]
        struct JOBOBJECT_EXTENDED_LIMIT_INFORMATION
        {
            public JOBOBJECT_BASIC_LIMIT_INFORMATION BasicLimitInformation;
            public IO_COUNTERS IoInfo;
            public UIntPtr ProcessMemoryLimit;
            public UIntPtr JobMemoryLimit;
            public UIntPtr PeakProcessMemoryUsed;
            public UIntPtr PeakJobMemoryUsed;
        }

        [DllImport("kernel32.dll", CharSet = CharSet.Unicode)]
        static extern IntPtr CreateJobObject(IntPtr attrs, string name);

        [DllImport("kernel32.dll")]
        static extern bool SetInformationJobObject(IntPtr job, int infoClass, ref JOBOBJECT_EXTENDED_LIMIT_INFORMATION info, uint size);

        [DllImport("kernel32.dll", SetLastError = true)]
        static extern bool AssignProcessToJobObject(IntPtr job, IntPtr process);

        static IntPtr _job;

        public static void AttachToKillJob(IntPtr processHandle)
        {
            try
            {
                if (_job == IntPtr.Zero)
                {
                    _job = CreateJobObject(IntPtr.Zero, null);
                    var info = new JOBOBJECT_EXTENDED_LIMIT_INFORMATION();
                    info.BasicLimitInformation.LimitFlags = 0x2000; // KILL_ON_JOB_CLOSE
                    SetInformationJobObject(_job, 9, ref info, (uint)Marshal.SizeOf(typeof(JOBOBJECT_EXTENDED_LIMIT_INFORMATION)));
                }
                // KILL_ON_JOB_CLOSE: uygulama çökse ya da zorla kapatılsa bile GoodbyeDPI onunla birlikte kapanır, filtre takılı kalmaz
                if (_job == IntPtr.Zero || !AssignProcessToJobObject(_job, processHandle))
                    App.Log("GoodbyeDPI uygulamaya bağlanamadı (Win32 hata " + Marshal.GetLastWin32Error() + "); uygulama çökerse açık kalabilir.", LogLevel.Warn);
            }
            catch (Exception ign) { App.Log("GoodbyeDPI uygulamaya bağlanamadı (uygulama çökerse açık kalabilir): " + ign.Message, LogLevel.Warn); }
        }
    }
}
