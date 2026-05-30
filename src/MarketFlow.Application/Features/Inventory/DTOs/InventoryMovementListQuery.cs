namespace MarketFlow.Application.Features.Inventory.DTOs;

public class InventoryMovementListQuery
{
    public int? ProductId { get; set; }

    public int? MarketId { get; set; }

    public int? DepartmentId { get; set; }

    public string? MovementType { get; set; }

    public DateTimeOffset? DateFrom { get; set; }

    public DateTimeOffset? DateTo { get; set; }

    public int Page { get; set; } = 1;

    public int PageSize { get; set; } = 20;
}
