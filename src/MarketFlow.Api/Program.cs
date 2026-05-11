using MarketFlow.Api.Configuration;
using MarketFlow.Api.Extensions;

DotEnv.Load();

var builder = WebApplication.CreateBuilder(args);
builder.Configuration.AddFriendlyEnvironmentVariables();

builder.Services.AddControllers();
builder.Services.AddEndpointsApiExplorer();
builder.Services.AddOpenApi(options =>
{
    options.AddDocumentTransformer((document, _, _) =>
    {
        document.Info ??= new();
        document.Info.Title = "MarketFlow API";
        document.Info.Version = "1.0.0";

        return Task.CompletedTask;
    });
});
builder.Services.AddApiServices(builder.Configuration);

var app = builder.Build();

app.UseApiPipeline();

app.Run();
