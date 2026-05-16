using MarketFlow.Application.Common.Interfaces;
using MarketFlow.Application.Common.Models;
using MarketFlow.Application.Features.Purchases.DTOs;
using MarketFlow.Application.Features.Purchases.Interfaces;

namespace MarketFlow.Application.Features.Purchases.Services;

public class PurchaseService : IPurchaseService
{
    private readonly ITenantQueryService _tenantQueryService;

    public PurchaseService(ITenantQueryService tenantQueryService)
    {
        _tenantQueryService = tenantQueryService;
    }

    public async Task<ServiceResult<IReadOnlyCollection<PurchaseDto>>> GetPurchasesAsync(
        CancellationToken cancellationToken = default)
    {
        var purchases = await _tenantQueryService.GetPurchasesAsync(cancellationToken);
        return ServiceResult<IReadOnlyCollection<PurchaseDto>>.Success(purchases);
    }
}
