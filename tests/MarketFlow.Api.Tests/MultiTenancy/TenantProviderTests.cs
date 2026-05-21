using MarketFlow.Application.Common.Interfaces;
using MarketFlow.Infrastructure.MultiTenancy;

namespace MarketFlow.Api.Tests.MultiTenancy;

public sealed class TenantProviderTests
{
    [Fact]
    public async Task GetCurrentSchemaNameAsync_UsesBackendTenantContextInsteadOfClaimSchema()
    {
        var store = new FakeTenantContextStore
        {
            TenantContext = new TenantContext("tenant_from_database", true, true)
        };
        var provider = new TenantProvider(
            new FakeCurrentUserService
            {
                UserId = 10,
                CompanyId = 25,
                SchemaName = "stale_token_schema"
            },
            store);

        var schemaName = await provider.GetCurrentSchemaNameAsync();

        Assert.Equal("tenant_from_database", schemaName);
        Assert.Equal(10, store.RequestedUserId);
    }

    [Fact]
    public async Task GetCurrentSchemaNameAsync_WhenUserIdIsMissing_FailsAsUnauthorized()
    {
        var provider = new TenantProvider(
            new FakeCurrentUserService(),
            new FakeTenantContextStore());

        await Assert.ThrowsAsync<UnauthorizedAccessException>(() =>
            provider.GetCurrentSchemaNameAsync());
    }

    [Theory]
    [InlineData(null, true, true)]
    [InlineData("tenant_valid", false, true)]
    [InlineData("tenant_valid", true, false)]
    [InlineData("", true, true)]
    [InlineData("tenant-name", true, true)]
    [InlineData("tenant.name", true, true)]
    [InlineData("tenant name", true, true)]
    [InlineData("tenant;drop_schema", true, true)]
    public async Task GetCurrentSchemaNameAsync_WhenTenantContextIsUnsafe_FailsAsForbidden(
        string? schemaName,
        bool userIsActive,
        bool companyIsActive)
    {
        var provider = new TenantProvider(
            new FakeCurrentUserService { UserId = 10 },
            new FakeTenantContextStore
            {
                TenantContext = schemaName is null
                    ? null
                    : new TenantContext(schemaName, userIsActive, companyIsActive)
            });

        await Assert.ThrowsAsync<TenantAccessException>(() =>
            provider.GetCurrentSchemaNameAsync());
    }

    [Fact]
    public async Task GetCurrentSchemaNameAsync_CachesResolvedSchemaForRequestScope()
    {
        var store = new FakeTenantContextStore
        {
            TenantContext = new TenantContext("tenant_cached", true, true)
        };
        var provider = new TenantProvider(
            new FakeCurrentUserService { UserId = 10 },
            store);

        var firstSchemaName = await provider.GetCurrentSchemaNameAsync();
        var secondSchemaName = await provider.GetCurrentSchemaNameAsync();

        Assert.Equal("tenant_cached", firstSchemaName);
        Assert.Equal(firstSchemaName, secondSchemaName);
        Assert.Equal(1, store.CallCount);
    }

    [Fact]
    public async Task GetCurrentSchemaNameAsync_WhenCalledConcurrently_ResolvesTenantContextOnce()
    {
        var tenantContextCompletion = new TaskCompletionSource<TenantContext?>(
            TaskCreationOptions.RunContinuationsAsynchronously);
        var store = new FakeTenantContextStore
        {
            TenantContextCompletion = tenantContextCompletion
        };
        var provider = new TenantProvider(
            new FakeCurrentUserService { UserId = 10 },
            store);

        var firstResolution = provider.GetCurrentSchemaNameAsync();
        await Task.Yield();
        var secondResolution = provider.GetCurrentSchemaNameAsync();

        Assert.Equal(1, store.CallCount);

        tenantContextCompletion.SetResult(new TenantContext("tenant_concurrent", true, true));

        var schemaNames = await Task.WhenAll(firstResolution, secondResolution);

        Assert.All(schemaNames, schemaName => Assert.Equal("tenant_concurrent", schemaName));
        Assert.Equal(1, store.CallCount);
    }

    private sealed class FakeTenantContextStore : ITenantContextStore
    {
        public TenantContext? TenantContext { get; init; }

        public TaskCompletionSource<TenantContext?>? TenantContextCompletion { get; init; }

        public int? RequestedUserId { get; private set; }

        public int CallCount { get; private set; }

        public Task<TenantContext?> GetTenantContextAsync(
            int userId,
            CancellationToken cancellationToken = default)
        {
            RequestedUserId = userId;
            CallCount++;

            if (TenantContextCompletion is not null)
            {
                return TenantContextCompletion.Task;
            }

            return Task.FromResult(TenantContext);
        }
    }

    private sealed class FakeCurrentUserService : ICurrentUserService
    {
        public int? UserId { get; init; }

        public int? CompanyId { get; init; }

        public string? Email { get; init; }

        public string? Role { get; init; }

        public string? SchemaName { get; init; }
    }
}
