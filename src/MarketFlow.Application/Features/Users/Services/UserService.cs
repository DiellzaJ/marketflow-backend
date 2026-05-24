using MarketFlow.Application.Common.Interfaces;
using MarketFlow.Application.Common.Models;
using MarketFlow.Application.Features.Users.Configuration;
using MarketFlow.Application.Features.Users.DTOs;
using MarketFlow.Application.Features.Users.Interfaces;

namespace MarketFlow.Application.Features.Users.Services;

public class UserService : IUserService
{
    private readonly IUserStore _userStore;
    private readonly ICurrentUserService _currentUserService;
    private readonly IUserCreationValidator _userCreationValidator;

    public UserService(
        IUserStore userStore,
        ICurrentUserService currentUserService,
        IUserCreationValidator userCreationValidator)
    {
        _userStore = userStore;
        _currentUserService = currentUserService;
        _userCreationValidator = userCreationValidator;
    }

    public async Task<ServiceResult<IReadOnlyCollection<UserDto>>> GetUsersAsync(
        CancellationToken cancellationToken = default)
    {
        var users = await _userStore.GetUsersAsync(
            _currentUserService.CompanyId,
            IsRootAdmin(),
            cancellationToken);

        return ServiceResult<IReadOnlyCollection<UserDto>>.Success(users);
    }

    public async Task<ServiceResult<UserDto>> GetUserAsync(
        int id,
        CancellationToken cancellationToken = default)
    {
        var user = await _userStore.GetUserAsync(
            id,
            _currentUserService.CompanyId,
            IsRootAdmin(),
            cancellationToken);

        return user is null
            ? ServiceResult<UserDto>.Failure("User was not found.")
            : ServiceResult<UserDto>.Success(user);
    }

    public async Task<ServiceResult<UserDto>> CreateUserAsync(
        CreateUserRequest request,
        CancellationToken cancellationToken = default)
    {
        var isRootAdmin = IsRootAdmin();

        var validation = await _userCreationValidator.ValidateAsync(
            request,
            isRootAdmin,
            _currentUserService.Role,
            _currentUserService.CompanyId,
            cancellationToken);

        if (!validation.Succeeded)
        {
            return ServiceResult<UserDto>.Failure(validation.Message);
        }

        var normalizedRequest = new CreateUserRequest
        {
            FullName = request.FullName.Trim(),
            Email = request.Email.Trim(),
            Password = request.Password,
            CompanyId = request.CompanyId,
            // Canonical role names keep database role lookups case-insensitive at the service boundary.
            RoleName = RoleAssignmentRules.NormalizeRoleName(request.RoleName ?? string.Empty),
            MarketId = request.MarketId,
            DepartmentId = request.DepartmentId,
            IsActive = request.IsActive
        };

        var user = await _userStore.CreateUserAsync(
            validation.Data,
            normalizedRequest,
            cancellationToken);

        return user is null
            ? ServiceResult<UserDto>.Failure("Role does not exist or email is already used.")
            : ServiceResult<UserDto>.Success(user, "User created.");
    }

    public async Task<ServiceResult<UserDto>> UpdateUserAsync(
        int id,
        UpdateUserRequest request,
        CancellationToken cancellationToken = default)
    {
        if (string.IsNullOrWhiteSpace(request.FullName))
        {
            return ServiceResult<UserDto>.Failure("Full name is required.");
        }

        request.RoleName = RoleAssignmentRules.NormalizeRoleName(request.RoleName ?? string.Empty);
        var currentUser = await _userStore.GetUserAsync(
            id,
            _currentUserService.CompanyId,
            IsRootAdmin(),
            cancellationToken);

        if (currentUser is null)
        {
            return ServiceResult<UserDto>.Failure("User was not found.");
        }

        var validation = await ValidateUserMutationAsync(
            currentUser.CompanyId,
            request.RoleName,
            request.MarketId,
            request.DepartmentId,
            cancellationToken);

        if (!validation.Succeeded)
        {
            return ServiceResult<UserDto>.Failure(validation.Message);
        }

        var user = await _userStore.UpdateUserAsync(
            id,
            _currentUserService.CompanyId,
            IsRootAdmin(),
            request,
            cancellationToken);

        return user is null
            ? ServiceResult<UserDto>.Failure("User was not found.")
            : ServiceResult<UserDto>.Success(user, "User updated.");
    }

