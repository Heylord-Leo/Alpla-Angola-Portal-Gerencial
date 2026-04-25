using AlplaPortal.Infrastructure.Data;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;

namespace AlplaPortal.Api.Controllers;

[ApiController]
[Route("api/v1/[controller]")]
public class LookupsController : ControllerBase
{
    private readonly ApplicationDbContext _context;

    public LookupsController(ApplicationDbContext context)
    {
        _context = context;
    }

    [HttpGet("units")]
    public async Task<IActionResult> GetUnits([FromQuery] bool includeInactive = false)
    {
        var unitsQuery = _context.Units.AsQueryable();
        
        if (!includeInactive)
        {
            unitsQuery = unitsQuery.Where(u => u.IsActive);
        }

        var units = await unitsQuery
            .OrderBy(u => u.Name)
            .Select(u => new { u.Id, u.Code, u.Name, u.IsActive, u.AllowsDecimalQuantity })
            .ToListAsync();

        return Ok(units);
    }

    [HttpPost("units")]
    [Authorize(Roles = "System Administrator")]
    public async Task<IActionResult> CreateUnit([FromBody] CreateUnitDto dto)
    {
        var entity = new Domain.Entities.Unit { Code = dto.Code?.ToUpper() ?? string.Empty, Name = dto.Name, IsActive = true, AllowsDecimalQuantity = dto.AllowsDecimalQuantity };
        _context.Units.Add(entity);
        
        try
        {
            await _context.SaveChangesAsync();
            return Created($"/api/v1/lookups/units/{entity.Id}", new { entity.Id, entity.Code, entity.Name, entity.IsActive, entity.AllowsDecimalQuantity });
        }
        catch (DbUpdateException ex) when (ex.InnerException?.Message.Contains("IX_Units_Code") == true)
        {
            return Conflict(new ProblemDetails 
            { 
                Title = "Regra de Negócio Violada", 
                Detail = $"A Unidade com o código '{dto.Code}' já existe. Não são permitidas duplicidades.", 
                Status = 409 
            });
        }
    }

    [HttpPut("units/{id}")]
    [Authorize(Roles = "System Administrator")]
    public async Task<IActionResult> UpdateUnit(int id, [FromBody] CreateUnitDto dto)
    {
        var entity = await _context.Units.FindAsync(id);
        if (entity == null) return NotFound();

        // Historical Immutability Check
        if (await _context.RequestLineItems.AnyAsync(r => r.UnitId == id))
        {
            return Conflict(new ProblemDetails
            {
                Title = "Regra de Negócio Violada",
                Detail = "Este registro já está em uso por pedidos históricos e não pode ser alterado. Por favor, desative-o e crie um novo para preservar o histórico.",
                Status = 409
            });
        }

        entity.Code = dto.Code?.ToUpper() ?? string.Empty;
        entity.Name = dto.Name;
        entity.AllowsDecimalQuantity = dto.AllowsDecimalQuantity;
        
        try
        {
            await _context.SaveChangesAsync();
            return NoContent();
        }
        catch (DbUpdateException ex) when (ex.InnerException?.Message.Contains("IX_Units_Code") == true)
        {
            return Conflict(new ProblemDetails 
            { 
                Title = "Regra de Negócio Violada", 
                Detail = $"A Unidade com o código '{dto.Code}' já existe. Não são permitidas duplicidades.", 
                Status = 409 
            });
        }
    }

    [HttpPut("units/{id}/toggle-active")]
    [Authorize(Roles = "System Administrator")]
    public async Task<IActionResult> ToggleUnitActive(int id)
    {
        var entity = await _context.Units.FindAsync(id);
        if (entity == null) return NotFound();
        
        entity.IsActive = !entity.IsActive;
        await _context.SaveChangesAsync();
        return NoContent();
    }

    [HttpGet("currencies")]
    public async Task<IActionResult> GetCurrencies([FromQuery] bool includeInactive = false)
    {
        var currencyQuery = _context.Currencies.AsQueryable();

        if (!includeInactive)
        {
            currencyQuery = currencyQuery.Where(c => c.IsActive);
        }

        var currencies = await currencyQuery
            .OrderBy(c => c.Code)
            .Select(c => new { c.Id, c.Code, c.Symbol, c.IsActive })
            .ToListAsync();

        return Ok(currencies);
    }

    [HttpGet("request-types")]
    public async Task<IActionResult> GetRequestTypes([FromQuery] bool includeInactive = false)
    {
        var query = _context.RequestTypes.AsQueryable();
        if (!includeInactive) query = query.Where(rt => rt.IsActive);

        var items = await query
            .OrderBy(rt => rt.Name)
            .Select(rt => new { rt.Id, rt.Code, rt.Name, rt.IsActive })
            .ToListAsync();

        return Ok(items);
    }

    [HttpPost("currencies")]
    [Authorize(Roles = "System Administrator")]
    public async Task<IActionResult> CreateCurrency([FromBody] CreateCurrencyDto dto)
    {
        var entity = new Domain.Entities.Currency { Code = dto.Code.ToUpper(), Symbol = dto.Symbol, IsActive = true };
        _context.Currencies.Add(entity);
        
        try
        {
            await _context.SaveChangesAsync();
            return Created($"/api/v1/lookups/currencies/{entity.Id}", new { entity.Id, entity.Code, entity.Symbol, entity.IsActive });
        }
        catch (DbUpdateException ex) when (ex.InnerException?.Message.Contains("IX_Currencies_Code") == true)
        {
            return Conflict(new ProblemDetails 
            { 
                Title = "Regra de Negócio Violada", 
                Detail = $"A Moeda com o código '{dto.Code}' já existe.", 
                Status = 409 
            });
        }
    }

    [HttpPut("currencies/{id}")]
    [Authorize(Roles = "System Administrator")]
    public async Task<IActionResult> UpdateCurrency(int id, [FromBody] CreateCurrencyDto dto)
    {
        var entity = await _context.Currencies.FindAsync(id);
        if (entity == null) return NotFound();

        // Historical Immutability Check
        if (await _context.Requests.AnyAsync(r => r.CurrencyId == id) || await _context.RequestLineItems.AnyAsync(r => r.CurrencyId == id))
        {
            return Conflict(new ProblemDetails
            {
                Title = "Regra de Negócio Violada",
                Detail = "Este registro já está em uso por pedidos históricos e não pode ser alterado. Por favor, desative-o e crie um novo para preservar o histórico.",
                Status = 409
            });
        }

        entity.Code = dto.Code.ToUpper();
        entity.Symbol = dto.Symbol;

        try
        {
            await _context.SaveChangesAsync();
            return NoContent();
        }
        catch (DbUpdateException ex) when (ex.InnerException?.Message.Contains("IX_Currencies_Code") == true)
        {
            return Conflict(new ProblemDetails 
            { 
                Title = "Regra de Negócio Violada", 
                Detail = $"A Moeda com o código '{dto.Code}' já existe.", 
                Status = 409 
            });
        }
    }

    [HttpPut("currencies/{id}/toggle-active")]
    [Authorize(Roles = "System Administrator")]
    public async Task<IActionResult> ToggleCurrencyActive(int id)
    {
        var entity = await _context.Currencies.FindAsync(id);
        if (entity == null) return NotFound();
        
        entity.IsActive = !entity.IsActive;
        await _context.SaveChangesAsync();
        return NoContent();
    }

    [HttpGet("request-statuses")]
    public async Task<IActionResult> GetRequestStatuses([FromQuery] bool includeInactive = false)
    {
        var query = _context.RequestStatuses.AsQueryable();

        if (!includeInactive)
        {
            query = query.Where(c => c.IsActive);
        }

        var items = await query
            .OrderBy(c => c.DisplayOrder)
            .Select(c => new { c.Id, c.Code, c.Name, c.DisplayOrder, c.BadgeColor, c.IsActive })
            .ToListAsync();

        return Ok(items);
    }

    [HttpGet("need-levels")]
    public async Task<IActionResult> GetNeedLevels([FromQuery] bool includeInactive = false)
    {
        var query = _context.NeedLevels.AsQueryable();

        if (!includeInactive)
        {
            query = query.Where(c => c.IsActive);
        }

        var items = await query
            .OrderBy(c => c.Name)
            .Select(c => new { c.Id, c.Code, c.Name, c.IsActive })
            .ToListAsync();

        return Ok(items);
    }

    [HttpPost("need-levels")]
    [Authorize(Roles = "System Administrator")]
    public async Task<IActionResult> CreateNeedLevel([FromBody] CreateLookupDto dto)
    {
        var entity = new Domain.Entities.NeedLevel { Code = dto.Code?.ToUpper() ?? string.Empty, Name = dto.Name, IsActive = true };
        _context.NeedLevels.Add(entity);
        
        try
        {
            await _context.SaveChangesAsync();
            return Created($"/api/v1/lookups/need-levels/{entity.Id}", new { entity.Id, entity.Code, entity.Name, entity.IsActive });
        }
        catch (DbUpdateException ex) when (ex.InnerException?.Message.Contains("IX_NeedLevels_Code") == true)
        {
            return Conflict(new ProblemDetails 
            { 
                Title = "Regra de Negócio Violada", 
                Detail = $"O Nível de Necessidade com o código '{dto.Code}' já existe.", 
                Status = 409 
            });
        }
    }

    [HttpPut("need-levels/{id}")]
    [Authorize(Roles = "System Administrator")]
    public async Task<IActionResult> UpdateNeedLevel(int id, [FromBody] CreateLookupDto dto)
    {
        var entity = await _context.NeedLevels.FindAsync(id);
        if (entity == null) return NotFound();

        // Historical Immutability Check
        if (await _context.Requests.AnyAsync(r => r.NeedLevelId == id))
        {
            return Conflict(new ProblemDetails
            {
                Title = "Regra de Negócio Violada",
                Detail = "Este registro já está em uso por pedidos históricos e não pode ser alterado. Por favor, desative-o e crie um novo para preservar o histórico.",
                Status = 409
            });
        }

        entity.Code = dto.Code?.ToUpper() ?? string.Empty;
        entity.Name = dto.Name;

        try
        {
            await _context.SaveChangesAsync();
            return NoContent();
        }
        catch (DbUpdateException ex) when (ex.InnerException?.Message.Contains("IX_NeedLevels_Code") == true)
        {
            return Conflict(new ProblemDetails 
            { 
                Title = "Regra de Negócio Violada", 
                Detail = $"O Nível de Necessidade com o código '{dto.Code}' já existe.", 
                Status = 409 
            });
        }
    }

