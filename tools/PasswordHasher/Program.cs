// ═══════════════════════════════════════════════════════════════════════════
// Password Hash Generator for Alpla Portal Admin Seed
// Usage: dotnet run --project tools/PasswordHasher -- "YourPassword"
// Output: BCrypt hash to stdout (copy into ADMIN_USER_SEED_TEMPLATE.sql)
// ═══════════════════════════════════════════════════════════════════════════

if (args.Length == 0)
{
    Console.Error.WriteLine("Usage: dotnet run --project tools/PasswordHasher -- \"YourTemporaryPassword\"");
    Console.Error.WriteLine();
    Console.Error.WriteLine("Generates a BCrypt hash suitable for the ADMIN_USER_SEED_TEMPLATE.sql.");
    Console.Error.WriteLine("The hash uses the same algorithm (BCrypt.Net-Next 4.1.0) as the application.");
    return 1;
}

var password = args[0];

if (password.Length < 8)
{
    Console.Error.WriteLine("ERROR: Password must be at least 8 characters.");
    return 1;
}

var hash = BCrypt.Net.BCrypt.HashPassword(password);
Console.WriteLine(hash);
return 0;
