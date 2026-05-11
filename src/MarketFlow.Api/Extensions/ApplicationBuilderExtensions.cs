using MarketFlow.Api.Middleware;

namespace MarketFlow.Api.Extensions;

public static class ApplicationBuilderExtensions
{
    public static WebApplication UseApiPipeline(this WebApplication app)
    {
        app.UseMiddleware<ExceptionHandlingMiddleware>();
        app.UseMiddleware<RequestLoggingMiddleware>();

        if (app.Environment.IsDevelopment())
        {
            app.MapOpenApi();
            app.MapOpenApi("/swagger/{documentName}/swagger.json");
            app.UseSwaggerUI(options =>
            {
                options.RoutePrefix = "swagger";
                options.DocumentTitle = "MarketFlow API";
                options.SwaggerEndpoint("/openapi/v1.json", "MarketFlow API v1");
            });
        }

        app.UseCors(ServiceCollectionExtensions.FrontendCorsPolicy);

        app.MapControllers();

        return app;
    }
}
