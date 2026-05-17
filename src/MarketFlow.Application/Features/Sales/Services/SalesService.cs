using MarketFlow.Application.Common.Interfaces;
using MarketFlow.Application.Common.Models;
using MarketFlow.Application.Features.Sales.DTOs;
using MarketFlow.Application.Features.Sales.Interfaces;

namespace MarketFlow.Application.Features.Sales.Services;

public class SalesService : ISalesService
{
    private readonly ITenantQueryService _tenantQueryService;
    private readonly ICurrentUserService _currentUserService;

    public SalesService(
        ITenantQueryService tenantQueryService,
        ICurrentUserService currentUserService)
    {
        _tenantQueryService = tenantQueryService;
        _currentUserService = currentUserService;
    }

    public async Task<ServiceResult<IReadOnlyCollection<SaleDto>>> GetSalesAsync(
        CancellationToken cancellationToken = default)
    {
        var sales = await _tenantQueryService.GetSalesAsync(cancellationToken);
        return ServiceResult<IReadOnlyCollection<SaleDto>>.Success(sales);
    }

    public async Task<ServiceResult<SaleDto>> CreateSaleAsync(
        CreateSaleRequest request,
        CancellationToken cancellationToken = default)
    {
        if (_currentUserService.UserId is not { } userId)
        {
            return ServiceResult<SaleDto>.Failure("Current user is required.");
        }

        if (request.MarketId <= 0)
        {
            return ServiceResult<SaleDto>.Failure("Market is required.");
        }

        var sale = await _tenantQueryService.CreateSaleAsync(request, userId, cancellationToken);

        return ServiceResult<SaleDto>.Success(sale, "Sale created.");
    }
}
