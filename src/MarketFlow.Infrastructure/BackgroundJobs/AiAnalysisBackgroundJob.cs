using System.Data;
using System.Text.Json;
using System.Text.Json.Serialization;
using System.Text.RegularExpressions;
using MarketFlow.Application.Features.AI.DTOs;
using MarketFlow.Application.Features.AI.Interfaces;
using MarketFlow.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;
using Npgsql;

namespace MarketFlow.Infrastructure.BackgroundJobs;

public sealed partial class AiAnalysisBackgroundJob
{
    private static readonly JsonSerializerOptions JsonOptions = new(JsonSerializerDefaults.Web)
    {
        DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull
    };

    private readonly ApplicationDbContext _dbContext;
    private readonly IAiClient _aiClient;
    private readonly ILogger<AiAnalysisBackgroundJob> _logger;

    public AiAnalysisBackgroundJob(
        ApplicationDbContext dbContext,
        IAiClient aiClient,
        ILogger<AiAnalysisBackgroundJob> logger)
    {
        _dbContext = dbContext;
        _aiClient = aiClient;
        _logger = logger;
    }

    public async Task ExecuteDailyDashboardSummaryAsync(CancellationToken cancellationToken = default)
    {
        var day = DateOnly.FromDateTime(DateTime.UtcNow.Date.AddDays(-1));

        await ExecuteForActiveTenantsAsync(
            "DailyDashboardSummary",
            tenant => RunDailyDashboardSummaryAsync(tenant, day, cancellationToken),
            cancellationToken);
    }

    public async Task ExecuteLowStockRecommendationCheckAsync(CancellationToken cancellationToken = default)
    {
        await ExecuteForActiveTenantsAsync(
            "LowStockRecommendationCheck",
            tenant => RunLowStockRecommendationCheckAsync(tenant, cancellationToken),
            cancellationToken);
    }

    public async Task ExecuteWeeklySupplierPerformanceSummaryAsync(CancellationToken cancellationToken = default)
    {
        var to = DateOnly.FromDateTime(DateTime.UtcNow.Date);
        var from = to.AddDays(-7);

        await ExecuteForActiveTenantsAsync(
            "WeeklySupplierPerformanceSummary",
            tenant => RunWeeklySupplierPerformanceSummaryAsync(tenant, from, to, cancellationToken),
            cancellationToken);
    }

    private async Task ExecuteForActiveTenantsAsync(
        string analysisType,
        Func<TenantScanTarget, Task> executeTenantAsync,
        CancellationToken cancellationToken)
    {
        var tenants = await GetActiveTenantsAsync(cancellationToken);

        foreach (var tenant in tenants)
        {
            try
            {
                await EnsureAiAnalysisTablesAsync(tenant, cancellationToken);
                await executeTenantAsync(tenant);
            }
            catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
            {
                throw;
            }
            catch (PostgresException exception) when (IsMissingTenantObject(exception))
            {
                _logger.LogWarning(
                    exception,
                    "{AnalysisType} skipped tenant {SchemaName} because its schema is not fully provisioned.",
                    analysisType,
                    tenant.SchemaName);
            }
            catch (Exception exception)
            {
                _logger.LogError(
                    exception,
                    "{AnalysisType} failed for tenant {SchemaName}.",
                    analysisType,
                    tenant.SchemaName);
            }
        }
    }

    private async Task RunDailyDashboardSummaryAsync(
        TenantScanTarget tenant,
        DateOnly day,
        CancellationToken cancellationToken)
    {
        var schemaName = QuoteTenantSchemaName(tenant.SchemaName);
        var data = await GetDailyDashboardDataAsync(schemaName, day, cancellationToken);
        var prompt = $"""
            Summarize this daily retail dashboard data for company administrators.
            Focus on sales, order volume, top products, and low-stock warnings.
            Daily data:
            {JsonSerializer.Serialize(data, JsonOptions)}
            """;

        await RunAnalysisAsync(
            tenant,
            "DailyDashboardSummary",
            prompt,
            data,
            new AiCompletionRequestDto
            {
                SystemPrompt = """
                    You are a retail business analyst writing a daily dashboard summary.
                    Use only the aggregate data provided.
                    Respond as compact JSON: {"summary":"...","warnings":["..."],"recommendedActions":["..."]}.
                    """,
                Prompt = prompt,
                Temperature = 0.2m
            },
            shouldNotifyCompanyAdmins: data.LowStockCount > 0,
            notificationTitle: "Daily AI dashboard warning",
            notificationBody: $"Daily AI summary found {data.LowStockCount} low-stock product warning(s).",
            cancellationToken);
    }

