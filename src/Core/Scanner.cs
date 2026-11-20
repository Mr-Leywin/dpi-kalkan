using System;
using System.Collections.Generic;
using System.Linq;
using System.Net;
using System.Threading;
using System.Threading.Tasks;

namespace DPIKalkan
{
    public enum ScanLineKind { Info, Step, Good, Warn, Bad, Muted }

    public sealed class ScanRow
    {
        public Profile P;
        public string Status = "Bekliyor";
        public int DnsOk, SitesOk, Total, Ms;
        public bool Passed, Skipped, Tested;
        public string Note = "";
        public List<HostResult> Fails = new List<HostResult>();
    }

    /// <summary>
    /// "En iyi yöntemi bul" taraması. Her profil için: koruma başlatılır, DNS önbelleği temizlenir,
    /// sistem DNS yanıtı bağımsız sunucularla karşılaştırılır ve tüm Discord sunucularına iki tur
    /// boyunca yeni bağlantılarla HTTPS isteği atılır. Ancak hepsi geçerse profil "çalışıyor" sayılır.
    /// </summary>
    public sealed class Scanner
    {
        public event Action<string, ScanLineKind> Line;
        public event Action<int, int, string> Progress;

        public bool Full;
        public List<string> Hosts;
        public readonly List<ScanRow> Rows = new List<ScanRow>();
        public Profile Best;
        public bool DnsBlocked, SniBlocked, IpBlocked;
        public string FailCode = "";
        public string Summary = "";

        readonly Dictionary<string, List<IPAddress>> refs = new Dictionary<string, List<IPAddress>>(StringComparer.OrdinalIgnoreCase);
        readonly Dictionary<DnsChoice, (bool Ok, int Ms)> providers = new Dictionary<DnsChoice, (bool, int)>();

        void Emit(string text, ScanLineKind kind = ScanLineKind.Info)
        {
            App.Log("[Tarama] " + text.Trim(), kind == ScanLineKind.Bad ? LogLevel.Warn : kind == ScanLineKind.Good ? LogLevel.Success : LogLevel.Info);
            App.Post(() => Line?.Invoke(text, kind));
        }

        void Prog(int done, int total, string label) => App.Post(() => Progress?.Invoke(done, total, label));

