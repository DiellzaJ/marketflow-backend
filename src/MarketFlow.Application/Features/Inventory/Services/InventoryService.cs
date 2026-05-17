using MarketFlow.Application.Common.Interfaces;
using MarketFlow.Application.Common.Models;
using MarketFlow.Application.Features.Inventory.DTOs;
using MarketFlow.Application.Features.Inventory.Interfaces;

namespace MarketFlow.Application.Features.Inventory.Services;

public class InventoryService : IInventoryService
{
    private readonly ITenantQueryService _tenantQueryService;
    private readonly ICurrentUserService _currentUserService;

    public InventoryService(
        ITenantQueryService tenantQueryService,
        ICurrentUserService currentUserService)
    {
        _tenantQueryService = tenantQueryService;
        _currentUserService = currentUserService;
    }

    public async Task<ServiceResult<IReadOnlyCollection<InventoryItemDto>>> GetInventoryAsync(
        CancellationToken cancellationToken = default)
    {
        var inventory = await _tenantQueryService.GetInventoryAsync(cancellationToken);
        return ServiceResult<IReadOnlyCollection<InventoryItemDto>>.Success(inventory);
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
}