    private async Task RunLowStockRecommendationCheckAsync(
        TenantScanTarget tenant,
        CancellationToken cancellationToken)
    {
        var schemaName = QuoteTenantSchemaName(tenant.SchemaName);
        var data = await GetLowStockRecommendationDataAsync(schemaName, cancellationToken);
        var prompt = $"""
            Review these low-stock products and recommend purchase actions for company administrators.
            Use the provided stock levels, minimum stock alerts, pending purchases, recent sales, and preferred supplier data.
            Low-stock data:
            {JsonSerializer.Serialize(data, JsonOptions)}
            """;

        await RunAnalysisAsync(
            tenant,
            "LowStockRecommendationCheck",
            prompt,
            data,
            new AiCompletionRequestDto
            {
                SystemPrompt = """
                    You explain purchase recommendations for low-stock retail products.
                    Use only the backend-calculated inventory data provided.
                    Respond as compact JSON: {"summary":"...","warnings":["..."],"recommendations":[{"productId":1,"recommendation":"..."}]}.
                    """,
                Prompt = prompt,
                Temperature = 0.2m
            },
            shouldNotifyCompanyAdmins: data.Products.Count > 0,
            notificationTitle: "Low-stock AI recommendations ready",
            notificationBody: $"AI reviewed {data.Products.Count} low-stock product(s).",
            cancellationToken);
    }

    private async Task RunWeeklySupplierPerformanceSummaryAsync(
        TenantScanTarget tenant,
        DateOnly from,
        DateOnly to,
        CancellationToken cancellationToken)
    {
        var schemaName = QuoteTenantSchemaName(tenant.SchemaName);
        var data = await GetWeeklySupplierPerformanceDataAsync(schemaName, from, to, cancellationToken);
        var prompt = $"""
            Summarize weekly supplier performance for company administrators.
            Highlight cancellation, delivery speed, spend, and suppliers needing review.
            Supplier performance data:
            {JsonSerializer.Serialize(data, JsonOptions)}
            """;

        await RunAnalysisAsync(
            tenant,
            "WeeklySupplierPerformanceSummary",
            prompt,
            data,
            new AiCompletionRequestDto
            {
                SystemPrompt = """
                    You explain supplier performance insights for retail operators.
                    Use only the backend-calculated supplier metrics provided.
                    Respond as compact JSON: {"summary":"...","warnings":["..."],"recommendations":[{"supplierId":1,"recommendation":"..."}]}.
                    """,
                Prompt = prompt,
                Temperature = 0.2m
            },
            shouldNotifyCompanyAdmins: data.Suppliers.Any(supplier => supplier.ReliabilityLevel != "High"),
            notificationTitle: "Supplier AI performance warning",
            notificationBody: "Weekly AI supplier analysis found suppliers that need review.",
            cancellationToken);
    }

    private async Task RunAnalysisAsync<TPayload>(
        TenantScanTarget tenant,
        string analysisType,
        string prompt,
        TPayload requestPayload,
        AiCompletionRequestDto completionRequest,
        bool shouldNotifyCompanyAdmins,
        string notificationTitle,
        string notificationBody,
        CancellationToken cancellationToken)
    {
        var requestId = await CreateAnalysisRequestAsync(
            tenant,
            analysisType,
            prompt,
            requestPayload,
            cancellationToken);

        try
        {
            var completion = await _aiClient.GenerateTextAsync(completionRequest, cancellationToken);
            var summary = ExtractSummary(completion.Text);

            await CompleteAnalysisRequestAsync(
                tenant,
                requestId,
                completion,
                requestPayload,
                summary,
                cancellationToken);

            if (shouldNotifyCompanyAdmins)
            {
                await CreateCompanyAdminNotificationsAsync(
                    tenant,
                    notificationTitle,
                    notificationBody,
                    cancellationToken);
            }
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
            throw;
        }
        catch (Exception exception)
        {
            await FailAnalysisRequestAsync(tenant, requestId, exception, cancellationToken);

            _logger.LogWarning(
                exception,
                "{AnalysisType} AI provider failed for tenant {SchemaName}; request {RequestId} marked Failed.",
                analysisType,
                tenant.SchemaName,
                requestId);
        }
    }

