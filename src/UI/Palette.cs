using System;
using System.Collections.Generic;
using System.Drawing;
using System.Globalization;
using System.Linq;
using System.Text;
using System.Windows.Forms;
using Microsoft.Win32;

namespace DPIKalkan
{
    /// <summary>Bir renk teması: kullanıcının değiştirebildiği 12 temel renk.</summary>
    sealed class Palette
    {
        public string Id, Name, Group = "";
        public Color Accent, Bg, Card, Border, Text, Muted, Sidebar, SidebarText, Green, Amber, Red, LogBg;

        public static readonly string[] Keys =
            { "Accent", "Bg", "Card", "Border", "Text", "Muted", "Sidebar", "SidebarText", "Green", "Amber", "Red", "LogBg" };

        public static readonly string[] Labels =
        {
            "Vurgu rengi (düğmeler, seçimler)", "Pencere arka planı", "Kart / panel", "Kenarlık", "Metin", "İkincil metin",
            "Kenar çubuğu", "Kenar çubuğu metni", "Başarı (yeşil)", "Uyarı (turuncu)", "Hata (kırmızı)", "Günlük (konsol) arka planı"
        };

        public Color Get(string key)
        {
            switch (key)
            {
                case "Accent": return Accent; case "Bg": return Bg; case "Card": return Card; case "Border": return Border;
                case "Text": return Text; case "Muted": return Muted; case "Sidebar": return Sidebar; case "SidebarText": return SidebarText;
                case "Green": return Green; case "Amber": return Amber; case "Red": return Red; case "LogBg": return LogBg;
            }
            return Color.Empty;
        }

        public void Set(string key, Color c)
        {
            switch (key)
            {
                case "Accent": Accent = c; break; case "Bg": Bg = c; break; case "Card": Card = c; break; case "Border": Border = c; break;
                case "Text": Text = c; break; case "Muted": Muted = c; break; case "Sidebar": Sidebar = c; break; case "SidebarText": SidebarText = c; break;
                case "Green": Green = c; break; case "Amber": Amber = c; break; case "Red": Red = c; break; case "LogBg": LogBg = c; break;
            }
        }

        public Palette Clone() => (Palette)MemberwiseClone();

        public bool IsDark => Theme.Lum(Card) < 0.35;

        /// <summary>Paylaşılabilir tema kodu: DKT1:Accent=2563EB;Bg=…</summary>
        public string ToCode() => "DKT1:" + string.Join(";", Keys.Select(k => k + "=" + Palettes.Hex(Get(k)).TrimStart('#')));

        public static bool TryParseCode(string code, Palette basePalette, out Palette p)
        {
            p = basePalette.Clone();
            code = (code ?? "").Trim();
            if (!code.StartsWith("DKT1:", StringComparison.OrdinalIgnoreCase)) return false;
            int n = 0;
            foreach (var part in code.Substring(5).Split(';'))
            {
                var kv = part.Split('=');
                if (kv.Length != 2) continue;
                string key = Keys.FirstOrDefault(k => k.Equals(kv[0].Trim(), StringComparison.OrdinalIgnoreCase));
                if (key != null && Palettes.TryParseColor(kv[1], out var c)) { p.Set(key, c); n++; }
            }
            return n > 0;
        }
    }

    static class Palettes
    {
        static Color H(string hex) { TryParseColor(hex, out var c); return c; }

        static Palette P(string id, string name, string group, string accent, string bg, string card, string border, string text, string muted,
                         string sidebar, string sideText, string green, string amber, string red, string log = null)
        {
            var p = new Palette
            {
                Id = id, Name = name, Group = group,
                Accent = H(accent), Bg = H(bg), Card = H(card), Border = H(border), Text = H(text), Muted = H(muted),
                Sidebar = H(sidebar), SidebarText = H(sideText), Green = H(green), Amber = H(amber), Red = H(red),
            };
            p.LogBg = log != null ? H(log) : p.IsDark ? Theme.Mix(p.Bg, Color.Black, 0.35f) : Color.FromArgb(15, 22, 38);
            return p;
        }

