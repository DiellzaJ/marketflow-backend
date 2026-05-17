using MarketFlow.Application.Features.Inventory.DTOs;
using MarketFlow.Application.Features.Products.DTOs;
using MarketFlow.Application.Features.Purchases.DTOs;
using MarketFlow.Application.Features.Sales.DTOs;

namespace MarketFlow.Application.Common.Interfaces;

public interface ITenantQueryService
{
    Task<IReadOnlyCollection<ProductDto>> GetProductsAsync(CancellationToken cancellationToken = default);

    Task<ProductDto?> GetProductAsync(int id, CancellationToken cancellationToken = default);

    Task<ProductDto> CreateProductAsync(CreateProductRequest request, CancellationToken cancellationToken = default);

    Task<ProductDto?> UpdateProductAsync(int id, UpdateProductRequest request, CancellationToken cancellationToken = default);

    Task<ProductDto?> PatchProductAsync(int id, PatchProductRequest request, CancellationToken cancellationToken = default);

    Task<bool> DeleteProductAsync(int id, CancellationToken cancellationToken = default);

    Task<IReadOnlyCollection<InventoryItemDto>> GetInventoryAsync(CancellationToken cancellationToken = default);

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

    Task<IReadOnlyCollection<SaleDto>> GetSalesAsync(CancellationToken cancellationToken = default);

    Task<SaleDto> CreateSaleAsync(
        CreateSaleRequest request,
        int createdByUserId,
        CancellationToken cancellationToken = default);

    Task<IReadOnlyCollection<PurchaseDto>> GetPurchasesAsync(CancellationToken cancellationToken = default);

    Task<PurchaseDto> CreatePurchaseAsync(
        CreatePurchaseRequest request,
        int createdByUserId,
        CancellationToken cancellationToken = default);

    Task<PurchaseDto?> UpdatePurchaseAsync(int id, UpdatePurchaseRequest request, CancellationToken cancellationToken = default);

    Task<PurchaseDto?> PatchPurchaseAsync(int id, PatchPurchaseRequest request, CancellationToken cancellationToken = default);
}