    private async Task<DailyDashboardPayload> GetDailyDashboardDataAsync(
        string schemaName,
        DateOnly day,
        CancellationToken cancellationToken)
    {
        var from = day.ToDateTime(TimeOnly.MinValue, DateTimeKind.Utc);
        var to = from.AddDays(1);

        await using var command = await CreateCommandAsync($"""
            WITH sales_scope AS (
                SELECT id, total_amount
                FROM {schemaName}.sales
                WHERE sale_date >= @from
                  AND sale_date < @to
                  AND status <> 'Cancelled'
            ),
            sales_metrics AS (
                SELECT COALESCE(SUM(total_amount), 0) AS total_sales,
                       COUNT(*)::int AS order_count
                FROM sales_scope
            ),
            item_metrics AS (
                SELECT COALESCE(SUM(si.quantity), 0)::bigint AS items_sold
                FROM {schemaName}.sale_items si
                INNER JOIN sales_scope s ON s.id = si.sale_id
            ),
            top_products AS (
                SELECT p.name AS product_name,
                       SUM(si.quantity)::bigint AS quantity_sold,
                       SUM(si.line_total) AS revenue
                FROM {schemaName}.sale_items si
                INNER JOIN sales_scope s ON s.id = si.sale_id
                INNER JOIN {schemaName}.products p ON p.id = si.product_id
                GROUP BY p.id, p.name
                ORDER BY quantity_sold DESC, revenue DESC, p.name ASC
                LIMIT 5
            ),
            low_stock AS (
                SELECT p.name AS product_name,
                       m.name AS market_name,
                       d.name AS department_name,
                       i.quantity,
                       p.min_stock_alert
                FROM {schemaName}.inventory i
                INNER JOIN {schemaName}.products p ON p.id = i.product_id
                INNER JOIN {schemaName}.markets m ON m.id = i.market_id
                LEFT JOIN {schemaName}.departments d ON d.id = i.department_id
                WHERE p.is_active = TRUE
                  AND i.quantity <= p.min_stock_alert
                ORDER BY i.quantity ASC, p.name ASC
                LIMIT 10
            )
            SELECT jsonb_build_object(
                'day', @day,
                'totalSales', (SELECT total_sales FROM sales_metrics),
                'orderCount', (SELECT order_count FROM sales_metrics),
                'itemsSold', (SELECT items_sold FROM item_metrics),
                'topProducts', COALESCE((SELECT jsonb_agg(to_jsonb(top_products)) FROM top_products), '[]'::jsonb),
                'lowStockWarnings', COALESCE((SELECT jsonb_agg(to_jsonb(low_stock)) FROM low_stock), '[]'::jsonb),
                'lowStockCount', (SELECT COUNT(*)::int FROM low_stock)
            )::text;
            """, cancellationToken);

        command.Parameters.AddWithValue("from", from);
        command.Parameters.AddWithValue("to", to);
        command.Parameters.AddWithValue("day", day.ToString("O"));

        var json = (string)(await command.ExecuteScalarAsync(cancellationToken) ?? "{}");

        return JsonSerializer.Deserialize<DailyDashboardPayload>(json, JsonOptions) ??
            new DailyDashboardPayload();
    }

