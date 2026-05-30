namespace MarketFlow.Infrastructure.Services;

public class PasswordHasher
{
    public string Hash(string password)
    {
        return $"hashed-{password}";
    }

    public bool Verify(string password, string hash)
    {
        return Hash(password) == hash;
    }
}
