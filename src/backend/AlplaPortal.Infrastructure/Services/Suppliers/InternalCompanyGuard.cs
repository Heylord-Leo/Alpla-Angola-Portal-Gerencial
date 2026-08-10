using AlplaPortal.Application.Interfaces;
using AlplaPortal.Domain.Services;
using AlplaPortal.Infrastructure.Data;
using Microsoft.EntityFrameworkCore;

namespace AlplaPortal.Infrastructure.Services.Suppliers;

/// <inheritdoc />
public sealed class InternalCompanyGuard : IInternalCompanyGuard
{
    private readonly ApplicationDbContext _context;

    /// <summary>
    /// Scoped cache. The company list is two rows that change roughly never, and a single request
    /// can ask this question once per source document plus once per line at submission.
    /// </summary>
    private IReadOnlyList<InternalCompanyRef>? _cached;

    public InternalCompanyGuard(ApplicationDbContext context) => _context = context;

    public async Task<IReadOnlyList<InternalCompanyRef>> GetInternalCompaniesAsync(
        CancellationToken ct = default)
    {
        if (_cached != null) return _cached;

        // Deliberately NOT filtered by IsActive. Deactivating an ALPLA legal entity does not make it
        // an acceptable payable supplier, and a rule that can be switched off by editing a lookup
        // row is not financial integrity.
        _cached = await _context.Companies
            .AsNoTracking()
            .Select(c => new InternalCompanyRef(c.Id, c.Name, c.Code, c.TaxId))
            .ToListAsync(ct);

        return _cached;
    }

    public async Task<InternalCompanyRef?> ResolveAsync(
        string? name, string? taxId, CancellationToken ct = default)
        => InternalCompanyPolicy.Resolve(name, taxId, await GetInternalCompaniesAsync(ct));

    public async Task<InternalCompanyRef?> ResolveSupplierAsync(
        int? supplierId, CancellationToken ct = default)
    {
        if (supplierId is not int id) return null;

        var supplier = await _context.Suppliers
            .AsNoTracking()
            .Where(s => s.Id == id)
            .Select(s => new { s.Name, s.TaxId })
            .FirstOrDefaultAsync(ct);

        if (supplier == null) return null;

        return InternalCompanyPolicy.Resolve(
            supplier.Name, supplier.TaxId, await GetInternalCompaniesAsync(ct));
    }
}
