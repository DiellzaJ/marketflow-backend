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
        return new TenantIntegrationTestOptions
        {
            ConnectionString = Environment.GetEnvironmentVariable(environmentVariableName) ?? string.Empty
        };
    }
}
