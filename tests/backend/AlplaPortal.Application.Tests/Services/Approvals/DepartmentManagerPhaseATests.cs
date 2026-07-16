using System;
using System.Linq;
using System.Threading.Tasks;
using AlplaPortal.Domain.Constants;
using AlplaPortal.Domain.Entities;
using AlplaPortal.Infrastructure.Data;
using AlplaPortal.Infrastructure.Services.Approvals;
using Microsoft.EntityFrameworkCore;
using Xunit;

namespace AlplaPortal.Application.Tests.Services.Approvals;

/// <summary>
/// Phase A tests: D3 (auto-completion of visibility scopes when saving a manager)
/// and D2 (reconciliation report classifications).
/// </summary>
public class DepartmentManagerPhaseATests
{
    private static ApplicationDbContext GetInMemoryDbContext()
    {
        var options = new DbContextOptionsBuilder<ApplicationDbContext>()
            .UseInMemoryDatabase(databaseName: Guid.NewGuid().ToString())
            .Options;
        return new ApplicationDbContext(options);
    }

    private static User NewUser(string name, bool isActive = true, string? email = null)
        => new() { Id = Guid.NewGuid(), FullName = name, Email = email ?? $"{name.Replace(" ", ".").ToLower()}@alpla.com", IsActive = isActive };

    private static async Task<ApplicationDbContext> SeedOrg()
    {
        var ctx = GetInMemoryDbContext();
        ctx.Companies.Add(new Company { Id = 1, Name = "Alpla" });
        ctx.Departments.Add(new Department { Id = 5, Name = "Produção", Code = "PROD" });
        ctx.Plants.AddRange(
            new Plant { Id = 1, Name = "Viana 1", CompanyId = 1 },
            new Plant { Id = 2, Name = "Viana 2", CompanyId = 1 },
            new Plant { Id = 3, Name = "Viana 3", CompanyId = 1 },
            new Plant { Id = 4, Name = "Desativada", CompanyId = 1, IsActive = false });
        await ctx.SaveChangesAsync();
        return ctx;
    }

    // ── D3: auto-completar escopos de visibilidade ──

    [Fact]
    public async Task Add_PlantSpecificManager_CreatesMissingScopes_AndReportsThem()
    {
        var ctx = await SeedOrg();
        var user = NewUser("Manager A");
        ctx.Users.Add(user);
        await ctx.SaveChangesAsync();
        var service = new DepartmentManagerService(ctx);

        var result = await service.AddAsync(5, user.Id, plantId: 1);

        Assert.True(await ctx.UserDepartmentScopes.AnyAsync(s => s.UserId == user.Id && s.DepartmentId == 5));
        Assert.True(await ctx.UserPlantScopes.AnyAsync(s => s.UserId == user.Id && s.PlantId == 1));
        Assert.Equal(new[] { "Produção" }, result.CreatedDepartmentScopes);
        Assert.Equal(new[] { "Viana 1" }, result.CreatedPlantScopes);
    }

    [Fact]
    public async Task Add_GlobalManager_CreatesScopesForAllActivePlants()
    {
        var ctx = await SeedOrg();
        var user = NewUser("Manager E");
        ctx.Users.Add(user);
        await ctx.SaveChangesAsync();
        var service = new DepartmentManagerService(ctx);

        var result = await service.AddAsync(5, user.Id, plantId: null);

        var plantScopeIds = await ctx.UserPlantScopes.Where(s => s.UserId == user.Id).Select(s => s.PlantId).ToListAsync();
        Assert.Equal(new[] { 1, 2, 3 }, plantScopeIds.OrderBy(p => p).ToArray()); // planta inativa (4) fora
        Assert.Equal(3, result.CreatedPlantScopes.Count);
    }

    [Fact]
    public async Task Add_UserWithExistingScopes_CreatesNothing_AndReportsNothing()
    {
        var ctx = await SeedOrg();
        var user = NewUser("Manager A");
        ctx.Users.Add(user);
        ctx.UserDepartmentScopes.Add(new UserDepartmentScope { UserId = user.Id, DepartmentId = 5 });
        ctx.UserPlantScopes.Add(new UserPlantScope { UserId = user.Id, PlantId = 1 });
        await ctx.SaveChangesAsync();
        var service = new DepartmentManagerService(ctx);

        var result = await service.AddAsync(5, user.Id, plantId: 1);

        Assert.Empty(result.CreatedDepartmentScopes);
        Assert.Empty(result.CreatedPlantScopes);
        Assert.Equal(1, await ctx.UserPlantScopes.CountAsync(s => s.UserId == user.Id));
    }

    [Fact]
    public async Task Add_DuplicateActiveManager_Throws()
    {
        var ctx = await SeedOrg();
        var user = NewUser("Manager A");
        ctx.Users.Add(user);
        await ctx.SaveChangesAsync();
        var service = new DepartmentManagerService(ctx);
        await service.AddAsync(5, user.Id, plantId: 1);

        await Assert.ThrowsAsync<InvalidOperationException>(() => service.AddAsync(5, user.Id, plantId: 1));
    }