        public static readonly List<Palette> Presets = new List<Palette>
        {
            P("light", "Açık (klasik)", "Açık", "#2563EB", "#F2F5FA", "#FFFFFF", "#E1E6EF", "#141C2D", "#647188", "#0D1424", "#C4CDDC", "#16A34A", "#D97706", "#DC2626"),
            P("emerald", "Zümrüt", "Açık", "#059669", "#F0F7F5", "#FFFFFF", "#D5E7E1", "#0F2A24", "#5B746D", "#063B33", "#C6E6DE", "#16A34A", "#D97706", "#DC2626"),
            P("ocean", "Okyanus", "Açık", "#0891B2", "#EEF6FB", "#FFFFFF", "#D3E5F0", "#0B2534", "#5B7586", "#0B3A53", "#CFE6F2", "#059669", "#D97706", "#DC2626"),
            P("lavender", "Lavanta", "Açık", "#7C3AED", "#F6F3FF", "#FFFFFF", "#E4DCF7", "#1E1433", "#6B5E86", "#2A1650", "#E2D8FA", "#16A34A", "#D97706", "#DC2626"),
            P("sakura", "Sakura", "Açık", "#DB2777", "#FFF5F8", "#FFFFFF", "#F5D5E0", "#3B1F2B", "#8A6070", "#4A1D33", "#F9DCE6", "#16A34A", "#D97706", "#DC2626"),
            P("solarized-light", "Solarized Açık", "Açık", "#268BD2", "#EEE8D5", "#FDF6E3", "#DDD6C1", "#073642", "#657B83", "#002B36", "#93A1A1", "#859900", "#B58900", "#DC322F"),
            P("latte", "Catppuccin Latte", "Açık", "#1E66F5", "#EFF1F5", "#FFFFFF", "#CCD0DA", "#4C4F69", "#6C6F85", "#DCE0E8", "#4C4F69", "#40A02B", "#DF8E1D", "#D20F39"),
            P("github-light", "GitHub Açık", "Açık", "#0969DA", "#F6F8FA", "#FFFFFF", "#D0D7DE", "#1F2328", "#656D76", "#24292F", "#E6EDF3", "#1A7F37", "#BF8700", "#CF222E"),

            P("dark", "Koyu", "Koyu", "#3B82F6", "#111318", "#1B1E25", "#2C313B", "#E6E9F0", "#969EAC", "#0B0D11", "#C9D0DC", "#22C55E", "#F59E0B", "#EF4444"),
            P("github-dark", "GitHub Koyu", "Koyu", "#2F81F7", "#0D1117", "#161B22", "#30363D", "#E6EDF3", "#8D96A0", "#010409", "#C9D1D9", "#3FB950", "#D29922", "#F85149"),
            P("nord", "Nord", "Koyu", "#88C0D0", "#2E3440", "#3B4252", "#4C566A", "#ECEFF4", "#AEB7C6", "#242933", "#D8DEE9", "#A3BE8C", "#EBCB8B", "#BF616A"),
            P("dracula", "Dracula", "Koyu", "#BD93F9", "#282A36", "#313342", "#44475A", "#F8F8F2", "#A9ADC9", "#21222C", "#E2E2DC", "#50FA7B", "#FFB86C", "#FF5555"),
            P("onedark", "One Dark", "Koyu", "#61AFEF", "#282C34", "#2F343E", "#3E4451", "#DCDFE4", "#9DA5B4", "#21252B", "#C8CCD4", "#98C379", "#E5C07B", "#E06C75"),
            P("monokai", "Monokai", "Koyu", "#66D9EF", "#272822", "#31322C", "#49483E", "#F8F8F2", "#B5B19C", "#1E1F1A", "#E0E0D8", "#A6E22E", "#FD971F", "#F92672"),
            P("tokyo", "Tokyo Night", "Koyu", "#7AA2F7", "#1A1B26", "#24283B", "#343A55", "#C0CAF5", "#9AA5CE", "#16161E", "#A9B1D6", "#9ECE6A", "#E0AF68", "#F7768E"),
            P("mocha", "Catppuccin Mocha", "Koyu", "#89B4FA", "#1E1E2E", "#282839", "#45475A", "#CDD6F4", "#A6ADC8", "#181825", "#BAC2DE", "#A6E3A1", "#F9E2AF", "#F38BA8"),
            P("gruvbox", "Gruvbox", "Koyu", "#FE8019", "#282828", "#32302F", "#504945", "#EBDBB2", "#A89984", "#1D2021", "#D5C4A1", "#B8BB26", "#FABD2F", "#FB4934"),
            P("solarized-dark", "Solarized Koyu", "Koyu", "#268BD2", "#002B36", "#073642", "#0E4A5A", "#EEE8D5", "#93A1A1", "#00212B", "#A9B7B7", "#859900", "#B58900", "#DC322F"),
            P("sunset", "Gün Batımı", "Koyu", "#FF7A59", "#1F1A24", "#2A2230", "#3D3245", "#F4E9E1", "#B9A8B5", "#17121B", "#E8D9E0", "#6BCB77", "#FFC75F", "#FF5C7A"),
            P("contrast", "Yüksek Kontrast", "Koyu", "#FFD60A", "#000000", "#0B0B0B", "#FFFFFF", "#FFFFFF", "#E0E0E0", "#000000", "#FFFFFF", "#3DFF7A", "#FFB000", "#FF4D4D", "#000000"),
        };

        // ------------------------------------------------------------ sistem teması

        public static bool SystemDark()
        {
            try
            {
                using (var k = Registry.CurrentUser.OpenSubKey(@"Software\Microsoft\Windows\CurrentVersion\Themes\Personalize"))
                    return k != null && Convert.ToInt32(k.GetValue("AppsUseLightTheme", 1)) == 0;
            }
            catch { return false; }
        }

