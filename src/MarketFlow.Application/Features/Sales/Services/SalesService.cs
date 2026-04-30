using MarketFlow.Application.Common.Models;
using MarketFlow.Application.Features.Sales.DTOs;
using MarketFlow.Application.Features.Sales.Interfaces;

namespace MarketFlow.Application.Features.Sales.Services;

public class SalesService : ISalesService
{
    public Task<ServiceResult<IReadOnlyCollection<SaleDto>>> GetSalesAsync(
        CancellationToken cancellationToken = default)
    {
        IReadOnlyCollection<SaleDto> sales = Array.Empty<SaleDto>();
        return Task.FromResult(ServiceResult<IReadOnlyCollection<SaleDto>>.Success(sales));
    }
}
