using System.Data;
using System.Security.Claims;
using System.Text.RegularExpressions;
using MarketFlow.Application.Common.Interfaces;
using MarketFlow.Infrastructure.MultiTenancy;
using MarketFlow.Infrastructure.Persistence;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.Filters;
using Microsoft.EntityFrameworkCore;
using Npgsql;

namespace MarketFlow.Api.Authorization;

public sealed class AiTenantScopeAuthorizationFilter(
    ApplicationDbContext dbContext,
    ICurrentUserService currentUserService,
    TenantProvider tenantProvider) : IAsyncActionFilter
{
    private static readonly Regex SchemaNamePattern = new(
        "^[a-zA-Z_][a-zA-Z0-9_]{0,62}$",
        RegexOptions.Compiled | RegexOptions.CultureInvariant);

    public async Task OnActionExecutionAsync(
        ActionExecutingContext context,
        ActionExecutionDelegate next)
    {
        var role = context.HttpContext.User.FindFirstValue(ClaimTypes.Role);

        if (string.Equals(role, "CompanyAdmin", StringComparison.OrdinalIgnoreCase))
        {
            await next();
            return;
        }

        var requestScope = AiRequestScope.FromActionArguments(context.ActionArguments.Values);

        if (requestScope is null)
        {
            await next();
            return;
        }

        if (!IsOperationalAiRole(role))
        {
            context.Result = new ForbidResult();
            return;
        }

        var schemaName = await tenantProvider.GetCurrentSchemaNameAsync(context.HttpContext.RequestAborted);
        var assignment = await GetCurrentStaffAssignmentAsync(schemaName, context.HttpContext.RequestAborted);

        if (assignment is null ||
            !await CanAccessRequestedScopeAsync(schemaName, role!, assignment, requestScope, context.HttpContext.RequestAborted))
        {
            context.Result = new ForbidResult();
            return;
        }

        ApplyAssignedScope(role!, assignment, requestScope);

        await next();
    }

    private static bool IsOperationalAiRole(string? role)
    {
        return string.Equals(role, "MainOperator", StringComparison.OrdinalIgnoreCase) ||
            string.Equals(role, "DepartmentManager", StringComparison.OrdinalIgnoreCase) ||
            string.Equals(role, "InventoryEmployee", StringComparison.OrdinalIgnoreCase);
    }

    private async Task<bool> CanAccessRequestedScopeAsync(
        string schemaName,
        string role,
        StaffAssignmentScope assignment,
        AiRequestScope requestScope,
        CancellationToken cancellationToken)
    {
        if (requestScope.MarketId.HasValue && requestScope.MarketId.Value != assignment.MarketId)
        {
            return false;
        }

        if (string.Equals(role, "DepartmentManager", StringComparison.OrdinalIgnoreCase))
        {
            return assignment.DepartmentId.HasValue &&
                (!requestScope.DepartmentId.HasValue || requestScope.DepartmentId.Value == assignment.DepartmentId.Value);
        }

        if (string.Equals(role, "InventoryEmployee", StringComparison.OrdinalIgnoreCase) &&
            assignment.DepartmentId.HasValue)
        {
            return !requestScope.DepartmentId.HasValue ||
                requestScope.DepartmentId.Value == assignment.DepartmentId.Value;
        }

        return !requestScope.DepartmentId.HasValue ||
            await DepartmentBelongsToMarketAsync(
                schemaName,
                assignment.MarketId,
                requestScope.DepartmentId.Value,
                cancellationToken);
    }

    private static void ApplyAssignedScope(
        string role,
        StaffAssignmentScope assignment,
        AiRequestScope requestScope)
    {
        if (!requestScope.MarketId.HasValue)
        {
            requestScope.SetMarketId(assignment.MarketId);
        }

        if (string.Equals(role, "DepartmentManager", StringComparison.OrdinalIgnoreCase) ||
            string.Equals(role, "InventoryEmployee", StringComparison.OrdinalIgnoreCase))
        {
            if (assignment.DepartmentId.HasValue && !requestScope.DepartmentId.HasValue)
            {
                requestScope.SetDepartmentId(assignment.DepartmentId.Value);
            }
        }
    }

    private async Task<StaffAssignmentScope?> GetCurrentStaffAssignmentAsync(
        string schemaName,
        CancellationToken cancellationToken)
    {
        if (currentUserService.UserId is not { } userId)
        {
            return null;
        }

        await using var command = await CreateTenantCommandAsync(schemaName, $"""
            SELECT market_id,
                   department_id
            FROM {QuoteIdentifier(schemaName)}.staff_assignments
            WHERE is_active = TRUE
              AND user_id = @user_id
            ORDER BY assigned_at DESC, id DESC
            LIMIT 1;
            """, cancellationToken);

        command.Parameters.AddWithValue("user_id", userId);

        await using var reader = await command.ExecuteReaderAsync(cancellationToken);

        return await reader.ReadAsync(cancellationToken)
            ? new StaffAssignmentScope(
                reader.GetInt32(0),
                reader.IsDBNull(1) ? null : reader.GetInt32(1))
            : null;
    }

    private async Task<bool> DepartmentBelongsToMarketAsync(
        string schemaName,
        int marketId,
        int departmentId,
        CancellationToken cancellationToken)
    {
        await using var command = await CreateTenantCommandAsync(schemaName, $"""
            SELECT EXISTS (
                SELECT 1
                FROM {QuoteIdentifier(schemaName)}.departments
                WHERE id = @department_id
                  AND market_id = @market_id
                  AND is_active = TRUE
            );
            """, cancellationToken);

        command.Parameters.AddWithValue("market_id", marketId);
        command.Parameters.AddWithValue("department_id", departmentId);

        return (bool)(await command.ExecuteScalarAsync(cancellationToken) ?? false);
    }

    private async Task<NpgsqlCommand> CreateTenantCommandAsync(
        string schemaName,
        string commandText,
        CancellationToken cancellationToken)
    {
        if (!SchemaNamePattern.IsMatch(schemaName))
        {
            throw new TenantAccessException();
        }

        var connection = (NpgsqlConnection)dbContext.Database.GetDbConnection();

        if (connection.State != ConnectionState.Open)
        {
            await connection.OpenAsync(cancellationToken);
        }

        return new NpgsqlCommand(commandText, connection);
    }

    private static string QuoteIdentifier(string value)
    {
        if (!SchemaNamePattern.IsMatch(value))
        {
            throw new TenantAccessException();
        }

        return "\"" + value.Replace("\"", "\"\"", StringComparison.Ordinal) + "\"";
    }

    private sealed record StaffAssignmentScope(int MarketId, int? DepartmentId);

    private sealed class AiRequestScope(
        object request,
        System.Reflection.PropertyInfo? marketIdProperty,
        System.Reflection.PropertyInfo? departmentIdProperty,
        int? marketId,
        int? departmentId)
    {
        public int? MarketId { get; private set; } = marketId;

        public int? DepartmentId { get; private set; } = departmentId;

        public void SetMarketId(int marketId)
        {
            SetNullableInt(marketIdProperty, request, marketId);
            MarketId = marketId;
        }

        public void SetDepartmentId(int departmentId)
        {
            SetNullableInt(departmentIdProperty, request, departmentId);
            DepartmentId = departmentId;
        }

        public static AiRequestScope? FromActionArguments(IEnumerable<object?> arguments)
        {
            foreach (var argument in arguments)
            {
                if (argument is null)
                {
                    continue;
                }

                var type = argument.GetType();
                var marketIdProperty = type.GetProperty("MarketId");
                var departmentIdProperty = type.GetProperty("DepartmentId");
                var marketId = ReadNullableInt(marketIdProperty, argument);
                var departmentId = ReadNullableInt(departmentIdProperty, argument);

                if (marketId.HasValue || departmentId.HasValue ||
                    type.Namespace?.Contains(".AI.", StringComparison.Ordinal) == true)
                {
                    return new AiRequestScope(
                        argument,
                        marketIdProperty,
                        departmentIdProperty,
                        marketId,
                        departmentId);
                }
            }

            return null;
        }

        private static int? ReadNullableInt(System.Reflection.PropertyInfo? property, object instance)
        {
            if (property is null)
            {
                return null;
            }

            return property.GetValue(instance) switch
            {
                int value => value,
                _ => null
            };
        }

        private static void SetNullableInt(
            System.Reflection.PropertyInfo? property,
            object instance,
            int value)
        {
            if (property is null || property.SetMethod is null)
            {
                return;
            }

            if (property.PropertyType == typeof(int) ||
                property.PropertyType == typeof(int?))
            {
                property.SetValue(instance, value);
            }
        }
    }
}
