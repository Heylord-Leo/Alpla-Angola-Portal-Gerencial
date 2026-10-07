using System;
using System.Collections.Generic;
using System.Linq;
using AlplaPortal.Infrastructure.Services.Reminders;
using Xunit;

namespace AlplaPortal.Application.Tests.Services.Reminders;

public class ApprovalReminderDigestRendererTests
{
    private static readonly DateTime Now = new(2026, 10, 7, 7, 0, 0, DateTimeKind.Utc);
    private static readonly TimeZoneInfo Zone = ApprovalReminderSchedule.ResolveZone("W. Central Africa Standard Time");

    private static List<PendingApprovalUnit> Units(int n) => Enumerable.Range(1, n).Select(i => new PendingApprovalUnit
    {
        RequestId = Guid.NewGuid(), RequestNumber = $"REQ-{i:000}", Stage = i % 2 == 0 ? PendingApprovalUnitQuery.StageFinal : PendingApprovalUnitQuery.StageArea,
        BatchNumber = i % 3 == 0 ? null : 1, StageEnteredAtUtc = Now.AddDays(-(3 + i)), DaysPending = 3 + i
    }).ToList();

    [Fact]
    public void Lists_oldest_first_caps_the_list_and_summarises_the_overflow()
    {
        var opts = new ApprovalReminderOptions { MaxItemsPerDigest = 50 };
        var units = Units(60);
        var r = ApprovalReminderDigestRenderer.Render("Ana", units, Now, Zone, opts, "https://portal.test/");

        Assert.Equal(60, r.ItemCount);
        Assert.Equal(10, r.OverflowCount);
        Assert.False(r.Degraded);
        Assert.Contains("60</strong> aprovações pendentes há mais de 3 dias", r.BodyHtml);
        Assert.Contains("+ 10 pedidos adicionais", r.BodyHtml);
        Assert.Contains("Situação às 07/10/2026 08:00 (hora de Luanda)", r.BodyHtml); // as-of header, local time
        Assert.Contains("a autorização é verificada ao abrir cada ligação", r.BodyHtml);
        Assert.Contains("https://portal.test/approvals", r.BodyHtml);

        var oldest = units.OrderBy(u => u.StageEnteredAtUtc).First();
        var newest = units.OrderBy(u => u.StageEnteredAtUtc).Last();
        Assert.Contains($"https://portal.test/requests/{oldest.RequestId}?mode=view", r.BodyHtml);
        Assert.DoesNotContain(newest.RequestNumber, r.BodyHtml); // beyond the cap → only in the "+N"
        Assert.Equal("Lembrete: 60 aprovações pendentes há mais de 3 dias", r.Subject);
    }

    [Fact]
    public void Single_unit_uses_singular_wording_and_no_overflow_line()
    {
        var r = ApprovalReminderDigestRenderer.Render("Ana", Units(1), Now, Zone, new ApprovalReminderOptions(), "https://portal.test");
        Assert.Equal("Lembrete: 1 aprovação pendente há mais de 3 dias", r.Subject);
        Assert.Contains("1</strong> aprovação pendente", r.BodyHtml);
        Assert.DoesNotContain("adicional", r.BodyHtml);
        Assert.Equal(0, r.OverflowCount);
    }

    [Fact]
    public void Body_above_the_byte_cap_degrades_to_counts_plus_link()
    {
        var opts = new ApprovalReminderOptions { MaxItemsPerDigest = 500, MaxBodyBytes = 4096 };
        var r = ApprovalReminderDigestRenderer.Render("Ana", Units(300), Now, Zone, opts, "https://portal.test");
        Assert.True(r.Degraded);
        Assert.Equal(300, r.ItemCount);
        Assert.Contains("excede o tamanho permitido", r.BodyHtml);
        Assert.DoesNotContain("<table", r.BodyHtml);
        Assert.Contains("https://portal.test/approvals", r.BodyHtml);
    }

    [Fact]
    public void Recipient_name_and_request_numbers_are_html_encoded()
    {
        var units = new List<PendingApprovalUnit> { new() { RequestId = Guid.NewGuid(), RequestNumber = "REQ<1>&", Stage = PendingApprovalUnitQuery.StageArea, StageEnteredAtUtc = Now.AddDays(-4), DaysPending = 4 } };
        var r = ApprovalReminderDigestRenderer.Render("<b>Ana</b>", units, Now, Zone, new ApprovalReminderOptions(), "https://portal.test");
        Assert.Contains("&lt;b&gt;Ana&lt;/b&gt;", r.BodyHtml);
        Assert.Contains("REQ&lt;1&gt;&amp;", r.BodyHtml);
    }
}
