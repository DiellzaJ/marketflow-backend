using MarketFlow.Application.Common.Interfaces;
using MarketFlow.Application.Common.Models;
using MarketFlow.Application.Features.Categories.DTOs;
using MarketFlow.Application.Features.Inventory.DTOs;
using MarketFlow.Application.Features.Inventory.Services;
using MarketFlow.Application.Features.Products.DTOs;
using MarketFlow.Application.Features.Purchases.DTOs;
using MarketFlow.Application.Features.Sales.DTOs;

namespace MarketFlow.Api.Tests.Inventory;

public sealed class InventoryServiceTests
{
    [Fact]
    public async Task GetInventoryAsync_RejectsInvalidPage()
    {
        var tenantQueryService = new RecordingTenantQueryService();
        var service = new InventoryService(tenantQueryService, new FakeCurrentUserService());

        var result = await service.GetInventoryAsync(new InventoryListQuery { Page = 0 });

        Assert.False(result.Succeeded);
        Assert.Equal("Page must be greater than zero.", result.Message);
        Assert.False(tenantQueryService.GetInventoryWasCalled);
    }

    [Fact]
    public async Task GetInventoryAsync_RejectsUnsupportedSortField()
    {
        var tenantQueryService = new RecordingTenantQueryService();
        var service = new InventoryService(tenantQueryService, new FakeCurrentUserService());

        var result = await service.GetInventoryAsync(new InventoryListQuery { SortBy = "unsafe_sql" });

        Assert.False(result.Succeeded);
        Assert.Equal("Sort field is not supported.", result.Message);
        Assert.False(tenantQueryService.GetInventoryWasCalled);
    }

    [Fact]
    public async Task GetInventoryAsync_RejectsInvalidSortDirection()
    {
        var tenantQueryService = new RecordingTenantQueryService();
        var service = new InventoryService(tenantQueryService, new FakeCurrentUserService());

        var result = await service.GetInventoryAsync(new InventoryListQuery { SortDirection = "sideways" });

        Assert.False(result.Succeeded);
        Assert.Equal("Sort direction must be asc or desc.", result.Message);
        Assert.False(tenantQueryService.GetInventoryWasCalled);
    }

    [Fact]
    public async Task GetInventoryAsync_PassesFiltersPaginationAndSortToTenantQueryService()
    {
        var tenantQueryService = new RecordingTenantQueryService();
        var service = new InventoryService(tenantQueryService, new FakeCurrentUserService());

        var query = new InventoryListQuery
        {
            Search = "milk",
            ProductId = 10,
            Barcode = "123",
            CategoryId = 3,
            MarketId = 1,
            DepartmentId = 2,
            LowStockOnly = true,
            Page = 2,
            PageSize = 20,
            SortBy = " quantity ",
            SortDirection = " asc "
        };

        var result = await service.GetInventoryAsync(query);

        Assert.True(result.Succeeded);
        Assert.True(tenantQueryService.GetInventoryWasCalled);
        Assert.Same(query, tenantQueryService.LastInventoryListQuery);
        Assert.Equal("quantity", tenantQueryService.LastInventoryListQuery?.SortBy);
        Assert.Equal("asc", tenantQueryService.LastInventoryListQuery?.SortDirection);
        Assert.Equal(2, result.Data?.Page);
        Assert.Equal(20, result.Data?.PageSize);
        Assert.Equal(1, result.Data?.TotalCount);
    }

