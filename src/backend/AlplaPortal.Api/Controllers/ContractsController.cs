namespace AlplaPortal.Api.Controllers;

using AlplaPortal.Application.DTOs.Contracts;
using AlplaPortal.Domain.Constants;
using AlplaPortal.Domain.Entities;
using AlplaPortal.Infrastructure.Data;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;

[Authorize]
[ApiController]
[Route("api/v1/contracts")]
public class ContractsController : BaseController
{
    private readonly ILogger<ContractsController> _logger;

    public ContractsController(ApplicationDbContext context, ILogger<ContractsController> logger) : base(context)
    {
        _logger = logger;
    }

    // ─── Helpers ───

    private bool HasContractAccess()
    {
        var roles = CurrentUserRoles;
        return roles.Contains(RoleConstants.SystemAdministrator)
            || roles.Contains(RoleConstants.Contracts)
            || roles.Contains(RoleConstants.Finance);
    }

    private bool HasWriteAccess()
    {
        var roles = CurrentUserRoles;
        return roles.Contains(RoleConstants.SystemAdministrator)
            || roles.Contains(RoleConstants.Contracts);
    }

    private bool CanGenerateRequest()
    {
        var roles = CurrentUserRoles;
        return roles.Contains(RoleConstants.SystemAdministrator)
            || roles.Contains(RoleConstants.Finance);
    }

    private async Task<string> GenerateContractNumber()
    {
        var counterKey = "CONTRACT_COUNTER";
        var year = DateTime.UtcNow.Year;

        var counter = await _context.SystemCounters.FirstOrDefaultAsync(sc => sc.Id == counterKey);
        int seqNumber;

        if (counter == null)
        {
            seqNumber = 1;
            counter = new SystemCounter
            {
                Id = counterKey,
                CurrentValue = seqNumber,
                LastUpdatedUtc = DateTime.UtcNow
            };
            _context.SystemCounters.Add(counter);
        }
        else
        {
            counter.CurrentValue++;
            counter.LastUpdatedUtc = DateTime.UtcNow;
            seqNumber = counter.CurrentValue;
        }

        await _context.SaveChangesAsync();
        return $"CTR-{year}-{seqNumber:D4}";
    }

    // ─── LIST ───

    [HttpGet]
    public async Task<ActionResult<ContractListResponseDto>> GetContracts(
        [FromQuery] string? search = null,
        [FromQuery] string? statusCodes = null,
        [FromQuery] string? typeIds = null,
        [FromQuery] string? companyIds = null,
        [FromQuery] string? plantIds = null,
        [FromQuery] string? departmentIds = null,
        [FromQuery] string? sortBy = null,
        [FromQuery] bool isDescending = true,
        [FromQuery] int page = 1,
        [FromQuery] int pageSize = 20)
    {
        if (!HasContractAccess()) return Forbid();

        var query = await GetScopedContractsQuery();
        var today = DateTime.UtcNow.Date;
        var in30Days = today.AddDays(30);

        // Filters
        if (!string.IsNullOrWhiteSpace(search))
        {
            var s = search.Trim().ToLower();
            query = query.Where(c =>
                c.ContractNumber.ToLower().Contains(s) ||
                c.Title.ToLower().Contains(s) ||
                (c.Supplier != null && c.Supplier.Name.ToLower().Contains(s)) ||
                (c.CounterpartyName != null && c.CounterpartyName.ToLower().Contains(s)));
        }

        if (!string.IsNullOrWhiteSpace(statusCodes))
        {
            var codes = statusCodes.Split(',', StringSplitOptions.RemoveEmptyEntries);
            query = query.Where(c => codes.Contains(c.StatusCode));
        }

        if (!string.IsNullOrWhiteSpace(typeIds))
        {
            var ids = typeIds.Split(',', StringSplitOptions.RemoveEmptyEntries).Select(int.Parse).ToList();
            if (ids.Any()) query = query.Where(c => ids.Contains(c.ContractTypeId));
        }

        if (!string.IsNullOrWhiteSpace(companyIds))
        {
            var ids = companyIds.Split(',', StringSplitOptions.RemoveEmptyEntries).Select(int.Parse).ToList();
            if (ids.Any()) query = query.Where(c => ids.Contains(c.CompanyId));
        }

        if (!string.IsNullOrWhiteSpace(plantIds))
        {
            var ids = plantIds.Split(',', StringSplitOptions.RemoveEmptyEntries).Select(int.Parse).ToList();
            if (ids.Any()) query = query.Where(c => c.PlantId.HasValue && ids.Contains(c.PlantId.Value));
        }

        if (!string.IsNullOrWhiteSpace(departmentIds))
        {
            var ids = departmentIds.Split(',', StringSplitOptions.RemoveEmptyEntries).Select(int.Parse).ToList();
            if (ids.Any()) query = query.Where(c => ids.Contains(c.DepartmentId));
        }

        // Summary — computed before pagination
        var summary = await query.GroupBy(_ => 1).Select(g => new ContractSummaryDto
        {
            TotalContracts = g.Count(),
            ActiveContracts = g.Count(c => c.StatusCode == ContractConstants.Statuses.Active),
            ExpiringIn30Days = g.Count(c => c.StatusCode == ContractConstants.Statuses.Active && c.ExpirationDateUtc <= in30Days && c.ExpirationDateUtc >= today),
            DraftContracts = g.Count(c => c.StatusCode == ContractConstants.Statuses.Draft),
            SuspendedContracts = g.Count(c => c.StatusCode == ContractConstants.Statuses.Suspended)
        }).FirstOrDefaultAsync() ?? new ContractSummaryDto();

        // Sorting
        query = sortBy?.ToLower() switch
        {
            "contractnumber" => isDescending ? query.OrderByDescending(c => c.ContractNumber) : query.OrderBy(c => c.ContractNumber),
            "title" => isDescending ? query.OrderByDescending(c => c.Title) : query.OrderBy(c => c.Title),
            "status" => isDescending ? query.OrderByDescending(c => c.StatusCode) : query.OrderBy(c => c.StatusCode),
            "expirationdate" => isDescending ? query.OrderByDescending(c => c.ExpirationDateUtc) : query.OrderBy(c => c.ExpirationDateUtc),
            "totalvalue" => isDescending ? query.OrderByDescending(c => c.TotalContractValue) : query.OrderBy(c => c.TotalContractValue),
            _ => query.OrderByDescending(c => c.CreatedAtUtc)
        };

        var totalCount = await query.CountAsync();

        var items = await query
            .Select(c => new ContractListItemDto
            {
                Id = c.Id,
                ContractNumber = c.ContractNumber,
                Title = c.Title,
                StatusCode = c.StatusCode,
                ContractTypeName = c.ContractType.Name,
                ContractTypeCode = c.ContractType.Code,
                SupplierName = c.Supplier != null ? c.Supplier.Name : null,
                CounterpartyName = c.CounterpartyName,
                CompanyName = c.Company.Name,
                PlantName = c.Plant != null ? c.Plant.Name : null,
                DepartmentName = c.Department.Name,
                EffectiveDateUtc = c.EffectiveDateUtc,
                ExpirationDateUtc = c.ExpirationDateUtc,
                TotalContractValue = c.TotalContractValue,
                CurrencyCode = c.Currency != null ? c.Currency.Code : null,
                CreatedAtUtc = c.CreatedAtUtc,
                ObligationCount = c.PaymentObligations.Count,
                PendingObligationCount = c.PaymentObligations.Count(o => o.StatusCode == ContractConstants.ObligationStatuses.Pending)
            })
            .Skip((page - 1) * pageSize)
            .Take(pageSize)
            .ToListAsync();

        return Ok(new ContractListResponseDto
        {
            Items = items,
            TotalCount = totalCount,
            Page = page,
            PageSize = pageSize,
            Summary = summary
        });
    }

