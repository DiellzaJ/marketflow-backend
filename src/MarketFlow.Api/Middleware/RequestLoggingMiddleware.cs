namespace MarketFlow.Api.Middleware;

public class RequestLoggingMiddleware(RequestDelegate next, ILogger<RequestLoggingMiddleware> logger)
{
    public async Task InvokeAsync(HttpContext context)
    {
        logger.LogInformation(
            "Handling {Method} {Path}",
            context.Request.Method,
            context.Request.Path);

        await next(context);
    }
}