    [HttpPut("need-levels/{id}/toggle-active")]
    [Authorize(Roles = "System Administrator")]
    public async Task<IActionResult> ToggleNeedLevelActive(int id)
    {
        var entity = await _context.NeedLevels.FindAsync(id);
        if (entity == null) return NotFound();
        
        entity.IsActive = !entity.IsActive;
        await _context.SaveChangesAsync();
        return NoContent();
    }

    // Companies
    [HttpGet("companies")]
    public async Task<IActionResult> GetCompanies([FromQuery] bool includeInactive = false)
    {
        var query = _context.Companies.AsQueryable();
        if (!includeInactive) query = query.Where(c => c.IsActive);

        var items = await query
            .OrderBy(c => c.Name)
            .Select(c => new { c.Id, c.Name, c.IsActive, c.FinalApproverUserId })
            .ToListAsync();

        return Ok(items);
    }

    [HttpPost("companies")]
    [Authorize(Roles = "System Administrator")]
    public async Task<IActionResult> CreateCompany([FromBody] CreateLookupDto dto)
    {
        var entity = new Domain.Entities.Company 
        { 
            Name = dto.Name, 
            IsActive = true,
            FinalApproverUserId = dto.FinalApproverUserId
        };
        _context.Companies.Add(entity);
        
        try
        {
            await _context.SaveChangesAsync();
            return Created($"/api/v1/lookups/companies/{entity.Id}", new { entity.Id, entity.Name, entity.IsActive, entity.FinalApproverUserId });
        }
        catch (DbUpdateException ex)
        {
            return StatusCode(500, new ProblemDetails { Title = "Erro ao criar Empresa", Detail = ex.InnerException?.Message ?? ex.Message });
        }
    }

    [HttpPut("companies/{id}")]
    [Authorize(Roles = "System Administrator")]
    public async Task<IActionResult> UpdateCompany(int id, [FromBody] CreateLookupDto dto)
    {
        var entity = await _context.Companies.FindAsync(id);
        if (entity == null) return NotFound();

        // Relaxed guard: block only if NAME changed AND there is historical data
        bool fundamentalFieldsChanged = entity.Name != dto.Name;
        
        if (fundamentalFieldsChanged && await _context.Requests.AnyAsync(r => r.CompanyId == id))
        {
            return Conflict(new ProblemDetails
            {
                Title = "Regra de Negócio Violada",
                Detail = "Este registro já possui pedidos vinculados. O nome não pode ser alterado para preservar a integridade do histórico. No entanto, o aprovador final pode ser atualizado normalmente.",
                Status = 409
            });
        }

        entity.Name = dto.Name;
        entity.FinalApproverUserId = dto.FinalApproverUserId;

        try
        {
            await _context.SaveChangesAsync();
            return NoContent();
        }
        catch (DbUpdateException ex)
        {
            return StatusCode(500, new ProblemDetails { Title = "Erro ao atualizar Empresa", Detail = ex.InnerException?.Message ?? ex.Message });
        }
    }

    [HttpPut("companies/{id}/toggle-active")]
    [Authorize(Roles = "System Administrator")]
    public async Task<IActionResult> ToggleCompanyActive(int id)
    {
        var entity = await _context.Companies.FindAsync(id);
        if (entity == null) return NotFound();
        
        entity.IsActive = !entity.IsActive;
        await _context.SaveChangesAsync();
        return NoContent();
    }

    // Contract Types
    [HttpGet("contract-types")]
    public async Task<IActionResult> GetContractTypes([FromQuery] bool includeInactive = false)
    {
        var query = _context.ContractTypes.AsQueryable();

        if (!includeInactive)
        {
            query = query.Where(t => t.IsActive);
        }

        var items = await query
            .OrderBy(t => t.DisplayOrder).ThenBy(t => t.Name)
            .Select(t => new { t.Id, t.Code, t.Name, t.IsActive })
            .ToListAsync();

        return Ok(items);
    }

    [HttpPost("contract-types")]
    [Authorize(Roles = "System Administrator")]
    public async Task<IActionResult> CreateContractType([FromBody] CreateLookupDto dto)
    {
        var entity = new Domain.Entities.ContractType 
        { 
            Code = dto.Code?.ToUpper() ?? string.Empty, 
            Name = dto.Name, 
            IsActive = true
        };
        _context.ContractTypes.Add(entity);
        
        try
        {
            await _context.SaveChangesAsync();
            return Created($"/api/v1/lookups/contract-types/{entity.Id}", new { entity.Id, entity.Code, entity.Name, entity.IsActive });
        }
        catch (DbUpdateException ex)
        {
            return StatusCode(500, new ProblemDetails { Title = "Erro ao criar Tipo de Contrato", Detail = ex.InnerException?.Message ?? ex.Message });
        }
    }

    [HttpPut("contract-types/{id}")]
    [Authorize(Roles = "System Administrator")]
    public async Task<IActionResult> UpdateContractType(int id, [FromBody] CreateLookupDto dto)
    {
        var entity = await _context.ContractTypes.FindAsync(id);
        if (entity == null) return NotFound();

        bool fundamentalFieldsChanged = entity.Name != dto.Name || (entity.Code != (dto.Code?.ToUpper() ?? string.Empty));
        
        if (fundamentalFieldsChanged && await _context.Contracts.AnyAsync(r => r.ContractTypeId == id))
        {
            return Conflict(new ProblemDetails
            {
                Title = "Regra de Negócio Violada",
                Detail = "Este registro já está em uso por contratos. O nome e o código não podem ser alterados para preservar a integridade do histórico.",
                Status = 409
            });
        }

        entity.Code = dto.Code?.ToUpper() ?? string.Empty;
        entity.Name = dto.Name;

        try
        {
            await _context.SaveChangesAsync();
            return NoContent();
        }
        catch (DbUpdateException ex)
        {
            return StatusCode(500, new ProblemDetails { Title = "Erro ao atualizar Tipo de Contrato", Detail = ex.InnerException?.Message ?? ex.Message });
        }
    }

    [HttpPut("contract-types/{id}/toggle-active")]
    [Authorize(Roles = "System Administrator")]
    public async Task<IActionResult> ToggleContractTypeActive(int id)
    {
        var entity = await _context.ContractTypes.FindAsync(id);
        if (entity == null) return NotFound();
        
        entity.IsActive = !entity.IsActive;
        await _context.SaveChangesAsync();
        return NoContent();
    }

    // Departments
    [HttpGet("departments")]
    public async Task<IActionResult> GetDepartments([FromQuery] bool includeInactive = false)
    {
        var query = _context.Departments.AsQueryable();

        if (!includeInactive)
        {
            query = query.Where(d => d.IsActive);
        }

        var items = await query
            .OrderBy(d => d.Name)
            .Select(d => new { d.Id, d.Code, d.Name, d.IsActive, d.ResponsibleUserId })
            .ToListAsync();

        return Ok(items);
    }

    [HttpPost("departments")]
    [Authorize(Roles = "System Administrator")]
    public async Task<IActionResult> CreateDepartment([FromBody] CreateLookupDto dto)
    {
        var entity = new Domain.Entities.Department 
        { 
            Code = dto.Code?.ToUpper() ?? string.Empty, 
            Name = dto.Name, 
            IsActive = true,
            ResponsibleUserId = dto.ResponsibleUserId
        };
        _context.Departments.Add(entity);
        
        try
        {
            await _context.SaveChangesAsync();
            return Created($"/api/v1/lookups/departments/{entity.Id}", new { entity.Id, entity.Code, entity.Name, entity.IsActive, entity.ResponsibleUserId });
        }
        catch (DbUpdateException ex)
        {
            return StatusCode(500, new ProblemDetails { Title = "Erro ao criar Departamento", Detail = ex.InnerException?.Message ?? ex.Message });
        }
    }

    [HttpPut("departments/{id}")]
    [Authorize(Roles = "System Administrator")]
    public async Task<IActionResult> UpdateDepartment(int id, [FromBody] CreateLookupDto dto)
    {
        var entity = await _context.Departments.FindAsync(id);
        if (entity == null) return NotFound();

        // Relaxed guard: block only if NAME or CODE changed AND there is historical data
        bool fundamentalFieldsChanged = entity.Name != dto.Name || (entity.Code != (dto.Code?.ToUpper() ?? string.Empty));
        
        if (fundamentalFieldsChanged && await _context.Requests.AnyAsync(r => r.DepartmentId == id))
        {
            return Conflict(new ProblemDetails
            {
                Title = "Regra de Negócio Violada",
                Detail = "Este registro já possui pedidos vinculados. O nome e o código não podem ser alterados para preservar a integridade do histórico. No entanto, o responsável pode ser atualizado normalmente.",
                Status = 409
            });
        }

        entity.Code = dto.Code?.ToUpper() ?? string.Empty;
        entity.Name = dto.Name;
        entity.ResponsibleUserId = dto.ResponsibleUserId;

        try
        {
            await _context.SaveChangesAsync();
            return NoContent();
        }
        catch (DbUpdateException ex)
        {
            return StatusCode(500, new ProblemDetails { Title = "Erro ao atualizar Departamento", Detail = ex.InnerException?.Message ?? ex.Message });
        }
    }

    [HttpPut("departments/{id}/toggle-active")]
    [Authorize(Roles = "System Administrator")]
    public async Task<IActionResult> ToggleDepartmentActive(int id)
    {
        var entity = await _context.Departments.FindAsync(id);
        if (entity == null) return NotFound();
        
        entity.IsActive = !entity.IsActive;
        await _context.SaveChangesAsync();
        return NoContent();
    }

    // Plants
    [HttpGet("plants")]
    public async Task<IActionResult> GetPlants([FromQuery] int? companyId, [FromQuery] bool includeInactive = false)
    {
        var query = _context.Plants.AsQueryable();

        if (companyId.HasValue)
        {
            query = query.Where(p => p.CompanyId == companyId.Value);
        }

        if (!includeInactive)
        {
            query = query.Where(p => p.IsActive);
        }

        var items = await query
            .OrderBy(p => p.Name)
            .Select(p => new { p.Id, p.Code, p.Name, CompanyName = p.Company.Name, p.IsActive, CompanyId = p.CompanyId })
            .ToListAsync();

        return Ok(items);
    }

