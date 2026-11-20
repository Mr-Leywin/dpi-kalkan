using System;
using System.Collections.Generic;
using System.Drawing;
using System.IO;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using System.Windows.Forms;
using static DPIKalkan.Theme;

namespace DPIKalkan
{
    /// <summary>Proxy: kayıtlı proxy'ler, test, Windows proxy'si ve program bazlı proxy.</summary>
    sealed class ProxyPage : PageBase
    {
        public override string Key => "proxy";

        readonly Segmented tabs;
        readonly Panel pList, pApps;

        // --- proxy'ler
        readonly ListBox list;
        readonly TextBox tQuick, tName, tHost, tUser, tPass;
        readonly NumericUpDown nPort;
        readonly Segmented kind;
        readonly FlatBtn bSave, bTest, bNew, bBulk, bDel, bSys, bSysOff, bDiscord, bShortcut, bDiscordNormal;
        readonly ReportBox result;
        readonly Label sysState;
        ProxyEntry editing;

        // --- program bazlı
        readonly ListBox appList;
        readonly TextBox aPath, aName, aArgs;
        readonly ThemedCombo aProxy;
        readonly Label aMode, aModeInfo, bridgeState;
        readonly CheckX aSeparate, aClose;
        readonly FlatBtn aAdd, aDel, aSave, aLaunch, aShortcut, aBrowse;
        ProxyApp editingApp;

