using AlplaPortal.Application.Interfaces;
using AlplaPortal.Application.Interfaces.Contracts;
using AlplaPortal.Application.Interfaces.Extraction;
using AlplaPortal.Application.Interfaces.Integration;
using AlplaPortal.Application.Models.Configuration;
using AlplaPortal.Infrastructure.Data;
using AlplaPortal.Infrastructure.Logging;
using AlplaPortal.Infrastructure.Services;
using AlplaPortal.Infrastructure.Services.Contracts;
using AlplaPortal.Infrastructure.Services.Extraction;
using AlplaPortal.Infrastructure.Services.Integration;
using AlplaPortal.Infrastructure.Services.Auth;
using AlplaPortal.Infrastructure.Services.Approvals;
using AlplaPortal.Application.Interfaces.MonthlyChanges;
using AlplaPortal.Application.Interfaces.Operations;
using AlplaPortal.Infrastructure.Services.MonthlyChanges;
using AlplaPortal.Infrastructure.Services.Integration.Operations;
using Microsoft.EntityFrameworkCore;
using Microsoft.AspNetCore.Authentication.JwtBearer;
using Microsoft.IdentityModel.Tokens;
using System.Text;
using System.Threading.RateLimiting;
using Microsoft.AspNetCore.RateLimiting;
using Microsoft.AspNetCore.HttpOverrides;
using System.Collections.Concurrent;

var builder = WebApplication.CreateBuilder(args);

// Add services to the container.
builder.Services.AddControllers();
builder.Services.AddEndpointsApiExplorer();
builder.Services.AddSwaggerGen();
builder.Services.AddHttpClient();
builder.Services.AddHttpContextAccessor();
builder.Services.AddMemoryCache();

builder.Services.AddHttpClient<IDocumentExtractionProvider, OpenAiDocumentExtractionProvider>();
builder.Services.AddScoped<IDocumentExtractionService, DocumentExtractionService>();
builder.Services.Configure<DocumentExtractionOptions>(builder.Configuration.GetSection("DocumentExtraction"));
builder.Services.AddScoped<IDocumentExtractionSettingsService, DocumentExtractionSettingsService>();

// Admin audit log writer — dedicated, best-effort persistence (not a generic ILoggerProvider).
builder.Services.AddScoped<AdminLogWriter>();

// Integration Foundation — settings cascade resolver + health service + concrete providers.
builder.Services.AddScoped<IntegrationConfigResolver>();
builder.Services.AddScoped<IIntegrationProvider, PrimaveraIntegrationProvider>();
builder.Services.AddScoped<IIntegrationProvider, InnuxIntegrationProvider>();
builder.Services.AddScoped<IIntegrationProvider, SmtpIntegrationProvider>();
builder.Services.AddScoped<IIntegrationProvider, OpenAiIntegrationProvider>();
builder.Services.AddScoped<AlplaProdConnectionFactory>();
builder.Services.AddScoped<IIntegrationProvider, AlplaProdIntegrationProvider>();

// Operations Module — Phase 2 Timeline + Phase 4 Transfer List + Phase 6 Details + Phase Live 2 Live Board
builder.Services.AddScoped<IOperationsPipelineDetector, OperationsPipelineDetector>();
builder.Services.AddScoped<IOperationsTimelineService, OperationsTimelineService>();
builder.Services.AddScoped<IOperationsTransferListService, OperationsTransferListService>();
builder.Services.AddScoped<IOperationsTransferDetailService, OperationsTransferDetailService>();
builder.Services.AddScoped<IOperationsLiveBoardService, OperationsLiveBoardService>();

builder.Services.AddScoped<IIntegrationHealthService, IntegrationHealthService>();
builder.Services.AddScoped<IIntegrationSettingsService, IntegrationSettingsService>();
builder.Services.AddScoped<PrimaveraConnectionFactory>();
builder.Services.AddScoped<IPrimaveraEmployeeService, PrimaveraEmployeeService>();
builder.Services.AddScoped<IPrimaveraArticleService, PrimaveraArticleService>();
builder.Services.AddScoped<IPrimaveraSupplierService, PrimaveraSupplierService>();
builder.Services.AddScoped<IPrimaveraArticleSupplierService, PrimaveraArticleSupplierService>();
builder.Services.AddScoped<IPrimaveraRequestValidationService, PrimaveraRequestValidationService>();
builder.Services.AddScoped<IPrimaveraDepartmentSyncService, PrimaveraDepartmentSyncService>();
builder.Services.AddScoped<IPrimaveraPlantSuggestionService, PrimaveraPlantSuggestionService>();
builder.Services.AddScoped<InnuxConnectionFactory>();
builder.Services.AddScoped<IInnuxEmployeeService, InnuxEmployeeService>();
builder.Services.AddScoped<IInnuxEmployeePhotoService, InnuxEmployeePhotoService>();
builder.Services.AddScoped<IInnuxAttendanceService, InnuxAttendanceService>();
builder.Services.AddScoped<IInnuxLookupService, InnuxLookupService>();
builder.Services.AddScoped<IInnuxScheduleService, InnuxScheduleService>();
builder.Services.AddScoped<IUnifiedEmployeeProfileService, UnifiedEmployeeProfileService>();
builder.Services.AddScoped<IHREmployeeSyncService, HREmployeeSyncService>();

