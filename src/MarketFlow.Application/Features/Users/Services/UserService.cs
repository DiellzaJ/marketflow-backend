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
        if (string.IsNullOrWhiteSpace(request.FullName) ||
            string.IsNullOrWhiteSpace(request.Email))
        {
            return ServiceResult<UserDto>.Failure("Full name and email are required.");
        }

        request.RoleName = RoleAssignmentRules.NormalizeRoleName(request.RoleName ?? string.Empty);

        var user = await _userStore.UpdateUserAsync(
            id,
            _currentUserService.CompanyId,
            IsRootAdmin(),
            request,
            cancellationToken);

        return user is null
            ? ServiceResult<UserDto>.Failure("User was not found, role does not exist, or email is already used.")
            : ServiceResult<UserDto>.Success(user, "User updated.");
    }

    public async Task<ServiceResult<UserDto>> PatchUserAsync(
        int id,
        PatchUserRequest request,
        CancellationToken cancellationToken = default)
    {
        if (!string.IsNullOrWhiteSpace(request.RoleName))
        {
            request.RoleName = RoleAssignmentRules.NormalizeRoleName(request.RoleName);
        }

        var user = await _userStore.PatchUserAsync(
            id,
            _currentUserService.CompanyId,
            IsRootAdmin(),
            request,
            cancellationToken);

        return user is null
            ? ServiceResult<UserDto>.Failure("User was not found, role does not exist, or email is already used.")
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
            ? ServiceResult<bool>.Success(true, "User deleted.")
            : ServiceResult<bool>.Failure("User was not found.");
    }

    private bool IsRootAdmin()
    {
        return string.Equals(_currentUserService.Role, RoleAssignmentRules.RootAdmin, StringComparison.OrdinalIgnoreCase);
    }
}
