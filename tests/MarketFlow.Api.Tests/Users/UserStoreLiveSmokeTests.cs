using MarketFlow.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging.Abstractions;
using Npgsql;

namespace MarketFlow.Api.Tests.Users;

public sealed class UserStoreLiveSmokeTests
{
    private const string TestConnectionStringEnvironmentVariable = "MARKETFLOW_TEST_DB_CONNECTION_STRING";

    [Fact]
    public async Task GetUsersAsync_WhenTenantAssignmentTableIsMissing_ReturnsUsersWithoutAssignments()
    {
        var connectionString = Environment.GetEnvironmentVariable(TestConnectionStringEnvironmentVariable);

        if (string.IsNullOrWhiteSpace(connectionString))
        {
            return;
        }

        var suffix = Guid.NewGuid().ToString("N")[..12];
        var schemaName = $"mf_test_assignment_{suffix}";
        var companyName = $"Assignment Smoke {suffix}";
        var email = $"assignment-smoke-{suffix}@marketflow.test";

        await using var setupConnection = new NpgsqlConnection(connectionString);
        await setupConnection.OpenAsync();

        try
        {
            await ExecuteAsync(
                setupConnection,
                $"CREATE SCHEMA {QuoteIdentifier(schemaName)};");

            var roleId = await ExecuteScalarAsync<int>(
                setupConnection,
                """
                INSERT INTO public.roles (name, description, permissions)
                VALUES ('Seller', 'Creates sales and handles POS operations', '{}'::jsonb)
                ON CONFLICT (name) DO UPDATE SET name = EXCLUDED.name
                RETURNING id;
                """);

            var companyId = await ExecuteScalarAsync<int>(
                setupConnection,
                """
                INSERT INTO public.companies (
                    name,
                    schema_name,
                    company_type,
                    subscription_plan,
                    max_markets,
                    max_users,
                    is_active
                )
                VALUES (
                    @name,
                    @schema_name,
                    'SMALL',
                    'BASIC',
                    5,
                    50,
                    TRUE
                )
                RETURNING id;
                """,
                new NpgsqlParameter("name", companyName),
                new NpgsqlParameter("schema_name", schemaName));

            await ExecuteAsync(
                setupConnection,
                """
                INSERT INTO public.users (
                    full_name,
                    email,
                    password_hash,
                    company_id,
                    role_id,
                    is_active
                )
                VALUES (
                    'Smoke Seller',
                    @email,
                    'not-a-real-password-hash',
                    @company_id,
                    @role_id,
                    TRUE
                );
                """,
                new NpgsqlParameter("email", email),
                new NpgsqlParameter("company_id", companyId),
                new NpgsqlParameter("role_id", roleId));

            var dbContextOptions = new DbContextOptionsBuilder<ApplicationDbContext>()
                .UseNpgsql(connectionString)
                .Options;
            await using var dbContext = new ApplicationDbContext(dbContextOptions);
            var store = new UserStore(dbContext, NullLogger<UserStore>.Instance);

            var users = await store.GetUsersAsync(companyId, includeAllCompanies: false);

            var user = Assert.Single(users);
            Assert.Equal(email, user.Email);
            Assert.Null(user.Assignment);
        }
        finally
        {
            await ExecuteAsync(
                setupConnection,
                "DELETE FROM public.companies WHERE schema_name = @schema_name;",
                new NpgsqlParameter("schema_name", schemaName));

            await ExecuteAsync(
                setupConnection,
                $"DROP SCHEMA IF EXISTS {QuoteIdentifier(schemaName)} CASCADE;");
        }
    }

    private static async Task ExecuteAsync(
        NpgsqlConnection connection,
        string commandText,
        params NpgsqlParameter[] parameters)
    {
        await using var command = new NpgsqlCommand(commandText, connection);
        command.Parameters.AddRange(parameters);
        await command.ExecuteNonQueryAsync();
    }

    private static async Task<T> ExecuteScalarAsync<T>(
        NpgsqlConnection connection,
        string commandText,
        params NpgsqlParameter[] parameters)
    {
        await using var command = new NpgsqlCommand(commandText, connection);
        command.Parameters.AddRange(parameters);
        var result = await command.ExecuteScalarAsync();
        return Assert.IsType<T>(result);
    }

    private static string QuoteIdentifier(string identifier)
    {
        return "\"" + identifier.Replace("\"", "\"\"", StringComparison.Ordinal) + "\"";
    }
}
