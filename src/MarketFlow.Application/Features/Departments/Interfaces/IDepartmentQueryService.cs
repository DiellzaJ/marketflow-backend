using MarketFlow.Application.Features.Departments.DTOs;

namespace MarketFlow.Application.Features.Departments.Interfaces;

public interface IDepartmentQueryService
{
    /// <summary>
    /// Returns active departments for the authenticated user's resolved tenant, optionally filtered by market, ordered by name and then id.
    /// </summary>
    Task<IReadOnlyCollection<DepartmentDto>> GetDepartmentsAsync(
        int? marketId = null,
        CancellationToken cancellationToken = default);
}
