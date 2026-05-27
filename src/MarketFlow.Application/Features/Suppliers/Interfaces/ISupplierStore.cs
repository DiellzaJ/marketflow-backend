using MarketFlow.Application.Features.Suppliers.DTOs;

namespace MarketFlow.Application.Features.Suppliers.Interfaces;

public interface ISupplierStore
{
    Task<IReadOnlyCollection<SupplierDto>> GetSuppliersAsync(
        bool includeInactive = false,
        CancellationToken cancellationToken = default);

    Task<SupplierDto?> GetSupplierAsync(
        int id,
        bool includeInactive = false,
        CancellationToken cancellationToken = default);

    Task<SupplierDto> CreateSupplierAsync(
        CreateSupplierRequest request,
        CancellationToken cancellationToken = default);

    Task<SupplierDto?> UpdateSupplierAsync(
        int id,
        UpdateSupplierRequest request,
        CancellationToken cancellationToken = default);

    Task<SupplierDto?> SetSupplierActiveStateAsync(
        int id,
        bool isActive,
        CancellationToken cancellationToken = default);
}
