using MarketFlow.Application.Common.Interfaces;
using MarketFlow.Application.Common.Models;
using MarketFlow.Application.Features.Inventory.Configuration;
using MarketFlow.Application.Features.Inventory.DTOs;
using MarketFlow.Application.Features.Inventory.Interfaces;

namespace MarketFlow.Application.Features.Inventory.Services;

public class InventoryService : IInventoryService
{
    private const int MaxPageSize = 100;
    private const int MaxPosLookupLimit = 50;
    private const int MaxAdjustmentReasonLength = 100;

    private readonly ITenantQueryService _tenantQueryService;
    private readonly ICurrentUserService _currentUserService;

    public InventoryService(
        ITenantQueryService tenantQueryService,
        ICurrentUserService currentUserService)
    {
        _tenantQueryService = tenantQueryService;
        _currentUserService = currentUserService;
    }

    public async Task<ServiceResult<PagedResult<InventoryItemDto>>> GetInventoryAsync(
        InventoryListQuery query,
        CancellationToken cancellationToken = default)
    {
        query.SortBy = query.SortBy?.Trim();
        query.SortDirection = query.SortDirection?.Trim();

        if (query.Page < 1)
        {
            return ServiceResult<PagedResult<InventoryItemDto>>.Failure("Page must be greater than zero.");
        }

        if (query.PageSize is < 1 or > MaxPageSize)
        {
            return ServiceResult<PagedResult<InventoryItemDto>>.Failure($"Page size must be between 1 and {MaxPageSize}.");
        }

        if (!InventorySortFields.IsAllowed(query.SortBy))
        {
            return ServiceResult<PagedResult<InventoryItemDto>>.Failure("Sort field is not supported.");
        }

        if (!string.IsNullOrWhiteSpace(query.SortDirection) &&
            !string.Equals(query.SortDirection, "asc", StringComparison.OrdinalIgnoreCase) &&
            !string.Equals(query.SortDirection, "desc", StringComparison.OrdinalIgnoreCase))
        {
            return ServiceResult<PagedResult<InventoryItemDto>>.Failure("Sort direction must be asc or desc.");
        }

        var inventory = await _tenantQueryService.GetInventoryAsync(query, cancellationToken);
        return ServiceResult<PagedResult<InventoryItemDto>>.Success(inventory);
    }

    public async Task<ServiceResult<IReadOnlyCollection<InventoryItemDto>>> GetLowStockInventoryAsync(
        CancellationToken cancellationToken = default)
    {
        var inventory = await _tenantQueryService.GetLowStockInventoryAsync(cancellationToken);
        return ServiceResult<IReadOnlyCollection<InventoryItemDto>>.Success(inventory);
    }

    public async Task<ServiceResult<IReadOnlyCollection<PosProductLookupItemDto>>> GetPosProductsAsync(
        PosProductLookupQuery query,
        CancellationToken cancellationToken = default)
    {
        query.Barcode = string.IsNullOrWhiteSpace(query.Barcode) ? null : query.Barcode.Trim();
        query.Search = string.IsNullOrWhiteSpace(query.Search) ? null : query.Search.Trim();

        if (!query.MarketId.HasValue || query.MarketId <= 0)
        {
            return ServiceResult<IReadOnlyCollection<PosProductLookupItemDto>>.Failure(
                "Market is required for POS product lookup.");
        }

        if (string.IsNullOrWhiteSpace(query.Barcode) && string.IsNullOrWhiteSpace(query.Search))
        {
            return ServiceResult<IReadOnlyCollection<PosProductLookupItemDto>>.Failure(
                "Barcode or product search is required.");
        }

        if (query.Limit is < 1 or > MaxPosLookupLimit)
        {
            return ServiceResult<IReadOnlyCollection<PosProductLookupItemDto>>.Failure(
                $"Limit must be between 1 and {MaxPosLookupLimit}.");
        }

        var products = await _tenantQueryService.GetPosProductsAsync(query, cancellationToken);

        return products is null
            ? ServiceResult<IReadOnlyCollection<PosProductLookupItemDto>>.Failure(
                "POS lookup market is outside the current user's inventory scope.")
            : ServiceResult<IReadOnlyCollection<PosProductLookupItemDto>>.Success(products);
    }