        public async Task<Profile> RunAsync(CancellationToken ct)
        {
            var eng = App.Engine;
            string originalId = App.Settings.SelectedProfileId;
            bool wasActive = eng.IsActive;
            var hosts = (Hosts ?? App.Settings.TestHosts).Select(Domains.HostOf).Distinct(StringComparer.OrdinalIgnoreCase).ToList();
            string main = hosts.FirstOrDefault() ?? "discord.com";
            Actions.Scanning = true;
            eng.Quiet = true;
            App.RaiseStatus();
            bool aborted = false;
            try
            {
                Prog(0, 1, "Hazırlanıyor…");
                Emit("Koruma tarama süresince geçici olarak durduruluyor.", ScanLineKind.Muted);
                await Task.Run(() => eng.Stop(true));
                await Task.Delay(700);

                // ---------------------------------------------------- 1) internet
                Emit("1/4  İnternet bağlantısı kontrol ediliyor…", ScanLineKind.Step);
                int inetMs = 0;
                bool inet = await Task.Run(() => NetTest.InternetOk(out inetMs));
                if (!inet)
                {
                    FailCode = "DK-801";
                    Emit("   ✗ Hiçbir genel sunucuya bağlanılamıyor. Sorun engelden değil, internet bağlantınızdan kaynaklanıyor. [DK-801]", ScanLineKind.Bad);
                    throw new ScanAbort();
                }
                Emit("   ✓ İnternet çalışıyor (" + inetMs + " ms)", ScanLineKind.Good);
                ct.ThrowIfCancellationRequested();

                // ---------------------------------------------------- 2) DNS
                Emit("2/4  DNS engeli ve DNS sunucuları kontrol ediliyor…", ScanLineKind.Step);
                Native.FlushDns();
                var refTasks = hosts.Select(h => Task.Run(() => (H: h, R: NetTest.ReferenceLookup(h)))).ToList();
                var provTasks = DnsProviders.All.Select(p => Task.Run(() => (P: p, R: NetTest.TimedQuery(main, p.V4, p.V4Port, 2500)))).ToList();
                var sysTask = Task.Run(() => NetTest.SystemLookup(main));
                await Task.WhenAll(refTasks.Cast<Task>().Concat(provTasks).Concat(new Task[] { sysTask }));
                foreach (var t in refTasks) refs[t.Result.H] = t.Result.R.Ips;
                var mainRef = refs.TryGetValue(main, out var mr) ? mr : new List<IPAddress>();
                var sys = sysTask.Result;
                foreach (var t in provTasks)
                {
                    var (p, r) = t.Result;
                    bool correct = r.Ok && r.Ips.Count > 0 && (mainRef.Count == 0 || NetTest.Overlap(r.Ips, mainRef));
                    providers[p.Id] = (correct, r.Ms);
                }
                if (mainRef.Count == 0)
                {
                    Emit("   ! Bağımsız DNS sunucularının hiçbiri yanıt vermedi; DNS doğrulaması yapılamayacak.", ScanLineKind.Warn);
                    DnsBlocked = sys.Count == 0;
                }
                else
                {
                    DnsBlocked = sys.Count == 0 || !NetTest.Overlap(sys, mainRef);
                    Emit(DnsBlocked
                        ? "   ✗ DNS ENGELİ VAR: servis sağlayıcının DNS'i " + main + " için " + (sys.Count == 0 ? "yanıt vermiyor" : "sahte adres (" + sys[0] + ") döndürüyor") + ". DNS yönlendirmesi zorunlu."
                        : "   ✓ Sistem DNS'i doğru yanıt veriyor (" + (sys.FirstOrDefault()?.ToString() ?? "") + ")",
                        DnsBlocked ? ScanLineKind.Warn : ScanLineKind.Good);
                }
                var provText = string.Join(" · ", DnsProviders.All.Select(p =>
                    p.Name + " " + (providers[p.Id].Ok ? providers[p.Id].Ms + " ms" : "✗")));
                Emit("   DNS sunucuları: " + provText, ScanLineKind.Muted);
                ct.ThrowIfCancellationRequested();

                // ---------------------------------------------------- 3) engel türü
                Emit("3/4  Engel türü belirleniyor (koruma kapalıyken doğru adrese bağlantı)…", ScanLineKind.Step);
                var target = mainRef.FirstOrDefault() ?? sys.FirstOrDefault();
                if (target != null)
                {
                    var probe = await Task.Run(() => NetTest.Probe(main, target));
                    IpBlocked = !probe.TcpOk;
                    SniBlocked = probe.TcpOk && (probe.Kind == ProbeKind.Reset || probe.Kind == ProbeKind.Timeout);
                    if (IpBlocked) Emit("   ✗ " + target + " adresine TCP bağlantısı bile kurulamadı (IP engeli olabilir).", ScanLineKind.Warn);
                    else if (SniBlocked) Emit("   ✗ DPI (SNI) ENGELİ VAR: " + probe.Describe() + ". DPI yöntemi zorunlu.", ScanLineKind.Warn);
                    else if (probe.TlsOk) Emit("   ✓ Site adına göre engelleme yok; yalnızca DNS yönlendirmesi yeterli olabilir.", ScanLineKind.Good);
                    else Emit("   ! " + probe.Describe(), ScanLineKind.Warn);
                }

                // ---------------------------------------------------- 4) profiller
                var cands = OrderCandidates(originalId);
                Emit("4/4  Yöntemler deneniyor (" + (Full ? "tümü" : "çalışan ilk yöntemde durulacak") + ")…", ScanLineKind.Step);
                int idx = 0;
                foreach (var p in cands)
                {
                    if (ct.IsCancellationRequested) break;
                    idx++;
                    var row = new ScanRow { P = p, Total = hosts.Count };
                    Rows.Add(row);
                    Prog(idx - 1, cands.Count, p.Name);

                    string skip = SkipReason(p);
                    if (skip == null && failedDpi.Contains(DpiSignature(p)))
                        skip = "aynı DPI yöntemi az önce başarısız oldu; yalnızca DNS'i farklı";
                    if (skip != null && !(Full && skip.StartsWith("DNS engeli")))
                    {
                        row.Skipped = true;
                        row.Status = "Atlandı";
                        row.Note = skip;
                        Emit("   – " + p.Name + ": atlandı (" + skip + ")", ScanLineKind.Muted);
                        continue;
                    }

                    await TryProfile(p, row, hosts);
                    if (row.Status == "Başlatılamadı")
                    {
                        Emit("   ✗ " + p.Name + ": başlatılamadı [" + eng.LastErrorCode + "] " + eng.LastError, ScanLineKind.Bad);
                        if (IsFatal(eng.LastErrorCode))
                        {
                            FailCode = eng.LastErrorCode;
                            Emit("   Tarama durduruldu: sürücü/dosya hatası tüm yöntemleri etkiler. Hata kodu " + FailCode + " için Sorun Giderme sayfasına bakın.", ScanLineKind.Bad);
                            aborted = true;
                            break;
                        }
                        continue;
                    }
                    string detail = "DNS " + row.DnsOk + "/" + row.Total + " · Siteler " + row.SitesOk + "/" + row.Total + (row.SitesOk > 0 ? " · " + row.Ms + " ms" : "");
                    if (row.Passed)
                    {
                        Emit("   ✓ " + p.Name + ": ÇALIŞIYOR (" + detail + ", 2 tur)", ScanLineKind.Good);
                        if (!Full) break;
                    }
                    else
                    {
                        string why = row.DnsOk < row.Total ? "DNS yanıtı yanlış" : row.Note;
                        Emit("   ✗ " + p.Name + ": çalışmıyor (" + detail + (why.Length > 0 ? " – " + why : "") + ")", row.SitesOk > 0 ? ScanLineKind.Warn : ScanLineKind.Bad);
                        // DNS doğru ama siteler açılmıyorsa sorun DPI yönteminde: aynı yöntemin diğer DNS'li kopyalarını deneme
                        if (row.DnsOk == row.Total && row.SitesOk < row.Total) failedDpi.Add(DpiSignature(p));
                    }
                }
                Prog(cands.Count, cands.Count, "Bitti");
            }
            catch (ScanAbort) { aborted = true; }
            catch (OperationCanceledException) { }
            catch (Exception ex)
            {
                Emit("Hata: " + ex.Message, ScanLineKind.Bad);
                aborted = true;
            }
            finally
            {
                await Task.Run(() => eng.Stop(true));
                eng.Quiet = false;
            }

            try
            {
                var passed = Rows.Where(r => r.Passed).ToList();
                if (passed.Count > 0)
                {
                    Best = Full ? passed.OrderBy(r => r.Ms).ThenBy(r => r.P.Priority).First().P : passed.First().P;
                    var br = Rows.First(r => r.P == Best);
                    Summary = "En iyi yöntem: " + Best.Name + " (" + br.SitesOk + "/" + br.Total + " sunucu, " + br.Ms + " ms)";
                    Emit("");
                    Emit("✓ " + Summary, ScanLineKind.Good);
                    App.Settings.SelectedProfileId = Best.Id;
                    App.Settings.LastScanTime = DateTime.Now;
                    App.Settings.LastScanResult = Best.Name;
                    App.Save();
                    App.Log("Profil seçildi: " + Best.Name, LogLevel.Info);
                    await Actions.StartAsync();
                    if (App.Engine.State == EngineState.Running)
                        Emit("Profil etkinleştirildi ve koruma başlatıldı.", ScanLineKind.Muted);
                }
                else
                {
                    if (!aborted && !ct.IsCancellationRequested)
                    {
                        FailCode = GuessFailCode();
                        Summary = "Hiçbir yöntem tüm Discord sunucularına erişemedi.";
                        Emit("");
                        Emit("✗ " + Summary + (FailCode.Length > 0 ? " Olası neden: " + FailCode + " – " + ErrorCatalog.Get(FailCode)?.Title : ""), ScanLineKind.Bad);
                    }
                    else if (ct.IsCancellationRequested) { Summary = "Tarama iptal edildi."; Emit(Summary, ScanLineKind.Warn); }
                    App.Settings.SelectedProfileId = originalId;
                    App.Settings.LastScanTime = DateTime.Now;
                    App.Settings.LastScanResult = "";
                    App.Save();
                    if (wasActive || App.Settings.AutoStartProtection) await Actions.StartAsync();
                    if (!string.IsNullOrEmpty(FailCode)) App.Problems.Report(FailCode, "Yöntem taraması");
                }
            }
            finally
            {
                Actions.LastHeal = DateTime.Now;   // tarama sonrası otomatik onarım hemen tekrar devreye girmesin
                History.Add(new HistoryEntry
                {
                    Time = DateTime.Now, Kind = "tarama", Profile = Best?.Name ?? "", Ok = Best != null ? 1 : 0, Total = 1, Running = Best != null,
                    Note = (Summary.Length > 0 ? Summary : "") + " · " + Rows.Count(r => r.Tested) + " yöntem denendi",
                });
                Actions.Scanning = false;
                App.RaiseStatus();
            }
            return Best;
        }

