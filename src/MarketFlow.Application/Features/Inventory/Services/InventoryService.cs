using MarketFlow.Application.Common.Interfaces;
using MarketFlow.Application.Common.Models;
using MarketFlow.Application.Features.Inventory.Configuration;
using MarketFlow.Application.Features.Inventory.DTOs;
using MarketFlow.Application.Features.Inventory.Interfaces;

namespace MarketFlow.Application.Features.Inventory.Services;

public class InventoryService : IInventoryService
{
    private const int MaxPageSize = 100;

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

    public async Task<ServiceResult<InventoryItemDto>> GetInventoryItemAsync(
        int id,
        CancellationToken cancellationToken = default)
    {
        var inventoryItem = await _tenantQueryService.GetInventoryItemAsync(id, cancellationToken);

        return inventoryItem is null
            ? ServiceResult<InventoryItemDto>.Failure("Inventory item was not found.")
            : ServiceResult<InventoryItemDto>.Success(inventoryItem);
    }

    public async Task<ServiceResult<IReadOnlyCollection<InventoryMovementDto>>> GetInventoryMovementsAsync(
        int inventoryId,
        CancellationToken cancellationToken = default)
    {
        var inventoryItem = await _tenantQueryService.GetInventoryItemAsync(inventoryId, cancellationToken);

        if (inventoryItem is null)
        {
            return ServiceResult<IReadOnlyCollection<InventoryMovementDto>>.Failure(
                "Inventory item was not found.");
        }

        var movements = await _tenantQueryService.GetInventoryMovementsAsync(inventoryId, cancellationToken);

        return ServiceResult<IReadOnlyCollection<InventoryMovementDto>>.Success(movements);
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

    public async Task<ServiceResult<bool>> DeleteInventoryItemAsync(
        int id,
        CancellationToken cancellationToken = default)
    {
        var deleted = await _tenantQueryService.DeleteInventoryItemAsync(id, cancellationToken);

        return deleted
            ? ServiceResult<bool>.Success(true, "Inventory item deleted.")
            : ServiceResult<bool>.Failure("Inventory item was not found.");
    }
}
