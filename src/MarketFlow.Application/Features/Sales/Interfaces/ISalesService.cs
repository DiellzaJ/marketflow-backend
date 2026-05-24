using MarketFlow.Application.Common.Models;
using MarketFlow.Application.Features.Sales.DTOs;

namespace MarketFlow.Application.Features.Sales.Interfaces;

public interface ISalesService
{
    Task<ServiceResult<IReadOnlyCollection<SaleDto>>> GetSalesAsync(
        CancellationToken cancellationToken = default);

    Task<ServiceResult<PagedResult<SaleHistoryItemDto>>> GetSalesHistoryAsync(
        SaleHistoryQuery query,
        CancellationToken cancellationToken = default);

    Task<ServiceResult<SaleDto>> CreateSaleAsync(
        CreateSaleRequest request,
        CancellationToken cancellationToken = default);

    Task<ServiceResult<SaleDetailsResponse>> GetSaleDetailsAsync(
        int id,
        CancellationToken cancellationToken = default);

    Task<ServiceResult<SaleDto>> UpdateSaleAsync(
        int id,
        UpdateSaleRequest request,
        CancellationToken cancellationToken = default);

    Task<ServiceResult<SaleDto>> PatchSaleAsync(
        int id,
        PatchSaleRequest request,
        CancellationToken cancellationToken = default);

    Task<ServiceResult<bool>> DeleteSaleAsync(
        int id,
        CancellationToken cancellationToken = default);
}
