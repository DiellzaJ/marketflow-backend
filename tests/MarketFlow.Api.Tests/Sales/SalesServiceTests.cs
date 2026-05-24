using MarketFlow.Application.Common.Exceptions;
using MarketFlow.Application.Common.Interfaces;
using MarketFlow.Application.Common.Models;
using MarketFlow.Application.Features.Categories.DTOs;
using MarketFlow.Application.Features.Inventory.DTOs;
using MarketFlow.Application.Features.Products.DTOs;
using MarketFlow.Application.Features.Purchases.DTOs;
using MarketFlow.Application.Features.Sales.DTOs;
using MarketFlow.Application.Features.Sales.Services;

namespace MarketFlow.Api.Tests.Sales;

public sealed class SalesServiceTests
{
    [Fact]
    public async Task GetSalesAsync_WhenSaleReferenceRepairFails_ReturnsFailure()
    {
        var service = CreateService(new StubTenantQueryService { ThrowSaleReferenceRepairException = true });

        var result = await service.GetSalesAsync();

        Assert.False(result.Succeeded);
        Assert.Equal(ServiceResultFailureType.Validation, result.FailureType);
        Assert.Equal(
            "Sales could not be loaded because the tenant sales schema could not be repaired. See server logs for database diagnostics.",
            result.Message);
    }

    [Fact]
    public async Task CreateSaleAsync_WhenSaleReferenceRepairFails_ReturnsFailure()
    {
        var service = CreateService(new StubTenantQueryService { ThrowSaleReferenceRepairException = true });

        var result = await service.CreateSaleAsync(new CreateSaleRequest
        {
            MarketId = 1,
            PaymentMethod = "Cash",
            Items =
            [
                new CreateSaleItemRequest
                {
                    ProductId = 1,
                    Quantity = 1,
                    UnitPrice = 5
                }
            ]
        });

        Assert.False(result.Succeeded);
        Assert.Equal(ServiceResultFailureType.Validation, result.FailureType);
        Assert.Equal(
            "Sale creation failed because the tenant sales schema could not be repaired. See server logs for database diagnostics.",
            result.Message);
    }

    [Fact]
    public async Task GetSaleDetailsAsync_WhenSaleReferenceRepairFails_ReturnsFailure()
    {
        var service = CreateService(new StubTenantQueryService { ThrowSaleReferenceRepairException = true });

        var result = await service.GetSaleDetailsAsync(10);

        Assert.False(result.Succeeded);
        Assert.Equal(ServiceResultFailureType.Validation, result.FailureType);
        Assert.Equal(
            "Sale details could not be loaded because the tenant sales schema could not be repaired. See server logs for database diagnostics.",
            result.Message);
    }

    [Fact]
    public async Task UpdateSaleAsync_WhenSaleReferenceRepairFails_ReturnsFailure()
    {
        var service = CreateService(new StubTenantQueryService { ThrowSaleReferenceRepairException = true });

        var result = await service.UpdateSaleAsync(10, new UpdateSaleRequest
        {
            MarketId = 1,
            SaleDate = DateOnly.FromDateTime(DateTime.UtcNow),
            PaymentMethod = "Cash"
        });

        Assert.False(result.Succeeded);
        Assert.Equal(ServiceResultFailureType.Validation, result.FailureType);
        Assert.Equal(
            "Sale update failed because the tenant sales schema could not be repaired. See server logs for database diagnostics.",
            result.Message);
    }

    [Fact]
    public async Task PatchSaleAsync_WhenSaleReferenceRepairFails_ReturnsFailure()
    {
        var service = CreateService(new StubTenantQueryService { ThrowSaleReferenceRepairException = true });

        var result = await service.PatchSaleAsync(10, new PatchSaleRequest { PaymentMethod = "Cash" });

        Assert.False(result.Succeeded);
        Assert.Equal(ServiceResultFailureType.Validation, result.FailureType);
        Assert.Equal(
            "Sale patch failed because the tenant sales schema could not be repaired. See server logs for database diagnostics.",
            result.Message);
    }