        public ProxyPage()
        {
            Controls.Add(UIx.L("Proxy", H1, Theme.Text, 28, 2, 300, 36));
            Controls.Add(UIx.L("Trafiği kendi proxy'niz üzerinden geçirin: tüm sistemde ya da yalnızca seçtiğiniz programlarda.",
                Body, Muted, 29, 40, 764, 22));
            tabs = new Segmented("Proxy'ler", "Program bazlı proxy") { Bounds = R(452, 4, 340, 34), BackColor = Bg };
            Controls.Add(tabs);
            pList = new Panel { Bounds = R(0, 72, 820, 568), BackColor = Bg };
            pApps = new Panel { Bounds = R(0, 72, 820, 568), BackColor = Bg, Visible = false };
            Controls.Add(pList);
            Controls.Add(pApps);
            tabs.SelectedChanged += (s, e) => { pList.Visible = tabs.SelectedIndex == 0; pApps.Visible = tabs.SelectedIndex == 1; if (pApps.Visible) ReloadApps(); };

            Card C(Panel host, int x, int y, int w, int h) { var c = new Card { Bounds = R(x, y, w, h) }; host.Controls.Add(c); return c; }

            // ============================================================ PROXY'LER
            var left = C(pList, 28, 4, 330, 412);
            left.Add(UIx.L("KAYITLI PROXY'LER", Caption, Muted, 18, 14, 290, 18));
            list = new ListBox
            {
                Bounds = R(8, 38, 314, 318), DrawMode = DrawMode.OwnerDrawFixed, ItemHeight = D(54),
                BorderStyle = BorderStyle.None, IntegralHeight = false, Font = Body,
            };
            left.Add(list);
            list.BackColor = Theme.Card;
            Theme.StyleScroll(list);
            list.DrawItem += DrawItem;
            list.SelectedIndexChanged += (s, e) => { if (list.SelectedItem is ProxyEntry p) Load(p); };
            bNew = left.Add(new FlatBtn { Text = "Yeni", Glyph = GAdd, Kind = FlatBtn.Kinds.Secondary, Bounds = R(14, 362, 92, 36), Font = Small });
            bBulk = left.Add(new FlatBtn { Text = "Toplu ekle", Glyph = GList, Kind = FlatBtn.Kinds.Secondary, Bounds = R(112, 362, 120, 36), Font = Small });
            bDel = left.Add(new FlatBtn { Text = "Sil", Glyph = GDelete, Kind = FlatBtn.Kinds.Secondary, Bounds = R(238, 362, 78, 36), Font = Small });
            bNew.Click += (s, e) => { list.ClearSelected(); Load(null); tQuick.Focus(); };
            bBulk.Click += (s, e) => { using (var d = new BulkProxyDialog()) if (d.ShowDialog(FindForm()) == DialogResult.OK) Reload(d.FirstAddedId); };
            bDel.Click += (s, e) => Delete();

            var right = C(pList, 370, 4, 422, 412);
            right.Add(UIx.L("PROXY BİLGİLERİ", Caption, Muted, 20, 14, 300, 18));
            Label Lbl(Card c, string t, int y) => c.Add(UIx.L(t, Body, Theme.Text, 20, y, 100, 26, ContentAlignment.MiddleLeft));
            Lbl(right, "Hızlı giriş", 38);
            tQuick = right.Add(Theme.Style(new TextBox { Bounds = R(120, 38, 196, 26), Font = Body }));
            var bFill = right.Add(new FlatBtn { Text = "Doldur", Kind = FlatBtn.Kinds.Secondary, Bounds = R(322, 36, 84, 30), Font = Small });
            bFill.Click += (s, e) => QuickFill();
            tQuick.KeyDown += (s, e) => { if (e.KeyCode == Keys.Enter) { e.SuppressKeyPress = true; QuickFill(); } };
            right.Add(UIx.L("Ör. 1.2.3.4:8080  ·  ip:port:kullanıcı:şifre\nsocks5://kullanıcı:şifre@1.2.3.4:1080", Small, Muted, 120, 66, 290, 32));
            Lbl(right, "Ad", 100);
            tName = right.Add(Theme.Style(new TextBox { Bounds = R(120, 100, 286, 26), Font = Body }));
            Lbl(right, "Tür", 134);
            kind = right.Add(new Segmented("HTTP", "SOCKS5") { Bounds = R(120, 132, 200, 32) });
            right.Add(UIx.L("Test yanlış türü kendisi düzeltir.", Small, Muted, 326, 132, 84, 32));
            Lbl(right, "Adres", 172);
            tHost = right.Add(Theme.Style(new TextBox { Bounds = R(120, 172, 190, 26), Font = Body }));
            right.Add(UIx.L(":", Bold, Muted, 312, 172, 10, 26, ContentAlignment.MiddleCenter));
            nPort = right.Add(Theme.Style(new NumericUpDown { Bounds = R(324, 172, 82, 26), Minimum = 1, Maximum = 65535, Value = 8080 }));
            Lbl(right, "Kullanıcı", 206);
            tUser = right.Add(Theme.Style(new TextBox { Bounds = R(120, 206, 286, 26), Font = Body }));
            Lbl(right, "Şifre", 240);
            tPass = right.Add(Theme.Style(new TextBox { Bounds = R(120, 240, 286, 26), Font = Body, UseSystemPasswordChar = true }));
            right.Add(UIx.L("İsteğe bağlı. Şifre şifrelenerek saklanır.", Small, Muted, 120, 268, 290, 20));
            bSave = right.Add(new FlatBtn { Text = "Kaydet", Glyph = GSave, Kind = FlatBtn.Kinds.Primary, Bounds = R(20, 294, 186, 36) });
            bTest = right.Add(new FlatBtn { Text = "Test et", Glyph = GSpeed, Kind = FlatBtn.Kinds.Secondary, Bounds = R(216, 294, 190, 36) });
            bSave.Click += (s, e) => Save();
            bTest.Click += async (s, e) => await RunTest();
            result = right.Add(new ReportBox { Bounds = R(20, 338, 386, 66) });
            result.BackColor = Theme.Card;
            result.Muted("Proxy'yi kaydetmeden de test edebilirsiniz. Test, proxy üzerinden discord.com'a şifreli bağlantı kurar ve çıkış IP'sini gösterir. Çok sayıda proxy'yi 'Toplu ekle' ile aynı anda deneyin.");

            var bottom = C(pList, 28, 428, 764, 132);
            bottom.Add(UIx.L("SEÇİLİ PROXY'Yİ KULLAN", Caption, Muted, 20, 14, 300, 18));
            sysState = bottom.Add(UIx.L("", Small, Muted, 300, 12, 444, 22, ContentAlignment.TopRight));
            bDiscord = bottom.Add(new FlatBtn { Text = "Discord'u bu proxy ile aç", Glyph = GChat, Kind = FlatBtn.Kinds.Primary, Bounds = R(20, 40, 214, 38), Font = Small });
            bShortcut = bottom.Add(new FlatBtn { Text = "Masaüstüne kısayol", Glyph = GLink, Kind = FlatBtn.Kinds.Secondary, Bounds = R(242, 40, 162, 38), Font = Small });
            bDiscordNormal = bottom.Add(new FlatBtn { Text = "Discord'u proxy'siz aç", Glyph = GRefresh, Kind = FlatBtn.Kinds.Secondary, Bounds = R(412, 40, 170, 38), Font = Small });
            bSys = bottom.Add(new FlatBtn { Text = "Windows proxy'si yap", Glyph = GGlobe, Kind = FlatBtn.Kinds.Secondary, Bounds = R(590, 40, 154, 38), Font = Small });
            bSysOff = bottom.Add(new FlatBtn { Text = "Windows proxy'sini kapat", Glyph = GUndo, Kind = FlatBtn.Kinds.Warning, Bounds = R(590, 40, 154, 38), Font = Small, Visible = false });
            bottom.Add(UIx.L("Başka programlar için 'Program bazlı proxy' sekmesini kullanın. Windows proxy'si tarayıcılar dahil tüm programları etkiler. " +
                             "Discord sesli sohbeti (UDP) proxy'den geçmez.", Small, Muted, 20, 86, 724, 40));
            bDiscord.Click += async (s, e) => await LaunchDiscord();
            bDiscordNormal.Click += async (s, e) => { bDiscordNormal.Enabled = false; string m = await Actions.RestartDiscordAsync(); if (!IsDisposed) { bDiscordNormal.Enabled = true; Info(m); } };
            bShortcut.Click += (s, e) => MakeShortcut();
            bSys.Click += (s, e) => ApplySystem();
            bSysOff.Click += (s, e) => { ProxyTools.RestoreSystem(); UpdateState(); Info("Windows proxy ayarı eski haline döndürüldü."); };

            // ============================================================ PROGRAM BAZLI
            var aleft = C(pApps, 28, 4, 330, 412);
            aleft.Add(UIx.L("PROXY KULLANAN PROGRAMLAR", Caption, Muted, 18, 14, 290, 18));
            appList = new ListBox
            {
                Bounds = R(8, 38, 314, 318), DrawMode = DrawMode.OwnerDrawFixed, ItemHeight = D(54),
                BorderStyle = BorderStyle.None, IntegralHeight = false, Font = Body,
            };
            aleft.Add(appList);
            appList.BackColor = Theme.Card;
            Theme.StyleScroll(appList);
            appList.DrawItem += DrawApp;
            appList.SelectedIndexChanged += (s, e) => { if (appList.SelectedItem is ProxyApp a) LoadApp(a); };
            aAdd = aleft.Add(new FlatBtn { Text = "Program ekle", Glyph = GAdd, Kind = FlatBtn.Kinds.Primary, Bounds = R(14, 362, 196, 36), Font = Small });
            aDel = aleft.Add(new FlatBtn { Text = "Sil", Glyph = GDelete, Kind = FlatBtn.Kinds.Secondary, Bounds = R(216, 362, 100, 36), Font = Small });
            aAdd.Click += (s, e) => ShowAddMenu();
            aDel.Click += (s, e) => DeleteApp();

            var aright = C(pApps, 370, 4, 422, 412);
            aright.Add(UIx.L("PROGRAM AYARI", Caption, Muted, 20, 14, 300, 18));
            Lbl(aright, "Program", 38);
            aPath = aright.Add(Theme.Style(new TextBox { Bounds = R(120, 38, 206, 26), Font = Small, ReadOnly = true }));
            aBrowse = aright.Add(new FlatBtn { Text = "Gözat", Kind = FlatBtn.Kinds.Secondary, Bounds = R(332, 36, 74, 30), Font = Small });
            aBrowse.Click += (s, e) => Browse(editingApp);
            Lbl(aright, "Ad", 72);
            aName = aright.Add(Theme.Style(new TextBox { Bounds = R(120, 72, 286, 26), Font = Body }));
            Lbl(aright, "Proxy", 106);
            aProxy = aright.Add(new ThemedCombo { Bounds = R(120, 106, 286, 26) });
            aProxy.SelectedIndexChanged += (s, e) => UpdateAppInfo();
            Lbl(aright, "Yöntem", 140);
            aMode = aright.Add(UIx.L("", Bold, Theme.Text, 120, 140, 286, 26, ContentAlignment.MiddleLeft));
            aModeInfo = aright.Add(UIx.L("", Small, Muted, 120, 166, 286, 46));
            aSeparate = aright.Add(new CheckX { Text = "Ayrı tarayıcı profili (tarayıcı açıkken de çalışır)", Bounds = R(20, 214, 386, 24), Font = Small });
            aClose = aright.Add(new CheckX { Text = "Açmadan önce programın açık kopyasını kapat", Bounds = R(20, 240, 386, 24), Font = Small });
            Lbl(aright, "Ek parametre", 270);
            aArgs = aright.Add(Theme.Style(new TextBox { Bounds = R(120, 270, 286, 26), Font = Mono }));
            aSave = aright.Add(new FlatBtn { Text = "Kaydet", Glyph = GSave, Kind = FlatBtn.Kinds.Secondary, Bounds = R(20, 308, 120, 36) });
            aLaunch = aright.Add(new FlatBtn { Text = "Proxy ile aç", Glyph = GPlay, Kind = FlatBtn.Kinds.Primary, Bounds = R(146, 308, 136, 36) });
            aShortcut = aright.Add(new FlatBtn { Text = "Kısayol", Glyph = GLink, Kind = FlatBtn.Kinds.Secondary, Bounds = R(288, 308, 118, 36) });
            aSave.Click += (s, e) => SaveApp(true);
            aLaunch.Click += async (s, e) => await LaunchApp();
            aShortcut.Click += (s, e) => AppShortcut();
            bridgeState = aright.Add(UIx.L("", Small, Muted, 20, 352, 386, 50));

            var abottom = C(pApps, 28, 428, 764, 132);
            abottom.Add(UIx.L("NASIL ÇALIŞIR?", Caption, Muted, 20, 14, 300, 18));
            abottom.Add(UIx.L(
                "Seçtiğiniz program proxy ayarıyla başlatılır; diğer programlar ve Windows ayarları etkilenmez. Chrome, Edge, Brave, Opera, Discord, Spotify gibi " +
                "Chromium tabanlı programlara --proxy-server verilir, Firefox ayrı bir proxy profiliyle açılır, diğer programlara HTTP_PROXY ortam değişkenleri verilir " +
                "(program desteklemiyorsa etkisi olmaz). Şifreli proxy'ler DPI Kalkan'ın yerel köprüsü (127.0.0.1) üzerinden kullanılır; bu durumda DPI Kalkan açık olmalıdır. " +
                "'Kısayol' ile masaüstüne kalıcı bir başlatıcı eklenir.",
                Small, Muted, 20, 36, 724, 90));

            OnStatus(() => { if (Visible) { UpdateState(); UpdateBridgeState(); } });
            Reload();
        }

