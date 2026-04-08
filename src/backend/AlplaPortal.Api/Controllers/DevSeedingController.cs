using AlplaPortal.Domain.Entities;
using AlplaPortal.Infrastructure.Data;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using AlplaPortal.Domain.Constants;

namespace AlplaPortal.Api.Controllers;

[ApiController]
[Route("api/v1/dev")]
#if DEBUG
public class DevSeedingController : BaseController
{
    public DevSeedingController(ApplicationDbContext context) : base(context)
    {
    }

    [HttpDelete("cleanup-intelligence")]
    public async Task<IActionResult> CleanupIntelligence()
    {
        try
        {
            var testRequests = await _context.Requests
                .Where(r => r.Title.StartsWith("[TEST_INTEL]"))
                .ToListAsync();

            foreach (var req in testRequests)
            {
                // Hard delete related data
                var lineItems = _context.RequestLineItems.Where(li => li.RequestId == req.Id).ToList();
                _context.RequestLineItems.RemoveRange(lineItems);

                var history = _context.RequestStatusHistories.Where(h => h.RequestId == req.Id).ToList();
                _context.RequestStatusHistories.RemoveRange(history);

                _context.Requests.Remove(req);
            }

            await _context.SaveChangesAsync();
            return Ok(new { message = "Limpeza de dados de teste concluída." });
        }
        catch (Exception ex)
        {
            return BadRequest(new ProblemDetails 
            { 
                Title = "Erro na Limpeza de Dados", 
                Detail = ex.InnerException?.Message ?? ex.Message 
            });
        }
    }

