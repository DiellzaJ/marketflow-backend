namespace MarketFlow.Api.Tests.Integration;

public sealed class TenantIntegrationTestOptions
{
    public const string DefaultConnectionStringEnvironmentVariable = "MARKETFLOW_TEST_DB_CONNECTION_STRING";

    public string ConnectionString { get; init; } = string.Empty;

    public string JwtIssuer { get; init; } = "MarketFlow.Tests";

    public string JwtAudience { get; init; } = "MarketFlow.Tests";

    public string JwtSecret { get; init; } =
        "MarketFlow integration tests need a deterministic local signing key.";

    public int AccessTokenMinutes { get; init; } = 60;

    public static TenantIntegrationTestOptions FromEnvironment(
        string environmentVariableName = DefaultConnectionStringEnvironmentVariable)
    {
        var accessTokenMinutesValue =
            GetEnvironmentValue("JWT_EXPIRES_MINUTES", "Jwt__AccessTokenMinutes");
        var accessTokenMinutes = int.TryParse(accessTokenMinutesValue, out var parsedAccessTokenMinutes)
            ? parsedAccessTokenMinutes
            : 60;

        return new TenantIntegrationTestOptions
        {
            ConnectionString = Environment.GetEnvironmentVariable(environmentVariableName) ?? string.Empty,
            JwtIssuer = GetEnvironmentValue("JWT_ISSUER", "Jwt__Issuer") ?? "MarketFlow.Tests",
            JwtAudience = GetEnvironmentValue("JWT_AUDIENCE", "Jwt__Audience") ?? "MarketFlow.Tests",
            JwtSecret = GetEnvironmentValue("JWT_SECRET", "Jwt__Secret") ??
                "MarketFlow integration tests need a deterministic local signing key.",
            AccessTokenMinutes = accessTokenMinutes
        };
    }

    private static string? GetEnvironmentValue(params string[] names)
    {
        return names
            .Select(Environment.GetEnvironmentVariable)
            .FirstOrDefault(value => !string.IsNullOrWhiteSpace(value));
    }
}
