using System;
using System.Linq;
using System.Threading.Tasks;
using AlplaPortal.Api.Controllers;
using AlplaPortal.Domain.Entities;
using AlplaPortal.Infrastructure.Data;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging.Abstractions;
using Xunit;

namespace AlplaPortal.Application.Tests.Services;

/// <summary>
/// Master Data › Accounts Payable Email: the two opt-in switches (NotifyOnPoRegistered, NotifyFinanceUsersByEmail)
/// are persisted and returned, default to false on creation when omitted, can be edited independently, and never
/// disturb the existing To/CC/IsActive/NotifyOnScheduled/NotifyOnCompleted values.
/// </summary>
public class AccountsPayableConfigControllerTests
{
    private static ApplicationDbContext NewCtx() => new(new DbContextOptionsBuilder<ApplicationDbContext>()
        .UseInMemoryDatabase(Guid.NewGuid().ToString()).Options);

    private static AccountsPayableConfigController Controller(ApplicationDbContext ctx) =>
        new(ctx, NullLogger<AccountsPayableConfigController>.Instance);

    private static T Prop<T>(object anonymous, string name) => (T)anonymous.GetType().GetProperty(name)!.GetValue(anonymous)!;

    [Fact]
    public async Task Create_without_the_new_flags_persists_explicit_false_and_keeps_existing_defaults()
    {
        await using var ctx = NewCtx();
        ctx.Companies.Add(new Company { Id = 1, Name = "AlplaPLASTICO", IsActive = true }); await ctx.SaveChangesAsync();

        var result = await Controller(ctx).Create(new AccountsPayableConfigController.CreateApConfigDto
        {
            CompanyId = 1, Email = "alpla-plasticos-accounts@alpla.com", CcEmails = "aovia-treasury@alpla.com"
            // NotifyOnPoRegistered / NotifyFinanceUsersByEmail omitted → DTO defaults false
        });
        var ok = Assert.IsType<OkObjectResult>(result);
        Assert.False(Prop<bool>(ok.Value!, "NotifyOnPoRegistered"));
        Assert.False(Prop<bool>(ok.Value!, "NotifyFinanceUsersByEmail"));

        var row = await ctx.AccountsPayableNotificationConfigs.AsNoTracking().SingleAsync();
        Assert.Equal("alpla-plasticos-accounts@alpla.com", row.Email);
        Assert.Equal("aovia-treasury@alpla.com", row.CcEmails);
        Assert.True(row.IsActive); Assert.True(row.NotifyOnScheduled); Assert.True(row.NotifyOnCompleted);
        Assert.False(row.NotifyOnPoRegistered); Assert.False(row.NotifyFinanceUsersByEmail);
    }

    [Fact]
    public async Task Entity_defaults_are_false_so_rows_created_outside_the_api_are_also_opted_out()
    {
        var e = new AccountsPayableNotificationConfig();
        Assert.False(e.NotifyOnPoRegistered);
        Assert.False(e.NotifyFinanceUsersByEmail);
        Assert.True(e.NotifyOnScheduled); Assert.True(e.NotifyOnCompleted);
    }

    [Fact]
    public async Task Update_edits_each_switch_independently_and_GetAll_returns_them()
    {
        await using var ctx = NewCtx();
        ctx.Companies.Add(new Company { Id = 1, Name = "AlplaPLASTICO", IsActive = true });
        ctx.AccountsPayableNotificationConfigs.Add(new AccountsPayableNotificationConfig { Id = 5, CompanyId = 1, Email = "ap@alpla.com", CcEmails = "cc@alpla.com", IsActive = true, NotifyOnScheduled = true, NotifyOnCompleted = false });
        await ctx.SaveChangesAsync();

        // Turn on only the P.O. notice
        Assert.IsType<OkResult>(await Controller(ctx).Update(5, new AccountsPayableConfigController.UpdateApConfigDto
        { Email = "ap@alpla.com", CcEmails = "cc@alpla.com", NotifyOnScheduled = true, NotifyOnCompleted = false, NotifyOnPoRegistered = true, NotifyFinanceUsersByEmail = false }));
        var row = await ctx.AccountsPayableNotificationConfigs.AsNoTracking().SingleAsync();
        Assert.True(row.NotifyOnPoRegistered); Assert.False(row.NotifyFinanceUsersByEmail);
        Assert.Equal("cc@alpla.com", row.CcEmails); Assert.True(row.NotifyOnScheduled); Assert.False(row.NotifyOnCompleted); Assert.True(row.IsActive);

        // Turn on only the Finance e-mail
        Assert.IsType<OkResult>(await Controller(ctx).Update(5, new AccountsPayableConfigController.UpdateApConfigDto
        { Email = "ap@alpla.com", CcEmails = "cc@alpla.com", NotifyOnScheduled = true, NotifyOnCompleted = false, NotifyOnPoRegistered = false, NotifyFinanceUsersByEmail = true }));
        row = await ctx.AccountsPayableNotificationConfigs.AsNoTracking().SingleAsync();
        Assert.False(row.NotifyOnPoRegistered); Assert.True(row.NotifyFinanceUsersByEmail);

        var list = Assert.IsType<OkObjectResult>(await Controller(ctx).GetAll());
        var item = ((System.Collections.IEnumerable)list.Value!).Cast<object>().Single();
        Assert.False(Prop<bool>(item, "NotifyOnPoRegistered"));
        Assert.True(Prop<bool>(item, "NotifyFinanceUsersByEmail"));
        Assert.Equal("ap@alpla.com", Prop<string>(item, "Email"));
    }

    [Fact]
    public async Task ToggleActive_does_not_touch_the_switches()
    {
        await using var ctx = NewCtx();
        ctx.Companies.Add(new Company { Id = 1, Name = "Co", IsActive = true });
        ctx.AccountsPayableNotificationConfigs.Add(new AccountsPayableNotificationConfig { Id = 5, CompanyId = 1, Email = "ap@alpla.com", IsActive = true, NotifyOnPoRegistered = true, NotifyFinanceUsersByEmail = true });
        await ctx.SaveChangesAsync();
        await Controller(ctx).ToggleActive(5);
        var row = await ctx.AccountsPayableNotificationConfigs.AsNoTracking().SingleAsync();
        Assert.False(row.IsActive); Assert.True(row.NotifyOnPoRegistered); Assert.True(row.NotifyFinanceUsersByEmail);
    }
}
