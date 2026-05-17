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
                Permissions = "{\"users:read\": true, \"users:create\": true, \"users:update\": true, \"users:delete\": true, \"products:read\": true, \"products:create\": true, \"products:update\": true, \"products:delete\": true, \"sales:read\": true, \"sales:create\": true, \"sales:update\": true, \"sales:delete\": true, \"inventory:create\": true, \"inventory:read\": true, \"inventory:update\": true, \"inventory:delete\": true, \"purchases:read\": true, \"purchases:create\": true, \"purchases:update\": true, \"purchases:delete\": true}"
            },
            new Role
            {
                Name = "MainOperator",
                Description = "Market-level manager",
                Permissions = "{\"products:read\": true, \"products:create\": true, \"products:update\": true, \"products:delete\": true, \"sales:read\": true, \"sales:create\": true, \"sales:update\": true, \"sales:delete\": true, \"inventory:create\": true, \"inventory:read\": true, \"inventory:update\": true, \"inventory:delete\": true, \"purchases:read\": true, \"purchases:create\": true, \"purchases:update\": true, \"purchases:delete\": true}"
            },
            new Role
            {
                Name = "DepartmentManager",
                Description = "Department-level manager",
                Permissions = "{\"products:read\": true, \"inventory:create\": true, \"inventory:read\": true, \"inventory:update\": true, \"inventory:delete\": true}"
            },
            new Role
            {
                Name = "InventoryEmployee",
                Description = "Stock and inventory employee",
                Permissions = "{\"products:read\": true, \"inventory:read\": true, \"inventory:update\": true}"
            },
            new Role
            {
                Name = "Seller",
                Description = "Creates sales and handles POS operations",
                Permissions = "{\"products:read\": true, \"sales:create\": true, \"inventory:read\": true, \"inventory:update\": true}"
            }
        };

        foreach (var role in roles)
        {
            var existingRole = await dbContext.Roles.FirstOrDefaultAsync(x => x.Name == role.Name);

            if (existingRole is null)
            {
                dbContext.Roles.Add(role);
                continue;
            }

            existingRole.Description = role.Description;
            existingRole.Permissions = role.Permissions;
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
