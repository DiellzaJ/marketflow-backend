using MarketFlow.Application.Common.Models;
using MarketFlow.Application.Features.Purchases.DTOs;

namespace MarketFlow.Application.Features.Purchases.Interfaces;

public interface IPurchaseService
{
    Task<ServiceResult<IReadOnlyCollection<PurchaseDto>>> GetPurchasesAsync(
        CancellationToken cancellationToken = default);

    Task<ServiceResult<PurchaseDto>> GetPurchaseAsync(
        int id,
        CancellationToken cancellationToken = default) =>
        Task.FromResult(ServiceResult<PurchaseDto>.Failure(
            "Purchase was not found.",
            ServiceResultFailureType.NotFound));

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

    Task<ServiceResult<PurchaseDto>> ReceivePurchaseAsync(
        int id,
        ReceivePurchaseRequest request,
        CancellationToken cancellationToken = default) =>
        Task.FromResult(ServiceResult<PurchaseDto>.Failure(
            "Purchase could not be received.",
            ServiceResultFailureType.Conflict));

    Task<ServiceResult<PurchaseDto>> CancelPurchaseAsync(
        int id,
        CancellationToken cancellationToken = default) =>
        Task.FromResult(ServiceResult<PurchaseDto>.Failure(
            "Purchase could not be cancelled.",
            ServiceResultFailureType.Conflict));

    Task<ServiceResult<bool>> DeletePurchaseAsync(
        int id,
        CancellationToken cancellationToken = default);
}
