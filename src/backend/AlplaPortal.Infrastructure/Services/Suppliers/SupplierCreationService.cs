using System.Globalization;
using System.Text;
using System.Text.RegularExpressions;
using AlplaPortal.Application.Interfaces;
using AlplaPortal.Domain.Constants;
using AlplaPortal.Domain.Entities;
using AlplaPortal.Domain.Services;
using AlplaPortal.Infrastructure.Data;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;

namespace AlplaPortal.Infrastructure.Services.Suppliers;

/// <inheritdoc cref="ISupplierCreationService"/>
public class SupplierCreationService : ISupplierCreationService
{
    private readonly ApplicationDbContext _context;
    private readonly ILogger<SupplierCreationService> _logger;

    public SupplierCreationService(ApplicationDbContext context, ILogger<SupplierCreationService> logger)
    {
        _context = context;
        _logger = logger;
    }

    // ─────────────────────────── Normalization (authoritative) ───────────────────────────

    /// <summary>Trim → uppercase → strip accents → collapse whitespace → drop punctuation.
    /// Corporate suffixes (LDA/SA/…) are intentionally NOT stripped.</summary>
    public static string NormalizeName(string? name)
    {
        if (string.IsNullOrWhiteSpace(name)) return string.Empty;
        var upper = name.Trim().ToUpperInvariant();
        var decomposed = upper.Normalize(NormalizationForm.FormD);
        var sb = new StringBuilder(decomposed.Length);
        foreach (var ch in decomposed)
            if (CharUnicodeInfo.GetUnicodeCategory(ch) != UnicodeCategory.NonSpacingMark)
                sb.Append(ch);
        var noAccents = sb.ToString().Normalize(NormalizationForm.FormC);
        var noPunct = Regex.Replace(noAccents, @"[.,;:!?/\\\-_'""()]", " ");
        return Regex.Replace(noPunct, @"\s+", " ").Trim();
    }

    /// <summary>NIF/TaxId comparison key — delegates to the shared canonical normalizer.</summary>
    public static string NormalizeNif(string? taxId) => AlplaPortal.Domain.Common.TaxIdNormalizer.Normalize(taxId);

    /// <summary>
    /// True when <paramref name="rejectedId"/> refers to a supplier that was a plausible name candidate for
    /// <paramref name="inputName"/> — i.e. its normalized name matches. Used to prevent a client from
    /// fabricating an audited "rejected supplier" for an arbitrary id that never matched the name.
    /// </summary>
    public static bool IsPlausibleNameCandidate(int rejectedId, string? inputName, IEnumerable<(int Id, string Name)> suppliers)
    {
        var normName = NormalizeName(inputName);
        if (normName.Length == 0) return false;
        return suppliers.Any(s => s.Id == rejectedId && NormalizeName(s.Name) == normName);
    }

    /// <summary>
    /// Builds the PAYMENT_OCR creation audit comment from SERVER-RESOLVED data only. The internal company
    /// and rejected supplier are passed as resolved (Id/Name from the DB), never as raw client text.
    /// </summary>
    public static string BuildPaymentOcrAuditComment(
        string? extractedName, string? extractedTaxId,
        (int Id, string Name, string TaxId)? internalCompany,
        (int Id, string Name)? rejectedSupplier,
        bool confirmedDespiteDuplicate)
    {
        var comment = $"Fornecedor criado (DRAFT) via OCR de pagamento. Nome extraído: '{extractedName}'. NIF extraído: '{extractedTaxId ?? "(vazio)"}'.";
        if (internalCompany is { } ic)
            comment += $" O NIF extraído pertencia à empresa interna '{ic.Name}' (Id {ic.Id}, NIF {ic.TaxId}) e foi desconsiderado; fornecedor criado sem NIF.";
        if (rejectedSupplier is { } rj)
            comment += $" O utilizador recusou o fornecedor sugerido pelo nome: '{rj.Name}' (Id {rj.Id}).";
        if (confirmedDespiteDuplicate)
            comment += " Criado apesar de possível duplicidade (confirmado).";
        return comment;
    }

    private static SupplierSummaryDto ToSummary(SupplierRow s) => new()
    {
        Id = s.Id,
        Name = s.Name,
        TaxId = s.TaxId,
        PortalCode = s.PortalCode,
        PrimaveraCode = s.PrimaveraCode,
        IsActive = s.IsActive,
        RegistrationStatus = s.RegistrationStatus
    };