    private async Task<LowStockRecommendationPayload> GetLowStockRecommendationDataAsync(
        string schemaName,
        CancellationToken cancellationToken)
    {
        await using var command = await CreateCommandAsync($"""
            WITH low_stock AS (
                SELECT i.product_id,
                       p.name AS product_name,
                       m.name AS market_name,
                       d.name AS department_name,
                       i.quantity AS current_stock,
                       p.min_stock_alert,
                       GREATEST(p.min_stock_alert * 2 - i.quantity, 0)::int AS suggested_restock_quantity
                FROM {schemaName}.inventory i
                INNER JOIN {schemaName}.products p ON p.id = i.product_id
                INNER JOIN {schemaName}.markets m ON m.id = i.market_id
                LEFT JOIN {schemaName}.departments d ON d.id = i.department_id
                WHERE p.is_active = TRUE
                  AND i.quantity <= p.min_stock_alert
            ),
            recent_sales AS (
                SELECT si.product_id,
                       COALESCE(SUM(si.quantity), 0)::bigint AS quantity_sold_30_days
                FROM {schemaName}.sale_items si
                INNER JOIN {schemaName}.sales s ON s.id = si.sale_id
                WHERE s.sale_date >= NOW() - INTERVAL '30 days'
                  AND s.status <> 'Cancelled'
                GROUP BY si.product_id
            ),
            pending_purchases AS (
                SELECT pi.product_id,
                       COALESCE(SUM(pi.quantity - pi.received_quantity), 0)::int AS pending_purchase_quantity
                FROM {schemaName}.purchase_items pi
                INNER JOIN {schemaName}.purchases p ON p.id = pi.purchase_id
                WHERE p.status IN ('Pending', 'Ordered', 'PartiallyReceived')
                GROUP BY pi.product_id
            ),
            preferred_suppliers AS (
                SELECT DISTINCT ON (pi.product_id)
                       pi.product_id,
                       s.id AS supplier_id,
                       s.name AS supplier_name
                FROM {schemaName}.purchase_items pi
                INNER JOIN {schemaName}.purchases p ON p.id = pi.purchase_id
                INNER JOIN {schemaName}.suppliers s ON s.id = p.supplier_id
                GROUP BY pi.product_id, s.id, s.name
                ORDER BY pi.product_id, SUM(pi.quantity) DESC, MAX(p.purchase_date) DESC
            )
            SELECT jsonb_build_object(
                'generatedAt', NOW(),
                'products', COALESCE(jsonb_agg(jsonb_build_object(
                    'productId', ls.product_id,
                    'productName', ls.product_name,
                    'marketName', ls.market_name,
                    'departmentName', ls.department_name,
                    'currentStock', ls.current_stock,
                    'minimumStockAlert', ls.min_stock_alert,
                    'suggestedRestockQuantity', ls.suggested_restock_quantity,
                    'quantitySold30Days', COALESCE(rs.quantity_sold_30_days, 0),
                    'pendingPurchaseQuantity', COALESCE(pp.pending_purchase_quantity, 0),
                    'preferredSupplierId', ps.supplier_id,
                    'preferredSupplierName', ps.supplier_name
                ) ORDER BY ls.current_stock ASC, ls.product_name ASC), '[]'::jsonb)
            )::text
            FROM low_stock ls
            LEFT JOIN recent_sales rs ON rs.product_id = ls.product_id
            LEFT JOIN pending_purchases pp ON pp.product_id = ls.product_id
            LEFT JOIN preferred_suppliers ps ON ps.product_id = ls.product_id;
            """, cancellationToken);

        var json = (string)(await command.ExecuteScalarAsync(cancellationToken) ?? "{}");

        return JsonSerializer.Deserialize<LowStockRecommendationPayload>(json, JsonOptions) ??
            new LowStockRecommendationPayload();
    }