        public override void OnActivated() { Reload(); UpdateState(); if (pApps.Visible) ReloadApps(); }

        /// <summary>Program bazlı proxy sekmesini açar.</summary>
        public void ShowApps() => tabs.SelectedIndex = 1;

        // ============================================================ proxy listesi

        ProxyEntry Selected => list.SelectedItem as ProxyEntry;

        void Reload(string selectId = null)
        {
            string keep = selectId ?? Selected?.Id;
            list.BeginUpdate();
            list.Items.Clear();
            foreach (var p in App.Settings.Proxies) list.Items.Add(p);
            list.EndUpdate();
            int idx = App.Settings.Proxies.FindIndex(p => p.Id == keep);
            if (idx < 0 && list.Items.Count > 0) idx = 0;
            if (idx >= 0) list.SelectedIndex = idx; else Load(null);
            UpdateState();
        }

        void Load(ProxyEntry p)
        {
            editing = p;
            tQuick.Text = "";
            tName.Text = p?.Name ?? "";
            kind.SetSilently(p != null && p.Kind == ProxyKind.Socks5 ? 1 : 0);
            tHost.Text = p?.Host ?? "";
            nPort.Value = Math.Max(1, Math.Min(65535, p?.Port ?? 8080));
            tUser.Text = p?.User ?? "";
            tPass.Text = p?.Password ?? "";
            bSave.Text = p == null ? "Ekle" : "Kaydet";
            UpdateState();
        }

        void UpdateState()
        {
            var sel = Selected;
            bool has = sel != null;
            bDel.Enabled = bDiscord.Enabled = bShortcut.Enabled = bSys.Enabled = has;
            var cur = ProxyTools.CurrentSystem();
            var mine = ProxyTools.ActiveSystemEntry();
            bool ours = mine != null;
            bSys.Visible = !ours;
            bSysOff.Visible = ours || App.Settings.SystemProxyBackup != null;
            sysState.Text = ours ? "Windows proxy'si: " + mine.Title + " (DPI Kalkan ayarladı)"
                          : cur.Enabled ? "Windows proxy'si: " + cur.Server + " (başka bir program ayarlamış)"
                          : "Windows proxy'si: kapalı";
            sysState.ForeColor = ours ? AccentInk : cur.Enabled ? AmberText : Muted;
            list.Invalidate();
        }

        void QuickFill()
        {
            if (!ProxyTools.TryParse(tQuick.Text, kind.SelectedIndex == 1 ? ProxyKind.Socks5 : ProxyKind.Http, out var p, out var err)) { Warn(err); return; }
            kind.SetSilently(p.Kind == ProxyKind.Socks5 ? 1 : 0);
            tHost.Text = p.Host;
            nPort.Value = p.Port;
            tUser.Text = p.User;
            tPass.Text = p.Password;
            if (tName.Text.Trim().Length == 0) tName.Text = p.KindLabel + " " + p.Host;
            tQuick.Text = "";
        }

