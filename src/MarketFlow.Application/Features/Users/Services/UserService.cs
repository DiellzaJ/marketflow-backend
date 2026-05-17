using MarketFlow.Application.Common.Interfaces;
using MarketFlow.Application.Common.Models;
using MarketFlow.Application.Features.Users.DTOs;
using MarketFlow.Application.Features.Users.Interfaces;

namespace MarketFlow.Application.Features.Users.Services;

public class UserService : IUserService
{
    private readonly IUserStore _userStore;
    private readonly ICurrentUserService _currentUserService;

    public UserService(
        IUserStore userStore,
        ICurrentUserService currentUserService)
    {
        _userStore = userStore;
        _currentUserService = currentUserService;
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

    public async Task<ServiceResult<UserDto>> CreateUserAsync(
        CreateUserRequest request,
        CancellationToken cancellationToken = default)
    {
        var companyId = IsRootAdmin()
            ? request.CompanyId ?? _currentUserService.CompanyId
            : _currentUserService.CompanyId;

        if (companyId is null)
        {
            return ServiceResult<UserDto>.Failure("Current company is required.");
        }

        if (string.IsNullOrWhiteSpace(request.FullName) ||
            string.IsNullOrWhiteSpace(request.Email) ||
            string.IsNullOrWhiteSpace(request.Password))
        {
            return ServiceResult<UserDto>.Failure("Full name, email, and password are required.");
        }

        var user = await _userStore.CreateUserAsync(companyId.Value, request, cancellationToken);

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
        return string.Equals(_currentUserService.Role, "RootAdmin", StringComparison.OrdinalIgnoreCase);
    }
}