    private async Task<WeeklySupplierPerformancePayload> GetWeeklySupplierPerformanceDataAsync(
        string schemaName,
        DateOnly from,
        DateOnly to,
        CancellationToken cancellationToken)
    {
        await using var command = await CreateCommandAsync($"""
            WITH supplier_metrics AS (
                SELECT s.id AS supplier_id,
                       s.name AS supplier_name,
                       COUNT(p.id)::bigint AS total_purchases,
                       COUNT(p.id) FILTER (WHERE p.status = 'Received')::bigint AS received_purchases,
                       COUNT(p.id) FILTER (WHERE p.status = 'Cancelled')::bigint AS cancelled_purchases,
                       AVG((p.received_at::date - p.purchase_date)::numeric)
                           FILTER (WHERE p.status = 'Received' AND p.received_at IS NOT NULL) AS average_delivery_days,
                       COALESCE(SUM(p.total_amount) FILTER (WHERE p.status = 'Received'), 0) AS total_amount_spent
                FROM {schemaName}.suppliers s
                LEFT JOIN {schemaName}.purchases p
                    ON p.supplier_id = s.id
                   AND p.purchase_date >= @from
                   AND p.purchase_date < @to
                WHERE s.is_active = TRUE
                GROUP BY s.id, s.name
            ),
            scored AS (
                SELECT *,
                       CASE
                           WHEN total_purchases > 0
                            AND cancelled_purchases::numeric / total_purchases >= 0.30 THEN 'Low'
                           WHEN average_delivery_days > 14 THEN 'Low'
                           WHEN cancelled_purchases > 0 OR average_delivery_days > 7 THEN 'Medium'
                           ELSE 'High'
                       END AS reliability_level
                FROM supplier_metrics
            )
            SELECT jsonb_build_object(
                'from', @from_text,
                'to', @to_text,
                'suppliers', COALESCE(jsonb_agg(jsonb_build_object(
                    'supplierId', supplier_id,
                    'supplierName', supplier_name,
                    'totalPurchases', total_purchases,
                    'receivedPurchases', received_purchases,
                    'cancelledPurchases', cancelled_purchases,
                    'averageDeliveryDays', average_delivery_days,
                    'totalAmountSpent', total_amount_spent,
                    'reliabilityLevel', reliability_level
                ) ORDER BY reliability_level ASC, total_amount_spent DESC, supplier_name ASC), '[]'::jsonb)
            )::text
            FROM scored;
            """, cancellationToken);

        command.Parameters.AddWithValue("from", from.ToDateTime(TimeOnly.MinValue));
        command.Parameters.AddWithValue("to", to.ToDateTime(TimeOnly.MinValue));
        command.Parameters.AddWithValue("from_text", from.ToString("O"));
        command.Parameters.AddWithValue("to_text", to.ToString("O"));

        var json = (string)(await command.ExecuteScalarAsync(cancellationToken) ?? "{}");

        return JsonSerializer.Deserialize<WeeklySupplierPerformancePayload>(json, JsonOptions) ??
            new WeeklySupplierPerformancePayload();
    }

    private async Task<Guid> CreateAnalysisRequestAsync<TPayload>(
        TenantScanTarget tenant,
        string analysisType,
        string prompt,
        TPayload payload,
        CancellationToken cancellationToken)
    {
        var schemaName = QuoteTenantSchemaName(tenant.SchemaName);
        var requestId = Guid.NewGuid();

        await using var command = await CreateCommandAsync($"""
            INSERT INTO {schemaName}.ai_analysis_requests (
                id,
                company_id,
                analysis_type,
                prompt,
                request_payload,
                status,
                created_at,
                updated_at)
            VALUES (
                @id,
                @company_id,
                @analysis_type,
                @prompt,
                @request_payload::jsonb,
                'Processing',
                NOW(),
                NOW());
            """, cancellationToken);

        command.Parameters.AddWithValue("id", requestId);
        command.Parameters.AddWithValue("company_id", ToCompanyGuid(tenant.CompanyId));
        command.Parameters.AddWithValue("analysis_type", analysisType);
        command.Parameters.AddWithValue("prompt", prompt);
        command.Parameters.AddWithValue("request_payload", JsonSerializer.Serialize(payload, JsonOptions));

        await command.ExecuteNonQueryAsync(cancellationToken);

        return requestId;
    }

