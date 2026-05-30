namespace MarketFlow.Domain.Entities;

public class RevokedAccessToken
{
    public int Id { get; set; }

    public string TokenId { get; set; } = string.Empty;

    public int UserId { get; set; }

    public User User { get; set; } = null!;

    public DateTimeOffset ExpiresAt { get; set; }

    public DateTimeOffset RevokedAt { get; set; }
}
