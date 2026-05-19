using System.Data;
using System.Text.RegularExpressions;
using BCrypt.Net;
using MarketFlow.Domain.Entities;
using MarketFlow.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Npgsql;

namespace MarketFlow.Infrastructure.Seed;

public static class GlobalDataSeeder
{
    private static readonly Regex SchemaNamePattern = new(
        "^[a-zA-Z_][a-zA-Z0-9_]{0,62}$",
        RegexOptions.Compiled | RegexOptions.CultureInvariant);

    private static readonly (string Name, string Description)[] DefaultCategories =
    [
        ("Uncategorized", "Fallback category for imported or legacy products."),
        ("Beverages", "Drinks, juices, water, coffee, tea, and soft drinks."),
        ("Dairy", "Milk, cheese, yogurt, butter, and other dairy products."),
        ("Bakery", "Bread, pastries, cakes, and baked goods."),
        ("Fruits & Vegetables", "Fresh fruits, vegetables, herbs, and produce."),
        ("Meat & Fish", "Fresh and packaged meat, poultry, seafood, and fish."),
        ("Snacks", "Chips, sweets, biscuits, nuts, and snack foods."),
        ("Hygiene", "Personal care, toiletries, and hygiene products."),
        ("Cleaning Products", "Household cleaning supplies and detergents."),
        ("Frozen Foods", "Frozen meals, vegetables, desserts, and ice cream."),
        ("Household", "General household essentials and everyday goods.")
    ];

    public static async Task SeedGlobalDataAsync(this IServiceProvider serviceProvider)
    {
        using var scope = serviceProvider.CreateScope();

        var dbContext = scope.ServiceProvider.GetRequiredService<ApplicationDbContext>();
        var configuration = scope.ServiceProvider.GetRequiredService<IConfiguration>();

        await SeedRolesAsync(dbContext);
        await SeedRootAdminCompanyAsync(dbContext);
        await SeedRootAdminUserAsync(dbContext, configuration);
        await SeedDefaultCategoriesAsync(dbContext);
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

    private static async Task SeedDefaultCategoriesAsync(ApplicationDbContext dbContext)
    {
        var schemaNames = await dbContext.Companies
            .AsNoTracking()
            .Where(x => x.IsActive)
            .Select(x => x.SchemaName)
            .ToListAsync();

        var connection = (NpgsqlConnection)dbContext.Database.GetDbConnection();

        if (connection.State != ConnectionState.Open)
        {
            await connection.OpenAsync();
        }

        foreach (var schemaName in schemaNames)
        {
            if (!SchemaNamePattern.IsMatch(schemaName))
            {
                continue;
            }

            await using (var createSchemaCommand = new NpgsqlCommand(
                "SELECT public.create_tenant_schema(@schema_name);",
                connection))
            {
                createSchemaCommand.Parameters.AddWithValue("schema_name", schemaName);
                await createSchemaCommand.ExecuteNonQueryAsync();
            }

            var quotedSchemaName = QuoteIdentifier(schemaName);

            foreach (var category in DefaultCategories)
            {
                await using var insertCommand = new NpgsqlCommand(
                    $"""
                    INSERT INTO {quotedSchemaName}.categories (name, description)
                    SELECT @name, @description
                    WHERE NOT EXISTS (
                        SELECT 1
                        FROM {quotedSchemaName}.categories
                        WHERE lower(name) = lower(@name)
                    );
                    """,
                    connection);

                insertCommand.Parameters.AddWithValue("name", category.Name);
                insertCommand.Parameters.AddWithValue("description", category.Description);
                await insertCommand.ExecuteNonQueryAsync();
            }

            await using var repairProductsCommand = new NpgsqlCommand(
                $"""
                UPDATE {quotedSchemaName}.products
                SET category_id = (
                    SELECT id
                    FROM {quotedSchemaName}.categories
                    WHERE lower(name) = lower(@uncategorized_name)
                    ORDER BY id
                    LIMIT 1
                )
                WHERE category_id IS NULL;
                """,
                connection);

            repairProductsCommand.Parameters.AddWithValue("uncategorized_name", "Uncategorized");
            await repairProductsCommand.ExecuteNonQueryAsync();
        }
    }

    private static string QuoteIdentifier(string identifier)
    {
        return "\"" + identifier.Replace("\"", "\"\"", StringComparison.Ordinal) + "\"";
    }
}