    private async Task CompleteAnalysisRequestAsync<TPayload>(
        TenantScanTarget tenant,
        Guid requestId,
        AiCompletionResponseDto completion,
        TPayload sourceData,
        string summary,
        CancellationToken cancellationToken)
    {
        var schemaName = QuoteTenantSchemaName(tenant.SchemaName);
        var resultPayload = new
        {
            completion.Text,
            SourceData = sourceData
        };

        await using var transaction = await BeginTransactionAsync(cancellationToken);

        await using (var insertCommand = await CreateCommandAsync($"""
            INSERT INTO {schemaName}.ai_analysis_results (
                id,
                company_id,
                ai_analysis_request_id,
                model,
                result_payload,
                summary,
                created_at)
            VALUES (
                @id,
                @company_id,
                @request_id,
                @model,
                @result_payload::jsonb,
                @summary,
                NOW());
            """, cancellationToken, transaction))
        {
            insertCommand.Parameters.AddWithValue("id", Guid.NewGuid());
            insertCommand.Parameters.AddWithValue("company_id", ToCompanyGuid(tenant.CompanyId));
            insertCommand.Parameters.AddWithValue("request_id", requestId);
            insertCommand.Parameters.AddWithValue("model", completion.Model);
            insertCommand.Parameters.AddWithValue("result_payload", JsonSerializer.Serialize(resultPayload, JsonOptions));
            insertCommand.Parameters.AddWithValue("summary", summary);

            await insertCommand.ExecuteNonQueryAsync(cancellationToken);
        }

        await using (var updateCommand = await CreateCommandAsync($"""
            UPDATE {schemaName}.ai_analysis_requests
            SET status = 'Completed',
                error_message = NULL,
                updated_at = NOW()
            WHERE id = @request_id;
            """, cancellationToken, transaction))
        {
            updateCommand.Parameters.AddWithValue("request_id", requestId);
            await updateCommand.ExecuteNonQueryAsync(cancellationToken);
        }

        await transaction.CommitAsync(cancellationToken);
    }

    private async Task FailAnalysisRequestAsync(
        TenantScanTarget tenant,
        Guid requestId,
        Exception exception,
        CancellationToken cancellationToken)
    {
        var schemaName = QuoteTenantSchemaName(tenant.SchemaName);

        await using var command = await CreateCommandAsync($"""
            UPDATE {schemaName}.ai_analysis_requests
            SET status = 'Failed',
                error_message = @error_message,
                updated_at = NOW()
            WHERE id = @request_id;
            """, cancellationToken);

        command.Parameters.AddWithValue("request_id", requestId);
        command.Parameters.AddWithValue("error_message", exception.Message);

        await command.ExecuteNonQueryAsync(cancellationToken);
    }

    private async Task CreateCompanyAdminNotificationsAsync(
        TenantScanTarget tenant,
        string title,
        string body,
        CancellationToken cancellationToken)
    {
        var schemaName = QuoteTenantSchemaName(tenant.SchemaName);

        await using var command = await CreateCommandAsync($"""
            INSERT INTO {schemaName}.notifications (user_id, type, title, body)
            SELECT u.id,
                   'AiWarning',
                   @title,
                   @body
            FROM public.users u
            INNER JOIN public.roles r ON r.id = u.role_id
            WHERE u.company_id = @company_id
              AND u.is_active = TRUE
              AND r.name = 'CompanyAdmin'
              AND NOT EXISTS (
                  SELECT 1
                  FROM {schemaName}.notifications existing
                  WHERE existing.user_id = u.id
                    AND existing.type = 'AiWarning'
                    AND existing.title = @title
                    AND COALESCE(existing.body, '') = COALESCE(@body, '')
                    AND existing.is_read = FALSE
              );
            """, cancellationToken);

        command.Parameters.AddWithValue("company_id", tenant.CompanyId);
        command.Parameters.AddWithValue("title", title);
        command.Parameters.AddWithValue("body", body);

        await command.ExecuteNonQueryAsync(cancellationToken);
    }