    private sealed class SupplierRow
    {
        public int Id { get; init; }
        public string Name { get; init; } = string.Empty;
        public string? TaxId { get; init; }
        public string? PortalCode { get; init; }
        public string? PrimaveraCode { get; init; }
        public bool IsActive { get; init; }
        public string RegistrationStatus { get; init; } = string.Empty;
    }

    private Task<List<SupplierRow>> LoadSuppliersAsync()
        => _context.Suppliers.AsNoTracking()
            .Select(s => new SupplierRow
            {
                Id = s.Id, Name = s.Name, TaxId = s.TaxId, PortalCode = s.PortalCode, PrimaveraCode = s.PrimaveraCode,
                IsActive = s.IsActive, RegistrationStatus = s.RegistrationStatus
            })
            .ToListAsync();

    // ─────────────────────────── Matching ───────────────────────────

    /// <summary>
    /// Classifies (does NOT write): exact NIF → exact name (same/diff NIF) → allowed.
    /// Returns Conflict/DuplicateSuspected/Invalid, or Created (Supplier == null) meaning "no blocking match".
    /// </summary>
    private SupplierCreationResult Classify(string? name, string? taxId, bool confirmDespiteDuplicate, IReadOnlyList<SupplierRow> suppliers)
    {
        var normName = NormalizeName(name);
        if (normName.Length == 0)
            return new SupplierCreationResult { Status = SupplierCreationStatus.Invalid, Code = "INVALID_NAME", Message = "O nome do fornecedor é obrigatório." };

        var normNif = NormalizeNif(taxId);

        // 1) Exact NIF — blocks even when the name differs, and even when inactive.
        if (normNif.Length > 0)
        {
            var byNif = suppliers.FirstOrDefault(s => NormalizeNif(s.TaxId) == normNif);
            if (byNif != null)
            {
                return new SupplierCreationResult
                {
                    Status = SupplierCreationStatus.Conflict,
                    Supplier = ToSummary(byNif),
                    Code = byNif.IsActive ? "SUPPLIER_ALREADY_EXISTS" : "SUPPLIER_INACTIVE_EXISTS",
                    Message = byNif.IsActive
                        ? "Já existe um fornecedor com este NIF."
                        : "Já existe um fornecedor com este NIF, atualmente inativo."
                };
            }
        }

        // 2) Exact name.
        var byName = suppliers.FirstOrDefault(s => NormalizeName(s.Name) == normName);
        if (byName != null)
        {
            var byNameNif = NormalizeNif(byName.TaxId);
            if (normNif.Length > 0 && byNameNif == normNif)
            {
                return new SupplierCreationResult
                {
                    Status = SupplierCreationStatus.Conflict,
                    Supplier = ToSummary(byName),
                    Code = "SUPPLIER_ALREADY_EXISTS",
                    Message = "Já existe um fornecedor com este nome e NIF."
                };
            }

            // Same name, different or absent NIF → suspected duplicate (requires confirmation).
            if (!confirmDespiteDuplicate)
            {
                return new SupplierCreationResult
                {
                    Status = SupplierCreationStatus.DuplicateSuspected,
                    Candidates = new List<SupplierSummaryDto> { ToSummary(byName) },
                    Code = "SUPPLIER_DUPLICATE_SUSPECTED",
                    Message = "Existe um fornecedor com nome semelhante e NIF diferente. Confirme para criar mesmo assim."
                };
            }
        }

        // No blocking match (or confirmed) → creation is allowed.
        return new SupplierCreationResult { Status = SupplierCreationStatus.Created };
    }

