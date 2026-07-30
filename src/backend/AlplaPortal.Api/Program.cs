using AlplaPortal.Application.Interfaces;
using AlplaPortal.Application.Interfaces.Approvals;
using AlplaPortal.Application.Validation;
using AlplaPortal.Application.Interfaces.Contracts;
using AlplaPortal.Application.Interfaces.Extraction;
using AlplaPortal.Application.Interfaces.Integration;
using AlplaPortal.Application.Models.Configuration;
using AlplaPortal.Application.Versioning;
using AlplaPortal.Api.Services;
using AlplaPortal.Api.Middleware;
using AlplaPortal.Infrastructure.Data;
using AlplaPortal.Infrastructure.Logging;
using AlplaPortal.Infrastructure.Services;
using AlplaPortal.Infrastructure.Services.Contracts;
using AlplaPortal.Infrastructure.Services.Extraction;
using AlplaPortal.Infrastructure.Services.Integration;
using AlplaPortal.Infrastructure.Services.Auth;
using AlplaPortal.Infrastructure.Services.Approvals;
using AlplaPortal.Infrastructure.Services.Requests;
using AlplaPortal.Infrastructure.Services.Suppliers;
using AlplaPortal.Application.Interfaces.Purchasing;
using AlplaPortal.Application.Interfaces.Requests;
using AlplaPortal.Application.Interfaces.Finance;
using AlplaPortal.Domain.Configuration;
using AlplaPortal.Application.Interfaces.MonthlyChanges;
using AlplaPortal.Application.Interfaces.Operations;
using AlplaPortal.Infrastructure.Services.MonthlyChanges;
using AlplaPortal.Infrastructure.Services.Purchasing;
using AlplaPortal.Infrastructure.Services.Finance;
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

// G5: Malware scanning extension point — NoOp placeholder until real AV is integrated.
builder.Services.AddScoped<IFileScanService, NoOpFileScanService>();

// G4: OCR cleanup background service — runs daily, disabled by default (AutoCleanupEnabled=false).
builder.Services.AddHostedService<OcrCleanupService>();

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

// Purchasing Module
builder.Services.AddScoped<IGroupBuilderService, GroupBuilderService>();
// Cancelled-batch quotation reuse rule (Option C) — single source of truth for eligibility
builder.Services.AddScoped<IQuotationItemEligibilityService, QuotationItemEligibilityService>();
builder.Services.AddScoped<IStatusAggregationService, StatusAggregationService>();

// Finance — single source of truth for SCHEDULE/PAY/RETURN action eligibility (listing + execution)
builder.Services.AddScoped<IFinancePaymentEligibilityService, FinancePaymentEligibilityService>();

// Approval Intelligence
builder.Services.AddScoped<IApprovalIntelligenceService, ApprovalIntelligenceService>();
builder.Services.AddScoped<IRequestStatusSyncService, RequestStatusSyncService>();

// Shared line-item creation (standard add-item + buyer reconciliation workaround)
builder.Services.AddScoped<ILineItemFactory, LineItemFactory>();

// Phase 2 — reusable line-item validity rule (QUOTATION create + PAYMENT submit)
builder.Services.AddScoped<IRequestLineItemSubmissionValidator, RequestLineItemSubmissionValidator>();

// Phase 3 — shared supplier matching + DRAFT creation (general admin + contextual payment-OCR endpoints)
builder.Services.AddScoped<ISupplierCreationService, SupplierCreationService>();

// Post-Payment Completion Workflow — Release 1 foundation.
// The options bind to a section that ships with Enabled=false in every environment; when the
// section is absent the class defaults (Enabled=false, EffectiveDateUtc=MaxValue) apply, so an
// unconfigured environment can never switch the workflow on by accident.
// The service is a two-phase skeleton and is a no-op while disabled — nothing calls it yet.
builder.Services.Configure<PostPaymentCompletionOptions>(
    builder.Configuration.GetSection(PostPaymentCompletionOptions.SectionName));
builder.Services.AddScoped<IRequestCompletionService, RequestCompletionService>();

