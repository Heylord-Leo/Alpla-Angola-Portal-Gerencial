namespace AlplaPortal.Application.Models.Configuration;

public class SecurityOptions
{
    public UploadOptions Upload { get; set; } = new();
    public AuthenticationOptions Authentication { get; set; } = new();
    public RateLimitingOptions RateLimiting { get; set; } = new();
}

public class UploadOptions
{
    public List<string> AllowedExtensions { get; set; } = new();
    public long MaxFileSizeBytes { get; set; }
    public List<string> BlockedExtensions { get; set; } = new();
}

public class AuthenticationOptions
{
    public int MaxFailedAttempts { get; set; }
    public int LockoutDurationMinutes { get; set; }
}

public class RateLimitingOptions
{
    public int PermitLimit { get; set; } = 10;
    public int WindowMinutes { get; set; } = 1;
    public bool EnableForLocalhost { get; set; } = false;
}