        ProxyEntry Collect(out string error)
        {
            error = "";
            string host = tHost.Text.Trim();
            if (host.Length == 0 && tQuick.Text.Trim().Length > 0) { QuickFill(); host = tHost.Text.Trim(); }
            if (host.Length == 0) { error = "Proxy adresini (IP ya da sunucu adı) girin."; return null; }
            if (Uri.CheckHostName(host.Trim('[', ']')) == UriHostNameType.Unknown) { error = "Geçersiz adres: " + host; return null; }
            var p = new ProxyEntry
            {
                Name = tName.Text.Trim(), Kind = kind.SelectedIndex == 1 ? ProxyKind.Socks5 : ProxyKind.Http,
                Host = host.Trim('[', ']'), Port = (int)nPort.Value, User = tUser.Text.Trim(),
            };
            p.Password = p.User.Length > 0 ? tPass.Text : "";
            return p;
        }

        void Save()
        {
            var p = Collect(out var err);
            if (p == null) { Warn(err); return; }
            if (editing != null)
            {
                editing.Name = p.Name; editing.Kind = p.Kind; editing.Host = p.Host; editing.Port = p.Port;
                editing.User = p.User; editing.PassData = p.PassData;
                p = editing;
            }
            else App.Settings.Proxies.Add(p);
            ProxyBridge.AssignPorts(App.Settings.Proxies);
            App.Save();
            App.Log("Proxy kaydedildi: " + p.KindLabel + " " + p.Address, LogLevel.Info);
            Reload(p.Id);
            if (ProxyTools.ActiveSystemEntry()?.Id == p.Id) ProxyTools.ApplySystem(p);
            if (ProxyBridge.IsRunning(p)) { ProxyBridge.Stop(p.Id); ProxyBridge.Sync(); }
        }

        void Delete()
        {
            var p = Selected;
            if (p == null) return;
            int used = App.Settings.ProxyApps.Count(a => a.ProxyId == p.Id);
            if (MessageBox.Show(FindForm(), "'" + p.Title + "' silinsin mi?" + (used > 0 ? "\n\nBu proxy'yi kullanan " + used + " program kuralı da silinecek." : ""),
                    App.Name, MessageBoxButtons.YesNo, MessageBoxIcon.Question) != DialogResult.Yes) return;
            if (ProxyTools.ActiveSystemEntry()?.Id == p.Id) ProxyTools.RestoreSystem();
            ProxyBridge.Stop(p.Id);
            App.Settings.Proxies.RemoveAll(x => x.Id == p.Id);
            App.Settings.ProxyApps.RemoveAll(a => a.ProxyId == p.Id);
            App.Save();
            Reload();
        }

        async Task RunTest()
        {
            var p = Collect(out var err);
            if (p == null) { Warn(err); return; }
            bTest.Enabled = false;
            result.Clear();
            result.Muted("Test ediliyor: " + p.KindLabel + " " + p.Address + " …");
            var r = await Task.Run(() => ProxyTools.Test(p));
            if (IsDisposed) return;
            bTest.Enabled = true;
            result.Clear();
            if (r.Ok)
            {
                result.Out("✓ Çalışıyor · discord.com erişilebilir · " + r.TotalMs + " ms", GreenText, Bold);
                result.Out("Çıkış IP'si: " + (r.ExitIp.Length > 0 ? r.ExitIp : "?") + (r.Country.Length > 0 ? "  ·  Ülke: " + r.Country : "") +
                           (r.KindFixed ? "  ·  Tür " + p.KindLabel + " olarak düzeltildi" : ""), Theme.Text, Small);
            }
            else
            {
                var info = ErrorCatalog.Get(r.Code);
                result.Out("✗ " + r.Code + " · " + (info?.Title ?? "Proxy çalışmıyor"), RedText, Bold);
                result.Out(r.Message, Theme.Text, Small);
            }
            result.SelectionStart = 0;
            if (r.KindFixed) kind.SetSilently(p.Kind == ProxyKind.Socks5 ? 1 : 0);
            if (editing != null && editing.Host == p.Host && editing.Port == p.Port)
            {
                editing.Kind = p.Kind;
                editing.LastOk = p.LastOk; editing.LastResult = p.LastResult; editing.LastTest = p.LastTest; editing.LastMs = p.LastMs;
                App.Save();
                list.Invalidate();
            }
        }

        async Task LaunchDiscord()
        {
            var p = Selected;
            if (p == null) return;
            bDiscord.Enabled = false;
            string msg = null;
            bool ok;
            var discordPath = AppProxy.Installed().FirstOrDefault(x => x.Name == "Discord").Path;
            if (discordPath != null)
            {
                // şifreli proxy'lerde yerel köprü kullanılır
                var rule = new ProxyApp { Name = "Discord", ExePath = discordPath, Mode = AppProxyMode.Discord, ProxyId = p.Id };
                ok = await Task.Run(() => AppProxy.Launch(rule, p, out msg));
            }
            else ok = await Task.Run(() => ProxyTools.LaunchDiscord(p, out msg));
            if (IsDisposed) return;
            bDiscord.Enabled = true;
            Info(msg + (ok ? "\n\nNormal Discord'a dönmek için 'Discord'u proxy'siz aç' düğmesini kullanın." : ""));
        }