// Department Manager redesign — single source of truth for area-approval routing
builder.Services.AddScoped<IApprovalRoutingService, ApprovalRoutingService>();
builder.Services.AddScoped<IBatchExtraItemDecisionService, BatchExtraItemDecisionService>();
builder.Services.AddScoped<DepartmentManagerService>();
builder.Services.AddScoped<AreaApproverReconciliationService>();

// Proforma Deadline Alerts — daily background check (first BackgroundService in the project)
builder.Services.AddHostedService<ProformaDeadlineAlertService>();

// Email Outbox Processor — async email delivery queue (polls every 10s)
builder.Services.AddHostedService<EmailOutboxProcessor>();

// Contract OCR Services
builder.Services.AddScoped<IContractOcrNormalisationService, ContractOcrNormalisationService>();
builder.Services.AddScoped<ContractOcrBackgroundProcessor>();

// Application Environment — visual differentiation between TEST and PROD (DEC-140)
builder.Services.Configure<AppEnvironmentOptions>(builder.Configuration.GetSection("AppEnvironment"));

// Build/version identity — single runtime source of truth, loaded once from build-manifest.json
// (version-mismatch protection). Singleton because the manifest is immutable for the process lifetime.
builder.Services.Configure<ClientVersionEnforcementOptions>(builder.Configuration.GetSection("ClientVersionEnforcement"));
builder.Services.AddSingleton<IBuildInfoProvider, BuildInfoProvider>();

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
builder.Services.AddScoped<ITAssetCodeGeneratorService>();

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

// Configure EF Core with SQL Server.
// Guard against EMPTY as well as missing: the committed appsettings.json ships
// "DefaultConnection": "" on purpose (secrets stay out of git), and an empty string
// passes a null-check but produces the confusing runtime error "The ConnectionString
// property has not been initialized" on first DB use instead of failing at startup.
var connectionString = builder.Configuration.GetConnectionString("DefaultConnection");
if (string.IsNullOrWhiteSpace(connectionString))
{
    throw new InvalidOperationException(
        "Connection string 'DefaultConnection' is missing or empty. " +
        "In Development, provide it via src/backend/AlplaPortal.Api/appsettings.Development.json " +
        "(gitignored — see docs) or the environment variable ConnectionStrings__DefaultConnection. " +
        "In deployed environments, check appsettings.json / environment configuration.");
}

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
            // --- Development-only: fail-closed database identity guard ---
            // This guard MUST run BEFORE Database.Migrate() to prevent
            // EF Core from applying schema changes to a wrong database.
            // Does not affect TEST or PROD.
            const string canonicalDevDb = "Portal-Gerencial-Dev-ProdClone";

            // Step 1: Resolve and open the actual DbConnection
            var devConn = context.Database.GetDbConnection();
            if (devConn.State != System.Data.ConnectionState.Open)
                devConn.Open();

            // Step 2: Query DB_NAME() and server identity BEFORE any migration
            using (var preCheckCmd = devConn.CreateCommand())
            {
                preCheckCmd.CommandText = "SELECT DB_NAME(), @@SERVERNAME";
                using var preReader = preCheckCmd.ExecuteReader();
                if (preReader.Read())
                {
                    var preDb     = preReader.GetString(0);
                    var preServer = preReader.GetString(1);

                    Console.WriteLine($"[STARTUP] PRE-MIGRATION identity: Server={preServer}, Database={preDb}");
                    Console.WriteLine($"[STARTUP] Environment: {app.Environment.EnvironmentName}");

                    // Step 3: Abort immediately if DB_NAME() is not the canonical clone
                    if (!string.Equals(preDb, canonicalDevDb, StringComparison.OrdinalIgnoreCase))
                    {
                        throw new InvalidOperationException(
                            $"[STARTUP] FATAL: Development DB_NAME() is '{preDb}' — " +
                            $"expected '{canonicalDevDb}'. " +
                            "The API will not start against a non-canonical Development database. " +
                            "NO MIGRATIONS WERE APPLIED. " +
                            "Update appsettings.Development.json or set " +
                            "ConnectionStrings__DefaultConnection to point to the canonical clone.");
                    }

                    Console.WriteLine($"[STARTUP] PRE-MIGRATION identity guard PASSED.");
                }
            }

            // Step 4: Only after the pre-migration guard passes, apply migrations
            context.Database.Migrate();
            Console.WriteLine("[STARTUP] Database migrations applied successfully (Development).");

            // Step 5: Post-migration verification — query DB_NAME(), server, latest migration
            using (var postCheckCmd = devConn.CreateCommand())
            {
                postCheckCmd.CommandText =
                    "SELECT DB_NAME(), @@SERVERNAME, " +
                    "(SELECT TOP 1 MigrationId FROM __EFMigrationsHistory ORDER BY MigrationId DESC)";
                using var postReader = postCheckCmd.ExecuteReader();
                if (postReader.Read())
                {
                    var postDb     = postReader.GetString(0);
                    var postServer = postReader.GetString(1);
                    var latestMig  = postReader.IsDBNull(2) ? "(none)" : postReader.GetString(2);

                    // Step 6: Log both pre-migration and post-migration verification
                    Console.WriteLine($"[STARTUP] POST-MIGRATION identity: Server={postServer}, Database={postDb}");
                    Console.WriteLine($"[STARTUP] POST-MIGRATION latest migration: {latestMig}");

                    if (!string.Equals(postDb, canonicalDevDb, StringComparison.OrdinalIgnoreCase))
                    {
                        throw new InvalidOperationException(
                            $"[STARTUP] FATAL: Post-migration DB_NAME() is '{postDb}' — " +
                            $"expected '{canonicalDevDb}'. Database identity changed during migration.");
                    }
                }
            }
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

