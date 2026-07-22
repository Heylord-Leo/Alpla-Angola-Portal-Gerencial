using System;
using System.Collections.Generic;
using AlplaPortal.Infrastructure.Data;
using Microsoft.Data.SqlClient;
using Microsoft.EntityFrameworkCore;

namespace AlplaPortal.Application.Tests;

/// <summary>
/// Shared integration-test database helper. Provides a single connection-string resolution
/// point with fail-closed guards against forbidden databases.
///
/// <para><b>Database isolation:</b> All four integration test classes seed their own data
/// with unique GUID-based identifiers (ZZTEST_ prefix) and clean up in <c>finally</c> blocks.
/// They are safe to share a single <c>Portal-Gerencial-IntegrationTests</c> database.
/// However, xUnit's default parallel execution is disabled for this collection via
/// <c>[Collection("IntegrationTests")]</c> to prevent interleaved EF context/connection issues.</para>
///
/// <para><b>Local execution:</b> Tests gracefully skip (<c>return</c>) when <see cref="CanConnect"/>
/// returns <c>false</c> (LocalDB unavailable or database not created).</para>
///
/// <para><b>CI execution:</b> Set <c>ALPLA_INTEGRATION_TESTS_REQUIRED=true</c> in CI.
/// When set, <see cref="CanConnect"/> will <b>throw</b> instead of returning <c>false</c>,
/// ensuring integration tests fail loudly when the database is missing.</para>
/// </summary>
internal static class IntegrationTestDatabase
{
    /// <summary>Default isolated test database name — never the Development clone.</summary>
    private const string DefaultTestDb = "Portal-Gerencial-IntegrationTests";

    /// <summary>Databases that must NEVER be used for integration tests.</summary>
    private static readonly HashSet<string> ForbiddenDatabases = new(StringComparer.OrdinalIgnoreCase)
    {
        "Portal-Gerencial-Dev-ProdClone",
        "AlplaPortalV1",
        "Portal-Gerencial-Test",
        "Portal-Gerencial"
    };

    /// <summary>
    /// Resolves the integration-test connection string.
    /// Priority: <c>$env:ALPLA_TEST_CONNECTION_STRING</c> → default LocalDB.
    /// Throws if the resolved database is forbidden.
    /// </summary>
    public static string GetConnectionString()
    {
        var fromEnv = Environment.GetEnvironmentVariable("ALPLA_TEST_CONNECTION_STRING");
        if (!string.IsNullOrWhiteSpace(fromEnv))
        {
            GuardForbidden(fromEnv);
            return fromEnv;
        }

        return $@"Server=(localdb)\MSSQLLocalDB;Database={DefaultTestDb};" +
               "Trusted_Connection=True;TrustServerCertificate=True";
    }

    /// <summary>
    /// Returns <c>true</c> if the test database is reachable.
    /// In CI mode (<c>ALPLA_INTEGRATION_TESTS_REQUIRED=true</c>), throws instead of
    /// returning <c>false</c> so missing databases are not silently skipped.
    /// </summary>
    public static bool CanConnect()
    {
        try
        {
            using var c = new SqlConnection(GetConnectionString());
            c.Open();
            return true;
        }
        catch (Exception ex)
        {
            var ciRequired = Environment.GetEnvironmentVariable("ALPLA_INTEGRATION_TESTS_REQUIRED");
            if (string.Equals(ciRequired, "true", StringComparison.OrdinalIgnoreCase))
            {
                throw new InvalidOperationException(
                    $"[CI] Integration-test database is required but unavailable: {ex.Message}", ex);
            }
            return false;
        }
    }

    /// <summary>Creates EF Core options pointing to the isolated test database.</summary>
    public static DbContextOptions<ApplicationDbContext> CreateOptions()
        => new DbContextOptionsBuilder<ApplicationDbContext>()
            .UseSqlServer(GetConnectionString())
            .Options;

    private static void GuardForbidden(string connectionString)
    {
        var builder = new SqlConnectionStringBuilder(connectionString);
        if (ForbiddenDatabases.Contains(builder.InitialCatalog))
        {
            throw new InvalidOperationException(
                $"FORBIDDEN: Integration tests must not use '{builder.InitialCatalog}'. " +
                $"Use '{DefaultTestDb}' or set ALPLA_TEST_CONNECTION_STRING to a safe isolated database.");
        }
    }
}
