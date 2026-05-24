using MarketFlow.Application.Common.Interfaces;
using MarketFlow.Application.Common.Models;
using MarketFlow.Application.Features.Profile.DTOs;
using MarketFlow.Application.Features.Profile.Interfaces;
using MarketFlow.Application.Features.Users.DTOs;

namespace MarketFlow.Application.Features.Profile.Services;

public class ProfileService : IProfileService
{
    private readonly IUserStore _userStore;
    private readonly ICurrentUserService _currentUserService;
    private readonly ICompanyStore _companyStore;

    public ProfileService(
        IUserStore userStore,
        ICurrentUserService currentUserService,
        ICompanyStore companyStore)
    {
        _userStore = userStore;
        _currentUserService = currentUserService;
        _companyStore = companyStore;
    }

    public async Task<ServiceResult<UserProfileResponse>> GetProfileAsync(CancellationToken cancellationToken = default)
    {
        if (!_currentUserService.UserId.HasValue)
        {
            return ServiceResult<UserProfileResponse>.Failure("Not authenticated.", ServiceResultFailureType.NotFound);
        }

        var user = await _userStore.GetUserAsync(_currentUserService.UserId.Value, _currentUserService.CompanyId, false, cancellationToken);

        if (user is null)
        {
            return ServiceResult<UserProfileResponse>.Failure("User not found.", ServiceResultFailureType.NotFound);
        }

        var profile = await MapAsync(user, _currentUserService.CompanyId, cancellationToken);

        return ServiceResult<UserProfileResponse>.Success(profile);
    }

    public async Task<ServiceResult<UserProfileResponse>> UpdateProfileAsync(UpdateProfileRequest request, CancellationToken cancellationToken = default)
    {
        if (!_currentUserService.UserId.HasValue)
        {
            return ServiceResult<UserProfileResponse>.Failure("Not authenticated.", ServiceResultFailureType.NotFound);
        }

        if (string.IsNullOrWhiteSpace(request.FullName))
        {
            return ServiceResult<UserProfileResponse>.Failure("Full name is required.");
        }

        var patch = new MarketFlow.Application.Features.Users.DTOs.PatchUserRequest
        {
            FullName = request.FullName.Trim()
        };

        var updated = await _userStore.PatchUserAsync(_currentUserService.UserId.Value, _currentUserService.CompanyId, false, patch, cancellationToken);

        return updated is null
            ? ServiceResult<UserProfileResponse>.Failure("User not found.", ServiceResultFailureType.NotFound)
            : ServiceResult<UserProfileResponse>.Success(await MapAsync(updated, _currentUserService.CompanyId, cancellationToken), "Profile updated.");
    }

    public async Task<ServiceResult<bool>> ChangePasswordAsync(ChangePasswordRequest request, CancellationToken cancellationToken = default)
    {
        if (!_currentUserService.UserId.HasValue)
        {
            return ServiceResult<bool>.Failure("Not authenticated.", ServiceResultFailureType.NotFound);
        }

        if (string.IsNullOrWhiteSpace(request.CurrentPassword) || string.IsNullOrWhiteSpace(request.NewPassword))
        {
            return ServiceResult<bool>.Failure("Current and new password are required.");
        }

        var changed = await _userStore.ChangePasswordAsync(_currentUserService.UserId.Value, request.CurrentPassword, request.NewPassword, _currentUserService.CompanyId, false, cancellationToken);

        return changed
            ? ServiceResult<bool>.Success(true, "Password changed.")
            : ServiceResult<bool>.Failure("Current password is invalid.");
    }

    private async Task<UserProfileResponse> MapAsync(UserDto user, int? companyId, CancellationToken cancellationToken)
    {
        var companyName = companyId.HasValue
            ? (await _companyStore.GetCompanyByIdAsync(companyId.Value, cancellationToken))?.Name
            : null;

        return new UserProfileResponse
        {
            Id = user.Id,
            FullName = user.FullName,
            Email = user.Email,
            Role = user.RoleName,
            IsActive = user.IsActive,
            CompanyId = companyId,
            CompanyName = companyName,
            MarketId = user.Assignment?.MarketId,
            MarketName = user.Assignment?.MarketName,
            DepartmentId = user.Assignment?.DepartmentId,
            DepartmentName = user.Assignment?.DepartmentName
        };
    }
}