        void MakeShortcut()
        {
            var p = Selected;
            if (p == null) return;
            try
            {
                string desk = Environment.GetFolderPath(Environment.SpecialFolder.DesktopDirectory);
                string name = "Discord (Proxy " + Scripts.Slug(p.Title) + ").lnk";
                string path;
                var discordPath = AppProxy.Installed().FirstOrDefault(x => x.Name == "Discord").Path;
                if (discordPath != null)
                {
                    var rule = new ProxyApp { Name = "Discord", ExePath = discordPath, Mode = AppProxyMode.Discord, ProxyId = p.Id };
                    if (AppProxy.NeedsBridge(rule, p)) { App.Settings.ProxyApps.Add(rule); App.Save(); ProxyBridge.Sync(); }
                    path = AppProxy.CreateShortcut(rule, p, Path.Combine(desk, name));
                }
                else path = ProxyTools.CreateShortcut(p, Path.Combine(desk, name));
                App.Log("Proxy'li Discord kısayolu oluşturuldu: " + path, LogLevel.Success);
                Info("Masaüstüne '" + name + "' kısayolu oluşturuldu. Discord'u bu kısayolla açtığınızda proxy kullanılır.");
            }
            catch (Exception ex) { Warn("Kısayol oluşturulamadı: " + ex.Message); }
        }

        void ApplySystem()
        {
            var p = Selected;
            if (p == null) return;
            string note = p.Kind == ProxyKind.Socks5
                ? "\n\nUyarı: Windows sistem proxy'si SOCKS5'i tam desteklemez (birçok program SOCKS4 sanır). SOCKS5 için 'Program bazlı proxy' daha güvenilirdir."
                : "";
            if (p.HasAuth) note += "\n\nBu proxy şifre istiyor; programlar şifreyi ayrıca sorabilir. Program bazlı proxy bunu yerel köprüyle otomatik halleder.";
            if (MessageBox.Show(FindForm(),
                    "Windows proxy'si " + p.KindLabel + " " + p.Address + " olarak ayarlanacak. Tarayıcılar ve sistem proxy'sini kullanan tüm programlar bu proxy'den geçer." +
                    note + "\n\nŞu anki ayar yedeklenir; 'Windows proxy'sini kapat' ile geri alabilirsiniz. Devam edilsin mi?",
                    App.Name, MessageBoxButtons.YesNo, MessageBoxIcon.Question) != DialogResult.Yes) return;
            try { ProxyTools.ApplySystem(p); }
            catch (Exception ex) { Warn("Ayarlanamadı: " + ex.Message); }
            UpdateState();
        }

        void DrawItem(object sender, DrawItemEventArgs e)
        {
            if (e.Index < 0) return;
            var p = (ProxyEntry)list.Items[e.Index];
            var (g, bg, r) = Row(e);
            bool active = ProxyTools.ActiveSystemEntry()?.Id == p.Id;
            Color dot = p.LastTest == null ? Mix(Theme.Card, Theme.Text, 0.2f) : p.LastOk ? Green : Red;
            using (var b = new SolidBrush(dot)) g.FillEllipse(b, r.X + D(12), r.Y + D(12), D(9), D(9));
            var flags = TextFormatFlags.NoPadding | TextFormatFlags.EndEllipsis | TextFormatFlags.SingleLine;
            string tag = active ? "  · Windows proxy'si" : ProxyBridge.IsRunning(p) ? "  · köprü açık" : "";
            TextRenderer.DrawText(g, p.Title + tag, Bold, new Rectangle(r.X + D(28), r.Y + D(6), r.Width - D(36), D(20)), active ? AccentInk : Theme.Text, bg, flags);
            TextRenderer.DrawText(g, p.KindLabel + " · " + p.Address + (p.HasAuth ? " · şifreli" : ""), Small, new Rectangle(r.X + D(28), r.Y + D(25), r.Width - D(36), D(16)), Muted, bg, flags);
            if (!string.IsNullOrEmpty(p.LastResult))
                TextRenderer.DrawText(g, p.LastResult, Small, new Rectangle(r.X + D(28), r.Y + D(39), r.Width - D(36), D(14)), p.LastOk ? GreenText : RedText, bg, flags);
        }

        (Graphics, Color, Rectangle) Row(DrawItemEventArgs e)
        {
            var g = e.Graphics;
            HQ(g);
            bool sel = (e.State & DrawItemState.Selected) != 0;
            var r = e.Bounds;
            using (var b = new SolidBrush(Theme.Card)) g.FillRectangle(b, r);
            Color bg = sel ? AccentSoft : Theme.Card;
            using (var path = Round(new RectangleF(r.X + 2, r.Y + 2, r.Width - 5, r.Height - 4), D(8)))
            {
                using (var b = new SolidBrush(bg)) g.FillPath(b, path);
                if (sel) using (var pen = new Pen(AccentBorder)) g.DrawPath(pen, path);
            }
            return (g, bg, r);
        }

        // ============================================================ program bazlı proxy

        ProxyApp SelectedApp => appList.SelectedItem as ProxyApp;

        void ReloadApps(string selectId = null)
        {
            string keep = selectId ?? SelectedApp?.Id;
            appList.BeginUpdate();
            appList.Items.Clear();
            foreach (var a in App.Settings.ProxyApps) appList.Items.Add(a);
            appList.EndUpdate();
            aProxy.BeginUpdate();
            aProxy.Items.Clear();
            foreach (var p in App.Settings.Proxies) aProxy.Items.Add(p.Title + "  (" + p.KindLabel + " " + p.Address + ")");
            aProxy.EndUpdate();
            int idx = App.Settings.ProxyApps.FindIndex(a => a.Id == keep);
            if (idx < 0 && appList.Items.Count > 0) idx = 0;
            if (idx >= 0) appList.SelectedIndex = idx; else LoadApp(null);
            UpdateBridgeState();
        }

        void LoadApp(ProxyApp a)
        {
            editingApp = a;
            bool has = a != null;
            foreach (Control c in new Control[] { aName, aProxy, aSeparate, aClose, aArgs, aSave, aLaunch, aShortcut, aBrowse }) c.Enabled = has;
            aDel.Enabled = has;
            aPath.Text = a?.ExePath ?? "";
            aName.Text = a?.Name ?? "";
            aArgs.Text = a?.ExtraArgs ?? "";
            aSeparate.Checked = a?.SeparateProfile ?? true;
            aClose.Checked = a?.CloseRunning ?? true;
            int pi = a == null ? -1 : App.Settings.Proxies.FindIndex(p => p.Id == a.ProxyId);
            aProxy.SelectedIndex = pi >= 0 ? pi : (aProxy.Items.Count > 0 && has ? 0 : -1);
            UpdateAppInfo();
        }

