using System;
using System.Threading.Tasks;
using AlplaPortal.Application.Interfaces;
using AlplaPortal.Application.Models.Auth;
using AlplaPortal.Application.Models.Configuration;
using AlplaPortal.Domain.Constants;
using AlplaPortal.Domain.Entities;
using AlplaPortal.Infrastructure.Data;
using AlplaPortal.Infrastructure.Logging;
using AlplaPortal.Infrastructure.Services.Auth;
using Microsoft.AspNetCore.Http;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using Moq;
using Xunit;

namespace AlplaPortal.Application.Tests.Services.Approvals;

/// <summary>
/// Phase B — derived "Area Approver" claim: granted at login iff the user has at least
/// one active DepartmentManager row; the manually assigned role is ignored as a source.
/// </summary>
public class DerivedAreaApproverClaimTests
{
    private static ApplicationDbContext GetInMemoryDbContext()
    {
        var options = new DbContextOptionsBuilder<ApplicationDbContext>()
            .UseInMemoryDatabase(databaseName: Guid.NewGuid().ToString())
            .Options;
        return new ApplicationDbContext(options);
    }

    private static AuthService BuildAuthService(ApplicationDbContext ctx)
    {
        var passwordHasher = new Mock<IPasswordHasher>();
        passwordHasher.Setup(p => p.VerifyPassword(It.IsAny<string>(), It.IsAny<string>())).Returns(true);

        var jwt = new Mock<IJwtService>();
        jwt.Setup(j => j.GenerateToken(It.IsAny<User>(), It.IsAny<System.Collections.Generic.List<string>>()))
            .Returns("token");

        // AdminLogWriter is only invoked on failure paths — safe with inert mocks here.
        var adminLog = new AdminLogWriter(
            new Mock<IServiceScopeFactory>().Object,
            new Mock<IHttpContextAccessor>().Object,
            new Mock<ILogger<AdminLogWriter>>().Object);

        return new AuthService(
            ctx,
            passwordHasher.Object,
            jwt.Object,
            Options.Create(new SecurityOptions()),
            adminLog,
            new Mock<IEmailService>().Object,
            new Mock<IConfiguration>().Object);
    }

    private static User NewLoginUser(string email)
        => new() { Id = Guid.NewGuid(), Email = email, FullName = email, PasswordHash = "hash", IsActive = true };

    private static async Task SeedOrg(ApplicationDbContext ctx)
    {
        ctx.Companies.Add(new Company { Id = 1, Name = "Alpla" });
        ctx.Departments.Add(new Department { Id = 5, Name = "Produção" });
        ctx.Plants.Add(new Plant { Id = 1, Name = "Viana 1", CompanyId = 1 });
        ctx.Roles.Add(new Role { Id = 10, RoleName = RoleConstants.AreaApprover });
        await ctx.SaveChangesAsync();
    }

    [Fact]
    public async Task Login_ActiveDepartmentManager_WithoutManualRole_ReceivesDerivedClaim()
    {
        var ctx = GetInMemoryDbContext();
        await SeedOrg(ctx);
        var user = NewLoginUser("manager@alpla.com");
        ctx.Users.Add(user);
        ctx.DepartmentManagers.Add(new DepartmentManager { DepartmentId = 5, PlantId = 1, UserId = user.Id });
        await ctx.SaveChangesAsync();

        var response = await BuildAuthService(ctx).LoginAsync(new LoginRequest { Email = user.Email, Password = "x" });

        Assert.NotNull(response);
        Assert.Contains(RoleConstants.AreaApprover, response!.User.Roles);
    }

    [Fact]
    public async Task Login_ManualRoleOnly_WithoutDepartmentManager_DoesNotReceiveClaim()
    {
        var ctx = GetInMemoryDbContext();
        await SeedOrg(ctx);
        var user = NewLoginUser("manual.role@alpla.com");
        ctx.Users.Add(user);
        ctx.UserRoleAssignments.Add(new UserRoleAssignment { UserId = user.Id, RoleId = 10 });
        await ctx.SaveChangesAsync();

        var response = await BuildAuthService(ctx).LoginAsync(new LoginRequest { Email = user.Email, Password = "x" });

        Assert.NotNull(response);
        Assert.DoesNotContain(RoleConstants.AreaApprover, response!.User.Roles);
    }

    [Fact]
    public async Task Login_InactiveManagerRow_DoesNotReceiveClaim()
    {
        var ctx = GetInMemoryDbContext();
        await SeedOrg(ctx);
        var user = NewLoginUser("row.off@alpla.com");
        ctx.Users.Add(user);
        ctx.UserRoleAssignments.Add(new UserRoleAssignment { UserId = user.Id, RoleId = 10 }); // role manual ignorada
        ctx.DepartmentManagers.Add(new DepartmentManager { DepartmentId = 5, PlantId = 1, UserId = user.Id, IsActive = false });
        await ctx.SaveChangesAsync();

        var response = await BuildAuthService(ctx).LoginAsync(new LoginRequest { Email = user.Email, Password = "x" });

        Assert.NotNull(response);
        Assert.DoesNotContain(RoleConstants.AreaApprover, response!.User.Roles);
    }

    [Fact]
    public async Task Login_OtherRolesAreUntouched()
    {
        var ctx = GetInMemoryDbContext();
        await SeedOrg(ctx);
        ctx.Roles.Add(new Role { Id = 11, RoleName = RoleConstants.Buyer });
        var user = NewLoginUser("buyer@alpla.com");
        ctx.Users.Add(user);
        ctx.UserRoleAssignments.Add(new UserRoleAssignment { UserId = user.Id, RoleId = 11 });
        await ctx.SaveChangesAsync();

        var response = await BuildAuthService(ctx).LoginAsync(new LoginRequest { Email = user.Email, Password = "x" });

        Assert.NotNull(response);
        Assert.Contains(RoleConstants.Buyer, response!.User.Roles);
        Assert.DoesNotContain(RoleConstants.AreaApprover, response!.User.Roles);
    }
}
