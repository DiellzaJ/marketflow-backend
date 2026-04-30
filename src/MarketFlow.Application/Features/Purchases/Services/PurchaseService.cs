using MarketFlow.Application.Common.Models;
using MarketFlow.Application.Features.Purchases.DTOs;
using MarketFlow.Application.Features.Purchases.Interfaces;

namespace MarketFlow.Application.Features.Purchases.Services;

public class PurchaseService : IPurchaseService
{
    public Task<ServiceResult<IReadOnlyCollection<PurchaseDto>>> GetPurchasesAsync(
        CancellationToken cancellationToken = default)
    {
        IReadOnlyCollection<PurchaseDto> purchases = Array.Empty<PurchaseDto>();
        return Task.FromResult(ServiceResult<IReadOnlyCollection<PurchaseDto>>.Success(purchases));
    }
}
