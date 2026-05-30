using MarketFlow.Application.Common.Exceptions;
using MarketFlow.Application.Common.Interfaces;
using MarketFlow.Application.Common.Models;
using MarketFlow.Application.Features.AI.Interfaces;
using MarketFlow.Application.Features.Sales.Configuration;
using MarketFlow.Application.Features.Sales.DTOs;
using MarketFlow.Application.Features.Sales.Interfaces;

namespace MarketFlow.Application.Features.Sales.Services;

public class SalesService : ISalesService
{
    private const int MaxPageSize = 100;

    private readonly ITenantQueryService _tenantQueryService;
    private readonly ICurrentUserService _currentUserService;
    private readonly IAiResultCache? _aiResultCache;

    public SalesService(
        ITenantQueryService tenantQueryService,
        ICurrentUserService currentUserService,
        IAiResultCache? aiResultCache = null)
    {
        _tenantQueryService = tenantQueryService;
        _currentUserService = currentUserService;
        _aiResultCache = aiResultCache;
    }

    public async Task<ServiceResult<IReadOnlyCollection<SaleDto>>> GetSalesAsync(
        CancellationToken cancellationToken = default)
    {
        try
        {
            var sales = await _tenantQueryService.GetSalesAsync(cancellationToken);
            return ServiceResult<IReadOnlyCollection<SaleDto>>.Success(sales);
        }
        catch (SaleReferenceNumberRepairException)
        {
            return ServiceResult<IReadOnlyCollection<SaleDto>>.Failure(
                "Sales could not be loaded because the tenant sales schema could not be repaired. See server logs for database diagnostics.");
        }
    }

    public async Task<ServiceResult<PagedResult<SaleHistoryItemDto>>> GetSalesHistoryAsync(
        SaleHistoryQuery query,
        CancellationToken cancellationToken = default)
    {
        query.SortBy = query.SortBy?.Trim();
        query.SortDirection = query.SortDirection?.Trim();
        query.Status = query.Status?.Trim();
        query.CashierUserId ??= query.UserId;

        if (query.Page < 1)
        {
            return ServiceResult<PagedResult<SaleHistoryItemDto>>.Failure("Page must be greater than zero.");
        }

        if (query.PageSize is < 1 or > MaxPageSize)
        {
            return ServiceResult<PagedResult<SaleHistoryItemDto>>.Failure($"Page size must be between 1 and {MaxPageSize}.");
        }

        if (query.DateFrom.HasValue && query.DateTo.HasValue && query.DateFrom > query.DateTo)
        {
            return ServiceResult<PagedResult<SaleHistoryItemDto>>.Failure("Date from must be on or before date to.");
        }

        if (query.MarketId is <= 0)
        {
            return ServiceResult<PagedResult<SaleHistoryItemDto>>.Failure("Market filter must be greater than zero.");
        }

        if (query.CashierUserId is <= 0)
        {
            return ServiceResult<PagedResult<SaleHistoryItemDto>>.Failure("Cashier filter must be greater than zero.");
        }

        if (query.UserId is <= 0)
        {
            return ServiceResult<PagedResult<SaleHistoryItemDto>>.Failure("User filter must be greater than zero.");
        }

        if (!SalesHistorySortFields.IsAllowed(query.SortBy))
        {
            return ServiceResult<PagedResult<SaleHistoryItemDto>>.Failure("Sort field is not supported.");
        }

        if (!string.IsNullOrWhiteSpace(query.SortDirection) &&
            !string.Equals(query.SortDirection, "asc", StringComparison.OrdinalIgnoreCase) &&
            !string.Equals(query.SortDirection, "desc", StringComparison.OrdinalIgnoreCase))
        {
            return ServiceResult<PagedResult<SaleHistoryItemDto>>.Failure("Sort direction must be asc or desc.");
        }

        try
        {
            var sales = await _tenantQueryService.GetSalesHistoryAsync(query, cancellationToken);
            return ServiceResult<PagedResult<SaleHistoryItemDto>>.Success(sales);
        }
        catch (SaleReferenceNumberRepairException)
        {
            return ServiceResult<PagedResult<SaleHistoryItemDto>>.Failure(
                "Sales history could not be loaded because the tenant sales schema could not be repaired. See server logs for database diagnostics.");
        }
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

            if (sale is null)
            {
                return ServiceResult<SaleDto>.Failure("Insufficient inventory stock for one or more sale items.");
            }

            await InvalidateAiCacheAsync(cancellationToken);
            return ServiceResult<SaleDto>.Success(sale, "Sale created.");
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
        try
        {
            var sale = await _tenantQueryService.GetSaleDetailsAsync(id, cancellationToken);

            return sale is null
                ? ServiceResult<SaleDetailsResponse>.Failure("Sale was not found.", ServiceResultFailureType.NotFound)
                : ServiceResult<SaleDetailsResponse>.Success(sale);
        }
        catch (SaleReferenceNumberRepairException)
        {
            return ServiceResult<SaleDetailsResponse>.Failure(
                "Sale details could not be loaded because the tenant sales schema could not be repaired. See server logs for database diagnostics.");
        }
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

        try
        {
            var sale = await _tenantQueryService.UpdateSaleAsync(id, request, cancellationToken);

            if (sale is null)
            {
                return ServiceResult<SaleDto>.Failure("Sale was not found.", ServiceResultFailureType.NotFound);
            }

            await InvalidateAiCacheAsync(cancellationToken);
            return ServiceResult<SaleDto>.Success(sale, "Sale updated.");
        }
        catch (SaleReferenceNumberRepairException)
        {
            return ServiceResult<SaleDto>.Failure(
                "Sale update failed because the tenant sales schema could not be repaired. See server logs for database diagnostics.");
        }
    }

    public async Task<ServiceResult<SaleDto>> PatchSaleAsync(
        int id,
        PatchSaleRequest request,
        CancellationToken cancellationToken = default)
    {
        try
        {
            var sale = await _tenantQueryService.PatchSaleAsync(id, request, cancellationToken);

            if (sale is null)
            {
                return ServiceResult<SaleDto>.Failure("Sale was not found.", ServiceResultFailureType.NotFound);
            }

            await InvalidateAiCacheAsync(cancellationToken);
            return ServiceResult<SaleDto>.Success(sale, "Sale updated.");
        }
        catch (SaleReferenceNumberRepairException)
        {
            return ServiceResult<SaleDto>.Failure(
                "Sale patch failed because the tenant sales schema could not be repaired. See server logs for database diagnostics.");
        }
    }

    public async Task<ServiceResult<bool>> DeleteSaleAsync(
        int id,
        CancellationToken cancellationToken = default)
    {
        var deleted = await _tenantQueryService.DeleteSaleAsync(id, cancellationToken);

        if (!deleted)
        {
            return ServiceResult<bool>.Failure("Sale was not found.", ServiceResultFailureType.NotFound);
        }

        await InvalidateAiCacheAsync(cancellationToken);
        return ServiceResult<bool>.Success(true, "Sale deleted.");
    }

    private Task InvalidateAiCacheAsync(CancellationToken cancellationToken) =>
        _currentUserService.CompanyId is { } companyId && _aiResultCache is not null
            ? _aiResultCache.InvalidateCompanyAsync(companyId, cancellationToken)
            : Task.CompletedTask;
}