    // ─── DETAIL ───

    [HttpGet("{id:guid}")]
    public async Task<ActionResult<ContractDetailDto>> GetContract(Guid id)
    {
        if (!HasContractAccess()) return Forbid();

        var contract = await _context.Contracts
            .AsNoTracking()
            .AsSplitQuery()
            .Where(c => c.Id == id)
            .Select(c => new ContractDetailDto
            {
                Id = c.Id,
                ContractNumber = c.ContractNumber,
                Title = c.Title,
                Description = c.Description,
                StatusCode = c.StatusCode,
                ContractTypeId = c.ContractTypeId,
                ContractTypeName = c.ContractType.Name,
                ContractTypeCode = c.ContractType.Code,
                SupplierId = c.SupplierId,
                SupplierName = c.Supplier != null ? c.Supplier.Name : null,
                CounterpartyName = c.CounterpartyName,
                DepartmentId = c.DepartmentId,
                DepartmentName = c.Department.Name,
                CompanyId = c.CompanyId,
                CompanyName = c.Company.Name,
                PlantId = c.PlantId,
                PlantName = c.Plant != null ? c.Plant.Name : null,
                SignedAtUtc = c.SignedAtUtc,
                EffectiveDateUtc = c.EffectiveDateUtc,
                ExpirationDateUtc = c.ExpirationDateUtc,
                TerminatedAtUtc = c.TerminatedAtUtc,
                AutoRenew = c.AutoRenew,
                RenewalNoticeDays = c.RenewalNoticeDays,
                TotalContractValue = c.TotalContractValue,
                CurrencyId = c.CurrencyId,
                CurrencyCode = c.Currency != null ? c.Currency.Code : null,
                PaymentTerms = c.PaymentTerms,
                GoverningLaw = c.GoverningLaw,
                TerminationClauses = c.TerminationClauses,
                OcrValidatedByUser = c.OcrValidatedByUser,
                CreatedAtUtc = c.CreatedAtUtc,
                CreatedByUserName = c.CreatedByUser.FullName,
                UpdatedAtUtc = c.UpdatedAtUtc,
                Documents = c.Documents.Select(d => new ContractDocumentDto
                {
                    Id = d.Id,
                    DocumentType = d.DocumentType,
                    FileName = d.FileName,
                    ContentType = d.ContentType,
                    FileSizeBytes = d.FileSizeBytes,
                    Description = d.Description,
                    VersionNumber = d.VersionNumber,
                    UploadedAtUtc = d.UploadedAtUtc,
                    UploadedByUserName = d.UploadedByUser.FullName
                }).ToList(),
                Histories = c.Histories.OrderByDescending(h => h.OccurredAtUtc).Select(h => new ContractHistoryDto
                {
                    Id = h.Id,
                    EventType = h.EventType,
                    FromStatusCode = h.FromStatusCode,
                    ToStatusCode = h.ToStatusCode,
                    Comment = h.Comment,
                    OccurredAtUtc = h.OccurredAtUtc,
                    ActorUserName = h.ActorUser.FullName
                }).ToList(),
                Alerts = c.Alerts.Where(a => !a.IsDismissed).OrderBy(a => a.TriggerDateUtc).Select(a => new ContractAlertDto
                {
                    Id = a.Id,
                    AlertType = a.AlertType,
                    TriggerDateUtc = a.TriggerDateUtc,
                    IsDismissed = a.IsDismissed,
                    Message = a.Message,
                    CreatedAtUtc = a.CreatedAtUtc
                }).ToList()
            })
            .FirstOrDefaultAsync();

        if (contract == null) return NotFound();

        // Obligations — with linked request lookup (unidirectional FK, requires separate query)
        var obligations = await _context.ContractPaymentObligations
            .AsNoTracking()
            .Where(o => o.ContractId == id)
            .OrderBy(o => o.SequenceNumber)
            .Select(o => new ObligationDto
            {
                Id = o.Id,
                SequenceNumber = o.SequenceNumber,
                Description = o.Description,
                ExpectedAmount = o.ExpectedAmount,
                CurrencyId = o.CurrencyId,
                CurrencyCode = o.Currency != null ? o.Currency.Code : null,
                DueDateUtc = o.DueDateUtc,
                StatusCode = o.StatusCode,
                CreatedAtUtc = o.CreatedAtUtc
            })
            .ToListAsync();

        // Resolve linked requests (unidirectional FK: Request → Obligation)
        var obligationIds = obligations.Select(o => o.Id).ToList();
        var linkedRequests = await _context.Requests
            .AsNoTracking()
            .Where(r => r.ContractPaymentObligationId.HasValue && 
                        obligationIds.Contains(r.ContractPaymentObligationId.Value) &&
                        !r.IsCancelled && 
                        r.Status!.Code != "REJECTED" && 
                        r.Status!.Code != "CANCELLED")
            .Select(r => new { r.ContractPaymentObligationId, r.Id, r.RequestNumber, StatusCode = r.Status!.Code })
            .ToListAsync();

        foreach (var obl in obligations)
        {
            var linked = linkedRequests.FirstOrDefault(r => r.ContractPaymentObligationId == obl.Id);
            if (linked != null)
            {
                obl.LinkedRequestId = linked.Id;
                obl.LinkedRequestNumber = linked.RequestNumber;
                obl.LinkedRequestStatusCode = linked.StatusCode;
            }
        }

        contract.Obligations = obligations;

        return Ok(contract);
    }