    [HttpPost("plants")]
    [Authorize(Roles = "System Administrator")]
    public async Task<IActionResult> CreatePlant([FromBody] CreateLookupDto dto)
    {
        if (dto.CompanyId == 0)
        {
            return BadRequest(new ProblemDetails { Title = "Erro de Validação", Detail = "O campo Empresa é obrigatório para Plantas." });
        }

        var entity = new Domain.Entities.Plant 
        { 
            Code = dto.Code?.ToUpper() ?? string.Empty, 
            Name = dto.Name, 
            CompanyId = dto.CompanyId,
            IsActive = true 
        };
        _context.Plants.Add(entity);
        
        try
        {
            await _context.SaveChangesAsync();
            return Created($"/api/v1/lookups/plants/{entity.Id}", new { entity.Id, entity.Code, entity.Name, entity.IsActive });
        }
        catch (DbUpdateException ex)
        {
            return StatusCode(500, new ProblemDetails { Title = "Erro ao criar Planta", Detail = ex.InnerException?.Message ?? ex.Message });
        }
    }

    [HttpPut("plants/{id}")]
    [Authorize(Roles = "System Administrator")]
    public async Task<IActionResult> UpdatePlant(int id, [FromBody] CreateLookupDto dto)
    {
        var entity = await _context.Plants.FindAsync(id);
        if (entity == null) return NotFound();

        if (await _context.Requests.AnyAsync(r => r.PlantId == id))
        {
            return Conflict(new ProblemDetails
            {
                Title = "Regra de Negócio Violada",
                Detail = "Este registro já está em uso por pedidos históricos e não pode ser alterado. Por favor, desative-o e crie um novo para preservar o histórico.",
                Status = 409
            });
        }

        entity.Code = dto.Code?.ToUpper() ?? string.Empty;
        entity.Name = dto.Name;
        entity.CompanyId = dto.CompanyId;

        try
        {
            await _context.SaveChangesAsync();
            return NoContent();
        }
        catch (DbUpdateException ex)
        {
            return StatusCode(500, new ProblemDetails { Title = "Erro ao atualizar Planta", Detail = ex.InnerException?.Message ?? ex.Message });
        }
    }

    [HttpPut("plants/{id}/toggle-active")]
    [Authorize(Roles = "System Administrator")]
    public async Task<IActionResult> TogglePlantActive(int id)
    {
        var entity = await _context.Plants.FindAsync(id);
        if (entity == null) return NotFound();
        
        entity.IsActive = !entity.IsActive;
        await _context.SaveChangesAsync();
        return NoContent();
    }

    // Suppliers
    [HttpGet("suppliers")]
    public async Task<IActionResult> GetSuppliers([FromQuery] bool includeInactive = false, [FromQuery] string? search = null, [FromQuery] int page = 1, [FromQuery] int pageSize = 15)
    {
        var query = _context.Suppliers.AsQueryable();

        if (!includeInactive)
        {
            query = query.Where(s => s.IsActive);
        }

        if (!string.IsNullOrEmpty(search))
        {
            var normalizedQ = search.Trim().ToLower();
            query = query.Where(s => s.Name.ToLower().Contains(normalizedQ) || 
                                     (s.TaxId != null && s.TaxId.ToLower().Contains(normalizedQ)) || 
                                     (s.PrimaveraCode != null && s.PrimaveraCode.ToLower().Contains(normalizedQ)) ||
                                     (s.PortalCode != null && s.PortalCode.ToLower().Contains(normalizedQ)));
        }

        var totalCount = await query.CountAsync();

        var items = await query
            .OrderBy(s => s.Name)
            .Skip((page - 1) * pageSize)
            .Take(pageSize)
            .Select(s => new { s.Id, s.PortalCode, s.PrimaveraCode, s.Name, s.TaxId, s.IsActive })
            .ToListAsync();

        return Ok(new Application.DTOs.Common.PagedResult<object>
        {
            Items = items,
            TotalCount = totalCount,
            Page = page,
            PageSize = pageSize
        });
    }

    [HttpGet("suppliers/search")]
    public async Task<IActionResult> SearchSuppliers([FromQuery] string? q, [FromQuery] int take = 20)
    {
        var queryable = _context.Suppliers.Where(s => s.IsActive);

        if (!string.IsNullOrWhiteSpace(q) && q.Length >= 1)
        {
            var queryText = q.ToUpper();
            queryable = queryable.Where(s =>
                (s.PortalCode != null && s.PortalCode.Contains(queryText)) ||
                (s.PrimaveraCode != null && s.PrimaveraCode.Contains(queryText)) ||
                (s.TaxId != null && s.TaxId.Contains(queryText)) ||
                s.Name.ToUpper().Contains(queryText)
            );
        }
        else
        {
            // If empty or too short, just take the first few for the initial dropdown view
            take = 5;
        }

        var results = await queryable
            .OrderBy(s => s.Name)
            .Take(take)
            .Select(s => new { s.Id, s.PortalCode, s.PrimaveraCode, s.Name, s.TaxId })
            .ToListAsync();

        return Ok(results);
    }

    [HttpGet("suppliers/check-uniqueness")]
    public async Task<IActionResult> CheckSupplierUniqueness(
        [FromQuery] string? name, 
        [FromQuery] string? primaveraCode, 
        [FromQuery] int? excludeId)
    {
        bool nameExists = false;
        bool primaveraExists = false;

        if (!string.IsNullOrWhiteSpace(name))
        {
            var normalizedName = name.Trim().ToUpper();
            nameExists = await _context.Suppliers
                .AnyAsync(s => s.Id != excludeId && s.Name.ToUpper() == normalizedName);
        }

        if (!string.IsNullOrWhiteSpace(primaveraCode))
        {
            var normalizedCode = primaveraCode.Trim().ToUpper();
            primaveraExists = await _context.Suppliers
                .AnyAsync(s => s.Id != excludeId && s.PrimaveraCode != null && s.PrimaveraCode.ToUpper() == normalizedCode);
        }

        return Ok(new 
        { 
            isNameDuplicate = nameExists, 
            isPrimaveraDuplicate = primaveraExists 
        });
    }

    private async Task<string> GetNextPortalCodeAsync()
    {
        var counterKey = "SUPPLIER_PORTAL_CODE";
        int seqNumber;

        // Use a transaction with a database-level lock to ensure concurrency safety
        using (var transaction = await _context.Database.BeginTransactionAsync(System.Data.IsolationLevel.ReadCommitted))
        {
            try
            {
                // 1. Acquire an update lock on the counter record (blocking other concurrent generations)
                var counter = await _context.SystemCounters
                    .FromSqlRaw("SELECT * FROM SystemCounters WITH (UPDLOCK, ROWLOCK) WHERE Id = {0}", counterKey)
                    .FirstOrDefaultAsync();

                // 2. Perform robust "Self-healing": always check the actual database state
                // This protects against sequence regressions if SystemCounters is reset or out of sync.
                var maxCodeStr = await _context.Suppliers
                    .Where(s => s.PortalCode != null && s.PortalCode.StartsWith("SUP-"))
                    .OrderByDescending(s => s.PortalCode)
                    .Select(s => s.PortalCode)
                    .FirstOrDefaultAsync();

                int maxInDb = 0;
                if (maxCodeStr != null && maxCodeStr.Length == 10 && int.TryParse(maxCodeStr.Substring(4), out int parsed))
                {
                    maxInDb = parsed;
                }

                if (counter == null)
                {
                    seqNumber = maxInDb + 1;
                    counter = new Domain.Entities.SystemCounter
                    {
                        Id = counterKey,
                        CurrentValue = seqNumber,
                        LastUpdatedUtc = DateTime.UtcNow
                    };
                    _context.SystemCounters.Add(counter);
                }
                else
                {
                    // If the counter lagging behind the actual data, force it to catch up
                    if (counter.CurrentValue < maxInDb)
                    {
                        counter.CurrentValue = maxInDb;
                    }

                    counter.CurrentValue++;
                    counter.LastUpdatedUtc = DateTime.UtcNow;
                    seqNumber = counter.CurrentValue;
                }

                // 3. Persist changes to the counter
                await _context.SaveChangesAsync();
                
                // 4. Commit the transaction to release the lock
                await transaction.CommitAsync();
            }
            catch (Exception)
            {
                await transaction.RollbackAsync();
                throw;
            }
        }

        // Return precisely formatted code: SUP- + 6 zero-padded digits
        return $"SUP-{seqNumber:D6}";
    }

    [HttpPost("suppliers")]
    [Authorize(Roles = "System Administrator,Buyer,Finance,Contracts,Local Manager")]
    public async Task<IActionResult> CreateSupplier([FromBody] CreateSupplierDto dto)
    {
        // NIF uniqueness guard — when provided, check for existing supplier
        if (!string.IsNullOrWhiteSpace(dto.TaxId))
        {
            var existingByNif = await _context.Suppliers
                .FirstOrDefaultAsync(s => s.TaxId == dto.TaxId.Trim());
            if (existingByNif != null)
            {
                return Conflict(new ProblemDetails
                {
                    Title = "NIF já registado",
                    Detail = $"Já existe um fornecedor com o NIF '{dto.TaxId.Trim()}': {existingByNif.Name} ({existingByNif.PortalCode}). Utilize o fornecedor existente.",
                    Status = 409
                });
            }
        }

        // 1. Generate Portal Code automatically
        var portalCode = await GetNextPortalCodeAsync();

        var actorId = User.FindFirst(System.Security.Claims.ClaimTypes.NameIdentifier)?.Value;

        var entity = new Domain.Entities.Supplier 
        { 
            PortalCode = portalCode,
            PrimaveraCode = dto.PrimaveraCode?.Trim().ToUpper(),
            Name = dto.Name.Trim(), 
            TaxId = dto.TaxId?.Trim(),
            IsActive = true,
            RegistrationStatus = "DRAFT",
            CreatedAtUtc = DateTime.UtcNow,
            CreatedByUserId = actorId
        };

        _context.Suppliers.Add(entity);
        
        try
        {
            await _context.SaveChangesAsync();
            return Created($"/api/v1/lookups/suppliers/{entity.Id}", new { entity.Id, entity.PortalCode, entity.PrimaveraCode, entity.Name, entity.TaxId, entity.IsActive, entity.RegistrationStatus });
        }
        catch (DbUpdateException ex)
        {
            return StatusCode(500, new ProblemDetails { Title = "Erro ao criar Fornecedor", Detail = ex.InnerException?.Message ?? ex.Message });
        }
    }

