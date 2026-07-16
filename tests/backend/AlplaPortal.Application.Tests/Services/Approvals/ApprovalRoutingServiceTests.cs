using System;
using System.Linq;
using System.Threading.Tasks;
using AlplaPortal.Application.DTOs.Requests;
using AlplaPortal.Domain.Entities;
using AlplaPortal.Infrastructure.Data;
using AlplaPortal.Infrastructure.Services.Approvals;
using Microsoft.EntityFrameworkCore;
using Xunit;

namespace AlplaPortal.Application.Tests.Services.Approvals;

public class ApprovalRoutingServiceTests
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

    /// <summary>Produção nas 3 plantas: A,B@V1, C@V2, E global. Departamento 5, plantas 1..3.</summary>
    private static async Task<(ApplicationDbContext ctx, User a, User b, User c, User e)> SeedScenario()
    {
        var ctx = GetInMemoryDbContext();
        var company = new Company { Id = 1, Name = "Alpla Angola" };
        var dept = new Department { Id = 5, Name = "Produção", Code = "PROD" };
        ctx.Companies.Add(company);
        ctx.Departments.Add(dept);
        ctx.Plants.AddRange(
            new Plant { Id = 1, Name = "Viana 1", CompanyId = 1 },
            new Plant { Id = 2, Name = "Viana 2", CompanyId = 1 },
            new Plant { Id = 3, Name = "Viana 3", CompanyId = 1 });

        var a = NewUser("Manager A");
        var b = NewUser("Manager B");
        var c = NewUser("Manager C");
        var e = NewUser("Manager E");
        ctx.Users.AddRange(a, b, c, e);

        ctx.DepartmentManagers.AddRange(
            new DepartmentManager { DepartmentId = 5, PlantId = 1, UserId = a.Id },
            new DepartmentManager { DepartmentId = 5, PlantId = 1, UserId = b.Id },
            new DepartmentManager { DepartmentId = 5, PlantId = 2, UserId = c.Id },
            new DepartmentManager { DepartmentId = 5, PlantId = null, UserId = e.Id });

        await ctx.SaveChangesAsync();
        return (ctx, a, b, c, e);
    }

    [Fact]
    public async Task Resolve_PlantWithManagers_ReturnsOnlyPlantSpecific()
    {
        var (ctx, a, b, _, e) = await SeedScenario();
        var service = new ApprovalRoutingService(ctx);

        var result = await service.ResolveAreaManagersAsync(5, plantId: 1);

        Assert.Equal(ApprovalRoutingSource.PlantSpecific, result.Source);
        Assert.Equal(2, result.Managers.Count);
        Assert.Contains(result.Managers, m => m.UserId == a.Id);
        Assert.Contains(result.Managers, m => m.UserId == b.Id);
        // D1 estrito: o global E não recebe e-mail quando a planta tem managers próprios.
        Assert.DoesNotContain(result.Managers, m => m.UserId == e.Id);
    }

    [Fact]
    public async Task Resolve_PlantWithoutManagers_FallsBackToGlobal()
    {
        var (ctx, _, _, _, e) = await SeedScenario();
        var service = new ApprovalRoutingService(ctx);

        // Viana 3 não tem manager específico no cenário.
        var result = await service.ResolveAreaManagersAsync(5, plantId: 3);

        Assert.Equal(ApprovalRoutingSource.GlobalManagers, result.Source);
        Assert.Single(result.Managers);
        Assert.Equal(e.Id, result.Managers[0].UserId);
    }

    [Fact]
    public async Task Resolve_NothingAnywhere_ReturnsNone()
    {
        var ctx = GetInMemoryDbContext();
        ctx.Departments.Add(new Department { Id = 9, Name = "Sem Ninguém" });
        await ctx.SaveChangesAsync();
        var service = new ApprovalRoutingService(ctx);

        var result = await service.ResolveAreaManagersAsync(9, plantId: 1);

        Assert.Equal(ApprovalRoutingSource.None, result.Source);
        Assert.False(result.HasManagers);
    }

    [Fact]
    public async Task Resolve_InactiveRowsAndUsersAndMissingEmail_AreExcluded()
    {
        var ctx = GetInMemoryDbContext();
        ctx.Departments.Add(new Department { Id = 5, Name = "Produção" });
        ctx.Companies.Add(new Company { Id = 1, Name = "Alpla" });
        ctx.Plants.Add(new Plant { Id = 1, Name = "Viana 1", CompanyId = 1 });

        var inactiveUser = NewUser("Inactive", isActive: false);
        var noEmail = NewUser("No Email", email: "");
        var inactiveRowUser = NewUser("Row Off");
        var valid = NewUser("Valid");
        ctx.Users.AddRange(inactiveUser, noEmail, inactiveRowUser, valid);
        ctx.DepartmentManagers.AddRange(
            new DepartmentManager { DepartmentId = 5, PlantId = 1, UserId = inactiveUser.Id },
            new DepartmentManager { DepartmentId = 5, PlantId = 1, UserId = noEmail.Id },
            new DepartmentManager { DepartmentId = 5, PlantId = 1, UserId = inactiveRowUser.Id, IsActive = false },
            new DepartmentManager { DepartmentId = 5, PlantId = 1, UserId = valid.Id });
        await ctx.SaveChangesAsync();
        var service = new ApprovalRoutingService(ctx);

        var result = await service.ResolveAreaManagersAsync(5, plantId: 1);

        Assert.Equal(ApprovalRoutingSource.PlantSpecific, result.Source);
        Assert.Single(result.Managers);
        Assert.Equal(valid.Id, result.Managers[0].UserId);
    }

    [Fact]
    public async Task Resolve_AllPlantRowsIneligible_CascadesToGlobal()
    {
        var ctx = GetInMemoryDbContext();
        ctx.Departments.Add(new Department { Id = 5, Name = "Produção" });
        ctx.Companies.Add(new Company { Id = 1, Name = "Alpla" });
        ctx.Plants.Add(new Plant { Id = 1, Name = "Viana 1", CompanyId = 1 });

        var inactive = NewUser("Inactive Plant Mgr", isActive: false);
        var global = NewUser("Global Mgr");
        ctx.Users.AddRange(inactive, global);
        ctx.DepartmentManagers.AddRange(
            new DepartmentManager { DepartmentId = 5, PlantId = 1, UserId = inactive.Id },
            new DepartmentManager { DepartmentId = 5, PlantId = null, UserId = global.Id });
        await ctx.SaveChangesAsync();
        var service = new ApprovalRoutingService(ctx);

        var result = await service.ResolveAreaManagersAsync(5, plantId: 1);

        Assert.Equal(ApprovalRoutingSource.GlobalManagers, result.Source);
        Assert.Equal(global.Id, result.Managers.Single().UserId);
    }

    [Fact]
    public async Task Resolve_RequestWithoutPlant_UsesGlobalManagers()
    {
        var (ctx, _, _, _, e) = await SeedScenario();
        var service = new ApprovalRoutingService(ctx);

        var result = await service.ResolveAreaManagersAsync(5, plantId: null);

        Assert.Equal(ApprovalRoutingSource.GlobalManagers, result.Source);
        Assert.Equal(e.Id, result.Managers.Single().UserId);
    }

    // ── D1: assimetria entre Resolve (e-mail, estrito) e IsAreaManager (autorização, inclusivo) ──

    [Fact]
    public async Task IsAreaManager_GlobalManager_AuthorizedEvenWhenPlantHasSpecificManagers()
    {
        var (ctx, _, _, _, e) = await SeedScenario();
        var service = new ApprovalRoutingService(ctx);

        // Resolve para V1 NÃO inclui E (teste acima), mas E PODE aprovar pedidos da V1.
        Assert.True(await service.IsAreaManagerAsync(e.Id, 5, plantId: 1));
    }

    [Fact]
    public async Task IsAreaManager_PlantSpecificManager_AuthorizedForOwnPlantOnly()
    {
        var (ctx, a, _, c, _) = await SeedScenario();
        var service = new ApprovalRoutingService(ctx);

        Assert.True(await service.IsAreaManagerAsync(a.Id, 5, plantId: 1));
        // Manager de outra planta nunca é válido (D1).
        Assert.False(await service.IsAreaManagerAsync(c.Id, 5, plantId: 1));
        Assert.False(await service.IsAreaManagerAsync(a.Id, 5, plantId: 2));
    }

    [Fact]
    public async Task IsAreaManager_RequestWithoutPlant_OnlyGlobalQualifies()
    {
        var (ctx, a, _, _, e) = await SeedScenario();
        var service = new ApprovalRoutingService(ctx);

        Assert.True(await service.IsAreaManagerAsync(e.Id, 5, plantId: null));
        Assert.False(await service.IsAreaManagerAsync(a.Id, 5, plantId: null));
    }

    [Fact]
    public async Task IsAreaManager_UserWithoutManagerRow_NeverAuthorized()
    {
        // Fase C: não existe mais nenhuma fonte legada (coluna ResponsibleUserId removida,
        // role manual inerte). Sem linha ativa em DepartmentManagers, nada autoriza.
        // (Compatibilidade para pedidos antigos em andamento vive nos controllers, via
        // Request.AreaApproverId == ator em etapa de área.)
        var ctx = GetInMemoryDbContext();
        var someone = NewUser("Sem Cadastro");
        ctx.Users.Add(someone);
        ctx.Departments.Add(new Department { Id = 9, Name = "Financeiro" });
        await ctx.SaveChangesAsync();
        var service = new ApprovalRoutingService(ctx);

        Assert.False(await service.IsAreaManagerAsync(someone.Id, 9, plantId: 1));
    }

    [Fact]
    public async Task Resolve_PlantSpecificRowOfInactivePlant_IsExcluded()
    {
        var ctx = GetInMemoryDbContext();
        ctx.Companies.Add(new Company { Id = 1, Name = "Alpla" });
        ctx.Departments.Add(new Department { Id = 5, Name = "Produção" });
        ctx.Plants.Add(new Plant { Id = 7, Name = "Desativada", CompanyId = 1, IsActive = false });
        var mgr = NewUser("Mgr Planta Inativa");
        var global = NewUser("Global");
        ctx.Users.AddRange(mgr, global);
        ctx.DepartmentManagers.AddRange(
            new DepartmentManager { DepartmentId = 5, PlantId = 7, UserId = mgr.Id },
            new DepartmentManager { DepartmentId = 5, PlantId = null, UserId = global.Id });
        await ctx.SaveChangesAsync();
        var service = new ApprovalRoutingService(ctx);

        var result = await service.ResolveAreaManagersAsync(5, plantId: 7);

        Assert.Equal(ApprovalRoutingSource.GlobalManagers, result.Source);
        Assert.Equal(global.Id, result.Managers.Single().UserId);
    }

    [Fact]
    public async Task GetManagedScopes_ReturnsAllActiveScopesOfUser()
    {
        var (ctx, a, _, _, e) = await SeedScenario();
        var service = new ApprovalRoutingService(ctx);

        var aScopes = await service.GetManagedScopesAsync(a.Id);
        var eScopes = await service.GetManagedScopesAsync(e.Id);

        Assert.Single(aScopes);
        Assert.Equal((5, (int?)1), (aScopes[0].DepartmentId, aScopes[0].PlantId));
        Assert.Single(eScopes);
        Assert.Null(eScopes[0].PlantId);
    }
}
