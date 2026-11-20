using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using System.Windows.Forms;

namespace DPIKalkan
{
    /// <summary>Arayüzün farklı yerlerinden (ana sayfa, tepsi, profiller) çağrılan ortak işlemler.</summary>
    static class Actions
    {
        public static bool Testing { get; private set; }
        public static bool Scanning { get; set; }
        static DateTime? discordHintFor;

        public static DateTime LastHeal = DateTime.MinValue;
        public static bool Healing { get; private set; }

        public static async Task StartAsync()
        {
            var p = App.SelectedProfile;
            bool ok = await Task.Run(() => App.Engine.Start(p));
            if (ok && App.Settings.TestAfterStart)
            {
                await Task.Delay(1500);
                var res = await QuickTestAsync(!App.Settings.AutoHeal);
                await MaybeHealAsync(res);
            }
        }

        static bool Broken(List<HostResult> res) =>
            res != null && res.Count > 0 && (res.Count(r => r.Ok) < res.Count || App.LastDnsOk == false);

        /// <summary>
        /// Koruma açıkken Discord'a erişilemiyorsa çalışan yöntemi kendiliğinden bulur (otomatik onarım).
        /// Tarama ya da onarım sonrası 10 dakika boyunca tekrar devreye girmez.
        /// </summary>
        public static async Task<bool> MaybeHealAsync(List<HostResult> res)
        {
            if (!App.Settings.AutoHeal || Scanning || Healing || App.Engine.Quiet) return false;
            if (App.Engine.State != EngineState.Running || !Broken(res)) return false;
            if ((DateTime.Now - LastHeal).TotalMinutes < 10) return false;
            Healing = true;
            LastHeal = DateTime.Now;
            try
            {
                int ok = res.Count(r => r.Ok);
                App.Log("Otomatik onarım: koruma açık ama " + (res.Count - ok) + "/" + res.Count + " sunucu açılmıyor" +
                        (App.LastDnsOk == false ? " (DNS yanlış)" : "") + "; çalışan yöntem aranıyor…", LogLevel.Warn);
                App.Notify("Otomatik onarım", "Discord'a erişilemiyor. Çalışan yöntem otomatik aranıyor (yaklaşık 30 saniye)…", ToolTipIcon.Info);
                string before = App.SelectedProfile.Name;
                var sc = new Scanner { Full = false, Hosts = App.Settings.TestHosts.ToList() };
                var best = await sc.RunAsync(System.Threading.CancellationToken.None);
                if (best != null)
                {
                    string msg = best.Name == before ? "Yöntem değişmedi (" + best.Name + "); bağlantı yeniden kuruldu."
                                                     : "Yeni yöntem: " + best.Name + " (" + before + " çalışmıyordu).";
                    App.Log("Otomatik onarım tamamlandı. " + msg, LogLevel.Success);
                    History.Add(new HistoryEntry { Time = DateTime.Now, Kind = "onarım", Profile = best.Name, Ok = 1, Total = 1, Running = true, Note = msg });
                    App.Notify("Sorun giderildi", msg + (DiscordHelper.IsRunning ? " Discord bağlanamıyorsa yeniden başlatın." : ""), ToolTipIcon.Info);
                    return true;
                }
                App.Log("Otomatik onarım çalışan bir yöntem bulamadı" + (sc.FailCode.Length > 0 ? " [" + sc.FailCode + "]" : "") + ".", LogLevel.Warn);
                History.Add(new HistoryEntry { Time = DateTime.Now, Kind = "onarım", Profile = before, Ok = 0, Total = 1, Running = true, Note = "Çalışan yöntem bulunamadı " + sc.FailCode });
                App.Notify("Otomatik onarım başarısız",
                    "Çalışan yöntem bulunamadı" + (sc.FailCode.Length > 0 ? " (" + sc.FailCode + " – " + ErrorCatalog.Get(sc.FailCode)?.Title + ")" : "") +
                    ". Ayrıntı için Sorun Giderme sayfasına bakın.", ToolTipIcon.Warning);
                return false;
            }
            finally
            {
                Healing = false;
                LastHeal = DateTime.Now;
                App.RaiseStatus();
            }
        }

        /// <summary>Bağlantı bekçisi: koruma açıksa sessizce test eder, sorun varsa onarır.</summary>
        public static async Task WatchCheckAsync(string reason)
        {
            if (App.Engine.State != EngineState.Running || Scanning || Testing || Healing || App.Engine.Quiet) return;
            App.Log("Bağlantı bekçisi: kontrol ediliyor (" + reason + ").", LogLevel.Debug);
            var res = await QuickTestAsync(!App.Settings.AutoHeal);
            await MaybeHealAsync(res);
        }

        public static Task StopAsync() => Task.Run(() => App.Engine.Stop());

        public static async Task ToggleAsync()
        {
            if (Scanning) return;
            var s = App.Engine.State;
            if (s == EngineState.Running || s == EngineState.Starting) await StopAsync();
            else if (s != EngineState.Stopping) await StartAsync();
        }

        public static async Task SelectProfileAsync(Profile p)
        {
            bool changed = p.Id != App.Settings.SelectedProfileId;
            App.Settings.SelectedProfileId = p.Id;
            App.Save();
            if (changed) App.Log("Profil seçildi: " + p.Name, LogLevel.Info);
            App.RaiseStatus();
            var s = App.Engine.State;
            if (s == EngineState.Running || s == EngineState.Starting || s == EngineState.Error)
                await StartAsync();
        }

