using MarketFlow.Api.Configuration;
using MarketFlow.Api.Extensions;

DotEnv.Load();

var builder = WebApplication.CreateBuilder(args);
builder.Configuration.AddFriendlyEnvironmentVariables();

builder.Services.AddControllers();
builder.Services.AddEndpointsApiExplorer();
builder.Services.AddSwaggerGen();
builder.Services.AddApiServices(builder.Configuration);

var app = builder.Build();

app.UseApiPipeline();

app.Run();
