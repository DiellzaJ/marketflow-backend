namespace MarketFlow.Application.Common.Exceptions;

public sealed class SaleReferenceNumberRepairException : Exception
{
    public string SchemaName { get; }

    public SaleReferenceNumberRepairException(
        string schemaName,
        string message,
        Exception? innerException = null)
        : base(message, innerException)
    {
        SchemaName = schemaName;
    }
}