    // ─── CREATE ───

    [HttpPost]
    public async Task<ActionResult<ContractDetailDto>> CreateContract([FromBody] CreateContractDto dto)
    {
        if (!HasWriteAccess()) return Forbid();

        // Validate references
        var contractType = await _context.ContractTypes.FindAsync(dto.ContractTypeId);
        if (contractType == null) return BadRequest("Tipo de contrato inválido.");

        var department = await _context.Departments.FindAsync(dto.DepartmentId);
        if (department == null) return BadRequest("Departamento inválido.");

        var company = await _context.Companies.FindAsync(dto.CompanyId);
        if (company == null) return BadRequest("Empresa inválida.");

        if (dto.PlantId.HasValue)
        {
            var plant = await _context.Plants.FindAsync(dto.PlantId.Value);
            if (plant == null) return BadRequest("Fábrica inválida.");
            if (plant.CompanyId != dto.CompanyId) return BadRequest("A fábrica não pertence à empresa selecionada.");
        }

        if (dto.ExpirationDateUtc <= dto.EffectiveDateUtc)
            return BadRequest("A data de expiração deve ser posterior à data de início.");

        var contractNumber = await GenerateContractNumber();
        var now = DateTime.UtcNow;
        var userId = CurrentUserId;

        var contract = new Contract
        {
            ContractNumber = contractNumber,
            Title = dto.Title,
            Description = dto.Description,
            ContractTypeId = dto.ContractTypeId,
            StatusCode = ContractConstants.Statuses.Draft,
            SupplierId = dto.SupplierId,
            CounterpartyName = dto.CounterpartyName,
            DepartmentId = dto.DepartmentId,
            CompanyId = dto.CompanyId,
            PlantId = dto.PlantId,
            SignedAtUtc = dto.SignedAtUtc,
            EffectiveDateUtc = dto.EffectiveDateUtc,
            ExpirationDateUtc = dto.ExpirationDateUtc,
            AutoRenew = dto.AutoRenew,
            RenewalNoticeDays = dto.RenewalNoticeDays,
            TotalContractValue = dto.TotalContractValue,
            CurrencyId = dto.CurrencyId,
            PaymentTerms = dto.PaymentTerms,
            GoverningLaw = dto.GoverningLaw,
            TerminationClauses = dto.TerminationClauses,
            CreatedAtUtc = now,
            CreatedByUserId = userId
        };

        _context.Contracts.Add(contract);

        // History entry
        _context.ContractHistories.Add(new ContractHistory
        {
            ContractId = contract.Id,
            EventType = ContractConstants.HistoryEventTypes.Created,
            ToStatusCode = ContractConstants.Statuses.Draft,
            Comment = $"Contrato {contractNumber} criado em rascunho.",
            OccurredAtUtc = now,
            ActorUserId = userId
        });

        await _context.SaveChangesAsync();

        _logger.LogInformation("Contract {ContractNumber} created by user {UserId}", contractNumber, userId);

        return await GetContract(contract.Id);
    }

