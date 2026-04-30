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

namespace MarketFlow.Api.Extensions;

public static class ServiceCollectionExtensions
{
    public static IServiceCollection AddApiServices(
        this IServiceCollection services,
        IConfiguration configuration)
    {
        services.AddHttpContextAccessor();

        services.AddDbContext<ApplicationDbContext>(options => { });

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
}
