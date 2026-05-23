using MarketFlow.Application.Common.Models;
using MarketFlow.Application.Features.Departments.DTOs;
using MarketFlow.Application.Features.Departments.Interfaces;

namespace MarketFlow.Application.Features.Departments.Services;

public class DepartmentService : IDepartmentService
{
    private readonly IDepartmentQueryService _departmentQueryService;

    public DepartmentService(IDepartmentQueryService departmentQueryService)
    {
        _departmentQueryService = departmentQueryService;
    }

    public async Task<ServiceResult<IReadOnlyCollection<DepartmentDto>>> GetDepartmentsAsync(
        int? marketId = null,
        CancellationToken cancellationToken = default)
    {
        var departments = await _departmentQueryService.GetDepartmentsAsync(marketId, cancellationToken);

        return ServiceResult<IReadOnlyCollection<DepartmentDto>>.Success(departments);
    }
}
