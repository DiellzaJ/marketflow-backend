using System.Collections.Concurrent;
using System.IdentityModel.Tokens.Jwt;
using System.Security.Claims;
using System.Text;
using BCrypt.Net;
using Microsoft.IdentityModel.Tokens;
using Npgsql;
using NpgsqlTypes;

namespace MarketFlow.Api.Tests.Integration;

public sealed class TenantIntegrationTestDatabase : IAsyncDisposable
{
    private const int MaxPostgresIdentifierLength = 63;
    private const int SchemaSuffixLength = 16;
    private const int SchemaSeparatorLength = 1;

    private static readonly SemaphoreSlim RequiredRolesLock = new(1, 1);
    private static readonly ConcurrentDictionary<string, byte> RequiredRolesEnsuredConnectionStrings =
        new(StringComparer.Ordinal);

    private static readonly IReadOnlyDictionary<string, (string Description, string Permissions)> RequiredRoles =
        new Dictionary<string, (string Description, string Permissions)>
    {
        ["RootAdmin"] = ("Platform administrator", "{\"company\": true, \"companies:read\": true, \"companies:create\": true, \"companies:update\": true, \"companies:delete\": true, \"users:read\": true, \"users:create\": true, \"users:update\": true, \"users:delete\": true}"),
        ["CompanyAdmin"] = ("Company-level administrator", "{\"users:read\": true, \"users:create\": true, \"users:update\": true, \"users:delete\": true, \"products:read\": true, \"products:create\": true, \"products:update\": true, \"products:delete\": true, \"sales:read\": true, \"sales:create\": true, \"sales:update\": true, \"sales:delete\": true, \"inventory:create\": true, \"inventory:read\": true, \"stock:update\": true, \"stock:adjust\": true, \"inventory:delete\": true, \"inventory-movements:read\": true, \"stock:transfer\": true, \"purchases:read\": true, \"purchases:create\": true, \"purchases:update\": true, \"purchases:delete\": true}"),
        ["MainOperator"] = ("Market-level manager", "{\"products:read\": true, \"products:create\": true, \"products:update\": true, \"products:delete\": true, \"sales:read\": true, \"sales:create\": true, \"sales:update\": true, \"sales:delete\": true, \"inventory:create\": true, \"inventory:read\": true, \"stock:update\": true, \"stock:adjust\": true, \"inventory:delete\": true, \"inventory-movements:read\": true, \"stock:transfer\": true, \"purchases:read\": true, \"purchases:create\": true, \"purchases:update\": true, \"purchases:delete\": true}"),
        ["DepartmentManager"] = ("Department-level manager", "{\"products:read\": true, \"inventory:read\": true, \"stock:update\": true, \"stock:adjust\": true, \"inventory-movements:read\": true, \"stock:transfer\": true}"),
        ["InventoryEmployee"] = ("Stock and inventory employee", "{\"products:read\": true, \"inventory:read\": true, \"stock:update\": true, \"stock:adjust\": true, \"inventory-movements:read\": true}"),
        ["Seller"] = ("Creates sales and handles POS operations", "{\"products:read\": true, \"sales:create\": true, \"inventory:read\": true}")
    };

    private readonly TenantIntegrationTestOptions _options;
    private readonly List<int> _createdUserIds = [];
    private readonly List<int> _createdCompanyIds = [];
    private readonly HashSet<string> _createdSchemaNames = new(StringComparer.Ordinal);

    public TenantIntegrationTestDatabase(TenantIntegrationTestOptions options)
    {
        if (string.IsNullOrWhiteSpace(options.ConnectionString))
        {
            throw new ArgumentException("A PostgreSQL connection string is required.", nameof(options));
        }

        _options = options;
    }

    public static bool HasConfiguredConnectionString =>
        !string.IsNullOrWhiteSpace(Environment.GetEnvironmentVariable(
            TenantIntegrationTestOptions.DefaultConnectionStringEnvironmentVariable));

    public static string MissingConnectionStringSkipReason =>
        $"{TenantIntegrationTestOptions.DefaultConnectionStringEnvironmentVariable} is not configured.";

