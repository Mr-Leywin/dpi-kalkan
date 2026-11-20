using System;
using System.IO;
using System.Linq;
using System.Net;
using System.Text;

namespace DPIKalkan
{
    /// <summary>Rehber.html şablonuna hata kodu ve profil tablolarını ekler (derleme sırasında).</summary>
    public static class Guide
    {
        static string H(string s) => WebUtility.HtmlEncode(s ?? "");

        public static string ErrorsHtml()
        {
            var sb = new StringBuilder();
            foreach (var cat in ErrorCatalog.Categories)
            {
                sb.AppendLine("<h3>" + H(cat) + "</h3>");
                foreach (var e in ErrorCatalog.All.Where(x => x.Category == cat))
                {
                    string sev = e.Severity == Severity.Error ? "err" : e.Severity == Severity.Warning ? "warn" : "info";
                    sb.AppendLine("<details class=\"code " + sev + "\" id=\"" + e.Code + "\"><summary><span class=\"badge\">" + e.Code + "</span> " + H(e.Title) + "</summary>");
                    sb.AppendLine("<p>" + H(e.Summary) + "</p>");
                    if (e.Causes.Length > 0)
                        sb.AppendLine("<p class=\"lbl\">Olası nedenler</p><ul>" + string.Concat(e.Causes.Select(c => "<li>" + H(c) + "</li>")) + "</ul>");
                    if (e.Steps.Length > 0)
                        sb.AppendLine("<p class=\"lbl\">Çözüm</p><ol>" + string.Concat(e.Steps.Select(c => "<li>" + H(c) + "</li>")) + "</ol>");
                    if (e.Fix != FixKind.None)
                        sb.AppendLine("<p class=\"fix\">Uygulamada tek tıkla: <b>" + H(ErrorInfo.FixLabel(e.Fix)) + "</b>" +
                                      (e.Fix2 != FixKind.None ? " · <b>" + H(ErrorInfo.FixLabel(e.Fix2)) + "</b>" : "") + "</p>");
                    sb.AppendLine("</details>");
                }
            }
            return sb.ToString();
        }

        public static string ProfilesHtml()
        {
            var sb = new StringBuilder("<table><thead><tr><th>#</th><th>Yöntem</th><th>DNS</th><th>Açıklama</th></tr></thead><tbody>\n");
            int i = 0;
            foreach (var p in Profiles.BuiltIns)
            {
                i++;
                sb.AppendLine("<tr" + (p.UsesDnsRedirect ? "" : " class=\"nodns\"") + "><td>" + i.ToString("00") + "</td><td><b>" + H(p.Name) + "</b><br><code>" +
                              H(p.BuildArgs(null)) + "</code></td><td>" + H(p.DnsLabel) + "</td><td>" + H(p.Description) + "</td></tr>");
            }
            sb.AppendLine("</tbody></table>");
            return sb.ToString();
        }

        public static void Build(string template, string output)
        {
            string html = File.ReadAllText(template, Encoding.UTF8)
                .Replace("<!--SURUM-->", App.Version)
                .Replace("<!--HATA_SAYISI-->", ErrorCatalog.All.Count.ToString())
                .Replace("<!--PROFIL_SAYISI-->", Profiles.BuiltIns.Count.ToString())
                .Replace("<!--HATA_KODLARI-->", ErrorsHtml())
                .Replace("<!--PROFILLER-->", ProfilesHtml());
            File.WriteAllText(output, html, new UTF8Encoding(true));
        }
    }
}
