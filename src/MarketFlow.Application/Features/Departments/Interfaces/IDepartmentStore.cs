using MarketFlow.Application.Features.Departments.DTOs;

namespace MarketFlow.Application.Features.Departments.Interfaces;

public interface IDepartmentStore : IDepartmentQueryService
{
    Task<DepartmentDto?> GetDepartmentAsync(
        int id,
        bool includeInactive = false,
        CancellationToken cancellationToken = default);

    Task<bool> MarketExistsAsync(
        int marketId,
        CancellationToken cancellationToken = default);

    Task<bool> DepartmentNameExistsAsync(
        int marketId,
        string name,
        int? excludedDepartmentId = null,
        CancellationToken cancellationToken = default);

    Task<DepartmentDto> CreateDepartmentAsync(
        CreateDepartmentRequest request,
        CancellationToken cancellationToken = default);

    Task<DepartmentDto?> UpdateDepartmentAsync(
        int id,
        UpdateDepartmentRequest request,
        CancellationToken cancellationToken = default);

    Task<DepartmentDto?> SetDepartmentActiveStateAsync(
        int id,
        bool isActive,
        CancellationToken cancellationToken = default);
}
