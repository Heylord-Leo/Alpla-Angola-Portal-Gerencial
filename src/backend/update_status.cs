
using Microsoft.Extensions.DependencyInjection;
using AlplaPortal.Infrastructure.Data;
using Microsoft.EntityFrameworkCore;
using AlplaPortal.Domain.Entities;

var services = new ServiceCollection();
services.AddDbContext<ApplicationDbContext>(options =>
    options.UseSqlServer("Server=(localdb)\\mssqllocaldb;Database=AlplaPortal;Trusted_Connection=True;MultipleActiveResultSets=true"));

var serviceProvider = services.BuildServiceProvider();
using var scope = serviceProvider.CreateScope();
var context = scope.ServiceProvider.GetRequiredService<ApplicationDbContext>();

var request = await context.Requests
    .OrderByDescending(r => r.CreatedAtUtc)
    .FirstOrDefaultAsync();

if (request != null)
{
    Console.WriteLine($"Updating Request {request.RequestNumber} (ID: {request.Id}) from Status {request.StatusId} to 16");
    request.StatusId = 16;
    await context.SaveChangesAsync();
    Console.WriteLine("Update successful.");
}
else
{
    Console.WriteLine("No requests found.");
}