    /// <summary>
    /// If this counterparty is an internal ALPLA legal entity, returns a blocking result — it must
    /// never be matched or created as a supplier.
    /// </summary>
    ///
    /// <remarks>
    /// <para>Checks the NIF <b>and the name</b>, through the shared
    /// <see cref="InternalCompanyPolicy"/>. It used to check the NIF alone, which was enough only
    /// while every document carried a readable one. The reported defect is exactly the other case: a
    /// document naming <c>ALPLA ANGOLA PLASTICOS LDA.</c> whose fiscal number the reading did not
    /// produce sailed straight through to "this supplier is not registered — create it".</para>
    ///
    /// <para>The name check is a fallback, not a substitute: the NIF is tried first and wins.</para>
    /// </remarks>
    private async Task<SupplierCreationResult?> CheckInternalCompanyAsync(string? taxId, string? name = null)
    {
        var companies = await _context.Companies.AsNoTracking()
            .Select(c => new InternalCompanyRef(c.Id, c.Name, c.Code, c.TaxId))
            .ToListAsync();

        var byTaxId = InternalCompanyPolicy.MatchByTaxId(taxId, companies);
        var company = byTaxId ?? InternalCompanyPolicy.MatchByAlias(name, companies);
        if (company == null) return null;

        var matchedByName = byTaxId == null;

        return new SupplierCreationResult
        {
            Status = SupplierCreationStatus.InternalCompanyTaxId,
            Code = "INTERNAL_COMPANY_TAX_ID",
            // A NIF-only match is the document's billed company appearing beside a real supplier —
            // recoverable. A name match means the counterparty itself is ALPLA — it is not.
            Message = matchedByName
                ? InternalCompanyPolicy.CreationMessage
                : "Este NIF pertence a uma empresa interna e não pode ser cadastrado como fornecedor por este fluxo.",
            InternalCompanyId = company.Id,
            InternalCompanyName = company.Name,
            InternalCompanyTaxId = company.TaxId,
            InternalCompanyMatchedByName = matchedByName
        };
    }

    /// <summary>
    /// Whether an existing supplier row is itself an internal ALPLA entity.
    /// </summary>
    ///
    /// <remarks>
    /// They do exist, and legitimately: <c>ALPLA ANGOLA SOPRO, LDA</c> is in the supplier master with
    /// <c>Origin = SYNCED_PRIMAVERA</c> and would come back on the next sync if it were deleted. So a
    /// name-only reading of an internal entity must not be allowed to auto-select that row either —
    /// which is precisely what matching by name would otherwise do, with a perfect score.
    /// </remarks>
    private async Task<SupplierCreationResult?> CheckMatchedSupplierIsInternalAsync(
        SupplierCreationResult classification)
    {
        // Both shapes matter. A Conflict names the row in `Supplier`; a DuplicateSuspected offers it
        // in `Candidates` instead — and a candidate the user is invited to accept is just as much an
        // offer as an automatic selection.
        var offered = new[] { classification.Supplier }
            .Concat(classification.Candidates)
            .Where(s => s != null)
            .ToList();

        foreach (var supplier in offered)
        {
            var internalCompany = await CheckInternalCompanyAsync(supplier!.TaxId, supplier.Name);
            if (internalCompany != null) return internalCompany;
        }

        return null;
    }

    public async Task<SupplierCreationResult> MatchAsync(string? name, string? taxId)
    {
        var internalCompany = await CheckInternalCompanyAsync(taxId, name);
        if (internalCompany != null) return internalCompany;

        var suppliers = await LoadSuppliersAsync();
        // confirm=false so callers see a duplicate as suspected; Created here means "no blocking match".
        var classification = Classify(name, taxId, confirmDespiteDuplicate: false, suppliers);

        // The reading was of a third party, but the row it landed on is an internal entity. Returning
        // the Conflict unchanged is what makes the caller auto-select it, so the internal answer must
        // replace it — a supplier row does not stop being ALPLA because the search found it.
        var matchedInternal = await CheckMatchedSupplierIsInternalAsync(classification);
        return matchedInternal ?? classification;
    }

    // ─────────────────────────── Creation ───────────────────────────

