using MarketFlow.Application.Common.Models;
using MarketFlow.Application.Features.Purchases.DTOs;

namespace MarketFlow.Application.Features.Purchases.Interfaces;

public interface IPurchaseService
{
    Task<ServiceResult<IReadOnlyCollection<PurchaseDto>>> GetPurchasesAsync(
        CancellationToken cancellationToken = default);

    Task<ServiceResult<PurchaseDto>> CreatePurchaseAsync(
        CreatePurchaseRequest request,
        CancellationToken cancellationToken = default);

    Task<ServiceResult<PurchaseDto>> UpdatePurchaseAsync(
        int id,
        UpdatePurchaseRequest request,
        CancellationToken cancellationToken = default);

    Task<ServiceResult<PurchaseDto>> PatchPurchaseAsync(
        int id,
        PatchPurchaseRequest request,
        CancellationToken cancellationToken = default);
}