    [Fact]
    public async Task GetSaleDetailsAsync_WhenSaleIsMissing_ReturnsNotFound()
    {
        var service = CreateService(new StubTenantQueryService { SaleDetailsResult = null });

        var result = await service.GetSaleDetailsAsync(10);

        Assert.False(result.Succeeded);
        Assert.Equal(ServiceResultFailureType.NotFound, result.FailureType);
        Assert.Equal("Sale was not found.", result.Message);
    }

    [Fact]
    public async Task UpdateSaleAsync_WhenSaleIsMissing_ReturnsNotFound()
    {
        var service = CreateService(new StubTenantQueryService { SaleResult = null });

        var result = await service.UpdateSaleAsync(10, new UpdateSaleRequest
        {
            MarketId = 1,
            SaleDate = DateOnly.FromDateTime(DateTime.UtcNow),
            PaymentMethod = "Cash"
        });

        Assert.False(result.Succeeded);
        Assert.Equal(ServiceResultFailureType.NotFound, result.FailureType);
        Assert.Equal("Sale was not found.", result.Message);
    }

    [Fact]
    public async Task PatchSaleAsync_WhenSaleIsMissing_ReturnsNotFound()
    {
        var service = CreateService(new StubTenantQueryService { SaleResult = null });

        var result = await service.PatchSaleAsync(10, new PatchSaleRequest { PaymentMethod = "Cash" });

        Assert.False(result.Succeeded);
        Assert.Equal(ServiceResultFailureType.NotFound, result.FailureType);
        Assert.Equal("Sale was not found.", result.Message);
    }

    [Fact]
    public async Task DeleteSaleAsync_WhenSaleIsMissing_ReturnsNotFound()
    {
        var service = CreateService(new StubTenantQueryService { SaleWasDeleted = false });

        var result = await service.DeleteSaleAsync(10);

        Assert.False(result.Succeeded);
        Assert.Equal(ServiceResultFailureType.NotFound, result.FailureType);
        Assert.Equal("Sale was not found.", result.Message);
    }

    private static SalesService CreateService(ITenantQueryService tenantQueryService)
    {
        return new SalesService(tenantQueryService, new StubCurrentUserService());
    }

    private sealed class StubTenantQueryService : ITenantQueryService
    {
        public SaleDetailsResponse? SaleDetailsResult { get; init; }
        public SaleDto? SaleResult { get; init; }
        public bool SaleWasDeleted { get; init; }
        public bool ThrowSaleReferenceRepairException { get; init; }

        public Task<IReadOnlyCollection<SaleDto>> GetSalesAsync(
            CancellationToken cancellationToken = default)
        {
            ThrowIfSaleReferenceRepairFails();
            return Task.FromResult<IReadOnlyCollection<SaleDto>>([]);
        }

        public Task<SaleDetailsResponse?> GetSaleDetailsAsync(
            int id,
            CancellationToken cancellationToken = default)
        {
            ThrowIfSaleReferenceRepairFails();
            return Task.FromResult(SaleDetailsResult);
        }

        public Task<SaleDto?> CreateSaleAsync(
            CreateSaleRequest request,
            int createdByUserId,
            CancellationToken cancellationToken = default)
        {
            ThrowIfSaleReferenceRepairFails();
            return Task.FromResult(SaleResult);
        }

        public Task<SaleDto?> UpdateSaleAsync(
            int id,
            UpdateSaleRequest request,
            CancellationToken cancellationToken = default)
        {
            ThrowIfSaleReferenceRepairFails();
            return Task.FromResult(SaleResult);
        }

        public Task<SaleDto?> PatchSaleAsync(
            int id,
            PatchSaleRequest request,
            CancellationToken cancellationToken = default)
        {
            ThrowIfSaleReferenceRepairFails();
            return Task.FromResult(SaleResult);
        }

