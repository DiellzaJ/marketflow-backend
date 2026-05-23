using System.Data;
using System.Text.RegularExpressions;
using BCrypt.Net;
using MarketFlow.Domain.Entities;
using MarketFlow.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
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
        var logger = scope.ServiceProvider.GetRequiredService<ILoggerFactory>().CreateLogger(nameof(GlobalDataSeeder));

        await SeedRolesAsync(dbContext);
        await SeedRootAdminCompanyAsync(dbContext);
        await SeedRootAdminUserAsync(dbContext, configuration);
        await SeedDefaultCategoriesAsync(dbContext, logger);
    }

    private static async Task SeedRolesAsync(ApplicationDbContext dbContext)
    {
        var roles = new[]
        {
            new Role
            {
                Name = "RootAdmin",
                Description = "Platform administrator",
                Permissions = "{\"company\": true, \"companies:read\": true, \"companies:create\": true, \"companies:update\": true, \"companies:delete\": true, \"users:read\": true, \"users:create\": true, \"users:update\": true, \"users:delete\": true}"
            },
            new Role
            {
                Name = "CompanyAdmin",
                Description = "Company-level administrator",
                Permissions = "{\"users:read\": true, \"users:create\": true, \"users:update\": true, \"users:delete\": true, \"markets:read\": true, \"departments:read\": true, \"products:read\": true, \"products:create\": true, \"products:update\": true, \"products:delete\": true, \"sales:read\": true, \"sales:create\": true, \"sales:update\": true, \"sales:delete\": true, \"inventory:create\": true, \"inventory:read\": true, \"stock:update\": true, \"stock:adjust\": true, \"inventory:delete\": true, \"inventory-movements:read\": true, \"stock:transfer\": true, \"purchases:read\": true, \"purchases:create\": true, \"purchases:update\": true, \"purchases:delete\": true}"
            },
            new Role
            {
                Name = "MainOperator",
                Description = "Market-level manager",
                Permissions = "{\"markets:read\": true, \"departments:read\": true, \"products:read\": true, \"products:create\": true, \"products:update\": true, \"products:delete\": true, \"sales:read\": true, \"sales:create\": true, \"sales:update\": true, \"sales:delete\": true, \"inventory:create\": true, \"inventory:read\": true, \"stock:update\": true, \"stock:adjust\": true, \"inventory:delete\": true, \"inventory-movements:read\": true, \"stock:transfer\": true, \"purchases:read\": true, \"purchases:create\": true, \"purchases:update\": true, \"purchases:delete\": true}"
            },
            new Role
            {
                Name = "DepartmentManager",
                Description = "Department-level manager",
                Permissions = "{\"markets:read\": true, \"departments:read\": true, \"products:read\": true, \"inventory:read\": true, \"stock:update\": true, \"stock:adjust\": true, \"inventory-movements:read\": true, \"stock:transfer\": true}"
            },
            new Role
            {
                Name = "InventoryEmployee",
                Description = "Stock and inventory employee",
                Permissions = "{\"markets:read\": true, \"departments:read\": true, \"products:read\": true, \"inventory:read\": true, \"stock:update\": true, \"stock:adjust\": true, \"inventory-movements:read\": true}"
            },
            new Role
            {
                Name = "Seller",
                Description = "Creates sales and handles POS operations",
                Permissions = "{\"markets:read\": true, \"departments:read\": true, \"products:read\": true, \"sales:create\": true, \"inventory:read\": true}"
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

    private static async Task SeedDefaultCategoriesAsync(
        ApplicationDbContext dbContext,
        ILogger logger)
    {
        var schemaNames = await dbContext.Companies
            .AsNoTracking()
            .Where(x => x.IsActive)
            .Select(x => x.SchemaName)
            .ToListAsync();

        var connection = (NpgsqlConnection)dbContext.Database.GetDbConnection();
        var shouldCloseConnection = connection.State != ConnectionState.Open;

        if (shouldCloseConnection)
        {
            await connection.OpenAsync();
        }

        try
        {
            var createTenantSchemaFunctionExists = await CreateTenantSchemaFunctionExistsAsync(connection);

            if (!createTenantSchemaFunctionExists)
            {
                logger.LogWarning(
                    "public.create_tenant_schema(text) was not found. Default category seeding will use existing tenant schemas only.");
            }

            foreach (var schemaName in schemaNames)
            {
                if (!SchemaNamePattern.IsMatch(schemaName))
                {
                    logger.LogWarning("Skipping default category seeding for invalid tenant schema name {SchemaName}.", schemaName);
                    continue;
                }

                await using var transaction = await connection.BeginTransactionAsync();

                try
                {
                    if (createTenantSchemaFunctionExists)
                    {
                        await using var createSchemaCommand = new NpgsqlCommand(
                            "SELECT public.create_tenant_schema(@schema_name);",
                            connection,
                            transaction);

                        createSchemaCommand.Parameters.AddWithValue("schema_name", schemaName);
                        await createSchemaCommand.ExecuteNonQueryAsync();
                    }

                    var categoriesTableExists = await TenantTableExistsAsync(
                        connection,
                        transaction,
                        schemaName,
                        "categories");

                    if (!categoriesTableExists)
                    {
                        logger.LogWarning(
                            "Skipping default category seeding for tenant schema {SchemaName} because the categories table is missing.",
                            schemaName);

                        await transaction.RollbackAsync();
                        continue;
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
                            connection,
                            transaction);

                        insertCommand.Parameters.AddWithValue("name", category.Name);
                        insertCommand.Parameters.AddWithValue("description", category.Description);
                        await insertCommand.ExecuteNonQueryAsync();
                    }

                    var productsTableExists = await TenantTableExistsAsync(
                        connection,
                        transaction,
                        schemaName,
                        "products");

                    if (productsTableExists)
                    {
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
                            connection,
                            transaction);

                        repairProductsCommand.Parameters.AddWithValue("uncategorized_name", "Uncategorized");
                        await repairProductsCommand.ExecuteNonQueryAsync();
                    }
                    else
                    {
                        logger.LogWarning(
                            "Skipped product category repair for tenant schema {SchemaName} because the products table is missing.",
                            schemaName);
                    }

                    await transaction.CommitAsync();
                }
                catch (Exception exception)
                {
                    logger.LogError(exception, "Failed to seed default categories for tenant schema {SchemaName}.", schemaName);

                    try
                    {
                        await transaction.RollbackAsync();
                    }
                    catch (Exception rollbackException)
                    {
                        logger.LogError(
                            rollbackException,
                            "Failed to roll back default category seeding for tenant schema {SchemaName}.",
                            schemaName);
                    }
                }
            }
        }
        finally
        {
            if (shouldCloseConnection)
            {
                await connection.CloseAsync();
            }
        }
    }

    private static async Task<bool> CreateTenantSchemaFunctionExistsAsync(NpgsqlConnection connection)
    {
        await using var command = new NpgsqlCommand(
            "SELECT to_regprocedure('public.create_tenant_schema(text)') IS NOT NULL;",
            connection);

        var result = await command.ExecuteScalarAsync();

        return result is bool exists && exists;
    }

    private static async Task<bool> TenantTableExistsAsync(
        NpgsqlConnection connection,
        NpgsqlTransaction transaction,
        string schemaName,
        string tableName)
    {
        await using var command = new NpgsqlCommand(
            """
            SELECT EXISTS (
                SELECT 1
                FROM pg_catalog.pg_class c
                INNER JOIN pg_catalog.pg_namespace n ON n.oid = c.relnamespace
                WHERE n.nspname = @schema_name
                  AND c.relname = @table_name
                  AND c.relkind IN ('r', 'p')
            );
            """,
            connection,
            transaction);

        command.Parameters.AddWithValue("schema_name", schemaName);
        command.Parameters.AddWithValue("table_name", tableName);

        var result = await command.ExecuteScalarAsync();

        return result is bool exists && exists;
    }

    private static string QuoteIdentifier(string identifier)
    {
        return "\"" + identifier.Replace("\"", "\"\"", StringComparison.Ordinal) + "\"";
    }
}
