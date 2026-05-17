using MarketFlow.Application.Features.Auth.DTOs;

namespace MarketFlow.Application.Features.Auth.Interfaces;

public interface IAuthService
{
    Task<AuthResponse> RegisterAsync(RegisterRequest request);

    Task<AuthResponse> CreateRootAdminAsync(CreateRootAdminRequest request);

    Task<AuthResponse> LoginAsync(LoginRequest request);

    Task<AuthResponse> RefreshTokenAsync(RefreshTokenRequest request);

    Task LogoutAsync(int userId, string accessTokenId, DateTimeOffset accessTokenExpiresAt);
}