// Portal-Side Attendance Interpretation — Phase 1 & 2 (diagnostic, read-only)
builder.Services.AddScoped<IPortalScheduleResolver, PortalScheduleResolver>();
builder.Services.AddScoped<IPortalPunchInterpreter, PortalPunchInterpreter>();

// Portal-Side Attendance Interpretation — Phase 3 (diagnostic comparison, read-only)
builder.Services.AddScoped<IAttendanceComparisonService, AttendanceComparisonService>();

// Monthly Changes Middleware — Innux → Portal → Primavera pipeline
builder.Services.AddScoped<IMonthlyChangesSyncService, MonthlyChangesSyncService>();
builder.Services.AddScoped<IOccurrenceDetectionEngine, OccurrenceDetectionEngine>();
builder.Services.AddScoped<IMonthlyChangesOrchestrator, MonthlyChangesOrchestrator>();

// Notification Service
builder.Services.AddScoped<INotificationService, NotificationService>();
builder.Services.AddScoped<IWorkflowNotificationOrchestrator, WorkflowNotificationOrchestrator>();

// Approval Intelligence
builder.Services.AddScoped<IApprovalIntelligenceService, ApprovalIntelligenceService>();

// Proforma Deadline Alerts — daily background check (first BackgroundService in the project)
builder.Services.AddHostedService<ProformaDeadlineAlertService>();

// Contract OCR Services
builder.Services.AddScoped<IContractOcrNormalisationService, ContractOcrNormalisationService>();
builder.Services.AddScoped<ContractOcrBackgroundProcessor>();

// Auth Services
builder.Services.Configure<SecurityOptions>(builder.Configuration.GetSection("Security"));
builder.Services.Configure<JwtOptions>(builder.Configuration.GetSection("Jwt"));
builder.Services.AddScoped<IPasswordHasher, PasswordHasher>();
builder.Services.AddScoped<IJwtService, JwtService>();
builder.Services.AddScoped<IAuthService, AuthService>();
builder.Services.AddScoped<IEmailService, EmailService>();
builder.Services.AddScoped<ISmtpSettingsService, SmtpSettingsService>();
builder.Services.AddScoped<ITEquipmentAgreementService>();
builder.Services.AddScoped<ITEquipmentPdfService>();

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

// Configure Forwarded Headers for correct scheme/IP resolution behind IIS ARR reverse proxy.
// In TEST/Production, IIS ARR proxies HTTPS → HTTP to Kestrel. Without this middleware,
// UseHttpsRedirection() sees plain HTTP and generates broken 307 redirects to internal
// localhost URLs (e.g., https://localhost:5001), which triggers browser "Not secure" warnings.
// KnownNetworks/KnownProxies are cleared because IIS ARR runs on the same machine (localhost)
// and is the only proxy in this architecture — this is safe for single-server IIS deployments.
builder.Services.Configure<ForwardedHeadersOptions>(options =>
{
    options.ForwardedHeaders = ForwardedHeaders.XForwardedFor | ForwardedHeaders.XForwardedProto;
    options.KnownNetworks.Clear();
    options.KnownProxies.Clear();
});

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