    public async Task<ServiceResult<InventoryItemDto>> GetInventoryItemAsync(
        int id,
        CancellationToken cancellationToken = default)
    {
        var inventoryItem = await _tenantQueryService.GetInventoryItemAsync(id, cancellationToken);

        return inventoryItem is null
            ? ServiceResult<InventoryItemDto>.Failure("Inventory item was not found.")
            : ServiceResult<InventoryItemDto>.Success(inventoryItem);
    }

    public async Task<ServiceResult<PagedResult<InventoryMovementDto>>> GetInventoryMovementsAsync(
        InventoryMovementListQuery query,
        CancellationToken cancellationToken = default)
    {
        var validation = ValidateMovementQuery(query);
        if (validation is not null)
        {
            return ServiceResult<PagedResult<InventoryMovementDto>>.Failure(validation);
        }

        var movements = await _tenantQueryService.GetInventoryMovementsAsync(query, cancellationToken);
        return ServiceResult<PagedResult<InventoryMovementDto>>.Success(movements);
    }

    public async Task<ServiceResult<PagedResult<InventoryMovementDto>>> GetInventoryMovementsForInventoryAsync(
        int inventoryId,
        InventoryMovementListQuery query,
        CancellationToken cancellationToken = default)
    {
        var inventoryItem = await _tenantQueryService.GetInventoryItemAsync(inventoryId, cancellationToken);

        if (inventoryItem is null)
        {
            return ServiceResult<PagedResult<InventoryMovementDto>>.Failure(
                "Inventory item was not found.");
        }

        var validation = ValidateMovementQuery(query);
        if (validation is not null)
        {
            return ServiceResult<PagedResult<InventoryMovementDto>>.Failure(validation);
        }

        var movements = await _tenantQueryService.GetInventoryMovementsForInventoryAsync(
            inventoryId,
            query,
            cancellationToken);

        return ServiceResult<PagedResult<InventoryMovementDto>>.Success(movements);
    }

    public async Task<ServiceResult<InventoryItemDto>> CreateInventoryItemAsync(
        CreateInventoryItemRequest request,
        CancellationToken cancellationToken = default)
    {
        if (request.ProductId <= 0 || request.MarketId <= 0)
        {
            return ServiceResult<InventoryItemDto>.Failure("Product and market are required.");
        }

        if (request.Quantity < 0 || request.ReservedQuantity < 0)
        {
            return ServiceResult<InventoryItemDto>.Failure("Inventory quantities cannot be negative.");
        }

        var inventoryItem = await _tenantQueryService.CreateInventoryItemAsync(
            request,
            _currentUserService.UserId,
            cancellationToken);

        return inventoryItem is null
            ? ServiceResult<InventoryItemDto>.Failure("Inventory item was not found.")
            : ServiceResult<InventoryItemDto>.Success(inventoryItem, "Inventory item created.");
    }

    public async Task<ServiceResult<InventoryItemDto>> UpdateInventoryItemAsync(
        int id,
        UpdateInventoryItemRequest request,
        CancellationToken cancellationToken = default)
    {
        if (request.Quantity < 0 || request.ReservedQuantity < 0)
        {
            return ServiceResult<InventoryItemDto>.Failure("Inventory quantities cannot be negative.");
        }

        var inventoryItem = await _tenantQueryService.UpdateInventoryItemAsync(
            id,
            request,
            _currentUserService.UserId,
            cancellationToken);

        return inventoryItem is null
            ? ServiceResult<InventoryItemDto>.Failure("Inventory item was not found.")
            : ServiceResult<InventoryItemDto>.Success(inventoryItem, "Inventory item updated.");
    }

    public async Task<ServiceResult<InventoryItemDto>> PatchInventoryItemAsync(
        int id,
        PatchInventoryItemRequest request,
        CancellationToken cancellationToken = default)
    {
        if (request.Quantity < 0 || request.ReservedQuantity < 0)
        {
            return ServiceResult<InventoryItemDto>.Failure("Inventory quantities cannot be negative.");
        }

        var inventoryItem = await _tenantQueryService.PatchInventoryItemAsync(
            id,
            request,
            _currentUserService.UserId,
            cancellationToken);

        return inventoryItem is null
            ? ServiceResult<InventoryItemDto>.Failure("Inventory item was not found.")
            : ServiceResult<InventoryItemDto>.Success(inventoryItem, "Inventory item updated.");
    }

