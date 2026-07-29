using System;
using System.Security.Claims;
using System.Text.Json;
using System.Threading.Tasks;
using AlplaPortal.Api.Services;
using AlplaPortal.Application.Versioning;
using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

namespace AlplaPortal.Api.Middleware;

/// <summary>Config: <c>ClientVersionEnforcement:Mode</c> = Disabled | Observe | EnforceMismatch | EnforceAll.</summary>
public class ClientVersionEnforcementOptions
{
    public string Mode { get; set; } = "Observe";

    public EnforcementMode ResolveMode() =>
        Enum.TryParse<EnforcementMode>(Mode, ignoreCase: true, out var m) ? m : EnforcementMode.Observe;
}

/// <summary>
/// Backend write-enforcement for version-mismatch protection (authoritative — the frontend monitor is
/// only proactive UX). Rejects unsafe (POST/PUT/PATCH/DELETE) requests whose <c>X-Portal-Frontend-Build</c>
/// header is incompatible with the deployed build, per the staged rollout mode. Decision logic lives in
/// the pure, unit-tested <see cref="ClientVersionEnforcement"/>; this middleware is a thin adapter that
/// logs safely and writes the RFC-7807 <c>CLIENT_VERSION_OUTDATED</c> response.
///
/// <para>Registered AFTER authentication/authorization so user + correlation context are available.
/// The client build header is untrusted metadata and is never used for authn/authz. When the server
/// build metadata is not <c>Valid</c>, enforcement fails-open (never self-locks the API); the
/// deployment-verification gate is what fails-closed on invalid metadata.</para>
/// </summary>
public sealed class ClientVersionEnforcementMiddleware
{
    public const string BuildHeader = "X-Portal-Frontend-Build";
    public const string VersionHeader = "X-Portal-Frontend-Version";
    public const string OutdatedCode = "CLIENT_VERSION_OUTDATED";

    private readonly RequestDelegate _next;
    private readonly ILogger<ClientVersionEnforcementMiddleware> _logger;
    private readonly IBuildInfoProvider _buildInfo;
    private readonly IOptionsMonitor<ClientVersionEnforcementOptions> _options;

    public ClientVersionEnforcementMiddleware(
        RequestDelegate next,
        ILogger<ClientVersionEnforcementMiddleware> logger,
        IBuildInfoProvider buildInfo,
        IOptionsMonitor<ClientVersionEnforcementOptions> options)
    {
        _next = next;
        _logger = logger;
        _buildInfo = buildInfo;
        _options = options;
    }

    public async Task InvokeAsync(HttpContext context)
    {
        var mode = _options.CurrentValue.ResolveMode();
        var method = context.Request.Method;
        var path = context.Request.Path.Value ?? string.Empty;

        // Fast path: nothing to do for disabled mode, safe methods, or exempt paths.
        if (mode == EnforcementMode.Disabled
            || !ClientVersionEnforcement.IsUnsafeMethod(method)
            || ClientVersionEnforcement.IsExemptPath(path, ClientVersionEnforcement.DefaultExemptPrefixes))
        {
            await _next(context);
            return;
        }

        var server = _buildInfo.Current;
        var clientBuild = context.Request.Headers[BuildHeader].ToString();
        if (string.IsNullOrEmpty(clientBuild)) clientBuild = null!;

        var decision = ClientVersionEnforcement.Decide(
            mode, method, path, clientBuild, server.BuildId, server.IsValid,
            ClientVersionEnforcement.DefaultExemptPrefixes);

        // Safe structured log — never JWT/body/secret. Include enough to drive rollout promotion.
        var userId = context.User?.Identity?.IsAuthenticated == true
            ? (context.User.FindFirst(ClaimTypes.NameIdentifier)?.Value ?? context.User.FindFirst("sub")?.Value ?? "authenticated")
            : "anonymous";
        var correlationId = context.Items.TryGetValue("CorrelationId", out var cid) ? cid?.ToString() : context.TraceIdentifier;

        if (!server.IsValid)
        {
            _logger.LogWarning("[ClientVersion] server build metadata is {Status} — enforcement fails-open. {Method} {Path} user={User} corr={Corr}",
                server.Status, method, path, userId, correlationId);
        }

        if (decision.Reject)
        {
            _logger.LogWarning("[ClientVersion] REJECT ({Reason}) mode={Mode} headerState={State} {Method} {Path} user={User} clientBuild={Client} serverBuild={Server} corr={Corr}",
                decision.Reason, mode, decision.HeaderState, method, path, userId, clientBuild ?? "(none)", server.BuildId, correlationId);
            await WriteOutdatedAsync(context, server.Version, server.BuildId, correlationId);
            return;
        }

        // Observe (and allowed enforce cases) — log at Information for matching, Warning for anomalies.
        if (decision.HeaderState is HeaderState.Missing or HeaderState.Malformed or HeaderState.Mismatched)
        {
            _logger.LogWarning("[ClientVersion] ALLOW ({Reason}) mode={Mode} headerState={State} {Method} {Path} user={User} clientBuild={Client} serverBuild={Server} corr={Corr}",
                decision.Reason, mode, decision.HeaderState, method, path, userId, clientBuild ?? "(none)", server.BuildId, correlationId);
        }

        await _next(context);
    }

    private static async Task WriteOutdatedAsync(HttpContext context, string version, string buildId, string? correlationId)
    {
        context.Response.StatusCode = StatusCodes.Status409Conflict;
        context.Response.ContentType = "application/problem+json";

        var payload = new
        {
            type = "https://portalgerencial/errors/client-version-outdated",
            title = "Versão do cliente desatualizada",
            status = 409,
            code = OutdatedCode,
            detail = "O Portal foi atualizado. Atualize a página para continuar.",
            message = "O Portal foi atualizado. Atualize a página para continuar.",
            currentVersion = version,
            currentBuildId = buildId,
            reloadRequired = true,
            correlationId
        };

        await context.Response.WriteAsync(JsonSerializer.Serialize(payload));
    }
}
