using System;
using Microsoft.Data.SqlClient;

class Program
{
    static void Main()
    {
        string connectionString = @"Server=(localdb)\MSSQLLocalDB;Database=AlplaPortalV1;Integrated Security=True;";
        using (SqlConnection connection = new SqlConnection(connectionString))
        {
            connection.Open();
            Console.WriteLine("--- Order 11 Details ---");
            string sqlRequest = @"
                SELECT r.Id, r.Number, r.Title, s.Code as Status, t.Code as Type, r.RequesterId, r.BuyerId, r.NeedByDateUtc
                FROM Requests r
                JOIN Statuses s ON r.StatusId = s.Id
                JOIN RequestTypes t ON r.RequestTypeId = t.Id
                WHERE r.Number LIKE '%011'";
            
            using (SqlCommand command = new SqlCommand(sqlRequest, connection))
            using (SqlDataReader reader = command.ExecuteReader())
            {
                if (reader.Read())
                {
                    Console.WriteLine($"Id: {reader["Id"]}");
                    Console.WriteLine($"Number: {reader["Number"]}");
                    Console.WriteLine($"Title: {reader["Title"]}");
                    Console.WriteLine($"Status: {reader["Status"]}");
                    Console.WriteLine($"Type: {reader["Type"]}");
                    Console.WriteLine($"RequesterId: {reader["RequesterId"]}");
                    Console.WriteLine($"BuyerId: {reader["BuyerId"]}");
                    Console.WriteLine($"NeedByDateUtc: {reader["NeedByDateUtc"]}");
                }
                else
                {
                    Console.WriteLine("Order 11 not found.");
                }
            }

            Console.WriteLine("\n--- Recent Logs (to identify user) ---");
            string sqlLogs = @"
                SELECT TOP 5 ActionTaken, Message, CreatedAtUtc 
                FROM AdminLogs 
                ORDER BY CreatedAtUtc DESC";
            using (SqlCommand command = new SqlCommand(sqlLogs, connection))
            using (SqlDataReader reader = command.ExecuteReader())
            {
                while (reader.Read())
                {
                    Console.WriteLine($"[{reader["CreatedAtUtc"]}] {reader["ActionTaken"]}: {reader["Message"]}");
                }
            }

            Console.WriteLine("\n--- Users and Roles ---");
            string sqlUsers = @"
                SELECT u.Id, u.Name, u.Email, r.Name as RoleName
                FROM AspNetUsers u
                LEFT JOIN AspNetUserRoles ur ON u.Id = ur.UserId
                LEFT JOIN AspNetRoles r ON ur.RoleId = r.Id";
            using (SqlCommand command = new SqlCommand(sqlUsers, connection))
            using (SqlDataReader reader = command.ExecuteReader())
            {
                while (reader.Read())
                {
                    Console.WriteLine($"{reader["Name"]} ({reader["Email"]}) - Role: {reader["RoleName"]}");
                }
            }
        }
    }
}
