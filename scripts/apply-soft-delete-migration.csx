#!/usr/bin/env dotnet-script
// ═══════════════════════════════════════════════════════════════════
// DEPRECATED: Historical one-time migration script. Do NOT re-run.
// Applied: AddContractDocumentSoftDelete migration.
// The target database AlplaPortalV1 has been decommissioned.
// The canonical dev database is Portal-Gerencial-Dev-ProdClone.
// Schema changes must use EF Core migrations:
//   execution/update_dev_database.ps1 -Apply -Confirmation 'APPLY-MIGRATIONS-TO-DEV-CLONE'
// ═══════════════════════════════════════════════════════════════════
Console.ForegroundColor = ConsoleColor.Red;
Console.WriteLine("[BLOCKED] This script is deprecated and must not be executed.");
Console.WriteLine("[BLOCKED] Target database AlplaPortalV1 has been decommissioned.");
Console.WriteLine("[BLOCKED] Use EF Core migrations for schema changes.");
Console.ResetColor();
return;

// --- Original code preserved below for historical reference ---
#r "nuget: Microsoft.Data.SqlClient, 5.2.0"
using Microsoft.Data.SqlClient;

var connStr = "Server=(localdb)\\MSSQLLocalDB;Database=AlplaPortalV1;Trusted_Connection=True;TrustServerCertificate=True";
using var conn = new SqlConnection(connStr);
conn.Open();

var checkSql = "SELECT COUNT(1) FROM INFORMATION_SCHEMA.COLUMNS WHERE TABLE_NAME='ContractDocuments' AND COLUMN_NAME='IsDeleted'";
using var chk = new SqlCommand(checkSql, conn);
var exists = (int)chk.ExecuteScalar() > 0;

if (exists)
{
    Console.WriteLine("Migration already applied - columns exist.");
    return;
}

var ddl = @"
ALTER TABLE [ContractDocuments] ADD [IsDeleted] bit NOT NULL DEFAULT 0;
ALTER TABLE [ContractDocuments] ADD [DeletedAtUtc] datetime2 NULL;
ALTER TABLE [ContractDocuments] ADD [DeletedByUserId] uniqueidentifier NULL;
CREATE INDEX [IX_ContractDocuments_IsDeleted] ON [ContractDocuments] ([IsDeleted]);
INSERT INTO [__EFMigrationsHistory] ([MigrationId],[ProductVersion])
VALUES ('20260421155149_AddContractDocumentSoftDelete','8.0.0');
";

using var cmd = new SqlCommand(ddl, conn);
cmd.ExecuteNonQuery();
Console.WriteLine("Migration applied successfully.");