    // ─── UPDATE ───

    [HttpPut("{id:guid}")]
    public async Task<ActionResult<ContractDetailDto>> UpdateContract(Guid id, [FromBody] UpdateContractDto dto)
    {
        if (!HasWriteAccess()) return Forbid();

        var contract = await _context.Contracts.FindAsync(id);
        if (contract == null) return NotFound();

        // Only DRAFT and UNDER_REVIEW contracts can be edited
        if (contract.StatusCode != ContractConstants.Statuses.Draft &&
            contract.StatusCode != ContractConstants.Statuses.UnderReview)
        {
            return BadRequest("Apenas contratos em Rascunho ou Em Revisão podem ser editados.");
        }

        if (dto.ExpirationDateUtc <= dto.EffectiveDateUtc)
            return BadRequest("A data de expiração deve ser posterior à data de início.");

        if (dto.PlantId.HasValue)
        {
            var plant = await _context.Plants.FindAsync(dto.PlantId.Value);
            if (plant == null) return BadRequest("Fábrica inválida.");
            if (plant.CompanyId != dto.CompanyId) return BadRequest("A fábrica não pertence à empresa selecionada.");
        }

        var now = DateTime.UtcNow;
        var userId = CurrentUserId;

        contract.Title = dto.Title;
        contract.Description = dto.Description;
        contract.ContractTypeId = dto.ContractTypeId;
        contract.SupplierId = dto.SupplierId;
        contract.CounterpartyName = dto.CounterpartyName;
        contract.DepartmentId = dto.DepartmentId;
        contract.CompanyId = dto.CompanyId;
        contract.PlantId = dto.PlantId;
        contract.SignedAtUtc = dto.SignedAtUtc;
        contract.EffectiveDateUtc = dto.EffectiveDateUtc;
        contract.ExpirationDateUtc = dto.ExpirationDateUtc;
        contract.AutoRenew = dto.AutoRenew;
        contract.RenewalNoticeDays = dto.RenewalNoticeDays;
        contract.TotalContractValue = dto.TotalContractValue;
        contract.CurrencyId = dto.CurrencyId;
        contract.PaymentTerms = dto.PaymentTerms;
        contract.GoverningLaw = dto.GoverningLaw;
        contract.TerminationClauses = dto.TerminationClauses;
        contract.UpdatedAtUtc = now;
        contract.UpdatedByUserId = userId;

        _context.ContractHistories.Add(new ContractHistory
        {
            ContractId = id,
            EventType = ContractConstants.HistoryEventTypes.FieldUpdated,
            Comment = "Dados do contrato atualizados.",
            OccurredAtUtc = now,
            ActorUserId = userId
        });

        await _context.SaveChangesAsync();
        return await GetContract(id);
    }

    // ─── STATUS TRANSITIONS ───

    [HttpPost("{id:guid}/activate")]
    public async Task<ActionResult> ActivateContract(Guid id, [FromBody] StatusTransitionDto dto)
    {
        if (!HasWriteAccess()) return Forbid();

        var contract = await _context.Contracts.FindAsync(id);
        if (contract == null) return NotFound();

        if (contract.StatusCode != ContractConstants.Statuses.Draft &&
            contract.StatusCode != ContractConstants.Statuses.UnderReview)
        {
            return BadRequest("Apenas contratos em Rascunho ou Em Revisão podem ser ativados.");
        }

        var fromStatus = contract.StatusCode;
        contract.StatusCode = ContractConstants.Statuses.Active;
        contract.UpdatedAtUtc = DateTime.UtcNow;
        contract.UpdatedByUserId = CurrentUserId;

        _context.ContractHistories.Add(new ContractHistory
        {
            ContractId = id,
            EventType = ContractConstants.HistoryEventTypes.StatusChanged,
            FromStatusCode = fromStatus,
            ToStatusCode = ContractConstants.Statuses.Active,
            Comment = dto.Comment ?? "Contrato ativado.",
            OccurredAtUtc = DateTime.UtcNow,
            ActorUserId = CurrentUserId
        });

        await _context.SaveChangesAsync();
        return Ok(new { statusCode = ContractConstants.Statuses.Active });
    }

    [HttpPost("{id:guid}/submit-review")]
    public async Task<ActionResult> SubmitForReview(Guid id, [FromBody] StatusTransitionDto dto)
    {
        if (!HasWriteAccess()) return Forbid();

        var contract = await _context.Contracts.FindAsync(id);
        if (contract == null) return NotFound();

        if (contract.StatusCode != ContractConstants.Statuses.Draft)
            return BadRequest("Apenas contratos em Rascunho podem ser submetidos para revisão.");

        contract.StatusCode = ContractConstants.Statuses.UnderReview;
        contract.UpdatedAtUtc = DateTime.UtcNow;
        contract.UpdatedByUserId = CurrentUserId;

        _context.ContractHistories.Add(new ContractHistory
        {
            ContractId = id,
            EventType = ContractConstants.HistoryEventTypes.StatusChanged,
            FromStatusCode = ContractConstants.Statuses.Draft,
            ToStatusCode = ContractConstants.Statuses.UnderReview,
            Comment = dto.Comment ?? "Contrato submetido para revisão.",
            OccurredAtUtc = DateTime.UtcNow,
            ActorUserId = CurrentUserId
        });

        await _context.SaveChangesAsync();
        return Ok(new { statusCode = ContractConstants.Statuses.UnderReview });
    }