        public Task<bool> DeleteSaleAsync(int id, CancellationToken cancellationToken = default) =>
            Task.FromResult(SaleWasDeleted);

        private void ThrowIfSaleReferenceRepairFails()
        {
            if (ThrowSaleReferenceRepairException)
            {
                throw new SaleReferenceNumberRepairException(
                    "tenant_test",
                    "Tenant sales schema repair failed while creating or updating sale reference number infrastructure.");
            }
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
        public Task<PagedResult<InventoryItemDto>> GetInventoryAsync(InventoryListQuery query, CancellationToken cancellationToken = default) => throw new NotSupportedException();
        public Task<IReadOnlyCollection<InventoryItemDto>> GetLowStockInventoryAsync(CancellationToken cancellationToken = default) => throw new NotSupportedException();
        public Task<IReadOnlyCollection<PosProductLookupItemDto>?> GetPosProductsAsync(PosProductLookupQuery query, CancellationToken cancellationToken = default) => throw new NotSupportedException();
        public Task<InventoryItemDto?> GetInventoryItemAsync(int id, CancellationToken cancellationToken = default) => throw new NotSupportedException();
        public Task<PagedResult<InventoryMovementDto>> GetInventoryMovementsAsync(InventoryMovementListQuery query, CancellationToken cancellationToken = default) => throw new NotSupportedException();
        public Task<PagedResult<InventoryMovementDto>> GetInventoryMovementsForInventoryAsync(int inventoryId, InventoryMovementListQuery query, CancellationToken cancellationToken = default) => throw new NotSupportedException();
        public Task<InventoryItemDto?> CreateInventoryItemAsync(CreateInventoryItemRequest request, int? updatedByUserId, CancellationToken cancellationToken = default) => throw new NotSupportedException();
        public Task<InventoryItemDto?> UpdateInventoryItemAsync(int id, UpdateInventoryItemRequest request, int? updatedByUserId, CancellationToken cancellationToken = default) => throw new NotSupportedException();
        public Task<InventoryItemDto?> PatchInventoryItemAsync(int id, PatchInventoryItemRequest request, int? updatedByUserId, CancellationToken cancellationToken = default) => throw new NotSupportedException();
        public Task<InventoryItemDto?> AdjustInventoryItemAsync(int id, AdjustInventoryRequest request, int? updatedByUserId, CancellationToken cancellationToken = default) => throw new NotSupportedException();
        public Task<bool> TransferInventoryAsync(TransferInventoryRequest request, int? updatedByUserId, CancellationToken cancellationToken = default) => throw new NotSupportedException();
        public Task<bool> DeleteInventoryItemAsync(int id, CancellationToken cancellationToken = default) => throw new NotSupportedException();
        public Task<IReadOnlyCollection<PurchaseDto>> GetPurchasesAsync(CancellationToken cancellationToken = default) => throw new NotSupportedException();
        public Task<PurchaseDto?> CreatePurchaseAsync(CreatePurchaseRequest request, int createdByUserId, CancellationToken cancellationToken = default) => throw new NotSupportedException();
        public Task<PurchaseDto?> UpdatePurchaseAsync(int id, UpdatePurchaseRequest request, int? updatedByUserId, CancellationToken cancellationToken = default) => throw new NotSupportedException();
        public Task<PurchaseDto?> PatchPurchaseAsync(int id, PatchPurchaseRequest request, int? updatedByUserId, CancellationToken cancellationToken = default) => throw new NotSupportedException();
        public Task<bool> DeletePurchaseAsync(int id, CancellationToken cancellationToken = default) => throw new NotSupportedException();
    }

    private sealed class StubCurrentUserService : ICurrentUserService
    {
        public int? UserId => 42;
        public int? CompanyId => 1;
        public string? Email => "sales-service@example.test";
        public string? Role => "CompanyAdmin";
        public string? SchemaName => "tenant_test";
    }
}
