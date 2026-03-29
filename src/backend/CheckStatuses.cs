
using System;
using System.Linq;
using Microsoft.EntityFrameworkCore;
using AlplaPortal.Infrastructure.Data;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Configuration;

namespace AlplaPortal.Scripts
{
    class Program
    {
        static void Main(string[] args)
        {
            var config = new ConfigurationBuilder()
                .AddJsonFile("appsettings.json", true)
                .AddEnvironmentVariables()
                .Build();

            var connectionString = "Server=localhost;Database=AlplaPortal;Trusted_Connection=True;MultipleActiveResultSets=true;TrustServerCertificate=True";

            var optionsBuilder = new DbContextOptionsBuilder<ApplicationDbContext>();
            optionsBuilder.UseSqlServer(connectionString);

            using (var context = new ApplicationDbContext(optionsBuilder.Options))
            {
                Console.WriteLine("--- Request Statuses ---");
                var statuses = context.RequestStatuses.OrderBy(s => s.DisplayOrder).ToList();
                foreach (var s in statuses)
                {
                    Console.WriteLine($"ID: {s.Id}, Code: {s.Code}, Name: {s.Name}, IsActive: {s.IsActive}");
                }

                Console.WriteLine("\n--- Request Types ---");
                var types = context.RequestTypes.ToList();
                foreach (var t in types)
                {
                    Console.WriteLine($"ID: {t.Id}, Code: {t.Code}, Name: {t.Name}, IsActive: {t.IsActive}");
                }
                
                Console.WriteLine("\n--- Receiving Request Counts ---");
                var targetCodes = new[] { "PAYMENT_COMPLETED", "WAITING_RECEIPT", "IN_FOLLOWUP", "COMPLETED", "PAG_REALIZADO", "AG_RECIBO", "FINALIZADO" };
                var receivingRequests = context.Requests
                    .Include(r => r.Status)
                    .Where(r => targetCodes.Contains(r.Status.Code))
                    .GroupBy(r => r.Status.Code)
                    .Select(g => new { Code = g.Key, Count = g.Count() })
                    .ToList();
                
                foreach (var r in receivingRequests)
                {
                    Console.WriteLine($"Code: {r.Code}, Count: {r.Count}");
                }
            }
        }
    }
}
