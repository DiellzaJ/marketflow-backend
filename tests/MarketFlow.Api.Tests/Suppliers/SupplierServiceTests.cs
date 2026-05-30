using MarketFlow.Application.Common.Interfaces;
using MarketFlow.Application.Common.Models;
using MarketFlow.Application.Features.AI.Interfaces;
using MarketFlow.Application.Features.Suppliers.DTOs;
using MarketFlow.Application.Features.Suppliers.Interfaces;
using MarketFlow.Application.Features.Suppliers.Services;

namespace MarketFlow.Api.Tests.Suppliers;

public sealed class SupplierServiceTests
{
    [Fact]
    public async Task GetSuppliersAsync_DefaultsToActiveSuppliersOnly()
    {
        var expected = new List<SupplierDto>
        {
            new() { Id = 2, Name = "Fresh Supplier", IsActive = true }
        };
        var store = new RecordingSupplierStore(expected);
        var service = new SupplierService(store);

        var result = await service.GetSuppliersAsync();

        Assert.True(result.Succeeded);
        Assert.Same(expected, result.Data);
        Assert.False(store.LastIncludeInactive);
    }

    [Fact]
    public async Task CreateSupplierAsync_RejectsMissingName()
    {
        var store = new RecordingSupplierStore();
        var service = new SupplierService(store);

        var result = await service.CreateSupplierAsync(new CreateSupplierRequest
        {
            Name = " "
        });

        Assert.False(result.Succeeded);
        Assert.Equal("Supplier name is required.", result.Message);
        Assert.False(store.CreateSupplierWasCalled);
    }

    [Fact]
    public async Task CreateSupplierAsync_RejectsInvalidEmail()
    {
        var store = new RecordingSupplierStore();
        var service = new SupplierService(store);

        var result = await service.CreateSupplierAsync(new CreateSupplierRequest
        {
            Name = "Fresh Supplier",
            Email = "not-an-email"
        });

        Assert.False(result.Succeeded);
        Assert.Equal("Supplier email must be a valid email address.", result.Message);
        Assert.False(store.CreateSupplierWasCalled);
    }

    [Fact]
    public async Task CreateSupplierAsync_TrimsPayload()
    {
        var store = new RecordingSupplierStore();
        var service = new SupplierService(store);

        var result = await service.CreateSupplierAsync(new CreateSupplierRequest
        {
            Name = " Fresh Supplier ",
            Phone = " 044 100 200 ",
            Email = " supplier@example.com ",
            Address = " Main Street "
        });

        Assert.True(result.Succeeded);
        Assert.Equal("Supplier created.", result.Message);
        Assert.Equal("Fresh Supplier", result.Data?.Name);
        Assert.Equal("044 100 200", result.Data?.Phone);
        Assert.Equal("supplier@example.com", result.Data?.Email);
        Assert.Equal("Main Street", result.Data?.Address);
    }

    [Fact]
    public async Task UpdateSupplierAsync_RejectsActiveStateChange()
    {
        var store = new RecordingSupplierStore();
        var service = new SupplierService(store);

        var result = await service.UpdateSupplierAsync(6, new UpdateSupplierRequest
        {
            Name = "Fresh Supplier",
            IsActive = false
        });

        Assert.False(result.Succeeded);
        Assert.Equal("Use the supplier status or deactivate endpoint to change active state.", result.Message);
        Assert.False(store.UpdateSupplierWasCalled);
    }

    [Fact]
    public async Task SetSupplierActiveStateAsync_WhenActive_DeactivatesSupplier()
    {
        var store = new RecordingSupplierStore();
        var service = new SupplierService(store);

        var result = await service.SetSupplierActiveStateAsync(6, isActive: false);

        Assert.True(result.Succeeded);
        Assert.Equal("Supplier deactivated.", result.Message);
        Assert.True(store.SetSupplierActiveStateWasCalled);
        Assert.False(store.LastActiveState);
        Assert.False(result.Data?.IsActive);
    }

    [Fact]
    public async Task UpdateSupplierAsync_WhenSupplierChanges_InvalidatesTenantAiCache()
    {
        var store = new RecordingSupplierStore();
        var cache = new RecordingAiResultCache();
        var service = new SupplierService(store, cache, new StubCurrentUserService());

        var result = await service.UpdateSupplierAsync(6, new UpdateSupplierRequest
        {
            Name = "Renamed Supplier"
        });

        Assert.True(result.Succeeded);
        Assert.Equal(1, cache.InvalidatedCompanyId);
    }

    [Fact]
    public async Task SetSupplierActiveStateAsync_WhenSupplierChanges_InvalidatesTenantAiCache()
    {
        var store = new RecordingSupplierStore();
        var cache = new RecordingAiResultCache();
        var service = new SupplierService(store, cache, new StubCurrentUserService());

        var result = await service.SetSupplierActiveStateAsync(6, isActive: false);

        Assert.True(result.Succeeded);
        Assert.Equal(1, cache.InvalidatedCompanyId);
    }