    [Fact]
    public async Task AdjustInventoryItemAsync_UpdatesQuantityAndRecordsAdjustment()
    {
        var tenantQueryService = new RecordingTenantQueryService
        {
            InventoryItem = new InventoryItemDto { Id = 7, Quantity = 5 }
        };
        var service = new InventoryService(tenantQueryService, new FakeCurrentUserService());

        var result = await service.AdjustInventoryItemAsync(
            7,
            new AdjustInventoryRequest
            {
                QuantityChange = 10,
                Reason = " ManualCorrection ",
                Note = " Initial stock count correction "
            });

        Assert.True(result.Succeeded);
        Assert.Equal("Inventory stock adjusted.", result.Message);
        Assert.Equal(15, result.Data?.Quantity);
        Assert.True(tenantQueryService.AdjustInventoryWasCalled);
        Assert.Equal(7, tenantQueryService.LastAdjustedInventoryId);
        Assert.Equal(10, tenantQueryService.LastAdjustmentRequest?.QuantityChange);
        Assert.Equal("ManualCorrection", tenantQueryService.LastAdjustmentRequest?.Reason);
        Assert.Equal("Initial stock count correction", tenantQueryService.LastAdjustmentRequest?.Note);
        Assert.Equal(1, tenantQueryService.LastAdjustmentUpdatedByUserId);
    }

    [Fact]
    public async Task AdjustInventoryItemAsync_RejectsMissingReason()
    {
        var tenantQueryService = new RecordingTenantQueryService
        {
            InventoryItem = new InventoryItemDto { Id = 7, Quantity = 5 }
        };
        var service = new InventoryService(tenantQueryService, new FakeCurrentUserService());

        var result = await service.AdjustInventoryItemAsync(
            7,
            new AdjustInventoryRequest { QuantityChange = 1, Reason = " " });

        Assert.False(result.Succeeded);
        Assert.Equal("Adjustment reason is required.", result.Message);
        Assert.False(tenantQueryService.AdjustInventoryWasCalled);
    }

    [Fact]
    public async Task AdjustInventoryItemAsync_RejectsReasonLongerThanMovementColumn()
    {
        var tenantQueryService = new RecordingTenantQueryService
        {
            InventoryItem = new InventoryItemDto { Id = 7, Quantity = 5 }
        };
        var service = new InventoryService(tenantQueryService, new FakeCurrentUserService());

        var result = await service.AdjustInventoryItemAsync(
            7,
            new AdjustInventoryRequest { QuantityChange = 1, Reason = new string('x', 101) });

        Assert.False(result.Succeeded);
        Assert.Equal("Adjustment reason cannot exceed 100 characters.", result.Message);
        Assert.False(tenantQueryService.AdjustInventoryWasCalled);
    }

    [Fact]
    public async Task AdjustInventoryItemAsync_RejectsNegativeResultingStock()
    {
        var tenantQueryService = new RecordingTenantQueryService
        {
            InventoryItem = new InventoryItemDto { Id = 7, Quantity = 5 }
        };
        var service = new InventoryService(tenantQueryService, new FakeCurrentUserService());

        var result = await service.AdjustInventoryItemAsync(
            7,
            new AdjustInventoryRequest { QuantityChange = -6, Reason = "ManualCorrection" });

        Assert.False(result.Succeeded);
        Assert.Equal("Stock cannot become negative.", result.Message);
        Assert.False(tenantQueryService.AdjustInventoryWasCalled);
    }

    [Fact]
    public async Task AdjustInventoryItemAsync_ReturnsNotFoundWhenScopedUpdateMissesAfterInitialRead()
    {
        var tenantQueryService = new RecordingTenantQueryService
        {
            InventoryItem = new InventoryItemDto { Id = 7, Quantity = 5 },
            ReturnNullFromAdjustment = true,
            ReturnNullFromSecondInventoryRead = true
        };
        var service = new InventoryService(tenantQueryService, new FakeCurrentUserService());

        var result = await service.AdjustInventoryItemAsync(
            7,
            new AdjustInventoryRequest { QuantityChange = 1, Reason = "ManualCorrection" });

        Assert.False(result.Succeeded);
        Assert.Equal(ServiceResultFailureType.NotFound, result.FailureType);
        Assert.Equal("Inventory item was not found.", result.Message);
        Assert.True(tenantQueryService.AdjustInventoryWasCalled);
    }

