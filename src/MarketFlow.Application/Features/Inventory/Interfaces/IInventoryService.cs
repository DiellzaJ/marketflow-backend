using MarketFlow.Application.Common.Models;
using MarketFlow.Application.Features.Inventory.DTOs;

namespace MarketFlow.Application.Features.Inventory.Interfaces;

public interface IInventoryService
{
    Task<ServiceResult<IReadOnlyCollection<InventoryItemDto>>> GetInventoryAsync(
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

    Task<ServiceResult<bool>> DeleteInventoryItemAsync(
        int id,
        CancellationToken cancellationToken = default);
}
