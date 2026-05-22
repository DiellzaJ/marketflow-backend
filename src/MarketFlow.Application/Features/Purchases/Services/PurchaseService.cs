using MarketFlow.Application.Common.Interfaces;
using MarketFlow.Application.Common.Models;
using MarketFlow.Application.Features.Purchases.DTOs;
using MarketFlow.Application.Features.Purchases.Interfaces;

namespace MarketFlow.Application.Features.Purchases.Services;

public class PurchaseService : IPurchaseService
{
    private readonly ITenantQueryService _tenantQueryService;
    private readonly ICurrentUserService _currentUserService;

    public PurchaseService(
        ITenantQueryService tenantQueryService,
        ICurrentUserService currentUserService)
    {
        _tenantQueryService = tenantQueryService;
        _currentUserService = currentUserService;
    }

    public async Task<ServiceResult<IReadOnlyCollection<PurchaseDto>>> GetPurchasesAsync(
        CancellationToken cancellationToken = default)
    {
        var purchases = await _tenantQueryService.GetPurchasesAsync(cancellationToken);
        return ServiceResult<IReadOnlyCollection<PurchaseDto>>.Success(purchases);
    }

    public async Task<ServiceResult<PurchaseDto>> CreatePurchaseAsync(
        CreatePurchaseRequest request,
        CancellationToken cancellationToken = default)
    {
        if (_currentUserService.UserId is not { } userId)
        {
            return ServiceResult<PurchaseDto>.Failure("Current user is required.");
        }

        if (request.SupplierId <= 0 || request.MarketId <= 0)
        {
            return ServiceResult<PurchaseDto>.Failure("Supplier and market are required.");
        }

        if (request.Items.Any(item => item.ProductId <= 0 || item.Quantity <= 0 || item.UnitCost < 0))
        {
            return ServiceResult<PurchaseDto>.Failure("Purchase items must include a product, positive quantity, and non-negative unit cost.");
        }

        var purchase = await _tenantQueryService.CreatePurchaseAsync(request, userId, cancellationToken);

        return ServiceResult<PurchaseDto>.Success(purchase, "Purchase created.");
    }

    public async Task<ServiceResult<PurchaseDto>> UpdatePurchaseAsync(
        int id,
        UpdatePurchaseRequest request,
        CancellationToken cancellationToken = default)
    {
        if (request.SupplierId <= 0 || request.MarketId <= 0)
        {
            return ServiceResult<PurchaseDto>.Failure("Supplier and market are required.");
        }

        var purchase = await _tenantQueryService.UpdatePurchaseAsync(id, request, cancellationToken);

        return purchase is null
            ? ServiceResult<PurchaseDto>.Failure("Purchase was not found.")
            : ServiceResult<PurchaseDto>.Success(purchase, "Purchase updated.");
    }

    public async Task<ServiceResult<PurchaseDto>> PatchPurchaseAsync(
        int id,
        PatchPurchaseRequest request,
        CancellationToken cancellationToken = default)
    {
        var purchase = await _tenantQueryService.PatchPurchaseAsync(id, request, cancellationToken);

        return purchase is null
            ? ServiceResult<PurchaseDto>.Failure("Purchase was not found.")
            : ServiceResult<PurchaseDto>.Success(purchase, "Purchase updated.");
    }

    public async Task<ServiceResult<bool>> DeletePurchaseAsync(
        int id,
        CancellationToken cancellationToken = default)
    {
        var deleted = await _tenantQueryService.DeletePurchaseAsync(id, cancellationToken);

        return deleted
            ? ServiceResult<bool>.Success(true, "Purchase deleted.")
            : ServiceResult<bool>.Failure("Purchase was not found.");
    }
}