    [HttpPut("suppliers/{id}")]
    [Authorize(Roles = "System Administrator")]
    public async Task<IActionResult> UpdateSupplier(int id, [FromBody] CreateSupplierDto dto)
    {
        var entity = await _context.Suppliers.FindAsync(id);
        if (entity == null) return NotFound();

        // Historical Immutability Check - only block if fundamental properties change that could break references
        // But here we'll allow name/code updates if not in use yet, or just follow the rule for others.
        // Actually, user only asked for uniqueness and auto-generation.
        
        // Block portal code edit - always keep the original
        // entity.PortalCode stays as is

        entity.PrimaveraCode = dto.PrimaveraCode?.Trim().ToUpper();
        entity.Name = dto.Name.Trim();
        entity.TaxId = dto.TaxId?.Trim();

        try
        {
            await _context.SaveChangesAsync();
            return NoContent();
        }
        catch (DbUpdateException ex)
        {
            return StatusCode(500, new ProblemDetails { Title = "Erro ao atualizar Fornecedor", Detail = ex.InnerException?.Message ?? ex.Message });
        }
    }

    [HttpPut("suppliers/{id}/toggle-active")]
    [Authorize(Roles = "System Administrator")]
    public async Task<IActionResult> ToggleSupplierActive(int id)
    {
        var entity = await _context.Suppliers.FindAsync(id);
        if (entity == null) return NotFound();
        
        entity.IsActive = !entity.IsActive;
        await _context.SaveChangesAsync();
        return NoContent();
    }

    // ─── Ficha de Fornecedor Endpoints ───

    /// <summary>
    /// List supplier fichas with pagination, search, and status filter.
    /// Returns KPI summary counts per registration status.
    /// </summary>
    [HttpGet("suppliers/fichas")]
    [Authorize(Roles = "System Administrator,Buyer,Finance,Contracts,Local Manager")]
    public async Task<IActionResult> GetSupplierFichas(
        [FromQuery] string? search,
        [FromQuery] string? status,
        [FromQuery] string? origin,
        [FromQuery] string? sortBy,
        [FromQuery] string? sortDir,
        [FromQuery] int page = 1,
        [FromQuery] int pageSize = 15)
    {
        var query = _context.Suppliers
            .Include(s => s.Documents.Where(d => !d.IsDeleted))
            .AsNoTracking()
            .AsQueryable();

        // Search filter
        if (!string.IsNullOrWhiteSpace(search))
        {
            var term = search.Trim().ToLower();
            query = query.Where(s =>
                s.Name.ToLower().Contains(term) ||
                (s.TaxId != null && s.TaxId.ToLower().Contains(term)) ||
                (s.PortalCode != null && s.PortalCode.ToLower().Contains(term)) ||
                (s.PrimaveraCode != null && s.PrimaveraCode.ToLower().Contains(term))
            );
        }

        // Status filter
        if (!string.IsNullOrWhiteSpace(status))
        {
            query = query.Where(s => s.RegistrationStatus == status);
        }

        // Origin filter
        if (!string.IsNullOrWhiteSpace(origin))
        {
            query = query.Where(s => s.Origin == origin);
        }

        // KPI counts (before pagination)
        var allStatuses = await _context.Suppliers.AsNoTracking()
            .GroupBy(s => s.RegistrationStatus)
            .Select(g => new { Status = g.Key, Count = g.Count() })
            .ToListAsync();

        var totalAll = allStatuses.Sum(s => s.Count);
        var kpi = new
        {
            total = totalAll,
            draft = allStatuses.FirstOrDefault(s => s.Status == "DRAFT")?.Count ?? 0,
            pendingCompletion = allStatuses.FirstOrDefault(s => s.Status == "PENDING_COMPLETION")?.Count ?? 0,
            active = allStatuses.FirstOrDefault(s => s.Status == "ACTIVE")?.Count ?? 0,
            suspended = allStatuses.FirstOrDefault(s => s.Status == "SUSPENDED")?.Count ?? 0,
            blocked = allStatuses.FirstOrDefault(s => s.Status == "BLOCKED")?.Count ?? 0
        };

        // Sorting
        query = (sortBy?.ToLower(), sortDir?.ToLower()) switch
        {
            ("name", "asc") => query.OrderBy(s => s.Name),
            ("name", "desc") => query.OrderByDescending(s => s.Name),
            ("createdat", "asc") => query.OrderBy(s => s.CreatedAtUtc),
            ("createdat", "desc") => query.OrderByDescending(s => s.CreatedAtUtc),
            ("status", "asc") => query.OrderBy(s => s.RegistrationStatus),
            ("status", "desc") => query.OrderByDescending(s => s.RegistrationStatus),
            _ => query.OrderByDescending(s => s.CreatedAtUtc)
        };

        var totalCount = await query.CountAsync();
        var items = await query
            .Skip((page - 1) * pageSize)
            .Take(pageSize)
            .Select(s => new
            {
                s.Id,
                s.PortalCode,
                s.PrimaveraCode,
                s.Name,
                s.TaxId,
                s.RegistrationStatus,
                s.Origin,
                s.IsActive,
                s.CreatedAtUtc,
                s.Address,
                s.ContactName1,
                s.ContactEmail1,
                s.BankIban,
                s.PaymentTerms,
                s.PaymentMethod,
                documentCount = s.Documents.Count(d => !d.IsDeleted),
                requiredDocTypes = new[] { "CONTRIBUINTE", "CERTIDAO_COMERCIAL", "DIARIO_REPUBLICA", "ALVARA" },
                uploadedDocTypes = s.Documents
                    .Where(d => !d.IsDeleted)
                    .Select(d => d.DocumentType)
                    .Distinct()
                    .ToList(),
                missingDocCount = 4 - s.Documents.Where(d => !d.IsDeleted).Select(d => d.DocumentType).Distinct().Count()
            })
            .ToListAsync();

        return Ok(new { items, totalCount, page, pageSize, kpi });
    }

    /// <summary>
    /// Get full supplier ficha detail including documents.
    /// </summary>
    [HttpGet("suppliers/{id}/ficha")]
    [Authorize(Roles = "System Administrator,Buyer,Finance,Contracts,Local Manager")]
    public async Task<IActionResult> GetSupplierFicha(int id)
    {
        var supplier = await _context.Suppliers
            .Include(s => s.Documents.Where(d => !d.IsDeleted))
                .ThenInclude(d => d.UploadedByUser)
            .AsNoTracking()
            .FirstOrDefaultAsync(s => s.Id == id);

        if (supplier == null) return NotFound();

        var result = new
        {
            supplier.Id,
            supplier.PortalCode,
            supplier.PrimaveraCode,
            supplier.Name,
            supplier.TaxId,
            supplier.Address,
            supplier.RegistrationStatus,
            supplier.Origin,
            supplier.IsActive,

            // Contact 1
            supplier.ContactName1,
            supplier.ContactRole1,
            supplier.ContactPhone1,
            supplier.ContactEmail1,

            // Contact 2
            supplier.ContactName2,
            supplier.ContactRole2,
            supplier.ContactPhone2,
            supplier.ContactEmail2,

            // Banking
            supplier.BankAccountNumber,
            supplier.BankIban,
            supplier.BankSwift,

            // Commercial
            supplier.PaymentTerms,
            supplier.PaymentMethod,

            // Audit
            supplier.CreatedAtUtc,
            supplier.CreatedByUserId,
            supplier.UpdatedAtUtc,
            supplier.UpdatedByUserId,
            supplier.Notes,

            // Documents
            documents = supplier.Documents.Select(d => new
            {
                d.Id,
                d.DocumentType,
                d.FileName,
                d.ContentType,
                d.FileSizeBytes,
                d.UploadedAtUtc,
                uploadedByUserName = d.UploadedByUser?.FullName ?? "Desconhecido"
            }).OrderBy(d => d.DocumentType).ToList(),

            // Document completeness
            requiredDocTypes = new[] { "CONTRIBUINTE", "CERTIDAO_COMERCIAL", "DIARIO_REPUBLICA", "ALVARA" },
            uploadedDocTypes = supplier.Documents.Select(d => d.DocumentType).Distinct().ToList()
        };

        return Ok(result);
    }

    /// <summary>
    /// Update extended supplier ficha fields.
    /// </summary>
    [HttpPut("suppliers/{id}/ficha")]
    [Authorize(Roles = "System Administrator,Buyer,Finance,Contracts,Local Manager")]
    public async Task<IActionResult> UpdateSupplierFicha(int id, [FromBody] UpdateSupplierFichaDto dto)
    {
        var entity = await _context.Suppliers.FindAsync(id);
        if (entity == null) return NotFound();

        var actorId = User.FindFirst(System.Security.Claims.ClaimTypes.NameIdentifier)?.Value;

        // NIF uniqueness guard on update
        if (!string.IsNullOrWhiteSpace(dto.TaxId))
        {
            var existingByNif = await _context.Suppliers
                .FirstOrDefaultAsync(s => s.TaxId == dto.TaxId.Trim() && s.Id != id);
            if (existingByNif != null)
            {
                return Conflict(new ProblemDetails
                {
                    Title = "NIF já registado",
                    Detail = $"O NIF '{dto.TaxId.Trim()}' já está atribuído ao fornecedor: {existingByNif.Name} ({existingByNif.PortalCode}).",
                    Status = 409
                });
            }
        }

        // Update fields
        entity.Name = dto.Name?.Trim() ?? entity.Name;
        entity.TaxId = dto.TaxId?.Trim();
        entity.PrimaveraCode = dto.PrimaveraCode?.Trim().ToUpper();
        entity.Address = dto.Address?.Trim();

        entity.ContactName1 = dto.ContactName1?.Trim();
        entity.ContactRole1 = dto.ContactRole1?.Trim();
        entity.ContactPhone1 = dto.ContactPhone1?.Trim();
        entity.ContactEmail1 = dto.ContactEmail1?.Trim();

        entity.ContactName2 = dto.ContactName2?.Trim();
        entity.ContactRole2 = dto.ContactRole2?.Trim();
        entity.ContactPhone2 = dto.ContactPhone2?.Trim();
        entity.ContactEmail2 = dto.ContactEmail2?.Trim();

        entity.BankAccountNumber = dto.BankAccountNumber?.Trim();
        entity.BankIban = dto.BankIban?.Trim();
        entity.BankSwift = dto.BankSwift?.Trim();

        entity.PaymentTerms = dto.PaymentTerms?.Trim();
        entity.PaymentMethod = dto.PaymentMethod?.Trim();

        entity.Notes = dto.Notes?.Trim();

        entity.UpdatedAtUtc = DateTime.UtcNow;
        entity.UpdatedByUserId = actorId;

        try
        {
            await _context.SaveChangesAsync();
            return NoContent();
        }
        catch (DbUpdateException ex)
        {
            return StatusCode(500, new ProblemDetails { Title = "Erro ao atualizar Ficha", Detail = ex.InnerException?.Message ?? ex.Message });
        }
    }

