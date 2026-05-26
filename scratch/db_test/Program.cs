using System;
using Microsoft.Data.SqlClient;

class Program
{
    static void Main()
    {
        string connectionString = @"Server=(localdb)\MSSQLLocalDB;Database=AlplaPortalV1;Integrated Security=True;TrustServerCertificate=True";
        using (SqlConnection connection = new SqlConnection(connectionString))
        {
            connection.Open();
            // 1. Get current user ID (guessing from recent activity)
            // 2. Get roles for that user
            string sql = @"
                SELECT u.Email, u.FullName, r.RoleName
                FROM UserRoleAssignments ura
                JOIN Users u ON ura.UserId = u.Id
                JOIN Roles r ON ura.RoleId = r.Id
                WHERE u.Email = 'buyer@alpla.com' OR u.Email = 'finance@alpla.com'"; // Common test emails
            
            using (SqlCommand command = new SqlCommand(sql, connection))
            using (SqlDataReader reader = command.ExecuteReader())
            {
                Console.WriteLine("Email\t\tRole");
                while (reader.Read())
                {
                    Console.WriteLine($"{reader["Email"]}\t{reader["RoleName"]}");
                }
            }
        }
    }
}
