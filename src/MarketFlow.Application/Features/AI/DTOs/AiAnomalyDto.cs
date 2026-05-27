namespace MarketFlow.Application.Features.AI.DTOs;

public class AiAnomalyDto
{
    public string AnomalyType { get; set; } = string.Empty;

    public string Severity { get; set; } = string.Empty;

    public string EntityType { get; set; } = string.Empty;

    public int EntityId { get; set; }

    public string? ReferenceNumber { get; set; }

    public int? ProductId { get; set; }

    public string? ProductName { get; set; }

    public DateTimeOffset OccurredAt { get; set; }

    public decimal MetricValue { get; set; }

    public decimal Threshold { get; set; }

    public string Reason { get; set; } = string.Empty;

    public string Explanation { get; set; } = string.Empty;
}
