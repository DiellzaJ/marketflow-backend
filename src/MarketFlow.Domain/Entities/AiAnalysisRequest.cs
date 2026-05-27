using MarketFlow.Domain.Enums;

namespace MarketFlow.Domain.Entities;

public class AiAnalysisRequest : TenantEntity
{
    public string AnalysisType { get; set; } = string.Empty;

    public string Prompt { get; set; } = string.Empty;

    public string RequestPayload { get; set; } = "{}";

    public AiAnalysisStatus Status { get; set; } = AiAnalysisStatus.Pending;

    public string? ErrorMessage { get; set; }

    public int? RequestedByUserId { get; set; }

    public ICollection<AiAnalysisResult> Results { get; set; } = new List<AiAnalysisResult>();
}