    /// <summary>
    /// Change supplier registration status.
    /// Validates transitions per SupplierConstants.AllowedTransitions.
    /// Completeness validation enforced for transitions to PENDING_APPROVAL.
    /// </summary>
    [HttpPut("suppliers/{id}/status")]
    [Authorize(Roles = "System Administrator,Buyer,Finance,Contracts,Local Manager")]
    public async Task<IActionResult> UpdateSupplierStatus(int id, [FromBody] UpdateSupplierStatusDto dto)
    {
        var entity = await _context.Suppliers
            .Include(s => s.Documents.Where(d => !d.IsDeleted))
            .FirstOrDefaultAsync(s => s.Id == id);
        if (entity == null) return NotFound();

        if (!Domain.Constants.SupplierConstants.AllowedTransitions.TryGetValue(entity.RegistrationStatus, out var validTargets) ||
            !validTargets.Contains(dto.NewStatus))
        {
            return BadRequest(new ProblemDetails
            {
                Title = "Transição Inválida",
                Detail = $"Não é possível alterar o estado de '{entity.RegistrationStatus}' para '{dto.NewStatus}'.",
                Status = 400
            });
        }

        // Completeness validation for approval submission
        if (dto.NewStatus == "PENDING_APPROVAL")
        {
            var completeness = BuildCompletenessReport(entity);
            if (!completeness.IsComplete)
            {
                return BadRequest(new ProblemDetails
                {
                    Title = "Ficha Incompleta",
                    Detail = $"Para submeter para aprovação, preencha: {string.Join(", ", completeness.MissingItems)}.",
                    Status = 400
                });
            }
        }

        var actorId = User.FindFirst(System.Security.Claims.ClaimTypes.NameIdentifier)?.Value;
        var previousStatus = entity.RegistrationStatus;
        entity.RegistrationStatus = dto.NewStatus;
        entity.UpdatedAtUtc = DateTime.UtcNow;
        entity.UpdatedByUserId = actorId;

        // Record audit trail
        var historyEvent = new Domain.Entities.SupplierStatusHistory
        {
            SupplierId = entity.Id,
            EventType = Domain.Constants.SupplierConstants.HistoryEventTypes.StatusChanged,
            FromStatusCode = previousStatus,
            ToStatusCode = dto.NewStatus,
            Comment = dto.Comment ?? string.Empty,
            OccurredAtUtc = DateTime.UtcNow,
            ActorUserId = Guid.Parse(actorId!)
        };
        _context.SupplierStatusHistories.Add(historyEvent);

        await _context.SaveChangesAsync();
        return Ok(new { entity.Id, entity.RegistrationStatus });
    }

    /// <summary>
    /// Upload a required document for a supplier ficha.
    /// </summary>
    [HttpPost("suppliers/{id}/documents")]
    [Authorize(Roles = "System Administrator,Buyer,Finance,Contracts,Local Manager")]
    public async Task<IActionResult> UploadSupplierDocument(int id, [FromForm] IFormFile file, [FromForm] string documentType)
    {
        var supplier = await _context.Suppliers.FindAsync(id);
        if (supplier == null) return NotFound();

        var validTypes = new[] { "CONTRIBUINTE", "CERTIDAO_COMERCIAL", "DIARIO_REPUBLICA", "ALVARA" };
        if (!validTypes.Contains(documentType))
        {
            return BadRequest(new ProblemDetails
            {
                Title = "Tipo de Documento Inválido",
                Detail = $"Tipo '{documentType}' não é válido. Tipos aceites: {string.Join(", ", validTypes)}.",
                Status = 400
            });
        }

        if (file == null || file.Length == 0)
            return BadRequest("Nenhum arquivo enviado.");

        var extension = Path.GetExtension(file.FileName).ToLowerInvariant();
        var allowedExts = new[] { ".pdf", ".jpg", ".jpeg", ".png", ".doc", ".docx" };
        if (!allowedExts.Contains(extension))
        {
            return BadRequest($"Extensão não permitida: {extension}. Permitidos: {string.Join(", ", allowedExts)}.");
        }

        if (file.Length > 10 * 1024 * 1024) // 10 MB limit
        {
            return BadRequest("O ficheiro excede o limite de 10MB.");
        }

        // Storage path: data/attachments/suppliers/{supplierId}/
        var env = HttpContext.RequestServices.GetRequiredService<IWebHostEnvironment>();
        var storagePath = Path.GetFullPath(Path.Combine(env.ContentRootPath, "..", "..", "..", "data", "attachments", "suppliers", id.ToString()));
        if (!Directory.Exists(storagePath)) Directory.CreateDirectory(storagePath);

        var fileId = Guid.NewGuid();
        var storageFileName = $"{fileId}{extension}";
        var filePath = Path.Combine(storagePath, storageFileName);

        string fileHash;
        using (var stream = new FileStream(filePath, FileMode.Create))
        {
            await file.CopyToAsync(stream);
        }
        using (var streamForHash = new FileStream(filePath, FileMode.Open, FileAccess.Read))
        using (var sha256 = System.Security.Cryptography.SHA256.Create())
        {
            var hashBytes = await sha256.ComputeHashAsync(streamForHash);
            fileHash = BitConverter.ToString(hashBytes).Replace("-", "").ToLowerInvariant();
        }

        var actorId = Guid.Parse(User.FindFirst(System.Security.Claims.ClaimTypes.NameIdentifier)!.Value);

        // Soft-delete any existing document of the same type (replace behavior)
        var existing = await _context.SupplierDocuments
            .Where(d => d.SupplierId == id && d.DocumentType == documentType && !d.IsDeleted)
            .ToListAsync();
        foreach (var old in existing)
        {
            old.IsDeleted = true;
            old.DeletedAtUtc = DateTime.UtcNow;
            old.DeletedByUserId = actorId;
        }

        var doc = new Domain.Entities.SupplierDocument
        {
            Id = fileId,
            SupplierId = id,
            DocumentType = documentType,
            FileName = file.FileName,
            StoragePath = $"suppliers/{id}/{storageFileName}",
            ContentType = file.ContentType,
            FileSizeBytes = file.Length,
            FileHash = fileHash,
            UploadedAtUtc = DateTime.UtcNow,
            UploadedByUserId = actorId
        };

        _context.SupplierDocuments.Add(doc);
        supplier.UpdatedAtUtc = DateTime.UtcNow;
        supplier.UpdatedByUserId = actorId.ToString();

        await _context.SaveChangesAsync();

        return Ok(new { doc.Id, doc.DocumentType, doc.FileName, doc.FileSizeBytes, doc.UploadedAtUtc });
    }

    /// <summary>
    /// Download a supplier document by ID.
    /// </summary>
    [HttpGet("suppliers/documents/{documentId}/download")]
    [Authorize(Roles = "System Administrator,Buyer,Finance,Contracts,Local Manager")]
    public async Task<IActionResult> DownloadSupplierDocument(Guid documentId)
    {
        var doc = await _context.SupplierDocuments
            .FirstOrDefaultAsync(d => d.Id == documentId && !d.IsDeleted);
        if (doc == null) return NotFound();

        var env = HttpContext.RequestServices.GetRequiredService<IWebHostEnvironment>();
        var storagePath = Path.GetFullPath(Path.Combine(env.ContentRootPath, "..", "..", "..", "data", "attachments", doc.StoragePath));
        if (!System.IO.File.Exists(storagePath)) return NotFound("Arquivo físico não encontrado.");

        var bytes = await System.IO.File.ReadAllBytesAsync(storagePath);
        return File(bytes, doc.ContentType ?? "application/octet-stream", doc.FileName);
    }

    /// <summary>
    /// Delete (soft) a supplier document.
    /// </summary>
    [HttpDelete("suppliers/documents/{documentId}")]
    [Authorize(Roles = "System Administrator,Buyer,Finance,Contracts,Local Manager")]
    public async Task<IActionResult> DeleteSupplierDocument(Guid documentId)
    {
        var doc = await _context.SupplierDocuments.FirstOrDefaultAsync(d => d.Id == documentId && !d.IsDeleted);
        if (doc == null) return NotFound();

        var actorId = Guid.Parse(User.FindFirst(System.Security.Claims.ClaimTypes.NameIdentifier)!.Value);
        doc.IsDeleted = true;
        doc.DeletedAtUtc = DateTime.UtcNow;
        doc.DeletedByUserId = actorId;

        var supplier = await _context.Suppliers.FindAsync(doc.SupplierId);
        if (supplier != null)
        {
            supplier.UpdatedAtUtc = DateTime.UtcNow;
            supplier.UpdatedByUserId = actorId.ToString();
        }

        await _context.SaveChangesAsync();
        return NoContent();
    }

    // ═══════════════════════════════════════════════════════════════════════════
    //  SUPPLIER APPROVAL WORKFLOW (Phase 2 — DAF/DG two-step)
    // ═══════════════════════════════════════════════════════════════════════════