    [Fact]
    public async Task SetSupplierActiveStateAsync_WhenAlreadyInactive_ReturnsConflict()
    {
        var store = new RecordingSupplierStore
        {
            CurrentSupplier = new SupplierDto
            {
                Id = 6,
                Name = "Fresh Supplier",
                IsActive = false
            }
        };
        var service = new SupplierService(store);

        var result = await service.SetSupplierActiveStateAsync(6, isActive: false);

        Assert.False(result.Succeeded);
        Assert.Equal(ServiceResultFailureType.Conflict, result.FailureType);
        Assert.Equal("Supplier is already inactive.", result.Message);
        Assert.False(store.SetSupplierActiveStateWasCalled);
    }

    private sealed class RecordingSupplierStore : ISupplierStore
    {
        private readonly IReadOnlyCollection<SupplierDto> _suppliers;

        public RecordingSupplierStore()
            : this([])
        {
        }

        public RecordingSupplierStore(IReadOnlyCollection<SupplierDto> suppliers)
        {
            _suppliers = suppliers;
        }

        public bool LastIncludeInactive { get; private set; }

        public bool CreateSupplierWasCalled { get; private set; }

        public bool UpdateSupplierWasCalled { get; private set; }

        public bool SetSupplierActiveStateWasCalled { get; private set; }

        public bool LastActiveState { get; private set; }

        public SupplierDto? CurrentSupplier { get; set; } = new()
        {
            Id = 6,
            Name = "Fresh Supplier",
            Phone = "044 100 200",
            Email = "supplier@example.com",
            Address = "Main Street",
            IsActive = true
        };

        public Task<IReadOnlyCollection<SupplierDto>> GetSuppliersAsync(
            bool includeInactive = false,
            CancellationToken cancellationToken = default)
        {
            LastIncludeInactive = includeInactive;
            return Task.FromResult(_suppliers);
        }

        public Task<SupplierDto?> GetSupplierAsync(
            int id,
            bool includeInactive = false,
            CancellationToken cancellationToken = default)
        {
            if (CurrentSupplier?.Id != id)
            {
                return Task.FromResult<SupplierDto?>(null);
            }

            if (!includeInactive && !CurrentSupplier.IsActive)
            {
                return Task.FromResult<SupplierDto?>(null);
            }

            return Task.FromResult<SupplierDto?>(CurrentSupplier);
        }

        public Task<SupplierDto> CreateSupplierAsync(
            CreateSupplierRequest request,
            CancellationToken cancellationToken = default)
        {
            CreateSupplierWasCalled = true;

            return Task.FromResult(new SupplierDto
            {
                Id = 6,
                Name = request.Name,
                Phone = request.Phone,
                Email = request.Email,
                Address = request.Address,
                IsActive = true
            });
        }

        public Task<SupplierDto?> UpdateSupplierAsync(
            int id,
            UpdateSupplierRequest request,
            CancellationToken cancellationToken = default)
        {
            UpdateSupplierWasCalled = true;

            if (CurrentSupplier is null || CurrentSupplier.Id != id)
            {
                return Task.FromResult<SupplierDto?>(null);
            }

            CurrentSupplier.Name = request.Name;
            CurrentSupplier.Phone = request.Phone;
            CurrentSupplier.Email = request.Email;
            CurrentSupplier.Address = request.Address;

            return Task.FromResult<SupplierDto?>(CurrentSupplier);
        }

        public Task<SupplierDto?> SetSupplierActiveStateAsync(
            int id,
            bool isActive,
            CancellationToken cancellationToken = default)
        {
            SetSupplierActiveStateWasCalled = true;
            LastActiveState = isActive;

            if (CurrentSupplier is null || CurrentSupplier.Id != id)
            {
                return Task.FromResult<SupplierDto?>(null);
            }

            CurrentSupplier.IsActive = isActive;
            return Task.FromResult<SupplierDto?>(CurrentSupplier);
        }
    }

    private sealed class RecordingAiResultCache : IAiResultCache
    {
        public int? InvalidatedCompanyId { get; private set; }

        public Task<T?> GetAsync<T>(
            string key,
            CancellationToken cancellationToken = default) =>
            Task.FromResult<T?>(default);

        public Task SetAsync<T>(
            string key,
            T value,
            TimeSpan? expiration = null,
            CancellationToken cancellationToken = default) =>
            Task.CompletedTask;

        public Task InvalidateCompanyAsync(
            int companyId,
            CancellationToken cancellationToken = default)
        {
            InvalidatedCompanyId = companyId;
            return Task.CompletedTask;
        }
    }

    private sealed class StubCurrentUserService : ICurrentUserService
    {
        public int? UserId => 1;

        public int? CompanyId => 1;

        public string? Email => "supplier-cache@example.test";

        public string? Role => "CompanyAdmin";

        public string? SchemaName => "tenant_test";
    }
}
