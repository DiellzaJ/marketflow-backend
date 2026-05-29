using System.Text.Json;
using System.Text.Json.Serialization;
using MarketFlow.Application.Common.Interfaces;
using MarketFlow.Application.Common.Models;
using MarketFlow.Application.Features.AI.DTOs;
using MarketFlow.Application.Features.AI.Interfaces;

namespace MarketFlow.Application.Features.AI.Services;

public sealed class AiChatService(
    ICurrentUserService currentUserService,
    IAiReportQueryService reportQueryService,
    IAiChatSessionStore chatSessionStore,
    IAiClient aiClient) : IAiChatService
{
    private const string UnsafeRequestMessage =
        "I can help with tenant-safe business summaries, but I cannot provide raw SQL, access other companies, or expose sensitive user data.";

    private static readonly JsonSerializerOptions JsonOptions = new(JsonSerializerDefaults.Web)
    {
        DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull
    };

    public async Task<ServiceResult<AiChatResponse>> ChatAsync(
        AiChatRequest request,
        CancellationToken cancellationToken = default)
    {
        if (string.IsNullOrWhiteSpace(request.Message))
        {
            return ServiceResult<AiChatResponse>.Failure("Message is required.");
        }

        if (request.SessionId is <= 0)
        {
            return ServiceResult<AiChatResponse>.Failure("Session id must be a positive value.");
        }

        if (currentUserService.UserId is not { } userId || currentUserService.CompanyId is null)
        {
            return ServiceResult<AiChatResponse>.Failure("Authentication is required.");
        }

        if (IsUnsafeRequest(request.Message))
        {
            return await SaveRejectedAsync(
                request,
                userId,
                UnsafeRequestMessage,
                cancellationToken);
        }

        var reportResult = await reportQueryService.QueryAsync(new NaturalLanguageReportRequest
        {
            Question = request.Message,
            From = request.From,
            To = request.To,
            MarketId = request.MarketId,
            DepartmentId = request.DepartmentId
        }, cancellationToken);

        if (!reportResult.Succeeded || reportResult.Data is null)
        {
            return await SaveRejectedAsync(
                request,
                userId,
                reportResult.Message,
                cancellationToken);
        }

        var answer = await GenerateAnswerAsync(request.Message, reportResult.Data, cancellationToken);

        return await SaveAndReturnAsync(
            request,
            userId,
            answer,
            reportResult.Data.Intent.ReportType,
            cancellationToken);
    }

    private async Task<ServiceResult<AiChatResponse>> SaveRejectedAsync(
        AiChatRequest request,
        int userId,
        string answer,
        CancellationToken cancellationToken)
    {
        var saveResult = await SaveAndReturnAsync(
            request,
            userId,
            answer,
            reportType: null,
            cancellationToken);

        return saveResult.Succeeded
            ? ServiceResult<AiChatResponse>.Failure(answer)
            : saveResult;
    }

    private async Task<ServiceResult<AiChatResponse>> SaveAndReturnAsync(
        AiChatRequest request,
        int userId,
        string answer,
        AiReportType? reportType,
        CancellationToken cancellationToken)
    {
        var now = DateTimeOffset.UtcNow;
        AiChatSessionDto session;

        try
        {
            session = await chatSessionStore.AppendMessagesAsync(
                request.SessionId,
                userId,
                request.MarketId,
                [
                    new AiChatMessageDto
                    {
                        Role = "user",
                        Content = request.Message.Trim(),
                        CreatedAt = now
                    },
                    new AiChatMessageDto
                    {
                        Role = "assistant",
                        Content = answer.Trim(),
                        CreatedAt = now,
                        ReportType = reportType
                    }
                ],
                cancellationToken);
        }
        catch (UnauthorizedAccessException)
        {
            return ServiceResult<AiChatResponse>.Failure("Chat session was not found for the current tenant.");
        }

        return ServiceResult<AiChatResponse>.Success(new AiChatResponse
        {
            SessionId = session.Id,
            Answer = answer.Trim(),
            ReportType = reportType
        });
    }

    private async Task<string> GenerateAnswerAsync(
        string message,
        NaturalLanguageReportResponseDto report,
        CancellationToken cancellationToken)
    {
        var completion = await aiClient.GenerateTextAsync(new AiCompletionRequestDto
        {
            SystemPrompt = """
                You are a company business assistant for MarketFlow.
                Answer only from the provided tenant-safe report JSON.
                Do not expose user records, credentials, emails, raw orders, private customer data, or data from other companies.
                Do not write SQL, code, or database instructions.
                If the report has no data, say that no matching data was found for the selected filters.
                Keep the answer concise and practical.
                """,
            Prompt = JsonSerializer.Serialize(new
            {
                UserQuestion = message.Trim(),
                report.Intent.ReportType,
                report.Report
            }, JsonOptions),
            Temperature = 0.2m
        }, cancellationToken);

        return completion.Text;
    }

    private static bool IsUnsafeRequest(string message)
    {
        var normalized = message.ToLowerInvariant();

        return ContainsAny(normalized, "sql", "select ", "insert ", "update ", "delete ", "drop ", "truncate ", "schema", "database") ||
            ContainsAny(normalized, "another company", "other company", "other tenant", "different tenant", "all companies", "company id") ||
            ContainsAny(normalized, "password", "credential", "token", "secret", "api key", "user email", "users email", "employee email", "staff email");
    }

    private static bool ContainsAny(string value, params string[] needles)
    {
        return needles.Any(needle => value.Contains(needle, StringComparison.Ordinal));
    }
}
