namespace MarketFlow.Application.Features.AI.Interfaces;

public interface IAiRuntimeInfo
{
    string Provider { get; }

    string Model { get; }
}
