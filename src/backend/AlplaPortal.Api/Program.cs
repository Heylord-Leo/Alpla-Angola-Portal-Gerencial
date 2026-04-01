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
builder.Services.Configure<JwtOptions>(builder.Configuration.GetSection("Jwt"));
builder.Services.AddScoped<IPasswordHasher, PasswordHasher>();
builder.Services.AddScoped<IJwtService, JwtService>();
builder.Services.AddScoped<IAuthService, AuthService>();

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

app.UseAuthentication();
app.UseAuthorization();

app.MapControllers();
app.MapHealthChecks("/health");

app.Run();
