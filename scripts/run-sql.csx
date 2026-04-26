#!/usr/bin/env dotnet-script
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
