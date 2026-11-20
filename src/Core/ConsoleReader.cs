using System;
using System.Runtime.InteropServices;
using System.Text;

namespace DPIKalkan
{
    /// <summary>
    /// goodbyedpi.exe çıktısını boruya yönlendirince tamponlanıyor (yalnızca kapanınca geliyor).
    /// Bu yüzden işlemi pencere-siz bir konsolla başlatıp konsol ekranını doğrudan okuyoruz.
    /// </summary>
    static class ConsoleReader
    {
        [DllImport("kernel32.dll", SetLastError = true)] static extern bool AttachConsole(uint pid);
        [DllImport("kernel32.dll")] static extern bool FreeConsole();
        [DllImport("kernel32.dll")] static extern bool CloseHandle(IntPtr h);

        [DllImport("kernel32.dll", CharSet = CharSet.Unicode, SetLastError = true)]
        static extern IntPtr CreateFile(string name, uint access, uint share, IntPtr sa, uint disposition, uint flags, IntPtr template);

        [StructLayout(LayoutKind.Sequential)]
        struct COORD { public short X, Y; }

        [StructLayout(LayoutKind.Sequential)]
        struct SMALL_RECT { public short Left, Top, Right, Bottom; }

        [StructLayout(LayoutKind.Sequential)]
        struct CONSOLE_SCREEN_BUFFER_INFO
        {
            public COORD dwSize;
            public COORD dwCursorPosition;
            public short wAttributes;
            public SMALL_RECT srWindow;
            public COORD dwMaximumWindowSize;
        }

        [DllImport("kernel32.dll")]
        static extern bool GetConsoleScreenBufferInfo(IntPtr h, out CONSOLE_SCREEN_BUFFER_INFO info);

        [DllImport("kernel32.dll", CharSet = CharSet.Unicode, EntryPoint = "ReadConsoleOutputCharacterW")]
        static extern bool ReadConsoleOutputCharacter(IntPtr h, [Out] char[] buffer, uint length, COORD start, out uint read);

        delegate bool CtrlHandler(uint ctrlType);
        [DllImport("kernel32.dll")] static extern bool SetConsoleCtrlHandler(CtrlHandler handler, bool add);

        static readonly object Gate = new object();
        static readonly CtrlHandler Ignore = _ => true;   // konsol olayları bizim uygulamamızı kapatmasın
        static bool handlerSet;

        /// <summary>İşlemin konsol ekranındaki metni döndürür, okunamazsa null.</summary>
        public static string Read(int pid)
        {
            lock (Gate)
            {
                if (!AttachConsole((uint)pid)) return null;
                try
                {
                    if (!handlerSet) { SetConsoleCtrlHandler(Ignore, true); handlerSet = true; }
                    IntPtr h = CreateFile("CONOUT$", 0xC0000000, 3, IntPtr.Zero, 3, 0, IntPtr.Zero);
                    if (h == IntPtr.Zero || h == new IntPtr(-1)) return null;
                    try
                    {
                        if (!GetConsoleScreenBufferInfo(h, out var info)) return null;
                        int width = Math.Max(1, (int)info.dwSize.X);
                        int lines = Math.Min(info.dwCursorPosition.Y + 1, 2000);
                        int len = width * lines;
                        var buf = new char[len];
                        if (!ReadConsoleOutputCharacter(h, buf, (uint)len, new COORD(), out uint read)) return null;
                        var sb = new StringBuilder();
                        for (int i = 0; i < lines; i++)
                        {
                            int start = i * width;
                            if (start >= read) break;
                            int n = Math.Min(width, (int)read - start);
                            sb.Append(new string(buf, start, n).TrimEnd()).Append('\n');
                        }
                        return sb.ToString();
                    }
                    finally { CloseHandle(h); }
                }
                catch { return null; }
                finally { FreeConsole(); }
            }
        }
    }
}
