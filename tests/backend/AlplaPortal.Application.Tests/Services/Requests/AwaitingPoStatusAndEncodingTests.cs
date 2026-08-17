using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using AlplaPortal.Domain.Constants;
using AlplaPortal.Domain.Entities;
using AlplaPortal.Domain.Services;
using AlplaPortal.Infrastructure.Data;
using AlplaPortal.Infrastructure.Services.Approvals;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Metadata;
using Xunit;

namespace AlplaPortal.Application.Tests.Services.Requests;

/// <summary>
/// v2.229.1 (REQ-17/08/2026-232): the actionable awaiting-P.O. request state and the encoding
/// repairs. Pins the calculator's zero/partial/all-P.O. semantics, the repurposed PO_REQUESTED
/// lookup, the awaiting-P.O. queue predicates, the mojibake-free seed, and the hardened
/// migration transport.
/// </summary>
public class AwaitingPoStatusAndEncodingTests
{
    private const string NonConnectingConnectionString =
        "Server=alpla-v22291-tests.invalid;Database=AlplaPortal_ModelOnly_DoNotConnect;" +
        "Trusted_Connection=True;TrustServerCertificate=True";

    private static ApplicationDbContext ModelOnlyContext() =>
        new(new DbContextOptionsBuilder<ApplicationDbContext>()
            .UseSqlServer(NonConnectingConnectionString)
            .Options);

    // ── Calculator: the batch-phase QUOTATION shape ──

    private static Request QuotationRequest(params string[] groupStatuses)
    {
        var batchId = Guid.NewGuid();
        var request = new Request
        {
            Id = Guid.NewGuid(),
            Status = new RequestStatus { Id = 5, Code = RequestConstants.Statuses.WaitingFinalApproval, Name = "x" },
            StatusId = 5
        };
        request.ApprovalBatches.Add(new ApprovalBatch
        {
            Id = batchId,
            RequestId = request.Id,
            Status = RequestConstants.ApprovalBatchStatuses.Approved
        });
        request.LineItems.Add(new RequestLineItem
        {
            Id = Guid.NewGuid(),
            RequestId = request.Id,
            LineNumber = 1,
            Description = "ZZTEST item",
            Quantity = 1,
            QuotationLifecycleStatus = RequestConstants.QuotationLifecycleStatuses.QuotationApproved
        });
        foreach (var status in groupStatuses)
        {
            request.PoGroups.Add(new RequestPoGroup
            {
                Id = Guid.NewGuid(),
                RequestId = request.Id,
                ApprovalBatchId = batchId,
                Status = status
            });
        }
        return request;
    }

    [Fact]
    public void A_single_group_zero_of_one_pos_reports_po_requested()
    {
        var result = RequestStatusCalculator.DetermineAggregateRequestStatus(
            QuotationRequest(RequestConstants.PoGroupStatuses.WaitingPo));

        Assert.Equal(RequestConstants.Statuses.PoRequested, result.StatusCode);
    }

    [Fact]
    public void B_multi_group_zero_of_n_pos_reports_po_requested()
    {
        var result = RequestStatusCalculator.DetermineAggregateRequestStatus(QuotationRequest(
            RequestConstants.PoGroupStatuses.WaitingPo,
            RequestConstants.PoGroupStatuses.WaitingPo,
            RequestConstants.PoGroupStatuses.WaitingPo));

        Assert.Equal(RequestConstants.Statuses.PoRequested, result.StatusCode);
    }

    [Fact]
    public void C_some_of_n_pos_keeps_po_partially_uploaded()
    {
        var result = RequestStatusCalculator.DetermineAggregateRequestStatus(QuotationRequest(
            RequestConstants.PoGroupStatuses.WaitingPo,
            RequestConstants.PoGroupStatuses.PoIssued));

        Assert.Equal(RequestConstants.Statuses.PoPartiallyUploaded, result.StatusCode);
    }

    [Fact]
    public void D_all_pos_registered_advances_to_the_group_ladder()
    {
        var issued = RequestStatusCalculator.DetermineAggregateRequestStatus(QuotationRequest(
            RequestConstants.PoGroupStatuses.PoIssued,
            RequestConstants.PoGroupStatuses.PoIssued));
        Assert.Equal(RequestConstants.PoGroupStatuses.PoIssued, issued.StatusCode);

        // Furthest-behind still governs beyond issuance — untouched by this patch.
        var mixedPayment = RequestStatusCalculator.DetermineAggregateRequestStatus(QuotationRequest(
            RequestConstants.PoGroupStatuses.PaymentScheduled,
            RequestConstants.PoGroupStatuses.PoIssued));
        Assert.Equal(RequestConstants.PoGroupStatuses.PoIssued, mixedPayment.StatusCode);
    }

