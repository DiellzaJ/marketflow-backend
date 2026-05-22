using MarketFlow.Application.Common.Interfaces;
using MarketFlow.Application.Common.Models;
using MarketFlow.Application.Features.Categories.DTOs;
using MarketFlow.Application.Features.Inventory.DTOs;
using MarketFlow.Application.Features.Products.DTOs;
using MarketFlow.Application.Features.Purchases.DTOs;
using MarketFlow.Application.Features.Purchases.Services;
using MarketFlow.Application.Features.Sales.DTOs;
using MarketFlow.Application.Features.Sales.Services;

namespace MarketFlow.Api.Tests.Inventory;

public sealed class StockMovementServiceReviewTests
{
    [Fact]
    public async Task CreateSaleAsync_ReturnsValidationFailureWhenStockCannotBeApplied()
    {
        var tenantQueryService = new RecordingTenantQueryService { SaleCreateResult = null };
        var service = new SalesService(tenantQueryService, new FakeCurrentUserService());

        var result = await service.CreateSaleAsync(new CreateSaleRequest
        {
            MarketId = 1,
            Items = [new CreateSaleItemRequest { ProductId = 10, Quantity = 3, UnitPrice = 2.50m }]
        });

        Assert.False(result.Succeeded);
        Assert.Equal("Insufficient inventory stock for one or more sale items.", result.Message);
    }

    [Fact]
    public async Task UpdateAndPatchPurchaseAsync_PassCurrentUserForReceiptStockAttribution()
    {
        var tenantQueryService = new RecordingTenantQueryService
        {
            PurchaseResult = new PurchaseDto { Id = 5, SupplierId = 1, MarketId = 2, Status = "Received" }
        };
        var service = new PurchaseService(tenantQueryService, new FakeCurrentUserService());

        await service.UpdatePurchaseAsync(5, new UpdatePurchaseRequest
        {
            SupplierId = 1,
            MarketId = 2,
            PurchaseDate = DateOnly.FromDateTime(DateTime.UtcNow),
            Status = "Received"
        });

        await service.PatchPurchaseAsync(5, new PatchPurchaseRequest { Status = "Received" });

        Assert.Equal(42, tenantQueryService.LastUpdatePurchaseUpdatedByUserId);
        Assert.Equal(42, tenantQueryService.LastPatchPurchaseUpdatedByUserId);
    }

    private sealed class RecordingTenantQueryService : ITenantQueryService
    {
        public SaleDto? SaleCreateResult { get; init; }
        public PurchaseDto? PurchaseResult { get; init; }
        public int? LastUpdatePurchaseUpdatedByUserId { get; private set; }
        public int? LastPatchPurchaseUpdatedByUserId { get; private set; }

        public Task<SaleDto?> CreateSaleAsync(
            CreateSaleRequest request,
            int createdByUserId,
            CancellationToken cancellationToken = default)
        {
            return Task.FromResult(SaleCreateResult);
        }

        public Task<PurchaseDto?> UpdatePurchaseAsync(
            int id,
            UpdatePurchaseRequest request,
            int? updatedByUserId,
            CancellationToken cancellationToken = default)
        {
            LastUpdatePurchaseUpdatedByUserId = updatedByUserId;
            return Task.FromResult(PurchaseResult);
        }

        public Task<PurchaseDto?> PatchPurchaseAsync(
            int id,
            PatchPurchaseRequest request,
            int? updatedByUserId,
            CancellationToken cancellationToken = default)
        {
            LastPatchPurchaseUpdatedByUserId = updatedByUserId;
            return Task.FromResult(PurchaseResult);
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
        public Task<InventoryItemDto?> GetInventoryItemAsync(int id, CancellationToken cancellationToken = default) => throw new NotSupportedException();
        public Task<PagedResult<InventoryMovementDto>> GetInventoryMovementsAsync(InventoryMovementListQuery query, CancellationToken cancellationToken = default) => throw new NotSupportedException();
        public Task<PagedResult<InventoryMovementDto>> GetInventoryMovementsForInventoryAsync(int inventoryId, InventoryMovementListQuery query, CancellationToken cancellationToken = default) => throw new NotSupportedException();
        public Task<InventoryItemDto?> CreateInventoryItemAsync(CreateInventoryItemRequest request, int? updatedByUserId, CancellationToken cancellationToken = default) => throw new NotSupportedException();
        public Task<InventoryItemDto?> UpdateInventoryItemAsync(int id, UpdateInventoryItemRequest request, int? updatedByUserId, CancellationToken cancellationToken = default) => throw new NotSupportedException();
        public Task<InventoryItemDto?> PatchInventoryItemAsync(int id, PatchInventoryItemRequest request, int? updatedByUserId, CancellationToken cancellationToken = default) => throw new NotSupportedException();
        public Task<InventoryItemDto?> AdjustInventoryItemAsync(int id, AdjustInventoryRequest request, int? updatedByUserId, CancellationToken cancellationToken = default) => throw new NotSupportedException();
        public Task<bool> TransferInventoryAsync(TransferInventoryRequest request, int? updatedByUserId, CancellationToken cancellationToken = default) => throw new NotSupportedException();
        public Task<bool> DeleteInventoryItemAsync(int id, CancellationToken cancellationToken = default) => throw new NotSupportedException();
        public Task<IReadOnlyCollection<SaleDto>> GetSalesAsync(CancellationToken cancellationToken = default) => throw new NotSupportedException();
        public Task<SaleDto?> UpdateSaleAsync(int id, UpdateSaleRequest request, CancellationToken cancellationToken = default) => throw new NotSupportedException();
        public Task<SaleDto?> PatchSaleAsync(int id, PatchSaleRequest request, CancellationToken cancellationToken = default) => throw new NotSupportedException();
        public Task<bool> DeleteSaleAsync(int id, CancellationToken cancellationToken = default) => throw new NotSupportedException();
        public Task<IReadOnlyCollection<PurchaseDto>> GetPurchasesAsync(CancellationToken cancellationToken = default) => throw new NotSupportedException();
        public Task<PurchaseDto?> CreatePurchaseAsync(CreatePurchaseRequest request, int createdByUserId, CancellationToken cancellationToken = default) => throw new NotSupportedException();
        public Task<bool> DeletePurchaseAsync(int id, CancellationToken cancellationToken = default) => throw new NotSupportedException();
    }

    private sealed class FakeCurrentUserService : ICurrentUserService
    {
        public int? UserId => 42;
        public int? CompanyId => 1;
        public string? Email => "review@example.test";
        public string? Role => "CompanyAdmin";
        public string? SchemaName => "tenant_test";
    }
}
