using System;
using AlplaPortal.Infrastructure.Data;
using Xunit;

namespace AlplaPortal.Application.Tests.Services.Requests;

/// <summary>
/// v2.229.3 — the HandoffParkedAdvancePaidGroupsToDelivery repair statements executed against a
/// real SQL Server (LocalDB), on a dedicated minimal-schema scratch database (only the five
/// tables the statements touch). Skips silently when LocalDB is unavailable.
/// </summary>
[Collection("IntegrationTests")]
public class AdvanceDeliveryRepairSqlTests
{
    private const string ScratchConnection =
        @"Server=(localdb)\MSSQLLocalDB;Database=Portal-Gerencial-AdvanceRepairTests;" +
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
                    "IF DB_ID('Portal-Gerencial-AdvanceRepairTests') IS NULL " +
                    "CREATE DATABASE [Portal-Gerencial-AdvanceRepairTests]";
                create.ExecuteNonQuery();
            }
            master.Close();

            var conn = new Microsoft.Data.SqlClient.SqlConnection(ScratchConnection);
            conn.Open();
            using var schema = conn.CreateCommand();
            schema.CommandText = @"
IF OBJECT_ID('RequestStatuses') IS NULL
    CREATE TABLE RequestStatuses (Id int PRIMARY KEY, Code nvarchar(64) NOT NULL);
IF OBJECT_ID('Requests') IS NULL
    CREATE TABLE Requests (Id uniqueidentifier PRIMARY KEY, StatusId int NOT NULL, IsCancelled bit NOT NULL DEFAULT 0);
IF OBJECT_ID('RequestPoGroups') IS NULL
    CREATE TABLE RequestPoGroups (Id uniqueidentifier PRIMARY KEY, RequestId uniqueidentifier NOT NULL,
        Status nvarchar(64) NOT NULL, OperationalReceiptCompletedAtUtc datetime2 NULL);
IF OBJECT_ID('RequestPayments') IS NULL
    CREATE TABLE RequestPayments (Id int IDENTITY PRIMARY KEY, RequestPoGroupId uniqueidentifier NULL,
        PaymentType nvarchar(32) NOT NULL, PaymentStatus nvarchar(32) NOT NULL);
IF OBJECT_ID('RequestReconciliations') IS NULL
    CREATE TABLE RequestReconciliations (Id int IDENTITY PRIMARY KEY, RequestId uniqueidentifier NOT NULL);
