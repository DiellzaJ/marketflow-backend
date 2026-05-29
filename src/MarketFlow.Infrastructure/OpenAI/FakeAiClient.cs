using MarketFlow.Application.Features.AI.DTOs;
using MarketFlow.Application.Features.AI.Interfaces;

namespace MarketFlow.Infrastructure.OpenAI;

public sealed class FakeAiClient : IAiClient
{
    private const string FakeModel = "fake-ai-development";

    public Task<AiCompletionResponseDto> GenerateTextAsync(
        AiCompletionRequestDto request,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(request);

        var text = CreateResponse(request);

        return Task.FromResult(new AiCompletionResponseDto
        {
            Text = text,
            Model = FakeModel
        });
    }

    private static string CreateResponse(AiCompletionRequestDto request)
    {
        var systemPrompt = request.SystemPrompt ?? string.Empty;
        var prompt = request.Prompt ?? string.Empty;

        if (Contains(systemPrompt, "allowed report type"))
        {
            return CreateReportIntentResponse(prompt);
        }

        if (Contains(systemPrompt, "dashboard"))
        {
            return """
                {"summary":"Fake development summary based on tenant-safe dashboard data.","recommendedActions":["Review the highlighted metrics.","Switch Ai:Provider to OpenAI or Ollama when you need real model responses."]}
                """;
        }

        if (Contains(systemPrompt, "supplier performance insights"))
        {
            return """{"recommendations":[{"supplierId":1,"recommendation":"Fake development recommendation: review supplier performance trends."}]}""";
        }

        if (Contains(systemPrompt, "anomalies"))
        {
            return """{"explanations":[{"index":0,"explanation":"Fake development explanation: this anomaly should be reviewed by an operator."}]}""";
        }

        if (Contains(systemPrompt, "inventory recommendations") ||
            Contains(systemPrompt, "purchase recommendations"))
        {
            return """{"explanations":[{"productId":1,"explanation":"Fake development explanation based on backend-calculated recommendation data."}]}""";
        }

        return "Fake development AI response based on tenant-safe backend data.";
    }

    private static string CreateReportIntentResponse(string prompt)
    {
        var normalized = prompt.ToLowerInvariant();
        var reportType =
            Contains(normalized, "low stock", "restock", "stockout") ? "LowStockProducts" :
            Contains(normalized, "market", "store", "location") ? "SalesByMarket" :
            Contains(normalized, "category", "categories") ? "SalesByCategory" :
            Contains(normalized, "supplier", "vendor") ? "SupplierPerformance" :
            Contains(normalized, "purchase", "purchases", "procurement") ? "PurchaseSummary" :
            Contains(normalized, "movement", "movements", "adjustment", "transfer") ? "InventoryMovements" :
            Contains(normalized, "daily", "today", "day by day") ? "DailySalesSummary" :
            Contains(normalized, "sold", "selling", "best", "top", "product") ? "TopSellingProducts" :
            "Unsupported";

        var confidence = reportType == "Unsupported" ? "0" : "0.99";
        return $$"""{"reportType":"{{reportType}}","confidence":{{confidence}}}""";
    }

    private static bool Contains(string value, params string[] needles)
    {
        return needles.Any(needle => value.Contains(needle, StringComparison.OrdinalIgnoreCase));
    }
}