    [HttpPost("{id:guid}/suspend")]
    public async Task<ActionResult> SuspendContract(Guid id, [FromBody] StatusTransitionDto dto)
    {
        if (!HasWriteAccess()) return Forbid();

        var contract = await _context.Contracts.FindAsync(id);
        if (contract == null) return NotFound();

        if (contract.StatusCode != ContractConstants.Statuses.Active)
            return BadRequest("Apenas contratos ativos podem ser suspensos.");

        contract.StatusCode = ContractConstants.Statuses.Suspended;
        contract.UpdatedAtUtc = DateTime.UtcNow;
        contract.UpdatedByUserId = CurrentUserId;

        _context.ContractHistories.Add(new ContractHistory
        {
            ContractId = id,
            EventType = ContractConstants.HistoryEventTypes.StatusChanged,
            FromStatusCode = ContractConstants.Statuses.Active,
            ToStatusCode = ContractConstants.Statuses.Suspended,
            Comment = dto.Comment ?? "Contrato suspenso.",
            OccurredAtUtc = DateTime.UtcNow,
            ActorUserId = CurrentUserId
        });

        await _context.SaveChangesAsync();
        return Ok(new { statusCode = ContractConstants.Statuses.Suspended });
    }

    [HttpPost("{id:guid}/reactivate")]
    public async Task<ActionResult> ReactivateContract(Guid id, [FromBody] StatusTransitionDto dto)
    {
        if (!HasWriteAccess()) return Forbid();

        var contract = await _context.Contracts.FindAsync(id);
        if (contract == null) return NotFound();

        if (contract.StatusCode != ContractConstants.Statuses.Suspended)
            return BadRequest("Apenas contratos suspensos podem ser reativados.");

        contract.StatusCode = ContractConstants.Statuses.Active;
        contract.UpdatedAtUtc = DateTime.UtcNow;
        contract.UpdatedByUserId = CurrentUserId;

        _context.ContractHistories.Add(new ContractHistory
        {
            ContractId = id,
            EventType = ContractConstants.HistoryEventTypes.StatusChanged,
            FromStatusCode = ContractConstants.Statuses.Suspended,
            ToStatusCode = ContractConstants.Statuses.Active,
            Comment = dto.Comment ?? "Contrato reativado.",
            OccurredAtUtc = DateTime.UtcNow,
            ActorUserId = CurrentUserId
        });

        await _context.SaveChangesAsync();
        return Ok(new { statusCode = ContractConstants.Statuses.Active });
    }

    [HttpPost("{id:guid}/terminate")]
    public async Task<ActionResult> TerminateContract(Guid id, [FromBody] StatusTransitionDto dto)
    {
        if (!HasWriteAccess()) return Forbid();

        var contract = await _context.Contracts.FindAsync(id);
        if (contract == null) return NotFound();

        if (contract.StatusCode != ContractConstants.Statuses.Active &&
            contract.StatusCode != ContractConstants.Statuses.Suspended)
        {
            return BadRequest("Apenas contratos ativos ou suspensos podem ser terminados.");
        }

        var fromStatus = contract.StatusCode;
        contract.StatusCode = ContractConstants.Statuses.Terminated;
        contract.TerminatedAtUtc = DateTime.UtcNow;
        contract.UpdatedAtUtc = DateTime.UtcNow;
        contract.UpdatedByUserId = CurrentUserId;

        _context.ContractHistories.Add(new ContractHistory
        {
            ContractId = id,
            EventType = ContractConstants.HistoryEventTypes.StatusChanged,
            FromStatusCode = fromStatus,
            ToStatusCode = ContractConstants.Statuses.Terminated,
            Comment = dto.Comment ?? "Contrato terminado.",
            OccurredAtUtc = DateTime.UtcNow,
            ActorUserId = CurrentUserId
        });

        await _context.SaveChangesAsync();
        return Ok(new { statusCode = ContractConstants.Statuses.Terminated });
    }

    // ─── OBLIGATIONS ───

    [HttpPost("{contractId:guid}/obligations")]
    public async Task<ActionResult<ObligationDto>> CreateObligation(Guid contractId, [FromBody] CreateObligationDto dto)
    {
        if (!HasWriteAccess()) return Forbid();

        var contract = await _context.Contracts.FindAsync(contractId);
        if (contract == null) return NotFound("Contrato não encontrado.");

        if (contract.StatusCode != ContractConstants.Statuses.Active &&
            contract.StatusCode != ContractConstants.Statuses.Draft)
        {
            return BadRequest("Obrigações só podem ser adicionadas a contratos ativos ou em rascunho.");
        }

        var maxSeq = await _context.ContractPaymentObligations
            .Where(o => o.ContractId == contractId)
            .MaxAsync(o => (int?)o.SequenceNumber) ?? 0;

        var now = DateTime.UtcNow;
        var obligation = new ContractPaymentObligation
        {
            ContractId = contractId,
            SequenceNumber = maxSeq + 1,
            Description = dto.Description,
            ExpectedAmount = dto.ExpectedAmount,
            CurrencyId = dto.CurrencyId ?? contract.CurrencyId,
            DueDateUtc = dto.DueDateUtc,
            StatusCode = ContractConstants.ObligationStatuses.Pending,
            CreatedAtUtc = now,
            CreatedByUserId = CurrentUserId
        };

        _context.ContractPaymentObligations.Add(obligation);

        _context.ContractHistories.Add(new ContractHistory
        {
            ContractId = contractId,
            EventType = ContractConstants.HistoryEventTypes.ObligationAdded,
            Comment = $"Obrigação #{obligation.SequenceNumber} adicionada: {dto.ExpectedAmount:N2}",
            OccurredAtUtc = now,
            ActorUserId = CurrentUserId
        });

        await _context.SaveChangesAsync();

        return Ok(new ObligationDto
        {
            Id = obligation.Id,
            SequenceNumber = obligation.SequenceNumber,
            Description = obligation.Description,
            ExpectedAmount = obligation.ExpectedAmount,
            CurrencyId = obligation.CurrencyId,
            DueDateUtc = obligation.DueDateUtc,
            StatusCode = obligation.StatusCode,
            CreatedAtUtc = obligation.CreatedAtUtc
        });
    }

