using System.IdentityModel.Tokens.Jwt;
using System.Security.Claims;
using System.Text;
using MarketFlow.Api.Authorization;
using MarketFlow.Api.Services;
using MarketFlow.Application.Common.Interfaces;
using MarketFlow.Application.Features.Auth.Interfaces;
using MarketFlow.Application.Features.Categories.Interfaces;
using MarketFlow.Application.Features.Categories.Services;
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
using MarketFlow.Infrastructure.Services.Auth;
using Microsoft.AspNetCore.Authentication.JwtBearer;
using Microsoft.AspNetCore.Authorization;
using Microsoft.EntityFrameworkCore;
using Microsoft.IdentityModel.Tokens;
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

        var jwtSecret = GetRequiredConfigurationValue(configuration, "Jwt:Secret", "Jwt__Secret");

        services
            .AddAuthentication(JwtBearerDefaults.AuthenticationScheme)
            .AddJwtBearer(options =>
            {
                options.TokenValidationParameters = new TokenValidationParameters
                {
                    ValidateIssuer = true,
                    ValidIssuer = GetRequiredConfigurationValue(configuration, "Jwt:Issuer", "Jwt__Issuer"),
                    ValidateAudience = true,
                    ValidAudience = GetRequiredConfigurationValue(configuration, "Jwt:Audience", "Jwt__Audience"),
                    ValidateIssuerSigningKey = true,
                    IssuerSigningKey = new SymmetricSecurityKey(Encoding.UTF8.GetBytes(jwtSecret)),
                    ValidateLifetime = true,
                    ClockSkew = TimeSpan.Zero
                };

                options.Events = new JwtBearerEvents
                {
                    OnTokenValidated = async context =>
                    {
                        var tokenId = context.Principal?.FindFirstValue(JwtRegisteredClaimNames.Jti);

                        if (string.IsNullOrWhiteSpace(tokenId))
                        {
                            context.Fail("Access token is missing token id.");
                            return;
                        }

                        var dbContext = context.HttpContext.RequestServices
                            .GetRequiredService<ApplicationDbContext>();

                        var tokenIsRevoked = await dbContext.RevokedAccessTokens
                            .AnyAsync(x => x.TokenId == tokenId && x.ExpiresAt > DateTimeOffset.UtcNow);

                        if (tokenIsRevoked)
                        {
                            context.Fail("Access token has been revoked.");
                        }
                    }
                };
            });

        services.AddAuthorization(options =>
        {
            options.AddPolicy(AuthorizationPolicies.RootAdminOnly, policy =>
                policy.RequireRole("RootAdmin"));

            options.AddPolicy(AuthorizationPolicies.ManageCompanies, policy =>
                policy.Requirements.Add(new PermissionRequirement("company")));

            options.AddPolicy(AuthorizationPolicies.ReadUsers, policy =>
                policy.Requirements.Add(new PermissionRequirement("users", "read")));

            options.AddPolicy(AuthorizationPolicies.CreateUsers, policy =>
                policy.Requirements.Add(new PermissionRequirement("users", "create")));

            options.AddPolicy(AuthorizationPolicies.UpdateUsers, policy =>
                policy.Requirements.Add(new PermissionRequirement("users", "update")));

            options.AddPolicy(AuthorizationPolicies.DeleteUsers, policy =>
                policy.Requirements.Add(new PermissionRequirement("users", "delete")));

            options.AddPolicy(AuthorizationPolicies.ReadProducts, policy =>
                policy.Requirements.Add(new PermissionRequirement("products", "read")));

            options.AddPolicy(AuthorizationPolicies.CreateProducts, policy =>
                policy.Requirements.Add(new PermissionRequirement("products", "create")));

            options.AddPolicy(AuthorizationPolicies.UpdateProducts, policy =>
                policy.Requirements.Add(new PermissionRequirement("products", "update")));

            options.AddPolicy(AuthorizationPolicies.DeleteProducts, policy =>
                policy.Requirements.Add(new PermissionRequirement("products", "delete")));

            options.AddPolicy(AuthorizationPolicies.ReadPurchases, policy =>
                policy.Requirements.Add(new PermissionRequirement("purchases", "read")));

            options.AddPolicy(AuthorizationPolicies.CreatePurchases, policy =>
                policy.Requirements.Add(new PermissionRequirement("purchases", "create")));

            options.AddPolicy(AuthorizationPolicies.UpdatePurchases, policy =>
                policy.Requirements.Add(new PermissionRequirement("purchases", "update")));

            options.AddPolicy(AuthorizationPolicies.DeletePurchases, policy =>
                policy.Requirements.Add(new PermissionRequirement("purchases", "delete")));

            options.AddPolicy(AuthorizationPolicies.ReadSales, policy =>
                policy.Requirements.Add(new PermissionRequirement("sales", "read")));

            options.AddPolicy(AuthorizationPolicies.CreateSales, policy =>
                policy.Requirements.Add(new PermissionRequirement("sales", "create")));

            options.AddPolicy(AuthorizationPolicies.UpdateSales, policy =>
                policy.Requirements.Add(new PermissionRequirement("sales", "update")));

            options.AddPolicy(AuthorizationPolicies.DeleteSales, policy =>
                policy.Requirements.Add(new PermissionRequirement("sales", "delete")));

            options.AddPolicy(AuthorizationPolicies.CreateInventory, policy =>
                policy.Requirements.Add(new PermissionRequirement("inventory", "create")));

            options.AddPolicy(AuthorizationPolicies.UpdateInventory, policy =>
                policy.Requirements.Add(new PermissionRequirement("inventory", "update")));

            options.AddPolicy(AuthorizationPolicies.ReadInventory, policy =>
                policy.Requirements.Add(new PermissionRequirement("inventory", "read")));

            options.AddPolicy(AuthorizationPolicies.DeleteInventory, policy =>
                policy.Requirements.Add(new PermissionRequirement("inventory", "delete")));
        });
        services.AddScoped<IAuthorizationHandler, PermissionAuthorizationHandler>();

        services.AddScoped<ICurrentUserService, CurrentUserService>();
        services.AddScoped<ICompanyStore, CompanyStore>();
        services.AddScoped<ITenantContextStore, TenantContextStore>();
        services.AddScoped<ITenantQueryService, TenantQueryService>();
        services.AddScoped<IUserStore, UserStore>();

        services.AddScoped<IAuthService, MarketFlow.Infrastructure.Services.Auth.AuthService>();
        services.AddScoped<IJwtTokenService, JwtTokenService>();
        services.AddScoped<ICompanyService, CompanyService>();
        services.AddScoped<ICategoryService, CategoryService>();
        services.AddScoped<IUserCreationValidator, UserCreationValidator>();
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
