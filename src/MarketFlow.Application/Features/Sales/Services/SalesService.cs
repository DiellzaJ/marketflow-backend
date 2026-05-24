using MarketFlow.Application.Common.Exceptions;
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

        if (request.Items.Any(item => item.ProductId <= 0 || item.Quantity <= 0 || item.UnitPrice < 0))
        {
            return ServiceResult<SaleDto>.Failure("Sale items must include a product, positive quantity, and non-negative unit price.");
        }

        try
        {
            var sale = await _tenantQueryService.CreateSaleAsync(request, userId, cancellationToken);

            return sale is null
                ? ServiceResult<SaleDto>.Failure("Insufficient inventory stock for one or more sale items.")
                : ServiceResult<SaleDto>.Success(sale, "Sale created.");
        }
        catch (SaleReferenceNumberRepairException)
        {
            return ServiceResult<SaleDto>.Failure(
                "Sale creation failed because the tenant sales schema could not be repaired. See server logs for database diagnostics.");
        }
    }

    public async Task<ServiceResult<SaleDetailsResponse>> GetSaleDetailsAsync(
        int id,
        CancellationToken cancellationToken = default)
    {
        var sale = await _tenantQueryService.GetSaleDetailsAsync(id, cancellationToken);

        return sale is null
            ? ServiceResult<SaleDetailsResponse>.Failure("Sale was not found.", ServiceResultFailureType.NotFound)
            : ServiceResult<SaleDetailsResponse>.Success(sale);
    }

    public async Task<ServiceResult<SaleDto>> UpdateSaleAsync(
        int id,
        UpdateSaleRequest request,
        CancellationToken cancellationToken = default)
    {
        if (request.MarketId <= 0)
        {
            return ServiceResult<SaleDto>.Failure("Market is required.");
        }

        var sale = await _tenantQueryService.UpdateSaleAsync(id, request, cancellationToken);

        return sale is null
            ? ServiceResult<SaleDto>.Failure("Sale was not found.", ServiceResultFailureType.NotFound)
            : ServiceResult<SaleDto>.Success(sale, "Sale updated.");
    }

    public async Task<ServiceResult<SaleDto>> PatchSaleAsync(
        int id,
        PatchSaleRequest request,
        CancellationToken cancellationToken = default)
    {
        var sale = await _tenantQueryService.PatchSaleAsync(id, request, cancellationToken);

        return sale is null
            ? ServiceResult<SaleDto>.Failure("Sale was not found.", ServiceResultFailureType.NotFound)
            : ServiceResult<SaleDto>.Success(sale, "Sale updated.");
    }

    public async Task<ServiceResult<bool>> DeleteSaleAsync(
        int id,
        CancellationToken cancellationToken = default)
    {
        var deleted = await _tenantQueryService.DeleteSaleAsync(id, cancellationToken);

        return deleted
            ? ServiceResult<bool>.Success(true, "Sale deleted.")
            : ServiceResult<bool>.Failure("Sale was not found.", ServiceResultFailureType.NotFound);
    }
}
