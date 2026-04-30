using MarketFlow.Application.Common.Models;
using MarketFlow.Application.Features.Auth.DTOs;
using MarketFlow.Application.Features.Auth.Interfaces;

namespace MarketFlow.Application.Features.Auth.Services;

public class AuthService : IAuthService
{
    public Task<ServiceResult<AuthResponseDto>> LoginAsync(
        LoginRequestDto request,
        CancellationToken cancellationToken = default)
    {
        var response = new AuthResponseDto
        {
            AccessToken = "placeholder-token",
            ExpiresAt = DateTime.UtcNow.AddHours(1)
        };

        return Task.FromResult(ServiceResult<AuthResponseDto>.Success(response));
    }
}
