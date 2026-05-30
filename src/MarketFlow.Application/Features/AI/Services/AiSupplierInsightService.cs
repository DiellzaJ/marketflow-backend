using System.Text.Json;
using System.Text.Json.Serialization;
using MarketFlow.Application.Common.Interfaces;
using MarketFlow.Application.Common.Models;
using MarketFlow.Application.Features.AI.DTOs;
using MarketFlow.Application.Features.AI.Interfaces;

namespace MarketFlow.Application.Features.AI.Services;

public class AiSupplierInsightService(
    IAiSupplierInsightDataService supplierInsightDataService,
    IAiClient aiClient,
    IAiResultCache? aiResultCache = null,
    IAiRuntimeInfo? aiRuntimeInfo = null,
    ICurrentUserService? currentUserService = null) : IAiSupplierInsightService
{
    private static readonly TimeSpan CacheExpiration = TimeSpan.FromMinutes(30);

    private static readonly JsonSerializerOptions JsonOptions = new(JsonSerializerDefaults.Web)
    {
        DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull
    };

    public async Task<ServiceResult<IReadOnlyCollection<AiSupplierInsightDto>>> GenerateSupplierPerformanceInsightsAsync(
        AiSupplierInsightRequest request,
        CancellationToken cancellationToken = default)
    {
        if (request.From.HasValue && request.To.HasValue && request.From > request.To)
        {
            return ServiceResult<IReadOnlyCollection<AiSupplierInsightDto>>.Failure(
                "From date must be on or before to date.");
        }

        if (request.MarketId.HasValue && request.MarketId <= 0)
        {
            return ServiceResult<IReadOnlyCollection<AiSupplierInsightDto>>.Failure(
                "Market filter must be a positive value.");
        }

        var cacheKey = CreateCacheKey(request);
        if (cacheKey is not null && aiResultCache is not null)
        {
            var cachedInsights = await aiResultCache.GetAsync<IReadOnlyCollection<AiSupplierInsightDto>>(
                cacheKey,
                cancellationToken);

            if (cachedInsights is not null)
            {
                return ServiceResult<IReadOnlyCollection<AiSupplierInsightDto>>.Success(cachedInsights);
            }
        }

        var data = await supplierInsightDataService.GetAiSupplierInsightDataAsync(
            request,
            cancellationToken);
        var insights = data
            .Select(CreateInsight)
            .OrderBy(insight => GetReliabilityRank(insight.ReliabilityLevel))
            .ThenByDescending(insight => insight.TotalAmountSpent)
            .ThenBy(insight => insight.SupplierName)
            .ThenBy(insight => insight.SupplierId)
            .ToArray();

        if (insights.Length == 0)
        {
            return ServiceResult<IReadOnlyCollection<AiSupplierInsightDto>>.Success(insights);
        }

        var recommendations = await GenerateRecommendationMapAsync(insights, cancellationToken);

        foreach (var insight in insights)
        {
            insight.RecommendationText = recommendations.TryGetValue(insight.SupplierId, out var recommendation)
                ? recommendation
                : CreateFallbackRecommendation(insight);
        }

        if (cacheKey is not null && aiResultCache is not null)
        {
            await aiResultCache.SetAsync<IReadOnlyCollection<AiSupplierInsightDto>>(
                cacheKey,
                insights,
                CacheExpiration,
                cancellationToken);
        }

        return ServiceResult<IReadOnlyCollection<AiSupplierInsightDto>>.Success(insights);
    }

    private string? CreateCacheKey(AiSupplierInsightRequest request)
    {
        if (currentUserService?.CompanyId is not { } companyId ||
            aiRuntimeInfo is null)
        {
            return null;
        }

        return AiCacheKeys.Supplier(companyId, request, aiRuntimeInfo.Provider, aiRuntimeInfo.Model);
    }

    private static AiSupplierInsightDto CreateInsight(AiSupplierInsightDataDto data)
    {
        var cancellationRate = data.TotalPurchases == 0
            ? 0
            : data.CancelledPurchases / (decimal)data.TotalPurchases;
        var flags = new List<string>();

        if (data.CancelledPurchases >= 2 && cancellationRate >= 0.30m)
        {
            flags.Add("HighCancellationRate");
        }

        if (data.AverageDeliveryDays > 7)
        {
            flags.Add("SlowDelivery");
        }

        var reliabilityLevel = flags.Contains("HighCancellationRate") || data.AverageDeliveryDays > 14
            ? "Low"
            : flags.Count > 0
                ? "Medium"
                : "High";

        return new AiSupplierInsightDto
        {
            SupplierId = data.SupplierId,
            SupplierName = data.SupplierName,
            TotalPurchases = data.TotalPurchases,
            ReceivedPurchases = data.ReceivedPurchases,
            CancelledPurchases = data.CancelledPurchases,
            CancellationRate = cancellationRate,
            AverageDeliveryDays = data.AverageDeliveryDays,
            TotalAmountSpent = data.TotalAmountSpent,
            ReliabilityLevel = reliabilityLevel,
            Flags = flags
        };
    }

    private async Task<IReadOnlyDictionary<int, string>> GenerateRecommendationMapAsync(
        IReadOnlyCollection<AiSupplierInsightDto> insights,
        CancellationToken cancellationToken)
    {
        var completion = await aiClient.GenerateTextAsync(new AiCompletionRequestDto
        {
            SystemPrompt = """
                You explain supplier performance insights for retail operators.
                Use only the backend-calculated supplier metrics provided.
                Do not invent purchase counts, cancellation rates, delivery days, spend, reliability levels, or flags.
                Respond as compact JSON: {"recommendations":[{"supplierId":1,"recommendation":"..."}]}.
                """,
            Prompt = BuildPrompt(insights),
            Temperature = 0.2m
        }, cancellationToken);

        return ParseRecommendations(completion.Text);
    }

    private static string BuildPrompt(IReadOnlyCollection<AiSupplierInsightDto> insights)
    {
        var payload = insights.Select(insight => new
        {
            insight.SupplierId,
            insight.SupplierName,
            insight.TotalPurchases,
            insight.ReceivedPurchases,
            insight.CancelledPurchases,
            insight.CancellationRate,
            insight.AverageDeliveryDays,
            insight.TotalAmountSpent,
            insight.ReliabilityLevel,
            insight.Flags
        });

        return $"""
            Recommend how to manage each supplier in one concise sentence.
            Supplier metrics:
            {JsonSerializer.Serialize(payload, JsonOptions)}
            """;
    }

    private static IReadOnlyDictionary<int, string> ParseRecommendations(string text)
    {
        try
        {
            var payload = JsonSerializer.Deserialize<AiSupplierRecommendationPayload>(text, JsonOptions);

            return payload?.Recommendations
                .Where(recommendation => recommendation.SupplierId > 0 &&
                    !string.IsNullOrWhiteSpace(recommendation.Recommendation))
                .GroupBy(recommendation => recommendation.SupplierId)
                .ToDictionary(
                    group => group.Key,
                    group => group.First().Recommendation.Trim()) ??
                new Dictionary<int, string>();
        }
        catch (JsonException)
        {
            return new Dictionary<int, string>();
        }
    }

    private static string CreateFallbackRecommendation(AiSupplierInsightDto insight) =>
        insight.ReliabilityLevel switch
        {
            "Low" => "Review this supplier before placing more orders.",
            "Medium" => "Monitor this supplier's performance on future purchases.",
            _ => "Supplier performance is reliable based on current purchase history."
        };

    private static int GetReliabilityRank(string reliabilityLevel) =>
        reliabilityLevel switch
        {
            "Low" => 0,
            "Medium" => 1,
            "High" => 2,
            _ => 3
        };

    private sealed class AiSupplierRecommendationPayload
    {
        public IReadOnlyCollection<AiSupplierRecommendationDto> Recommendations { get; init; } = [];
    }

    private sealed class AiSupplierRecommendationDto
    {
        public int SupplierId { get; init; }

        public string Recommendation { get; init; } = string.Empty;
    }
}
