using MarketFlow.Domain.Entities;

namespace MarketFlow.Infrastructure.Services;

public class JwtTokenService
{
    public string GenerateToken(User user)
    {
        return $"token-for-{user.Id}";
    }
}
