using System.Reflection;
using MarketFlow.Infrastructure.Persistence.Migrations;
using Microsoft.EntityFrameworkCore.Migrations;
using Microsoft.EntityFrameworkCore.Migrations.Operations;

namespace MarketFlow.Api.Tests.Integration;

public sealed class SaleReferenceNumberMigrationTests
{
    [Theory]
    [InlineData(typeof(AddTenantSaleReferenceNumbers))]
    [InlineData(typeof(OptimizeTenantSaleReferenceNumberEnsureFunction))]
    public void EnsureTenantSaleReferenceNumbers_DoesNotDropOrRecreateHealthyTriggers(Type migrationType)
    {
        var sql = GetUpSql(migrationType);

        Assert.DoesNotContain(
            "DROP TRIGGER IF EXISTS trg_sales_assign_reference_number",
            sql,
            StringComparison.OrdinalIgnoreCase);
        Assert.Contains(
            "IF NOT reference_number_trigger_exists THEN",
            sql,
            StringComparison.OrdinalIgnoreCase);
        Assert.Contains(
            "CREATE TRIGGER trg_sales_assign_reference_number",
            sql,
            StringComparison.OrdinalIgnoreCase);
    }

    private static string GetUpSql(Type migrationType)
    {
        var migration = (Migration)Activator.CreateInstance(migrationType)!;
        var builder = new MigrationBuilder("Npgsql.EntityFrameworkCore.PostgreSQL");
        var up = migrationType.GetMethod(
            "Up",
            BindingFlags.Instance | BindingFlags.NonPublic);

        Assert.NotNull(up);

        up.Invoke(migration, [builder]);

        return string.Join(
            "\n",
            builder.Operations
                .OfType<SqlOperation>()
                .Select(operation => operation.Sql));
    }
}
