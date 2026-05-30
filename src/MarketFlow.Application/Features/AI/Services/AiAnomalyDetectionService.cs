using System.Text.Json;
using System.Text.Json.Serialization;
using MarketFlow.Application.Common.Models;
using MarketFlow.Application.Features.AI.DTOs;
using MarketFlow.Application.Features.AI.Interfaces;

namespace MarketFlow.Application.Features.AI.Services;

public class AiAnomalyDetectionService(
    IAiAnomalyDetectionDataService anomalyDetectionDataService,
    IAiClient aiClient) : IAiAnomalyDetectionService
{
    private static readonly JsonSerializerOptions JsonOptions = new(JsonSerializerDefaults.Web)
    {
        DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull
    };

    public async Task<ServiceResult<IReadOnlyCollection<AiAnomalyDto>>> DetectAnomaliesAsync(
        AiAnomalyDetectionRequest request,
        CancellationToken cancellationToken = default)
    {
        if (request.From.HasValue && request.To.HasValue && request.From > request.To)
        {
            return ServiceResult<IReadOnlyCollection<AiAnomalyDto>>.Failure(
                "From date must be on or before to date.");
        }

        if ((request.MarketId.HasValue && request.MarketId <= 0) ||
            (request.DepartmentId.HasValue && request.DepartmentId <= 0))
        {
            return ServiceResult<IReadOnlyCollection<AiAnomalyDto>>.Failure(
                "Market and department filters must be positive values.");
        }

        if (request.HighDiscountRateThreshold <= 0 ||
            request.LargeStockMovementThreshold <= 0 ||
            request.SalesSpikeMultiplier <= 1 ||
            request.MinimumSalesSpikeQuantity <= 0)
        {
            return ServiceResult<IReadOnlyCollection<AiAnomalyDto>>.Failure(
                "Anomaly detection thresholds must be positive values.");
        }

        var normalizedRequest = new AiAnomalyDetectionRequest
        {
            From = request.From,
            To = request.To,
            MarketId = request.MarketId,
            DepartmentId = request.DepartmentId,
            HighDiscountRateThreshold = Math.Clamp(request.HighDiscountRateThreshold, 0.01m, 0.95m),
            LargeStockMovementThreshold = Math.Clamp(request.LargeStockMovementThreshold, 1, 100000),
            SalesSpikeMultiplier = Math.Clamp(request.SalesSpikeMultiplier, 1.01m, 100m),
            MinimumSalesSpikeQuantity = Math.Clamp(request.MinimumSalesSpikeQuantity, 1, 100000)
        };

        var data = await anomalyDetectionDataService.GetAiAnomalyDetectionDataAsync(
            normalizedRequest,
            cancellationToken);
        var anomalies = DetectAnomalies(data, normalizedRequest)
            .OrderByDescending(anomaly => GetSeverityRank(anomaly.Severity))
            .ThenByDescending(anomaly => anomaly.OccurredAt)
            .ThenBy(anomaly => anomaly.AnomalyType)
            .ThenBy(anomaly => anomaly.EntityId)
            .ToArray();

        if (anomalies.Length == 0)
        {
            return ServiceResult<IReadOnlyCollection<AiAnomalyDto>>.Success(anomalies);
        }

        var explanations = await GenerateExplanationMapAsync(anomalies, cancellationToken);

        for (var index = 0; index < anomalies.Length; index++)
        {
            anomalies[index].Explanation = explanations.TryGetValue(index, out var explanation)
                ? SanitizeExplanation(explanation)
                : anomalies[index].Reason;
        }

        return ServiceResult<IReadOnlyCollection<AiAnomalyDto>>.Success(anomalies);
    }

    private static IReadOnlyCollection<AiAnomalyDto> DetectAnomalies(
        AiAnomalyDetectionDataDto data,
        AiAnomalyDetectionRequest request)
    {
        var anomalies = new List<AiAnomalyDto>();
        anomalies.AddRange(DetectHighDiscounts(data.Sales, request));
        anomalies.AddRange(DetectBelowCostSales(data.SaleItems));
        anomalies.AddRange(DetectLargeStockMovements(data.StockMovements, request));
        anomalies.AddRange(DetectUnmatchedStockMovements(data.StockMovements));
        anomalies.AddRange(DetectSalesSpikes(data.DailyProductSales, request));
        return anomalies;
    }

    private static IEnumerable<AiAnomalyDto> DetectHighDiscounts(
        IEnumerable<AiAnomalySaleDataDto> sales,
        AiAnomalyDetectionRequest request)
    {
        foreach (var sale in sales)
        {
            var grossAmount = sale.TotalAmount + sale.DiscountAmount;

            if (grossAmount <= 0)
            {
                continue;
            }

            var discountRate = sale.DiscountAmount / grossAmount;

            if (discountRate < request.HighDiscountRateThreshold)
            {
                continue;
            }

            yield return new AiAnomalyDto
            {
                AnomalyType = "HighDiscount",
                Severity = discountRate >= 0.50m ? "High" : "Medium",
                EntityType = "Sale",
                EntityId = sale.SaleId,
                ReferenceNumber = sale.ReferenceNumber,
                OccurredAt = sale.SaleDate.ToDateTime(TimeOnly.MinValue, DateTimeKind.Utc),
                MetricValue = discountRate,
                Threshold = request.HighDiscountRateThreshold,
                Reason = "Sale discount rate is unusually high and needs review."
            };
        }
    }

    private static IEnumerable<AiAnomalyDto> DetectBelowCostSales(IEnumerable<AiAnomalySaleItemDataDto> saleItems)
    {
        foreach (var item in saleItems.Where(item => item.UnitPrice < item.CostPrice))
        {
            yield return new AiAnomalyDto
            {
                AnomalyType = "BelowCostSale",
                Severity = "High",
                EntityType = "Sale",
                EntityId = item.SaleId,
                ReferenceNumber = item.ReferenceNumber,
                ProductId = item.ProductId,
                ProductName = item.ProductName,
                OccurredAt = item.SaleDate.ToDateTime(TimeOnly.MinValue, DateTimeKind.Utc),
                MetricValue = item.UnitPrice,
                Threshold = item.CostPrice,
                Reason = "Sale item unit price is below product cost price and needs review."
            };
        }
    }

    private static IEnumerable<AiAnomalyDto> DetectLargeStockMovements(
        IEnumerable<AiAnomalyStockMovementDataDto> movements,
        AiAnomalyDetectionRequest request)
    {
        foreach (var movement in movements.Where(movement =>
            string.Equals(movement.MovementType, "ManualAdjustment", StringComparison.OrdinalIgnoreCase) &&
            Math.Abs(movement.QuantityChanged) >= request.LargeStockMovementThreshold))
        {
            yield return new AiAnomalyDto
            {
                AnomalyType = "LargeStockAdjustment",
                Severity = Math.Abs(movement.QuantityChanged) >= request.LargeStockMovementThreshold * 2 ? "High" : "Medium",
                EntityType = "InventoryMovement",
                EntityId = movement.MovementId,
                ReferenceNumber = movement.ReferenceNumber,
                ProductId = movement.ProductId,
                ProductName = movement.ProductName,
                OccurredAt = movement.CreatedAt,
                MetricValue = Math.Abs(movement.QuantityChanged),
                Threshold = request.LargeStockMovementThreshold,
                Reason = "Manual stock adjustment quantity is unusually large and needs review."
            };
        }
    }

    private static IEnumerable<AiAnomalyDto> DetectUnmatchedStockMovements(
        IEnumerable<AiAnomalyStockMovementDataDto> movements)
    {
        foreach (var movement in movements.Where(movement =>
            !movement.HasMatchingReference &&
            (string.Equals(movement.MovementType, "SaleCompleted", StringComparison.OrdinalIgnoreCase) ||
                string.Equals(movement.MovementType, "PurchaseReceived", StringComparison.OrdinalIgnoreCase))))
        {
            yield return new AiAnomalyDto
            {
                AnomalyType = "UnmatchedStockMovement",
                Severity = "High",
                EntityType = "InventoryMovement",
                EntityId = movement.MovementId,
                ReferenceNumber = movement.ReferenceNumber,
                ProductId = movement.ProductId,
                ProductName = movement.ProductName,
                OccurredAt = movement.CreatedAt,
                MetricValue = Math.Abs(movement.QuantityChanged),
                Threshold = 1,
                Reason = "Stock movement does not match an existing sale or purchase reference and needs review."
            };
        }
    }

    private static IEnumerable<AiAnomalyDto> DetectSalesSpikes(
        IEnumerable<AiAnomalyDailyProductSalesDataDto> dailySales,
        AiAnomalyDetectionRequest request)
    {
        foreach (var dailySale in dailySales.Where(dailySale =>
            dailySale.AverageDailyQuantity > 0 &&
            dailySale.QuantitySold >= request.MinimumSalesSpikeQuantity &&
            dailySale.QuantitySold >= dailySale.AverageDailyQuantity * request.SalesSpikeMultiplier))
        {
            yield return new AiAnomalyDto
            {
                AnomalyType = "SalesSpike",
                Severity = dailySale.QuantitySold >= dailySale.AverageDailyQuantity * request.SalesSpikeMultiplier * 2
                    ? "High"
                    : "Medium",
                EntityType = "ProductDailySales",
                EntityId = dailySale.ProductId,
                ProductId = dailySale.ProductId,
                ProductName = dailySale.ProductName,
                OccurredAt = dailySale.SaleDate.ToDateTime(TimeOnly.MinValue, DateTimeKind.Utc),
                MetricValue = dailySale.QuantitySold,
                Threshold = dailySale.AverageDailyQuantity * request.SalesSpikeMultiplier,
                Reason = "Product daily sales are unusually high compared with recent average demand and need review."
            };
        }
    }

    private async Task<IReadOnlyDictionary<int, string>> GenerateExplanationMapAsync(
        IReadOnlyCollection<AiAnomalyDto> anomalies,
        CancellationToken cancellationToken)
    {
        var completion = await aiClient.GenerateTextAsync(new AiCompletionRequestDto
        {
            SystemPrompt = """
                You explain retail sales and stock anomalies for operations teams.
                Use only the backend-calculated anomaly data provided.
                Do not accuse users of fraud, theft, dishonesty, or wrongdoing.
                Say anomalies need review, investigation, or follow-up.
                Respond as compact JSON: {"explanations":[{"index":0,"explanation":"..."}]}.
                """,
            Prompt = BuildPrompt(anomalies),
            Temperature = 0.2m
        }, cancellationToken);

        return ParseExplanations(completion.Text);
    }

    private static string BuildPrompt(IReadOnlyCollection<AiAnomalyDto> anomalies)
    {
        var payload = anomalies.Select((anomaly, index) => new
        {
            Index = index,
            anomaly.AnomalyType,
            anomaly.Severity,
            anomaly.EntityType,
            anomaly.EntityId,
            anomaly.ReferenceNumber,
            anomaly.ProductId,
            anomaly.ProductName,
            anomaly.OccurredAt,
            anomaly.MetricValue,
            anomaly.Threshold,
            anomaly.Reason
        });

        return $"""
            Explain why each backend-detected anomaly needs review in one concise sentence.
            Anomalies:
            {JsonSerializer.Serialize(payload, JsonOptions)}
            """;
    }

    private static IReadOnlyDictionary<int, string> ParseExplanations(string text)
    {
        try
        {
            var payload = JsonSerializer.Deserialize<AiAnomalyExplanationPayload>(text, JsonOptions);

            return payload?.Explanations
                .Where(explanation => explanation.Index >= 0 && !string.IsNullOrWhiteSpace(explanation.Explanation))
                .GroupBy(explanation => explanation.Index)
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

    private static string SanitizeExplanation(string explanation)
    {
        var sanitized = explanation
            .Replace("fraud", "issue", StringComparison.OrdinalIgnoreCase)
            .Replace("fraudulent", "unusual", StringComparison.OrdinalIgnoreCase)
            .Replace("theft", "stock issue", StringComparison.OrdinalIgnoreCase)
            .Replace("stolen", "unaccounted", StringComparison.OrdinalIgnoreCase)
            .Replace("stealing", "unaccounted movement", StringComparison.OrdinalIgnoreCase);

        return sanitized.Contains("review", StringComparison.OrdinalIgnoreCase)
            ? sanitized
            : $"{sanitized} Needs review.";
    }

    private static int GetSeverityRank(string severity) =>
        severity switch
        {
            "High" => 2,
            "Medium" => 1,
            _ => 0
        };

    private sealed class AiAnomalyExplanationPayload
    {
        public IReadOnlyCollection<AiAnomalyExplanationDto> Explanations { get; init; } = [];
    }

    private sealed class AiAnomalyExplanationDto
    {
        public int Index { get; init; }

        public string Explanation { get; init; } = string.Empty;
    }
}
