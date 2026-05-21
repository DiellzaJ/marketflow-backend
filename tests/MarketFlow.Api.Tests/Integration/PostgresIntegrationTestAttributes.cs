namespace MarketFlow.Api.Tests.Integration;

public sealed class PostgresIntegrationFactAttribute : FactAttribute
{
    public PostgresIntegrationFactAttribute()
    {
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
        if (!TenantIntegrationTestDatabase.HasConfiguredConnectionString)
        {
            Skip = TenantIntegrationTestDatabase.MissingConnectionStringSkipReason;
        }
    }
}