// ── DEV-ONLY: Connection pool + query plan warmup ─────────────────────
// LocalDB auto-suspends after ~5 minutes of inactivity. After a cold restart,
// the first real query would pay ~10-27s for plan compilation per query shape.
// This warmup forces that cost here at startup instead of on the first user request.
if (app.Environment.IsDevelopment())
{
    try
    {
        using var warmupScope = app.Services.CreateScope();
        var warmupCtx = warmupScope.ServiceProvider.GetRequiredService<ApplicationDbContext>();
        // Allow up to 120s per query — cold LocalDB plan compilation for complex
        // projections can exceed the default 30s CommandTimeout.
        warmupCtx.Database.SetCommandTimeout(120);
        
        var swTotal = System.Diagnostics.Stopwatch.StartNew();
        var sw = System.Diagnostics.Stopwatch.StartNew();
        var canConnect = await warmupCtx.Database.CanConnectAsync();
        sw.Stop();
        Console.WriteLine($"[DEV Warmup] CanConnectAsync = {canConnect} ({sw.ElapsedMilliseconds}ms)");
        
        if (canConnect)
        {
            // 1. Requests table — simple count (primes base Requests table plan)
            sw.Restart();
            await warmupCtx.Requests.AsNoTracking().CountAsync();
            sw.Stop();
            Console.WriteLine($"[DEV Warmup] Requests.Count() = {sw.ElapsedMilliseconds}ms");

            // 2. LineItems list — matches GetLineItems Count query shape
            //    (SelectMany + status filter + type filter)
            sw.Restart();
            var liStatuses = new[] {
                "WAITING_QUOTATION", "AREA_ADJUSTMENT", "FINAL_ADJUSTMENT",
                "PAYMENT_COMPLETED", "IN_FOLLOWUP",
                "WAITING_AREA_APPROVAL", "WAITING_FINAL_APPROVAL", "WAITING_COST_CENTER"
            };
            await warmupCtx.Requests.AsNoTracking()
                .Where(r => !r.IsCancelled && r.RequestType!.Code == "QUOTATION")
                .Where(r => liStatuses.Contains(r.Status!.Code))
                .SelectMany(
                    r => r.LineItems.Where(li => !li.IsDeleted).DefaultIfEmpty(),
                    (r, li) => new { r.Id, LineItemId = li != null ? (Guid?)li.Id : null })
                .CountAsync();
            sw.Stop();
            Console.WriteLine($"[DEV Warmup] LineItems Count shape = {sw.ElapsedMilliseconds}ms");

            // 3. LineItems list — page query shape (EXACT replica of GetLineItems projection)
            //    SQL Server caches plans by exact SQL text hash.
            //    Property names MUST match production code for identical column aliases.
            sw.Restart();
            await warmupCtx.Requests.AsNoTracking()
                .Where(r => !r.IsCancelled && r.RequestType!.Code == "QUOTATION")
                .Where(r => liStatuses.Contains(r.Status!.Code))
                .SelectMany(
                    r => r.LineItems.Where(li => !li.IsDeleted).DefaultIfEmpty(),
                    (r, li) => new { Request = r, LineItem = li })
                .OrderByDescending(x => x.Request.CreatedAtUtc)
                .ThenBy(x => x.Request.Id)
                .ThenBy(x => x.LineItem != null ? x.LineItem.LineNumber : 0)
                .Skip(0)
                .Take(1)
                .Select(x => new
                {
                    LineItem = x.LineItem,
                    RequestId = x.Request.Id,
                    RequestNumber = x.Request.RequestNumber,
                    RequestTitle = x.Request.Title,
                    RequestDescription = x.Request.Description,
                    RequestStatusName = x.Request.Status!.Name,
                    RequestStatusCode = x.Request.Status!.Code,
                    RequestStatusBadgeColor = x.Request.Status!.BadgeColor,
                    RequestTypeCode = x.Request.RequestType!.Code,
                    RequestTypeName = x.Request.RequestType!.Name,
                    RequestPlantId = x.Request.PlantId,
                    RequestPlantName = x.Request.Plant != null ? x.Request.Plant.Name : (string?)null,
                    RequesterName = x.Request.Requester!.FullName,
                    RequesterEmail = x.Request.Requester!.Email,
                    NeedByDateUtc = x.Request.NeedByDateUtc,
                    DepartmentName = x.Request.Department!.Name,
                    CompanyId = x.Request.CompanyId,
                    RequestSupplierId = x.Request.SupplierId,
                    RequestSupplierName = x.Request.Supplier != null ? x.Request.Supplier.Name : (string?)null,
                    RequestSupplierCode = x.Request.Supplier != null ? x.Request.Supplier.PortalCode : (string?)null,
                    RequestCurrencyId = x.Request.CurrencyId,
                    RequestCurrencyCode = x.Request.Currency != null ? x.Request.Currency.Code : (string?)null,
                    RequestUpdatedAtUtc = x.Request.UpdatedAtUtc,
                    RequestCreatedAtUtc = x.Request.CreatedAtUtc,
                    RequestBuyerId = x.Request.BuyerId,
                    RequestBuyerName = x.Request.Buyer != null ? x.Request.Buyer.FullName : (string?)null,
                    RequestBuyerEmail = x.Request.Buyer != null ? x.Request.Buyer.Email : (string?)null,
                    RequestAreaApproverId = x.Request.AreaApproverId,
                    RequestAreaApproverName = x.Request.AreaApprover != null ? x.Request.AreaApprover.FullName : (string?)null,
                    RequestAreaApproverEmail = x.Request.AreaApprover != null ? x.Request.AreaApprover.Email : (string?)null,
                    RequestFinalApproverId = x.Request.FinalApproverId,
                    RequestFinalApproverName = x.Request.FinalApprover != null ? x.Request.FinalApprover.FullName : (string?)null,
                    RequestFinalApproverEmail = x.Request.FinalApprover != null ? x.Request.FinalApprover.Email : (string?)null,
                    ItemPlantName = x.LineItem != null && x.LineItem.Plant != null ? x.LineItem.Plant.Name : (string?)null,
                    ItemUnitCode = x.LineItem != null && x.LineItem.Unit != null ? x.LineItem.Unit.Code : (string?)null,
                    ItemCurrencyCode = x.LineItem != null && x.LineItem.Currency != null ? x.LineItem.Currency.Code : (string?)null,
                    ItemStatusName = x.LineItem != null && x.LineItem.LineItemStatus != null ? x.LineItem.LineItemStatus.Name : (string?)null,
                    ItemStatusCode = x.LineItem != null && x.LineItem.LineItemStatus != null ? x.LineItem.LineItemStatus.Code : (string?)null,
                    ItemStatusBadgeColor = x.LineItem != null && x.LineItem.LineItemStatus != null ? x.LineItem.LineItemStatus.BadgeColor : (string?)null,
                    ItemSupplierName = x.LineItem != null && x.LineItem.Supplier != null ? x.LineItem.Supplier.Name : (x.LineItem != null ? x.LineItem.SupplierName : (string?)null),
                    ItemSupplierCode = x.LineItem != null && x.LineItem.Supplier != null ? x.LineItem.Supplier.PortalCode : (string?)null,
                    ItemPrimaveraCode = x.LineItem != null && x.LineItem.Supplier != null ? x.LineItem.Supplier.PrimaveraCode : (string?)null,
                    ItemCostCenterName = x.LineItem != null && x.LineItem.CostCenter != null ? x.LineItem.CostCenter.Name : (string?)null,
                    ItemCostCenterCode = x.LineItem != null && x.LineItem.CostCenter != null ? x.LineItem.CostCenter.Code : (string?)null,
                    ItemCatalogId = x.LineItem != null ? x.LineItem.ItemCatalogId : (int?)null
                })
                .ToListAsync();
            sw.Stop();
            Console.WriteLine($"[DEV Warmup] LineItems Page shape = {sw.ElapsedMilliseconds}ms");

            // 4. Finance payments — count query shape (status filter + attachment exists)
            sw.Restart();
            var finStatuses = new[] { "PO_ISSUED", "PAYMENT_SCHEDULED", "PAID" };
            await warmupCtx.Requests.AsNoTracking()
                .Where(r => finStatuses.Contains(r.Status!.Code)
                    && r.Attachments.Any(a => !a.IsDeleted && a.AttachmentTypeCode == "PO"))
                .CountAsync();
            sw.Stop();
            Console.WriteLine($"[DEV Warmup] Finance Count shape = {sw.ElapsedMilliseconds}ms");

            // 5. Finance payments — page query shape (joins + PoGroups)
            sw.Restart();
            await warmupCtx.Requests.AsNoTracking()
                .Where(r => finStatuses.Contains(r.Status!.Code)
                    && r.Attachments.Any(a => !a.IsDeleted && a.AttachmentTypeCode == "PO"))
                .Include(r => r.Status)
                .Include(r => r.RequestType)
                .Include(r => r.Quotations)
                .Include(r => r.PoGroups).ThenInclude(g => g.Payments)
                .OrderByDescending(r => r.NeedByDateUtc)
                .Take(1)
                .Select(r => new { r.Id, r.RequestNumber, StatusCode = r.Status!.Code })
                .ToListAsync();
            sw.Stop();
            Console.WriteLine($"[DEV Warmup] Finance Page shape = {sw.ElapsedMilliseconds}ms");

            // 6. Dashboard / GetRequests — count + page shape (multi-Include)
            sw.Restart();
            await warmupCtx.Requests.AsNoTracking()
                .Include(r => r.Status)
                .Include(r => r.RequestType)
                .Include(r => r.Requester)
                .Include(r => r.Department)
                .OrderByDescending(r => r.CreatedAtUtc)
                .Take(1)
                .Select(r => new { r.Id, r.RequestNumber, StatusCode = r.Status!.Code, TypeCode = r.RequestType!.Code })
                .ToListAsync();
            sw.Stop();
            Console.WriteLine($"[DEV Warmup] Dashboard/Requests Page shape = {sw.ElapsedMilliseconds}ms");

            // 7. EmailOutbox — recover stuck entries shape (raw SQL plan)
            sw.Restart();
            await warmupCtx.Database.ExecuteSqlRawAsync(
                "SELECT COUNT(*) FROM EmailOutbox WITH (NOLOCK) WHERE Status = 'PROCESSING'");
            sw.Stop();
            Console.WriteLine($"[DEV Warmup] EmailOutbox shape = {sw.ElapsedMilliseconds}ms");

            swTotal.Stop();
            Console.WriteLine($"[DEV Warmup] Query plan priming completed in {swTotal.ElapsedMilliseconds}ms");
        }
    }
    catch (Exception ex)
    {
        Console.WriteLine($"[DEV Warmup] Non-fatal error — {ex.Message}");
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

// Version-mismatch protection: reject outdated frontend WRITE requests (staged rollout via
// ClientVersionEnforcement:Mode). Placed after authn/authz so user + correlation context are
// available; reads, exempt paths, and invalid-server-metadata all pass through (fail-open).
app.UseMiddleware<ClientVersionEnforcementMiddleware>();

app.MapControllers();
app.MapHealthChecks("/health");

app.Run();
