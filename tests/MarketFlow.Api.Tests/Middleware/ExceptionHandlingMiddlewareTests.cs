using MarketFlow.Api.Middleware;
using MarketFlow.Infrastructure.MultiTenancy;
using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.Logging.Abstractions;

namespace MarketFlow.Api.Tests.Middleware;

public sealed class ExceptionHandlingMiddlewareTests
{
    [Fact]
    public async Task InvokeAsync_WhenUnauthorizedAccessException_Returns401()
    {
        var context = CreateHttpContext();
        var middleware = CreateMiddleware(_ => throw new UnauthorizedAccessException("Invalid credentials."));

        await middleware.InvokeAsync(context);

        Assert.Equal(StatusCodes.Status401Unauthorized, context.Response.StatusCode);
        Assert.Equal("{\"message\":\"Unauthorized.\"}", await ReadResponseBodyAsync(context));
    }

    [Fact]
    public async Task InvokeAsync_WhenTenantAccessException_Returns403()
    {
        var context = CreateHttpContext();
        var middleware = CreateMiddleware(_ => throw new TenantAccessException());

        await middleware.InvokeAsync(context);

        Assert.Equal(StatusCodes.Status403Forbidden, context.Response.StatusCode);
        Assert.Equal("{\"message\":\"Forbidden.\"}", await ReadResponseBodyAsync(context));
    }

    [Fact]
    public async Task InvokeAsync_WhenUnhandledException_DoesNotExposeInternalDetails()
    {
        var context = CreateHttpContext();
        var middleware = CreateMiddleware(_ =>
            throw new InvalidOperationException(
                "SQL failed for schema tenant_alpha and table products."));

        await middleware.InvokeAsync(context);

        var responseBody = await ReadResponseBodyAsync(context);

        Assert.Equal(StatusCodes.Status500InternalServerError, context.Response.StatusCode);
        Assert.Equal("{\"message\":\"An unexpected error occurred.\"}", responseBody);
        Assert.DoesNotContain("tenant_alpha", responseBody, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("products", responseBody, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("SQL", responseBody, StringComparison.OrdinalIgnoreCase);
    }

    private static ExceptionHandlingMiddleware CreateMiddleware(RequestDelegate next)
    {
        return new ExceptionHandlingMiddleware(
            next,
            NullLogger<ExceptionHandlingMiddleware>.Instance);
    }

    private static DefaultHttpContext CreateHttpContext()
    {
        var context = new DefaultHttpContext();
        context.Response.Body = new MemoryStream();

        return context;
    }

    private static async Task<string> ReadResponseBodyAsync(HttpContext context)
    {
        context.Response.Body.Seek(0, SeekOrigin.Begin);

        using var reader = new StreamReader(context.Response.Body);

        return await reader.ReadToEndAsync();
    }
}