    [Fact]
    public async Task AdjustInventoryItemAsync_ReturnsNegativeStockFailureWhenConcurrentAdjustmentUnderflows()
    {
        var tenantQueryService = new RecordingTenantQueryService
        {
            InventoryItem = new InventoryItemDto { Id = 7, Quantity = 5 },
            ReturnNullFromAdjustment = true
        };
        var service = new InventoryService(tenantQueryService, new FakeCurrentUserService());

        var result = await service.AdjustInventoryItemAsync(
            7,
            new AdjustInventoryRequest { QuantityChange = -4, Reason = "ManualCorrection" });

        Assert.False(result.Succeeded);
        Assert.Equal(ServiceResultFailureType.Validation, result.FailureType);
        Assert.Equal("Stock cannot become negative.", result.Message);
        Assert.True(tenantQueryService.AdjustInventoryWasCalled);
    }

    private sealed class RecordingTenantQueryService : ITenantQueryService
    {
        public bool GetInventoryWasCalled { get; private set; }

        public bool AdjustInventoryWasCalled { get; private set; }

        public InventoryListQuery? LastInventoryListQuery { get; private set; }

        public int LastAdjustedInventoryId { get; private set; }

        public AdjustInventoryRequest? LastAdjustmentRequest { get; private set; }

        public int? LastAdjustmentUpdatedByUserId { get; private set; }

        public InventoryItemDto? InventoryItem { get; init; }

        public bool ReturnNullFromAdjustment { get; init; }

        public bool ReturnNullFromSecondInventoryRead { get; init; }

        private int _inventoryReadCount;

        public Task<PagedResult<InventoryItemDto>> GetInventoryAsync(
            InventoryListQuery query,
            CancellationToken cancellationToken = default)
        {
            GetInventoryWasCalled = true;
            LastInventoryListQuery = query;

            return Task.FromResult(new PagedResult<InventoryItemDto>
            {
                Items = [new InventoryItemDto { Id = 1, ProductId = 10, ProductName = "Milk" }],
                Page = query.Page,
                PageSize = query.PageSize,
                TotalCount = 1,
                TotalPages = 1
            });
        }

        public Task<PagedResult<ProductDto>> GetProductsAsync(ProductListQuery query, CancellationToken cancellationToken = default) => throw new NotSupportedException();
        public Task<ProductDto?> GetProductAsync(int id, bool includeInactive = false, CancellationToken cancellationToken = default) => throw new NotSupportedException();
        public Task<bool> CategoryExistsAsync(int categoryId, CancellationToken cancellationToken = default) => throw new NotSupportedException();
        public Task<bool> ProductBarcodeExistsAsync(string barcode, int? excludedProductId = null, CancellationToken cancellationToken = default) => throw new NotSupportedException();
        public Task<ProductDto> CreateProductAsync(CreateProductRequest request, CancellationToken cancellationToken = default) => throw new NotSupportedException();
        public Task<ProductDto?> UpdateProductAsync(int id, UpdateProductRequest request, CancellationToken cancellationToken = default) => throw new NotSupportedException();
        public Task<ProductDto?> PatchProductAsync(int id, PatchProductRequest request, CancellationToken cancellationToken = default) => throw new NotSupportedException();
        public Task<ProductDto?> SetProductActiveStateAsync(int id, bool isActive, CancellationToken cancellationToken = default) => throw new NotSupportedException();
        public Task<IReadOnlyCollection<CategoryDto>> GetCategoriesAsync(CancellationToken cancellationToken = default) => throw new NotSupportedException();
        public Task<InventoryItemDto?> GetInventoryItemAsync(int id, CancellationToken cancellationToken = default)
        {
            _inventoryReadCount++;

            return Task.FromResult<InventoryItemDto?>(
                ReturnNullFromSecondInventoryRead && _inventoryReadCount > 1
                    ? null
                    : InventoryItem);
        }
        public Task<PagedResult<InventoryMovementDto>> GetInventoryMovementsAsync(InventoryMovementListQuery query, CancellationToken cancellationToken = default) => throw new NotSupportedException();
        public Task<PagedResult<InventoryMovementDto>> GetInventoryMovementsForInventoryAsync(int inventoryId, InventoryMovementListQuery query, CancellationToken cancellationToken = default) => throw new NotSupportedException();
        public Task<InventoryItemDto?> CreateInventoryItemAsync(CreateInventoryItemRequest request, int? updatedByUserId, CancellationToken cancellationToken = default) => throw new NotSupportedException();
        public Task<InventoryItemDto?> UpdateInventoryItemAsync(int id, UpdateInventoryItemRequest request, int? updatedByUserId, CancellationToken cancellationToken = default) => throw new NotSupportedException();
        public Task<InventoryItemDto?> PatchInventoryItemAsync(int id, PatchInventoryItemRequest request, int? updatedByUserId, CancellationToken cancellationToken = default) => throw new NotSupportedException();
        public Task<InventoryItemDto?> AdjustInventoryItemAsync(int id, AdjustInventoryRequest request, int? updatedByUserId, CancellationToken cancellationToken = default)
        {
            AdjustInventoryWasCalled = true;
            LastAdjustedInventoryId = id;
            LastAdjustmentRequest = request;
            LastAdjustmentUpdatedByUserId = updatedByUserId;

            if (ReturnNullFromAdjustment)
            {
                return Task.FromResult<InventoryItemDto?>(null);
            }

            return Task.FromResult<InventoryItemDto?>(
                InventoryItem is null
                    ? null
                    : new InventoryItemDto { Id = InventoryItem.Id, Quantity = InventoryItem.Quantity + request.QuantityChange });
        }

