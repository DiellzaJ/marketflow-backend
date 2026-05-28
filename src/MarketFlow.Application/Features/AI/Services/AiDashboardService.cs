using System.Text.Json;
using System.Text.Json.Serialization;
using MarketFlow.Application.Common.Models;
using MarketFlow.Application.Features.AI.DTOs;
using MarketFlow.Application.Features.AI.Interfaces;

namespace MarketFlow.Application.Features.AI.Services;

public class AiDashboardService(
    IAiBusinessDataService businessDataService,
    IOpenAiClient openAiClient) : IAiDashboardService
{
    private static readonly JsonSerializerOptions JsonOptions = new(JsonSerializerDefaults.Web)
    {
        DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull
    };

    public async Task<ServiceResult<AiDashboardSummaryResponse>> GenerateDashboardSummaryAsync(
        AiDashboardSummaryRequest request,
        CancellationToken cancellationToken = default)
    {
        if (request.From.HasValue && request.To.HasValue && request.From > request.To)
        {
            return ServiceResult<AiDashboardSummaryResponse>.Failure("From date must be on or before to date.");
        }

        if ((request.MarketId.HasValue && request.MarketId <= 0) ||
            (request.DepartmentId.HasValue && request.DepartmentId <= 0))
        {
            return ServiceResult<AiDashboardSummaryResponse>.Failure(
                "Market and department filters must be positive values.");
        }

        var dataResult = await businessDataService.GetBusinessDataAsync(new AiBusinessDataQuery
        {
            From = request.From,
            To = request.To,
            MarketId = request.MarketId,
            DepartmentId = request.DepartmentId,
            TopProductsLimit = Math.Clamp(request.TopProductsLimit, 1, 20),
            LowStockLimit = Math.Clamp(request.LowStockLimit, 1, 25)
        }, cancellationToken);

        if (!dataResult.Succeeded || dataResult.Data is null)
        {
            return ServiceResult<AiDashboardSummaryResponse>.Failure(dataResult.Message, dataResult.FailureType);
        }

        var kpis = CreateKpis(dataResult.Data);
        var completion = await openAiClient.GenerateTextAsync(new AiCompletionRequestDto
        {
            SystemPrompt = """
                You are a retail business analyst. Write simple, direct business language for a company dashboard.
                Use only the KPI data provided. Do not infer private customer details, raw orders, or data that is not present.
                Respond as compact JSON with this shape: {"summary":"...","recommendedActions":["..."]}.
                """,
            Prompt = BuildPrompt(dataResult.Data, kpis),
            Temperature = 0.2m
        }, cancellationToken);

        var aiSummary = ParseAiSummary(completion.Text);

        return ServiceResult<AiDashboardSummaryResponse>.Success(new AiDashboardSummaryResponse
        {
            Kpis = kpis,
            Summary = aiSummary.Summary,
            RecommendedActions = aiSummary.RecommendedActions,
            Model = completion.Model
        });
    }

    private static AiDashboardKpisDto CreateKpis(AiBusinessDataDto data)
    {
        return new AiDashboardKpisDto
        {
            TotalSales = data.SalesMetrics.TotalRevenue,
            TotalOrders = data.SalesMetrics.TotalSales,
            AverageOrderValue = data.SalesMetrics.AverageSaleAmount,
            TotalItemsSold = data.SalesMetrics.TotalItemsSold,
            TopSellingProducts = data.TopSellingProducts,
            LowStockWarnings = data.LowStockProducts
        };
    }

    private static string BuildPrompt(AiBusinessDataDto data, AiDashboardKpisDto kpis)
    {
        var promptPayload = new
        {
            data.Filters,
            Kpis = new
            {
                kpis.TotalSales,
                kpis.TotalOrders,
                kpis.AverageOrderValue,
                kpis.TotalItemsSold
            },
            TopSellingProducts = kpis.TopSellingProducts.Select(product => new
            {
                product.ProductName,
                product.QuantitySold,
                product.Revenue
            }),
            LowStockWarnings = kpis.LowStockWarnings.Select(product => new
            {
                product.ProductName,
                product.MarketName,
                product.DepartmentName,
                product.AvailableQuantity,
                product.MinimumStockAlert,
                product.SuggestedRestockQuantity
            })
        };

        return $"""
            Summarize this aggregated company dashboard data and recommend 3 to 5 practical actions.
            Aggregated dashboard data:
            {JsonSerializer.Serialize(promptPayload, JsonOptions)}
            """;
    }

    private static AiSummaryPayload ParseAiSummary(string text)
    {
        try
        {
            var payload = JsonSerializer.Deserialize<AiSummaryPayload>(text, JsonOptions);

            if (!string.IsNullOrWhiteSpace(payload?.Summary))
            {
                return new AiSummaryPayload
                {
                    Summary = payload.Summary.Trim(),
                    RecommendedActions = payload.RecommendedActions
                        .Where(action => !string.IsNullOrWhiteSpace(action))
                        .Select(action => action.Trim())
                        .ToArray()
                };
            }
        }
        catch (JsonException)
        {
        }

        return new AiSummaryPayload
        {
            Summary = text.Trim(),
            RecommendedActions = []
        };
    }

    private sealed class AiSummaryPayload
    {
        public string Summary { get; init; } = string.Empty;

        public IReadOnlyCollection<string> RecommendedActions { get; init; } = [];
    }
}
