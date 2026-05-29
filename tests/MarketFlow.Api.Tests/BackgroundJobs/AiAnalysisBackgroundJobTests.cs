using MarketFlow.Api.Tests.Integration;
using MarketFlow.Application.Features.AI.DTOs;
using MarketFlow.Application.Features.AI.Interfaces;
using MarketFlow.Infrastructure.BackgroundJobs;
using MarketFlow.Infrastructure.OpenAI;
using MarketFlow.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging.Abstractions;
using Npgsql;

namespace MarketFlow.Api.Tests.BackgroundJobs;

public sealed class AiAnalysisBackgroundJobTests
{
    [PostgresIntegrationFact]
    public async Task ExecuteLowStockRecommendationCheckAsync_CompletesAnalysisAndNotifiesCompanyAdmins()
    {
        var options = TenantIntegrationTestOptions.FromEnvironment();
        await using var database = new TenantIntegrationTestDatabase(options);
        var company = await database.CreateCompanyAsync();
        var admin = await database.CreateUserAsync(company, roleName: "CompanyAdmin");
        var market = await database.InsertMarketAsync(company.SchemaName);
        var product = await database.InsertProductAsync(
            company.SchemaName,
            minStockAlert: 5);
        await database.InsertInventoryAsync(
            company.SchemaName,
            product.Id,
            market.Id,
            quantity: 2);
        await using var dbContext = CreateDbContext(options.ConnectionString);
        var job = new AiAnalysisBackgroundJob(
            dbContext,
            new FakeAiClient(),
            NullLogger<AiAnalysisBackgroundJob>.Instance);

        await job.ExecuteLowStockRecommendationCheckAsync();

        Assert.Equal(1, await CountAnalysisRequestsAsync(
            options.ConnectionString,
            company.SchemaName,
            "LowStockRecommendationCheck",
            "Completed"));
        Assert.Equal(1, await CountAnalysisResultsAsync(options.ConnectionString, company.SchemaName));
        Assert.Equal(1, await CountNotificationsAsync(options.ConnectionString, company.SchemaName, admin.Id));
    }

    [PostgresIntegrationFact]
    public async Task ExecuteDailyDashboardSummaryAsync_WhenAiProviderFails_MarksRequestFailed()
    {
        var options = TenantIntegrationTestOptions.FromEnvironment();
        await using var database = new TenantIntegrationTestDatabase(options);
        var company = await database.CreateCompanyAsync();
        await database.CreateUserAsync(company, roleName: "CompanyAdmin");
        await using var dbContext = CreateDbContext(options.ConnectionString);
        var job = new AiAnalysisBackgroundJob(
            dbContext,
            new ThrowingAiClient(),
            NullLogger<AiAnalysisBackgroundJob>.Instance);

        await job.ExecuteDailyDashboardSummaryAsync();

        Assert.Equal(1, await CountAnalysisRequestsAsync(
            options.ConnectionString,
            company.SchemaName,
            "DailyDashboardSummary",
            "Failed"));
        Assert.Equal(0, await CountAnalysisResultsAsync(options.ConnectionString, company.SchemaName));
    }

    private static ApplicationDbContext CreateDbContext(string connectionString)
    {
        var options = new DbContextOptionsBuilder<ApplicationDbContext>()
            .UseNpgsql(connectionString)
            .Options;

        return new ApplicationDbContext(options);
    }

    private static async Task<int> CountAnalysisRequestsAsync(
        string connectionString,
        string schemaName,
        string analysisType,
        string status)
    {
        await using var connection = new NpgsqlConnection(connectionString);
        await connection.OpenAsync();

        await using var command = new NpgsqlCommand(
            $"""
            SELECT COUNT(*)::int
            FROM {QuoteIdentifier(schemaName)}.ai_analysis_requests
            WHERE analysis_type = @analysis_type
              AND status = @status;
            """,
            connection);
        command.Parameters.AddWithValue("analysis_type", analysisType);
        command.Parameters.AddWithValue("status", status);

        return (int)(await command.ExecuteScalarAsync() ?? 0);
    }

    private static async Task<int> CountAnalysisResultsAsync(
        string connectionString,
        string schemaName)
    {
        await using var connection = new NpgsqlConnection(connectionString);
        await connection.OpenAsync();

        await using var command = new NpgsqlCommand(
            $"""
            SELECT COUNT(*)::int
            FROM {QuoteIdentifier(schemaName)}.ai_analysis_results;
            """,
            connection);

        return (int)(await command.ExecuteScalarAsync() ?? 0);
    }

    private static async Task<int> CountNotificationsAsync(
        string connectionString,
        string schemaName,
        int userId)
    {
        await using var connection = new NpgsqlConnection(connectionString);
        await connection.OpenAsync();

        await using var command = new NpgsqlCommand(
            $"""
            SELECT COUNT(*)::int
            FROM {QuoteIdentifier(schemaName)}.notifications
            WHERE user_id = @user_id
              AND type = 'AiWarning';
            """,
            connection);
        command.Parameters.AddWithValue("user_id", userId);

        return (int)(await command.ExecuteScalarAsync() ?? 0);
    }

    private static string QuoteIdentifier(string identifier)
    {
        return "\"" + identifier.Replace("\"", "\"\"", StringComparison.Ordinal) + "\"";
    }

    private sealed class ThrowingAiClient : IAiClient
    {
        public Task<AiCompletionResponseDto> GenerateTextAsync(
            AiCompletionRequestDto request,
            CancellationToken cancellationToken = default)
        {
            throw new InvalidOperationException("AI provider unavailable.");
        }
    }
}