    [Fact]
    public void E_zero_po_groups_after_settled_quotation_keeps_quotation_completed()
    {
        var result = RequestStatusCalculator.DetermineAggregateRequestStatus(QuotationRequest());

        Assert.Equal(RequestConstants.Statuses.QuotationCompleted, result.StatusCode);
    }

    [Fact]
    public void F_po_correction_precedence_is_unchanged()
    {
        var result = RequestStatusCalculator.DetermineAggregateRequestStatus(QuotationRequest(
            RequestConstants.PoGroupStatuses.WaitingPo,
            RequestConstants.PoGroupStatuses.WaitingPoCorrection));

        Assert.Equal(RequestConstants.Statuses.WaitingPoCorrection, result.StatusCode);
    }

    [Fact]
    public void Batchless_group_workflow_still_preserves_its_current_status()
    {
        // PAYMENT-type shape: no batches, groups exist — RegisterPo owns those transitions.
        var request = new Request
        {
            Id = Guid.NewGuid(),
            Status = new RequestStatus { Id = 9, Code = RequestConstants.Statuses.FinalApproved, Name = "x" },
            StatusId = 9
        };
        request.PoGroups.Add(new RequestPoGroup
        {
            Id = Guid.NewGuid(),
            RequestId = request.Id,
            Status = RequestConstants.PoGroupStatuses.WaitingPo
        });

        var result = RequestStatusCalculator.DetermineAggregateRequestStatus(request);

        Assert.Equal(RequestConstants.Statuses.FinalApproved, result.StatusCode);
    }

    // ── Queue predicates ──

    [Fact]
    public void Budget_committed_statuses_include_the_awaiting_po_state()
    {
        Assert.Contains(RequestConstants.Statuses.PoRequested,
            BudgetCalculationHelper.BudgetCommittedStatuses);
        // Legacy rows keep counting during the transition.
        Assert.Contains(RequestConstants.Statuses.QuotationCompleted,
            BudgetCalculationHelper.BudgetCommittedStatuses);
    }

    // ── Seed pins: repurposed lookup + mojibake-free names ──

    [Fact]
    public void Po_requested_seed_is_active_and_labeled_aguardando_po()
    {
        using var context = ModelOnlyContext();
        var seed = context.GetService<IDesignTimeModel>().Model
            .FindEntityType(typeof(RequestStatus))!
            .GetSeedData()
            .ToList();

        var poRequested = seed.Single(row => (string?)row["Code"] == "PO_REQUESTED");
        Assert.Equal("Aguardando P.O.", (string?)poRequested["Name"]);
        Assert.Equal(11, (int)poRequested["Id"]!);

        // The old defective label lives on QUOTATION_COMPLETED, which stays inactive (it is a
        // historical/zero-group label, deliberately not offered in the status filter).
        var quotationCompleted = seed.Single(row => (string?)row["Code"] == "QUOTATION_COMPLETED");
        Assert.Equal(false, quotationCompleted["IsActive"]);
    }

    [Fact]
    public void Seeded_status_names_contain_no_mojibake_sequences()
    {
        using var context = ModelOnlyContext();
        var names = context.GetService<IDesignTimeModel>().Model
            .FindEntityType(typeof(RequestStatus))!
            .GetSeedData()
            .Select(row => (string?)row["Name"])
            .Where(n => n != null)
            .ToList();

        Assert.NotEmpty(names);
        foreach (var name in names)
        {
            // 'Ã' (U+00C3) and 'Â' (U+00C2) never occur in a correct Portuguese status name —
            // they are the double-encoding fingerprints this patch eradicates.
            Assert.DoesNotContain('Ã', name!);
            Assert.DoesNotContain('Â', name!);
        }

        // The three repaired names, pinned to their exact Unicode forms.
        var byCode = context.GetService<IDesignTimeModel>().Model
            .FindEntityType(typeof(RequestStatus))!
            .GetSeedData()
            .ToDictionary(row => (string)row["Code"]!, row => (string?)row["Name"]);

        Assert.Equal("Adiantamento Necessário", byCode["ADVANCE_PAYMENT_REQUIRED"]);
        Assert.Equal("Ag. Entrega/Serviço", byCode["WAITING_SUPPLIER_DELIVERY"]);
        Assert.Equal("Ag. Reconciliação", byCode["WAITING_RECONCILIATION"]);
    }

