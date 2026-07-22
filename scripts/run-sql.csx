#!/usr/bin/env dotnet-script
// ═══════════════════════════════════════════════════════════════════
// DEPRECATED: Historical one-time script. Do NOT re-run.
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
using System.IO;
using System;

var connStr = "Server=(localdb)\\MSSQLLocalDB;Database=AlplaPortalV1;Trusted_Connection=True;TrustServerCertificate=True";
var scriptPath = "C:\\dev\\alpla-portal\\src\\backend\\scripts\\maintenance\\ResetTransactionalData.sql";
var sql = File.ReadAllText(scriptPath);

using var conn = new SqlConnection(connStr);
conn.InfoMessage += (sender, e) => Console.WriteLine(e.Message);
conn.Open();

using var cmd = new SqlCommand(sql, conn);
cmd.ExecuteNonQuery();

Console.WriteLine("SQL executed successfully.");
