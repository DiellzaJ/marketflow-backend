using MarketFlow.Application.Common.Models;
using MarketFlow.Application.Features.Inventory.DTOs;

namespace MarketFlow.Application.Features.Inventory.Interfaces;

public interface IInventoryService
{
    Task<ServiceResult<IReadOnlyCollection<InventoryItemDto>>> GetInventoryAsync(
        CancellationToken cancellationToken = default);
}
