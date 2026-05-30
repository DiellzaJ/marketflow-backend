namespace MarketFlow.Application.Features.Inventory.DTOs;

public class PosProductLookupQuery
{
    public int? MarketId { get; set; }

    public string? Barcode { get; set; }

    public string? Search { get; set; }

    public int Limit { get; set; } = 10;
}