    public async Task<SupplierCreationResult> CreateAsync(SupplierCreationInput input, Guid actorId)
    {
        // Backend-authoritative protection: never create a supplier that IS an internal ALPLA
        // entity, even if the frontend fails to block it — by NIF, or by the name when the document
        // named the entity but its fiscal number was never read.
        var internalCompany = await CheckInternalCompanyAsync(input.TaxId, input.Name);
        if (internalCompany != null) return internalCompany;

        var suppliers = await LoadSuppliersAsync();
        var classification = Classify(input.Name, input.TaxId, input.ConfirmCreateDespiteDuplicate, suppliers);
        if (classification.Status != SupplierCreationStatus.Created)
            return classification; // Conflict / DuplicateSuspected / Invalid — do not create.

        // ── Resolve audit metadata SERVER-SIDE — never trust client-provided names/ids/NIFs. ──
        // Internal-NIF claim: only recorded when it actually resolves to an internal Company (by TaxId);
        // the Company's own Id/Name/TaxId (from the DB) are used, not the client text.
        (int Id, string Name, string TaxId)? resolvedInternalCompany = null;
        if (!string.IsNullOrWhiteSpace(input.InternalCompanyTaxIdExtracted))
        {
            var internalAudit = await CheckInternalCompanyAsync(input.InternalCompanyTaxIdExtracted);
            if (internalAudit?.InternalCompanyId is int cid)
                resolvedInternalCompany = (cid, internalAudit.InternalCompanyName ?? string.Empty, internalAudit.InternalCompanyTaxId ?? string.Empty);
        }
        // Rejected-suggestion claim: only recorded when the id is a real supplier AND was a plausible
        // name candidate for the submitted name (re-checked against the normalized-name match set).
        (int Id, string Name)? resolvedRejectedSupplier = null;
        if (input.RejectedSuggestedSupplierId is int rejId
            && IsPlausibleNameCandidate(rejId, input.Name, suppliers.Select(s => (s.Id, s.Name))))
        {
            var rej = suppliers.First(s => s.Id == rejId);
            resolvedRejectedSupplier = (rej.Id, rej.Name);
        }

        const int maxRetries = 3;
        for (int attempt = 1; attempt <= maxRetries; attempt++)
        {
            var portalCode = await GetNextPortalCodeAsync();
            var entity = new Supplier
            {
                PortalCode = portalCode,
                PrimaveraCode = string.IsNullOrWhiteSpace(input.PrimaveraCode) ? null : input.PrimaveraCode.Trim().ToUpperInvariant(),
                Name = input.Name.Trim(),
                // Persist the NIF in canonical (normalized) form so the unique TaxId index is functionally
                // sufficient — two different formats of the same NIF cannot both be stored via this service.
                TaxId = NormalizeNif(input.TaxId) is { Length: > 0 } normNif ? normNif : null,
                Address = string.IsNullOrWhiteSpace(input.Address) ? null : input.Address.Trim(),
                ContactName1 = string.IsNullOrWhiteSpace(input.ContactName) ? null : input.ContactName.Trim(),
                ContactEmail1 = string.IsNullOrWhiteSpace(input.ContactEmail) ? null : input.ContactEmail.Trim(),
                ContactPhone1 = string.IsNullOrWhiteSpace(input.ContactPhone) ? null : input.ContactPhone.Trim(),
                IsActive = true,
                RegistrationStatus = SupplierConstants.Statuses.Draft,
                Origin = input.Origin,
                CreatedAtUtc = DateTime.UtcNow,
                CreatedByUserId = actorId.ToString()
            };
            _context.Suppliers.Add(entity);

            // Structured provenance/audit (no new column needed) — built exclusively from server-resolved data.
            var auditComment = input.Origin == "PAYMENT_OCR"
                ? BuildPaymentOcrAuditComment(input.ExtractedName ?? input.Name, input.ExtractedTaxId ?? input.TaxId,
                    resolvedInternalCompany, resolvedRejectedSupplier, input.ConfirmCreateDespiteDuplicate)
                : "Fornecedor criado (DRAFT).";
            _context.SupplierStatusHistories.Add(new SupplierStatusHistory
            {
                Supplier = entity,
                EventType = SupplierConstants.HistoryEventTypes.Created,
                ToStatusCode = SupplierConstants.Statuses.Draft,
                Comment = auditComment,
                OccurredAtUtc = DateTime.UtcNow,
                ActorUserId = actorId
            });

            try
            {
                await _context.SaveChangesAsync();
                return new SupplierCreationResult { Status = SupplierCreationStatus.Created, Supplier = ToSummary(new SupplierRow
                {
                    Id = entity.Id, Name = entity.Name, TaxId = entity.TaxId, PortalCode = entity.PortalCode, PrimaveraCode = entity.PrimaveraCode,
                    IsActive = entity.IsActive, RegistrationStatus = entity.RegistrationStatus
                }) };
            }
            catch (DbUpdateException ex) when (
                attempt < maxRetries &&
                ex.InnerException?.Message?.Contains("IX_Suppliers_PortalCode", StringComparison.OrdinalIgnoreCase) == true)
            {
                _context.Entry(entity).State = EntityState.Detached;
                _logger.LogWarning("PortalCode collision on attempt {Attempt}: '{PortalCode}'. Retrying...", attempt, portalCode);
                continue;
            }
            catch (DbUpdateException ex)
            {
                _context.Entry(entity).State = EntityState.Detached;
                var innerMsg = ex.InnerException?.Message ?? ex.Message;

                // Race safety nets over the DB unique indexes — re-read and return the winner as a conflict.
                if (innerMsg.Contains("IX_Suppliers_Name", StringComparison.OrdinalIgnoreCase) ||
                    innerMsg.Contains("IX_Suppliers_TaxId", StringComparison.OrdinalIgnoreCase) ||
                    (innerMsg.Contains("TaxId", StringComparison.OrdinalIgnoreCase) && innerMsg.Contains("UNIQUE", StringComparison.OrdinalIgnoreCase)))
                {
                    // Re-classify with confirm=false so the caller gets the existing supplier/candidate
                    // (this also covers the unique-name-index case where a same-name create can never win).
                    var fresh = await LoadSuppliersAsync();
                    var raced = Classify(input.Name, input.TaxId, confirmDespiteDuplicate: false, fresh);
                    if (raced.Status == SupplierCreationStatus.Conflict || raced.Status == SupplierCreationStatus.DuplicateSuspected)
                        return raced;
                    return new SupplierCreationResult { Status = SupplierCreationStatus.Conflict, Code = "SUPPLIER_ALREADY_EXISTS", Message = "Já existe um fornecedor com estes dados." };
                }

                if (innerMsg.Contains("IX_Suppliers_PortalCode", StringComparison.OrdinalIgnoreCase))
                {
                    _logger.LogError(ex, "PortalCode collision exhausted retries. Inner: {InnerMessage}", innerMsg);
                    return new SupplierCreationResult { Status = SupplierCreationStatus.Error, Code = "PORTAL_CODE_EXHAUSTED", Message = "Não foi possível gerar um código único para o fornecedor. Tente novamente." };
                }

                _logger.LogError(ex, "Failed to create supplier. Inner: {InnerMessage}", innerMsg);
                return new SupplierCreationResult { Status = SupplierCreationStatus.Error, Code = "CREATE_FAILED", Message = "Ocorreu um erro ao salvar o fornecedor. Tente novamente." };
            }
        }

        return new SupplierCreationResult { Status = SupplierCreationStatus.Error, Code = "PORTAL_CODE_EXHAUSTED", Message = "Não foi possível gerar um código único para o fornecedor após múltiplas tentativas." };
    }

