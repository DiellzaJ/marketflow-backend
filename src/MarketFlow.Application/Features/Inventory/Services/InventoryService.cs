using MarketFlow.Application.Common.Interfaces;
using MarketFlow.Application.Common.Models;
using MarketFlow.Application.Features.Inventory.DTOs;
using MarketFlow.Application.Features.Inventory.Interfaces;

namespace MarketFlow.Application.Features.Inventory.Services;

public class InventoryService : IInventoryService
{
    private readonly ITenantQueryService _tenantQueryService;

    public InventoryService(ITenantQueryService tenantQueryService)
    {
        _tenantQueryService = tenantQueryService;
    }

    public async Task<ServiceResult<IReadOnlyCollection<InventoryItemDto>>> GetInventoryAsync(
        CancellationToken cancellationToken = default)
    {
        var inventory = await _tenantQueryService.GetInventoryAsync(cancellationToken);
        return ServiceResult<IReadOnlyCollection<InventoryItemDto>>.Success(inventory);
    }
}
