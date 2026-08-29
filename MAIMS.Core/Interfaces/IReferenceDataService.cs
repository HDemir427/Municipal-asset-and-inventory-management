using MAIMS.Core.Entities;

namespace MAIMS.Core.Interfaces;

/// <summary>
/// Read-only lookup service for reference data (categories, departments, locations, users).
/// Used by WinForms dropdowns to populate DisplayMember/ValueMember without leaking
/// DbContext into the UI layer.
/// </summary>
public interface IReferenceDataService
{
    Task<IReadOnlyList<AssetCategory>> GetCategoriesAsync(CancellationToken ct = default);
    Task<IReadOnlyList<Department>> GetDepartmentsAsync(CancellationToken ct = default);
    Task<IReadOnlyList<Location>> GetLocationsAsync(CancellationToken ct = default);
    Task<IReadOnlyList<User>> GetCustodiansAsync(long? departmentId = null, CancellationToken ct = default);
}