    /// <summary>
    /// Get all suppliers pending approval (for Approval Center).
    /// </summary>
    [HttpGet("suppliers/pending-approval")]
    [Authorize(Roles = "System Administrator,Final Approver")]
    public async Task<IActionResult> GetPendingSupplierApprovals()
    {
        var suppliers = await _context.Suppliers
            .Include(s => s.Documents.Where(d => !d.IsDeleted))
            .Where(s => s.RegistrationStatus == "PENDING_APPROVAL")
            .OrderBy(s => s.SubmittedAtUtc)
            .ToListAsync();

        var submitterIds = suppliers.Select(s => s.SubmittedByUserId).Where(id => id.HasValue).Select(id => id!.Value).Distinct().ToList();
        var users = await _context.Users.Where(u => submitterIds.Contains(u.Id)).ToDictionaryAsync(u => u.Id, u => u.FullName);

        var result = suppliers.Select(s =>
        {
            var comp = BuildCompletenessReport(s);
            var requiredDocs = new[] { "CONTRIBUINTE", "CERTIDAO_COMERCIAL", "DIARIO_REPUBLICA", "ALVARA" };
            var uploadedTypes = s.Documents.Select(d => d.DocumentType).Distinct().ToList();
            var missingDocs = requiredDocs.Where(dt => !uploadedTypes.Contains(dt)).ToList();
            var submitterName = s.SubmittedByUserId.HasValue && users.ContainsKey(s.SubmittedByUserId.Value) ? users[s.SubmittedByUserId.Value] : "—";

            return new
            {
                s.Id,
                s.Name,
                s.TaxId,
                s.PortalCode,
                s.RegistrationStatus,
                SubmittedByUserName = submitterName,
                s.SubmittedAtUtc,
                CompletionPercentage = comp.CompletionPercentage,
                MissingItems = comp.MissingItems,
                DocumentSummary = new { Total = requiredDocs.Length, Uploaded = requiredDocs.Length - missingDocs.Count, Missing = missingDocs },
                s.ContactName1,
                s.ContactEmail1,
                s.ContactPhone1,
                s.BankIban,
                s.PaymentTerms,
                s.PaymentMethod,
                s.Address,
                s.BankAccountNumber,
                s.BankSwift,
            };
        }).ToList();

        return Ok(new { pendingFichas = result });
    }

    /// <summary>
    /// Submit supplier for Final Approver approval. Validates completeness first.
    /// </summary>
    [HttpPost("suppliers/{id}/submit-approval")]
    [Authorize(Roles = "System Administrator,Buyer,Finance,Contracts,Local Manager")]
    public async Task<IActionResult> SubmitSupplierForApproval(int id)
    {
        var entity = await _context.Suppliers
            .Include(s => s.Documents.Where(d => !d.IsDeleted))
            .FirstOrDefaultAsync(s => s.Id == id);
        if (entity == null) return NotFound();

        // Only DRAFT, PENDING_COMPLETION, or ADJUSTMENT_REQUESTED can submit
        var submittable = new[] { "DRAFT", "PENDING_COMPLETION", "ADJUSTMENT_REQUESTED" };
        if (!submittable.Contains(entity.RegistrationStatus))
        {
            return BadRequest(new ProblemDetails
            {
                Title = "Transição Inválida",
                Detail = $"Não é possível submeter para aprovação a partir do estado '{entity.RegistrationStatus}'.",
                Status = 400
            });
        }

        // Validate completeness
        var completeness = BuildCompletenessReport(entity);
        if (!completeness.IsComplete)
        {
            return BadRequest(new ProblemDetails
            {
                Title = "Ficha Incompleta",
                Detail = $"Para submeter para aprovação, preencha: {string.Join(", ", completeness.MissingItems)}.",
                Status = 400
            });
        }

        // Resolve Final Approver (mapped to "Final Approver" role, stored as DG for DB compatibility)
        var finalApprover = await _context.UserRoleAssignments
            .Where(ura => ura.Role.RoleName == "Final Approver")
            .Select(ura => ura.User)
            .Where(u => u.IsActive)
            .FirstOrDefaultAsync();
        if (finalApprover == null)
        {
            return BadRequest(new ProblemDetails
            {
                Title = "Aprovador Final Não Encontrado",
                Detail = "Nenhum utilizador com o papel 'Final Approver' está ativo. Configure um aprovador antes de submeter.",
                Status = 400
            });
        }

        var actorId = Guid.Parse(User.FindFirst(System.Security.Claims.ClaimTypes.NameIdentifier)!.Value);
        var previousStatus = entity.RegistrationStatus;

        entity.RegistrationStatus = "PENDING_APPROVAL";
        // Auto-stamp DAF as internal system step (not a real DAF approval)
        entity.DafApproverId = actorId;
        entity.DafApprovedAtUtc = DateTime.UtcNow;
        // Assign Final Approver
        entity.DgApproverId = finalApprover.Id;
        entity.DgApprovedAtUtc = null;
        entity.SubmittedByUserId = actorId;
        entity.SubmittedAtUtc = DateTime.UtcNow;
        entity.AdjustmentComment = null;
        entity.UpdatedAtUtc = DateTime.UtcNow;
        entity.UpdatedByUserId = actorId.ToString();

        _context.SupplierStatusHistories.Add(new Domain.Entities.SupplierStatusHistory
        {
            SupplierId = entity.Id,
            EventType = Domain.Constants.SupplierConstants.HistoryEventTypes.SubmittedForApproval,
            FromStatusCode = previousStatus,
            ToStatusCode = "PENDING_APPROVAL",
            Comment = $"Submetido para aprovação. Aprovador Final: {finalApprover.FullName}.",
            OccurredAtUtc = DateTime.UtcNow,
            ActorUserId = actorId
        });

        await _context.SaveChangesAsync();

        return Ok(new
        {
            entity.Id,
            entity.RegistrationStatus,
            FinalApproverName = finalApprover.FullName,
            entity.SubmittedAtUtc
        });
    }

    /// <summary>
    /// DAF approves supplier registration (Stage 1).
    /// </summary>
    [HttpPost("suppliers/{id}/daf-approve")]
    [Authorize(Roles = "System Administrator,Area Approver")]
    public async Task<IActionResult> DafApproveSupplier(int id)
    {
        var entity = await _context.Suppliers.FindAsync(id);
        if (entity == null) return NotFound();

        if (entity.RegistrationStatus != "PENDING_APPROVAL")
            return BadRequest(new ProblemDetails { Title = "Estado Inválido", Detail = "O fornecedor não está pendente de aprovação.", Status = 400 });

        if (entity.DafApprovedAtUtc != null)
            return BadRequest(new ProblemDetails { Title = "Já Aprovado", Detail = "A aprovação DAF já foi registada.", Status = 400 });

        var actorId = Guid.Parse(User.FindFirst(System.Security.Claims.ClaimTypes.NameIdentifier)!.Value);

        entity.DafApprovedAtUtc = DateTime.UtcNow;
        entity.UpdatedAtUtc = DateTime.UtcNow;
        entity.UpdatedByUserId = actorId.ToString();

        _context.SupplierStatusHistories.Add(new Domain.Entities.SupplierStatusHistory
        {
            SupplierId = entity.Id,
            EventType = Domain.Constants.SupplierConstants.HistoryEventTypes.DafApproved,
            FromStatusCode = "PENDING_APPROVAL",
            ToStatusCode = "PENDING_APPROVAL",
            Comment = "Aprovação DAF concedida.",
            OccurredAtUtc = DateTime.UtcNow,
            ActorUserId = actorId
        });

        // Check if DG also already approved (shouldn't happen normally, but handle gracefully)
        if (entity.DgApprovedAtUtc != null)
        {
            entity.RegistrationStatus = "ACTIVE";
            entity.IsActive = true;
            _context.SupplierStatusHistories.Add(new Domain.Entities.SupplierStatusHistory
            {
                SupplierId = entity.Id,
                EventType = Domain.Constants.SupplierConstants.HistoryEventTypes.Activated,
                FromStatusCode = "PENDING_APPROVAL",
                ToStatusCode = "ACTIVE",
                Comment = "Fornecedor ativado após aprovação DAF e DG.",
                OccurredAtUtc = DateTime.UtcNow,
                ActorUserId = actorId
            });
        }

        await _context.SaveChangesAsync();
        return Ok(new { entity.Id, entity.RegistrationStatus, entity.DafApprovedAtUtc });
    }

    /// <summary>
    /// DAF returns supplier for adjustment (Stage 1). Mandatory comment.
    /// </summary>
    [HttpPost("suppliers/{id}/daf-return")]
    [Authorize(Roles = "System Administrator,Area Approver")]
    public async Task<IActionResult> DafReturnSupplier(int id, [FromBody] SupplierApprovalCommentDto dto)
    {
        if (string.IsNullOrWhiteSpace(dto.Comment))
            return BadRequest(new ProblemDetails { Title = "Comentário Obrigatório", Detail = "É necessário justificar o pedido de reajuste.", Status = 400 });

        var entity = await _context.Suppliers.FindAsync(id);
        if (entity == null) return NotFound();

        if (entity.RegistrationStatus != "PENDING_APPROVAL")
            return BadRequest(new ProblemDetails { Title = "Estado Inválido", Detail = "O fornecedor não está pendente de aprovação.", Status = 400 });

        var actorId = Guid.Parse(User.FindFirst(System.Security.Claims.ClaimTypes.NameIdentifier)!.Value);

        entity.RegistrationStatus = "ADJUSTMENT_REQUESTED";
        entity.AdjustmentComment = dto.Comment;
        entity.DafApprovedAtUtc = null;
        entity.DgApprovedAtUtc = null;
        entity.UpdatedAtUtc = DateTime.UtcNow;
        entity.UpdatedByUserId = actorId.ToString();

        _context.SupplierStatusHistories.Add(new Domain.Entities.SupplierStatusHistory
        {
            SupplierId = entity.Id,
            EventType = Domain.Constants.SupplierConstants.HistoryEventTypes.AdjustmentRequested,
            FromStatusCode = "PENDING_APPROVAL",
            ToStatusCode = "ADJUSTMENT_REQUESTED",
            Comment = dto.Comment,
            OccurredAtUtc = DateTime.UtcNow,
            ActorUserId = actorId
        });

        await _context.SaveChangesAsync();
        return Ok(new { entity.Id, entity.RegistrationStatus, entity.AdjustmentComment });
    }

