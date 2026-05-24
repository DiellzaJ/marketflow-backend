using MarketFlow.Application.Common.Interfaces;
using MarketFlow.Application.Common.Models;
using MarketFlow.Application.Features.Purchases.DTOs;
using MarketFlow.Application.Features.Purchases.Interfaces;

namespace MarketFlow.Application.Features.Purchases.Services;

public class PurchaseService : IPurchaseService
{
    private static readonly HashSet<string> CreateOrUpdateStatuses = new(StringComparer.OrdinalIgnoreCase)
    {
        "Draft",
        "Ordered",
        "Received"
    };

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

    public async Task<ServiceResult<PurchaseDto>> GetPurchaseAsync(
        int id,
        CancellationToken cancellationToken = default)
    {
        var purchase = await _tenantQueryService.GetPurchaseAsync(id, cancellationToken);

        return purchase is null
            ? ServiceResult<PurchaseDto>.Failure("Purchase was not found.", ServiceResultFailureType.NotFound)
            : ServiceResult<PurchaseDto>.Success(purchase);
    }

    public async Task<ServiceResult<PurchaseDto>> CreatePurchaseAsync(
        CreatePurchaseRequest request,
        CancellationToken cancellationToken = default)
    {
        if (_currentUserService.UserId is not { } userId)
        {
            return ServiceResult<PurchaseDto>.Failure("Current user is required.");
        }

        var validationError = ValidatePurchaseShape(request.SupplierId, request.MarketId, request.Status, request.Items);
        if (validationError is not null)
        {
            return validationError;
        }

        var purchase = await _tenantQueryService.CreatePurchaseAsync(request, userId, cancellationToken);

        return purchase is null
            ? ServiceResult<PurchaseDto>.Failure("Purchase could not be created. Verify supplier, market, products, and access.")
            : ServiceResult<PurchaseDto>.Success(purchase, "Purchase created.");
    }

    public async Task<ServiceResult<PurchaseDto>> UpdatePurchaseAsync(
        int id,
        UpdatePurchaseRequest request,
        CancellationToken cancellationToken = default)
    {
        var validationError = ValidatePurchaseShape(
            request.SupplierId,
            request.MarketId,
            request.Status,
            request.Items);
        if (validationError is not null)
        {
            return validationError;
        }

        var purchase = await _tenantQueryService.UpdatePurchaseAsync(
            id,
            request,
            _currentUserService.UserId,
            cancellationToken);

        return purchase is null
            ? ServiceResult<PurchaseDto>.Failure("Purchase was not found.", ServiceResultFailureType.NotFound)
            : ServiceResult<PurchaseDto>.Success(purchase, "Purchase updated.");
    }

    public async Task<ServiceResult<PurchaseDto>> PatchPurchaseAsync(
        int id,
        PatchPurchaseRequest request,
        CancellationToken cancellationToken = default)
    {
        var purchase = await _tenantQueryService.PatchPurchaseAsync(
            id,
            request,
            _currentUserService.UserId,
            cancellationToken);

        return purchase is null
            ? ServiceResult<PurchaseDto>.Failure("Purchase was not found.", ServiceResultFailureType.NotFound)
            : ServiceResult<PurchaseDto>.Success(purchase, "Purchase updated.");
    }

    public async Task<ServiceResult<PurchaseDto>> ReceivePurchaseAsync(
        int id,
        ReceivePurchaseRequest request,
        CancellationToken cancellationToken = default)
    {
        if (request.Items.Any(item => item.ProductId <= 0 || item.Quantity <= 0))
        {
            return ServiceResult<PurchaseDto>.Failure("Received items must include a product and positive quantity.");
        }

        var purchase = await _tenantQueryService.ReceivePurchaseAsync(
            id,
            request,
            _currentUserService.UserId,
            cancellationToken);

        return purchase is null
            ? ServiceResult<PurchaseDto>.Failure("Purchase could not be received. Verify status, quantities, and inventory access.", ServiceResultFailureType.Conflict)
            : ServiceResult<PurchaseDto>.Success(purchase, "Purchase received.");
    }

    public async Task<ServiceResult<PurchaseDto>> CancelPurchaseAsync(
        int id,
        CancellationToken cancellationToken = default)
    {
        var purchase = await _tenantQueryService.CancelPurchaseAsync(id, cancellationToken);

        return purchase is null
            ? ServiceResult<PurchaseDto>.Failure("Purchase could not be cancelled.", ServiceResultFailureType.Conflict)
            : ServiceResult<PurchaseDto>.Success(purchase, "Purchase cancelled.");
    }

    public async Task<ServiceResult<bool>> DeletePurchaseAsync(
        int id,
        CancellationToken cancellationToken = default)
    {
        var deleted = await _tenantQueryService.DeletePurchaseAsync(id, cancellationToken);

        return deleted
            ? ServiceResult<bool>.Success(true, "Purchase deleted.")
            : ServiceResult<bool>.Failure("Purchase was not found.", ServiceResultFailureType.NotFound);
    }

    private static ServiceResult<PurchaseDto>? ValidatePurchaseShape(
        int supplierId,
        int marketId,
        string status,
        IReadOnlyCollection<CreatePurchaseItemRequest>? items)
    {
        if (supplierId <= 0 || marketId <= 0)
        {
            return ServiceResult<PurchaseDto>.Failure("Supplier and market are required.");
        }

        if (!CreateOrUpdateStatuses.Contains(status.Trim()))
        {
            return ServiceResult<PurchaseDto>.Failure("Purchase status must be Draft, Ordered, or Received.");
        }

        if (items is null)
        {
            return null;
        }

        if (items.Count == 0)
        {
            return ServiceResult<PurchaseDto>.Failure("At least one purchase item is required.");
        }

        if (items.Any(item => item.ProductId <= 0 || item.Quantity <= 0 || item.UnitCost < 0))
        {
            return ServiceResult<PurchaseDto>.Failure("Purchase items must include a product, positive quantity, and non-negative unit cost.");
        }

        if (items.Select(item => item.ProductId).Distinct().Count() != items.Count)
        {
            return ServiceResult<PurchaseDto>.Failure("Purchase items cannot contain duplicate products.");
        }

        return null;
    }
}
