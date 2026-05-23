namespace MarketFlow.Application.Features.Markets.DTOs;

public class UpdateMarketRequest
{
    public string Name { get; set; } = string.Empty;

    public string? City { get; set; }

    public string? Address { get; set; }

    public bool? IsActive { get; set; }
}
