using MarketFlow.Application.Features.Auth.Interfaces;
using MarketFlow.Application.Features.Auth.Services;
using MarketFlow.Application.Features.Companies.Interfaces;
using MarketFlow.Application.Features.Companies.Services;
using MarketFlow.Application.Features.Inventory.Interfaces;
using MarketFlow.Application.Features.Inventory.Services;
using MarketFlow.Application.Features.Products.Interfaces;
using MarketFlow.Application.Features.Products.Services;
using MarketFlow.Application.Features.Purchases.Interfaces;
using MarketFlow.Application.Features.Purchases.Services;
using MarketFlow.Application.Features.Sales.Interfaces;
using MarketFlow.Application.Features.Sales.Services;
using MarketFlow.Application.Features.Users.Interfaces;
using MarketFlow.Application.Features.Users.Services;
using MarketFlow.Infrastructure.Caching;
using MarketFlow.Infrastructure.MultiTenancy;
using MarketFlow.Infrastructure.Persistence;
using MarketFlow.Infrastructure.Repositories;
using MarketFlow.Infrastructure.Services;
using Microsoft.EntityFrameworkCore;
using Npgsql;

namespace MarketFlow.Api.Extensions;

public static class ServiceCollectionExtensions
{
    public const string FrontendCorsPolicy = "FrontendCorsPolicy";

    public static IServiceCollection AddApiServices(
        this IServiceCollection services,
        IConfiguration configuration)
    {
        services.AddHttpContextAccessor();

        services.AddDbContext<ApplicationDbContext>(options =>
            options.UseNpgsql(CreatePostgresConnectionString(configuration)));

        services.AddCors(options =>
        {
            options.AddPolicy(FrontendCorsPolicy, policy =>
            {
                var frontendUrls = GetFrontendUrls(configuration);

                if (frontendUrls.Length > 0)
                {
                    policy.WithOrigins(frontendUrls);
                }

                policy
                    .AllowAnyHeader()
                    .AllowAnyMethod();
            });
        });

        services.AddScoped<IAuthService, AuthService>();
        services.AddScoped<ICompanyService, CompanyService>();
        services.AddScoped<IUserService, UserService>();
        services.AddScoped<IProductService, ProductService>();
        services.AddScoped<IInventoryService, InventoryService>();
        services.AddScoped<IPurchaseService, PurchaseService>();
        services.AddScoped<ISalesService, SalesService>();

        services.AddScoped<ProductRepository>();
        services.AddScoped<InventoryRepository>();
        services.AddScoped<SalesRepository>();

        services.AddScoped<JwtTokenService>();
        services.AddScoped<PasswordHasher>();
        services.AddScoped<OpenAiService>();
        services.AddScoped<TenantProvider>();
        services.AddScoped<RedisCacheService>();

        return services;
    }

    private static string CreatePostgresConnectionString(IConfiguration configuration)
    {
        var configuredConnectionString = configuration.GetConnectionString("DefaultConnection");

        if (!string.IsNullOrWhiteSpace(configuredConnectionString))
        {
            return configuredConnectionString;
        }

        var portValue = GetRequiredConfigurationValue(configuration, "Database:Port", "DB_PORT");

        if (!int.TryParse(portValue, out var port))
        {
            throw new InvalidOperationException("Database port must be a valid integer.");
        }

        return new NpgsqlConnectionStringBuilder
        {
            Host = GetRequiredConfigurationValue(configuration, "Database:Host", "DB_HOST"),
            Port = port,
            Database = GetRequiredConfigurationValue(configuration, "Database:Name", "DB_NAME"),
            Username = GetRequiredConfigurationValue(configuration, "Database:Username", "DB_USERNAME"),
            Password = configuration["Database:Password"] ?? string.Empty
        }.ConnectionString;
    }

    private static string GetRequiredConfigurationValue(
        IConfiguration configuration,
        string configurationKey,
        string environmentVariableName)
    {
        var value = configuration[configurationKey];

        if (!string.IsNullOrWhiteSpace(value))
        {
            return value;
        }

        throw new InvalidOperationException(
            $"Missing required configuration value '{configurationKey}'. Set it with '{environmentVariableName}' in .env or environment variables.");
    }

    private static string[] GetFrontendUrls(IConfiguration configuration)
    {
        return (configuration["Cors:FrontendUrl"] ?? string.Empty)
            .Split(new[] { ',', ';' }, StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);
    }
}