        public Task<bool> TransferInventoryAsync(TransferInventoryRequest request, int? updatedByUserId, CancellationToken cancellationToken = default) => throw new NotSupportedException();
        public Task<bool> DeleteInventoryItemAsync(int id, CancellationToken cancellationToken = default) => throw new NotSupportedException();
        public Task<IReadOnlyCollection<SaleDto>> GetSalesAsync(CancellationToken cancellationToken = default) => throw new NotSupportedException();
        public Task<SaleDto?> CreateSaleAsync(CreateSaleRequest request, int createdByUserId, CancellationToken cancellationToken = default) => throw new NotSupportedException();
        public Task<SaleDto?> UpdateSaleAsync(int id, UpdateSaleRequest request, CancellationToken cancellationToken = default) => throw new NotSupportedException();
        public Task<SaleDto?> PatchSaleAsync(int id, PatchSaleRequest request, CancellationToken cancellationToken = default) => throw new NotSupportedException();
        public Task<bool> DeleteSaleAsync(int id, CancellationToken cancellationToken = default) => throw new NotSupportedException();
        public Task<IReadOnlyCollection<PurchaseDto>> GetPurchasesAsync(CancellationToken cancellationToken = default) => throw new NotSupportedException();
        public Task<PurchaseDto> CreatePurchaseAsync(CreatePurchaseRequest request, int createdByUserId, CancellationToken cancellationToken = default) => throw new NotSupportedException();
        public Task<PurchaseDto?> UpdatePurchaseAsync(int id, UpdatePurchaseRequest request, int? updatedByUserId, CancellationToken cancellationToken = default) => throw new NotSupportedException();
        public Task<PurchaseDto?> PatchPurchaseAsync(int id, PatchPurchaseRequest request, int? updatedByUserId, CancellationToken cancellationToken = default) => throw new NotSupportedException();
        public Task<bool> DeletePurchaseAsync(int id, CancellationToken cancellationToken = default) => throw new NotSupportedException();
    }

    private sealed class FakeCurrentUserService : ICurrentUserService
    {
        public int? UserId => 1;
        public int? CompanyId => 1;
        public string? Email => "inventory@example.test";
        public string? Role => "CompanyAdmin";
        public string? SchemaName => "tenant_test";
    }
}
