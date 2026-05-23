namespace MarketFlow.Application.Features.Markets.DTOs;

public class CreateMarketRequest
{
    public string Name { get; set; } = string.Empty;

    public string? City { get; set; }

    public string? Address { get; set; }
}