        sealed class ScanAbort : Exception { }

        readonly HashSet<string> failedDpi = new HashSet<string>();

        /// <summary>Profilin DNS dışındaki (DPI) parametreleri: aynı imzalı profiller aynı şekilde davranır.</summary>
        internal static string DpiSignature(Profile p)
        {
            if (p.UseRaw)
            {
                var t = (p.RawArgs ?? "").Split(new[] { ' ', '\t', '\r', '\n' }, StringSplitOptions.RemoveEmptyEntries).ToList();
                var keep = new List<string>();
                for (int i = 0; i < t.Count; i++)
                {
                    if (t[i].StartsWith("--dns")) { if (t[i] != "--dns-verb") i++; continue; }
                    keep.Add(t[i]);
                }
                return "raw:" + string.Join(" ", keep);
            }
            var c = p.CloneAsCustom();
            c.Dns = DnsChoice.None;
            c.CustomDns4 = c.CustomDns6 = "";
            return c.BuildArgs(null);
        }

        static bool IsFatal(string code) =>
            code == "DK-102" || code == "DK-201" || code == "DK-202" || code == "DK-203" || code == "DK-204" || code == "DK-205" || code == "DK-206";

        List<Profile> OrderCandidates(string currentId)
        {
            var all = App.AllProfiles.ToList();
            var rec = all.Where(p => p.BuiltIn && p.Group == Profiles.GroupRecommended)
                         .OrderBy(p => providers.TryGetValue(p.Dns, out var v) && v.Ok ? v.Ms : 99999).ThenBy(p => p.Priority);
            var alt = all.Where(p => p.BuiltIn && p.Group == Profiles.GroupAlt).OrderBy(p => p.Priority);
            var custom = all.Where(p => !p.BuiltIn);
            var noDns = all.Where(p => p.BuiltIn && p.Group == Profiles.GroupNoDns).OrderBy(p => p.Priority);
            var list = rec.Concat(alt).Concat(custom).Concat(noDns).ToList();
            if (!Full)
            {
                // Hızlı taramada: şu an seçili profil çalışıyorsa korunur (kararlılık)
                var cur = list.FirstOrDefault(p => p.Id == currentId);
                if (cur != null && (cur.UsesDnsRedirect || !DnsBlocked)) { list.Remove(cur); list.Insert(0, cur); }
            }
            return list;
        }