    [HttpPut("{contractId:guid}/obligations/{obligationId:guid}")]
    public async Task<ActionResult<ObligationDto>> UpdateObligation(Guid contractId, Guid obligationId, [FromBody] UpdateObligationDto dto)
    {
        if (!HasWriteAccess()) return Forbid();

        var obligation = await _context.ContractPaymentObligations
            .FirstOrDefaultAsync(o => o.Id == obligationId && o.ContractId == contractId);

        if (obligation == null) return NotFound("Obrigação não encontrada.");

        if (obligation.StatusCode != ContractConstants.ObligationStatuses.Pending)
            return BadRequest("Apenas obrigações pendentes podem ser editadas.");

        obligation.Description = dto.Description;
        obligation.ExpectedAmount = dto.ExpectedAmount;
        obligation.CurrencyId = dto.CurrencyId;
        obligation.DueDateUtc = dto.DueDateUtc;
        obligation.UpdatedAtUtc = DateTime.UtcNow;
        obligation.UpdatedByUserId = CurrentUserId;

        await _context.SaveChangesAsync();

        return Ok(new ObligationDto
        {
            Id = obligation.Id,
            SequenceNumber = obligation.SequenceNumber,
            Description = obligation.Description,
            ExpectedAmount = obligation.ExpectedAmount,
            CurrencyId = obligation.CurrencyId,
            DueDateUtc = obligation.DueDateUtc,
            StatusCode = obligation.StatusCode,
            CreatedAtUtc = obligation.CreatedAtUtc
        });
    }

    [HttpDelete("{contractId:guid}/obligations/{obligationId:guid}")]
    public async Task<ActionResult> CancelObligation(Guid contractId, Guid obligationId)
    {
        if (!HasWriteAccess()) return Forbid();

        var obligation = await _context.ContractPaymentObligations
            .FirstOrDefaultAsync(o => o.Id == obligationId && o.ContractId == contractId);

        if (obligation == null) return NotFound("Obrigação não encontrada.");

        if (obligation.StatusCode != ContractConstants.ObligationStatuses.Pending)
            return BadRequest("Apenas obrigações pendentes podem ser canceladas.");

        obligation.StatusCode = ContractConstants.ObligationStatuses.Cancelled;
        obligation.UpdatedAtUtc = DateTime.UtcNow;
        obligation.UpdatedByUserId = CurrentUserId;

        await _context.SaveChangesAsync();
        return NoContent();
    }

    // ─── GENERATE PAYMENT REQUEST FROM OBLIGATION ───

