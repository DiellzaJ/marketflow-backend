using MarketFlow.Application.Common.Interfaces;
using MarketFlow.Application.Common.Models;
using MarketFlow.Application.Features.Sales.DTOs;
using MarketFlow.Application.Features.Sales.Interfaces;

namespace MarketFlow.Application.Features.Sales.Services;

public class SalesService : ISalesService
{
    private readonly ITenantQueryService _tenantQueryService;

    public SalesService(ITenantQueryService tenantQueryService)
    {
        _tenantQueryService = tenantQueryService;
    }

    public async Task<ServiceResult<IReadOnlyCollection<SaleDto>>> GetSalesAsync(
        CancellationToken cancellationToken = default)
    {
        var sales = await _tenantQueryService.GetSalesAsync(cancellationToken);
        return ServiceResult<IReadOnlyCollection<SaleDto>>.Success(sales);
    }
}
