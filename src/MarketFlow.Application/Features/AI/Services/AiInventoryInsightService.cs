using System.Text.Json;
using System.Text.Json.Serialization;
using MarketFlow.Application.Common.Interfaces;
using MarketFlow.Application.Common.Models;
using MarketFlow.Application.Features.AI.DTOs;
using MarketFlow.Application.Features.AI.Interfaces;

namespace MarketFlow.Application.Features.AI.Services;

public class AiInventoryInsightService(
    IAiInventoryInsightDataService insightDataService,
    IAiClient aiClient,
    IAiResultCache? aiResultCache = null,
    IAiRuntimeInfo? aiRuntimeInfo = null,
    ICurrentUserService? currentUserService = null) : IAiInventoryInsightService
{
    private const string LowStockIssue = "LowStock";
    private const string CriticalLowStockIssue = "CriticalLowStock";
    private const string OverstockIssue = "Overstock";
    private static readonly TimeSpan CacheExpiration = TimeSpan.FromMinutes(30);

    private static readonly JsonSerializerOptions JsonOptions = new(JsonSerializerDefaults.Web)
    {
        DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull
    };

    public async Task<ServiceResult<IReadOnlyCollection<AiInventoryRecommendationDto>>> GenerateInventoryRecommendationsAsync(
        AiInventoryRecommendationRequest request,
        CancellationToken cancellationToken = default)
    {
        if (request.SalesHistoryDays <= 0 ||
            request.TargetStockDays <= 0 ||
            request.OverstockDays <= 0)
        {
            return ServiceResult<IReadOnlyCollection<AiInventoryRecommendationDto>>.Failure(
                "Sales history days, target stock days, and overstock days must be positive values.");
        }

        if ((request.MarketId.HasValue && request.MarketId <= 0) ||
            (request.DepartmentId.HasValue && request.DepartmentId <= 0))
        {
            return ServiceResult<IReadOnlyCollection<AiInventoryRecommendationDto>>.Failure(
                "Market and department filters must be positive values.");
        }

        var normalizedRequest = new AiInventoryRecommendationRequest
        {
            SalesHistoryDays = Math.Clamp(request.SalesHistoryDays, 1, 365),
            TargetStockDays = Math.Clamp(request.TargetStockDays, 1, 365),
            OverstockDays = Math.Clamp(request.OverstockDays, 1, 365),
            MarketId = request.MarketId,
            DepartmentId = request.DepartmentId
        };

        var cacheKey = CreateCacheKey(normalizedRequest);
        if (cacheKey is not null && aiResultCache is not null)
        {
            var cachedRecommendations = await aiResultCache.GetAsync<IReadOnlyCollection<AiInventoryRecommendationDto>>(
                cacheKey,
                cancellationToken);

            if (cachedRecommendations is not null)
            {
                return ServiceResult<IReadOnlyCollection<AiInventoryRecommendationDto>>.Success(cachedRecommendations);
            }
        }

        var data = await insightDataService.GetAiInventoryInsightDataAsync(
            normalizedRequest,
            cancellationToken);
        var recommendations = data
            .Select(item => CreateRecommendation(item, normalizedRequest))
            .Where(recommendation => recommendation is not null)
            .Cast<AiInventoryRecommendationDto>()
            .OrderBy(recommendation => GetUrgencyRank(recommendation.UrgencyLevel))
            .ThenBy(recommendation => recommendation.ProductName)
            .ThenBy(recommendation => recommendation.ProductId)
            .ToArray();

        if (recommendations.Length == 0)
        {
            return ServiceResult<IReadOnlyCollection<AiInventoryRecommendationDto>>.Success(recommendations);
        }

        var explanations = await GenerateExplanationMapAsync(recommendations, cancellationToken);

        foreach (var recommendation in recommendations)
        {
            recommendation.Explanation = explanations.TryGetValue(recommendation.ProductId, out var explanation)
                ? explanation
                : recommendation.Reason;
        }

        if (cacheKey is not null && aiResultCache is not null)
        {
            await aiResultCache.SetAsync<IReadOnlyCollection<AiInventoryRecommendationDto>>(
                cacheKey,
                recommendations,
                CacheExpiration,
                cancellationToken);
        }

        return ServiceResult<IReadOnlyCollection<AiInventoryRecommendationDto>>.Success(recommendations);
    }

    private string? CreateCacheKey(AiInventoryRecommendationRequest request)
    {
        if (currentUserService?.CompanyId is not { } companyId ||
            aiRuntimeInfo is null)
        {
            return null;
        }

        return AiCacheKeys.Inventory(companyId, request, aiRuntimeInfo.Provider, aiRuntimeInfo.Model);
    }

    private static AiInventoryRecommendationDto? CreateRecommendation(
        AiInventoryInsightDataDto item,
        AiInventoryRecommendationRequest request)
    {
        var averageDailySales = item.TotalQuantitySold <= 0
            ? 0
            : item.TotalQuantitySold / (decimal)request.SalesHistoryDays;
        var currentStock = item.CurrentStock;
        var minimumStockAlert = item.MinimumStockAlert;

        if (currentStock <= minimumStockAlert)
        {
            var criticalThreshold = Math.Max(1, (int)Math.Ceiling(minimumStockAlert / 2m));
            var isCritical = currentStock <= criticalThreshold;
            var targetStock = Math.Max(
                minimumStockAlert,
                (int)Math.Ceiling(averageDailySales * request.TargetStockDays));
            var recommendedPurchaseQuantity = Math.Max(targetStock - currentStock, 0);

            return new AiInventoryRecommendationDto
            {
                ProductId = item.ProductId,
                ProductName = item.ProductName,
                CurrentStock = currentStock,
                MinimumStockAlert = minimumStockAlert,
                TotalQuantitySold = item.TotalQuantitySold,
                AverageDailySales = averageDailySales,
                IssueType = isCritical ? CriticalLowStockIssue : LowStockIssue,
                UrgencyLevel = isCritical ? "Critical" : "High",
                RecommendedPurchaseQuantity = recommendedPurchaseQuantity,
                Reason = isCritical
                    ? "Current stock is at or below half of the minimum stock alert."
                    : "Current stock is at or below the minimum stock alert."
            };
        }

        var daysOfStockRemaining = averageDailySales == 0
            ? (decimal?)null
            : currentStock / averageDailySales;
        var noSalesOverstock = averageDailySales == 0 && currentStock > minimumStockAlert;
        var velocityOverstock = daysOfStockRemaining > request.OverstockDays;

        if (!noSalesOverstock && !velocityOverstock)
        {
            return null;
        }

        return new AiInventoryRecommendationDto
        {
            ProductId = item.ProductId,
            ProductName = item.ProductName,
            CurrentStock = currentStock,
            MinimumStockAlert = minimumStockAlert,
            TotalQuantitySold = item.TotalQuantitySold,
            AverageDailySales = averageDailySales,
            IssueType = OverstockIssue,
            UrgencyLevel = "Medium",
            RecommendedPurchaseQuantity = 0,
            Reason = noSalesOverstock
                ? "Product has stock on hand but no sales in the selected sales history window."
                : $"Current stock covers more than {request.OverstockDays} days based on recent sales history."
        };
    }

    private async Task<IReadOnlyDictionary<int, string>> GenerateExplanationMapAsync(
        IReadOnlyCollection<AiInventoryRecommendationDto> recommendations,
        CancellationToken cancellationToken)
    {
        var completion = await aiClient.GenerateTextAsync(new AiCompletionRequestDto
        {
            SystemPrompt = """
                You explain inventory recommendations for retail operators.
                Use only the backend-calculated recommendation data provided.
                Do not change issue type, urgency, quantity, stock, or sales calculations.
                Respond as compact JSON: {"explanations":[{"productId":1,"explanation":"..."}]}.
                """,
            Prompt = BuildPrompt(recommendations),
            Temperature = 0.2m
        }, cancellationToken);

        return ParseExplanations(completion.Text);
    }

    private static string BuildPrompt(IReadOnlyCollection<AiInventoryRecommendationDto> recommendations)
    {
        var payload = recommendations.Select(recommendation => new
        {
            recommendation.ProductId,
            recommendation.ProductName,
            recommendation.IssueType,
            recommendation.UrgencyLevel,
            recommendation.CurrentStock,
            recommendation.MinimumStockAlert,
            recommendation.TotalQuantitySold,
            recommendation.AverageDailySales,
            recommendation.RecommendedPurchaseQuantity,
            recommendation.Reason
        });

        return $"""
            Explain each backend-generated inventory recommendation in one concise sentence.
            Recommendations:
            {JsonSerializer.Serialize(payload, JsonOptions)}
            """;
    }

    private static IReadOnlyDictionary<int, string> ParseExplanations(string text)
    {
        try
        {
            var payload = JsonSerializer.Deserialize<AiRecommendationExplanationPayload>(text, JsonOptions);

            return payload?.Explanations
                .Where(explanation => explanation.ProductId > 0 && !string.IsNullOrWhiteSpace(explanation.Explanation))
                .GroupBy(explanation => explanation.ProductId)
                .ToDictionary(
                    group => group.Key,
                    group => group.First().Explanation.Trim()) ??
                new Dictionary<int, string>();
        }
        catch (JsonException)
        {
            return new Dictionary<int, string>();
        }
    }

    private static int GetUrgencyRank(string urgencyLevel) =>
        urgencyLevel switch
        {
            "Critical" => 0,
            "High" => 1,
            "Medium" => 2,
            _ => 3
        };

    private sealed class AiRecommendationExplanationPayload
    {
        public IReadOnlyCollection<AiRecommendationExplanationDto> Explanations { get; init; } = [];
    }

    private sealed class AiRecommendationExplanationDto
    {
        public int ProductId { get; init; }

        public string Explanation { get; init; } = string.Empty;
    }
}