    /// <summary>
    /// DG approves supplier registration (Stage 2). If DAF already approved, activates supplier.
    /// </summary>
    [HttpPost("suppliers/{id}/dg-approve")]
    [Authorize(Roles = "System Administrator,Final Approver")]
    public async Task<IActionResult> DgApproveSupplier(int id)
    {
        var entity = await _context.Suppliers.FindAsync(id);
        if (entity == null) return NotFound();

        if (entity.RegistrationStatus != "PENDING_APPROVAL")
            return BadRequest(new ProblemDetails { Title = "Estado Inválido", Detail = "O fornecedor não está pendente de aprovação.", Status = 400 });

        if (entity.DgApprovedAtUtc != null)
            return BadRequest(new ProblemDetails { Title = "Já Aprovado", Detail = "A aprovação DG já foi registada.", Status = 400 });

        var actorId = Guid.Parse(User.FindFirst(System.Security.Claims.ClaimTypes.NameIdentifier)!.Value);

        entity.DgApprovedAtUtc = DateTime.UtcNow;
        entity.UpdatedAtUtc = DateTime.UtcNow;
        entity.UpdatedByUserId = actorId.ToString();

        _context.SupplierStatusHistories.Add(new Domain.Entities.SupplierStatusHistory
        {
            SupplierId = entity.Id,
            EventType = Domain.Constants.SupplierConstants.HistoryEventTypes.DgApproved,
            FromStatusCode = "PENDING_APPROVAL",
            ToStatusCode = entity.DafApprovedAtUtc != null ? "ACTIVE" : "PENDING_APPROVAL",
            Comment = "Aprovação DG concedida.",
            OccurredAtUtc = DateTime.UtcNow,
            ActorUserId = actorId
        });

        // Both approved → activate
        if (entity.DafApprovedAtUtc != null)
        {
            entity.RegistrationStatus = "ACTIVE";
            entity.IsActive = true;
            _context.SupplierStatusHistories.Add(new Domain.Entities.SupplierStatusHistory
            {
                SupplierId = entity.Id,
                EventType = Domain.Constants.SupplierConstants.HistoryEventTypes.Activated,
                FromStatusCode = "PENDING_APPROVAL",
                ToStatusCode = "ACTIVE",
                Comment = "Fornecedor ativado após aprovação DAF e DG.",
                OccurredAtUtc = DateTime.UtcNow,
                ActorUserId = actorId
            });
        }

        await _context.SaveChangesAsync();
        return Ok(new { entity.Id, entity.RegistrationStatus, entity.DgApprovedAtUtc });
    }

    /// <summary>
    /// DG returns supplier for adjustment (Stage 2). Mandatory comment.
    /// </summary>
    [HttpPost("suppliers/{id}/dg-return")]
    [Authorize(Roles = "System Administrator,Final Approver")]
    public async Task<IActionResult> DgReturnSupplier(int id, [FromBody] SupplierApprovalCommentDto dto)
    {
        if (string.IsNullOrWhiteSpace(dto.Comment))
            return BadRequest(new ProblemDetails { Title = "Comentário Obrigatório", Detail = "É necessário justificar o pedido de reajuste.", Status = 400 });

        var entity = await _context.Suppliers.FindAsync(id);
        if (entity == null) return NotFound();

        if (entity.RegistrationStatus != "PENDING_APPROVAL")
            return BadRequest(new ProblemDetails { Title = "Estado Inválido", Detail = "O fornecedor não está pendente de aprovação.", Status = 400 });

        var actorId = Guid.Parse(User.FindFirst(System.Security.Claims.ClaimTypes.NameIdentifier)!.Value);

        entity.RegistrationStatus = "ADJUSTMENT_REQUESTED";
        entity.AdjustmentComment = dto.Comment;
        entity.DafApprovedAtUtc = null;
        entity.DgApprovedAtUtc = null;
        entity.UpdatedAtUtc = DateTime.UtcNow;
        entity.UpdatedByUserId = actorId.ToString();

        _context.SupplierStatusHistories.Add(new Domain.Entities.SupplierStatusHistory
        {
            SupplierId = entity.Id,
            EventType = Domain.Constants.SupplierConstants.HistoryEventTypes.AdjustmentRequested,
            FromStatusCode = "PENDING_APPROVAL",
            ToStatusCode = "ADJUSTMENT_REQUESTED",
            Comment = dto.Comment,
            OccurredAtUtc = DateTime.UtcNow,
            ActorUserId = actorId
        });

        await _context.SaveChangesAsync();
        return Ok(new { entity.Id, entity.RegistrationStatus, entity.AdjustmentComment });
    }

    /// <summary>
    /// Get supplier completeness report — shows mandatory field/document status.
    /// </summary>
    [HttpGet("suppliers/{id}/completeness")]
    [Authorize(Roles = "System Administrator,Buyer,Finance,Contracts,Local Manager,Area Approver,Final Approver")]
    public async Task<IActionResult> GetSupplierCompleteness(int id)
    {
        var entity = await _context.Suppliers
            .Include(s => s.Documents.Where(d => !d.IsDeleted))
            .FirstOrDefaultAsync(s => s.Id == id);
        if (entity == null) return NotFound();

        var report = BuildCompletenessReport(entity);
        return Ok(report);
    }

    /// <summary>
    /// Get supplier audit history timeline.
    /// </summary>
    [HttpGet("suppliers/{id}/history")]
    [Authorize(Roles = "System Administrator,Buyer,Finance,Contracts,Local Manager,Area Approver,Final Approver")]
    public async Task<IActionResult> GetSupplierHistory(int id)
    {
        var supplier = await _context.Suppliers.FindAsync(id);
        if (supplier == null) return NotFound();

        var history = await _context.SupplierStatusHistories
            .Where(h => h.SupplierId == id)
            .OrderByDescending(h => h.OccurredAtUtc)
            .Include(h => h.ActorUser)
            .Select(h => new
            {
                h.Id,
                h.EventType,
                h.FromStatusCode,
                h.ToStatusCode,
                h.Comment,
                h.OccurredAtUtc,
                h.ActorUserId,
                ActorName = h.ActorUser.FullName
            })
            .ToListAsync();

        return Ok(history);
    }

    /// <summary>
    /// Check supplier registration status — used by P.O./Contract flows for operational restrictions.
    /// Returns supplier status and whether the operation is allowed/warned/blocked.
    /// </summary>
    [HttpGet("suppliers/{id}/registration-check")]
    [Authorize]
    public async Task<IActionResult> CheckSupplierRegistration(int id, [FromQuery] string operation = "po")
    {
        var entity = await _context.Suppliers
            .Select(s => new { s.Id, s.Name, s.RegistrationStatus })
            .FirstOrDefaultAsync(s => s.Id == id);
        if (entity == null) return NotFound();

        var status = entity.RegistrationStatus;
        var result = operation.ToLowerInvariant() switch
        {
            "po" => status switch
            {
                "ACTIVE" => new { Allowed = true, Warning = false, Blocked = false, Message = "" },
                "PENDING_APPROVAL" => new { Allowed = true, Warning = true, Blocked = false, Message = $"O fornecedor \"{entity.Name}\" está pendente de aprovação. A P.O. poderá ser emitida, mas recomenda-se aguardar a aprovação." },
                _ => new { Allowed = false, Warning = false, Blocked = true, Message = $"O fornecedor \"{entity.Name}\" não está ativo (estado: {Domain.Constants.SupplierConstants.StatusLabels.GetValueOrDefault(status, status)}). Não é possível emitir P.O." }
            },
            "contract" => status switch
            {
                "ACTIVE" => new { Allowed = true, Warning = false, Blocked = false, Message = "" },
                _ => new { Allowed = false, Warning = false, Blocked = true, Message = $"Apenas fornecedores ativos podem ser associados a contratos. Estado atual: {Domain.Constants.SupplierConstants.StatusLabels.GetValueOrDefault(status, status)}." }
            },
            "payment" => status switch
            {
                "ACTIVE" => new { Allowed = true, Warning = false, Blocked = false, Message = "" },
                _ => new { Allowed = false, Warning = false, Blocked = true, Message = $"Apenas fornecedores ativos podem receber pagamentos. Estado atual: {Domain.Constants.SupplierConstants.StatusLabels.GetValueOrDefault(status, status)}." }
            },
            "winner" => status switch
            {
                "ACTIVE" => new { Allowed = true, Warning = false, Blocked = false, Message = "" },
                _ => new { Allowed = true, Warning = true, Blocked = false, Message = $"O fornecedor \"{entity.Name}\" não está ativo (estado: {Domain.Constants.SupplierConstants.StatusLabels.GetValueOrDefault(status, status)}). A emissão de P.O. e criação de contratos será restrita até a ativação." }
            },
            _ => new { Allowed = true, Warning = false, Blocked = false, Message = "" }
        };

        return Ok(new { entity.Id, entity.Name, entity.RegistrationStatus, result.Allowed, result.Warning, result.Blocked, result.Message });
    }

    /// <summary>
    /// Builds a completeness report for a supplier ficha.
    /// Checks mandatory fields and required documents (excluding Alvará which is optional).
    /// </summary>
    private SupplierCompletenessReport BuildCompletenessReport(Domain.Entities.Supplier s)
    {
        var checks = new List<(string Category, string Label, bool IsComplete)>
        {
            ("identification", "Denominação Social", !string.IsNullOrWhiteSpace(s.Name)),
            ("identification", "NIF / Contribuinte", !string.IsNullOrWhiteSpace(s.TaxId)),
            ("identification", "Morada", !string.IsNullOrWhiteSpace(s.Address)),
            ("contact", "Nome do Contacto Principal", !string.IsNullOrWhiteSpace(s.ContactName1)),
            ("contact", "Telefone ou E-mail do Contacto", !string.IsNullOrWhiteSpace(s.ContactPhone1) || !string.IsNullOrWhiteSpace(s.ContactEmail1)),
            ("banking", "Conta Bancária", !string.IsNullOrWhiteSpace(s.BankAccountNumber)),
            ("banking", "IBAN", !string.IsNullOrWhiteSpace(s.BankIban)),
            ("commercial", "Condições de Pagamento", !string.IsNullOrWhiteSpace(s.PaymentTerms)),
            ("commercial", "Forma de Pagamento", !string.IsNullOrWhiteSpace(s.PaymentMethod)),
        };

        var uploadedTypes = s.Documents?.Where(d => !d.IsDeleted).Select(d => d.DocumentType).Distinct().ToList() ?? new List<string>();
        foreach (var mandatoryDoc in Domain.Constants.SupplierConstants.MandatoryDocumentTypes)
        {
            var docLabel = mandatoryDoc switch
            {
                "CONTRIBUINTE" => "Documento: Contribuinte",
                "CERTIDAO_COMERCIAL" => "Documento: Certidão Comercial",
                "DIARIO_REPUBLICA" => "Documento: Diário da República",
                _ => $"Documento: {mandatoryDoc}"
            };
            checks.Add(("documents", docLabel, uploadedTypes.Contains(mandatoryDoc)));
        }

        var totalChecks = checks.Count;
        var completedChecks = checks.Count(c => c.IsComplete);
        var missingItems = checks.Where(c => !c.IsComplete).Select(c => c.Label).ToList();

        // Category-level completeness
        var categories = checks
            .GroupBy(c => c.Category)
            .Select(g => new SupplierCompletenessCategory
            {
                Category = g.Key,
                Label = g.Key switch
                {
                    "identification" => "Identificação",
                    "contact" => "Contacto Principal",
                    "banking" => "Dados Bancários",
                    "commercial" => "Condições Comerciais",
                    "documents" => "Documentos Obrigatórios",
                    _ => g.Key
                },
                TotalItems = g.Count(),
                CompletedItems = g.Count(c => c.IsComplete),
                IsComplete = g.All(c => c.IsComplete),
                MissingItems = g.Where(c => !c.IsComplete).Select(c => c.Label).ToList()
            })
            .ToList();

        // Alvará info (optional — not blocking)
        var hasAlvara = uploadedTypes.Contains("ALVARA");

        return new SupplierCompletenessReport
        {
            TotalChecks = totalChecks,
            CompletedChecks = completedChecks,
            CompletionPercentage = totalChecks > 0 ? (int)Math.Round(100.0 * completedChecks / totalChecks) : 0,
            IsComplete = missingItems.Count == 0,
            MissingItems = missingItems,
            Categories = categories,
            HasAlvara = hasAlvara,
            AlvaraNote = hasAlvara ? null : "Alvará não enviado. Recomendado se aplicável à atividade do fornecedor."
        };
    }

