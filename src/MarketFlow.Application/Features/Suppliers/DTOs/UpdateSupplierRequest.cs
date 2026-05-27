namespace MarketFlow.Application.Features.Suppliers.DTOs;

public class UpdateSupplierRequest
{
    public string Name { get; set; } = string.Empty;

    public string? Phone { get; set; }

    public string? Email { get; set; }

    public string? Address { get; set; }

    public bool? IsActive { get; set; }
}
