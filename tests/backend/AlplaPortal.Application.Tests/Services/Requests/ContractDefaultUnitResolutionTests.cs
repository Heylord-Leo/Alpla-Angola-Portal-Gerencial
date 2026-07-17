using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using AlplaPortal.Application.Interfaces;
using AlplaPortal.Application.Validation;
using AlplaPortal.Domain.Constants;
using AlplaPortal.Domain.Entities;
using AlplaPortal.Infrastructure.Data;
using Microsoft.EntityFrameworkCore;
using Xunit;

namespace AlplaPortal.Application.Tests.Services.Requests;

/// <summary>
/// Covers the contract-payment default-unit resolution the ContractsController relies on
/// (resolve the active unit by <see cref="ContractConstants.DefaultLineItemUnitCode"/>), plus proof
/// that a contract-style item passes the payment submission validator. The full controller flow is
/// exercised manually (see runtime script) — here we test the extracted resolution + validation logic.
/// </summary>
public class ContractDefaultUnitResolutionTests
{
    private static ApplicationDbContext NewContext()
        => new(new DbContextOptionsBuilder<ApplicationDbContext>()
            .UseInMemoryDatabase(Guid.NewGuid().ToString()).Options);

    // Mirrors the controller's resolution query exactly.
    private static Task<Unit?> ResolveDefaultUnit(ApplicationDbContext ctx)
        => ctx.Units.FirstOrDefaultAsync(u => u.Code == ContractConstants.DefaultLineItemUnitCode && u.IsActive);

    [Fact] // 11 / 14
    public async Task ActiveDefaultUnit_ResolvesToId()
    {
        using var ctx = NewContext();
        ctx.Units.Add(new Unit { Id = 1, Code = ContractConstants.DefaultLineItemUnitCode, Name = "Unidade", IsActive = true });
        await ctx.SaveChangesAsync();

        var unit = await ResolveDefaultUnit(ctx);

        Assert.NotNull(unit);
        Assert.Equal(1, unit!.Id); // the contract item would receive a non-null UnitId
    }

    [Fact] // 12
    public async Task MissingDefaultUnit_ReturnsNull_SoTheControllerCanFailControlled()
    {
        using var ctx = NewContext();
        ctx.Units.Add(new Unit { Id = 5, Code = "KG", Name = "Quilograma", IsActive = true });
        await ctx.SaveChangesAsync();

        Assert.Null(await ResolveDefaultUnit(ctx));
    }

    [Fact] // 13
    public async Task InactiveDefaultUnit_ReturnsNull_SoTheControllerCanFailControlled()
    {
        using var ctx = NewContext();
        ctx.Units.Add(new Unit { Id = 1, Code = ContractConstants.DefaultLineItemUnitCode, Name = "Unidade", IsActive = false });
        await ctx.SaveChangesAsync();

        Assert.Null(await ResolveDefaultUnit(ctx));
    }

    [Fact] // 15
    public async Task ContractStyleItem_WithResolvedUnit_PassesPaymentValidation()
    {
        using var ctx = NewContext();
        ctx.Units.Add(new Unit { Id = 1, Code = ContractConstants.DefaultLineItemUnitCode, Name = "Unidade", IsActive = true });
        await ctx.SaveChangesAsync();
        var unit = await ResolveDefaultUnit(ctx);
        Assert.NotNull(unit);

        // A contract item: Description set, Quantity 1, UnitId = resolved UN, TotalAmount = ExpectedAmount > 0.
        var candidate = new LineItemCandidate
        {
            Index = 1,
            Description = "Pagamento contratual - Contrato X - Obrigação #1",
            Quantity = 1m,
            UnitId = unit!.Id,
            LineTotal = 1500m
        };

        IRequestLineItemSubmissionValidator validator = new RequestLineItemSubmissionValidator();
        var result = validator.ValidatePaymentSubmit(new[] { candidate }, new HashSet<int> { unit.Id });

        Assert.True(result.IsValid);
    }
}
