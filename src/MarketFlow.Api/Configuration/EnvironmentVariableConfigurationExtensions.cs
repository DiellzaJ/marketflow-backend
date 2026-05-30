namespace MarketFlow.Api.Configuration;

public static class EnvironmentVariableConfigurationExtensions
{
    private static readonly IReadOnlyDictionary<string, string> ConfigurationMappings =
        new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase)
        {
            ["DB_CONNECTION_STRING"] = "ConnectionStrings:DefaultConnection",
            ["DB_HOST"] = "Database:Host",
            ["DB_PORT"] = "Database:Port",
            ["DB_NAME"] = "Database:Name",
            ["DB_USERNAME"] = "Database:Username",
            ["DB_PASSWORD"] = "Database:Password",
            ["JWT_SECRET"] = "Jwt:Secret",
            ["JWT_ISSUER"] = "Jwt:Issuer",
            ["JWT_AUDIENCE"] = "Jwt:Audience",
            ["JWT_EXPIRES_MINUTES"] = "Jwt:AccessTokenMinutes",
            ["REFRESH_TOKEN_EXPIRES_DAYS"] = "Jwt:RefreshTokenExpiresDays",
            ["ROOT_ADMIN_FULL_NAME"] = "RootAdmin:FullName",
            ["ROOT_ADMIN_EMAIL"] = "RootAdmin:Email",
            ["ROOT_ADMIN_PASSWORD"] = "RootAdmin:Password",
            ["TENANT_SCHEMA_PREFIX"] = "Tenant:SchemaPrefix",
            ["REDIS_CONNECTION"] = "Redis:Configuration",
            ["REDIS_BARCODE_LOOKUP_TTL_SECONDS"] = "Redis:BarcodeLookupTtlSeconds",
            ["AI_PROVIDER"] = "Ai:Provider",
            ["OPENAI_API_KEY"] = "OpenAi:ApiKey",
            ["OPENAI_MODEL"] = "OpenAi:Model",
            ["OPENAI_BASE_URL"] = "OpenAi:BaseUrl",
            ["OPENAI_USE_FAKE_CLIENT"] = "OpenAi:UseFakeClient",
            ["OLLAMA_BASE_URL"] = "Ollama:BaseUrl",
            ["OLLAMA_MODEL"] = "Ollama:Model",
            ["FRONTEND_URL"] = "Cors:FrontendUrl"
        };

    public static IConfigurationBuilder AddFriendlyEnvironmentVariables(
        this IConfigurationBuilder configurationBuilder)
    {
        var mappedValues = ConfigurationMappings
            .Select(mapping => new KeyValuePair<string, string?>(
                mapping.Value,
                Environment.GetEnvironmentVariable(mapping.Key)))
            .Where(mapping => !string.IsNullOrWhiteSpace(mapping.Value));

        return configurationBuilder.AddInMemoryCollection(mappedValues);
    }
}
