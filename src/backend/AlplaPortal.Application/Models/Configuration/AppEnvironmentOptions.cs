namespace AlplaPortal.Application.Models.Configuration;

/// <summary>
/// Application environment configuration (DEC-140).
/// Controls visual differentiation between TEST and PRODUCTION.
/// Default values produce PRODUCTION behavior (no banner, no badge).
/// Configured via appsettings.json or IIS AppPool environment variables:
///   AppEnvironment__Code=TEST
///   AppEnvironment__Name=Ambiente de Teste
///   AppEnvironment__ShowBanner=true
/// </summary>
public class AppEnvironmentOptions
{
    public string Code { get; set; } = "PROD";
    public string Name { get; set; } = "Produção";
    public bool ShowBanner { get; set; } = false;
}