    [HttpPost("{contractId:guid}/obligations/{obligationId:guid}/generate-request")]
    public async Task<ActionResult<GenerateRequestResultDto>> GeneratePaymentRequest(Guid contractId, Guid obligationId)
    {
        if (!CanGenerateRequest() && !HasWriteAccess()) return Forbid();

        var contract = await _context.Contracts
            .Include(c => c.ContractType)
            .FirstOrDefaultAsync(c => c.Id == contractId);

        if (contract == null) return NotFound("Contrato não encontrado.");

        if (contract.StatusCode != ContractConstants.Statuses.Active)
            return BadRequest("Pedidos de pagamento só podem ser gerados a partir de contratos ativos.");

        if (!contract.SupplierId.HasValue)
            return BadRequest("O contrato deve estar vinculado a um fornecedor para gerar pedidos de pagamento.");

        var obligation = await _context.ContractPaymentObligations
            .FirstOrDefaultAsync(o => o.Id == obligationId && o.ContractId == contractId);

        if (obligation == null) return NotFound("Obrigação não encontrada.");

        if (obligation.StatusCode != ContractConstants.ObligationStatuses.Pending)
            return BadRequest("Apenas obrigações pendentes podem gerar pedidos de pagamento.");

        // Check for existing linked request (ignore cancelled/rejected ones)
        var existingRequest = await _context.Requests
            .AnyAsync(r => r.ContractPaymentObligationId == obligationId &&
                           !r.IsCancelled &&
                           r.Status!.Code != "REJECTED" &&
                           r.Status!.Code != "CANCELLED");

        if (existingRequest)
            return BadRequest("Esta obrigação já possui um pedido de pagamento vinculado.");

        // Resolve lookup IDs
        var paymentType = await _context.RequestTypes.FirstOrDefaultAsync(t => t.Code == RequestConstants.Types.Payment);
        if (paymentType == null) return StatusCode(500, "Tipo de pedido PAYMENT não encontrado.");

        var draftStatus = await _context.RequestStatuses.FirstOrDefaultAsync(s => s.Code == RequestConstants.Statuses.Draft);
        if (draftStatus == null) return StatusCode(500, "Status DRAFT não encontrado.");

        // Generate request number
        var counterKey = "GLOBAL_REQUEST_COUNTER";
        var counter = await _context.SystemCounters.FirstOrDefaultAsync(sc => sc.Id == counterKey);
        int seqNumber;

        if (counter == null)
        {
            seqNumber = 1;
            counter = new SystemCounter { Id = counterKey, CurrentValue = seqNumber, LastUpdatedUtc = DateTime.UtcNow };
            _context.SystemCounters.Add(counter);
        }
        else
        {
            counter.CurrentValue++;
            counter.LastUpdatedUtc = DateTime.UtcNow;
            seqNumber = counter.CurrentValue;
        }
        await _context.SaveChangesAsync();

        var dateStr = DateTime.UtcNow.Date.ToString("dd/MM/yyyy");
        var requestNumber = $"REQ-{dateStr}-{seqNumber:D3}";

        var now = DateTime.UtcNow;

        // Auto-calculate NeedLevel based on days until due date
        // Seeded IDs: 1=BAIXO, 2=NORMAL, 3=URGENTE, 4=CRITICO
        var daysUntilDue = (obligation.DueDateUtc.Date - now.Date).Days;
        int needLevelId;
        if (daysUntilDue <= 0)
            needLevelId = 4; // CRITICO
        else if (daysUntilDue <= 3)
            needLevelId = 3; // URGENTE
        else if (daysUntilDue <= 10)
            needLevelId = 2; // NORMAL
        else
            needLevelId = 1; // BAIXO

        var request = new Request
        {
            RequestNumber = requestNumber,
            Title = $"Pagamento Contratual — {contract.ContractNumber} — {obligation.Description ?? $"Parcela {obligation.SequenceNumber}"}",
            Description = $"Pedido de pagamento gerado automaticamente a partir da obrigação #{obligation.SequenceNumber} do contrato {contract.ContractNumber} ({contract.Title}).",
            RequestTypeId = paymentType.Id,
            StatusId = draftStatus.Id,
            RequesterId = CurrentUserId,
            DepartmentId = contract.DepartmentId,
            CompanyId = contract.CompanyId,
            PlantId = contract.PlantId,
            SupplierId = contract.SupplierId,
            CurrencyId = obligation.CurrencyId ?? contract.CurrencyId,
            EstimatedTotalAmount = obligation.ExpectedAmount,
            NeedLevelId = needLevelId,
            NeedByDateUtc = obligation.DueDateUtc,
            RequestedDateUtc = now,
            CreatedAtUtc = now,
            CreatedByUserId = CurrentUserId,
            ContractId = contractId,
            ContractPaymentObligationId = obligationId
        };

        // Create default initial request item to satisfy "must contain at least one item" submission rule
        var defaultItem = new RequestLineItem
        {
            Request = request,
            LineNumber = 1,
            ItemPriority = "MEDIUM",
            Description = $"Pagamento contratual - {contract.Title} - {obligation.Description}",
            Quantity = 1.0m,
            UnitId = null,
            UnitPrice = obligation.ExpectedAmount,
            TotalAmount = obligation.ExpectedAmount,
            CurrencyId = obligation.CurrencyId ?? contract.CurrencyId,
            PlantId = contract.PlantId,
            SupplierId = contract.SupplierId,
            CreatedAtUtc = now,
            CreatedByUserId = CurrentUserId
        };

        request.LineItems.Add(defaultItem);

        _context.Requests.Add(request);

        // Update obligation status
        obligation.StatusCode = ContractConstants.ObligationStatuses.RequestCreated;
        obligation.UpdatedAtUtc = now;
        obligation.UpdatedByUserId = CurrentUserId;

        // Contract history
        _context.ContractHistories.Add(new ContractHistory
        {
            ContractId = contractId,
            EventType = ContractConstants.HistoryEventTypes.RequestLinked,
            Comment = $"Pedido de pagamento {requestNumber} gerado para obrigação #{obligation.SequenceNumber}.",
            OccurredAtUtc = now,
            ActorUserId = CurrentUserId
        });

        await _context.SaveChangesAsync();

        _logger.LogInformation("Payment request {RequestNumber} generated from contract {ContractNumber} obligation #{SeqNum}",
            requestNumber, contract.ContractNumber, obligation.SequenceNumber);

        return Ok(new GenerateRequestResultDto
        {
            RequestId = request.Id,
            RequestNumber = requestNumber,
            Message = $"Pedido de pagamento {requestNumber} criado com sucesso."
        });
    }

    // ─── DOCUMENTS ───

