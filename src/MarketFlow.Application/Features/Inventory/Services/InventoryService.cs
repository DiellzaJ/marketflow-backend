using MarketFlow.Application.Common.Models;
using MarketFlow.Application.Features.Inventory.DTOs;
using MarketFlow.Application.Features.Inventory.Interfaces;

namespace MarketFlow.Application.Features.Inventory.Services;

public class InventoryService : IInventoryService
{
    public Task<ServiceResult<IReadOnlyCollection<InventoryItemDto>>> GetInventoryAsync(
        CancellationToken cancellationToken = default)
    {
        IReadOnlyCollection<InventoryItemDto> inventory = Array.Empty<InventoryItemDto>();
        return Task.FromResult(ServiceResult<IReadOnlyCollection<InventoryItemDto>>.Success(inventory));
    }
}
