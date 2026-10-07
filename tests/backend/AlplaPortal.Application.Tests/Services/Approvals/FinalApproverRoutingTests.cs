using System;
using System.Threading.Tasks;
using AlplaPortal.Application.DTOs.Requests;
using AlplaPortal.Domain.Constants;
using AlplaPortal.Domain.Entities;
using AlplaPortal.Infrastructure.Data;
using AlplaPortal.Infrastructure.Services.Approvals;
using Microsoft.EntityFrameworkCore;
using Xunit;

namespace AlplaPortal.Application.Tests.Services.Approvals;

/// <summary>
/// Final-stage NOTIFICATION recipients under the current single-final-approver model (used by proforma
/// alerts and reminder digests). This is not an authorization rule: who may approve stays "Final
/// Approver" role + access scope. Rule: request nominee if active with e-mail → else company nominee
/// if active with e-mail → else none. The role is NOT required to be notified, and a missing e-mail
/// only blocks notification (never approval) — both documented gaps are asserted here.
/// </summary>
public class FinalApproverRoutingTests
{
    private static ApplicationDbContext NewCtx() => new(new DbContextOptionsBuilder<ApplicationDbContext>()
        .UseInMemoryDatabase(Guid.NewGuid().ToString()).Options);

    private sealed record Seed(Company Company, User RequestNominee, User CompanyNominee, User RoleHolder);

    private static async Task<Seed> SeedAsync(ApplicationDbContext ctx, bool requestNomineeActive = true, string requestNomineeEmail = "req.nominee@test.local", bool companyNomineeHasRole = true)
    {
        var role = new Role { Id = 7, RoleName = RoleConstants.FinalApprover };
        var reqNominee = new User { Id = Guid.NewGuid(), FullName = "Request Nominee", Email = requestNomineeEmail, IsActive = requestNomineeActive };
        var coNominee = new User { Id = Guid.NewGuid(), FullName = "Company Nominee", Email = "co.nominee@test.local", IsActive = true };
        var roleHolder = new User { Id = Guid.NewGuid(), FullName = "Role Holder", Email = "role.holder@test.local", IsActive = true };
        var company = new Company { Id = 100, Name = "ZZ Co", IsActive = true, FinalApproverUserId = coNominee.Id };
        ctx.Roles.Add(role); ctx.Users.AddRange(reqNominee, coNominee, roleHolder); ctx.Companies.Add(company);
        ctx.UserRoleAssignments.Add(new UserRoleAssignment { UserId = roleHolder.Id, RoleId = role.Id });
        if (companyNomineeHasRole) ctx.UserRoleAssignments.Add(new UserRoleAssignment { UserId = coNominee.Id, RoleId = role.Id });
        await ctx.SaveChangesAsync();
        return new Seed(company, reqNominee, coNominee, roleHolder);
    }

    [Fact]
    public async Task Request_nominee_is_notified_when_active_with_email_even_without_the_role()
    {
        await using var ctx = NewCtx();
        var s = await SeedAsync(ctx); // request nominee holds NO role

        var r = await new ApprovalRoutingService(ctx).ResolveFinalNotificationRecipientsAsync(s.RequestNominee.Id, s.Company.Id);

        Assert.Equal(FinalNotificationSource.RequestNominee, r.Source);
        var only = Assert.Single(r.Recipients);
        Assert.Equal(s.RequestNominee.Id, only.UserId);
        Assert.Equal(s.RequestNominee.Email, only.Email);
        // Documented gap: notified but (without the role) unable to approve — reported by the recipients-vs-approvers script.
    }

    [Fact]
    public async Task Falls_back_to_the_company_nominee_when_the_request_nominee_is_missing_or_inactive()
    {
        await using (var ctx = NewCtx())
        {
            var s = await SeedAsync(ctx);
            var r = await new ApprovalRoutingService(ctx).ResolveFinalNotificationRecipientsAsync(null, s.Company.Id);
            Assert.Equal(FinalNotificationSource.CompanyNominee, r.Source);
            Assert.Equal(s.CompanyNominee.Id, Assert.Single(r.Recipients).UserId);
        }
        await using (var ctx = NewCtx())
        {
            var s = await SeedAsync(ctx, requestNomineeActive: false);
            var r = await new ApprovalRoutingService(ctx).ResolveFinalNotificationRecipientsAsync(s.RequestNominee.Id, s.Company.Id);
            Assert.Equal(FinalNotificationSource.CompanyNominee, r.Source);
            Assert.Equal(s.CompanyNominee.Id, Assert.Single(r.Recipients).UserId);
        }
    }

    [Fact]
    public async Task Missing_email_blocks_notification_only_and_never_adds_a_role_holder_as_substitute()
    {
        await using var ctx = NewCtx();
        var s = await SeedAsync(ctx, requestNomineeEmail: "");
        s.Company.FinalApproverUserId = null; // no company nominee either
        await ctx.SaveChangesAsync();

        var r = await new ApprovalRoutingService(ctx).ResolveFinalNotificationRecipientsAsync(s.RequestNominee.Id, s.Company.Id);

        Assert.Equal(FinalNotificationSource.None, r.Source);
        Assert.Empty(r.Recipients); // the role-holder CAN approve but is never auto-selected as a recipient (documented gap)
    }

    [Fact]
    public async Task Company_nominee_without_email_yields_no_recipient()
    {
        await using var ctx = NewCtx();
        var s = await SeedAsync(ctx);
        s.CompanyNominee.Email = string.Empty;
        await ctx.SaveChangesAsync();

        var r = await new ApprovalRoutingService(ctx).ResolveFinalNotificationRecipientsAsync(null, s.Company.Id);
        Assert.Equal(FinalNotificationSource.None, r.Source);
        Assert.False(r.HasRecipients);
    }
}
