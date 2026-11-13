using System;
using System.Linq;
using System.Security.Claims;
using System.Threading;
using System.Threading.Tasks;
using AlplaPortal.Application.DTOs.SupplierCredit;
using AlplaPortal.Application.Interfaces;
using AlplaPortal.Application.Interfaces.Suppliers;
using AlplaPortal.Domain.Constants;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using SC = AlplaPortal.Domain.Constants.SupplierCreditConstants;

namespace AlplaPortal.Api.Controllers;

/// <summary>
/// Supplier credit accounts on the supplier ficha ("Créditos em conta corrente"). Reads for every ficha viewer
/// (<c>CanViewSupplierCredit</c>: Contracts, Finance, Local Manager, SysAdmin, request-scoped Buyer); writes for
/// Purchasing only (<c>CanManageSupplierCredit</c>: Contracts, SysAdmin). Authorization is the Supplier Sheet
/// capability evaluator — the same server-side authority as the rest of the ficha.
/// </summary>
[ApiController]
[Route("api/v1/lookups/suppliers/{supplierId:int}/credit-accounts")]
[Authorize(Roles = "System Administrator,Buyer,Finance,Contracts,Local Manager")]
public class SupplierCreditsController : ControllerBase
{
    private readonly ISupplierCreditLedgerService _ledger;
    private readonly ISupplierCapabilityEvaluator _capabilities;

    public SupplierCreditsController(ISupplierCreditLedgerService ledger, ISupplierCapabilityEvaluator capabilities)
    {
        _ledger = ledger;
        _capabilities = capabilities;
    }

    private Guid UserId => Guid.TryParse(User.FindFirst(ClaimTypes.NameIdentifier)?.Value, out var g) ? g : Guid.Empty;
    private string[] Roles => User.FindAll(ClaimTypes.Role).Select(c => c.Value).ToArray();

    private Task<SupplierSheetCapabilities> CapsAsync(int supplierId, CancellationToken ct) => _capabilities.EvaluateAsync(supplierId, UserId, Roles, ct);

    [HttpGet]
    public async Task<IActionResult> List(int supplierId, CancellationToken ct)
    {
        var caps = await CapsAsync(supplierId, ct);
        if (!caps.CanViewSupplierCredit) return Forbid();
        var accounts = await _ledger.ListAccountsAsync(supplierId, ct);
        return Ok(new { items = accounts, canManage = caps.CanManageSupplierCredit });
    }

    [HttpGet("{accountId:guid}")]
    public async Task<IActionResult> Get(int supplierId, Guid accountId, CancellationToken ct)
    {
        var caps = await CapsAsync(supplierId, ct);
        if (!caps.CanViewSupplierCredit) return Forbid();
        var account = await _ledger.GetAccountAsync(accountId, ct);
        if (account == null || account.SupplierId != supplierId) return NotFound();
        return Ok(account);
    }

    [HttpGet("{accountId:guid}/movements")]
    public async Task<IActionResult> Movements(int supplierId, Guid accountId, CancellationToken ct)
    {
        var caps = await CapsAsync(supplierId, ct);
        if (!caps.CanViewSupplierCredit) return Forbid();
        var account = await _ledger.GetAccountAsync(accountId, ct);
        if (account == null || account.SupplierId != supplierId) return NotFound();
        return Ok(await _ledger.ListMovementsAsync(accountId, ct));
    }

    [HttpPost]
    public async Task<IActionResult> Create(int supplierId, [FromBody] CreateSupplierCreditAccountDto dto, CancellationToken ct)
    {
        var caps = await CapsAsync(supplierId, ct);
        if (!caps.CanManageSupplierCredit) return Forbid();
        var outcome = await _ledger.CreateAccountAsync(supplierId, dto, UserId, ct);
        return outcome.Succeeded ? Ok(outcome.Value) : Problem(outcome);
    }

    [HttpPost("{accountId:guid}/entries")]
    public async Task<IActionResult> AddEntry(int supplierId, Guid accountId, [FromBody] AddSupplierCreditEntryDto dto, CancellationToken ct)
    {
        var caps = await CapsAsync(supplierId, ct);
        if (!caps.CanManageSupplierCredit) return Forbid();
        var account = await _ledger.GetAccountAsync(accountId, ct);
        if (account == null || account.SupplierId != supplierId) return NotFound();
        var outcome = await _ledger.AddEntryAsync(accountId, dto, UserId, ct);
        return outcome.Succeeded ? Ok(outcome.Value) : Problem(outcome);
    }

    private ObjectResult Problem<T>(SupplierCreditOperationOutcome<T> outcome)
    {
        var pd = new ProblemDetails { Title = outcome.Title, Detail = outcome.Detail, Status = outcome.StatusCode };
        pd.Extensions["code"] = outcome.Code;
        foreach (var kv in outcome.Extensions) pd.Extensions[kv.Key] = kv.Value;
        return new ObjectResult(pd) { StatusCode = outcome.StatusCode };
    }
}