        /// <summary>Windows vurgu rengi (koyu temada açık tonu, açık temada koyu tonu).</summary>
        public static Color SystemAccent(bool dark)
        {
            try
            {
                using (var k = Registry.CurrentUser.OpenSubKey(@"Software\Microsoft\Windows\CurrentVersion\Explorer\Accent"))
                {
                    if (k?.GetValue("AccentPalette") is byte[] b && b.Length >= 32)
                    {
                        int i = dark ? 2 : 4;   // AccentLight1 / AccentDark1
                        return Color.FromArgb(b[i * 4], b[i * 4 + 1], b[i * 4 + 2]);
                    }
                }
                using (var k = Registry.CurrentUser.OpenSubKey(@"Software\Microsoft\Windows\DWM"))
                {
                    if (k?.GetValue("AccentColor") is int abgr)
                    {
                        var c = Color.FromArgb(abgr & 0xFF, (abgr >> 8) & 0xFF, (abgr >> 16) & 0xFF);
                        return dark ? Theme.Mix(c, Color.White, 0.25f) : c;
                    }
                }
            }
            catch (Exception ign) { App.Ignored("Sistem rengi okunamadı", ign); }
            return dark ? H("#60A5FA") : H("#2563EB");
        }

        public static Palette System()
        {
            if (SystemInformation.HighContrast)
            {
                var hc = new Palette
                {
                    Id = "system", Name = "Sistem (yüksek kontrast)",
                    Accent = SystemColors.Highlight, Bg = SystemColors.Window, Card = SystemColors.Window, Border = SystemColors.WindowText,
                    Text = SystemColors.WindowText, Muted = SystemColors.WindowText, Sidebar = SystemColors.Window, SidebarText = SystemColors.WindowText,
                    Green = H("#3DFF7A"), Amber = H("#FFB000"), Red = H("#FF4D4D"), LogBg = SystemColors.Window,
                };
                return hc;
            }
            bool dark = SystemDark();
            var p = dark
                ? P("system", "Sistem", "", "#60A5FA", "#202020", "#2B2B2B", "#3A3A3A", "#FFFFFF", "#ABABAB", "#1B1B1B", "#F0F0F0", "#6CCB5F", "#FCE100", "#FF99A4")
                : P("system", "Sistem", "", "#2563EB", "#F3F3F3", "#FFFFFF", "#E5E5E5", "#1A1A1A", "#616161", "#EBEBEB", "#1A1A1A", "#0F7B0F", "#9D5D00", "#C42B1C");
            if (dark) { p.Green = H("#4CC35A"); p.Amber = H("#F2B02F"); p.Red = H("#F1707B"); }
            p.Accent = SystemAccent(dark);
            p.Name = "Sistem (" + (dark ? "koyu" : "açık") + ")";
            return p;
        }

        public static Palette Base(string id)
        {
            if (id == "system" || string.IsNullOrEmpty(id)) return System();
            return (Presets.FirstOrDefault(p => p.Id == id) ?? System()).Clone();
        }

        /// <summary>Ayarlardaki tema + kullanıcının değiştirdiği renkler.</summary>
        public static Palette Resolve(AppSettings s)
        {
            var p = Base(s.ThemeId);
            foreach (var o in s.ThemeOverrides)
                if (Palette.Keys.Contains(o.Key) && TryParseColor(o.Hex, out var c)) p.Set(o.Key, c);
            if (s.ThemeOverrides.Count > 0) p.Name += " + özel renkler";
            return p;
        }

        // ------------------------------------------------------------ renk kodu

        public static string Hex(Color c) => "#" + c.R.ToString("X2") + c.G.ToString("X2") + c.B.ToString("X2");

        /// <summary>#RGB, #RRGGBB, RRGGBB, rgb(r,g,b) ya da "r,g,b" biçimlerini kabul eder.</summary>
        public static bool TryParseColor(string text, out Color c)
        {
            c = Color.Empty;
            if (string.IsNullOrWhiteSpace(text)) return false;
            string s = text.Trim().ToLowerInvariant();
            if (s.StartsWith("rgb")) s = s.Substring(3).Trim('(', ')', ' ', 'a');
            if (s.Contains(","))
            {
                var parts = s.Split(',').Select(x => x.Trim()).ToArray();
                if (parts.Length < 3) return false;
                if (int.TryParse(parts[0], out int r) && int.TryParse(parts[1], out int g) && int.TryParse(parts[2], out int b) &&
                    r >= 0 && r < 256 && g >= 0 && g < 256 && b >= 0 && b < 256)
                { c = Color.FromArgb(r, g, b); return true; }
                return false;
            }
            s = s.TrimStart('#');
            if (s.Length == 3) s = new string(new[] { s[0], s[0], s[1], s[1], s[2], s[2] });
            if (s.Length == 8) s = s.Substring(2);
            if (s.Length != 6) return false;
            if (!int.TryParse(s, NumberStyles.HexNumber, CultureInfo.InvariantCulture, out int v)) return false;
            c = Color.FromArgb((v >> 16) & 0xFF, (v >> 8) & 0xFF, v & 0xFF);
            return true;
        }
    }
}
