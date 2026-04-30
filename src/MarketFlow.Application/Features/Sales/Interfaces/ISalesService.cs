using MarketFlow.Application.Common.Models;
using MarketFlow.Application.Features.Sales.DTOs;

namespace MarketFlow.Application.Features.Sales.Interfaces;

public interface ISalesService
{
    Task<ServiceResult<IReadOnlyCollection<SaleDto>>> GetSalesAsync(
        CancellationToken cancellationToken = default);
}
