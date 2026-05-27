using System.Net.Mail;
using MarketFlow.Application.Common.Models;
using MarketFlow.Application.Features.Suppliers.DTOs;
using MarketFlow.Application.Features.Suppliers.Interfaces;

namespace MarketFlow.Application.Features.Suppliers.Services;

public class SupplierService : ISupplierService
{
    private const int MaxNameLength = 150;
    private const string ActiveStateChangeMessage =
        "Use the supplier status or deactivate endpoint to change active state.";

    private readonly ISupplierStore _supplierStore;

    public SupplierService(ISupplierStore supplierStore)
    {
        _supplierStore = supplierStore;
    }

    public async Task<ServiceResult<IReadOnlyCollection<SupplierDto>>> GetSuppliersAsync(
        bool includeInactive = false,
        CancellationToken cancellationToken = default)
    {
        var suppliers = await _supplierStore.GetSuppliersAsync(includeInactive, cancellationToken);

        return ServiceResult<IReadOnlyCollection<SupplierDto>>.Success(suppliers);
    }

    public async Task<ServiceResult<SupplierDto>> GetSupplierAsync(
        int id,
        CancellationToken cancellationToken = default)
    {
        var supplier = await _supplierStore.GetSupplierAsync(id, cancellationToken: cancellationToken);

        return supplier is null
            ? ServiceResult<SupplierDto>.Failure("Supplier was not found.", ServiceResultFailureType.NotFound)
            : ServiceResult<SupplierDto>.Success(supplier);
    }

    public async Task<ServiceResult<SupplierDto>> CreateSupplierAsync(
        CreateSupplierRequest request,
        CancellationToken cancellationToken = default)
    {
        var validationError = ValidateSupplierShape(request.Name, request.Email);

        if (validationError is not null)
        {
            return validationError;
        }

        request.Name = request.Name.Trim();
        request.Phone = NormalizeOptionalText(request.Phone);
        request.Email = NormalizeOptionalText(request.Email);
        request.Address = NormalizeOptionalText(request.Address);

        var supplier = await _supplierStore.CreateSupplierAsync(request, cancellationToken);

        return ServiceResult<SupplierDto>.Success(supplier, "Supplier created.");
    }

    public async Task<ServiceResult<SupplierDto>> UpdateSupplierAsync(
        int id,
        UpdateSupplierRequest request,
        CancellationToken cancellationToken = default)
    {
        if (request.IsActive.HasValue)
        {
            return ServiceResult<SupplierDto>.Failure(ActiveStateChangeMessage);
        }

        var validationError = ValidateSupplierShape(request.Name, request.Email);

        if (validationError is not null)
        {
            return validationError;
        }

        request.Name = request.Name.Trim();
        request.Phone = NormalizeOptionalText(request.Phone);
        request.Email = NormalizeOptionalText(request.Email);
        request.Address = NormalizeOptionalText(request.Address);

        var supplier = await _supplierStore.UpdateSupplierAsync(id, request, cancellationToken);

        return supplier is null
            ? ServiceResult<SupplierDto>.Failure("Supplier was not found.", ServiceResultFailureType.NotFound)
            : ServiceResult<SupplierDto>.Success(supplier, "Supplier updated.");
    }

    public async Task<ServiceResult<SupplierDto>> SetSupplierActiveStateAsync(
        int id,
        bool isActive,
        CancellationToken cancellationToken = default)
    {
        var currentSupplier = await _supplierStore.GetSupplierAsync(
            id,
            includeInactive: true,
            cancellationToken);

        if (currentSupplier is null)
        {
            return ServiceResult<SupplierDto>.Failure("Supplier was not found.", ServiceResultFailureType.NotFound);
        }

        if (currentSupplier.IsActive == isActive)
        {
            return ServiceResult<SupplierDto>.Failure(
                isActive ? "Supplier is already active." : "Supplier is already inactive.",
                ServiceResultFailureType.Conflict);
        }

        var supplier = await _supplierStore.SetSupplierActiveStateAsync(
            id,
            isActive,
            cancellationToken);

        return supplier is null
            ? ServiceResult<SupplierDto>.Failure("Supplier active state could not be changed.")
            : ServiceResult<SupplierDto>.Success(
                supplier,
                isActive ? "Supplier activated." : "Supplier deactivated.");
    }

    private static ServiceResult<SupplierDto>? ValidateSupplierShape(string name, string? email)
    {
        if (string.IsNullOrWhiteSpace(name))
        {
            return ServiceResult<SupplierDto>.Failure("Supplier name is required.");
        }

        if (name.Trim().Length > MaxNameLength)
        {
            return ServiceResult<SupplierDto>.Failure($"Supplier name cannot exceed {MaxNameLength} characters.");
        }

        var normalizedEmail = NormalizeOptionalText(email);

        if (normalizedEmail is not null && !IsValidEmail(normalizedEmail))
        {
            return ServiceResult<SupplierDto>.Failure("Supplier email must be a valid email address.");
        }

        return null;
    }

    private static bool IsValidEmail(string email)
    {
        try
        {
            var address = new MailAddress(email);
            return string.Equals(address.Address, email, StringComparison.OrdinalIgnoreCase);
        }
        catch (FormatException)
        {
            return false;
        }
    }

    private static string? NormalizeOptionalText(string? value)
    {
        return string.IsNullOrWhiteSpace(value) ? null : value.Trim();
    }
}
