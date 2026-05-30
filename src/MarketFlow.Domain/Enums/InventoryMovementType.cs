namespace MarketFlow.Domain.Enums;

public enum InventoryMovementType
{
    InitialStock = 1,
    ManualAdjustment = 2,
    PurchaseReceived = 3,
    SaleCompleted = 4,
    TransferOut = 5,
    TransferIn = 6
}
