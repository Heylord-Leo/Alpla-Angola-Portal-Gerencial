using AlplaPortal.Application.Interfaces;
using AlplaPortal.Application.Interfaces.Extraction;
using AlplaPortal.Application.Models.Configuration;
using AlplaPortal.Infrastructure.Data;
using AlplaPortal.Infrastructure.Logging;
using AlplaPortal.Infrastructure.Services;
using AlplaPortal.Infrastructure.Services.Extraction;
using AlplaPortal.Infrastructure.Services.Auth;
using AlplaPortal.Infrastructure.Services.Approvals;
using Microsoft.EntityFrameworkCore;
using Microsoft.AspNetCore.Authentication.JwtBearer;
using Microsoft.IdentityModel.Tokens;
using System.Text;
using System.Threading.RateLimiting;
using Microsoft.AspNetCore.RateLimiting;
using System.Collections.Concurrent;

var builder = WebApplication.CreateBuilder(args);

// Add services to the container.
builder.Services.AddControllers();
builder.Services.AddEndpointsApiExplorer();
builder.Services.AddSwaggerGen();
builder.Services.AddHttpClient();
builder.Services.AddHttpContextAccessor();

builder.Services.AddHttpClient<IDocumentExtractionProvider, LocalOcrExtractionProvider>();
builder.Services.AddHttpClient<IDocumentExtractionProvider, OpenAiDocumentExtractionProvider>();
builder.Services.AddScoped<IDocumentExtractionService, DocumentExtractionService>();
builder.Services.Configure<DocumentExtractionOptions>(builder.Configuration.GetSection("DocumentExtraction"));
builder.Services.AddScoped<IDocumentExtractionSettingsService, DocumentExtractionSettingsService>();

// Admin audit log writer — dedicated, best-effort persistence (not a generic ILoggerProvider).
builder.Services.AddScoped<AdminLogWriter>();

// Notification Service
builder.Services.AddScoped<INotificationService, NotificationService>();

// Approval Intelligence
builder.Services.AddScoped<IApprovalIntelligenceService, ApprovalIntelligenceService>();

// Auth Services
builder.Services.Configure<SecurityOptions>(builder.Configuration.GetSection("Security"));
builder.Services.Configure<JwtOptions>(builder.Configuration.GetSection("Jwt"));
builder.Services.AddScoped<IPasswordHasher, PasswordHasher>();
builder.Services.AddScoped<IJwtService, JwtService>();
builder.Services.AddScoped<IAuthService, AuthService>();
builder.Services.AddScoped<IEmailService, EmailService>();
builder.Services.AddScoped<ISmtpSettingsService, SmtpSettingsService>();

// Authentication
var jwtOptions = builder.Configuration.GetSection("Jwt").Get<JwtOptions>();
if (jwtOptions != null && !string.IsNullOrEmpty(jwtOptions.Secret))
{
    builder.Services.AddAuthentication(options =>
    {
        options.DefaultAuthenticateScheme = JwtBearerDefaults.AuthenticationScheme;
        options.DefaultChallengeScheme = JwtBearerDefaults.AuthenticationScheme;
    })
    .AddJwtBearer(options =>
    {
        options.RequireHttpsMetadata = false; 
        options.SaveToken = true;
        options.TokenValidationParameters = new TokenValidationParameters
        {
            ValidateIssuerSigningKey = true,
            IssuerSigningKey = new SymmetricSecurityKey(Encoding.ASCII.GetBytes(jwtOptions.Secret)),
            ValidateIssuer = true,
            ValidIssuer = jwtOptions.Issuer,
            ValidateAudience = true,
            ValidAudience = jwtOptions.Audience,
            ValidateLifetime = true,
            ClockSkew = TimeSpan.Zero
        };
    });
}

