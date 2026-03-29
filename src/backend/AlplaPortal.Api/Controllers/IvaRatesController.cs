using AlplaPortal.Domain.Entities;
using AlplaPortal.Infrastructure.Data;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;

namespace AlplaPortal.Api.Controllers;

[ApiController]
[Route("api/v1/iva-rates")]
public class IvaRatesController : ControllerBase
{
    private readonly ApplicationDbContext _context;

    public IvaRatesController(ApplicationDbContext context)
    {
        _context = context;
    }

    [HttpGet]
    public async Task<IActionResult> GetAllIvaRates([FromQuery] bool activeOnly = false)
    {
        var query = _context.IvaRates.AsQueryable();

        if (activeOnly)
        {
            query = query.Where(i => i.IsActive);
        }

        var rates = await query
            .OrderBy(i => i.DisplayOrder)
            .Select(i => new
            {
                i.Id,
                i.Code,
                i.Name,
                i.RatePercent,
                i.IsActive
            })
            .ToListAsync();

        return Ok(rates);
    }

    public class CreateIvaRateDto
    {
        public string Code { get; set; } = string.Empty;
        public string Name { get; set; } = string.Empty;
        public decimal RatePercent { get; set; }
    }

    [HttpPost]
    public async Task<IActionResult> CreateIvaRate([FromBody] CreateIvaRateDto dto)
    {
        if (string.IsNullOrWhiteSpace(dto.Name) || string.IsNullOrWhiteSpace(dto.Code))
        {
            return BadRequest("Code and Name are required.");
        }

        if (await _context.IvaRates.AnyAsync(i => i.Code == dto.Code))
        {
            return Conflict("An IVA rate with this code already exists.");
        }

        var newOrder = await _context.IvaRates.MaxAsync(i => (int?)i.DisplayOrder) ?? 0;

        var rate = new IvaRate
        {
            Code = dto.Code,
            Name = dto.Name,
            RatePercent = dto.RatePercent,
            IsActive = true,
            DisplayOrder = newOrder + 1
        };

        _context.IvaRates.Add(rate);
        await _context.SaveChangesAsync();

        return CreatedAtAction(nameof(GetAllIvaRates), new { id = rate.Id }, new
        {
            rate.Id,
            rate.Code,
            rate.Name,
            rate.RatePercent,
            rate.IsActive
        });
    }

    [HttpPut("{id}")]
    public async Task<IActionResult> UpdateIvaRate(int id, [FromBody] CreateIvaRateDto dto)
    {
        var rate = await _context.IvaRates.FindAsync(id);
        if (rate == null) return NotFound("IVA rate not found.");

        if (rate.Code != dto.Code && await _context.IvaRates.AnyAsync(i => i.Code == dto.Code))
        {
            return Conflict("An IVA rate with this code already exists.");
        }

        rate.Code = dto.Code;
        rate.Name = dto.Name;
        rate.RatePercent = Math.Round(dto.RatePercent, 2);

        await _context.SaveChangesAsync();

        return Ok(new
        {
            rate.Id,
            rate.Code,
            rate.Name,
            rate.RatePercent,
            rate.IsActive
        });
    }

    [HttpPut("{id}/toggle-active")]
    public async Task<IActionResult> ToggleIvaRateActive(int id)
    {
        var rate = await _context.IvaRates.FindAsync(id);
        if (rate == null) return NotFound("IVA rate not found.");

        rate.IsActive = !rate.IsActive;
        await _context.SaveChangesAsync();

        return Ok(new
        {
            rate.Id,
            rate.Code,
            rate.Name,
            rate.RatePercent,
            rate.IsActive
        });
    }
}
