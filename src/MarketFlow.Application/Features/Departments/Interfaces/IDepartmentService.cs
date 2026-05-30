using MarketFlow.Application.Common.Models;
using MarketFlow.Application.Features.Departments.DTOs;

namespace MarketFlow.Application.Features.Departments.Interfaces;

public interface IDepartmentService
{
    /// <summary>
    /// Returns departments from the department query service wrapped in a service result.
    /// </summary>
    Task<ServiceResult<IReadOnlyCollection<DepartmentDto>>> GetDepartmentsAsync(
        int? marketId = null,
        CancellationToken cancellationToken = default);

    Task<ServiceResult<DepartmentDto>> GetDepartmentAsync(
        int id,
        CancellationToken cancellationToken = default);

    Task<ServiceResult<DepartmentDto>> CreateDepartmentAsync(
        CreateDepartmentRequest request,
        CancellationToken cancellationToken = default);

    Task<ServiceResult<DepartmentDto>> UpdateDepartmentAsync(
        int id,
        UpdateDepartmentRequest request,
        CancellationToken cancellationToken = default);

    Task<ServiceResult<DepartmentDto>> DeactivateDepartmentAsync(
        int id,
        CancellationToken cancellationToken = default);

    Task<ServiceResult<DepartmentDto>> ActivateDepartmentAsync(
        int id,
        CancellationToken cancellationToken = default);
}
