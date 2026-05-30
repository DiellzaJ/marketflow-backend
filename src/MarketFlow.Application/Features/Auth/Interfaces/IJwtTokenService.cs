using MarketFlow.Domain.Entities;
using MarketFlow.Application.Features.Auth.DTOs;

namespace MarketFlow.Application.Features.Auth.Interfaces;

public interface IJwtTokenService
{
    string GenerateAccessToken(User user, AuthUserAssignmentDto? assignment = null);

    string GenerateRefreshToken();

    string HashRefreshToken(string refreshToken);
}
