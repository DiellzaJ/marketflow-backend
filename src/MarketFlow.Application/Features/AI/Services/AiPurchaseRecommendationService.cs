using System.Text.Json;
using System.Text.Json.Serialization;
using MarketFlow.Application.Common.Models;
using MarketFlow.Application.Features.AI.DTOs;
using MarketFlow.Application.Features.AI.Interfaces;

namespace MarketFlow.Application.Features.AI.Services;

public class AiPurchaseRecommendationService(
    IAiPurchaseRecommendationDataService recommendationDataService,
    IOpenAiClient openAiClient) : IAiPurchaseRecommendationService
{
    private static readonly JsonSerializerOptions JsonOptions = new(JsonSerializerDefaults.Web)
    {
        DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull
    };

    public async Task<ServiceResult<IReadOnlyCollection<AiPurchaseRecommendationDto>>> GeneratePurchaseRecommendationsAsync(
        AiPurchaseRecommendationRequest request,
        CancellationToken cancellationToken = default)
    {
        if (request.SalesHistoryDays <= 0 || request.TargetStockDays <= 0)
        {
            return ServiceResult<IReadOnlyCollection<AiPurchaseRecommendationDto>>.Failure(
                "Sales history days and target stock days must be positive values.");
        }

        if ((request.MarketId.HasValue && request.MarketId <= 0) ||
            (request.DepartmentId.HasValue && request.DepartmentId <= 0))
        {
            return ServiceResult<IReadOnlyCollection<AiPurchaseRecommendationDto>>.Failure(
                "Market and department filters must be positive values.");
        }

        var normalizedRequest = new AiPurchaseRecommendationRequest
        {
            SalesHistoryDays = Math.Clamp(request.SalesHistoryDays, 1, 365),
            TargetStockDays = Math.Clamp(request.TargetStockDays, 1, 365),
            MarketId = request.MarketId,
            DepartmentId = request.DepartmentId
        };

        var data = await recommendationDataService.GetAiPurchaseRecommendationDataAsync(
            normalizedRequest,
            cancellationToken);
        var recommendations = data
            .Select(item => CreateRecommendation(item, normalizedRequest))
            .Where(recommendation => recommendation.RecommendedPurchaseQuantity > 0)
            .OrderByDescending(recommendation => recommendation.RecommendedPurchaseQuantity)
            .ThenBy(recommendation => recommendation.ProductName)
            .ThenBy(recommendation => recommendation.ProductId)
            .ToArray();

        if (recommendations.Length == 0)
        {
            return ServiceResult<IReadOnlyCollection<AiPurchaseRecommendationDto>>.Success(recommendations);
        }

        var explanations = await GenerateExplanationMapAsync(recommendations, cancellationToken);

        foreach (var recommendation in recommendations)
        {
            recommendation.Explanation = explanations.TryGetValue(recommendation.ProductId, out var explanation)
                ? explanation
                : recommendation.Reason;
        }

        return ServiceResult<IReadOnlyCollection<AiPurchaseRecommendationDto>>.Success(recommendations);
    }

    private static AiPurchaseRecommendationDto CreateRecommendation(
        AiPurchaseRecommendationDataDto item,
        AiPurchaseRecommendationRequest request)
    {
        var forecast = AiInventoryForecastCalculator.Calculate(
            new AiInventoryForecastDataDto
            {
                ProductId = item.ProductId,
                ProductName = item.ProductName,
                CurrentStock = item.CurrentStock,
                TotalQuantitySold = item.TotalQuantitySold
            },
            request.SalesHistoryDays,
            request.TargetStockDays);
        var effectiveStock = item.CurrentStock + item.PendingPurchaseQuantity;
        var recommendedPurchaseQuantity = Math.Max(
            (int)Math.Ceiling(forecast.ForecastDemand) - effectiveStock,
            0);

        return new AiPurchaseRecommendationDto
        {
            ProductId = item.ProductId,
            ProductName = item.ProductName,
            CurrentStock = item.CurrentStock,
            TotalQuantitySold = item.TotalQuantitySold,
            AverageDailySales = forecast.AverageDailySales,
            ForecastDemand = forecast.ForecastDemand,
            PendingPurchaseQuantity = item.PendingPurchaseQuantity,
            RecommendedPurchaseQuantity = recommendedPurchaseQuantity,
            PreferredSupplierId = item.PreferredSupplierId,
            PreferredSupplierName = item.PreferredSupplierName,
            Reason = recommendedPurchaseQuantity == 0
                ? "Current stock and pending purchases cover forecast demand."
                : "Forecast demand is greater than current stock plus pending purchases."
        };
    }

    private async Task<IReadOnlyDictionary<int, string>> GenerateExplanationMapAsync(
        IReadOnlyCollection<AiPurchaseRecommendationDto> recommendations,
        CancellationToken cancellationToken)
    {
        var completion = await openAiClient.GenerateTextAsync(new AiCompletionRequestDto
        {
            SystemPrompt = """
                You explain purchase recommendations for retail operators.
                Use only the backend-calculated recommendation data provided.
                Do not change recommended quantities, suppliers, stock, pending purchases, or demand calculations.
                Respond as compact JSON: {"explanations":[{"productId":1,"explanation":"..."}]}.
                """,
            Prompt = BuildPrompt(recommendations),
            Temperature = 0.2m
        }, cancellationToken);

        return ParseExplanations(completion.Text);
    }

    private static string BuildPrompt(IReadOnlyCollection<AiPurchaseRecommendationDto> recommendations)
    {
        var payload = recommendations.Select(recommendation => new
        {
            recommendation.ProductId,
            recommendation.ProductName,
            recommendation.CurrentStock,
            recommendation.PendingPurchaseQuantity,
            recommendation.TotalQuantitySold,
            recommendation.AverageDailySales,
            recommendation.ForecastDemand,
            recommendation.RecommendedPurchaseQuantity,
            recommendation.PreferredSupplierName,
            recommendation.Reason
        });

        return $"""
            Explain each backend-generated purchase recommendation in one concise sentence.
            Recommendations:
            {JsonSerializer.Serialize(payload, JsonOptions)}
            """;
    }

    private static IReadOnlyDictionary<int, string> ParseExplanations(string text)
    {
        try
        {
            var payload = JsonSerializer.Deserialize<AiPurchaseRecommendationExplanationPayload>(text, JsonOptions);

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

    private sealed class AiPurchaseRecommendationExplanationPayload
    {
        public IReadOnlyCollection<AiPurchaseRecommendationExplanationDto> Explanations { get; init; } = [];
    }

    private sealed class AiPurchaseRecommendationExplanationDto
    {
        public int ProductId { get; init; }

        public string Explanation { get; init; } = string.Empty;
    }
}
