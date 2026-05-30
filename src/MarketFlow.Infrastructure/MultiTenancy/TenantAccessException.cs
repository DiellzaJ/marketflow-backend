namespace MarketFlow.Infrastructure.MultiTenancy;

public sealed class TenantAccessException : Exception
{
    public TenantAccessException()
        : base("Tenant access is forbidden.")
    {
    }
}