    public string CreateUniqueSchemaName(string prefix = "mf_test")
    {
        var sanitizedPrefix = new string(prefix
            .ToLowerInvariant()
            .Select(character => char.IsLetterOrDigit(character) || character == '_' ? character : '_')
            .ToArray())
            .Trim('_');

        if (string.IsNullOrWhiteSpace(sanitizedPrefix) || !char.IsLetter(sanitizedPrefix[0]))
        {
            sanitizedPrefix = "mf_test";
        }

        var maxPrefixLength = MaxPostgresIdentifierLength - SchemaSeparatorLength - SchemaSuffixLength;

        if (sanitizedPrefix.Length > maxPrefixLength)
        {
            sanitizedPrefix = sanitizedPrefix[..maxPrefixLength].TrimEnd('_');
        }

        if (string.IsNullOrWhiteSpace(sanitizedPrefix))
        {
            sanitizedPrefix = "mf_test";
        }

        var suffix = Guid.NewGuid().ToString("N")[..SchemaSuffixLength];

        return $"{sanitizedPrefix}_{suffix}";
    }

    public async Task EnsureRequiredRolesAsync(CancellationToken cancellationToken = default)
    {
        if (RequiredRolesEnsuredConnectionStrings.ContainsKey(_options.ConnectionString))
        {
            return;
        }

        await RequiredRolesLock.WaitAsync(cancellationToken);

        try
        {
            if (RequiredRolesEnsuredConnectionStrings.ContainsKey(_options.ConnectionString))
            {
                return;
            }

            await using var connection = await OpenConnectionAsync(cancellationToken);

            foreach (var role in RequiredRoles)
            {
                await ExecuteAsync(
                    connection,
                    """
                    INSERT INTO public.roles (name, description, permissions)
                    VALUES (@name, @description, @permissions::jsonb)
                    ON CONFLICT (name) DO UPDATE
                    SET description = EXCLUDED.description,
                        permissions = EXCLUDED.permissions;
                    """,
                    cancellationToken,
                    new NpgsqlParameter("name", role.Key),
                    new NpgsqlParameter("description", role.Value.Description),
                    new NpgsqlParameter("permissions", role.Value.Permissions));
            }

            RequiredRolesEnsuredConnectionStrings.TryAdd(_options.ConnectionString, 0);
        }
        finally
        {
            RequiredRolesLock.Release();
        }
    }

    public async Task<TenantTestCompany> CreateCompanyAsync(
        string? schemaName = null,
        string? name = null,
        bool isActive = true,
        bool dropSchemaOnDispose = false,
        CancellationToken cancellationToken = default)
    {
        var generatedSchemaName = string.IsNullOrWhiteSpace(schemaName);
        schemaName ??= CreateUniqueSchemaName();
        name ??= $"Tenant Test {schemaName}";

        await using var connection = await OpenConnectionAsync(cancellationToken);
        var companyId = await ExecuteScalarAsync<int>(
            connection,
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
                @is_active
            )
            RETURNING id;
            """,
            cancellationToken,
            new NpgsqlParameter("name", name),
            new NpgsqlParameter("schema_name", schemaName),
            new NpgsqlParameter("is_active", isActive));

        _createdCompanyIds.Add(companyId);

        if (generatedSchemaName || dropSchemaOnDispose)
        {
            _createdSchemaNames.Add(schemaName);
        }

        return new TenantTestCompany(companyId, name, schemaName);
    }

    public void TrackCompanyForCleanup(
        int companyId,
        string schemaName,
        bool dropSchemaOnDispose = true)
    {
        _createdCompanyIds.Add(companyId);

        if (dropSchemaOnDispose)
        {
            _createdSchemaNames.Add(schemaName);
        }
    }

    public async Task<TenantTestUser> CreateUserAsync(
        TenantTestCompany company,
        string roleName = "CompanyAdmin",
        bool isActive = true,
        string? email = null,
        string? fullName = null,
        string password = "User12345",
        CancellationToken cancellationToken = default)
    {
        await EnsureRequiredRolesAsync(cancellationToken);

        email ??= $"user-{Guid.NewGuid():N}@marketflow.test";
        fullName ??= $"{roleName} Test User";

        await using var connection = await OpenConnectionAsync(cancellationToken);
        var roleId = await ExecuteScalarAsync<int>(
            connection,
            "SELECT id FROM public.roles WHERE name = @role_name;",
            cancellationToken,
            new NpgsqlParameter("role_name", roleName));

        var userId = await ExecuteScalarAsync<int>(
            connection,
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
                @full_name,
                @email,
                @password_hash,
                @company_id,
                @role_id,
                @is_active
            )
            RETURNING id;
            """,
            cancellationToken,
            new NpgsqlParameter("full_name", fullName),
            new NpgsqlParameter("email", email.Trim().ToLowerInvariant()),
            new NpgsqlParameter("password_hash", BCrypt.Net.BCrypt.HashPassword(password)),
            new NpgsqlParameter("company_id", company.Id),
            new NpgsqlParameter("role_id", roleId),
            new NpgsqlParameter("is_active", isActive));

