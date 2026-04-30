using MarketFlow.Api.Extensions;

var builder = WebApplication.CreateBuilder(args);

builder.Services.AddControllers();
builder.Services.AddEndpointsApiExplorer();
builder.Services.AddSwaggerGen();
builder.Services.AddApiServices(builder.Configuration);

var app = builder.Build();

app.UseApiPipeline();

app.Run();