    public async Task<ServiceResult<InventoryItemDto>> AdjustInventoryItemAsync(
        int id,
        AdjustInventoryRequest request,
        CancellationToken cancellationToken = default)
    {
        request.Reason = (request.Reason ?? string.Empty).Trim();
        request.Note = string.IsNullOrWhiteSpace(request.Note) ? null : request.Note.Trim();

        if (string.IsNullOrWhiteSpace(request.Reason))
        {
            return ServiceResult<InventoryItemDto>.Failure("Adjustment reason is required.");
        }

        if (request.Reason.Length > MaxAdjustmentReasonLength)
        {
            return ServiceResult<InventoryItemDto>.Failure(
                $"Adjustment reason cannot exceed {MaxAdjustmentReasonLength} characters.");
        }

        var currentInventoryItem = await _tenantQueryService.GetInventoryItemAsync(id, cancellationToken);

        if (currentInventoryItem is null)
        {
            return ServiceResult<InventoryItemDto>.Failure(
                "Inventory item was not found.",
                ServiceResultFailureType.NotFound);
        }

        if (currentInventoryItem.Quantity + request.QuantityChange < 0)
        {
            return ServiceResult<InventoryItemDto>.Failure("Stock cannot become negative.");
        }

        var inventoryItem = await _tenantQueryService.AdjustInventoryItemAsync(
            id,
            request,
            _currentUserService.UserId,
            cancellationToken);

        if (inventoryItem is not null)
        {
            return ServiceResult<InventoryItemDto>.Success(inventoryItem, "Inventory stock adjusted.");
        }

        var inventoryStillExists = await _tenantQueryService.GetInventoryItemAsync(id, cancellationToken);

        return inventoryStillExists is null
            ? ServiceResult<InventoryItemDto>.Failure(
                "Inventory item was not found.",
                ServiceResultFailureType.NotFound)
            : ServiceResult<InventoryItemDto>.Failure("Stock cannot become negative.");
    }

    public async Task<ServiceResult<bool>> DeleteInventoryItemAsync(
        int id,
        CancellationToken cancellationToken = default)
    {
        var deleted = await _tenantQueryService.DeleteInventoryItemAsync(id, cancellationToken);

        return deleted
            ? ServiceResult<bool>.Success(true, "Inventory item deleted.")
            : ServiceResult<bool>.Failure("Inventory item was not found.");
    }

    public async Task<ServiceResult<bool>> TransferInventoryAsync(
        TransferInventoryRequest request,
        CancellationToken cancellationToken = default)
    {
        request.Note = string.IsNullOrWhiteSpace(request.Note) ? null : request.Note.Trim();

        if (request.ProductId <= 0 || request.FromMarketId <= 0 || request.ToMarketId <= 0)
        {
            return ServiceResult<bool>.Failure("Product, source market, and destination market are required.");
        }

        if (request.FromMarketId == request.ToMarketId &&
            request.FromDepartmentId == request.ToDepartmentId)
        {
            return ServiceResult<bool>.Failure("Source and destination must be different.");
        }

        if (request.Quantity <= 0)
        {
            return ServiceResult<bool>.Failure("Transfer quantity must be greater than zero.");
        }

        var transferred = await _tenantQueryService.TransferInventoryAsync(
            request,
            _currentUserService.UserId,
            cancellationToken);

        return transferred
            ? ServiceResult<bool>.Success(true, "Inventory stock transferred.")
            : ServiceResult<bool>.Failure("Inventory transfer could not be completed.");
    }

    private static string? ValidateMovementQuery(InventoryMovementListQuery query)
    {
        query.MovementType = query.MovementType?.Trim();

        if (query.Page < 1)
        {
            return "Page must be greater than zero.";
        }

        if (query.PageSize is < 1 or > MaxPageSize)
        {
            return $"Page size must be between 1 and {MaxPageSize}.";
        }

        if (query.DateFrom.HasValue && query.DateTo.HasValue && query.DateFrom > query.DateTo)
        {
            return "Date from cannot be later than date to.";
        }

        if (!string.IsNullOrWhiteSpace(query.MovementType) &&
            !AllowedMovementTypes.Contains(query.MovementType, StringComparer.OrdinalIgnoreCase))
        {
            return "Movement type is not supported.";
        }

        return null;
    }

    private static readonly IReadOnlySet<string> AllowedMovementTypes =
        new HashSet<string>(StringComparer.OrdinalIgnoreCase)
        {
            "InitialStock",
            "ManualAdjustment",
            "PurchaseReceived",
            "SaleCompleted",
            "TransferOut",
            "TransferIn"
        };
}
