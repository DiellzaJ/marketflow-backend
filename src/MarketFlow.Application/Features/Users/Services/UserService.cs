using MarketFlow.Application.Common.Interfaces;
using MarketFlow.Application.Common.Models;
using MarketFlow.Application.Features.Users.DTOs;
using MarketFlow.Application.Features.Users.Interfaces;

namespace MarketFlow.Application.Features.Users.Services;

public class UserService : IUserService
{
    private const string RootAdminRoleName = "RootAdmin";
    private const string CompanyAdminRoleName = "CompanyAdmin";
    private const string SellerRoleName = "Seller";
    private const string MainOperatorRoleName = "MainOperator";
    private const string DepartmentManagerRoleName = "DepartmentManager";

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
        var isRootAdmin = IsRootAdmin();
        var companyId = isRootAdmin
            ? request.CompanyId
            : _currentUserService.CompanyId;

        if (companyId is null)
        {
            return ServiceResult<UserDto>.Failure(
                isRootAdmin
                    ? "Company is required when RootAdmin creates a user."
                    : "Current company is required.");
        }

        if (string.IsNullOrWhiteSpace(request.FullName) ||
            string.IsNullOrWhiteSpace(request.Email) ||
            string.IsNullOrWhiteSpace(request.Password))
        {
            return ServiceResult<UserDto>.Failure("Full name, email, and password are required.");
        }

        var assignmentValidation = ValidateRoleAssignment(request);

        if (!string.IsNullOrWhiteSpace(assignmentValidation))
        {
            return ServiceResult<UserDto>.Failure(assignmentValidation);
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
        return string.Equals(_currentUserService.Role, RootAdminRoleName, StringComparison.OrdinalIgnoreCase);
    }

    private static string? ValidateRoleAssignment(CreateUserRequest request)
    {
        var roleName = (request.RoleName ?? string.Empty).Trim();
        var hasMarket = request.MarketId.HasValue;
        var hasDepartment = request.DepartmentId.HasValue;

        return roleName switch
        {
            SellerRoleName or MainOperatorRoleName when !hasMarket =>
                $"{roleName} requires market assignment.",
            SellerRoleName or MainOperatorRoleName when hasDepartment =>
                $"{roleName} cannot be assigned to a department.",
            DepartmentManagerRoleName when !hasMarket || !hasDepartment =>
                "DepartmentManager requires market and department assignment.",
            CompanyAdminRoleName when hasMarket || hasDepartment =>
                "CompanyAdmin cannot be assigned to a market or department.",
            _ when hasDepartment && !hasMarket =>
                "Department assignment requires market assignment.",
            _ => null
        };
    }
}
