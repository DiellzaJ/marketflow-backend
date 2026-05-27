using System.Text.Json;
using System.Text.Json.Serialization;
using MarketFlow.Application.Common.Models;
using MarketFlow.Application.Features.AI.DTOs;
using MarketFlow.Application.Features.AI.Interfaces;

namespace MarketFlow.Application.Features.AI.Services;

public sealed class AiReportQueryService(
    IAiBusinessDataService businessDataService,
    IOpenAiClient openAiClient) : IAiReportQueryService
{
    private const string UnsupportedMessage =
        "I can only answer supported company report questions. Try asking about sales, inventory, purchases, suppliers, or stock movements.";

    private static readonly JsonSerializerOptions JsonOptions = new(JsonSerializerDefaults.Web)
    {
        DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull
    };

    public async Task<ServiceResult<NaturalLanguageReportResponseDto>> QueryAsync(
        NaturalLanguageReportRequest request,
        CancellationToken cancellationToken = default)
    {
        if (string.IsNullOrWhiteSpace(request.Question))
        {
            return ServiceResult<NaturalLanguageReportResponseDto>.Failure("Question is required.");
        }

        if (request.From.HasValue && request.To.HasValue && request.From > request.To)
        {
            return ServiceResult<NaturalLanguageReportResponseDto>.Failure("From date must be on or before to date.");
        }

        if ((request.MarketId.HasValue && request.MarketId <= 0) ||
            (request.DepartmentId.HasValue && request.DepartmentId <= 0))
        {
            return ServiceResult<NaturalLanguageReportResponseDto>.Failure(
                "Market and department filters must be positive values.");
        }

        var intent = await ClassifyIntentAsync(request.Question, cancellationToken);

        if (intent is null)
        {
            return ServiceResult<NaturalLanguageReportResponseDto>.Failure(UnsupportedMessage);
        }

        var query = CreateDataQuery(request);
        var dataResult = await businessDataService.GetBusinessDataAsync(query, cancellationToken);

        if (!dataResult.Succeeded || dataResult.Data is null)
        {
            return ServiceResult<NaturalLanguageReportResponseDto>.Failure(
                dataResult.Message,
                dataResult.FailureType);
        }

        var data = dataResult.Data;
        var report = CreateReport(intent.ReportType, data);

        return ServiceResult<NaturalLanguageReportResponseDto>.Success(new NaturalLanguageReportResponseDto
        {
            Intent = intent,
            Report = report
        });
    }

    private async Task<NaturalLanguageReportIntentDto?> ClassifyIntentAsync(
        string question,
        CancellationToken cancellationToken)
    {
        var completion = await openAiClient.GenerateTextAsync(new AiCompletionRequestDto
        {
            SystemPrompt = """
                Classify the user's business question into exactly one allowed report type.
                Allowed report types: TopSellingProducts, LowStockProducts, SalesByMarket, SalesByCategory, SupplierPerformance, PurchaseSummary, InventoryMovements, DailySalesSummary.
                If the question does not fit one allowed report type, return Unsupported.
                Never write SQL, database queries, code, explanations, or extra keys.
                Respond as compact JSON with this shape: {"reportType":"TopSellingProducts","confidence":0.95} or {"reportType":"Unsupported","confidence":0}.
                """,
            Prompt = $"Question: {question.Trim()}",
            Temperature = 0m
        }, cancellationToken);

        return ParseIntent(completion.Text);
    }

    private static NaturalLanguageReportIntentDto? ParseIntent(string text)
    {
        try
        {
            var payload = JsonSerializer.Deserialize<IntentPayload>(text, JsonOptions);

            if (payload is not null &&
                Enum.TryParse<AiReportType>(payload.ReportType, ignoreCase: true, out var reportType))
            {
                return new NaturalLanguageReportIntentDto
                {
                    ReportType = reportType,
                    Confidence = Math.Clamp(payload.Confidence ?? 1m, 0m, 1m)
                };
            }
        }
        catch (JsonException)
        {
        }

        var normalized = text.Trim().Trim('"');

        return Enum.TryParse<AiReportType>(normalized, ignoreCase: true, out var parsedReportType)
            ? new NaturalLanguageReportIntentDto { ReportType = parsedReportType }
            : null;
    }

    private static AiBusinessDataQuery CreateDataQuery(NaturalLanguageReportRequest request)
    {
        var (from, to) = ResolveDateRange(request);
        var limit = Math.Clamp(request.Limit ?? 10, 1, 50);

        return new AiBusinessDataQuery
        {
            From = from,
            To = to,
            MarketId = request.MarketId,
            DepartmentId = request.DepartmentId,
            TopProductsLimit = limit,
            LowStockLimit = limit
        };
    }

    private static (DateOnly? From, DateOnly? To) ResolveDateRange(NaturalLanguageReportRequest request)
    {
        if (request.From.HasValue || request.To.HasValue)
        {
            return (request.From, request.To);
        }

        var question = request.Question.ToLowerInvariant();
        var today = DateOnly.FromDateTime(DateTime.UtcNow);

        if (question.Contains("this week", StringComparison.Ordinal))
        {
            var daysSinceMonday = ((int)today.DayOfWeek + 6) % 7;
            return (today.AddDays(-daysSinceMonday), today);
        }

        if (question.Contains("today", StringComparison.Ordinal))
        {
            return (today, today);
        }

        if (question.Contains("this month", StringComparison.Ordinal))
        {
            return (new DateOnly(today.Year, today.Month, 1), today);
        }

        return (null, null);
    }

    private static object CreateReport(AiReportType reportType, AiBusinessDataDto data)
    {
        return reportType switch
        {
            AiReportType.TopSellingProducts => data.TopSellingProducts,
            AiReportType.LowStockProducts => data.LowStockProducts,
            AiReportType.SalesByMarket => data.SalesByMarket,
            AiReportType.SalesByCategory => data.SalesByCategory,
            AiReportType.SupplierPerformance => data.SupplierPurchaseMetrics,
            AiReportType.InventoryMovements => data.InventoryMovementMetrics,
            AiReportType.DailySalesSummary => data.DailySalesSummaries,
            AiReportType.PurchaseSummary => new
            {
                PurchaseCount = data.SupplierPurchaseMetrics.Sum(x => x.PurchaseCount),
                TotalPurchaseAmount = data.SupplierPurchaseMetrics.Sum(x => x.TotalPurchaseAmount),
                TotalPurchasedQuantity = data.SupplierPurchaseMetrics.Sum(x => x.TotalPurchasedQuantity),
                Suppliers = data.SupplierPurchaseMetrics
            },
            _ => throw new ArgumentOutOfRangeException(nameof(reportType), reportType, null)
        };
    }

    private sealed class IntentPayload
    {
        public string? ReportType { get; init; }

        public decimal? Confidence { get; init; }
    }
}