    // ── Pipeline pin: the migration transport is explicitly UTF-8 ──

    [Fact]
    public void Apply_migrations_script_uses_explicit_utf8_transport()
    {
        var script = FindRepoFile(Path.Combine("scripts", "db", "apply-migrations.ps1"));
        if (script == null) return; // repo layout unavailable (isolated runner) — skip silently

        var content = File.ReadAllText(script);

        // BOM on generation…
        Assert.Contains("UTF8Encoding]::new($true)", content);
        // …and explicit UTF-8 input codepage on execution.
        Assert.Contains("-f 65001", content);
        // The BOM-less writer that caused the corruption must not return.
        Assert.DoesNotContain("Set-Content -Path $sqlOutputFile", content);
    }

    private static string? FindRepoFile(string relativePath)
    {
        var dir = new DirectoryInfo(AppContext.BaseDirectory);
        while (dir != null)
        {
            var candidate = Path.Combine(dir.FullName, relativePath);
            if (File.Exists(candidate)) return candidate;
            dir = dir.Parent;
        }
        return null;
    }
}

/// <summary>
/// v2.229.1 — the repair SQL executed against a real SQL Server (LocalDB), on a dedicated
/// minimal-schema scratch database (NOT the shared integration database: these pins only need
/// the three tables the statements touch, and creating them here keeps the test self-contained
/// and safe). Skips silently when LocalDB is unavailable.
/// </summary>
[Collection("IntegrationTests")]
public class WorkflowStatusRepairSqlTests
{
    private const string ScratchConnection =
        @"Server=(localdb)\MSSQLLocalDB;Database=Portal-Gerencial-EncodingRepairTests;" +
        "Trusted_Connection=True;TrustServerCertificate=True";

    private static Microsoft.Data.SqlClient.SqlConnection? TryOpen()
    {
        try
        {
            var master = new Microsoft.Data.SqlClient.SqlConnection(
                @"Server=(localdb)\MSSQLLocalDB;Database=master;Trusted_Connection=True;TrustServerCertificate=True");
            master.Open();
            using (var create = master.CreateCommand())
            {
                create.CommandText =
                    "IF DB_ID('Portal-Gerencial-EncodingRepairTests') IS NULL " +
                    "CREATE DATABASE [Portal-Gerencial-EncodingRepairTests]";
                create.ExecuteNonQuery();
            }
            master.Close();

            var conn = new Microsoft.Data.SqlClient.SqlConnection(ScratchConnection);
            conn.Open();
            using var schema = conn.CreateCommand();
            schema.CommandText = @"
IF OBJECT_ID('RequestStatuses') IS NULL
    CREATE TABLE RequestStatuses (Id int PRIMARY KEY, Code nvarchar(64) NOT NULL, Name nvarchar(256) NOT NULL);
IF OBJECT_ID('Requests') IS NULL
    CREATE TABLE Requests (Id uniqueidentifier PRIMARY KEY, StatusId int NOT NULL);
IF OBJECT_ID('RequestPoGroups') IS NULL
    CREATE TABLE RequestPoGroups (Id uniqueidentifier PRIMARY KEY, RequestId uniqueidentifier NOT NULL, Status nvarchar(64) NOT NULL);
DELETE FROM RequestPoGroups; DELETE FROM Requests; DELETE FROM RequestStatuses;";
            schema.ExecuteNonQuery();
            return conn;
        }
        catch
        {
            return null; // LocalDB unavailable — skip
        }
    }

    private static void Exec(Microsoft.Data.SqlClient.SqlConnection conn, string sql)
    {
        using var cmd = conn.CreateCommand();
        cmd.CommandText = sql;
        cmd.ExecuteNonQuery();
    }

    private static string Scalar(Microsoft.Data.SqlClient.SqlConnection conn, string sql)
    {
        using var cmd = conn.CreateCommand();
        cmd.CommandText = sql;
        return (string)cmd.ExecuteScalar()!;
    }

    private static void RunAllRepairs(Microsoft.Data.SqlClient.SqlConnection conn)
    {
        foreach (var statement in WorkflowStatusRepairSql.All) Exec(conn, statement);
    }

