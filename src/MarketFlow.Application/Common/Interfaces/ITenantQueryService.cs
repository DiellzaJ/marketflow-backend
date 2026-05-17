using MarketFlow.Application.Features.Inventory.DTOs;
using MarketFlow.Application.Features.Products.DTOs;
using MarketFlow.Application.Features.Purchases.DTOs;
using MarketFlow.Application.Features.Sales.DTOs;

namespace MarketFlow.Application.Common.Interfaces;

public interface ITenantQueryService
{
    Task<IReadOnlyCollection<ProductDto>> GetProductsAsync(CancellationToken cancellationToken = default);

    Task<IReadOnlyCollection<InventoryItemDto>> GetInventoryAsync(CancellationToken cancellationToken = default);

    Task<IReadOnlyCollection<SaleDto>> GetSalesAsync(CancellationToken cancellationToken = default);

    Task<IReadOnlyCollection<PurchaseDto>> GetPurchasesAsync(CancellationToken cancellationToken = default);
}