    // ─────────────────────────── PortalCode (concurrency-safe, self-healing) ───────────────────────────

    private static int ParsePortalCodeSequence(string? code)
    {
        if (code == null || !code.StartsWith("SUP-") || code.Length <= 4) return 0;
        return int.TryParse(code.Substring(4), out int parsed) ? parsed : 0;
    }

    private async Task<string> GetNextPortalCodeAsync()
    {
        const string counterKey = "SUPPLIER_PORTAL_CODE";
        int seqNumber;
        using var transaction = await _context.Database.BeginTransactionAsync(System.Data.IsolationLevel.ReadCommitted);
        try
        {
            var counter = await _context.SystemCounters
                .FromSqlRaw("SELECT * FROM SystemCounters WITH (UPDLOCK, ROWLOCK) WHERE Id = {0}", counterKey)
                .FirstOrDefaultAsync();

            var allPortalCodes = await _context.Suppliers
                .Where(s => s.PortalCode != null && s.PortalCode.StartsWith("SUP-"))
                .Select(s => s.PortalCode!)
                .ToListAsync();
            int maxInDb = allPortalCodes.Select(ParsePortalCodeSequence).DefaultIfEmpty(0).Max();

            if (counter == null)
            {
                seqNumber = maxInDb + 1;
                _context.SystemCounters.Add(new SystemCounter { Id = counterKey, CurrentValue = seqNumber, LastUpdatedUtc = DateTime.UtcNow });
            }
            else
            {
                if (counter.CurrentValue < maxInDb) counter.CurrentValue = maxInDb;
                counter.CurrentValue++;
                counter.LastUpdatedUtc = DateTime.UtcNow;
                seqNumber = counter.CurrentValue;
            }

            await _context.SaveChangesAsync();
            await transaction.CommitAsync();
        }
        catch
        {
            await transaction.RollbackAsync();
            throw;
        }
        return $"SUP-{seqNumber:D6}";
    }
}
