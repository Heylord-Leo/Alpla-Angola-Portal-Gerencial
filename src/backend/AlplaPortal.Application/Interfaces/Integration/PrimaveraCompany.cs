namespace AlplaPortal.Application.Interfaces.Integration;

/// <summary>
/// Represents a configured Primavera business company/database target.
/// Maps directly to configuration keys under Integrations:Primavera:Companies.
///
/// These are stable internal codes — display labels should be resolved separately
/// if user-facing presentation is needed.
/// </summary>
public enum PrimaveraCompany
{
    ALPLAPLASTICO,
    ALPLASOPRO
}