    [HttpPost("{contractId:guid}/documents")]
    public async Task<ActionResult<ContractDocumentDto>> UploadDocument(
        Guid contractId,
        [FromForm] IFormFile file,
        [FromForm] string? documentType,
        [FromForm] string? description)
    {
        if (!HasWriteAccess()) return Forbid();

        var contract = await _context.Contracts.FindAsync(contractId);
        if (contract == null) return NotFound("Contrato não encontrado.");

        if (file == null || file.Length == 0)
            return BadRequest("Ficheiro inválido.");

        var storageDir = Path.Combine("data", "attachments", "contracts", contractId.ToString());
        Directory.CreateDirectory(storageDir);

        var uniqueName = $"{Guid.NewGuid()}{Path.GetExtension(file.FileName)}";
        var storagePath = Path.Combine(storageDir, uniqueName);

        using (var stream = new FileStream(storagePath, FileMode.Create))
        {
            await file.CopyToAsync(stream);
        }

        // Compute hash
        string? fileHash = null;
        try
        {
            using var hashStream = new FileStream(storagePath, FileMode.Open, FileAccess.Read);
            using var sha256 = System.Security.Cryptography.SHA256.Create();
            var hashBytes = await sha256.ComputeHashAsync(hashStream);
            fileHash = Convert.ToHexString(hashBytes);
        }
        catch { /* Non-critical */ }

        var now = DateTime.UtcNow;
        var maxVersion = await _context.ContractDocuments
            .Where(d => d.ContractId == contractId && d.FileName == file.FileName)
            .MaxAsync(d => (int?)d.VersionNumber) ?? 0;

        var doc = new ContractDocument
        {
            ContractId = contractId,
            DocumentType = documentType ?? ContractConstants.DocumentTypes.Original,
            FileName = file.FileName,
            StoragePath = storagePath,
            ContentType = file.ContentType,
            FileSizeBytes = file.Length,
            FileHash = fileHash,
            Description = description,
            VersionNumber = maxVersion + 1,
            UploadedAtUtc = now,
            UploadedByUserId = CurrentUserId
        };

        _context.ContractDocuments.Add(doc);

        _context.ContractHistories.Add(new ContractHistory
        {
            ContractId = contractId,
            EventType = ContractConstants.HistoryEventTypes.DocumentUploaded,
            Comment = $"Documento \"{file.FileName}\" carregado ({documentType ?? "ORIGINAL"}).",
            OccurredAtUtc = now,
            ActorUserId = CurrentUserId
        });

        await _context.SaveChangesAsync();

        var user = await _context.Users.FindAsync(CurrentUserId);

        return Ok(new ContractDocumentDto
        {
            Id = doc.Id,
            DocumentType = doc.DocumentType,
            FileName = doc.FileName,
            ContentType = doc.ContentType,
            FileSizeBytes = doc.FileSizeBytes,
            Description = doc.Description,
            VersionNumber = doc.VersionNumber,
            UploadedAtUtc = doc.UploadedAtUtc,
            UploadedByUserName = user?.FullName ?? "Unknown"
        });
    }

    [HttpGet("{contractId:guid}/documents/{documentId:guid}/download")]
    public async Task<ActionResult> DownloadDocument(Guid contractId, Guid documentId)
    {
        if (!HasContractAccess()) return Forbid();

        var doc = await _context.ContractDocuments
            .FirstOrDefaultAsync(d => d.Id == documentId && d.ContractId == contractId);

        if (doc == null) return NotFound("Documento não encontrado.");

        if (!System.IO.File.Exists(doc.StoragePath))
            return NotFound("Ficheiro não encontrado no servidor.");

        var stream = new FileStream(doc.StoragePath, FileMode.Open, FileAccess.Read);
        return File(stream, doc.ContentType ?? "application/octet-stream", doc.FileName);
    }

    // ─── ALERTS ───

    [HttpGet("alerts")]
    public async Task<ActionResult<List<ContractAlertDto>>> GetActiveAlerts()
    {
        if (!HasContractAccess()) return Forbid();

        var query = await GetScopedContractsQuery();
        var contractIds = await query.Select(c => c.Id).ToListAsync();

        var alerts = await _context.ContractAlerts
            .AsNoTracking()
            .Where(a => contractIds.Contains(a.ContractId) && !a.IsDismissed)
            .OrderBy(a => a.TriggerDateUtc)
            .Select(a => new ContractAlertDto
            {
                Id = a.Id,
                AlertType = a.AlertType,
                TriggerDateUtc = a.TriggerDateUtc,
                IsDismissed = a.IsDismissed,
                Message = a.Message,
                CreatedAtUtc = a.CreatedAtUtc,
                ContractId = a.ContractId,
                ContractNumber = a.Contract.ContractNumber,
                ContractTitle = a.Contract.Title,
                ContractStatusCode = a.Contract.StatusCode
            })
            .ToListAsync();

        return Ok(alerts);
    }

    [HttpPost("alerts/{alertId:guid}/dismiss")]
    public async Task<ActionResult> DismissAlert(Guid alertId)
    {
        if (!HasWriteAccess()) return Forbid();

        var alert = await _context.ContractAlerts.FindAsync(alertId);
        if (alert == null) return NotFound();

        alert.IsDismissed = true;
        alert.DismissedByUserId = CurrentUserId;
        alert.DismissedAtUtc = DateTime.UtcNow;

        await _context.SaveChangesAsync();
        return NoContent();
    }

    // ─── LOOKUPS ───

    [HttpGet("types")]
    public async Task<ActionResult> GetContractTypes()
    {
        var types = await _context.ContractTypes
            .AsNoTracking()
            .Where(t => t.IsActive)
            .OrderBy(t => t.DisplayOrder)
            .Select(t => new { t.Id, t.Code, t.Name })
            .ToListAsync();

        return Ok(types);
    }
}