    [Fact]
    public void Corrupted_names_are_repaired_and_correct_names_are_untouched()
    {
        using var conn = TryOpen();
        if (conn == null) return;

        // The exact corrupted values observed in the PROD clone, plus one already-correct row.
        Exec(conn, "INSERT INTO RequestStatuses VALUES " +
                   "(23, 'ADVANCE_PAYMENT_REQUIRED', N'Adiantamento Necess' + NCHAR(195) + NCHAR(161) + N'rio')," +
                   "(25, 'WAITING_SUPPLIER_DELIVERY', N'Ag. Entrega/Servi' + NCHAR(195) + NCHAR(167) + N'o')," +
                   "(26, 'WAITING_RECONCILIATION', N'Ag. Reconcilia' + NCHAR(195) + NCHAR(167) + NCHAR(195) + NCHAR(163) + N'o')," +
                   "(24, 'ADVANCE_PAYMENT_COMPLETED', N'Adiantamento Realizado')");

        RunAllRepairs(conn);

        Assert.Equal("Adiantamento Necessário",
            Scalar(conn, "SELECT Name FROM RequestStatuses WHERE Id = 23"));
        Assert.Equal("Ag. Entrega/Serviço",
            Scalar(conn, "SELECT Name FROM RequestStatuses WHERE Id = 25"));
        Assert.Equal("Ag. Reconciliação",
            Scalar(conn, "SELECT Name FROM RequestStatuses WHERE Id = 26"));
        Assert.Equal("Adiantamento Realizado",
            Scalar(conn, "SELECT Name FROM RequestStatuses WHERE Id = 24"));

        // Idempotent: a second pass changes nothing and fails nothing.
        RunAllRepairs(conn);
        Assert.Equal("Adiantamento Necessário",
            Scalar(conn, "SELECT Name FROM RequestStatuses WHERE Id = 23"));
    }

    [Fact]
    public void Parked_request_correction_moves_only_the_exact_defect_shape()
    {
        using var conn = TryOpen();
        if (conn == null) return;

        Exec(conn, "INSERT INTO RequestStatuses VALUES " +
                   "(11, 'PO_REQUESTED', N'Aguardando P.O.'), (20, 'QUOTATION_COMPLETED', N'Cota' + NCHAR(231) + NCHAR(227) + N'o Conclu' + NCHAR(237) + N'da')");

        var defect = Guid.NewGuid();          // all groups WAITING_PO → must move
        var zeroGroup = Guid.NewGuid();       // no groups → must stay
        var partial = Guid.NewGuid();         // one PO issued → must stay
        var laterStage = Guid.NewGuid();      // payment stage groups → must stay
        var inCorrection = Guid.NewGuid();    // correction shape → must stay (own aggregate)
        var cancelledPlusWaiting = Guid.NewGuid(); // cancelled sibling ignored → must move

        foreach (var id in new[] { defect, zeroGroup, partial, laterStage, inCorrection, cancelledPlusWaiting })
            Exec(conn, $"INSERT INTO Requests VALUES ('{id}', 20)");

        Exec(conn, $"INSERT INTO RequestPoGroups VALUES " +
                   $"('{Guid.NewGuid()}', '{defect}', 'WAITING_PO')," +
                   $"('{Guid.NewGuid()}', '{defect}', 'WAITING_PO')," +
                   $"('{Guid.NewGuid()}', '{partial}', 'WAITING_PO')," +
                   $"('{Guid.NewGuid()}', '{partial}', 'PO_ISSUED')," +
                   $"('{Guid.NewGuid()}', '{laterStage}', 'PAYMENT_COMPLETED')," +
                   $"('{Guid.NewGuid()}', '{inCorrection}', 'WAITING_PO')," +
                   $"('{Guid.NewGuid()}', '{inCorrection}', 'WAITING_PO_CORRECTION')," +
                   $"('{Guid.NewGuid()}', '{cancelledPlusWaiting}', 'WAITING_PO')," +
                   $"('{Guid.NewGuid()}', '{cancelledPlusWaiting}', 'CANCELLED')");

        Exec(conn, WorkflowStatusRepairSql.CorrectParkedAwaitingPoRequests);

        string StatusOf(Guid id) =>
            Scalar(conn, $"SELECT CAST(StatusId AS nvarchar(10)) FROM Requests WHERE Id = '{id}'");

        Assert.Equal("11", StatusOf(defect));
        Assert.Equal("11", StatusOf(cancelledPlusWaiting));
        Assert.Equal("20", StatusOf(zeroGroup));
        Assert.Equal("20", StatusOf(partial));
        Assert.Equal("20", StatusOf(laterStage));
        Assert.Equal("20", StatusOf(inCorrection));

        // Idempotent: re-run finds nothing left to move.
        Exec(conn, WorkflowStatusRepairSql.CorrectParkedAwaitingPoRequests);
        Assert.Equal("11", StatusOf(defect));
        Assert.Equal("20", StatusOf(partial));
    }
}
