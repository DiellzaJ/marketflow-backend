namespace MarketFlow.Application.Features.Products.Exceptions;

public sealed class ProductBarcodeConflictException : Exception
{
    public ProductBarcodeConflictException()
        : base("Barcode is already used by another product.")
    {
    }
}
