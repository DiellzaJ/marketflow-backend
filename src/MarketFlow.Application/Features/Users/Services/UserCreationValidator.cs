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
        int? currentCompanyId,
        CancellationToken cancellationToken = default)
    {
        var companyId = isRootAdmin ? request.CompanyId : currentCompanyId;

        if (companyId is null)
        {
            return ServiceResult<int>.Failure(
                isRootAdmin
                    ? "Company is required when RootAdmin creates a user."
                    : "Current company is required.");
        }

        if (string.IsNullOrWhiteSpace(request.FullName) ||
            string.IsNullOrWhiteSpace(request.Email) ||
            string.IsNullOrWhiteSpace(request.Password))
        {
            return ServiceResult<int>.Failure("Full name, email, and password are required.");
        }

        var assignmentValidation = RoleAssignmentRules.ValidateAssignment(
            request.RoleName ?? string.Empty,
            request.MarketId,
            request.DepartmentId);

        if (!string.IsNullOrWhiteSpace(assignmentValidation))
        {
            return ServiceResult<int>.Failure(assignmentValidation);
        }

        if (request.MarketId.HasValue)
        {
            var marketExists = await _userStore.MarketExistsAsync(
                companyId.Value,
                request.MarketId.Value,
                cancellationToken);

            if (!marketExists)
            {
                return ServiceResult<int>.Failure($"Market ID {request.MarketId.Value} not found.");
            }
        }

        if (request.MarketId.HasValue && request.DepartmentId.HasValue)
        {
            var departmentExists = await _userStore.DepartmentExistsAsync(
                companyId.Value,
                request.MarketId.Value,
                request.DepartmentId.Value,
                cancellationToken);

            if (!departmentExists)
            {
                return ServiceResult<int>.Failure(
                    $"Department ID {request.DepartmentId.Value} not found in market {request.MarketId.Value}.");
            }
        }

        return ServiceResult<int>.Success(companyId.Value);
    }
}