// Rate Limiting — Phase 2: Login IP-based Throttling
builder.Services.AddRateLimiter(options =>
{
    // Use a simple in-memory tracker for log throttling (one log per window per IP)
    var lastLogTimes = new ConcurrentDictionary<string, DateTime>();

    options.AddFixedWindowLimiter("LoginPolicy", opt =>
    {
        opt.PermitLimit = builder.Configuration.GetValue<int>("Security:RateLimiting:PermitLimit", 10);
        opt.Window = TimeSpan.FromMinutes(builder.Configuration.GetValue<int>("Security:RateLimiting:WindowMinutes", 1));
        opt.QueueLimit = 0;
    });

    options.RejectionStatusCode = StatusCodes.Status429TooManyRequests;

    options.OnRejected = async (context, token) =>
    {
        var securityOptions = builder.Configuration.GetSection("Security:RateLimiting").Get<RateLimitingOptions>();
        var ip = context.HttpContext.Connection.RemoteIpAddress?.ToString() ?? "unknown";

        // Throttled Logging: Only log once per minute per IP to avoid flooding
        var now = DateTime.UtcNow;
        if (lastLogTimes.TryGetValue(ip, out var lastLog) && (now - lastLog).TotalMinutes < 1)
        {
            // Skip logging but still reject
        }
        else
        {
            lastLogTimes[ip] = now;
            var adminLogWriter = context.HttpContext.RequestServices.GetRequiredService<AdminLogWriter>();
            await adminLogWriter.WriteAsync("Warning", "Auth", "IP_RATE_LIMITED", $"Login attempt throttled for IP: {ip}");
        }

        context.HttpContext.Response.StatusCode = StatusCodes.Status429TooManyRequests;
        if (context.Lease.TryGetMetadata(MetadataName.RetryAfter, out var retryAfter))
        {
            context.HttpContext.Response.Headers.RetryAfter = retryAfter.TotalSeconds.ToString();
        }

        await context.HttpContext.Response.WriteAsJsonAsync(new { message = "Muitas tentativas. Tente novamente em breve." }, token);
    };
});

// Configure Forwarded Headers for IP resolution behind proxies (disabled by default for safety)
// builder.Services.Configure<ForwardedHeadersOptions>(options => { options.ForwardedHeaders = Microsoft.AspNetCore.HttpOverrides.ForwardedHeaders.XForwardedFor | Microsoft.AspNetCore.HttpOverrides.ForwardedHeaders.XForwardedProto; });

// Health checks
builder.Services.AddHealthChecks();

// Problem details for standard error envelopes (RFC 7807)
builder.Services.AddProblemDetails();

// CORS for Vite local dev
builder.Services.AddCors(options =>
{
    options.AddPolicy("LocalFrontend", policy =>
    {
        policy.WithOrigins("http://localhost:5173")
              .AllowAnyMethod()
              .AllowAnyHeader();
    });
});

// Configure EF Core with SQL Server
var connectionString = builder.Configuration.GetConnectionString("DefaultConnection") 
    ?? throw new InvalidOperationException("Connection string 'DefaultConnection' not found.");

builder.Services.AddDbContext<ApplicationDbContext>(options =>
    options.UseSqlServer(connectionString, b => b.MigrationsAssembly("AlplaPortal.Infrastructure")));

var app = builder.Build();

// Correlation ID middleware — must run first so all downstream services can access the ID.
app.UseMiddleware<CorrelationIdMiddleware>();

// Initialize database
using (var scope = app.Services.CreateScope())
{
    var services = scope.ServiceProvider;
    try
    {
        var context = services.GetRequiredService<ApplicationDbContext>();
        context.Database.Migrate();
        Console.WriteLine("[DEBUG] Database initialized and migrations applied.");
    }
    catch (Exception ex)
    {
        Console.WriteLine($"[DEBUG] An error occurred while initializing the DB: {ex.Message}");
        if (ex.InnerException != null) Console.WriteLine($"[DEBUG] Inner: {ex.InnerException.Message}");
    }
}

// Configure the HTTP request pipeline.
if (app.Environment.IsDevelopment())
{
    app.UseSwagger();
    app.UseSwaggerUI();
}
else 
{
    // Return standard problem details format on unhandled exceptions in non-dev envs
    app.UseExceptionHandler(); 
}

// Only force HTTPS redirect if we aren't in Development
if (!app.Environment.IsDevelopment())
{
    app.UseHttpsRedirection();
}

app.UseCors("LocalFrontend");

app.UseRateLimiter();

app.UseAuthentication();
app.UseAuthorization();

app.MapControllers();
app.MapHealthChecks("/health");

app.Run();
