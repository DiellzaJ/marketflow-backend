using MarketFlow.Domain.Entities;

namespace MarketFlow.Application.Features.Auth.Interfaces;

public interface IJwtTokenService
{
    string GenerateAccessToken(User user);

    string GenerateRefreshToken();

    string HashRefreshToken(string refreshToken);
}