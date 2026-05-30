using MarketFlow.Application.Common.Models;
using MarketFlow.Application.Features.Departments.DTOs;
using MarketFlow.Application.Features.Departments.Exceptions;
using MarketFlow.Application.Features.Departments.Interfaces;

namespace MarketFlow.Application.Features.Departments.Services;

public class DepartmentService : IDepartmentService
{
    private const int MaxNameLength = 100;
    private const string ActiveStateChangeMessage =
        "Use the department deactivate or activate endpoint to change active state.";

    private readonly IDepartmentStore _departmentStore;

    public DepartmentService(IDepartmentStore departmentStore)
    {
        _departmentStore = departmentStore;
    }

    public async Task<ServiceResult<IReadOnlyCollection<DepartmentDto>>> GetDepartmentsAsync(
        int? marketId = null,
        CancellationToken cancellationToken = default)
    {
        var departments = await _departmentStore.GetDepartmentsAsync(marketId, cancellationToken);

        return ServiceResult<IReadOnlyCollection<DepartmentDto>>.Success(departments);
    }

    public async Task<ServiceResult<DepartmentDto>> GetDepartmentAsync(
        int id,
        CancellationToken cancellationToken = default)
    {
        var department = await _departmentStore.GetDepartmentAsync(id, cancellationToken: cancellationToken);

        return department is null
            ? ServiceResult<DepartmentDto>.Failure("Department was not found.", ServiceResultFailureType.NotFound)
            : ServiceResult<DepartmentDto>.Success(department);
    }

    public async Task<ServiceResult<DepartmentDto>> CreateDepartmentAsync(
        CreateDepartmentRequest request,
        CancellationToken cancellationToken = default)
    {
        var validationError = await ValidateDepartmentForCreateAsync(request, cancellationToken);

        if (validationError is not null)
        {
            return validationError;
        }

        request.Name = request.Name.Trim();
        request.Description = NormalizeOptionalText(request.Description);

        try
        {
            var department = await _departmentStore.CreateDepartmentAsync(request, cancellationToken);
            return ServiceResult<DepartmentDto>.Success(department, "Department created.");
        }
        catch (DepartmentMarketNotFoundException)
        {
            return ServiceResult<DepartmentDto>.Failure(
                "Department market was not found.",
                ServiceResultFailureType.NotFound);
        }
        catch (DepartmentNameConflictException)
        {
            return NameConflict();
        }
    }

    public async Task<ServiceResult<DepartmentDto>> UpdateDepartmentAsync(
        int id,
        UpdateDepartmentRequest request,
        CancellationToken cancellationToken = default)
    {
        if (request.IsActive.HasValue)
        {
            return ServiceResult<DepartmentDto>.Failure(ActiveStateChangeMessage);
        }

        var validationError = ValidateDepartmentShape(request.Name);

        if (validationError is not null)
        {
            return validationError;
        }

        request.Name = request.Name.Trim();
        request.Description = NormalizeOptionalText(request.Description);

        DepartmentDto? department;

        try
        {
            department = await _departmentStore.UpdateDepartmentAsync(id, request, cancellationToken);
        }
        catch (DepartmentNameConflictException)
        {
            return NameConflict();
        }

        return department is null
            ? ServiceResult<DepartmentDto>.Failure("Department was not found.", ServiceResultFailureType.NotFound)
            : ServiceResult<DepartmentDto>.Success(department, "Department updated.");
    }

