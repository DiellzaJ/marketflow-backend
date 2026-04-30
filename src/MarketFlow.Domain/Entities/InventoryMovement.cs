using MarketFlow.Domain.Enums;

namespace MarketFlow.Domain.Entities;

public class InventoryMovement : TenantEntity
{
    public Guid InventoryId { get; set; }

    public Inventory? Inventory { get; set; }

    public InventoryMovementType MovementType { get; set; }

    public int QuantityChanged { get; set; }

    public string ReferenceNumber { get; set; } = string.Empty;
}
