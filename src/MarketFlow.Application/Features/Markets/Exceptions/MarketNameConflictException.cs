namespace MarketFlow.Application.Features.Markets.Exceptions;

public sealed class MarketNameConflictException : Exception
{
    public MarketNameConflictException()
        : base("Market name is already used by another market.")
    {
    }
}