        void UpdateAppInfo()
        {
            var a = editingApp;
            if (a == null)
            {
                aMode.Text = "";
                aModeInfo.Text = App.Settings.Proxies.Count == 0
                    ? "Önce 'Proxy'ler' sekmesinden bir proxy ekleyin, sonra 'Program ekle' ile program seçin."
                    : "'Program ekle' ile bir program seçin.";
                return;
            }
            aMode.Text = a.ModeLabel;
            var p = aProxy.SelectedIndex >= 0 && aProxy.SelectedIndex < App.Settings.Proxies.Count ? App.Settings.Proxies[aProxy.SelectedIndex] : null;
            bool bridge = p != null && AppProxy.NeedsBridge(a, p);
            switch (a.Mode)
            {
                case AppProxyMode.Discord: aModeInfo.Text = "Discord kapatılıp proxy ile yeniden açılır."; break;
                case AppProxyMode.Chromium: aModeInfo.Text = "Program --proxy-server ile açılır."; break;
                case AppProxyMode.Firefox: aModeInfo.Text = "Firefox, ayarları proxy'ye yönlendirilmiş ayrı bir profille açılır."; break;
                default: aModeInfo.Text = "HTTP_PROXY/HTTPS_PROXY ile açılır; yalnızca bunu destekleyen programlarda çalışır."; break;
            }
            if (bridge) aModeInfo.Text += " Yerel köprü kullanılır (127.0.0.1:" + p.LocalPort + ").";
            bool browser = a.Mode == AppProxyMode.Firefox || (a.Mode == AppProxyMode.Chromium && new[] { "chrome.exe", "msedge.exe", "brave.exe", "opera.exe", "vivaldi.exe", "browser.exe", "chromium.exe" }.Contains(Path.GetFileName(a.ExePath).ToLowerInvariant()));
            aSeparate.Visible = browser && a.Mode == AppProxyMode.Chromium;
            aClose.Visible = !browser || !aSeparate.Checked;
        }

        void UpdateBridgeState()
        {
            int n = ProxyBridge.RunningCount, act = ProxyBridge.ActiveConnections;
            bridgeState.Text = n == 0 ? "Yerel köprü: kapalı (şifresiz proxy'lerde gerekmez)."
                                      : "Yerel köprü: " + n + " proxy için açık · " + act + " etkin bağlantı. DPI Kalkan açık kaldığı sürece çalışır.";
            bridgeState.ForeColor = n > 0 ? AccentInk : Muted;
            appList.Invalidate();
        }

        void ShowAddMenu()
        {
            if (App.Settings.Proxies.Count == 0)
            {
                Warn("Önce 'Proxy'ler' sekmesinden bir proxy ekleyip test edin.");
                tabs.SelectedIndex = 0;
                return;
            }
            var m = new ContextMenuStrip { Font = Body, ShowImageMargin = false };
            var found = AppProxy.Installed();
            foreach (var (name, path) in found)
            {
                var pth = path; var nm = name;
                m.Items.Add(nm, null, (s, e) => AddApp(nm, pth));
            }
            if (found.Count > 0) m.Items.Add(new ToolStripSeparator());
            m.Items.Add("Başka bir program seç (.exe)…", null, (s, e) => Browse(null));
            m.Closed += (s, e) => BeginInvoke(new Action(m.Dispose));
            m.Show(aAdd, new Point(0, aAdd.Height));
        }

        void Browse(ProxyApp target)
        {
            using (var d = new OpenFileDialog { Filter = "Programlar (*.exe)|*.exe", Title = "Proxy ile açılacak programı seçin" })
            {
                if (d.ShowDialog(FindForm()) != DialogResult.OK) return;
                if (target == null) AddApp(System.Diagnostics.FileVersionInfo.GetVersionInfo(d.FileName).FileDescription is string desc && desc.Trim().Length > 0
                                           ? desc.Trim() : Path.GetFileNameWithoutExtension(d.FileName), d.FileName);
                else
                {
                    target.ExePath = d.FileName;
                    target.Mode = AppProxy.Detect(d.FileName);
                    App.Save();
                    ReloadApps(target.Id);
                }
            }
        }

        void AddApp(string name, string path)
        {
            var a = new ProxyApp
            {
                Name = name, ExePath = path, Mode = AppProxy.Detect(path),
                ProxyId = (Selected ?? App.Settings.Proxies.FirstOrDefault())?.Id ?? "",
            };
            App.Settings.ProxyApps.Add(a);
            App.Save();
            ProxyBridge.Sync();
            App.Log("Program bazlı proxy kuralı eklendi: " + a.Name + " (" + a.ModeLabel + ")", LogLevel.Info);
            ReloadApps(a.Id);
        }

        bool SaveApp(bool notify)
        {
            var a = editingApp;
            if (a == null) return false;
            if (aProxy.SelectedIndex < 0 || aProxy.SelectedIndex >= App.Settings.Proxies.Count) { Warn("Bir proxy seçin."); return false; }
            a.Name = aName.Text.Trim().Length > 0 ? aName.Text.Trim() : Path.GetFileNameWithoutExtension(a.ExePath);
            a.ProxyId = App.Settings.Proxies[aProxy.SelectedIndex].Id;
            a.ExtraArgs = aArgs.Text.Trim();
            a.SeparateProfile = aSeparate.Checked;
            a.CloseRunning = aClose.Checked;
            App.Save();
            ProxyBridge.Sync();
            ReloadApps(a.Id);
            if (notify) App.Log("Program bazlı proxy kuralı kaydedildi: " + a.Name, LogLevel.Info);
            return true;
        }

