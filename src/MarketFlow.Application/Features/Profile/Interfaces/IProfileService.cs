using MarketFlow.Application.Common.Models;
using MarketFlow.Application.Features.Profile.DTOs;

namespace MarketFlow.Application.Features.Profile.Interfaces;

public interface IProfileService
{
    Task<ServiceResult<UserProfileResponse>> GetProfileAsync(CancellationToken cancellationToken = default);

    Task<ServiceResult<UserProfileResponse>> UpdateProfileAsync(UpdateProfileRequest request, CancellationToken cancellationToken = default);

    Task<ServiceResult<bool>> ChangePasswordAsync(ChangePasswordRequest request, CancellationToken cancellationToken = default);
}