    public async Task<ServiceResult<UserDto>> PatchUserAsync(
        int id,
        PatchUserRequest request,
        CancellationToken cancellationToken = default)
    {
        var currentUser = await _userStore.GetUserAsync(
            id,
            _currentUserService.CompanyId,
            IsRootAdmin(),
            cancellationToken);

        if (currentUser is null)
        {
            return ServiceResult<UserDto>.Failure("User was not found.");
        }

        if (!string.IsNullOrWhiteSpace(request.RoleName))
        {
            request.RoleName = RoleAssignmentRules.NormalizeRoleName(request.RoleName);
        }

        var targetRoleName = !string.IsNullOrWhiteSpace(request.RoleName)
            ? request.RoleName
            : currentUser.RoleName;

        var targetAssignment = ResolveTargetAssignment(currentUser, request, targetRoleName);
        var validation = await ValidateUserMutationAsync(
            currentUser.CompanyId,
            targetRoleName,
            targetAssignment.MarketId,
            targetAssignment.DepartmentId,
            cancellationToken);

        if (!validation.Succeeded)
        {
            return ServiceResult<UserDto>.Failure(validation.Message);
        }

        var user = await _userStore.PatchUserAsync(
            id,
            _currentUserService.CompanyId,
            IsRootAdmin(),
            request,
            cancellationToken);

        return user is null
            ? ServiceResult<UserDto>.Failure("User was not found.")
            : ServiceResult<UserDto>.Success(user, "User updated.");
    }

    public async Task<ServiceResult<bool>> DeleteUserAsync(
        int id,
        CancellationToken cancellationToken = default)
    {
        var deleted = await _userStore.DeleteUserAsync(
            id,
            _currentUserService.CompanyId,
            IsRootAdmin(),
            cancellationToken);

        return deleted
            ? ServiceResult<bool>.Success(true, "User deactivated.")
            : ServiceResult<bool>.Failure("User was not found.");
    }

    private bool IsRootAdmin()
    {
        return string.Equals(_currentUserService.Role, RoleAssignmentRules.RootAdmin, StringComparison.OrdinalIgnoreCase);
    }

    private async Task<ServiceResult<bool>> ValidateUserMutationAsync(
        int companyId,
        string roleName,
        int? marketId,
        int? departmentId,
        CancellationToken cancellationToken)
    {
        if (string.IsNullOrWhiteSpace(roleName))
        {
            return ServiceResult<bool>.Failure("Role is required.");
        }

        if (string.Equals(roleName, RoleAssignmentRules.RootAdmin, StringComparison.OrdinalIgnoreCase))
        {
            return ServiceResult<bool>.Failure("RootAdmin users cannot be managed through this endpoint.");
        }

        var assignmentValidation = RoleAssignmentRules.ValidateAssignment(roleName, marketId, departmentId);

        if (!string.IsNullOrWhiteSpace(assignmentValidation))
        {
            return ServiceResult<bool>.Failure(assignmentValidation);
        }

        if (marketId.HasValue)
        {
            var marketExists = await _userStore.MarketExistsAsync(companyId, marketId.Value, cancellationToken);

            if (!marketExists)
            {
                return ServiceResult<bool>.Failure($"Market ID {marketId.Value} not found or inactive.");
            }
        }

        if (marketId.HasValue && departmentId.HasValue)
        {
            var departmentExists = await _userStore.DepartmentExistsAsync(
                companyId,
                marketId.Value,
                departmentId.Value,
                cancellationToken);

            if (!departmentExists)
            {
                return ServiceResult<bool>.Failure(
                    $"Department ID {departmentId.Value} not found or inactive in market {marketId.Value}.");
            }
        }

        return ServiceResult<bool>.Success(true);
    }

    private static UserAssignmentUpdate ResolveTargetAssignment(
        UserDto currentUser,
        PatchUserRequest request,
        string targetRoleName)
    {
        if (string.Equals(targetRoleName, RoleAssignmentRules.CompanyAdmin, StringComparison.OrdinalIgnoreCase))
        {
            return new UserAssignmentUpdate(null, null);
        }

        if (request.MarketId.HasValue)
        {
            return new UserAssignmentUpdate(request.MarketId.Value, request.DepartmentId);
        }

        if (request.DepartmentId.HasValue)
        {
            return new UserAssignmentUpdate(currentUser.Assignment?.MarketId, request.DepartmentId.Value);
        }

        return new UserAssignmentUpdate(
            currentUser.Assignment?.MarketId,
            currentUser.Assignment?.DepartmentId);
    }

    private sealed record UserAssignmentUpdate(int? MarketId, int? DepartmentId);
}
