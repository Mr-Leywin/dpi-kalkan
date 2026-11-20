using System;
using System.Drawing;
using System.Linq;
using System.Windows.Forms;
using static DPIKalkan.Theme;

namespace DPIKalkan
{
    sealed class SitesPage : PageBase
    {
        public override string Key => "sites";

        readonly ToggleRow targeted;
        readonly ListBox list;
        readonly TextBox input;
        readonly Label pending, count;
        readonly FlatBtn apply;

        public SitesPage()
        {
            Header("Hedef Siteler", "Atlatma yöntemlerinin hangi sitelere uygulanacağını belirleyin.");

            var top = AddCard(28, 76, 764, 84);
            targeted = new ToggleRow("Hedefli mod (yalnızca listedeki sitelere uygula)",
                "Açıkken diğer sitelerin trafiğine hiç dokunulmaz; daha güvenli ve hızlıdır. Kapalıyken tüm HTTPS trafiğine uygulanır.",
                D(744)) { Location = new Point(D(10), D(10)), Divider = false };
            top.Add(targeted);
            targeted.Switch.SetSilently(App.Settings.TargetedMode);
            targeted.Switch.CheckedChanged += (s, e) =>
            {
                App.Settings.TargetedMode = targeted.Switch.Checked;
                App.Save();
                App.Log("Hedefli mod " + (App.Settings.TargetedMode ? "açıldı." : "kapatıldı."), LogLevel.Info);
                App.RaiseStatus();
                _ = Actions.RestartIfActiveAsync();
            };

            var card = AddCard(28, 174, 764, 432);
            card.Add(UIx.L("SİTE LİSTESİ", Caption, Muted, 20, 16, 200, 18));
            count = card.Add(UIx.L("", Small, Muted, 300, 16, 180, 18, ContentAlignment.TopRight));
            list = Theme.Style(new ListBox
            {
                Bounds = R(20, 40, 460, 372), Font = Body, IntegralHeight = false,
                BorderStyle = BorderStyle.FixedSingle, SelectionMode = SelectionMode.MultiExtended,
            });
            card.Add(list);
            list.KeyDown += (s, e) => { if (e.KeyCode == Keys.Delete) RemoveSelected(); };

            card.Add(UIx.L("Site ekle", Bold, Theme.Text, 500, 40, 240, 22));
            input = Theme.Style(new TextBox { Bounds = R(500, 66, 244, 26), Font = Body });
            card.Add(input);
            input.KeyDown += (s, e) => { if (e.KeyCode == Keys.Enter) { e.SuppressKeyPress = true; AddSite(); } };
            var bAdd = card.Add(new FlatBtn { Text = "Ekle", Glyph = GAdd, Kind = FlatBtn.Kinds.Primary, Bounds = R(500, 100, 244, 36) });
            bAdd.Click += (s, e) => AddSite();
            var bDel = card.Add(new FlatBtn { Text = "Seçilenleri sil", Glyph = GDelete, Kind = FlatBtn.Kinds.Secondary, Bounds = R(500, 144, 244, 36) });
            bDel.Click += (s, e) => RemoveSelected();
            var bDiscord = card.Add(new FlatBtn { Text = "Hazır liste ekle", Glyph = GChevron, Kind = FlatBtn.Kinds.Secondary, Bounds = R(500, 188, 244, 36) });
            bDiscord.Click += (s, e) =>
            {
                var m = new ContextMenuStrip { Font = Body, ShowImageMargin = false };
                foreach (var (name, domains) in Presets)
                {
                    var list = domains;
                    m.Items.Add(name + " (" + domains.Length + " adres)", null, (s2, e2) =>
                    {
                        foreach (var d in list)
                            if (!App.Settings.TargetDomains.Contains(d)) App.Settings.TargetDomains.Add(d);
                        Changed();
                    });
                }
                m.Closed += (s2, e2) => BeginInvoke(new Action(m.Dispose));
                m.Show(bDiscord, new Point(0, bDiscord.Height));
            };
            card.Add(UIx.L("Alt alan adları otomatik dahildir: 'discordapp.com' yazınca 'cdn.discordapp.com' da kapsanır. Adresi tarayıcıdan kopyalayıp yapıştırabilirsiniz.",
                Small, Muted, 500, 236, 244, 70));

            pending = card.Add(UIx.L("Değişikliklerin geçerli olması için koruma yeniden başlatılmalı.", Small, AmberText, 500, 310, 244, 36));
            apply = card.Add(new FlatBtn { Text = "Şimdi uygula", Glyph = GRefresh, Kind = FlatBtn.Kinds.Warning, Bounds = R(500, 350, 244, 36) });
            apply.Click += async (s, e) => { pending.Visible = apply.Visible = false; await Actions.StartAsync(); };
            pending.Visible = apply.Visible = false;
        }

        /// <summary>Türkiye'de erişim engeli bulunan bilinen servislerin alan adları.</summary>
        static readonly (string Name, string[] Domains)[] Presets =
        {
            ("Discord", AppSettings.DiscordDomains),
            ("Roblox", new[] { "roblox.com", "rbxcdn.com", "rbx.com", "robloxlabs.com" }),
            ("Wattpad", new[] { "wattpad.com" }),
        };

        public override void OnActivated()
        {
            targeted.Switch.SetSilently(App.Settings.TargetedMode);
            Reload();
        }

        void Reload()
        {
            list.BeginUpdate();
            list.Items.Clear();
            foreach (var d in App.Settings.TargetDomains) list.Items.Add(d);
            list.EndUpdate();
            count.Text = App.Settings.TargetDomains.Count + " site";
        }

        void Changed()
        {
            App.Settings.Normalize();
            App.Save();
            Reload();
            App.RaiseStatus();
            if (App.Settings.TargetedMode && App.Engine.IsActive) pending.Visible = apply.Visible = true;
        }

        void AddSite()
        {
            var parts = input.Text.Split(new[] { ' ', ',', ';', '\n', '\r', '\t' }, StringSplitOptions.RemoveEmptyEntries);
            int added = 0;
            foreach (var part in parts)
            {
                var d = Domains.Clean(part);
                if (d == null)
                {
                    MessageBox.Show(FindForm(), "'" + part + "' geçerli bir alan adı değil.\nÖrnek: discord.com", App.Name,
                        MessageBoxButtons.OK, MessageBoxIcon.Warning);
                    continue;
                }
                if (!App.Settings.TargetDomains.Contains(d)) { App.Settings.TargetDomains.Add(d); added++; }
            }
            input.Clear();
            if (added > 0) Changed();
        }

        void RemoveSelected()
        {
            var sel = list.SelectedItems.Cast<string>().ToList();
            if (sel.Count == 0) return;
            if (sel.Count >= App.Settings.TargetDomains.Count)
            {
                MessageBox.Show(FindForm(), "Listede en az bir site kalmalı.", App.Name, MessageBoxButtons.OK, MessageBoxIcon.Information);
                return;
            }
            App.Settings.TargetDomains.RemoveAll(sel.Contains);
            Changed();
        }
    }
}