    private async Task EnsureAiAnalysisTablesAsync(
        TenantScanTarget tenant,
        CancellationToken cancellationToken)
    {
        try
        {
            await using var command = await CreateCommandAsync(
                "SELECT public.ensure_tenant_ai_analysis_tables(@schema_name);",
                cancellationToken);

            command.Parameters.AddWithValue("schema_name", tenant.SchemaName);

            await command.ExecuteNonQueryAsync(cancellationToken);
        }
        catch (PostgresException exception) when (exception.SqlState == "42883")
        {
            await EnsureAiAnalysisTablesInlineAsync(tenant, cancellationToken);
        }
    }

    private async Task EnsureAiAnalysisTablesInlineAsync(
        TenantScanTarget tenant,
        CancellationToken cancellationToken)
    {
        var schemaName = QuoteTenantSchemaName(tenant.SchemaName);

        await using var command = await CreateCommandAsync($"""
            CREATE EXTENSION IF NOT EXISTS pgcrypto;

            CREATE TABLE IF NOT EXISTS {schemaName}.ai_analysis_requests (
                id                   UUID PRIMARY KEY DEFAULT gen_random_uuid(),
                company_id           UUID        NOT NULL,
                analysis_type        VARCHAR(100) NOT NULL,
                prompt               TEXT        NOT NULL,
                request_payload      JSONB       NOT NULL DEFAULT jsonb_build_object(),
                status               VARCHAR(20) NOT NULL DEFAULT 'Pending',
                error_message        TEXT,
                requested_by_user_id INT,
                created_at           TIMESTAMPTZ NOT NULL DEFAULT NOW(),
                updated_at           TIMESTAMPTZ
            );

            ALTER TABLE {schemaName}.ai_analysis_requests
                DROP CONSTRAINT IF EXISTS ai_analysis_requests_status_check;

            ALTER TABLE {schemaName}.ai_analysis_requests
                ADD CONSTRAINT ai_analysis_requests_status_check
                CHECK (status IN ('Pending', 'Processing', 'Completed', 'Failed'));

            CREATE INDEX IF NOT EXISTS idx_ai_analysis_requests_company
                ON {schemaName}.ai_analysis_requests(company_id);
            CREATE INDEX IF NOT EXISTS idx_ai_analysis_requests_status
                ON {schemaName}.ai_analysis_requests(status);
            CREATE INDEX IF NOT EXISTS idx_ai_analysis_requests_created
                ON {schemaName}.ai_analysis_requests(created_at);

            CREATE TABLE IF NOT EXISTS {schemaName}.ai_analysis_results (
                id                     UUID PRIMARY KEY DEFAULT gen_random_uuid(),
                company_id             UUID        NOT NULL,
                ai_analysis_request_id UUID        NOT NULL REFERENCES {schemaName}.ai_analysis_requests(id) ON DELETE CASCADE,
                model                  VARCHAR(100) NOT NULL,
                result_payload         JSONB       NOT NULL DEFAULT jsonb_build_object(),
                summary                TEXT,
                prompt_tokens          INT,
                completion_tokens      INT,
                created_at             TIMESTAMPTZ NOT NULL DEFAULT NOW(),
                updated_at             TIMESTAMPTZ
            );

            CREATE INDEX IF NOT EXISTS idx_ai_analysis_results_company
                ON {schemaName}.ai_analysis_results(company_id);
            CREATE INDEX IF NOT EXISTS idx_ai_analysis_results_request
                ON {schemaName}.ai_analysis_results(ai_analysis_request_id);
            """, cancellationToken);

        await command.ExecuteNonQueryAsync(cancellationToken);
    }

    private async Task<IReadOnlyCollection<TenantScanTarget>> GetActiveTenantsAsync(
        CancellationToken cancellationToken)
    {
        var tenants = new List<TenantScanTarget>();

        await using var command = await CreateCommandAsync("""
            SELECT id, schema_name
            FROM public.companies
            WHERE is_active = TRUE
            ORDER BY id;
            """, cancellationToken);

        await using var reader = await command.ExecuteReaderAsync(cancellationToken);

        while (await reader.ReadAsync(cancellationToken))
        {
            tenants.Add(new TenantScanTarget(reader.GetInt32(0), reader.GetString(1)));
        }

        return tenants;
    }

