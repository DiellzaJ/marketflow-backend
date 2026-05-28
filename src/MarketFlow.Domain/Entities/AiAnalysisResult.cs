namespace MarketFlow.Domain.Entities;

public class AiAnalysisResult : TenantEntity
{
    public Guid AiAnalysisRequestId { get; set; }

    public AiAnalysisRequest? Request { get; set; }

    public string Model { get; set; } = string.Empty;

    public string ResultPayload { get; set; } = "{}";

    public string? Summary { get; set; }

    public int? PromptTokens { get; set; }

    public int? CompletionTokens { get; set; }
}
