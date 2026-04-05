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
    public async Task<IActionResult> GetSuppliers([FromQuery] bool includeInactive = false)
    {
        var query = _context.Suppliers.AsQueryable();

        if (!includeInactive)
        {
            query = query.Where(s => s.IsActive);
        }

        var items = await query
            .OrderBy(s => s.Name)
            .Select(s => new { s.Id, s.PortalCode, s.PrimaveraCode, s.Name, s.TaxId, s.IsActive })
            .ToListAsync();

        return Ok(items);
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
            .Select(s => new { s.Id, s.PortalCode, s.PrimaveraCode, s.Name })
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
        var counter = await _context.SystemCounters.FirstOrDefaultAsync(sc => sc.Id == counterKey);
        int seqNumber;

        if (counter == null)
        {
            // Self-healing: find the highest sequence already used
            var existingMax = await _context.Suppliers
                .Where(s => s.PortalCode != null && s.PortalCode.StartsWith("SUP-"))
                .Select(s => s.PortalCode)
                .ToListAsync();

            int maxSeq = 0;
            foreach (var num in existingMax)
            {
                if (num != null && num.StartsWith("SUP-") && int.TryParse(num.Substring(4), out int parsed))
                {
                    maxSeq = Math.Max(maxSeq, parsed);
                }
            }

            seqNumber = maxSeq + 1;
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
            counter.CurrentValue++;
            counter.LastUpdatedUtc = DateTime.UtcNow;
            seqNumber = counter.CurrentValue;
        }

        // Save counter changes immediately to prevent collisions
        await _context.SaveChangesAsync();

        return $"SUP-{seqNumber:D6}";
    }

    [HttpPost("suppliers")]
    [Authorize(Roles = "System Administrator")]
    public async Task<IActionResult> CreateSupplier([FromBody] CreateSupplierDto dto)
    {
        // 1. Generate Portal Code automatically
        var portalCode = await GetNextPortalCodeAsync();

        var entity = new Domain.Entities.Supplier 
        { 
            PortalCode = portalCode,
            PrimaveraCode = dto.PrimaveraCode?.Trim().ToUpper(),
            Name = dto.Name.Trim(), 
            TaxId = dto.TaxId?.Trim(),
            IsActive = true 
        };

        _context.Suppliers.Add(entity);
        
        try
        {
            await _context.SaveChangesAsync();
            return Created($"/api/v1/lookups/suppliers/{entity.Id}", new { entity.Id, entity.PortalCode, entity.PrimaveraCode, entity.Name, entity.TaxId, entity.IsActive });
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