    private async Task<NpgsqlCommand> CreateCommandAsync(
        string commandText,
        CancellationToken cancellationToken,
        NpgsqlTransaction? transaction = null)
    {
        var connection = (NpgsqlConnection)_dbContext.Database.GetDbConnection();

        if (connection.State != ConnectionState.Open)
        {
            await connection.OpenAsync(cancellationToken);
        }

        return new NpgsqlCommand(commandText, connection, transaction);
    }

    private async Task<NpgsqlTransaction> BeginTransactionAsync(CancellationToken cancellationToken)
    {
        var connection = (NpgsqlConnection)_dbContext.Database.GetDbConnection();

        if (connection.State != ConnectionState.Open)
        {
            await connection.OpenAsync(cancellationToken);
        }

        return await connection.BeginTransactionAsync(cancellationToken);
    }

    private static string ExtractSummary(string text)
    {
        try
        {
            using var document = JsonDocument.Parse(text);

            if (document.RootElement.ValueKind == JsonValueKind.Object &&
                document.RootElement.TryGetProperty("summary", out var summary) &&
                summary.ValueKind == JsonValueKind.String)
            {
                return summary.GetString()?.Trim() ?? text.Trim();
            }
        }
        catch (JsonException)
        {
        }

        return text.Trim();
    }

    private static Guid ToCompanyGuid(int companyId)
    {
        return Guid.Parse($"00000000-0000-0000-0000-{companyId:000000000000}");
    }

    private static string QuoteTenantSchemaName(string schemaName)
    {
        if (!TenantSchemaNamePattern().IsMatch(schemaName))
        {
            throw new InvalidOperationException($"Tenant schema name '{schemaName}' is invalid.");
        }

        return "\"" + schemaName.Replace("\"", "\"\"", StringComparison.Ordinal) + "\"";
    }

    private static bool IsMissingTenantObject(PostgresException exception)
    {
        return exception.SqlState is
            "3F000" or
            "42P01" or
            "42703" or
            "42883";
    }

    [GeneratedRegex("^[a-zA-Z_][a-zA-Z0-9_]{0,62}$", RegexOptions.CultureInvariant)]
    private static partial Regex TenantSchemaNamePattern();

    private sealed record TenantScanTarget(int CompanyId, string SchemaName);

    private sealed class DailyDashboardPayload
    {
        public string Day { get; init; } = string.Empty;

        public decimal TotalSales { get; init; }

        public int OrderCount { get; init; }

        public long ItemsSold { get; init; }

        public IReadOnlyCollection<object> TopProducts { get; init; } = [];

        public IReadOnlyCollection<object> LowStockWarnings { get; init; } = [];

        public int LowStockCount { get; init; }
    }

    private sealed class LowStockRecommendationPayload
    {
        public DateTimeOffset GeneratedAt { get; init; }

        public IReadOnlyCollection<object> Products { get; init; } = [];
    }

    private sealed class WeeklySupplierPerformancePayload
    {
        public string From { get; init; } = string.Empty;

        public string To { get; init; } = string.Empty;

        public IReadOnlyCollection<WeeklySupplierPayload> Suppliers { get; init; } = [];
    }

    private sealed class WeeklySupplierPayload
    {
        public int SupplierId { get; init; }

        public string SupplierName { get; init; } = string.Empty;

        public long TotalPurchases { get; init; }

        public long ReceivedPurchases { get; init; }

        public long CancelledPurchases { get; init; }

        public decimal? AverageDeliveryDays { get; init; }

        public decimal TotalAmountSpent { get; init; }

        public string ReliabilityLevel { get; init; } = "High";
    }
}

public sealed class AiAnalysisBackgroundJobOptions
{
    public bool Enabled { get; set; } = true;

    public bool RunOnStartup { get; set; }

    public TimeSpan DailyInterval { get; set; } = TimeSpan.FromDays(1);

    public TimeSpan LowStockInterval { get; set; } = TimeSpan.FromHours(6);

    public TimeSpan WeeklyInterval { get; set; } = TimeSpan.FromDays(7);
}
