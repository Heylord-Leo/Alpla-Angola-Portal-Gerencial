namespace AlplaPortal.Application.Interfaces.Integration;

/// <summary>
/// Innux employee photo retrieval — read-only, single database.
///
/// Retrieves the actual binary photo (Fotografia blob) from Innux's
/// dbo.Funcionarios table. This is intentionally separated from
/// IInnuxEmployeeService, which only exposes a HasPhoto boolean
/// presence indicator and never fetches blob data.
///
/// Scope:
/// - Read-only: SELECT only, parameterized queries
/// - Returns raw bytes, no image processing
/// - Returns null if employee not found or has no photo
/// </summary>
public interface IInnuxEmployeePhotoService
{
    /// <summary>
    /// Retrieves the raw photo bytes for an employee by their employee number.
    /// Returns null if the employee is not found or has no photo.
    /// </summary>
    Task<byte[]?> GetEmployeePhotoAsync(string employeeNumber);
}
