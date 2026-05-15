using BCrypt.Net;
using MarketFlow.Domain.Entities;
using MarketFlow.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;

namespace MarketFlow.Infrastructure.Seed;

public static class GlobalDataSeeder
{
    public static async Task SeedGlobalDataAsync(this IServiceProvider serviceProvider)
    {
        using var scope = serviceProvider.CreateScope();

        var dbContext = scope.ServiceProvider.GetRequiredService<ApplicationDbContext>();
        var configuration = scope.ServiceProvider.GetRequiredService<IConfiguration>();

        await SeedRolesAsync(dbContext);
        await SeedRootAdminCompanyAsync(dbContext);
        await SeedRootAdminUserAsync(dbContext, configuration);
    }

    private static async Task SeedRolesAsync(ApplicationDbContext dbContext)
    {
        var roles = new[]
        {
            new Role
            {
                Name = "RootAdmin",
                Description = "Platform administrator",
                Permissions = "{\"all\": true}"
            },
            new Role
            {
                Name = "CompanyAdmin",
                Description = "Company-level administrator",
                Permissions = "{\"company\": true, \"markets\": true, \"users\": true}"
            },
            new Role
            {
                Name = "MainOperator",
                Description = "Market-level manager",
                Permissions = "{\"market\": true, \"products\": true, \"purchases\": true, \"sales\": true}"
            },
            new Role
            {
                Name = "DepartmentManager",
                Description = "Department-level manager",
                Permissions = "{\"department\": true, \"inventory\": true, \"employees\": true}"
            },
            new Role
            {
                Name = "InventoryEmployee",
                Description = "Stock and inventory employee",
                Permissions = "{\"inventory\": \"read-update\"}"
            },
            new Role
            {
                Name = "Seller",
                Description = "Creates sales and handles POS operations",
                Permissions = "{\"sales\": true, \"inventory\": \"update\"}"
            }
        };

        foreach (var role in roles)
        {
            var exists = await dbContext.Roles.AnyAsync(x => x.Name == role.Name);

            if (!exists)
            {
                dbContext.Roles.Add(role);
            }
        }

        await dbContext.SaveChangesAsync();
    }

    private static async Task SeedRootAdminCompanyAsync(ApplicationDbContext dbContext)
    {
        var exists = await dbContext.Companies
            .AnyAsync(x => x.SchemaName == "platform_admin");

        if (exists)
        {
            return;
        }

        var company = new Company
        {
            Name = "MarketFlow Platform",
            SchemaName = "platform_admin",
            CompanyType = "BIG",
            SubscriptionPlan = "PLATFORM",
            MaxMarkets = 999,
            MaxUsers = 999,
            IsActive = true
        };

        dbContext.Companies.Add(company);

        await dbContext.SaveChangesAsync();
    }

    private static async Task SeedRootAdminUserAsync(
        ApplicationDbContext dbContext,
        IConfiguration configuration)
    {
        var email =
            Environment.GetEnvironmentVariable("ROOT_ADMIN_EMAIL")
            ?? configuration["ROOT_ADMIN_EMAIL"]
            ?? "admin@marketflow.local";

        var password =
            Environment.GetEnvironmentVariable("ROOT_ADMIN_PASSWORD")
            ?? configuration["ROOT_ADMIN_PASSWORD"]
            ?? "Admin@12345";

        var fullName =
            Environment.GetEnvironmentVariable("ROOT_ADMIN_FULL_NAME")
            ?? configuration["ROOT_ADMIN_FULL_NAME"]
            ?? "Root Admin";

        var userExists = await dbContext.Users.AnyAsync(x => x.Email == email);

        if (userExists)
        {
            return;
        }

        var rootRole = await dbContext.Roles
            .FirstAsync(x => x.Name == "RootAdmin");

        var platformCompany = await dbContext.Companies
            .FirstAsync(x => x.SchemaName == "platform_admin");

        var user = new User
        {
            FullName = fullName,
            Email = email,
            PasswordHash = BCrypt.Net.BCrypt.HashPassword(password),
            CompanyId = platformCompany.Id,
            RoleId = rootRole.Id,
            IsActive = true
        };

        dbContext.Users.Add(user);

        await dbContext.SaveChangesAsync();
    }
}