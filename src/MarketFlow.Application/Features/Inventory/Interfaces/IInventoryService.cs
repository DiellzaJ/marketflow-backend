using MarketFlow.Application.Common.Models;
using MarketFlow.Application.Features.Inventory.DTOs;

namespace MarketFlow.Application.Features.Inventory.Interfaces;

public interface IInventoryService
{
    Task<ServiceResult<PagedResult<InventoryItemDto>>> GetInventoryAsync(
        InventoryListQuery query,
        CancellationToken cancellationToken = default);

    Task<ServiceResult<InventoryItemDto>> GetInventoryItemAsync(
        int id,
        CancellationToken cancellationToken = default);

    Task<ServiceResult<IReadOnlyCollection<InventoryMovementDto>>> GetInventoryMovementsAsync(
        int inventoryId,
        CancellationToken cancellationToken = default);

    Task<ServiceResult<InventoryItemDto>> CreateInventoryItemAsync(
        CreateInventoryItemRequest request,
        CancellationToken cancellationToken = default);

    Task<ServiceResult<InventoryItemDto>> UpdateInventoryItemAsync(
        int id,
        UpdateInventoryItemRequest request,
        CancellationToken cancellationToken = default);

    Task<ServiceResult<InventoryItemDto>> PatchInventoryItemAsync(
        int id,
        PatchInventoryItemRequest request,
        CancellationToken cancellationToken = default);

    Task<ServiceResult<InventoryItemDto>> AdjustInventoryItemAsync(
        int id,
        AdjustInventoryRequest request,
        CancellationToken cancellationToken = default);

    Task<ServiceResult<bool>> DeleteInventoryItemAsync(
        int id,
        CancellationToken cancellationToken = default);
}
