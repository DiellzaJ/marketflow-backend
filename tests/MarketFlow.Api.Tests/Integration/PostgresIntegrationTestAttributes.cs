using MarketFlow.Api.Configuration;

namespace MarketFlow.Api.Tests.Integration;

public sealed class PostgresIntegrationFactAttribute : FactAttribute
{
    public PostgresIntegrationFactAttribute()
    {
        DotEnv.Load();

        if (!TenantIntegrationTestDatabase.HasConfiguredConnectionString)
        {
            Skip = TenantIntegrationTestDatabase.MissingConnectionStringSkipReason;
        }
    }
}

public sealed class PostgresIntegrationTheoryAttribute : TheoryAttribute
{
    public PostgresIntegrationTheoryAttribute()
    {
        DotEnv.Load();

        if (!TenantIntegrationTestDatabase.HasConfiguredConnectionString)
        {
            Skip = TenantIntegrationTestDatabase.MissingConnectionStringSkipReason;
        }
    }
}
