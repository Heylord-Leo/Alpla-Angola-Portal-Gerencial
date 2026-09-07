using System;
using System.Linq;
using System.Text.RegularExpressions;
using System.Threading.Tasks;
using AlplaPortal.Infrastructure.Data;
using Microsoft.Data.SqlClient;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Migrations;
using Xunit;

namespace AlplaPortal.Application.Tests.Services.Dashboard;

/// <summary>
/// Dashboard V2 B9.2 (§22) — validate the B9.1 migration through the REAL migration ARTIFACT, not
/// EnsureCreated. NOTE/LIMITATION: a full <c>Database.Migrate()</c> from empty is impossible in this repo
/// because of a PRE-EXISTING, unrelated defect in the historical chain (a duplicate <c>SourceCompany</c>
/// column on <c>ItemCatalogItems</c>) — nothing to do with B9. So this executes the generated SQL for JUST
/// the <c>20260905203852_AddOperationalStageTracking</c> migration (its Up is dependency-free: two tables,
/// no FKs) against a throwaway, uniquely-named LocalDB, then drops it. This runs the actual migration
/// artifact — not the model builder. Skips gracefully when LocalDB is unavailable.
/// </summary>
[Collection("IntegrationTests")]
public class OperationalStageMigrationPathTests
{
    private const string PrevMigration = "20260901075730_AddQuotationRevisionProvenance";
    private const string StageMigration = "20260905203852_AddOperationalStageTracking";

    private static string ConnString(string db)
        => $@"Server=(localdb)\MSSQLLocalDB;Database={db};Trusted_Connection=True;TrustServerCertificate=True";

    private static bool LocalDbAvailable()
    {
        try { using var c = new SqlConnection(ConnString("master")); c.Open(); return true; }
        catch { return false; }
    }

    [Fact]
    public async Task The_b9_1_migration_artifact_creates_the_tables_and_indexes()
    {
        if (!LocalDbAvailable()) return;

        var dbName = "Portal-Gerencial-MigPath-" + Guid.NewGuid().ToString("N")[..12];
        var options = new DbContextOptionsBuilder<ApplicationDbContext>()
            .UseSqlServer(ConnString(dbName), b => b.MigrationsAssembly("AlplaPortal.Infrastructure"))
            .Options;

        // Create the empty throwaway DB via a master connection (the context can't create the DB it targets).
        using (var master = new SqlConnection(ConnString("master")))
        {
            master.Open();
            using var cmd = master.CreateCommand();
            cmd.CommandText = $"IF DB_ID('{dbName}') IS NULL CREATE DATABASE [{dbName}]";
            cmd.ExecuteNonQuery();
        }

        await using var ctx = new ApplicationDbContext(options);
        try
        {
            // The generated SQL for ONLY the B9.1 migration (dependency-free): CREATE TABLE + CREATE INDEX.
            var migrator = ctx.GetService<IMigrator>();
            var sql = migrator.GenerateScript(fromMigration: PrevMigration, toMigration: StageMigration);

            foreach (var batch in Regex.Split(sql, @"^\s*GO\s*$", RegexOptions.Multiline)
                         .Select(b => b.Trim())
                         .Where(b => b.Length > 0)
                         // The bare DB has no __EFMigrationsHistory table; we validate the DDL only.
                         .Where(b => !b.Contains("__EFMigrationsHistory"))
                         // Each batch runs standalone, so drop the script's transaction-control batches.
                         .Where(b => !Regex.IsMatch(b, @"^(BEGIN TRANSACTION|COMMIT)\s*;?\s*$", RegexOptions.IgnoreCase)))
            {
                await ctx.Database.ExecuteSqlRawAsync(batch);
            }

            int Obj(string n) => ctx.Database.SqlQueryRaw<int>($"SELECT ISNULL(OBJECT_ID('dbo.{n}'), 0) AS [Value]").AsEnumerable().First();
            Assert.NotEqual(0, Obj("OperationalStageStates"));
            Assert.NotEqual(0, Obj("OperationalStageTransitions"));

            int Idx(string n) => ctx.Database.SqlQueryRaw<int>($"SELECT COUNT(*) AS [Value] FROM sys.indexes WHERE name = '{n}'").AsEnumerable().First();
            Assert.Equal(1, Idx("UX_OperationalStageState_Entity"));
            Assert.Equal(1, Idx("IX_OperationalStageState_RequestId"));
            Assert.Equal(1, Idx("IX_OperationalStageState_Domain_Stage"));
            Assert.Equal(1, Idx("IX_OperationalStageTransition_Entity_Occurred"));
            Assert.Equal(1, Idx("IX_OperationalStageTransition_RequestId"));
        }
        finally
        {
            await ctx.Database.EnsureDeletedAsync();
        }
    }
}
