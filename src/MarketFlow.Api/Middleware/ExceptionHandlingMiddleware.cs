using System.Text.Json;

namespace MarketFlow.Api.Middleware;

public class ExceptionHandlingMiddleware(
    RequestDelegate next,
    ILogger<ExceptionHandlingMiddleware> logger,
    IHostEnvironment environment)
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

            context.Response.StatusCode = StatusCodes.Status500InternalServerError;
            context.Response.ContentType = "application/json";

            var payload = JsonSerializer.Serialize(CreateErrorResponse(exception));

            await context.Response.WriteAsync(payload);
        }
    }

    private object CreateErrorResponse(Exception exception)
    {
        if (environment.IsDevelopment())
        {
            return new
            {
                message = "An unexpected error occurred.",
                detail = exception.Message
            };
        }

        return new
        {
            message = "An unexpected error occurred."
        };
    }
}
