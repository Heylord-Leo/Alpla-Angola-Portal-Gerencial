using System;
using System.Data.SqlClient;

class Program
{
    static void Main()
    {
        string connectionString = @"Server=(localdb)\MSSQLLocalDB;Database=AlplaPortal_DB;Integrated Security=True;";
        using (SqlConnection connection = new SqlConnection(connectionString))
        {
            connection.Open();
            string sql = @"
                SELECT TOP 1 Id, RequesterId, AreaApproverId, DepartmentId 
                FROM Requests 
                ORDER BY CreatedAtUtc DESC";
            using (SqlCommand command = new SqlCommand(sql, connection))
            {
                using (SqlDataReader reader = command.ExecuteReader())
                {
                    while (reader.Read())
                    {
                        Console.WriteLine($"RequestId: {reader[0]}");
                        Console.WriteLine($"RequesterId: {reader[1]}");
                        Console.WriteLine($"AreaApproverId: {reader[2]}");
                        Console.WriteLine($"DepartmentId: {reader[3]}");
                    }
                }
            }

            Console.WriteLine("------------------------------");

            string sqlLogs = @"
                SELECT TOP 10 ActionTaken, EventName, Message, CreatedAtUtc 
                FROM AdminLogs 
                ORDER BY CreatedAtUtc DESC";
            using (SqlCommand command = new SqlCommand(sqlLogs, connection))
            {
                using (SqlDataReader reader = command.ExecuteReader())
                {
                    while (reader.Read())
                    {
                        Console.WriteLine($"[{reader[3]}] {reader[0]} / {reader[1]}: {reader[2]}");
                    }
                }
            }

        }
    }
}
