namespace MarketFlow.Application.Features.Inventory.DTOs;

public class InventoryMovementDto
{
    public int Id { get; set; }

    public int InventoryId { get; set; }

    public int ProductId { get; set; }

    public string ProductName { get; set; } = string.Empty;

    public int MarketId { get; set; }

    public string MarketName { get; set; } = string.Empty;

    public int? DepartmentId { get; set; }

    public string MovementType { get; set; } = string.Empty;

    public int QuantityChanged { get; set; }

    public string? ReferenceNumber { get; set; }

    public int? CreatedByUserId { get; set; }

    public DateTimeOffset CreatedAt { get; set; }
}
