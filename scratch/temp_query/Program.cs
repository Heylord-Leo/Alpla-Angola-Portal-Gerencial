using System;
using System.Data;
using Microsoft.Data.SqlClient;

class Program
{
    static void Main()
    {
        var innuxConnStr = "Server=AOVIA1VMS012\\SQLINNUX;Database=Innux;User Id=sa;Password=ad#56&Hfe;TrustServerCertificate=True;Encrypt=False;Connection Timeout=60;";
        
        Console.WriteLine("\n=== Innux Entidades ===");
        try
        {
            using var conn = new SqlConnection(innuxConnStr);
            conn.Open();
            using var cmdData = new SqlCommand("SELECT TOP 10 * FROM Entidades", conn);
            using var readerData = cmdData.ExecuteReader();
            for (int i = 0; i < readerData.FieldCount; i++) Console.Write(readerData.GetName(i) + "\t");
            Console.WriteLine();
            while (readerData.Read())
            {
                for (int i = 0; i < readerData.FieldCount; i++) Console.Write(readerData.IsDBNull(i) ? "NULL\t" : readerData.GetValue(i).ToString() + "\t");
                Console.WriteLine();
            }
            readerData.Close();

            Console.WriteLine("\n=== Innux Sync Tables ===");
            using var cmdTables = new SqlCommand("SELECT TABLE_NAME FROM INFORMATION_SCHEMA.TABLES WHERE TABLE_NAME LIKE '%Sync%' OR TABLE_NAME LIKE '%Integr%' OR TABLE_NAME LIKE '%Log%'", conn);
            using var readerTables = cmdTables.ExecuteReader();
            while (readerTables.Read()) Console.WriteLine("  " + readerTables.GetString(0));
            readerTables.Close();

        }
        catch (Exception ex)
        {
            Console.WriteLine("Error: " + ex.Message);
        }
    }
}
