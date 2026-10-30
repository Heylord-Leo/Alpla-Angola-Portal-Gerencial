using System;
using System.Collections.Generic;
using AlplaPortal.Domain.Services;
using Xunit;

namespace AlplaPortal.Application.Tests.Services.Requests;

/// <summary>v2.249.5 — the pure rule behind "registered P.O. evidence is never deleted" (docs/PO_EVIDENCE_AND_QUOTATION_LIFECYCLE_v2.249.5.md §2).</summary>
public class PoEvidenceProtectionTests
{
    private static readonly Guid Group = Guid.Parse("4ac0fd69-0000-0000-0000-000000000001");
    private static readonly Guid OtherGroup = Guid.Parse("7b1c2d3e-0000-0000-0000-000000000002");
    private static readonly DateTime T0 = new(2026, 10, 29, 10, 0, 0, DateTimeKind.Utc);

    private static PoEvidenceProtection.FileFacts File(Guid? group = null, DateTime? uploaded = null, DateTime? voided = null, DateTime? superseded = null)
        => new(Guid.NewGuid(), group ?? Group, uploaded ?? T0, voided, superseded);

    [Fact]
    public void Voided_file_is_protected()
    {
        var v = PoEvidenceProtection.Evaluate(File(voided: T0.AddHours(1)), null, Array.Empty<PoEvidenceProtection.Registration>());
        Assert.True(v.IsProtected);
        Assert.Equal(PoEvidenceProtection.Reasons.Voided, v.ReasonCode);
    }

    [Fact]
    public void Superseded_file_is_protected_even_when_not_the_pointer()
    {
        var v = PoEvidenceProtection.Evaluate(File(superseded: T0.AddDays(1)), Guid.NewGuid(), Array.Empty<PoEvidenceProtection.Registration>());
        Assert.True(v.IsProtected);
        Assert.Equal(PoEvidenceProtection.Reasons.Superseded, v.ReasonCode);
    }

    [Fact]
    public void Current_pointer_is_protected_whatever_the_group_status()
    {
        var f = File();
        var v = PoEvidenceProtection.Evaluate(f, f.Id, Array.Empty<PoEvidenceProtection.Registration>());
        Assert.True(v.IsProtected);
        Assert.Equal(PoEvidenceProtection.Reasons.Current, v.ReasonCode);
        Assert.Contains("substituído", v.Detail);
    }

    [Fact]
    public void Legacy_group_without_pointer_registration_after_upload_protects_the_file()
    {
        // REQ-628 shape before v2.249.4: no CurrentPoAttachmentId, REGISTER_PO row of the same group one minute after the upload.
        var f = File(uploaded: T0);
        var regs = new[] { new PoEvidenceProtection.Registration(T0.AddMinutes(1), Group) };
        var v = PoEvidenceProtection.Evaluate(f, null, regs);
        Assert.True(v.IsProtected);
        Assert.Equal(PoEvidenceProtection.Reasons.RegisteredAfterUpload, v.ReasonCode);
    }

    [Fact]
    public void Registration_whose_group_is_unknown_counts_for_every_group()
    {
        var f = File(uploaded: T0);
        var regs = new[] { new PoEvidenceProtection.Registration(T0.AddMinutes(1), null) };
        Assert.True(PoEvidenceProtection.Evaluate(f, null, regs).IsProtected);
    }

    [Fact]
    public void Registration_of_another_group_does_not_protect_this_file()
    {
        var f = File(uploaded: T0);
        var regs = new[] { new PoEvidenceProtection.Registration(T0.AddMinutes(1), OtherGroup) };
        Assert.False(PoEvidenceProtection.Evaluate(f, null, regs).IsProtected);
    }

    [Fact]
    public void File_without_group_ownership_is_protected_by_any_registration_after_its_upload()
    {
        var f = File(group: null, uploaded: T0) with { RequestPoGroupId = null };
        var regs = new[] { new PoEvidenceProtection.Registration(T0.AddMinutes(5), OtherGroup) };
        Assert.True(PoEvidenceProtection.Evaluate(f, null, regs).IsProtected);
    }

    [Fact]
    public void Temporary_upload_after_the_last_registration_is_deletable()
    {
        // Abandoned correction attempt: file uploaded AFTER the group's last registration, never pointed at.
        var f = File(uploaded: T0.AddDays(1));
        var regs = new[] { new PoEvidenceProtection.Registration(T0, Group) };
        var v = PoEvidenceProtection.Evaluate(f, Guid.NewGuid() /* another file is current */, regs);
        Assert.False(v.IsProtected);
        Assert.Null(v.ReasonCode);
    }

    [Fact]
    public void Clock_skew_of_one_second_still_protects()
    {
        var f = File(uploaded: T0);
        var regs = new[] { new PoEvidenceProtection.Registration(T0.AddMilliseconds(-800), Group) };
        Assert.True(PoEvidenceProtection.Evaluate(f, null, regs).IsProtected);
    }

    [Theory]
    [InlineData("[Grupo P.O.: KRONES | Total: 1.000,00 | GroupId: 4ac0fd69] P.O. registada", true)]
    [InlineData("[Grupo P.O.: KRONES | GroupId: 4AC0FD69] correção", true)]
    [InlineData("P.O. registada (legacy row without group)", false)]
    [InlineData("", false)]
    [InlineData(null, false)]
    public void GroupFromHistoryComment_reads_the_8_hex_prefix(string? comment, bool expectGroup)
    {
        var g = PoEvidenceProtection.GroupFromHistoryComment(comment, new List<Guid> { Group, OtherGroup });
        if (expectGroup) Assert.Equal(Group, g); else Assert.Null(g);
    }

    [Fact]
    public void GroupFromHistoryComment_is_null_when_the_prefix_is_ambiguous()
    {
        var twin = Guid.Parse("4ac0fd69-ffff-0000-0000-000000000009");
        Assert.Null(PoEvidenceProtection.GroupFromHistoryComment("[GroupId: 4ac0fd69]", new[] { Group, twin }));
    }
}
