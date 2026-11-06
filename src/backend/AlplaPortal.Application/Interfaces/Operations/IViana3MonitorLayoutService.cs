using AlplaPortal.Application.DTOs.Operations;

namespace AlplaPortal.Application.Interfaces.Operations;

/// <summary>
/// Shared TV screens of the Viana 3 Production Monitor (persisted in the Portal
/// database, table OperationsBoardLayouts, one row per screen). Display preferences
/// only — no AlplaPROD access, no production data.
///
/// The default screen (key "default") backs the historical TV URL: it always exists
/// (virtually, with the discovery layout, until it is saved once) and cannot be deleted.
/// </summary>
public interface IViana3MonitorLayoutService
{
    /// <summary>Every screen of the board, default first.</summary>
    Task<IReadOnlyList<Viana3MonitorScreenDto>> ListScreensAsync(CancellationToken ct = default);

    /// <summary>Layout of one screen; null when the key is unknown (the default screen is never null).</summary>
    Task<Viana3MonitorLayoutDto?> GetLayoutAsync(string screenKey, CancellationToken ct = default);

    /// <summary>Creates a named screen with a generated stable key (optionally copying another screen's layout).</summary>
    Task<Viana3MonitorLayoutDto> CreateScreenAsync(CreateViana3MonitorScreenRequest request, Guid userId, CancellationToken ct = default);

    /// <summary>Renames a screen (the default screen included).</summary>
    Task<Viana3MonitorScreenDto?> RenameScreenAsync(string screenKey, RenameViana3MonitorScreenRequest request, Guid userId, CancellationToken ct = default);

    /// <summary>
    /// Validates and replaces the layout of one screen. Throws <see cref="Viana3MonitorLayoutValidationException"/>
    /// on an invalid document (nothing written). Null when the screen is unknown.
    /// </summary>
    Task<Viana3MonitorLayoutDto?> SaveLayoutAsync(string screenKey, SaveViana3MonitorLayoutRequest request, Guid userId, CancellationToken ct = default);

    /// <summary>
    /// Default screen: removes the saved row (back to the discovery layout). Named screen: empties the
    /// machine selection and restores the default columns, keeping the screen and its name. Null when unknown.
    /// </summary>
    Task<Viana3MonitorLayoutDto?> ResetLayoutAsync(string screenKey, Guid userId, CancellationToken ct = default);

    /// <summary>Deletes a named screen. Throws <see cref="Viana3MonitorLayoutValidationException"/> for the default screen. False when unknown.</summary>
    Task<bool> DeleteScreenAsync(string screenKey, Guid userId, CancellationToken ct = default);
}

/// <summary>Invalid layout / screen request (400 at the API).</summary>
public class Viana3MonitorLayoutValidationException : Exception
{
    public Viana3MonitorLayoutValidationException(string message) : base(message) { }
}
