using MarketFlow.Application.Common.Models;
using MarketFlow.Application.Features.Suppliers.DTOs;

namespace MarketFlow.Application.Features.Suppliers.Interfaces;

public interface ISupplierService
{
    Task<ServiceResult<IReadOnlyCollection<SupplierDto>>> GetSuppliersAsync(
        bool includeInactive = false,
        CancellationToken cancellationToken = default);

    Task<ServiceResult<SupplierDto>> GetSupplierAsync(
        int id,
        CancellationToken cancellationToken = default);

    Task<ServiceResult<SupplierDto>> CreateSupplierAsync(
        CreateSupplierRequest request,
        CancellationToken cancellationToken = default);

    Task<ServiceResult<SupplierDto>> UpdateSupplierAsync(
        int id,
        UpdateSupplierRequest request,
        CancellationToken cancellationToken = default);

    Task<ServiceResult<SupplierDto>> SetSupplierActiveStateAsync(
        int id,
        bool isActive,
        CancellationToken cancellationToken = default);
}