    /// <summary>
    /// Returns the 3 business priority options for line items.
    /// Static list — not stored in DB as it is a fixed classification.
    /// </summary>
    [HttpGet("item-priorities")]
    public IActionResult GetItemPriorities()
    {
        var priorities = new[]
        {
            new { Code = "HIGH",   Name = "Alta",  DisplayOrder = 1 },
            new { Code = "MEDIUM", Name = "Média", DisplayOrder = 2 },
            new { Code = "LOW",    Name = "Baixa", DisplayOrder = 3 }
        };
        return Ok(priorities);
    }

    /// <summary>
    /// Returns all active line item statuses.
    /// Read-only for requesters. Buyer-editable in future phases.
    /// </summary>
    [HttpGet("line-item-statuses")]
    public async Task<IActionResult> GetLineItemStatuses([FromQuery] bool includeInactive = false)
    {
        var query = _context.LineItemStatuses.AsQueryable();
        if (!includeInactive) query = query.Where(s => s.IsActive);

        var items = await query
            .OrderBy(s => s.DisplayOrder)
            .Select(s => new { s.Id, s.Code, s.Name, s.DisplayOrder, s.BadgeColor, s.IsActive })
            .ToListAsync();

        return Ok(items);
    }

    // Cost Centers
    [HttpGet("cost-centers")]
    public async Task<IActionResult> GetCostCenters([FromQuery] int? plantId, [FromQuery] bool includeInactive = false)
    {
        var query = _context.CostCenters.AsQueryable();

        if (plantId.HasValue)
        {
            query = query.Where(c => c.PlantId == plantId.Value);
        }

        if (!includeInactive)
        {
            query = query.Where(c => c.IsActive);
        }

        var items = await query
            .OrderBy(c => c.Name)
            .Select(c => new { c.Id, c.Code, c.Name, c.IsActive, c.PlantId, PlantName = c.Plant.Name })
            .ToListAsync();

        return Ok(items);
    }

    [HttpGet("cost-centers/search")]
    public async Task<IActionResult> SearchCostCenters([FromQuery] string? q, [FromQuery] int? plantId, [FromQuery] int take = 20)
    {
        var queryable = _context.CostCenters.Where(c => c.IsActive);

        if (plantId.HasValue)
        {
            queryable = queryable.Where(c => c.PlantId == plantId.Value);
        }

        if (!string.IsNullOrWhiteSpace(q) && q.Length >= 1)
        {
            var queryText = q.ToUpper();
            queryable = queryable.Where(c =>
                c.Code.Contains(queryText) ||
                c.Name.ToUpper().Contains(queryText)
            );
        }
        else
        {
            take = 5;
        }

        var results = await queryable
            .OrderBy(c => c.Name)
            .Take(take)
            .Select(c => new { c.Id, c.Code, c.Name, c.PlantId })
            .ToListAsync();

        return Ok(results);
    }

    [HttpPost("cost-centers")]
    [Authorize(Roles = "System Administrator")]
    public async Task<IActionResult> CreateCostCenter([FromBody] CreateLookupDto dto)
    {
        if (dto.CompanyId == 0) // CompanyId field reused as PlantId for CostCenters
        {
            return BadRequest(new ProblemDetails { Title = "Erro de Validação", Detail = "O campo Planta é obrigatório para Centros de Custo." });
        }

        var plant = await _context.Plants.FindAsync(dto.CompanyId);
        if (plant == null)
        {
            return BadRequest(new ProblemDetails { Title = "Erro de Validação", Detail = "Planta inválida." });
        }

        var entity = new Domain.Entities.CostCenter { Code = dto.Code?.ToUpper() ?? string.Empty, Name = dto.Name, PlantId = dto.CompanyId, IsActive = true };
        _context.CostCenters.Add(entity);
        
        try
        {
            await _context.SaveChangesAsync();
            return Created($"/api/v1/lookups/cost-centers/{entity.Id}", new { entity.Id, entity.Code, entity.Name, entity.PlantId, entity.IsActive });
        }
        catch (DbUpdateException ex) when (ex.InnerException?.Message.Contains("IX_CostCenters_Code") == true)
        {
            return Conflict(new ProblemDetails 
            { 
                Title = "Regra de Negócio Violada", 
                Detail = $"O Centro de Custo com o código '{dto.Code}' já existe.", 
                Status = 409 
            });
        }
    }

    [HttpPut("cost-centers/{id}")]
    [Authorize(Roles = "System Administrator")]
    public async Task<IActionResult> UpdateCostCenter(int id, [FromBody] CreateLookupDto dto)
    {
        var entity = await _context.CostCenters.FindAsync(id);
        if (entity == null) return NotFound();

        // Historical Immutability Check
        if (await _context.RequestLineItems.AnyAsync(r => r.CostCenterId == id))
        {
            return Conflict(new ProblemDetails
            {
                Title = "Regra de Negócio Violada",
                Detail = "Este registro já está em uso por pedidos históricos e não pode ser alterado. Por favor, desative-o e crie um novo para preservar o histórico.",
                Status = 409
            });
        }

        if (dto.CompanyId != 0)
        {
            var plant = await _context.Plants.FindAsync(dto.CompanyId);
            if (plant == null) return BadRequest(new ProblemDetails { Title = "Erro de Validação", Detail = "Planta inválida." });
            entity.PlantId = dto.CompanyId;
        }

        entity.Code = dto.Code?.ToUpper() ?? string.Empty;
        entity.Name = dto.Name;

        try
        {
            await _context.SaveChangesAsync();
            return NoContent();
        }
        catch (DbUpdateException ex) when (ex.InnerException?.Message.Contains("IX_CostCenters_Code") == true)
        {
            return Conflict(new ProblemDetails 
            { 
                Title = "Regra de Negócio Violada", 
                Detail = $"O Centro de Custo com o código '{dto.Code}' já existe.", 
                Status = 409 
            });
        }
    }

    [HttpPut("cost-centers/{id}/toggle-active")]
    [Authorize(Roles = "System Administrator")]
    public async Task<IActionResult> ToggleCostCenterActive(int id)
    {
        var entity = await _context.CostCenters.FindAsync(id);
        if (entity == null) return NotFound();
        
        entity.IsActive = !entity.IsActive;
        await _context.SaveChangesAsync();
        return NoContent();
    }

    // Roles
    [HttpGet("roles")]
    public async Task<IActionResult> GetRoles()
    {
        var items = await _context.Roles
            .OrderBy(r => r.RoleName)
            .Select(r => new { r.Id, r.RoleName })
            .ToListAsync();

        return Ok(items);
    }
}

public class CreateLookupDto
{
    public string? Code { get; set; }
    public string Name { get; set; } = string.Empty;
    public int CompanyId { get; set; } // Only used for Plants
    public Guid? ResponsibleUserId { get; set; } // Only used for Departments
    public Guid? FinalApproverUserId { get; set; } // Only used for Companies
}

public class CreateSupplierDto
{
    public string? PortalCode { get; set; }
    public string? PrimaveraCode { get; set; }
    public string Name { get; set; } = string.Empty;
    public string? TaxId { get; set; }
}

public class CreateUnitDto : CreateLookupDto
{
    public bool AllowsDecimalQuantity { get; set; } = true;
}

public class CreateCurrencyDto
{
    public string Code { get; set; } = string.Empty;
    public string Symbol { get; set; } = string.Empty;
}

public class UpdateSupplierFichaDto
{
    public string? Name { get; set; }
    public string? TaxId { get; set; }
    public string? PrimaveraCode { get; set; }
    public string? Address { get; set; }

    public string? ContactName1 { get; set; }
    public string? ContactRole1 { get; set; }
    public string? ContactPhone1 { get; set; }
    public string? ContactEmail1 { get; set; }

    public string? ContactName2 { get; set; }
    public string? ContactRole2 { get; set; }
    public string? ContactPhone2 { get; set; }
    public string? ContactEmail2 { get; set; }

    public string? BankAccountNumber { get; set; }
    public string? BankIban { get; set; }
    public string? BankSwift { get; set; }

    public string? PaymentTerms { get; set; }
    public string? PaymentMethod { get; set; }

    public string? Notes { get; set; }
}

public class UpdateSupplierStatusDto
{
    public string NewStatus { get; set; } = string.Empty;
    public string? Comment { get; set; }
}

public class SupplierApprovalCommentDto
{
    public string Comment { get; set; } = string.Empty;
}

public class SupplierCompletenessReport
{
    public int TotalChecks { get; set; }
    public int CompletedChecks { get; set; }
    public int CompletionPercentage { get; set; }
    public bool IsComplete { get; set; }
    public List<string> MissingItems { get; set; } = new();
    public List<SupplierCompletenessCategory> Categories { get; set; } = new();
    public bool HasAlvara { get; set; }
    public string? AlvaraNote { get; set; }
}

public class SupplierCompletenessCategory
{
    public string Category { get; set; } = string.Empty;
    public string Label { get; set; } = string.Empty;
    public int TotalItems { get; set; }
    public int CompletedItems { get; set; }
    public bool IsComplete { get; set; }
    public List<string> MissingItems { get; set; } = new();
}
