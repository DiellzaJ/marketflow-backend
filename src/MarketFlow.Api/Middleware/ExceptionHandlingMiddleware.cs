using System.Text.Json;
using MarketFlow.Infrastructure.MultiTenancy;
using Npgsql;

namespace MarketFlow.Api.Middleware;

public class ExceptionHandlingMiddleware(
    RequestDelegate next,
    ILogger<ExceptionHandlingMiddleware> logger)
{
    public async Task InvokeAsync(HttpContext context)
    {
        try
        {
            await next(context);
        }
        catch (Exception exception)
        {
            logger.LogError(exception, "Unhandled exception while processing request.");

            context.Response.StatusCode = GetStatusCode(exception);
            context.Response.ContentType = "application/json";

            var payload = JsonSerializer.Serialize(CreateErrorResponse(exception));

            await context.Response.WriteAsync(payload);
        }
    }

    private object CreateErrorResponse(Exception exception)
    {
        return new
        {
            message = GetSafeMessage(exception)
        };
    }

    private static int GetStatusCode(Exception exception)
    {
        return exception switch
        {
            UnauthorizedAccessException => StatusCodes.Status401Unauthorized,
            TenantAccessException => StatusCodes.Status403Forbidden,
            _ => StatusCodes.Status500InternalServerError
        };
    }

    private static string GetSafeMessage(Exception exception)
    {
        return exception switch
        {
            UnauthorizedAccessException => "Unauthorized.",
            TenantAccessException => "Forbidden.",
            PostgresException => "An unexpected error occurred.",
            _ => "An unexpected error occurred."
        };
    }
}
