using MarketFlow.Application.Features.Inventory.DTOs;
using MarketFlow.Application.Features.Categories.DTOs;
using MarketFlow.Application.Common.Models;
using MarketFlow.Application.Features.Products.DTOs;
using MarketFlow.Application.Features.Purchases.DTOs;
using MarketFlow.Application.Features.Sales.DTOs;

namespace MarketFlow.Application.Common.Interfaces;

public interface ITenantQueryService
{
    Task<PagedResult<ProductDto>> GetProductsAsync(
        ProductListQuery query,
        CancellationToken cancellationToken = default);

    Task<ProductDto?> GetProductAsync(
        int id,
        bool includeInactive = false,
        CancellationToken cancellationToken = default);

    Task<bool> CategoryExistsAsync(int categoryId, CancellationToken cancellationToken = default);

    Task<bool> ProductBarcodeExistsAsync(
        string barcode,
        int? excludedProductId = null,
        CancellationToken cancellationToken = default);

    Task<ProductDto> CreateProductAsync(CreateProductRequest request, CancellationToken cancellationToken = default);

    Task<ProductDto?> UpdateProductAsync(int id, UpdateProductRequest request, CancellationToken cancellationToken = default);

    Task<ProductDto?> PatchProductAsync(int id, PatchProductRequest request, CancellationToken cancellationToken = default);

    Task<ProductDto?> SetProductActiveStateAsync(
        int id,
        bool isActive,
        CancellationToken cancellationToken = default);

    Task<IReadOnlyCollection<CategoryDto>> GetCategoriesAsync(CancellationToken cancellationToken = default);

    Task<PagedResult<InventoryItemDto>> GetInventoryAsync(
        InventoryListQuery query,
        CancellationToken cancellationToken = default);

    Task<InventoryItemDto?> GetInventoryItemAsync(int id, CancellationToken cancellationToken = default);

    Task<PagedResult<InventoryMovementDto>> GetInventoryMovementsAsync(
        InventoryMovementListQuery query,
        CancellationToken cancellationToken = default);

    Task<PagedResult<InventoryMovementDto>> GetInventoryMovementsForInventoryAsync(
        int inventoryId,
        InventoryMovementListQuery query,
        CancellationToken cancellationToken = default);

    Task<InventoryItemDto?> CreateInventoryItemAsync(
        CreateInventoryItemRequest request,
        int? updatedByUserId,
        CancellationToken cancellationToken = default);

    Task<InventoryItemDto?> UpdateInventoryItemAsync(
        int id,
        UpdateInventoryItemRequest request,
        int? updatedByUserId,
        CancellationToken cancellationToken = default);

    Task<InventoryItemDto?> PatchInventoryItemAsync(
        int id,
        PatchInventoryItemRequest request,
        int? updatedByUserId,
        CancellationToken cancellationToken = default);

    Task<InventoryItemDto?> AdjustInventoryItemAsync(
        int id,
        AdjustInventoryRequest request,
        int? updatedByUserId,
        CancellationToken cancellationToken = default);

    Task<bool> TransferInventoryAsync(
        TransferInventoryRequest request,
        int? updatedByUserId,
        CancellationToken cancellationToken = default);

    Task<bool> DeleteInventoryItemAsync(int id, CancellationToken cancellationToken = default);

    Task<IReadOnlyCollection<SaleDto>> GetSalesAsync(CancellationToken cancellationToken = default);

    Task<SaleDto> CreateSaleAsync(
        CreateSaleRequest request,
        int createdByUserId,
        CancellationToken cancellationToken = default);

    Task<SaleDto?> UpdateSaleAsync(int id, UpdateSaleRequest request, CancellationToken cancellationToken = default);

    Task<SaleDto?> PatchSaleAsync(int id, PatchSaleRequest request, CancellationToken cancellationToken = default);

    Task<bool> DeleteSaleAsync(int id, CancellationToken cancellationToken = default);

    Task<IReadOnlyCollection<PurchaseDto>> GetPurchasesAsync(CancellationToken cancellationToken = default);

    Task<PurchaseDto> CreatePurchaseAsync(
        CreatePurchaseRequest request,
        int createdByUserId,
        CancellationToken cancellationToken = default);

    Task<PurchaseDto?> UpdatePurchaseAsync(int id, UpdatePurchaseRequest request, CancellationToken cancellationToken = default);

    Task<PurchaseDto?> PatchPurchaseAsync(int id, PatchPurchaseRequest request, CancellationToken cancellationToken = default);

    Task<bool> DeletePurchaseAsync(int id, CancellationToken cancellationToken = default);
}
