using MarketFlow.Application.Features.Companies.DTOs;
using MarketFlow.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging.Abstractions;
using Npgsql;

namespace MarketFlow.Api.Tests.Integration;

public sealed class TenantSchemaProvisioningIntegrationTests
{
    private const string PlPgSqlRaiseExceptionSqlState = "P0001";

    private static readonly string[] RequiredTenantTables =
    [
        "markets",
        "departments",
        "staff_assignments",
        "categories",
        "products",
        "inventory",
        "low_stock_alerts",
        "purchases",
        "purchase_items",
        "sales",
        "sale_items"
    ];

    [PostgresIntegrationFact]
    public async Task CreatingCompany_WithValidSchemaName_CreatesTenantSchemaAndRequiredTables()
    {
        var options = TenantIntegrationTestOptions.FromEnvironment();

        await using var database = new TenantIntegrationTestDatabase(options);
        var schemaName = database.CreateUniqueSchemaName("tenant_provisioning");

        var company = await database.CreateCompanyAsync(
            schemaName,
            name: $"Provisioning {schemaName}",
            dropSchemaOnDispose: true);

        Assert.True(company.Id > 0);
        Assert.Equal(schemaName, company.SchemaName);
        Assert.True(await database.SchemaExistsAsync(schemaName));

        foreach (var tableName in RequiredTenantTables)
        {
            Assert.True(
                await database.TableExistsAsync(schemaName, tableName),
                $"Expected tenant table {schemaName}.{tableName} to exist.");
        }
    }

    [PostgresIntegrationFact]
    public async Task CreatingCompany_WithDuplicateSchemaName_IsRejected()
    {
        var options = TenantIntegrationTestOptions.FromEnvironment();

        await using var database = new TenantIntegrationTestDatabase(options);
        var schemaName = database.CreateUniqueSchemaName("tenant_duplicate");

        await database.CreateCompanyAsync(
            schemaName,
            name: $"Duplicate A {schemaName}",
            dropSchemaOnDispose: true);

        var exception = await Assert.ThrowsAsync<PostgresException>(() =>
            database.CreateCompanyAsync(
                schemaName,
                name: $"Duplicate B {schemaName}",
                dropSchemaOnDispose: true));

        Assert.Equal(PostgresErrorCodes.UniqueViolation, exception.SqlState);
        Assert.Equal(1, await database.CountCompaniesBySchemaNameAsync(schemaName));
    }

    [PostgresIntegrationTheory]
    [InlineData("tenant-provisioning-invalid")]
    [InlineData("tenant provisioning invalid")]
    [InlineData("1tenant_provisioning_invalid")]
    public async Task CreatingCompany_WithInvalidSchemaName_IsRejectedWithoutGlobalRecord(
        string invalidSchemaName)
    {
        var options = TenantIntegrationTestOptions.FromEnvironment();

        await using var database = new TenantIntegrationTestDatabase(options);

        var exception = await Assert.ThrowsAsync<PostgresException>(() =>
            database.CreateCompanyAsync(
                invalidSchemaName,
                name: $"Invalid Schema {Guid.NewGuid():N}"));

        Assert.Equal(PlPgSqlRaiseExceptionSqlState, exception.SqlState);
        Assert.False(await database.SchemaExistsAsync(invalidSchemaName));
        Assert.Equal(0, await database.CountCompaniesBySchemaNameAsync(invalidSchemaName));
    }

    [PostgresIntegrationFact]
    public async Task CompanyOnboarding_WithValidSchemaName_CreatesCompanyAdminAndTenantSchema()
    {
        var options = TenantIntegrationTestOptions.FromEnvironment();

        await using var database = new TenantIntegrationTestDatabase(options);

        await database.EnsureRequiredRolesAsync();

        var schemaName = database.CreateUniqueSchemaName("tenant_onboarding");
        var adminEmail = $"admin-{Guid.NewGuid():N}@marketflow.test";
        await using var dbContext = CreateDbContext(options);
        var store = new CompanyStore(dbContext, NullLogger<CompanyStore>.Instance);

        var onboarding = await store.CreateCompanyAsync(
            CreateCompanyRequest(schemaName, adminEmail),
            schemaName);

        Assert.NotNull(onboarding);
        database.TrackCompanyForCleanup(onboarding.Company.Id, schemaName);

        Assert.True(await database.SchemaExistsAsync(schemaName));
        Assert.Equal(1, await database.CountCompaniesBySchemaNameAsync(schemaName));
        Assert.Equal(1, await database.CountUsersByEmailAsync(adminEmail));
        Assert.Equal(adminEmail, onboarding.CompanyAdmin.Email);
    }

    [PostgresIntegrationFact]
    public async Task CompanyOnboarding_WhenProvisioningFails_RollsBackCompanyAndAdminUser()
    {
        var options = TenantIntegrationTestOptions.FromEnvironment();

        await using var database = new TenantIntegrationTestDatabase(options);

        await database.EnsureRequiredRolesAsync();

        const string invalidSchemaName = "tenant-onboarding-failure";
        var adminEmail = $"admin-{Guid.NewGuid():N}@marketflow.test";
        await using var dbContext = CreateDbContext(options);
        var store = new CompanyStore(dbContext, NullLogger<CompanyStore>.Instance);

        var exception = await Assert.ThrowsAsync<DbUpdateException>(() =>
            store.CreateCompanyAsync(
                CreateCompanyRequest(invalidSchemaName, adminEmail),
                invalidSchemaName));

        var postgresException = FindPostgresException(exception);

        Assert.NotNull(postgresException);
        Assert.Equal(PlPgSqlRaiseExceptionSqlState, postgresException.SqlState);
        Assert.False(await database.SchemaExistsAsync(invalidSchemaName));
        Assert.Equal(0, await database.CountCompaniesBySchemaNameAsync(invalidSchemaName));
        Assert.Equal(0, await database.CountUsersByEmailAsync(adminEmail));
    }

    private static ApplicationDbContext CreateDbContext(TenantIntegrationTestOptions options)
    {
        var dbOptions = new DbContextOptionsBuilder<ApplicationDbContext>()
            .UseNpgsql(options.ConnectionString)
            .Options;

        return new ApplicationDbContext(dbOptions);
    }

    private static CreateCompanyRequest CreateCompanyRequest(string schemaName, string adminEmail)
    {
        return new CreateCompanyRequest
        {
            Name = $"Provisioned Company {Guid.NewGuid():N}",
            CompanyType = "SMALL",
            SchemaName = schemaName,
            CompanyAdmin = new CreateCompanyAdminRequest
            {
                FullName = "Provisioned Admin",
                Email = adminEmail,
                Password = "Admin12345"
            }
        };
    }

    private static PostgresException? FindPostgresException(Exception exception)
    {
        for (var current = exception; current is not null; current = current.InnerException)
        {
            if (current is PostgresException postgresException)
            {
                return postgresException;
            }
        }

        return null;
    }
}