    public async Task<ServiceResult<DepartmentDto>> DeactivateDepartmentAsync(
        int id,
        CancellationToken cancellationToken = default)
    {
        var currentDepartment = await _departmentStore.GetDepartmentAsync(
            id,
            includeInactive: true,
            cancellationToken);

        if (currentDepartment is null)
        {
            return ServiceResult<DepartmentDto>.Failure("Department was not found.", ServiceResultFailureType.NotFound);
        }

        if (!currentDepartment.IsActive)
        {
            return ServiceResult<DepartmentDto>.Failure(
                "Department is already inactive.",
                ServiceResultFailureType.Conflict);
        }

        var department = await _departmentStore.SetDepartmentActiveStateAsync(
            id,
            isActive: false,
            cancellationToken: cancellationToken);

        if (department is not null)
        {
            return ServiceResult<DepartmentDto>.Success(department, "Department deactivated.");
        }

        currentDepartment = await _departmentStore.GetDepartmentAsync(
            id,
            includeInactive: true,
            cancellationToken);

        if (currentDepartment is null)
        {
            return ServiceResult<DepartmentDto>.Failure("Department was not found.", ServiceResultFailureType.NotFound);
        }

        return !currentDepartment.IsActive
            ? ServiceResult<DepartmentDto>.Failure(
                "Department is already inactive.",
                ServiceResultFailureType.Conflict)
            : ServiceResult<DepartmentDto>.Failure("Department active state could not be changed.");
    }

    public async Task<ServiceResult<DepartmentDto>> ActivateDepartmentAsync(
        int id,
        CancellationToken cancellationToken = default)
    {
        var currentDepartment = await _departmentStore.GetDepartmentAsync(
            id,
            includeInactive: true,
            cancellationToken);

        if (currentDepartment is null)
        {
            return ServiceResult<DepartmentDto>.Failure("Department was not found.", ServiceResultFailureType.NotFound);
        }

        if (currentDepartment.IsActive)
        {
            return ServiceResult<DepartmentDto>.Failure(
                "Department is already active.",
                ServiceResultFailureType.Conflict);
        }

        var department = await _departmentStore.SetDepartmentActiveStateAsync(
            id,
            isActive: true,
            cancellationToken: cancellationToken);

        if (department is not null)
        {
            return ServiceResult<DepartmentDto>.Success(department, "Department activated.");
        }

        currentDepartment = await _departmentStore.GetDepartmentAsync(
            id,
            includeInactive: true,
            cancellationToken);

        if (currentDepartment is null)
        {
            return ServiceResult<DepartmentDto>.Failure("Department was not found.", ServiceResultFailureType.NotFound);
        }

        return currentDepartment.IsActive
            ? ServiceResult<DepartmentDto>.Failure(
                "Department is already active.",
                ServiceResultFailureType.Conflict)
            : ServiceResult<DepartmentDto>.Failure("Department active state could not be changed.");
    }

    private async Task<ServiceResult<DepartmentDto>?> ValidateDepartmentForCreateAsync(
        CreateDepartmentRequest request,
        CancellationToken cancellationToken)
    {
        if (request.MarketId <= 0)
        {
            return ServiceResult<DepartmentDto>.Failure("Department marketId is required.");
        }

        var validationError = ValidateDepartmentShape(request.Name);

        if (validationError is not null)
        {
            return validationError;
        }

        if (!await _departmentStore.MarketExistsAsync(request.MarketId, cancellationToken))
        {
            return ServiceResult<DepartmentDto>.Failure(
                "Department market was not found.",
                ServiceResultFailureType.NotFound);
        }

        if (await _departmentStore.DepartmentNameExistsAsync(request.MarketId, request.Name, cancellationToken: cancellationToken))
        {
            return NameConflict();
        }

        return null;
    }

    private static ServiceResult<DepartmentDto>? ValidateDepartmentShape(string name)
    {
        if (string.IsNullOrWhiteSpace(name))
        {
            return ServiceResult<DepartmentDto>.Failure("Department name is required.");
        }

        if (name.Trim().Length > MaxNameLength)
        {
            return ServiceResult<DepartmentDto>.Failure(
                $"Department name cannot exceed {MaxNameLength} characters.");
        }

        return null;
    }

    private static string? NormalizeOptionalText(string? value)
    {
        return string.IsNullOrWhiteSpace(value) ? null : value.Trim();
    }

    private static ServiceResult<DepartmentDto> NameConflict()
    {
        return ServiceResult<DepartmentDto>.Failure(
            "Department name is already used by another department in this market.",
            ServiceResultFailureType.Conflict);
    }
}
