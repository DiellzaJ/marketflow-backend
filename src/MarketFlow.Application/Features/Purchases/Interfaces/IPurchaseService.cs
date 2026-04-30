using MarketFlow.Application.Common.Models;
using MarketFlow.Application.Features.Purchases.DTOs;

namespace MarketFlow.Application.Features.Purchases.Interfaces;

public interface IPurchaseService
{
    Task<ServiceResult<IReadOnlyCollection<PurchaseDto>>> GetPurchasesAsync(
        CancellationToken cancellationToken = default);
}
