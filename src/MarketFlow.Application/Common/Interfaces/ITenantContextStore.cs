namespace MarketFlow.Application.Common.Interfaces;

public interface ITenantContextStore
{
    Task<TenantContext?> GetTenantContextAsync(
        int userId,
        CancellationToken cancellationToken = default);
}