// =========================================================================
// Database Initialization — Environment-Aware Migration Handling (DEC-137)
// =========================================================================
// Development:  Run Database.Migrate() automatically (local iteration).
// Non-Dev:      Do NOT run Database.Migrate(). Detect pending migrations
//               and fail fast with a descriptive message listing each
//               missing migration ID. The IIS runtime identity must NOT
//               have DDL/db_owner permissions — all schema changes are
//               applied manually via controlled SQL scripts before deploy.
// =========================================================================
using (var scope = app.Services.CreateScope())
{
    var services = scope.ServiceProvider;
    try
    {
        var context = services.GetRequiredService<ApplicationDbContext>();

        if (app.Environment.IsDevelopment())
        {
            // --- Development: automatic migration (unchanged behavior) ---
            context.Database.Migrate();
            Console.WriteLine("[STARTUP] Database initialized and migrations applied successfully (Development).");
        }
        else
        {
            // --- Non-Development: detect pending migrations, never apply ---
            Console.WriteLine($"[STARTUP] Environment: {app.Environment.EnvironmentName} — automatic migrations DISABLED.");
            Console.WriteLine("[STARTUP] Checking for pending EF Core migrations...");

            var pendingMigrations = context.Database.GetPendingMigrations().ToList();

            if (pendingMigrations.Count > 0)
            {
                Console.WriteLine($"[STARTUP] FATAL: {pendingMigrations.Count} pending migration(s) detected.");
                Console.WriteLine("[STARTUP] The following migrations have NOT been applied to the database:");
                foreach (var migrationId in pendingMigrations)
                {
                    Console.WriteLine($"[STARTUP]   PENDING: {migrationId}");
                }
                Console.WriteLine("[STARTUP] REMEDIATION:");
                Console.WriteLine("[STARTUP]   1. Generate an idempotent SQL script:");
                Console.WriteLine("[STARTUP]      dotnet ef migrations script <last-applied> -i -o migration.sql");
                Console.WriteLine("[STARTUP]   2. Review and apply the script using SSMS or sqlcmd with a DBA account.");
                Console.WriteLine("[STARTUP]   3. Verify __EFMigrationsHistory matches the expected list.");
                Console.WriteLine("[STARTUP]   4. Restart the API App Pool.");
                Console.WriteLine("[STARTUP]   See: docs/DEPLOYMENT_CHECKLIST.md for the full procedure.");

                throw new InvalidOperationException(
                    $"[STARTUP] FATAL: {pendingMigrations.Count} pending EF Core migration(s) detected " +
                    $"in {app.Environment.EnvironmentName} environment. " +
                    $"Missing: {string.Join(", ", pendingMigrations)}. " +
                    "Migrations must be applied manually before the API can start. " +
                    "See docs/DEPLOYMENT_CHECKLIST.md for instructions.");
            }

            Console.WriteLine("[STARTUP] All EF Core migrations are up to date.");
        }

        // Post-migration schema validation: verify critical tables exist (all environments)
        var criticalTables = new[] { "Users", "Roles", "Plants", "Departments", "Companies",
            "RequestTypes", "RequestStatuses", "IvaRates", "Currencies", "Units",
            "NeedLevels", "LineItemStatuses", "SystemCounters", "CostCenters" };
        
        var connection = context.Database.GetDbConnection();
        connection.Open();
        foreach (var table in criticalTables)
        {
            using var cmd = connection.CreateCommand();
            cmd.CommandText = $"SELECT CASE WHEN OBJECT_ID('{table}', 'U') IS NOT NULL THEN 1 ELSE 0 END";
            var exists = (int)cmd.ExecuteScalar()!;
            if (exists != 1)
            {
                throw new InvalidOperationException(
                    $"[STARTUP] CRITICAL: Table '{table}' does not exist after migration. " +
                    "The database schema is incomplete. Run POST_INSTALL_DATABASE_VALIDATION.sql for diagnostics.");
            }
        }
        connection.Close();
        Console.WriteLine("[STARTUP] Post-migration schema validation passed — all critical tables exist.");
    }
    catch (Exception ex)
    {
        Console.WriteLine($"[STARTUP] CRITICAL: Database initialization failed: {ex.Message}");
        if (ex.InnerException != null)
            Console.WriteLine($"[STARTUP] Inner: {ex.InnerException.Message}");

        // In deployed environments (TEST/PRODUCTION), crash immediately.
        // The application must NOT start with a broken or partial schema.
        if (!app.Environment.IsDevelopment())
        {
            Console.WriteLine("[STARTUP] FATAL: Non-development environment detected. Shutting down to prevent operation with broken schema.");
            throw;
        }

        // In Development only: log warning and continue (enables local iteration)
        Console.WriteLine("[STARTUP] WARNING: Development environment — continuing despite migration failure. Fix the database before testing.");
    }
}

// Configure the HTTP request pipeline.

// Must be first: corrects request scheme and client IP from IIS ARR reverse proxy.
// IIS ARR sends X-Forwarded-Proto: https and X-Forwarded-For headers.
// Without this, the app sees all requests as plain HTTP behind the proxy.
app.UseForwardedHeaders();

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