    [Fact]
    public async Task Add_PreviouslyDeactivatedManager_Reactivates()
    {
        var ctx = await SeedOrg();
        var user = NewUser("Manager A");
        ctx.Users.Add(user);
        await ctx.SaveChangesAsync();
        var service = new DepartmentManagerService(ctx);
        var first = await service.AddAsync(5, user.Id, plantId: 1);
        await service.ToggleActiveAsync(5, first.Manager.Id);

        var second = await service.AddAsync(5, user.Id, plantId: 1);

        Assert.Equal(first.Manager.Id, second.Manager.Id); // mesma linha, sem violar o unique
        Assert.True((await ctx.DepartmentManagers.SingleAsync(dm => dm.Id == first.Manager.Id)).IsActive);
    }

    [Fact]
    public async Task Add_InactiveUser_Throws()
    {
        var ctx = await SeedOrg();
        var user = NewUser("Inactive", isActive: false);
        ctx.Users.Add(user);
        await ctx.SaveChangesAsync();
        var service = new DepartmentManagerService(ctx);

        await Assert.ThrowsAsync<InvalidOperationException>(() => service.AddAsync(5, user.Id, plantId: 1));
    }

    [Fact]
    public async Task Remove_Manager_KeepsScopes()
    {
        var ctx = await SeedOrg();
        var user = NewUser("Manager A");
        ctx.Users.Add(user);
        await ctx.SaveChangesAsync();
        var service = new DepartmentManagerService(ctx);
        var added = await service.AddAsync(5, user.Id, plantId: 1);

        var removed = await service.RemoveAsync(5, added.Manager.Id);

        Assert.True(removed);
        Assert.False(await ctx.DepartmentManagers.AnyAsync());
        // D3: remoção não remove escopos (podem ter outras origens).
        Assert.True(await ctx.UserDepartmentScopes.AnyAsync(s => s.UserId == user.Id && s.DepartmentId == 5));
        Assert.True(await ctx.UserPlantScopes.AnyAsync(s => s.UserId == user.Id && s.PlantId == 1));
    }

    // ── D2: relatório de reconciliação ──

    [Fact]
    public async Task Reconciliation_ClassifiesAllFiveCases()
    {
        var ctx = await SeedOrg();
        var role = new Role { Id = 10, RoleName = RoleConstants.AreaApprover };
        ctx.Roles.Add(role);

        var okDerivado = NewUser("Ok Derivado");
        var perdeAcesso = NewUser("Perde Acesso");
        var soCadastro = NewUser("So Cadastro");
        var inativo = NewUser("Inativo Com Vinculo", isActive: false);
        var inconsistente = NewUser("Inconsistente", email: "");
        ctx.Users.AddRange(okDerivado, perdeAcesso, soCadastro, inativo, inconsistente);

        ctx.UserRoleAssignments.AddRange(
            new UserRoleAssignment { UserId = okDerivado.Id, RoleId = role.Id },
            new UserRoleAssignment { UserId = perdeAcesso.Id, RoleId = role.Id },
            new UserRoleAssignment { UserId = inativo.Id, RoleId = role.Id },
            new UserRoleAssignment { UserId = inconsistente.Id, RoleId = role.Id });

        ctx.DepartmentManagers.AddRange(
            new DepartmentManager { DepartmentId = 5, PlantId = 1, UserId = okDerivado.Id },
            new DepartmentManager { DepartmentId = 5, PlantId = 2, UserId = soCadastro.Id },
            new DepartmentManager { DepartmentId = 5, PlantId = 3, UserId = inativo.Id },
            new DepartmentManager { DepartmentId = 5, PlantId = 1, UserId = inconsistente.Id });
        await ctx.SaveChangesAsync();

        var report = await new AreaApproverReconciliationService(ctx).BuildAsync();

        string ClassOf(Guid id) => report.Single(r => r.UserId == id).Classification;
        Assert.Equal(AreaApproverReconciliationService.OkDerivado, ClassOf(okDerivado.Id));
        Assert.Equal(AreaApproverReconciliationService.PerdeAcesso, ClassOf(perdeAcesso.Id));
        Assert.Equal(AreaApproverReconciliationService.SoCadastro, ClassOf(soCadastro.Id));
        Assert.Equal(AreaApproverReconciliationService.InativoComVinculo, ClassOf(inativo.Id));
        Assert.Equal(AreaApproverReconciliationService.Inconsistente, ClassOf(inconsistente.Id));
        Assert.Equal(5, report.Count);
    }

    [Fact]
    public async Task Reconciliation_FlagsGlobalPlusSpecificInSameDepartment_AsInconsistency()
    {
        var ctx = await SeedOrg();
        var dual = NewUser("Dual Manager");
        ctx.Users.Add(dual);
        ctx.DepartmentManagers.AddRange(
            new DepartmentManager { DepartmentId = 5, PlantId = null, UserId = dual.Id },
            new DepartmentManager { DepartmentId = 5, PlantId = 1, UserId = dual.Id });
        await ctx.SaveChangesAsync();

        var report = await new AreaApproverReconciliationService(ctx).BuildAsync();

        var row = report.Single(r => r.UserId == dual.Id);
        Assert.Equal(AreaApproverReconciliationService.Inconsistente, row.Classification);
        Assert.Contains(row.Inconsistencies, i => i.Contains("Global e específico"));
    }
}