    [HttpPost("seed-intelligence")]
    public async Task<IActionResult> SeedIntelligence()
    {
        try
        {
            // 1. Cleanup first to ensure idempotency
            var existing = await _context.Requests.Where(r => r.Title.StartsWith("[TEST_INTEL]")).ToListAsync();
            foreach (var r in existing)
            {
                _context.RequestLineItems.RemoveRange(_context.RequestLineItems.Where(li => li.RequestId == r.Id));
                _context.RequestStatusHistories.RemoveRange(_context.RequestStatusHistories.Where(h => h.RequestId == r.Id));
                _context.Requests.Remove(r);
            }
            await _context.SaveChangesAsync();

            // 2. Baseline Master Data (From ApplicationDbContext hardcoded IDs)
            var requesterId = Guid.Parse("11111111-1111-1111-1111-111111111111");
            var approverId = Guid.Parse("33333333-3333-3333-3333-333333333333");
            var deptId = 1; // Admin
            var companyId = 1;
            var plantId = 1;
            var aoaId = 1;
            var usdId = 2;
            var unitEaId = 2; // EA
            var ivaIsentoId = 5; // IVA 0%
            var costCenter1Id = 1;

            var quotationTypeId = await _context.RequestTypes.Where(t => t.Code == RequestConstants.Types.Quotation).Select(t => t.Id).FirstOrDefaultAsync() != 0 ? await _context.RequestTypes.Where(t => t.Code == RequestConstants.Types.Quotation).Select(t => t.Id).FirstOrDefaultAsync() : 1;
            var statusWaitingAreaId = await _context.RequestStatuses.Where(s => s.Code == RequestConstants.Statuses.WaitingAreaApproval).Select(s => s.Id).FirstOrDefaultAsync() != 0 ? await _context.RequestStatuses.Where(s => s.Code == RequestConstants.Statuses.WaitingAreaApproval).Select(s => s.Id).FirstOrDefaultAsync() : 3;
            var statusPaymentCompletedId = await _context.RequestStatuses.Where(s => s.Code == RequestConstants.Statuses.PaymentCompleted).Select(s => s.Id).FirstOrDefaultAsync() != 0 ? await _context.RequestStatuses.Where(s => s.Code == RequestConstants.Statuses.PaymentCompleted).Select(s => s.Id).FirstOrDefaultAsync() : 15;
            var statusApprovedId = await _context.RequestStatuses.Where(s => s.Code == RequestConstants.Statuses.FinalApproved).Select(s => s.Id).FirstOrDefaultAsync() != 0 ? await _context.RequestStatuses.Where(s => s.Code == RequestConstants.Statuses.FinalApproved).Select(s => s.Id).FirstOrDefaultAsync() : 5;

            var now = DateTime.UtcNow;

            // --- SCENARIO 1: Financial Accumulation (MTD/YTD) ---
            for (int i = 1; i <= 30; i++)
            {
                // Alternate between Approved and Paid to show both trends
                var isPaid = i % 2 == 0;
                var monthOffset = i % 12; // Distribute across last 12 months

                var histDate = now.AddMonths(-monthOffset).AddDays(-i);
                
                var req = new Request
                {
                    Title = $"[TEST_INTEL] Pedido Gráfico Financeiro {i}",
                    Description = "Dados históricos para validação do Gráfico de Tendência (Recharts).",
                    RequestTypeId = quotationTypeId,
                    StatusId = isPaid ? statusPaymentCompletedId : statusApprovedId,
                    RequesterId = requesterId,
                    DepartmentId = deptId,
                    CompanyId = companyId,
                    PlantId = plantId,
                    CurrencyId = aoaId,
                    // Random-ish amount between 100k and 1m based on i
                    EstimatedTotalAmount = 100000 + (decimal)(i * 25000),
                    CreatedAtUtc = histDate,
                    RequestedDateUtc = histDate,
                    CreatedByUserId = requesterId
                };
                
                req.StatusHistories.Add(new RequestStatusHistory
                {
                    PreviousStatusId = statusWaitingAreaId,
                    NewStatusId = statusApprovedId, // Reached Approval
                    CreatedAtUtc = histDate.AddDays(1), // Reached approval 1 day after creation
                    ActorUserId = requesterId,
                    ActionTaken = "Approved Area"
                });

                if (isPaid)
                {
                    req.StatusHistories.Add(new RequestStatusHistory
                    {
                        PreviousStatusId = statusApprovedId,
                        NewStatusId = statusPaymentCompletedId, // Reached Payment
                        CreatedAtUtc = histDate.AddDays(5), // Reached payment 5 days later
                        ActorUserId = requesterId,
                        ActionTaken = "Payment Completed"
                    });
                }

                _context.Requests.Add(req);
            }

            // --- SCENARIO 2: Historical Item Pricing (Description-based matching) ---
            for (int i = 1; i <= 3; i++)
            {
                var hReq = new Request
                {
                    Title = $"[TEST_INTEL] Compra Papel A4 Ref {i}",
                    RequestTypeId = quotationTypeId,
                    StatusId = statusPaymentCompletedId,
                    RequesterId = requesterId,
                    DepartmentId = deptId,
                    CompanyId = companyId,
                    PlantId = plantId,
                    CurrencyId = aoaId,
                    EstimatedTotalAmount = 5000 + (decimal)(i * 100),
                    CreatedAtUtc = now.AddMonths(-i),
                    RequestedDateUtc = now.AddMonths(-i),
                    CreatedByUserId = requesterId
                };
                _context.Requests.Add(hReq);

                var hItem = new RequestLineItem
                {
                    RequestId = hReq.Id,
                    Description = "Papel A4 Premium",
                    Quantity = 1,
                    UnitPrice = 5000 + (decimal)(i * 100),
                    TotalAmount = 5000 + (decimal)(i * 100),
                    LineNumber = 1,
                    SupplierName = "Papelaria Central de Viana",
                    UnitId = unitEaId,
                    IvaRateId = ivaIsentoId,
                    CostCenterId = costCenter1Id,
                    CreatedAtUtc = hReq.CreatedAtUtc,
                    CreatedByUserId = requesterId
                };
                _context.RequestLineItems.Add(hItem);
            }

            // --- SCENARIO 3: Currency Isolation ---
            var usdReq = new Request
            {
                Title = "[TEST_INTEL] Compra Toner HP (USD Only)",
                RequestTypeId = quotationTypeId,
                StatusId = statusPaymentCompletedId,
                RequesterId = requesterId,
                DepartmentId = deptId,
                CompanyId = companyId,
                PlantId = plantId,
                CurrencyId = usdId,
                EstimatedTotalAmount = 100,
                CreatedAtUtc = now.AddMonths(-2),
                RequestedDateUtc = now.AddMonths(-2),
                CreatedByUserId = requesterId
            };
            _context.Requests.Add(usdReq);
            _context.RequestLineItems.Add(new RequestLineItem 
            { 
                RequestId = usdReq.Id, 
                Description = "Toner HP 85A", 
                Quantity = 1, 
                UnitPrice = 100, 
                TotalAmount = 100, 
                LineNumber = 1,
                UnitId = unitEaId,
                IvaRateId = ivaIsentoId,
                CostCenterId = costCenter1Id,
                CreatedAtUtc = usdReq.CreatedAtUtc,
                CreatedByUserId = requesterId
            });


            // --- MASTER PENDING REQUEST (THE WORKSPACE) ---
            var masterReq = new Request
            {
                Title = "[TEST_INTEL] Workspace de Validação de Inteligência",
                Description = "Este pedido contém cenários específicos para validar o DecisionInsightsPanel.\n\n" +
                              "1. Papel A4: Deve mostrar variação positiva (+25%) e histórico.\n" +
                              "2. Toner HP: Deve mostrar 'Novo Item' (histórico USD isolado).\n" +
                              "3. Robô Kuka: Deve mostrar 'Novo Item' (Histórico inexistente).",
                RequestTypeId = quotationTypeId,
                StatusId = statusWaitingAreaId,
                RequesterId = requesterId,
                AreaApproverId = approverId,
                DepartmentId = deptId,
                CompanyId = companyId,
                PlantId = plantId,
                CurrencyId = aoaId,
                EstimatedTotalAmount = 1141500,
                CreatedAtUtc = now,
                RequestedDateUtc = now,
                CreatedByUserId = requesterId,
                CurrentResponsibleUserId = approverId,
                CurrentResponsibleRole = "Area Approver"
            };
            
            // Item 1: High Price Alert
            masterReq.LineItems.Add(new RequestLineItem 
            { 
                RequestId = masterReq.Id, 
                Description = "Papel A4 Premium", 
                Quantity = 1, 
                UnitPrice = 6500, 
                TotalAmount = 6500, 
                LineNumber = 1, 
                UnitId = unitEaId,
                IvaRateId = ivaIsentoId,
                CostCenterId = costCenter1Id,
                CreatedAtUtc = now,
                CreatedByUserId = requesterId
            });
            
            // Item 2: Currency isolation check
            masterReq.LineItems.Add(new RequestLineItem 
            { 
                RequestId = masterReq.Id, 
                Description = "Toner HP 85A", 
                Quantity = 1, 
                UnitPrice = 85000, 
                TotalAmount = 85000, 
                LineNumber = 2, 
                UnitId = unitEaId,
                IvaRateId = ivaIsentoId,
                CostCenterId = costCenter1Id,
                CreatedAtUtc = now,
                CreatedByUserId = requesterId
            });
            
            // Item 3: Adapted Scenario (Missing CC Alert is skipped as schema requires it)
            masterReq.LineItems.Add(new RequestLineItem 
            { 
                RequestId = masterReq.Id, 
                Description = "Manutenção Predial (Atenção)", 
                Quantity = 1, 
                UnitPrice = 50000, 
                TotalAmount = 50000, 
                LineNumber = 3, 
                UnitId = unitEaId,
                IvaRateId = ivaIsentoId,
                CostCenterId = costCenter1Id, // Set to valid ID for now
                CreatedAtUtc = now,
                CreatedByUserId = requesterId
            });
            
            // Item 4: Truly new item
            masterReq.LineItems.Add(new RequestLineItem 
            { 
                RequestId = masterReq.Id, 
                Description = "Novo Robô Industrial Kuka v2", 
                Quantity = 1, 
                UnitPrice = 1000000, 
                TotalAmount = 1000000, 
                LineNumber = 4, 
                UnitId = unitEaId,
                IvaRateId = ivaIsentoId,
                CostCenterId = costCenter1Id,
                CreatedAtUtc = now,
                CreatedByUserId = requesterId
            });

            _context.Requests.Add(masterReq);

            await _context.SaveChangesAsync();
            return Ok(new { message = "Dados de inteligência semeados com sucesso.", requestId = masterReq.Id });
        }
        catch (DbUpdateException ex)
        {
            var inner = ex.InnerException?.Message ?? ex.Message;
            return BadRequest(new ProblemDetails 
            { 
                Title = "Erro de Banco de Dados na Semeadura", 
                Detail = inner 
            });
        }
        catch (Exception ex)
        {
            return BadRequest(new ProblemDetails 
            { 
                Title = "Erro Inesperado na Semeadura", 
                Detail = ex.Message 
            });
        }
    }
}
#else
public class DevSeedingController : ControllerBase { [HttpGet] public IActionResult Index() => NotFound(); }
#endif