        string SkipReason(Profile p)
        {
            if (p.UsesDnsRedirect && !p.UseRaw)
            {
                if (p.Dns != DnsChoice.Custom && providers.TryGetValue(p.Dns, out var v) && !v.Ok)
                    return "DNS sunucusu yanıt vermiyor ya da yanlış yanıt veriyor";
            }
            if (DnsBlocked && !p.UsesDnsRedirect) return "DNS engeli var; bu profil DNS'i yönlendirmiyor";
            return null;
        }

        async Task TryProfile(Profile p, ScanRow row, List<string> hosts)
        {
            var eng = App.Engine;
            row.Tested = true;
            row.Status = "Deneniyor";
            bool started = await Task.Run(() => eng.Start(p, forceApp: true));
            if (!started) { row.Status = "Başlatılamadı"; row.Note = eng.LastErrorCode; return; }
            await Task.Delay(900);
            Native.FlushDns();

            // a) DNS doğrulaması: sistem çözümleyicisi doğru adresi veriyor mu?
            var dnsTasks = hosts.Select(h => Task.Run(() =>
            {
                var s = NetTest.SystemLookup(h);
                var r = refs.TryGetValue(h, out var x) ? x : new List<IPAddress>();
                return r.Count == 0 ? s.Count > 0 : s.Count > 0 && NetTest.Overlap(s, r);
            })).ToList();
            await Task.WhenAll(dnsTasks);
            row.DnsOk = dnsTasks.Count(t => t.Result);

            // b) iki tur HTTPS (her istek yeni bağlantı, .NET DNS önbelleği kapalı)
            var r1 = await NetTest.TestManyAsync(hosts, 6000);
            int ok1 = r1.Count(x => x.Ok);
            int ok2 = ok1;
            var all = new List<HostResult>(r1);
            if (ok1 == hosts.Count && row.DnsOk == hosts.Count)
            {
                await Task.Delay(1200);
                Native.FlushDns();
                var r2 = await NetTest.TestManyAsync(hosts, 6000);
                ok2 = r2.Count(x => x.Ok);
                all.AddRange(r2);
            }
            row.SitesOk = Math.Min(ok1, ok2);
            var good = all.Where(x => x.Ok).ToList();
            row.Ms = good.Count > 0 ? (int)good.Average(x => x.Ms) : 0;
            row.Fails = all.Where(x => !x.Ok).ToList();
            row.Passed = row.DnsOk == hosts.Count && row.SitesOk == hosts.Count;
            row.Status = row.Passed ? "Çalışıyor" : row.SitesOk > 0 ? "Kısmen" : "Çalışmıyor";
            if (!row.Passed && row.Fails.Count > 0)
                row.Note = row.Fails.GroupBy(f => f.Detail).OrderByDescending(g => g.Count()).First().Key;
        }

        string GuessFailCode()
        {
            var tested = Rows.Where(r => r.Tested && r.Status != "Başlatılamadı").ToList();
            if (tested.Count == 0) return Rows.All(r => r.Skipped) ? "DK-402" : "";
            if (IpBlocked) return "DK-503";
            if (tested.All(r => r.DnsOk == 0)) return "DK-403";
            var codes = tested.SelectMany(r => r.Fails).Select(f => f.Code).Where(c => !string.IsNullOrEmpty(c)).ToList();
            if (codes.Count == 0) return "DK-505";
            return codes.GroupBy(c => c).OrderByDescending(g => g.Count()).First().Key;
        }
    }
}