        void DeleteApp()
        {
            var a = SelectedApp;
            if (a == null) return;
            if (MessageBox.Show(FindForm(), "'" + a.Name + "' kuralı silinsin mi? (Program etkilenmez; masaüstü kısayolu varsa onu da silin.)",
                    App.Name, MessageBoxButtons.YesNo, MessageBoxIcon.Question) != DialogResult.Yes) return;
            App.Settings.ProxyApps.RemoveAll(x => x.Id == a.Id);
            App.Save();
            ProxyBridge.Sync();
            ReloadApps();
        }

        async Task LaunchApp()
        {
            if (!SaveApp(false)) return;
            var a = editingApp;
            var p = App.Settings.Proxies.FirstOrDefault(x => x.Id == a.ProxyId);
            if (p == null) return;
            if (a.CloseRunning && aClose.Visible && Path.GetFileName(a.ExePath).Length > 0 &&
                System.Diagnostics.Process.GetProcessesByName(a.Mode == AppProxyMode.Discord ? "Discord" : Path.GetFileNameWithoutExtension(a.ExePath)).Length > 0 &&
                MessageBox.Show(FindForm(), a.Name + " şu an açık. Proxy ile yeniden açmak için kapatılacak (kaydedilmemiş işleriniz varsa önce kaydedin). Devam edilsin mi?",
                    App.Name, MessageBoxButtons.YesNo, MessageBoxIcon.Question) != DialogResult.Yes) return;
            aLaunch.Enabled = false;
            string msg = null;
            bool ok = await Task.Run(() => AppProxy.Launch(a, p, out msg));
            if (IsDisposed) return;
            aLaunch.Enabled = true;
            UpdateBridgeState();
            if (!ok) Warn(msg);
        }

        void AppShortcut()
        {
            if (!SaveApp(false)) return;
            var a = editingApp;
            var p = App.Settings.Proxies.FirstOrDefault(x => x.Id == a.ProxyId);
            if (p == null) return;
            try
            {
                string desk = Environment.GetFolderPath(Environment.SpecialFolder.DesktopDirectory);
                string name = a.Name + " (Proxy).lnk";
                foreach (var ch in Path.GetInvalidFileNameChars()) name = name.Replace(ch, '_');
                string path = AppProxy.CreateShortcut(a, p, Path.Combine(desk, name));
                App.Log("Proxy'li kısayol oluşturuldu: " + path, LogLevel.Success);
                Info("Masaüstüne '" + name + "' kısayolu oluşturuldu." +
                     (AppProxy.NeedsBridge(a, p) ? "\n\nBu proxy şifreli olduğu için kısayol DPI Kalkan'ın yerel köprüsünü kullanır; DPI Kalkan açıkken çalışır." : ""));
            }
            catch (Exception ex) { Warn("Kısayol oluşturulamadı: " + ex.Message); }
        }

        void DrawApp(object sender, DrawItemEventArgs e)
        {
            if (e.Index < 0) return;
            var a = (ProxyApp)appList.Items[e.Index];
            var (g, bg, r) = Row(e);
            Icon ic = null;
            try
            {
                string icoFile = a.Mode == AppProxyMode.Discord ? Path.Combine(Path.GetDirectoryName(a.ExePath) ?? "", "app.ico") : null;
                if (icoFile != null && File.Exists(icoFile)) ic = new Icon(icoFile, D(24), D(24));
                else if (File.Exists(a.ExePath)) ic = Icon.ExtractAssociatedIcon(a.ExePath);
            }
            catch { }
            if (ic != null) using (ic) g.DrawIcon(ic, new Rectangle(r.X + D(10), r.Y + D(11), D(24), D(24)));
            var p = App.Settings.Proxies.FirstOrDefault(x => x.Id == a.ProxyId);
            var flags = TextFormatFlags.NoPadding | TextFormatFlags.EndEllipsis | TextFormatFlags.SingleLine;
            TextRenderer.DrawText(g, a.Name, Bold, new Rectangle(r.X + D(42), r.Y + D(6), r.Width - D(50), D(20)), Theme.Text, bg, flags);
            TextRenderer.DrawText(g, p != null ? "→ " + p.Title + " (" + p.KindLabel + ")" : "→ proxy seçilmedi", Small,
                new Rectangle(r.X + D(42), r.Y + D(25), r.Width - D(50), D(16)), p != null ? AccentInk : RedText, bg, flags);
            TextRenderer.DrawText(g, a.ModeLabel + (p != null && AppProxy.NeedsBridge(a, p) ? " · köprü" : ""), Small,
                new Rectangle(r.X + D(42), r.Y + D(39), r.Width - D(50), D(14)), Muted, bg, flags);
        }

        void Info(string m) => MessageBox.Show(FindForm(), m, App.Name, MessageBoxButtons.OK, MessageBoxIcon.Information);
        void Warn(string m) => MessageBox.Show(FindForm(), m, App.Name, MessageBoxButtons.OK, MessageBoxIcon.Warning);
    }

    /// <summary>Proxy listesini yapıştırıp hepsini aynı anda test etme penceresi.</summary>
    sealed class BulkProxyDialog : Form
    {
        readonly TextBox input;
        readonly ListView lv;
        readonly FlatBtn bTest, bAdd, bClose;
        readonly ProgressLine bar;
        readonly Label status;
        readonly Segmented kind;
        List<(ProxyEntry P, ProxyTestResult R)> results;
        CancellationTokenSource cts;
        public string FirstAddedId;

