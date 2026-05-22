namespace MarketFlow.Application.Features.Inventory.DTOs;

public class TransferInventoryRequest
{
    public int ProductId { get; set; }

    public int FromMarketId { get; set; }

    public int? FromDepartmentId { get; set; }

    public int ToMarketId { get; set; }

    public int? ToDepartmentId { get; set; }

    public int Quantity { get; set; }

    public string? Note { get; set; }
}
