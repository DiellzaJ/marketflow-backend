using MarketFlow.Application.Common.Interfaces;
using MarketFlow.Application.Common.Models;
using MarketFlow.Application.Features.Users.Configuration;
using MarketFlow.Application.Features.Users.DTOs;
using MarketFlow.Application.Features.Users.Interfaces;

namespace MarketFlow.Application.Features.Users.Services;

public sealed class UserCreationValidator : IUserCreationValidator
{
    private readonly IUserStore _userStore;

    public UserCreationValidator(IUserStore userStore)
    {
        _userStore = userStore;
    }

    /// <summary>
    /// Validates user creation and returns the resolved company id in Data on success.
    /// </summary>
    public async Task<ServiceResult<int>> ValidateAsync(
        CreateUserRequest request,
        bool isRootAdmin,
        string? currentRole,
        int? currentCompanyId,
        CancellationToken cancellationToken = default)
    {
        if (string.IsNullOrWhiteSpace(request.FullName) ||
            string.IsNullOrWhiteSpace(request.Email) ||
            string.IsNullOrWhiteSpace(request.Password))
        {
            return ServiceResult<int>.Failure("Full name, email, and password are required.");
        }

        var normalizedRoleName = RoleAssignmentRules.NormalizeRoleName(request.RoleName ?? string.Empty);
        var roleValidation = RoleAssignmentRules.ValidateCreatePermission(currentRole, normalizedRoleName);

        if (!string.IsNullOrWhiteSpace(roleValidation))
        {
            return ServiceResult<int>.Failure(roleValidation);
        }

        var companyValidation = ResolveCompanyId(request, isRootAdmin, currentCompanyId);

        if (!companyValidation.Succeeded)
        {
            return companyValidation;
        }

        var companyId = companyValidation.Data;

        if (companyId <= 0)
        {
            return ServiceResult<int>.Failure("Company ID must be greater than zero.");
        }

        var companyExists = await _userStore.CompanyExistsAsync(companyId, cancellationToken);

        if (!companyExists)
        {
            return ServiceResult<int>.Failure($"Company ID {companyId} not found or inactive.");
        }

        var assignmentValidation = RoleAssignmentRules.ValidateAssignment(
            normalizedRoleName,
            request.MarketId,
            request.DepartmentId);

        if (!string.IsNullOrWhiteSpace(assignmentValidation))
        {
            return ServiceResult<int>.Failure(assignmentValidation);
        }

        if (request.MarketId.HasValue)
        {
            var marketExists = await _userStore.MarketExistsAsync(
                companyId,
                request.MarketId.Value,
                cancellationToken);

            if (!marketExists)
            {
                return ServiceResult<int>.Failure($"Market ID {request.MarketId.Value} not found or inactive.");
            }
        }

        if (request.MarketId.HasValue && request.DepartmentId.HasValue)
        {
            var departmentExists = await _userStore.DepartmentExistsAsync(
                companyId,
                request.MarketId.Value,
                request.DepartmentId.Value,
                cancellationToken);

            if (!departmentExists)
            {
                return ServiceResult<int>.Failure(
                    $"Department ID {request.DepartmentId.Value} not found or inactive in market {request.MarketId.Value}.");
            }
        }

        return ServiceResult<int>.Success(companyId);
    }

    private static ServiceResult<int> ResolveCompanyId(
        CreateUserRequest request,
        bool isRootAdmin,
        int? currentCompanyId)
    {
        if (isRootAdmin)
        {
            return request.CompanyId is null
                ? ServiceResult<int>.Failure("Company is required when RootAdmin creates a user.")
                : ServiceResult<int>.Success(request.CompanyId.Value);
        }

        if (currentCompanyId is null)
        {
            return ServiceResult<int>.Failure("Current company is required.");
        }

        if (request.CompanyId.HasValue && request.CompanyId.Value != currentCompanyId.Value)
        {
            return ServiceResult<int>.Failure(
                $"Company ID {request.CompanyId.Value} does not match the authenticated user's company.");
        }

        return ServiceResult<int>.Success(currentCompanyId.Value);
    }
}
