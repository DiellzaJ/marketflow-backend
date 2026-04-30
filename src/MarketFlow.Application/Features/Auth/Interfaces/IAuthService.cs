using MarketFlow.Application.Common.Models;
using MarketFlow.Application.Features.Auth.DTOs;

namespace MarketFlow.Application.Features.Auth.Interfaces;

public interface IAuthService
{
    Task<ServiceResult<AuthResponseDto>> LoginAsync(
        LoginRequestDto request,
        CancellationToken cancellationToken = default);
}
