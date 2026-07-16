using System.Text;
using AlplaPortal.Infrastructure.Services.Approvals;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace AlplaPortal.Api.Controllers.Admin;

[ApiController]
[Route("api/admin/reports")]
[Authorize(Roles = "System Administrator")]
public class AdminReportsController : ControllerBase
{
    private readonly AreaApproverReconciliationService _reconciliation;

    public AdminReportsController(AreaApproverReconciliationService reconciliation)
    {
        _reconciliation = reconciliation;
    }

    /// <summary>
    /// Reconciliation report Role "Area Approver" × DepartmentManager registrations
    /// (redesign plan §16.1 — mandatory gate before Phase C). `?format=csv` downloads
    /// a CSV; default is JSON.
    /// </summary>
    [HttpGet("area-approver-reconciliation")]
    public async Task<IActionResult> GetAreaApproverReconciliation([FromQuery] string? format = null)
    {
        var rows = await _reconciliation.BuildAsync();
        var legacyPending = await _reconciliation.BuildLegacyPendingRequestsAsync();

        if (!string.Equals(format, "csv", StringComparison.OrdinalIgnoreCase))
        {
            return Ok(new
            {
                GeneratedAtUtc = DateTime.UtcNow,
                PhaseNote = "Fase C ativa: atribuições manuais da role 'Area Approver' foram removidas do banco pela migration " +
                            "(PERDE_ACESSO só reaparece se algo recriar atribuições manualmente — investigar). SO_CADASTRO é o " +
                            "estado normal: a role existe apenas como claim derivada de DepartmentManagers. " +
                            "LegacyPendingRequests lista os pedidos em etapa de área ainda cobertos pela cláusula de compatibilidade " +
                            "de nomeado legado — quando estiver vazia em PRODUÇÃO, a cláusula pode ser removida do código.",
                Total = rows.Count,
                Summary = rows.GroupBy(r => r.Classification)
                    .ToDictionary(g => g.Key, g => g.Count()),
                Rows = rows,
                LegacyPendingRequests = new
                {
                    Count = legacyPending.Count,
                    Items = legacyPending
                }
            });
        }

        var sb = new StringBuilder();
        sb.AppendLine("Nome;Email;Ativo;RoleManualAreaApprover;EscoposManager;Classificacao;Inconsistencias");
        foreach (var r in rows)
        {
            sb.AppendLine(string.Join(';',
                Csv(r.FullName), Csv(r.Email),
                r.UserIsActive ? "Sim" : "Não",
                r.HasManualAreaApproverRole ? "Sim" : "Não",
                Csv(string.Join(" | ", r.ActiveManagerScopes)),
                r.Classification,
                Csv(string.Join(" | ", r.Inconsistencies))));
        }

        // BOM so Excel opens the accented Portuguese text correctly.
        var bytes = Encoding.UTF8.GetPreamble().Concat(Encoding.UTF8.GetBytes(sb.ToString())).ToArray();
        return File(bytes, "text/csv", $"area-approver-reconciliation-{DateTime.UtcNow:yyyyMMdd-HHmm}.csv");
    }

    private static string Csv(string value)
        => value.Contains(';') || value.Contains('"') || value.Contains('\n')
            ? $"\"{value.Replace("\"", "\"\"")}\""
            : value;
}
