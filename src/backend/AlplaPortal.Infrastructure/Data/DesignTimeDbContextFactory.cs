using System;
using System.IO;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Design;
using Microsoft.Extensions.Configuration;

namespace AlplaPortal.Infrastructure.Data;

/// <summary>
/// Design-time factory used exclusively by the EF Core tools (<c>dotnet ef</c>).
///
/// Its purpose is to let commands such as <c>migrations add/script</c> build the model WITHOUT
/// constructing or running the API host (<c>Program.cs</c>). This keeps the runtime
/// connection-string guard in <c>Program.cs</c> intact while ensuring design-time operations never
/// depend on it.
///
/// It never resolves application services, never runs seeding, and never opens a database
/// connection during <see cref="CreateDbContext"/>.
///
/// Connection string resolution order (no real credentials are stored here):
///   1. Environment variable <c>ConnectionStrings__DefaultConnection</c> (used by CI / when a
///      connecting command genuinely needs the real database).
///   2. Optional local configuration files, ONLY when they exist (developer convenience).
///   3. A strictly non-operational design-time placeholder pointing to a non-existent server.
///
/// The placeholder deliberately does NOT fall back to LocalDB: SQL-generating commands
/// (<c>migrations script</c>, <c>migrations add</c>) never open it, and any command that DOES
/// require a connection will fail clearly instead of silently acting on an unintended database.
/// </summary>
public sealed class DesignTimeDbContextFactory : IDesignTimeDbContextFactory<ApplicationDbContext>
{
    // Clearly-fake, non-routable server. Valid connection-string syntax so the provider can build
    // the model, but it can never open a real connection.
    private const string DesignTimePlaceholder =
        "Server=alpla-ef-design-time.invalid;Database=AlplaPortal_DesignTime_DoNotConnect;" +
        "Trusted_Connection=True;TrustServerCertificate=True";

    public ApplicationDbContext CreateDbContext(string[] args)
    {
        var connectionString = ResolveConnectionString();

        var options = new DbContextOptionsBuilder<ApplicationDbContext>()
            .UseSqlServer(connectionString, sql => sql.MigrationsAssembly("AlplaPortal.Infrastructure"))
            .Options;

        return new ApplicationDbContext(options);
    }

    private static string ResolveConnectionString()
    {
        // 1) Environment variable (standard ASP.NET Core key for the "DefaultConnection" entry).
        var fromEnv = Environment.GetEnvironmentVariable("ConnectionStrings__DefaultConnection");
        if (!string.IsNullOrWhiteSpace(fromEnv))
            return fromEnv;

        // 2) Optional local configuration — only consulted when the files actually exist.
        var fromConfig = TryReadFromLocalConfiguration();
        if (!string.IsNullOrWhiteSpace(fromConfig))
            return fromConfig!;

        // 3) Non-operational design-time placeholder (never opens a connection).
        return DesignTimePlaceholder;
    }

    /// <summary>
    /// Best-effort probe of the API project's appsettings for local development. Reads
    /// <c>ConnectionStrings:DefaultConnection</c> when a config file is present; returns null
    /// otherwise. Never throws and never logs the value.
    /// </summary>
    private static string? TryReadFromLocalConfiguration()
    {
        try
        {
            var apiDir = FindApiProjectDirectory();
            if (apiDir == null)
                return null;

            var builder = new ConfigurationBuilder()
                .SetBasePath(apiDir)
                .AddJsonFile("appsettings.json", optional: true, reloadOnChange: false)
                .AddJsonFile("appsettings.Development.json", optional: true, reloadOnChange: false)
                .AddEnvironmentVariables();

            var value = builder.Build().GetConnectionString("DefaultConnection");
            return string.IsNullOrWhiteSpace(value) ? null : value;
        }
        catch
        {
            // Configuration probing is a convenience only — never fail design-time over it.
            return null;
        }
    }

    /// <summary>
    /// Walks up from the current directory looking for the AlplaPortal.Api project directory
    /// (identified by its .csproj). Returns null when not found.
    /// </summary>
    private static string? FindApiProjectDirectory()
    {
        var dir = new DirectoryInfo(Directory.GetCurrentDirectory());
        while (dir != null)
        {
            var candidate = Path.Combine(dir.FullName, "src", "backend", "AlplaPortal.Api");
            if (File.Exists(Path.Combine(candidate, "AlplaPortal.Api.csproj")))
                return candidate;

            // Also handle running from within the Api project directory itself.
            if (File.Exists(Path.Combine(dir.FullName, "AlplaPortal.Api.csproj")))
                return dir.FullName;

            dir = dir.Parent;
        }
        return null;
    }
}