        /// <summary>Çalışıyorsa korumayı yeni ayarlarla yeniden başlatır.</summary>
        public static async Task RestartIfActiveAsync()
        {
            if (App.Engine.IsActive) await StartAsync();
        }

        public static async Task<List<HostResult>> QuickTestAsync(bool notifyOnFail = false)
        {
            if (Testing) return App.LastTest;
            Testing = true;
            App.RaiseStatus();
            try
            {
                var hosts = App.Settings.TestHosts.ToList();
                var eng = App.Engine;
                bool running = eng.State == EngineState.Running;
                var prof = running ? eng.ActiveProfile : null;
                string main = Domains.HostOf(hosts.FirstOrDefault() ?? "discord.com");

                // DNS doğrulaması ve HTTPS testi paralel
                var dnsTask = Task.Run(() =>
                {
                    var (refIps, _) = NetTest.ReferenceLookup(main);
                    if (refIps.Count == 0) return (bool?)null;
                    var sys = NetTest.SystemLookup(main);
                    return sys.Count > 0 && NetTest.Overlap(sys, refIps);
                });
                var res = await NetTest.TestManyAsync(hosts);
                bool? dnsOk = await dnsTask;

                App.LastTest = res;
                App.LastTestTime = DateTime.Now;
                App.LastDnsOk = dnsOk;
                History.Add(new HistoryEntry
                {
                    Time = DateTime.Now, Kind = "test", Profile = (prof ?? App.SelectedProfile)?.Name ?? "", Running = running,
                    Ok = res.Count(r => r.Ok), Total = res.Count, DnsOk = dnsOk,
                    Ms = res.Any(r => r.Ok) ? (int)res.Where(r => r.Ok).Average(r => r.Ms) : 0,
                    Note = string.Join(", ", res.Where(r => !r.Ok).Select(r => r.Host + " " + r.Code)),
                });
                int ok = res.Count(r => r.Ok);
                App.Log("Bağlantı testi: " + ok + "/" + res.Count + " site erişilebilir" +
                        (dnsOk == true ? ", DNS doğru." : dnsOk == false ? ", DNS YANLIŞ." : "."),
                    ok == res.Count ? LogLevel.Success : ok == 0 ? LogLevel.Error : LogLevel.Warn);
                foreach (var r in res.Where(r => !r.Ok)) App.Log("   " + r.Host + ": " + r.Detail + " [" + r.Code + "]", LogLevel.Warn);

                // DNS sorunları
                if (dnsOk == false)
                {
                    if (running && prof != null && prof.UsesDnsRedirect) App.Problems.Report("DK-403", main);
                    else if (running) App.Problems.Report("DK-401", "Seçili profil DNS'i yönlendirmiyor.");
                }
                else if (dnsOk == true) { App.Problems.Clear("DK-403"); App.Problems.Clear("DK-401"); }

                // Erişim sorunları (yalnızca koruma açıkken anlamlı)
                var accessCodes = new[] { "DK-501", "DK-502", "DK-503", "DK-504", "DK-505", "DK-506" };
                foreach (var c in accessCodes) App.Problems.Clear(c);
                if (running && ok < res.Count)
                {
                    var fails = res.Where(r => !r.Ok).ToList();
                    string code;
                    if (App.Settings.TargetedMode && fails.Any(f => !Domains.Covered(f.Host, App.Settings.TargetDomains))) code = "DK-506";
                    else if (ok > 0) code = "DK-505";
                    else code = fails.GroupBy(f => f.Code).OrderByDescending(g => g.Count()).First().Key;
                    if (code != "DK-401") App.Problems.Report(code, string.Join(", ", fails.Select(f => f.Host)));
                }

                if (notifyOnFail && ok < res.Count && running && !eng.Quiet)
                    App.Notify("Erişim sorunu",
                        "Koruma açık ama " + (res.Count - ok) + " sunucu açılamadı. 'En iyi yöntemi bul' ile çalışan yöntemi otomatik bulabilirsiniz.",
                        ToolTipIcon.Warning);

                // Discord korumadan önce açıldıysa yeniden başlatmayı öner
                if (running && ok == res.Count && App.Settings.SuggestDiscordRestart && eng.StartedAt.HasValue && !eng.Quiet)
                {
                    if (DiscordHelper.StartedBefore(eng.StartedAt.Value))
                    {
                        App.Problems.Report("DK-901");
                        if (discordHintFor != eng.StartedAt)
                        {
                            discordHintFor = eng.StartedAt;
                            App.Notify("Discord'u yeniden başlatın",
                                "Discord korumadan önce açılmış. Bağlanamıyorsa ana sayfadaki 'Discord'u yeniden başlat' düğmesini kullanın.", ToolTipIcon.Info);
                        }
                    }
                    else App.Problems.Clear("DK-901");
                }
                return res;
            }
            finally
            {
                Testing = false;
                App.RaiseStatus();
            }
        }

        public static async Task<string> RestartDiscordAsync()
        {
            string msg = null;
            await Task.Run(() => DiscordHelper.Restart(out msg));
            return msg;
        }
    }
}