DELETE FROM RequestReconciliations; DELETE FROM RequestPayments;
DELETE FROM RequestPoGroups; DELETE FROM Requests; DELETE FROM RequestStatuses;
INSERT INTO RequestStatuses VALUES (24, 'ADVANCE_PAYMENT_COMPLETED'), (25, 'WAITING_SUPPLIER_DELIVERY'),
    (17, 'COMPLETED'), (16, 'WAITING_RECEIPT');";
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
        return cmd.ExecuteScalar()!.ToString()!;
    }

    private static void RunRepairs(Microsoft.Data.SqlClient.SqlConnection conn)
    {
        foreach (var statement in AdvanceDeliveryRepairSql.All) Exec(conn, statement);
    }

    private static Guid AddRequest(Microsoft.Data.SqlClient.SqlConnection conn, int statusId, bool cancelled = false)
    {
        var id = Guid.NewGuid();
        Exec(conn, $"INSERT INTO Requests VALUES ('{id}', {statusId}, {(cancelled ? 1 : 0)})");
        return id;
    }

    private static Guid AddGroup(
        Microsoft.Data.SqlClient.SqlConnection conn, Guid requestId, string status,
        bool receiptStamped = false)
    {
        var id = Guid.NewGuid();
        Exec(conn, $"INSERT INTO RequestPoGroups VALUES ('{id}', '{requestId}', '{status}', " +
                   (receiptStamped ? "SYSUTCDATETIME()" : "NULL") + ")");
        return id;
    }

    private static void AddPayment(
        Microsoft.Data.SqlClient.SqlConnection conn, Guid groupId, string type, string status) =>
        Exec(conn, $"INSERT INTO RequestPayments VALUES ('{groupId}', '{type}', '{status}')");

    [Fact]
    public void Groups_matching_the_defect_shape_move_and_everything_else_is_untouched()
    {
        using var conn = TryOpen();
        if (conn == null) return;

        // Defect shape: APC group, COMPLETED advance, no stamp, live request → moves.
        var defectRequest = AddRequest(conn, 24);
        var defectGroup = AddGroup(conn, defectRequest, "ADVANCE_PAYMENT_COMPLETED");
        AddPayment(conn, defectGroup, "ADVANCE", "COMPLETED");

        // Planned-only advance (never confirmed) → stays.
        var plannedRequest = AddRequest(conn, 24);
        var plannedGroup = AddGroup(conn, plannedRequest, "ADVANCE_PAYMENT_COMPLETED");
        AddPayment(conn, plannedGroup, "ADVANCE", "PLANNED");

        // Receipt already stamped → stays (progressed by some other path).
        var stampedRequest = AddRequest(conn, 24);
        var stampedGroup = AddGroup(conn, stampedRequest, "ADVANCE_PAYMENT_COMPLETED", receiptStamped: true);
        AddPayment(conn, stampedGroup, "ADVANCE", "COMPLETED");

        // Reconciliation exists on the request → stays.
        var reconRequest = AddRequest(conn, 24);
        var reconGroup = AddGroup(conn, reconRequest, "ADVANCE_PAYMENT_COMPLETED");
        AddPayment(conn, reconGroup, "ADVANCE", "COMPLETED");
        Exec(conn, $"INSERT INTO RequestReconciliations VALUES ('{reconRequest}')");

        // Completed request → stays.
        var doneRequest = AddRequest(conn, 17);
        var doneGroup = AddGroup(conn, doneRequest, "ADVANCE_PAYMENT_COMPLETED");
        AddPayment(conn, doneGroup, "ADVANCE", "COMPLETED");

        // Later-stage group → stays (status filter never matches).
        var laterRequest = AddRequest(conn, 16);
        var laterGroup = AddGroup(conn, laterRequest, "WAITING_RECEIPT");
        AddPayment(conn, laterGroup, "ADVANCE", "COMPLETED");

        RunRepairs(conn);

        string StatusOf(Guid id) =>
            Scalar(conn, $"SELECT Status FROM RequestPoGroups WHERE Id = '{id}'");

        Assert.Equal("WAITING_SUPPLIER_DELIVERY", StatusOf(defectGroup));
        Assert.Equal("ADVANCE_PAYMENT_COMPLETED", StatusOf(plannedGroup));
        Assert.Equal("ADVANCE_PAYMENT_COMPLETED", StatusOf(stampedGroup));
        Assert.Equal("ADVANCE_PAYMENT_COMPLETED", StatusOf(reconGroup));
        Assert.Equal("ADVANCE_PAYMENT_COMPLETED", StatusOf(doneGroup));
        Assert.Equal("WAITING_RECEIPT", StatusOf(laterGroup));

        // Parent repair: the defect request (APC, its only group now WSD) follows; the others don't.
        string RequestStatusOf(Guid id) =>
            Scalar(conn, $"SELECT StatusId FROM Requests WHERE Id = '{id}'");
        Assert.Equal("25", RequestStatusOf(defectRequest));
        Assert.Equal("24", RequestStatusOf(plannedRequest));
        Assert.Equal("17", RequestStatusOf(doneRequest));

        // Idempotent: a second pass changes nothing further.
        RunRepairs(conn);
        Assert.Equal("WAITING_SUPPLIER_DELIVERY", StatusOf(defectGroup));
        Assert.Equal("25", RequestStatusOf(defectRequest));
    }

    [Fact]
    public void Parent_with_a_sibling_still_behind_is_left_to_self_heal()
    {
        using var conn = TryOpen();
        if (conn == null) return;

        // Multi-group: one defect group (moves), one sibling with an unconfirmed advance
        // (stays APC) → the parent's furthest-behind reading is NOT uniformly delivery-or-later,
        // so the parent stays untouched for the next aggregation touch.
        var request = AddRequest(conn, 24);
        var defectGroup = AddGroup(conn, request, "ADVANCE_PAYMENT_COMPLETED");
        AddPayment(conn, defectGroup, "ADVANCE", "COMPLETED");
        var siblingGroup = AddGroup(conn, request, "ADVANCE_PAYMENT_COMPLETED");
        AddPayment(conn, siblingGroup, "ADVANCE", "PLANNED");

        RunRepairs(conn);

        Assert.Equal("WAITING_SUPPLIER_DELIVERY",
            Scalar(conn, $"SELECT Status FROM RequestPoGroups WHERE Id = '{defectGroup}'"));
        Assert.Equal("ADVANCE_PAYMENT_COMPLETED",
            Scalar(conn, $"SELECT Status FROM RequestPoGroups WHERE Id = '{siblingGroup}'"));
        Assert.Equal("24", Scalar(conn, $"SELECT StatusId FROM Requests WHERE Id = '{request}'"));
    }
}