        public BulkProxyDialog()
        {
            Text = "Toplu proxy ekle – " + App.Name;
            FormBorderStyle = FormBorderStyle.FixedDialog;
            StartPosition = FormStartPosition.CenterParent;
            MaximizeBox = MinimizeBox = false;
            ShowInTaskbar = false;
            AutoScaleMode = AutoScaleMode.None;
            BackColor = Bg;
            Font = Body;
            ClientSize = new Size(D(720), D(600));
            Icon = IconArt.AppIcon(32);

            Controls.Add(UIx.L("Toplu proxy ekle ve test et", H2, Theme.Text, 24, 14, 600, 30));
            Controls.Add(UIx.L("Her satıra bir proxy yapıştırın (ip:port, ip:port:kullanıcı:şifre, socks5://…). Hepsi aynı anda test edilir; yalnızca Discord'a ulaşabilenler eklenir.",
                Small, Muted, 25, 44, 670, 36));
            input = Theme.Style(new TextBox { Bounds = R(24, 84, 672, 150), Multiline = true, ScrollBars = ScrollBars.Vertical, Font = Mono, AcceptsReturn = true });
            Controls.Add(input);
            Controls.Add(UIx.L("Tür belirtilmemişse:", Body, Theme.Text, 24, 244, 150, 30, ContentAlignment.MiddleLeft));
            kind = new Segmented("HTTP", "SOCKS5") { Bounds = R(170, 242, 200, 32), BackColor = Bg };
            Controls.Add(kind);
            Controls.Add(UIx.L("(yanlışsa test diğer türü de dener)", Small, Muted, 378, 244, 300, 30, ContentAlignment.MiddleLeft));
            bTest = new FlatBtn { Text = "Hepsini test et", Glyph = GSpeed, Kind = FlatBtn.Kinds.Primary, Bounds = R(24, 284, 180, 38) };
            Controls.Add(bTest);
            status = UIx.L("", Small, Muted, 214, 284, 482, 38, ContentAlignment.MiddleLeft);
            Controls.Add(status);
            bar = new ProgressLine { Bounds = R(24, 330, 672, 6), BackColor = Bg };
            Controls.Add(bar);
            lv = UIx.StyleList(new ListView
            {
                Bounds = R(24, 344, 672, 196), View = View.Details, FullRowSelect = true, HeaderStyle = ColumnHeaderStyle.Nonclickable,
                BorderStyle = BorderStyle.FixedSingle, Font = Body, MultiSelect = false,
            });
            lv.Columns.Add("Proxy", D(210));
            lv.Columns.Add("Tür", D(70));
            lv.Columns.Add("Süre", D(70));
            lv.Columns.Add("Ülke", D(60));
            lv.Columns.Add("Sonuç", D(240));
            Controls.Add(lv);
            bAdd = new FlatBtn { Text = "Çalışanları ekle", Glyph = GAdd, Kind = FlatBtn.Kinds.Success, Bounds = R(24, 550, 220, 38), Enabled = false };
            bClose = new FlatBtn { Text = "Kapat", Kind = FlatBtn.Kinds.Secondary, Bounds = R(576, 550, 120, 38) };
            Controls.Add(bAdd);
            Controls.Add(bClose);
            bTest.Click += async (s, e) => await RunAll();
            bAdd.Click += (s, e) => AddWorking();
            bClose.Click += (s, e) => { cts?.Cancel(); DialogResult = FirstAddedId != null ? DialogResult.OK : DialogResult.Cancel; Close(); };
        }

        protected override void OnHandleCreated(EventArgs e)
        {
            base.OnHandleCreated(e);
            Native.DarkTitle(Handle, IsDark);
        }

        async Task RunAll()
        {
            var items = ProxyTools.ParseList(input.Text, kind.SelectedIndex == 1 ? ProxyKind.Socks5 : ProxyKind.Http, out int invalid);
            if (items.Count == 0) { status.Text = "Geçerli proxy bulunamadı" + (invalid > 0 ? " (" + invalid + " satır okunamadı)." : "."); return; }
            if (items.Count > 500) items = items.Take(500).ToList();
            bTest.Enabled = bAdd.Enabled = false;
            lv.Items.Clear();
            cts = new CancellationTokenSource();
            status.Text = items.Count + " proxy test ediliyor (aynı anda 24)…" + (invalid > 0 ? " " + invalid + " satır okunamadı." : "");
            status.ForeColor = Muted;
            var token = cts.Token;
            results = await Task.Run(() => ProxyTools.BulkTest(items, (done, total) =>
            {
                try { BeginInvoke(new Action(() => { bar.Value = done / (float)total; status.Text = done + "/" + total + " test edildi…"; })); } catch { }
            }, token));
            if (IsDisposed) return;
            lv.BeginUpdate();
            foreach (var (p, r) in results.OrderByDescending(x => x.R.Ok).ThenBy(x => x.R.Ok ? x.R.TotalMs : int.MaxValue))
                lv.Items.Add(new ListViewItem(new[]
                {
                    p.Address + (p.HasAuth ? " (şifreli)" : ""), p.KindLabel, r.Ok ? r.TotalMs + " ms" : "", r.Country,
                    r.Ok ? "✓ Çalışıyor" + (r.KindFixed ? " (tür düzeltildi)" : "") : r.Code + " " + r.Message,
                }) { ForeColor = r.Ok ? GreenText : Muted });
            lv.EndUpdate();
            int ok = results.Count(x => x.R.Ok);
            bar.Value = 1;
            status.Text = ok + "/" + results.Count + " proxy Discord'a ulaşabiliyor." + (ok == 0 ? " Ücretsiz proxy'ler sık kapanır; başka bir liste deneyin." : "");
            status.ForeColor = ok > 0 ? GreenText : AmberText;
            bAdd.Text = "Çalışanları ekle (" + ok + ")";
            bAdd.Enabled = ok > 0;
            bTest.Enabled = true;
        }

        void AddWorking()
        {
            int n = 0;
            foreach (var (p, r) in results.Where(x => x.R.Ok).OrderBy(x => x.R.TotalMs))
            {
                if (App.Settings.Proxies.Any(x => x.Host == p.Host && x.Port == p.Port)) continue;
                p.Name = p.KindLabel + " " + p.Host + (r.Country.Length > 0 ? " · " + r.Country : "");
                App.Settings.Proxies.Add(p);
                if (FirstAddedId == null) FirstAddedId = p.Id;
                n++;
            }
            ProxyBridge.AssignPorts(App.Settings.Proxies);
            App.Save();
            App.Log(n + " çalışan proxy eklendi (toplu test).", LogLevel.Success);
            bAdd.Enabled = false;
            bAdd.Text = n + " proxy eklendi";
        }
    }
}