        _createdUserIds.Add(userId);

        return new TenantTestUser(
            userId,
            company.Id,
            company.SchemaName,
            fullName,
            email.Trim().ToLowerInvariant(),
            roleName,
            isActive);
    }

    public async Task<TenantTestCategory> InsertCategoryAsync(
        string schemaName,
        string? name = null,
        CancellationToken cancellationToken = default)
    {
        name ??= $"Category {Guid.NewGuid():N}"[..24];

        await using var connection = await OpenConnectionAsync(cancellationToken);
        var categoryId = await ExecuteScalarAsync<int>(
            connection,
            $"""
            INSERT INTO {QuoteIdentifier(schemaName)}.categories (name, description, is_active)
            VALUES (@name, @description, TRUE)
            RETURNING id;
            """,
            cancellationToken,
            new NpgsqlParameter("name", name),
            new NpgsqlParameter("description", $"{name} integration test category"));

        return new TenantTestCategory(categoryId, name);
    }

    public async Task<TenantTestMarket> InsertMarketAsync(
        string schemaName,
        string? name = null,
        CancellationToken cancellationToken = default)
    {
        name ??= $"Market {Guid.NewGuid():N}"[..22];

        await using var connection = await OpenConnectionAsync(cancellationToken);
        var marketId = await ExecuteScalarAsync<int>(
            connection,
            $"""
            INSERT INTO {QuoteIdentifier(schemaName)}.markets (name, city, is_active)
            VALUES (@name, @city, TRUE)
            RETURNING id;
            """,
            cancellationToken,
            new NpgsqlParameter("name", name),
            new NpgsqlParameter("city", "Test City"));

        return new TenantTestMarket(marketId, name);
    }

    public async Task<TenantTestProduct> InsertProductAsync(
        string schemaName,
        int? categoryId = null,
        string? name = null,
        string? barcode = null,
        decimal unitPrice = 1.25m,
        CancellationToken cancellationToken = default)
    {
        if (!categoryId.HasValue)
        {
            categoryId = (await InsertCategoryAsync(schemaName, cancellationToken: cancellationToken)).Id;
        }

        name ??= $"Product {Guid.NewGuid():N}"[..23];
        barcode ??= Guid.NewGuid().ToString("N")[..18];

        await using var connection = await OpenConnectionAsync(cancellationToken);
        var productId = await ExecuteScalarAsync<int>(
            connection,
            $"""
            INSERT INTO {QuoteIdentifier(schemaName)}.products (
                name,
                barcode,
                category_id,
                unit_price,
                cost_price,
                tax_rate,
                min_stock_alert,
                is_active
            )
            VALUES (
                @name,
                @barcode,
                @category_id,
                @unit_price,
                0,
                0,
                0,
                TRUE
            )
            RETURNING id;
            """,
            cancellationToken,
            new NpgsqlParameter("name", name),
            new NpgsqlParameter("barcode", barcode),
            new NpgsqlParameter("category_id", categoryId.Value),
            new NpgsqlParameter("unit_price", unitPrice));

        return new TenantTestProduct(productId, name, barcode, categoryId.Value);
    }

    public async Task<bool> SchemaExistsAsync(
        string schemaName,
        CancellationToken cancellationToken = default)
    {
        await using var connection = await OpenConnectionAsync(cancellationToken);

        return await ExecuteScalarAsync<bool>(
            connection,
            """
            SELECT EXISTS (
                SELECT 1
                FROM information_schema.schemata
                WHERE schema_name = @schema_name
            );
            """,
            cancellationToken,
            new NpgsqlParameter("schema_name", schemaName));
    }

    public async Task<bool> TableExistsAsync(
        string schemaName,
        string tableName,
        CancellationToken cancellationToken = default)
    {
        await using var connection = await OpenConnectionAsync(cancellationToken);

        return await ExecuteScalarAsync<bool>(
            connection,
            """
            SELECT EXISTS (
                SELECT 1
                FROM information_schema.tables
                WHERE table_schema = @schema_name
                  AND table_name = @table_name
            );
            """,
            cancellationToken,
            new NpgsqlParameter("schema_name", schemaName),
            new NpgsqlParameter("table_name", tableName));
    }

    public async Task<int> CountRowsAsync(
        string schemaName,
        string tableName,
        CancellationToken cancellationToken = default)
    {
        await using var connection = await OpenConnectionAsync(cancellationToken);

        return await ExecuteScalarAsync<int>(
            connection,
            $"SELECT COUNT(*)::int FROM {QuoteIdentifier(schemaName)}.{QuoteIdentifier(tableName)};",
            cancellationToken);
    }

    public async Task<int> CountProductsByBarcodeAsync(
        string schemaName,
        string barcode,
        CancellationToken cancellationToken = default)
    {
        await using var connection = await OpenConnectionAsync(cancellationToken);

        return await ExecuteScalarAsync<int>(
            connection,
            $"""
            SELECT COUNT(*)::int
            FROM {QuoteIdentifier(schemaName)}.products
            WHERE barcode = @barcode;
            """,
            cancellationToken,
            new NpgsqlParameter("barcode", barcode));
    }

    public async Task<TenantTestProductDetails?> GetProductDetailsAsync(
        string schemaName,
        int productId,
        CancellationToken cancellationToken = default)
    {
        await using var connection = await OpenConnectionAsync(cancellationToken);
        await using var command = new NpgsqlCommand(
            $"""
            SELECT id,
                   name,
                   barcode,
                   category_id,
                   unit_price,
                   cost_price,
                   tax_rate,
                   min_stock_alert,
                   is_active
            FROM {QuoteIdentifier(schemaName)}.products
            WHERE id = @product_id;
            """,
            connection);
        command.Parameters.Add(new NpgsqlParameter("product_id", NpgsqlDbType.Integer)
        {
            Value = productId
        });

        await using var reader = await command.ExecuteReaderAsync(cancellationToken);

        return await reader.ReadAsync(cancellationToken)
            ? new TenantTestProductDetails(
                reader.GetInt32(0),
                reader.GetString(1),
                reader.GetString(2),
                reader.GetInt32(3),
                reader.GetDecimal(4),
                reader.GetDecimal(5),
                reader.GetDecimal(6),
                reader.GetInt32(7),
                reader.GetBoolean(8))
            : null;
    }

    public async Task<int> CountCompaniesBySchemaNameAsync(
        string schemaName,
        CancellationToken cancellationToken = default)
    {
        await using var connection = await OpenConnectionAsync(cancellationToken);

        return await ExecuteScalarAsync<int>(
            connection,
            """
            SELECT COUNT(*)::int
            FROM public.companies
            WHERE schema_name = @schema_name;
            """,
            cancellationToken,
            new NpgsqlParameter("schema_name", schemaName));
    }

    public async Task<int> CountUsersByEmailAsync(
        string email,
        CancellationToken cancellationToken = default)
    {
        await using var connection = await OpenConnectionAsync(cancellationToken);

        return await ExecuteScalarAsync<int>(
            connection,
            """
            SELECT COUNT(*)::int
            FROM public.users
            WHERE email = @email;
            """,
            cancellationToken,
            new NpgsqlParameter("email", email.Trim().ToLowerInvariant()));
    }

    public string GenerateAccessToken(TenantTestUser user)
    {
        var claims = new List<Claim>
        {
            new(JwtRegisteredClaimNames.Jti, Guid.NewGuid().ToString("N")),
            new(ClaimTypes.NameIdentifier, user.Id.ToString()),
            new(ClaimTypes.Email, user.Email),
            new(ClaimTypes.Name, user.FullName),
            new(ClaimTypes.Role, user.RoleName),
            new("company_id", user.CompanyId.ToString()),
            new("schema_name", user.SchemaName)
        };

        var key = new SymmetricSecurityKey(Encoding.UTF8.GetBytes(_options.JwtSecret));
        var credentials = new SigningCredentials(key, SecurityAlgorithms.HmacSha256);

        var token = new JwtSecurityToken(
            issuer: _options.JwtIssuer,
            audience: _options.JwtAudience,
            claims: claims,
            expires: DateTime.UtcNow.AddMinutes(_options.AccessTokenMinutes),
            signingCredentials: credentials);

        return new JwtSecurityTokenHandler().WriteToken(token);
    }

    public async ValueTask DisposeAsync()
    {
        if (string.IsNullOrWhiteSpace(_options.ConnectionString))
        {
            return;
        }

        await using var connection = await OpenConnectionAsync(CancellationToken.None);

        foreach (var schemaName in _createdSchemaNames)
        {
            await ExecuteAsync(
                connection,
                $"DROP SCHEMA IF EXISTS {QuoteIdentifier(schemaName)} CASCADE;",
                CancellationToken.None);
        }

        if (_createdUserIds.Count > 0)
        {
            await ExecuteAsync(
                connection,
                "DELETE FROM public.users WHERE id = ANY (@user_ids);",
                CancellationToken.None,
                new NpgsqlParameter<int[]>("user_ids", _createdUserIds.Distinct().ToArray()));
        }

        if (_createdCompanyIds.Count > 0)
        {
            await ExecuteAsync(
                connection,
                "DELETE FROM public.companies WHERE id = ANY (@company_ids);",
                CancellationToken.None,
                new NpgsqlParameter<int[]>("company_ids", _createdCompanyIds.Distinct().ToArray()));
        }
    }

    private async Task<NpgsqlConnection> OpenConnectionAsync(CancellationToken cancellationToken)
    {
        var connection = new NpgsqlConnection(_options.ConnectionString);
        await connection.OpenAsync(cancellationToken);

        return connection;
    }

    private static async Task ExecuteAsync(
        NpgsqlConnection connection,
        string commandText,
        CancellationToken cancellationToken,
        params NpgsqlParameter[] parameters)
    {
        await using var command = new NpgsqlCommand(commandText, connection);
        command.Parameters.AddRange(parameters);

        await command.ExecuteNonQueryAsync(cancellationToken);
    }

    private static async Task<T> ExecuteScalarAsync<T>(
        NpgsqlConnection connection,
        string commandText,
        CancellationToken cancellationToken,
        params NpgsqlParameter[] parameters)
    {
        await using var command = new NpgsqlCommand(commandText, connection);
        command.Parameters.AddRange(parameters);

        var result = await command.ExecuteScalarAsync(cancellationToken);

        return result is T typedResult
            ? typedResult
            : throw new InvalidOperationException(
                $"Expected scalar result of type {typeof(T).Name}, but got {result?.GetType().Name ?? "null"}.");
    }

    private static string QuoteIdentifier(string identifier)
    {
        return "\"" + identifier.Replace("\"", "\"\"", StringComparison.Ordinal) + "\"";
    }
}
