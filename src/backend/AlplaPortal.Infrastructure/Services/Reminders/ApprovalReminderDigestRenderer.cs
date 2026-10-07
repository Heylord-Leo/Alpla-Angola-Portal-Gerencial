using System.Globalization;
using System.Net;
using System.Text;

namespace AlplaPortal.Infrastructure.Services.Reminders;

public sealed record RenderedDigest(string Subject, string Headline, string BodyHtml, int ItemCount, int OverflowCount, bool Degraded);

/// <summary>
/// Pure HTML rendering of one approver's digest. Lists the oldest <c>MaxItemsPerDigest</c> units,
/// summarises the rest as "+N", and degrades to counts + link when the body exceeds
/// <c>MaxBodyBytes</c>. The body states its "as of" time: a digest is a snapshot and a unit may
/// have been decided between queueing and reading; authorization is enforced when the link is opened.
/// </summary>
public static class ApprovalReminderDigestRenderer
{
    public static RenderedDigest Render(
        string recipientName,
        IReadOnlyList<PendingApprovalUnit> units,
        DateTime nowUtc,
        TimeZoneInfo zone,
        ApprovalReminderOptions options,
        string frontendBaseUrl)
    {
        var ordered = units.OrderBy(u => u.StageEnteredAtUtc).ThenBy(u => u.RequestNumber).ToList();
        var total = ordered.Count;
        var cap = Math.Max(1, options.MaxItemsPerDigest);
        var listed = ordered.Take(cap).ToList();
        var overflow = Math.Max(0, total - listed.Count);

        var baseUrl = (frontendBaseUrl ?? string.Empty).TrimEnd('/');
        var centerUrl = baseUrl + (options.ApprovalsCenterPath.StartsWith('/') ? options.ApprovalsCenterPath : "/" + options.ApprovalsCenterPath);
        var asOfLocal = ApprovalReminderSchedule.ToLocal(nowUtc, zone);
        var asOfText = asOfLocal.ToString("dd/MM/yyyy HH:mm", CultureInfo.InvariantCulture);

        var areaCount = ordered.Count(u => u.Stage == PendingApprovalUnitQuery.StageArea);
        var finalCount = ordered.Count(u => u.Stage == PendingApprovalUnitQuery.StageFinal);

        var subject = total == 1
            ? $"Lembrete: 1 aprovação pendente há mais de {options.MinPendingAgeDays} dias"
            : $"Lembrete: {total} aprovações pendentes há mais de {options.MinPendingAgeDays} dias";
        var headline = "Aprovações pendentes aguardam a sua decisão";

        var body = BuildBody(recipientName, listed, overflow, total, areaCount, finalCount, asOfText, centerUrl, baseUrl, options);
        var degraded = false;
        if (Encoding.UTF8.GetByteCount(body) > Math.Max(4096, options.MaxBodyBytes))
        {
            degraded = true;
            body = BuildDegradedBody(recipientName, total, areaCount, finalCount, asOfText, centerUrl, options);
        }

        return new RenderedDigest(subject, headline, body, total, overflow, degraded);
    }

    private static string BuildBody(string recipientName, List<PendingApprovalUnit> listed, int overflow, int total,
        int areaCount, int finalCount, string asOfText, string centerUrl, string baseUrl, ApprovalReminderOptions options)
    {
        var sb = new StringBuilder();
        sb.Append("<p>Olá ").Append(WebUtility.HtmlEncode(recipientName)).Append(",</p>");
        sb.Append("<p>Existem <strong>").Append(total).Append("</strong> ")
          .Append(total == 1 ? "aprovação pendente" : "aprovações pendentes")
          .Append(" há mais de ").Append(options.MinPendingAgeDays).Append(" dias que aguardam a sua decisão")
          .Append(" (Área: ").Append(areaCount).Append(", Final: ").Append(finalCount).Append(").</p>");
        sb.Append("<p style=\"color:#666;font-size:12px\">Situação às ").Append(asOfText)
          .Append(" (hora de Luanda). Alguns pedidos podem já ter sido decididos por um aprovador alternativo; ")
          .Append("a autorização é verificada ao abrir cada ligação.</p>");

        sb.Append("<table style=\"border-collapse:collapse;width:100%;font-size:13px\">");
        sb.Append("<thead><tr style=\"background:#f3f4f6\">")
          .Append("<th style=\"text-align:left;padding:6px 8px\">Pedido</th>")
          .Append("<th style=\"text-align:left;padding:6px 8px\">Lote</th>")
          .Append("<th style=\"text-align:left;padding:6px 8px\">Etapa</th>")
          .Append("<th style=\"text-align:left;padding:6px 8px\">Em espera desde</th>")
          .Append("<th style=\"text-align:right;padding:6px 8px\">Dias</th>")
          .Append("</tr></thead><tbody>");

        foreach (var u in listed)
        {
            var url = $"{baseUrl}/requests/{u.RequestId}?mode=view";
            var since = u.StageEnteredAtUtc.HasValue
                ? u.StageEnteredAtUtc.Value.ToString("dd/MM/yyyy", CultureInfo.InvariantCulture)
                : "—";
            sb.Append("<tr style=\"border-bottom:1px solid #e5e7eb\">")
              .Append("<td style=\"padding:6px 8px\"><a href=\"").Append(WebUtility.HtmlEncode(url)).Append("\">")
              .Append(WebUtility.HtmlEncode(u.RequestNumber)).Append("</a></td>")
              .Append("<td style=\"padding:6px 8px\">").Append(u.BatchNumber.HasValue ? $"#{u.BatchNumber.Value}" : "—").Append("</td>")
              .Append("<td style=\"padding:6px 8px\">").Append(u.Stage == PendingApprovalUnitQuery.StageArea ? "Aprovação da Área" : "Aprovação Final").Append("</td>")
              .Append("<td style=\"padding:6px 8px\">").Append(since).Append("</td>")
              .Append("<td style=\"padding:6px 8px;text-align:right\">").Append(u.DaysPending).Append("</td>")
              .Append("</tr>");
        }
        sb.Append("</tbody></table>");

        if (overflow > 0)
        {
            sb.Append("<p>+ ").Append(overflow).Append(overflow == 1 ? " pedido adicional" : " pedidos adicionais")
              .Append(" não listado(s) aqui — consulte o <a href=\"").Append(WebUtility.HtmlEncode(centerUrl)).Append("\">Centro de Aprovações</a>.</p>");
        }

        sb.Append("<p><a href=\"").Append(WebUtility.HtmlEncode(centerUrl)).Append("\">Abrir Centro de Aprovações</a></p>");
        return sb.ToString();
    }

    private static string BuildDegradedBody(string recipientName, int total, int areaCount, int finalCount, string asOfText, string centerUrl, ApprovalReminderOptions options)
    {
        var sb = new StringBuilder();
        sb.Append("<p>Olá ").Append(WebUtility.HtmlEncode(recipientName)).Append(",</p>");
        sb.Append("<p>Existem <strong>").Append(total).Append("</strong> aprovações pendentes há mais de ")
          .Append(options.MinPendingAgeDays).Append(" dias que aguardam a sua decisão (Área: ").Append(areaCount)
          .Append(", Final: ").Append(finalCount).Append(").</p>");
        sb.Append("<p style=\"color:#666;font-size:12px\">Situação às ").Append(asOfText).Append(" (hora de Luanda). A lista detalhada excede o tamanho permitido para e-mail; consulte o portal.</p>");
        sb.Append("<p><a href=\"").Append(WebUtility.HtmlEncode(centerUrl)).Append("\">Abrir Centro de Aprovações</a></p>");
        return sb.ToString();
    }
}
