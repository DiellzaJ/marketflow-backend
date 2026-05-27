using System.Collections.Concurrent;
using System.Data;
using MarketFlow.Application.Common.Interfaces;
using MarketFlow.Application.Common.Models;
using MarketFlow.Application.Features.AI.DTOs;
using MarketFlow.Application.Features.AI.Interfaces;
using MarketFlow.Application.Features.Categories.DTOs;
using MarketFlow.Application.Features.Dashboard.DTOs;
using MarketFlow.Application.Features.Departments.DTOs;
using MarketFlow.Application.Features.Departments.Exceptions;
using MarketFlow.Application.Features.Departments.Interfaces;
using MarketFlow.Application.Features.Inventory.Configuration;
using MarketFlow.Application.Features.Inventory.DTOs;
using MarketFlow.Application.Features.Markets.DTOs;
using MarketFlow.Application.Features.Markets.Exceptions;
using MarketFlow.Application.Features.Markets.Interfaces;
using MarketFlow.Application.Features.Products.Configuration;
using MarketFlow.Application.Features.Products.DTOs;
using MarketFlow.Application.Features.Products.Exceptions;
using MarketFlow.Application.Features.Purchases.DTOs;
using MarketFlow.Application.Features.Sales.Configuration;
using MarketFlow.Application.Features.Sales.DTOs;
using MarketFlow.Application.Features.Suppliers.DTOs;
using MarketFlow.Application.Features.Suppliers.Interfaces;
using MarketFlow.Infrastructure.Caching;
using MarketFlow.Application.Common.Exceptions;
using MarketFlow.Application.Features.Users.Configuration;
using MarketFlow.Infrastructure.MultiTenancy;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Logging;
using Microsoft.EntityFrameworkCore;
using Npgsql;

namespace MarketFlow.Infrastructure.Persistence;

public sealed class TenantQueryService :
    ITenantQueryService,
    IMarketQueryService,
    IMarketStore,
    IDepartmentStore,
    ISupplierStore,
    IAiInventoryForecastDataService,
    IAiInventoryInsightDataService
{
    private const int DefaultBarcodeLookupCacheTtlSeconds = 300;
    private const string InventoryMovementSavepointName = "before_inventory_movement_insert";
    private static readonly TimeSpan SaleReferenceNumberRepairCacheTtl = TimeSpan.FromMinutes(5);

    private static readonly ConcurrentDictionary<string, byte> InventoryMovementTableRepairCache = new();
    private static readonly ConcurrentDictionary<string, DateTimeOffset> SaleReferenceNumberRepairCache = new();

    private readonly ApplicationDbContext _dbContext;
    private readonly TenantProvider _tenantProvider;
    private readonly ICurrentUserService _currentUserService;
    private readonly RedisCacheService? _cacheService;
    private readonly ILogger<TenantQueryService> _logger;
    private readonly TimeSpan _barcodeLookupCacheTtl;

    public TenantQueryService(
        ApplicationDbContext dbContext,
        TenantProvider tenantProvider,
        ICurrentUserService currentUserService,
        ILogger<TenantQueryService> logger,
        RedisCacheService? cacheService = null,
        IConfiguration? configuration = null)
    {
        _dbContext = dbContext;
        _tenantProvider = tenantProvider;
        _currentUserService = currentUserService;
        _cacheService = cacheService;
        _logger = logger;
        _barcodeLookupCacheTtl = TimeSpan.FromSeconds(
            GetBarcodeLookupCacheTtlSeconds(configuration));
    }

    public async Task<PagedResult<ProductDto>> GetProductsAsync(
        ProductListQuery query,
        CancellationToken cancellationToken = default)
    {
        var schemaName = await GetQuotedCurrentSchemaNameAsync(cancellationToken);

        if (IsProductBarcodeLookup(query))
        {
            var cacheKey = CreateBarcodeCacheKey(
                await GetCurrentTenantIdAsync(cancellationToken),
                query.Barcode!);
            var lookupVariantKey = CreateProductBarcodeLookupVariantKey(query);
            var cachedLookup = await GetBarcodeLookupCacheAsync(cacheKey, cancellationToken);

            if (cachedLookup?.ProductLookups.TryGetValue(lookupVariantKey, out var cachedProducts) == true)
            {
                return cachedProducts;
            }

            var lookupResult = await GetProductsFromDatabaseAsync(query, schemaName, cancellationToken);
            cachedLookup ??= new BarcodeLookupCacheEntry();
            cachedLookup.ProductLookups[lookupVariantKey] = lookupResult;
            await SetBarcodeLookupCacheAsync(cacheKey, cachedLookup, cancellationToken);

            return lookupResult;
        }

        return await GetProductsFromDatabaseAsync(query, schemaName, cancellationToken);
    }

    private async Task<PagedResult<ProductDto>> GetProductsFromDatabaseAsync(
        ProductListQuery query,
        string schemaName,
        CancellationToken cancellationToken)
    {
        var products = new List<ProductDto>();
        var whereClause = BuildProductWhereClause(query);
        var sortColumn = GetProductSortColumn(query.SortBy);
        var sortDirection = string.Equals(query.SortDirection, "desc", StringComparison.OrdinalIgnoreCase)
            ? "DESC"
            : "ASC";
        var offset = ((long)query.Page - 1L) * query.PageSize;

        await using var countCommand = await CreateCommandAsync($"""
            SELECT COUNT(*)
            FROM {schemaName}.products p
            LEFT JOIN {schemaName}.categories c ON c.id = p.category_id
            {whereClause};
            """, cancellationToken);

        AddProductListParameters(countCommand, query);
        var totalCount = Convert.ToInt32(await countCommand.ExecuteScalarAsync(cancellationToken));

        await using var command = await CreateCommandAsync($"""
            SELECT p.id,
                   p.name,
                   p.description,
                   p.barcode,
                   p.category_id,
                   c.name AS category_name,
                   p.unit_price,
                   p.cost_price,
                   p.tax_rate,
                   p.min_stock_alert,
                   p.is_active
            FROM {schemaName}.products p
            LEFT JOIN {schemaName}.categories c ON c.id = p.category_id
            {whereClause}
            ORDER BY {sortColumn} {sortDirection}, p.id ASC
            LIMIT @page_size OFFSET @offset;
            """, cancellationToken);

        AddProductListParameters(command, query);
        command.Parameters.AddWithValue("page_size", query.PageSize);
        command.Parameters.AddWithValue("offset", offset);

        await using var reader = await command.ExecuteReaderAsync(cancellationToken);

        while (await reader.ReadAsync(cancellationToken))
        {
            products.Add(ReadProduct(reader));
        }

        return new PagedResult<ProductDto>
        {
            Items = products,
            Page = query.Page,
            PageSize = query.PageSize,
            TotalCount = totalCount,
            TotalPages = totalCount == 0 ? 0 : (int)Math.Ceiling(totalCount / (double)query.PageSize)
        };
    }

    public async Task<ProductDto?> GetProductAsync(
        int id,
        bool includeInactive = false,
        CancellationToken cancellationToken = default)
    {
        var schemaName = await GetQuotedCurrentSchemaNameAsync(cancellationToken);
        var activeCondition = includeInactive ? string.Empty : " AND p.is_active = TRUE";

        await using var command = await CreateCommandAsync($"""
            SELECT p.id,
                   p.name,
                   p.description,
                   p.barcode,
                   p.category_id,
                   c.name AS category_name,
                   p.unit_price,
                   p.cost_price,
                   p.tax_rate,
                   p.min_stock_alert,
                   p.is_active
            FROM {schemaName}.products p
            LEFT JOIN {schemaName}.categories c ON c.id = p.category_id
            WHERE p.id = @id{activeCondition};
            """, cancellationToken);
        command.Parameters.AddWithValue("id", id);

        return await ReadProductAsync(command, cancellationToken);
    }

    public async Task<bool> CategoryExistsAsync(
        int categoryId,
        CancellationToken cancellationToken = default)
    {
        var schemaName = await GetQuotedCurrentSchemaNameAsync(cancellationToken);

        await using var command = await CreateCommandAsync($"""
            SELECT EXISTS (
                SELECT 1
                FROM {schemaName}.categories
                WHERE id = @category_id AND is_active = TRUE
            );
            """, cancellationToken);
        command.Parameters.AddWithValue("category_id", categoryId);

        return (bool)(await command.ExecuteScalarAsync(cancellationToken) ?? false);
    }

    public async Task<bool> ProductBarcodeExistsAsync(
        string barcode,
        int? excludedProductId = null,
        CancellationToken cancellationToken = default)
    {
        var schemaName = await GetQuotedCurrentSchemaNameAsync(cancellationToken);
        var excludedProductCondition = excludedProductId.HasValue
            ? " AND id <> @excluded_product_id"
            : string.Empty;

        await using var command = await CreateCommandAsync($"""
            SELECT EXISTS (
                SELECT 1
                FROM {schemaName}.products
                WHERE barcode = @barcode{excludedProductCondition}
            );
            """, cancellationToken);
        command.Parameters.AddWithValue("barcode", barcode.Trim());

        if (excludedProductId.HasValue)
        {
            command.Parameters.Add(new NpgsqlParameter("excluded_product_id", NpgsqlTypes.NpgsqlDbType.Integer)
            {
                Value = excludedProductId.Value
            });
        }

        return (bool)(await command.ExecuteScalarAsync(cancellationToken) ?? false);
    }

    public async Task<ProductDto> CreateProductAsync(
        CreateProductRequest request,
        CancellationToken cancellationToken = default)
    {
        var schemaName = await GetQuotedCurrentSchemaNameAsync(cancellationToken);

        await using var command = await CreateCommandAsync($"""
            INSERT INTO {schemaName}.products (
                name,
                description,
                barcode,
                category_id,
                unit_price,
                cost_price,
                tax_rate,
                image_url,
                min_stock_alert)
            VALUES (
                @name,
                @description,
                @barcode,
                @category_id,
                @unit_price,
                @cost_price,
                @tax_rate,
                @image_url,
                @min_stock_alert)
            RETURNING id;
            """, cancellationToken);

        AddProductParameters(command, request);

        object? createdIdValue;

        try
        {
            createdIdValue = await command.ExecuteScalarAsync(cancellationToken);
        }
        catch (PostgresException exception) when (IsUniqueViolation(exception))
        {
            throw new ProductBarcodeConflictException();
        }

        var createdId = createdIdValue is int value
            ? value
            : throw new InvalidOperationException("Product was not created.");

        await InvalidateBarcodeLookupAsync(request.Barcode, cancellationToken);

        return await GetProductAsync(createdId, cancellationToken: cancellationToken)
            ?? throw new InvalidOperationException("Product was not found after creation.");
    }

    public async Task<ProductDto?> UpdateProductAsync(
        int id,
        UpdateProductRequest request,
        CancellationToken cancellationToken = default)
    {
        var schemaName = await GetQuotedCurrentSchemaNameAsync(cancellationToken);
        var currentBarcode = await GetProductBarcodeByIdAsync(schemaName, id, cancellationToken);

        await using var command = await CreateCommandAsync($"""
            UPDATE {schemaName}.products
            SET name = @name,
                description = @description,
                barcode = @barcode,
                category_id = @category_id,
                unit_price = @unit_price,
                cost_price = @cost_price,
                tax_rate = @tax_rate,
                image_url = @image_url,
                min_stock_alert = @min_stock_alert
            WHERE id = @id
            RETURNING id;
            """, cancellationToken);

        command.Parameters.AddWithValue("id", id);
        AddProductParameters(command, request);

        object? updatedId;

        try
        {
            updatedId = await command.ExecuteScalarAsync(cancellationToken);
        }
        catch (PostgresException exception) when (IsUniqueViolation(exception))
        {
            throw new ProductBarcodeConflictException();
        }

        if (updatedId is null)
        {
            return null;
        }

        await InvalidateBarcodeLookupAsync(currentBarcode, cancellationToken);
        await InvalidateBarcodeLookupAsync(request.Barcode, cancellationToken);

        return await GetProductAsync(id, includeInactive: true, cancellationToken: cancellationToken);
    }

    public async Task<ProductDto?> PatchProductAsync(
        int id,
        PatchProductRequest request,
        CancellationToken cancellationToken = default)
    {
        var schemaName = await GetQuotedCurrentSchemaNameAsync(cancellationToken);
        var currentBarcode = await GetProductBarcodeByIdAsync(schemaName, id, cancellationToken);

        await using var command = await CreateCommandAsync($"""
            UPDATE {schemaName}.products
            SET name = COALESCE(@name, name),
                description = COALESCE(@description, description),
                barcode = COALESCE(@barcode, barcode),
                category_id = COALESCE(@category_id, category_id),
                unit_price = COALESCE(@unit_price, unit_price),
                cost_price = COALESCE(@cost_price, cost_price),
                tax_rate = COALESCE(@tax_rate, tax_rate),
                image_url = COALESCE(@image_url, image_url),
                min_stock_alert = COALESCE(@min_stock_alert, min_stock_alert)
            WHERE id = @id
            RETURNING id;
            """, cancellationToken);

        command.Parameters.AddWithValue("id", id);
        command.Parameters.AddWithValue("name", DbValue(request.Name?.Trim()));
        command.Parameters.AddWithValue("description", DbValue(request.Description));
        command.Parameters.AddWithValue("barcode", DbValue(request.Barcode));
        command.Parameters.AddWithValue("category_id", DbValue(request.CategoryId));
        command.Parameters.AddWithValue("unit_price", DbValue(request.UnitPrice));
        command.Parameters.AddWithValue("cost_price", DbValue(request.CostPrice));
        command.Parameters.AddWithValue("tax_rate", DbValue(request.TaxRate));
        command.Parameters.AddWithValue("image_url", DbValue(request.ImageUrl));
        command.Parameters.AddWithValue("min_stock_alert", DbValue(request.MinStockAlert));

        object? updatedId;

        try
        {
            updatedId = await command.ExecuteScalarAsync(cancellationToken);
        }
        catch (PostgresException exception) when (IsUniqueViolation(exception))
        {
            throw new ProductBarcodeConflictException();
        }

        if (updatedId is null)
        {
            return null;
        }

        await InvalidateBarcodeLookupAsync(currentBarcode, cancellationToken);
        await InvalidateBarcodeLookupAsync(request.Barcode ?? currentBarcode, cancellationToken);

        return await GetProductAsync(id, includeInactive: true, cancellationToken: cancellationToken);
    }

    public async Task<ProductDto?> SetProductActiveStateAsync(
        int id,
        bool isActive,
        CancellationToken cancellationToken = default)
    {
        var schemaName = await GetQuotedCurrentSchemaNameAsync(cancellationToken);
        var currentBarcode = await GetProductBarcodeByIdAsync(schemaName, id, cancellationToken);

        await using var command = await CreateCommandAsync($"""
            UPDATE {schemaName}.products
            SET is_active = @is_active
            WHERE id = @id
              AND is_active <> @is_active
            RETURNING id;
            """, cancellationToken);
        command.Parameters.AddWithValue("id", id);
        command.Parameters.AddWithValue("is_active", isActive);

        var updatedId = await command.ExecuteScalarAsync(cancellationToken);

        if (updatedId is null)
        {
            return null;
        }

        await InvalidateBarcodeLookupAsync(currentBarcode, cancellationToken);

        return await GetProductAsync(id, includeInactive: true, cancellationToken: cancellationToken);
    }

    public async Task<IReadOnlyCollection<CategoryDto>> GetCategoriesAsync(
        CancellationToken cancellationToken = default)
    {
        var schemaName = await GetQuotedCurrentSchemaNameAsync(cancellationToken);
        var categories = new List<CategoryDto>();

        await using var command = await CreateCommandAsync($"""
            SELECT id, name, description
            FROM {schemaName}.categories
            WHERE is_active = TRUE
            ORDER BY name;
            """, cancellationToken);

        await using var reader = await command.ExecuteReaderAsync(cancellationToken);

        while (await reader.ReadAsync(cancellationToken))
        {
            categories.Add(new CategoryDto
            {
                Id = reader.GetInt32(0),
                Name = reader.GetString(1),
                Description = reader.IsDBNull(2) ? null : reader.GetString(2)
            });
        }

        return categories;
    }

    public async Task<IReadOnlyCollection<MarketDto>> GetMarketsAsync(
        CancellationToken cancellationToken = default)
    {
        var schemaName = await GetQuotedCurrentSchemaNameAsync(cancellationToken);
        var markets = new List<MarketDto>();

        await using var command = await CreateCommandAsync($"""
            SELECT id, name, city, address, is_active
            FROM {schemaName}.markets
            WHERE is_active = TRUE
            ORDER BY name, id;
            """, cancellationToken);

        await using var reader = await command.ExecuteReaderAsync(cancellationToken);

        while (await reader.ReadAsync(cancellationToken))
        {
            markets.Add(new MarketDto
            {
                Id = reader.GetInt32(0),
                Name = reader.GetString(1),
                City = reader.IsDBNull(2) ? null : reader.GetString(2),
                Address = reader.IsDBNull(3) ? null : reader.GetString(3),
                IsActive = reader.GetBoolean(4)
            });
        }

        return markets;
    }

    public async Task<MarketDto?> GetMarketAsync(
        int id,
        bool includeInactive = false,
        CancellationToken cancellationToken = default)
    {
        var schemaName = await GetQuotedCurrentSchemaNameAsync(cancellationToken);
        var activeCondition = includeInactive ? string.Empty : " AND is_active = TRUE";

        await using var command = await CreateCommandAsync($"""
            SELECT id, name, city, address, is_active
            FROM {schemaName}.markets
            WHERE id = @id{activeCondition};
            """, cancellationToken);
        command.Parameters.AddWithValue("id", id);

        return await ReadMarketAsync(command, cancellationToken);
    }

    public async Task<bool> MarketNameExistsAsync(
        string name,
        int? excludedMarketId = null,
        CancellationToken cancellationToken = default)
    {
        var schemaName = await GetQuotedCurrentSchemaNameAsync(cancellationToken);

        return await MarketNameExistsAsync(schemaName, name, excludedMarketId, cancellationToken);
    }

    public async Task<MarketDto> CreateMarketAsync(
        CreateMarketRequest request,
        CancellationToken cancellationToken = default)
    {
        var schemaName = await GetQuotedCurrentSchemaNameAsync(cancellationToken);
        await using var transaction = await BeginTransactionAsync(cancellationToken);

        await LockMarketsTableAsync(schemaName, transaction, cancellationToken);

        if (await MarketNameExistsAsync(schemaName, request.Name, excludedMarketId: null, cancellationToken, transaction))
        {
            await transaction.RollbackAsync(cancellationToken);
            throw new MarketNameConflictException();
        }

        await using var command = await CreateCommandAsync($"""
            INSERT INTO {schemaName}.markets (name, city, address)
            VALUES (@name, @city, @address)
            RETURNING id;
            """, cancellationToken, transaction);

        AddMarketParameters(command, request);

        var createdIdValue = await command.ExecuteScalarAsync(cancellationToken);
        var createdId = createdIdValue is int value
            ? value
            : throw new InvalidOperationException("Market was not created.");

        await transaction.CommitAsync(cancellationToken);

        return await GetMarketAsync(createdId, includeInactive: true, cancellationToken)
            ?? throw new InvalidOperationException("Market was not found after creation.");
    }

    public async Task<MarketDto?> UpdateMarketAsync(
        int id,
        UpdateMarketRequest request,
        CancellationToken cancellationToken = default)
    {
        var schemaName = await GetQuotedCurrentSchemaNameAsync(cancellationToken);
        await using var transaction = await BeginTransactionAsync(cancellationToken);

        await LockMarketsTableAsync(schemaName, transaction, cancellationToken);

        if (!await MarketExistsAsync(schemaName, id, cancellationToken, transaction))
        {
            await transaction.RollbackAsync(cancellationToken);
            return null;
        }

        if (await MarketNameExistsAsync(schemaName, request.Name, id, cancellationToken, transaction))
        {
            await transaction.RollbackAsync(cancellationToken);
            throw new MarketNameConflictException();
        }

        await using var command = await CreateCommandAsync($"""
            UPDATE {schemaName}.markets
            SET name = @name,
                city = @city,
                address = @address
            WHERE id = @id
            RETURNING id;
            """, cancellationToken, transaction);

        command.Parameters.AddWithValue("id", id);
        AddMarketParameters(command, request);

        var updatedId = await command.ExecuteScalarAsync(cancellationToken);

        await transaction.CommitAsync(cancellationToken);

        return updatedId is null
            ? null
            : await GetMarketAsync(id, includeInactive: true, cancellationToken);
    }

    public async Task<MarketDto?> SetMarketActiveStateAsync(
        int id,
        bool isActive,
        CancellationToken cancellationToken = default)
    {
        var schemaName = await GetQuotedCurrentSchemaNameAsync(cancellationToken);

        await using var command = await CreateCommandAsync($"""
            UPDATE {schemaName}.markets
            SET is_active = @is_active
            WHERE id = @id
              AND is_active <> @is_active
            RETURNING id;
            """, cancellationToken);
        command.Parameters.AddWithValue("id", id);
        command.Parameters.AddWithValue("is_active", isActive);

        var updatedId = await command.ExecuteScalarAsync(cancellationToken);

        return updatedId is null
            ? null
            : await GetMarketAsync(id, includeInactive: true, cancellationToken);
    }

    public async Task<IReadOnlyCollection<DepartmentDto>> GetDepartmentsAsync(
        int? marketId = null,
        CancellationToken cancellationToken = default)
    {
        var schemaName = await GetQuotedCurrentSchemaNameAsync(cancellationToken);
        var departments = new List<DepartmentDto>();
        var marketFilter = marketId.HasValue ? " AND market_id = @market_id" : string.Empty;

        await using var command = await CreateCommandAsync($"""
            SELECT id,
                   market_id,
                   name,
                   description,
                   is_active
            FROM {schemaName}.departments
            WHERE is_active = TRUE{marketFilter}
            ORDER BY name, id;
            """, cancellationToken);

        if (marketId.HasValue)
        {
            command.Parameters.Add(new NpgsqlParameter("market_id", NpgsqlTypes.NpgsqlDbType.Integer)
            {
                Value = marketId.Value
            });
        }

        await using var reader = await command.ExecuteReaderAsync(cancellationToken);

        while (await reader.ReadAsync(cancellationToken))
        {
            departments.Add(new DepartmentDto
            {
                Id = reader.GetInt32(0),
                MarketId = reader.GetInt32(1),
                Name = reader.GetString(2),
                Description = reader.IsDBNull(3) ? null : reader.GetString(3),
                IsActive = reader.GetBoolean(4)
            });
        }

        return departments;
    }

    public async Task<DepartmentDto?> GetDepartmentAsync(
        int id,
        bool includeInactive = false,
        CancellationToken cancellationToken = default)
    {
        var schemaName = await GetQuotedCurrentSchemaNameAsync(cancellationToken);

        return await GetDepartmentAsync(
            schemaName,
            id,
            includeInactive,
            cancellationToken);
    }

    private async Task<DepartmentDto?> GetDepartmentAsync(
        string schemaName,
        int id,
        bool includeInactive,
        CancellationToken cancellationToken,
        NpgsqlTransaction? transaction = null)
    {
        var activeCondition = includeInactive ? string.Empty : " AND is_active = TRUE";

        await using var command = await CreateCommandAsync($"""
            SELECT id,
                   market_id,
                   name,
                   description,
                   is_active
            FROM {schemaName}.departments
            WHERE id = @id{activeCondition};
            """, cancellationToken, transaction);
        command.Parameters.AddWithValue("id", id);

        return await ReadDepartmentAsync(command, cancellationToken);
    }

    public async Task<bool> MarketExistsAsync(
        int marketId,
        CancellationToken cancellationToken = default)
    {
        var schemaName = await GetQuotedCurrentSchemaNameAsync(cancellationToken);

        return await MarketExistsAsync(schemaName, marketId, cancellationToken);
    }

    public async Task<bool> DepartmentNameExistsAsync(
        int marketId,
        string name,
        int? excludedDepartmentId = null,
        CancellationToken cancellationToken = default)
    {
        var schemaName = await GetQuotedCurrentSchemaNameAsync(cancellationToken);

        return await DepartmentNameExistsAsync(
            schemaName,
            marketId,
            name,
            excludedDepartmentId,
            cancellationToken);
    }

    public async Task<DepartmentDto> CreateDepartmentAsync(
        CreateDepartmentRequest request,
        CancellationToken cancellationToken = default)
    {
        var schemaName = await GetQuotedCurrentSchemaNameAsync(cancellationToken);
        await using var transaction = await BeginTransactionAsync(cancellationToken);

        await LockDepartmentsTableAsync(schemaName, transaction, cancellationToken);

        if (!await MarketExistsAsync(schemaName, request.MarketId, cancellationToken, transaction))
        {
            await transaction.RollbackAsync(cancellationToken);
            throw new DepartmentMarketNotFoundException();
        }

        if (await DepartmentNameExistsAsync(
                schemaName,
                request.MarketId,
                request.Name,
                excludedDepartmentId: null,
                cancellationToken,
                transaction))
        {
            await transaction.RollbackAsync(cancellationToken);
            throw new DepartmentNameConflictException();
        }

        await using var command = await CreateCommandAsync($"""
            INSERT INTO {schemaName}.departments (market_id, name, description)
            VALUES (@market_id, @name, @description)
            RETURNING id;
            """, cancellationToken, transaction);

        AddDepartmentParameters(command, request);

        object? createdIdValue;

        try
        {
            createdIdValue = await command.ExecuteScalarAsync(cancellationToken);
        }
        catch (PostgresException exception) when (IsForeignKeyViolation(exception))
        {
            await transaction.RollbackAsync(cancellationToken);
            throw new DepartmentMarketNotFoundException();
        }

        var createdId = createdIdValue is int value
            ? value
            : throw new InvalidOperationException("Department was not created.");

        await transaction.CommitAsync(cancellationToken);

        return await GetDepartmentAsync(createdId, includeInactive: true, cancellationToken)
            ?? throw new InvalidOperationException("Department was not found after creation.");
    }

    public async Task<DepartmentDto?> UpdateDepartmentAsync(
        int id,
        UpdateDepartmentRequest request,
        CancellationToken cancellationToken = default)
    {
        var schemaName = await GetQuotedCurrentSchemaNameAsync(cancellationToken);
        await using var transaction = await BeginTransactionAsync(cancellationToken);

        await LockDepartmentsTableAsync(schemaName, transaction, cancellationToken);

        var currentDepartment = await GetDepartmentAsync(
            schemaName,
            id,
            includeInactive: true,
            cancellationToken,
            transaction);

        if (currentDepartment is null)
        {
            await transaction.RollbackAsync(cancellationToken);
            return null;
        }

        if (await DepartmentNameExistsAsync(
                schemaName,
                currentDepartment.MarketId,
                request.Name,
                id,
                cancellationToken,
                transaction))
        {
            await transaction.RollbackAsync(cancellationToken);
            throw new DepartmentNameConflictException();
        }

        await using var command = await CreateCommandAsync($"""
            UPDATE {schemaName}.departments
            SET name = @name,
                description = @description
            WHERE id = @id
            RETURNING id;
            """, cancellationToken, transaction);

        command.Parameters.AddWithValue("id", id);
        AddDepartmentParameters(command, request);

        var updatedId = await command.ExecuteScalarAsync(cancellationToken);

        await transaction.CommitAsync(cancellationToken);

        return updatedId is null
            ? null
            : await GetDepartmentAsync(id, includeInactive: true, cancellationToken);
    }

    public async Task<DepartmentDto?> SetDepartmentActiveStateAsync(
        int id,
        bool isActive,
        CancellationToken cancellationToken = default)
    {
        var schemaName = await GetQuotedCurrentSchemaNameAsync(cancellationToken);

        await using var command = await CreateCommandAsync($"""
            UPDATE {schemaName}.departments
            SET is_active = @is_active
            WHERE id = @id
              AND is_active <> @is_active
            RETURNING id;
            """, cancellationToken);
        command.Parameters.AddWithValue("id", id);
        command.Parameters.AddWithValue("is_active", isActive);

        var updatedId = await command.ExecuteScalarAsync(cancellationToken);

        return updatedId is null
            ? null
            : await GetDepartmentAsync(id, includeInactive: true, cancellationToken);
    }

    public async Task<IReadOnlyCollection<SupplierDto>> GetSuppliersAsync(
        bool includeInactive = false,
        CancellationToken cancellationToken = default)
    {
        var schemaName = await GetQuotedCurrentSchemaNameAsync(cancellationToken);
        var suppliers = new List<SupplierDto>();
        var activeCondition = includeInactive ? string.Empty : "WHERE is_active = TRUE";

        await using var command = await CreateCommandAsync($"""
            SELECT id,
                   name,
                   phone,
                   email,
                   address,
                   is_active,
                   created_at
            FROM {schemaName}.suppliers
            {activeCondition}
            ORDER BY name, id;
            """, cancellationToken);

        await using var reader = await command.ExecuteReaderAsync(cancellationToken);

        while (await reader.ReadAsync(cancellationToken))
        {
            suppliers.Add(ReadSupplier(reader));
        }

        return suppliers;
    }

    public async Task<SupplierDto?> GetSupplierAsync(
        int id,
        bool includeInactive = false,
        CancellationToken cancellationToken = default)
    {
        var schemaName = await GetQuotedCurrentSchemaNameAsync(cancellationToken);

        return await GetSupplierAsync(schemaName, id, includeInactive, cancellationToken);
    }

    private async Task<SupplierDto?> GetSupplierAsync(
        string schemaName,
        int id,
        bool includeInactive,
        CancellationToken cancellationToken,
        NpgsqlTransaction? transaction = null)
    {
        var activeCondition = includeInactive ? string.Empty : " AND is_active = TRUE";

        await using var command = await CreateCommandAsync($"""
            SELECT id,
                   name,
                   phone,
                   email,
                   address,
                   is_active,
                   created_at
            FROM {schemaName}.suppliers
            WHERE id = @id{activeCondition};
            """, cancellationToken, transaction);
        command.Parameters.AddWithValue("id", id);

        return await ReadSupplierAsync(command, cancellationToken);
    }

    public async Task<SupplierDto> CreateSupplierAsync(
        CreateSupplierRequest request,
        CancellationToken cancellationToken = default)
    {
        var schemaName = await GetQuotedCurrentSchemaNameAsync(cancellationToken);

        await using var command = await CreateCommandAsync($"""
            INSERT INTO {schemaName}.suppliers (name, phone, email, address)
            VALUES (@name, @phone, @email, @address)
            RETURNING id;
            """, cancellationToken);

        AddSupplierParameters(command, request.Name, request.Phone, request.Email, request.Address);

        var createdIdValue = await command.ExecuteScalarAsync(cancellationToken);
        var createdId = createdIdValue is int value
            ? value
            : throw new InvalidOperationException("Supplier was not created.");

        return await GetSupplierAsync(createdId, includeInactive: true, cancellationToken)
            ?? throw new InvalidOperationException("Supplier was not found after creation.");
    }

    public async Task<SupplierDto?> UpdateSupplierAsync(
        int id,
        UpdateSupplierRequest request,
        CancellationToken cancellationToken = default)
    {
        var schemaName = await GetQuotedCurrentSchemaNameAsync(cancellationToken);

        await using var command = await CreateCommandAsync($"""
            UPDATE {schemaName}.suppliers
            SET name = @name,
                phone = @phone,
                email = @email,
                address = @address
            WHERE id = @id
            RETURNING id;
            """, cancellationToken);
        command.Parameters.AddWithValue("id", id);
        AddSupplierParameters(command, request.Name, request.Phone, request.Email, request.Address);

        var updatedId = await command.ExecuteScalarAsync(cancellationToken);

        return updatedId is null
            ? null
            : await GetSupplierAsync(id, includeInactive: true, cancellationToken);
    }

    public async Task<SupplierDto?> SetSupplierActiveStateAsync(
        int id,
        bool isActive,
        CancellationToken cancellationToken = default)
    {
        var schemaName = await GetQuotedCurrentSchemaNameAsync(cancellationToken);

        await using var command = await CreateCommandAsync($"""
            UPDATE {schemaName}.suppliers
            SET is_active = @is_active
            WHERE id = @id
              AND is_active <> @is_active
            RETURNING id;
            """, cancellationToken);
        command.Parameters.AddWithValue("id", id);
        command.Parameters.AddWithValue("is_active", isActive);

        var updatedId = await command.ExecuteScalarAsync(cancellationToken);

        return updatedId is null
            ? null
            : await GetSupplierAsync(id, includeInactive: true, cancellationToken);
    }

    public async Task<PagedResult<InventoryItemDto>> GetInventoryAsync(
        InventoryListQuery query,
        CancellationToken cancellationToken = default)
    {
        var schemaName = await GetQuotedCurrentSchemaNameAsync(cancellationToken);
        var scope = await GetCurrentInventoryScopeAsync(schemaName, cancellationToken);

        if (IsInventoryBarcodeLookup(query))
        {
            var cacheKey = CreateBarcodeCacheKey(
                await GetCurrentTenantIdAsync(cancellationToken),
                query.Barcode!);
            var lookupVariantKey = CreateInventoryBarcodeLookupVariantKey(query, scope);
            var cachedLookup = await GetBarcodeLookupCacheAsync(cacheKey, cancellationToken);

            if (cachedLookup?.InventoryLookups.TryGetValue(lookupVariantKey, out var cachedInventory) == true)
            {
                return cachedInventory;
            }

            var lookupResult = await GetInventoryFromDatabaseAsync(query, schemaName, scope, cancellationToken);
            cachedLookup ??= new BarcodeLookupCacheEntry();
            cachedLookup.InventoryLookups[lookupVariantKey] = lookupResult;
            await SetBarcodeLookupCacheAsync(cacheKey, cachedLookup, cancellationToken);

            return lookupResult;
        }

        return await GetInventoryFromDatabaseAsync(query, schemaName, scope, cancellationToken);
    }

    private async Task<PagedResult<InventoryItemDto>> GetInventoryFromDatabaseAsync(
        InventoryListQuery query,
        string schemaName,
        InventoryScope scope,
        CancellationToken cancellationToken)
    {
        var inventory = new List<InventoryItemDto>();
        var whereClause = BuildInventoryWhereClause(query, scope);
        var sortColumn = GetInventorySortColumn(query.SortBy);
        var sortDirection = string.Equals(query.SortDirection, "desc", StringComparison.OrdinalIgnoreCase)
            ? "DESC"
            : "ASC";
        var offset = ((long)query.Page - 1L) * query.PageSize;

        await using var countCommand = await CreateCommandAsync($"""
            SELECT COUNT(*)
            FROM {schemaName}.inventory i
            INNER JOIN {schemaName}.products p ON p.id = i.product_id
            INNER JOIN {schemaName}.markets m ON m.id = i.market_id
            LEFT JOIN {schemaName}.departments d ON d.id = i.department_id
            LEFT JOIN {schemaName}.categories c ON c.id = p.category_id
            {whereClause};
            """, cancellationToken);
        AddInventoryListParameters(countCommand, query);
        AddInventoryScopeParameters(countCommand, scope);
        var totalCount = Convert.ToInt32(await countCommand.ExecuteScalarAsync(cancellationToken));

        await using var command = await CreateCommandAsync($"""
            SELECT i.id,
                   i.product_id,
                   p.name,
                   p.barcode,
                   p.category_id,
                   c.name AS category_name,
                   p.unit_price,
                   p.min_stock_alert,
                   GREATEST(p.min_stock_alert - i.quantity, 0) AS suggested_restock_quantity,
                   i.market_id,
                   m.name,
                   i.department_id,
                   d.name AS department_name,
                   i.quantity,
                   i.reserved_quantity,
                   i.quantity - i.reserved_quantity AS available_quantity,
                   i.quantity <= p.min_stock_alert AS is_low_stock,
                   i.updated_at,
                   i.last_updated_by
            FROM {schemaName}.inventory i
            INNER JOIN {schemaName}.products p ON p.id = i.product_id
            INNER JOIN {schemaName}.markets m ON m.id = i.market_id
            LEFT JOIN {schemaName}.departments d ON d.id = i.department_id
            LEFT JOIN {schemaName}.categories c ON c.id = p.category_id
            {whereClause}
            ORDER BY {sortColumn} {sortDirection}, i.id ASC
            LIMIT @page_size OFFSET @offset;
            """, cancellationToken);
        AddInventoryListParameters(command, query);
        AddInventoryScopeParameters(command, scope);
        command.Parameters.AddWithValue("page_size", query.PageSize);
        command.Parameters.AddWithValue("offset", offset);

        await using var reader = await command.ExecuteReaderAsync(cancellationToken);

        while (await reader.ReadAsync(cancellationToken))
        {
            inventory.Add(ReadInventoryItem(reader));
        }

        return new PagedResult<InventoryItemDto>
        {
            Items = inventory,
            Page = query.Page,
            PageSize = query.PageSize,
            TotalCount = totalCount,
            TotalPages = totalCount == 0 ? 0 : (int)Math.Ceiling(totalCount / (double)query.PageSize)
        };
    }

    public async Task<IReadOnlyCollection<InventoryItemDto>> GetLowStockInventoryAsync(
        CancellationToken cancellationToken = default)
    {
        var schemaName = await GetQuotedCurrentSchemaNameAsync(cancellationToken);
        var scope = await GetCurrentInventoryScopeAsync(schemaName, cancellationToken);
        var inventory = new List<InventoryItemDto>();
        var query = new InventoryListQuery
        {
            LowStockOnly = true,
            SortBy = InventorySortFields.ProductName,
            SortDirection = "asc"
        };
        var whereClause = BuildInventoryWhereClause(query, scope);

        await using var command = await CreateCommandAsync($"""
            SELECT i.id,
                   i.product_id,
                   p.name,
                   p.barcode,
                   p.category_id,
                   c.name AS category_name,
                   p.unit_price,
                   p.min_stock_alert,
                   GREATEST(p.min_stock_alert - i.quantity, 0) AS suggested_restock_quantity,
                   i.market_id,
                   m.name,
                   i.department_id,
                   d.name AS department_name,
                   i.quantity,
                   i.reserved_quantity,
                   i.quantity - i.reserved_quantity AS available_quantity,
                   i.quantity <= p.min_stock_alert AS is_low_stock,
                   i.updated_at,
                   i.last_updated_by
            FROM {schemaName}.inventory i
            INNER JOIN {schemaName}.products p ON p.id = i.product_id
            INNER JOIN {schemaName}.markets m ON m.id = i.market_id
            LEFT JOIN {schemaName}.departments d ON d.id = i.department_id
            LEFT JOIN {schemaName}.categories c ON c.id = p.category_id
            {whereClause}
            ORDER BY p.name ASC, i.id ASC;
            """, cancellationToken);
        AddInventoryScopeParameters(command, scope);

        await using var reader = await command.ExecuteReaderAsync(cancellationToken);

        while (await reader.ReadAsync(cancellationToken))
        {
            inventory.Add(ReadInventoryItem(reader));
        }

        return inventory;
    }

    public async Task<IReadOnlyCollection<PosProductLookupItemDto>?> GetPosProductsAsync(
        PosProductLookupQuery query,
        CancellationToken cancellationToken = default)
    {
        if (!query.MarketId.HasValue)
        {
            return null;
        }

        var schemaName = await GetQuotedCurrentSchemaNameAsync(cancellationToken);
        var scope = await GetCurrentInventoryScopeAsync(schemaName, cancellationToken);
        var target = GetPosInventoryTarget(scope, query.MarketId.Value);

        if (target is null)
        {
            return null;
        }

        var products = new List<PosProductLookupItemDto>();
        var searchCondition = !string.IsNullOrWhiteSpace(query.Barcode)
            ? "p.barcode = @barcode"
            : "p.name ILIKE @search ESCAPE '\\'";
        var departmentCondition = target.DepartmentId.HasValue
            ? "AND i.department_id = @department_id"
            : "AND i.department_id IS NULL";

        await using var command = await CreateCommandAsync($"""
            SELECT p.id,
                   p.name,
                   p.barcode,
                   p.unit_price,
                   GREATEST(COALESCE(SUM(i.quantity - i.reserved_quantity), 0), 0)::int AS available_quantity
            FROM {schemaName}.products p
            LEFT JOIN {schemaName}.inventory i
                ON i.product_id = p.id
               AND i.market_id = @market_id
               {departmentCondition}
            WHERE p.is_active = TRUE
              AND {searchCondition}
            GROUP BY p.id, p.name, p.barcode, p.unit_price
            ORDER BY
                CASE WHEN p.barcode = @exact_lookup THEN 0 ELSE 1 END,
                p.name ASC,
                p.id ASC
            LIMIT @limit;
            """, cancellationToken);

        command.Parameters.AddWithValue("market_id", target.MarketId);
        command.Parameters.AddWithValue("exact_lookup", query.Barcode ?? string.Empty);
        command.Parameters.AddWithValue("limit", query.Limit);

        if (target.DepartmentId.HasValue)
        {
            command.Parameters.AddWithValue("department_id", target.DepartmentId.Value);
        }

        if (!string.IsNullOrWhiteSpace(query.Barcode))
        {
            command.Parameters.AddWithValue("barcode", query.Barcode);
        }
        else
        {
            command.Parameters.AddWithValue("search", LikePattern(query.Search!));
        }

        await using var reader = await command.ExecuteReaderAsync(cancellationToken);

        while (await reader.ReadAsync(cancellationToken))
        {
            products.Add(new PosProductLookupItemDto
            {
                ProductId = reader.GetInt32(0),
                ProductName = reader.GetString(1),
                Barcode = reader.IsDBNull(2) ? null : reader.GetString(2),
                UnitPrice = reader.GetDecimal(3),
                AvailableQuantity = reader.GetInt32(4)
            });
        }

        return products;
    }

    public async Task<InventoryItemDto?> CreateInventoryItemAsync(
        CreateInventoryItemRequest request,
        int? updatedByUserId,
        CancellationToken cancellationToken = default)
    {
        var schemaName = await GetQuotedCurrentSchemaNameAsync(cancellationToken);
        var scope = await GetCurrentInventoryScopeAsync(schemaName, cancellationToken);

        if (!CanAccessInventoryTarget(scope, request.MarketId, request.DepartmentId))
        {
            return null;
        }

        var productBarcode = await GetProductBarcodeByIdAsync(schemaName, request.ProductId, cancellationToken);

        await using var transaction = await BeginTransactionAsync(cancellationToken);
        await using var command = await CreateCommandAsync($"""
            INSERT INTO {schemaName}.inventory (
                product_id,
                market_id,
                department_id,
                quantity,
                reserved_quantity,
                last_updated_by)
            VALUES (
                @product_id,
                @market_id,
                @department_id,
                @quantity,
                @reserved_quantity,
                @last_updated_by)
            RETURNING id;
            """, cancellationToken, transaction);

        command.Parameters.AddWithValue("product_id", request.ProductId);
        command.Parameters.AddWithValue("market_id", request.MarketId);
        command.Parameters.AddWithValue("department_id", DbValue(request.DepartmentId));
        command.Parameters.AddWithValue("quantity", request.Quantity);
        command.Parameters.AddWithValue("reserved_quantity", request.ReservedQuantity);
        command.Parameters.AddWithValue("last_updated_by", DbValue(updatedByUserId));

        var createdId = (int?)await command.ExecuteScalarAsync(cancellationToken)
            ?? throw new InvalidOperationException("Inventory item was not created.");

        if (request.Quantity != 0)
        {
            await RecordInventoryMovementAsync(
                schemaName,
                createdId,
                "InitialStock",
                request.Quantity,
                updatedByUserId,
                "inventory-create",
                cancellationToken,
                transaction);
        }

        await transaction.CommitAsync(cancellationToken);
        await InvalidateBarcodeLookupAsync(productBarcode, cancellationToken);

        return await GetInventoryItemAsync(createdId, cancellationToken)
            ?? throw new InvalidOperationException("Inventory item was not found after creation.");
    }

    public async Task<InventoryItemDto?> UpdateInventoryItemAsync(
        int id,
        UpdateInventoryItemRequest request,
        int? updatedByUserId,
        CancellationToken cancellationToken = default)
    {
        var schemaName = await GetQuotedCurrentSchemaNameAsync(cancellationToken);
        var scope = await GetCurrentInventoryScopeAsync(schemaName, cancellationToken);
        var scopeCondition = BuildInventoryScopeCondition(scope, "AND", columnQualifier: string.Empty);
        var currentBarcode = await GetInventoryItemBarcodeByIdAsync(schemaName, id, cancellationToken);

        await using var transaction = await BeginTransactionAsync(cancellationToken);
        await using var command = await CreateCommandAsync($"""
            WITH target AS (
                SELECT id, quantity
                FROM {schemaName}.inventory
                WHERE id = @id
                  {scopeCondition}
                FOR UPDATE
            ),
            updated AS (
                UPDATE {schemaName}.inventory i
                SET quantity = @quantity,
                    reserved_quantity = @reserved_quantity,
                    last_updated_by = @last_updated_by,
                    updated_at = NOW()
                FROM target t
                WHERE i.id = t.id
                RETURNING i.id, @quantity - t.quantity AS quantity_delta
            )
            SELECT id, quantity_delta
            FROM updated;
            """, cancellationToken, transaction);

        command.Parameters.AddWithValue("id", id);
        command.Parameters.AddWithValue("quantity", request.Quantity);
        command.Parameters.AddWithValue("reserved_quantity", request.ReservedQuantity);
        command.Parameters.AddWithValue("last_updated_by", DbValue(updatedByUserId));
        AddInventoryScopeParameters(command, scope);

        await using var reader = await command.ExecuteReaderAsync(cancellationToken);

        if (!await reader.ReadAsync(cancellationToken))
        {
            return null;
        }

        var updatedId = reader.GetInt32(0);
        var quantityDelta = reader.GetInt32(1);
        await reader.DisposeAsync();

        if (quantityDelta != 0)
        {
            await RecordInventoryMovementAsync(
                schemaName,
                id,
                "ManualAdjustment",
                quantityDelta,
                updatedByUserId,
                "inventory-update",
                cancellationToken,
                transaction);
        }

        await transaction.CommitAsync(cancellationToken);
        await InvalidateBarcodeLookupAsync(currentBarcode, cancellationToken);

        return await GetInventoryItemAsync(id, cancellationToken);
    }

    public async Task<bool> DeleteInventoryItemAsync(
        int id,
        CancellationToken cancellationToken = default)
    {
        var schemaName = await GetQuotedCurrentSchemaNameAsync(cancellationToken);
        var scope = await GetCurrentInventoryScopeAsync(schemaName, cancellationToken);
        var scopeCondition = BuildInventoryScopeCondition(scope, "AND", columnQualifier: string.Empty);
        var currentBarcode = await GetInventoryItemBarcodeByIdAsync(schemaName, id, cancellationToken);

        await using var command = await CreateCommandAsync($"""
            DELETE FROM {schemaName}.inventory
            WHERE id = @id
              {scopeCondition};
            """, cancellationToken);
        command.Parameters.AddWithValue("id", id);
        AddInventoryScopeParameters(command, scope);

        var deleted = await command.ExecuteNonQueryAsync(cancellationToken) > 0;

        if (deleted)
        {
            await InvalidateBarcodeLookupAsync(currentBarcode, cancellationToken);
        }

        return deleted;
    }

    public async Task<InventoryItemDto?> PatchInventoryItemAsync(
        int id,
        PatchInventoryItemRequest request,
        int? updatedByUserId,
        CancellationToken cancellationToken = default)
    {
        var schemaName = await GetQuotedCurrentSchemaNameAsync(cancellationToken);
        var scope = await GetCurrentInventoryScopeAsync(schemaName, cancellationToken);
        var scopeCondition = BuildInventoryScopeCondition(scope, "AND", columnQualifier: string.Empty);
        var currentBarcode = await GetInventoryItemBarcodeByIdAsync(schemaName, id, cancellationToken);

        await using var transaction = await BeginTransactionAsync(cancellationToken);
        await using var command = await CreateCommandAsync($"""
            WITH target AS (
                SELECT id, quantity
                FROM {schemaName}.inventory
                WHERE id = @id
                  {scopeCondition}
                FOR UPDATE
            ),
            updated AS (
                UPDATE {schemaName}.inventory i
                SET quantity = COALESCE(@quantity, i.quantity),
                    reserved_quantity = COALESCE(@reserved_quantity, i.reserved_quantity),
                    last_updated_by = @last_updated_by,
                    updated_at = NOW()
                FROM target t
                WHERE i.id = t.id
                RETURNING i.id, COALESCE(@quantity, i.quantity) - t.quantity AS quantity_delta
            )
            SELECT id, quantity_delta
            FROM updated;
            """, cancellationToken, transaction);

        command.Parameters.AddWithValue("id", id);
        command.Parameters.AddWithValue("quantity", DbValue(request.Quantity));
        command.Parameters.AddWithValue("reserved_quantity", DbValue(request.ReservedQuantity));
        command.Parameters.AddWithValue("last_updated_by", DbValue(updatedByUserId));
        AddInventoryScopeParameters(command, scope);

        await using var reader = await command.ExecuteReaderAsync(cancellationToken);

        if (!await reader.ReadAsync(cancellationToken))
        {
            return null;
        }

        var updatedId = reader.GetInt32(0);
        var quantityDelta = reader.GetInt32(1);
        await reader.DisposeAsync();

        if (quantityDelta != 0)
        {
            await RecordInventoryMovementAsync(
                schemaName,
                id,
                "ManualAdjustment",
                quantityDelta,
                updatedByUserId,
                "stock-adjust",
                cancellationToken,
                transaction);
        }

        await transaction.CommitAsync(cancellationToken);
        await InvalidateBarcodeLookupAsync(currentBarcode, cancellationToken);

        return await GetInventoryItemAsync(id, cancellationToken);
    }

    public async Task<InventoryItemDto?> AdjustInventoryItemAsync(
        int id,
        AdjustInventoryRequest request,
        int? updatedByUserId,
        CancellationToken cancellationToken = default)
    {
        var schemaName = await GetQuotedCurrentSchemaNameAsync(cancellationToken);
        var scope = await GetCurrentInventoryScopeAsync(schemaName, cancellationToken);
        var scopeCondition = BuildInventoryScopeCondition(scope, "AND", columnQualifier: string.Empty);
        var currentBarcode = await GetInventoryItemBarcodeByIdAsync(schemaName, id, cancellationToken);

        await using var transaction = await BeginTransactionAsync(cancellationToken);
        await using var command = await CreateCommandAsync($"""
            WITH target AS (
                SELECT id, quantity
                FROM {schemaName}.inventory
                WHERE id = @id
                  {scopeCondition}
                FOR UPDATE
            ),
            updated AS (
                UPDATE {schemaName}.inventory i
                SET quantity = t.quantity + @quantity_change,
                    last_updated_by = @last_updated_by,
                    updated_at = NOW()
                FROM target t
                WHERE i.id = t.id
                  AND t.quantity + @quantity_change >= 0
                RETURNING i.id
            )
            SELECT id
            FROM updated;
            """, cancellationToken, transaction);

        command.Parameters.AddWithValue("id", id);
        command.Parameters.AddWithValue("quantity_change", request.QuantityChange);
        command.Parameters.AddWithValue("last_updated_by", DbValue(updatedByUserId));
        AddInventoryScopeParameters(command, scope);

        var updatedId = await command.ExecuteScalarAsync(cancellationToken);

        if (updatedId is null)
        {
            return null;
        }

        await RecordInventoryMovementAsync(
            schemaName,
            id,
            "ManualAdjustment",
            request.QuantityChange,
            updatedByUserId,
            "manual-adjustment",
            cancellationToken,
            transaction,
            request.Reason,
            request.Note);

        await transaction.CommitAsync(cancellationToken);
        await InvalidateBarcodeLookupAsync(currentBarcode, cancellationToken);

        return await GetInventoryItemAsync(id, cancellationToken);
    }

    public async Task<bool> TransferInventoryAsync(
        TransferInventoryRequest request,
        int? updatedByUserId,
        CancellationToken cancellationToken = default)
    {
        var schemaName = await GetQuotedCurrentSchemaNameAsync(cancellationToken);
        var scope = await GetCurrentInventoryScopeAsync(schemaName, cancellationToken);
        var scopeCondition = BuildInventoryScopeCondition(scope, "AND", columnQualifier: string.Empty);
        var productBarcode = await GetProductBarcodeByIdAsync(schemaName, request.ProductId, cancellationToken);

        await using var transaction = await BeginTransactionAsync(cancellationToken);
        await using var command = await CreateCommandAsync($"""
            WITH source AS (
                SELECT id, product_id, quantity
                FROM {schemaName}.inventory
                WHERE product_id = @product_id
                  AND market_id = @from_market_id
                  AND department_id IS NOT DISTINCT FROM @from_department_id
                  {scopeCondition}
                FOR UPDATE
            ),
            destination AS (
                SELECT id, product_id
                FROM {schemaName}.inventory
                WHERE product_id = @product_id
                  AND market_id = @to_market_id
                  AND department_id IS NOT DISTINCT FROM @to_department_id
                  {scopeCondition}
                FOR UPDATE
            ),
            updated_source AS (
                UPDATE {schemaName}.inventory i
                SET quantity = i.quantity - @quantity,
                    last_updated_by = @last_updated_by,
                    updated_at = NOW()
                FROM source s, destination d
                WHERE i.id = s.id
                  AND s.product_id = d.product_id
                  AND s.quantity >= @quantity
                RETURNING i.id
            ),
            updated_destination AS (
                UPDATE {schemaName}.inventory i
                SET quantity = i.quantity + @quantity,
                    last_updated_by = @last_updated_by,
                    updated_at = NOW()
                FROM destination d, updated_source us
                WHERE i.id = d.id
                RETURNING i.id
            )
            SELECT (SELECT id FROM updated_source), (SELECT id FROM updated_destination);
            """, cancellationToken, transaction);

        command.Parameters.AddWithValue("product_id", request.ProductId);
        command.Parameters.AddWithValue("from_market_id", request.FromMarketId);
        command.Parameters.AddWithValue("from_department_id", DbValue(request.FromDepartmentId));
        command.Parameters.AddWithValue("to_market_id", request.ToMarketId);
        command.Parameters.AddWithValue("to_department_id", DbValue(request.ToDepartmentId));
        command.Parameters.AddWithValue("quantity", request.Quantity);
        command.Parameters.AddWithValue("last_updated_by", DbValue(updatedByUserId));
        AddInventoryScopeParameters(command, scope);

        await using var reader = await command.ExecuteReaderAsync(cancellationToken);

        if (!await reader.ReadAsync(cancellationToken) || reader.IsDBNull(0) || reader.IsDBNull(1))
        {
            return false;
        }

        var sourceInventoryId = reader.GetInt32(0);
        var destinationInventoryId = reader.GetInt32(1);
        var referenceNumber = $"transfer:{sourceInventoryId}:{destinationInventoryId}";
        await reader.DisposeAsync();

        await RecordInventoryMovementAsync(
            schemaName,
            sourceInventoryId,
            "TransferOut",
            -request.Quantity,
            updatedByUserId,
            referenceNumber,
            cancellationToken,
            transaction,
            note: request.Note);

        await RecordInventoryMovementAsync(
            schemaName,
            destinationInventoryId,
            "TransferIn",
            request.Quantity,
            updatedByUserId,
            referenceNumber,
            cancellationToken,
            transaction,
            note: request.Note);

        await transaction.CommitAsync(cancellationToken);
        await InvalidateBarcodeLookupAsync(productBarcode, cancellationToken);
        return true;
    }

    public async Task<IReadOnlyCollection<SaleDto>> GetSalesAsync(
        CancellationToken cancellationToken = default)
    {
        var schemaName = await GetQuotedCurrentSchemaNameAsync(cancellationToken);

        return await ExecuteWithSaleReferenceNumberRecoveryAsync(
            schemaName,
            () => GetSalesCoreAsync(schemaName, cancellationToken),
            cancellationToken);
    }

    private async Task<IReadOnlyCollection<SaleDto>> GetSalesCoreAsync(
        string schemaName,
        CancellationToken cancellationToken)
    {
        var scope = await GetCurrentInventoryScopeAsync(schemaName, cancellationToken);
        var scopeCondition = BuildSalesScopeCondition(scope, "WHERE");
        var sales = new List<SaleDto>();

        await using var command = await CreateCommandAsync($"""
            SELECT s.id, s.reference_number, s.market_id, s.sale_date, s.payment_method, s.total_amount
            FROM {schemaName}.sales s
            {scopeCondition}
            ORDER BY s.sale_date DESC, s.id DESC;
            """, cancellationToken);
        AddInventoryScopeParameters(command, scope);

        await using var reader = await command.ExecuteReaderAsync(cancellationToken);

        while (await reader.ReadAsync(cancellationToken))
        {
            sales.Add(new SaleDto
            {
                Id = reader.GetInt32(0),
                ReferenceNumber = reader.GetString(1),
                MarketId = reader.GetInt32(2),
                SaleDate = reader.GetFieldValue<DateOnly>(3),
                PaymentMethod = reader.GetString(4),
                TotalAmount = reader.GetDecimal(5)
            });
        }

        return sales;
    }

    public async Task<PagedResult<SaleHistoryItemDto>> GetSalesHistoryAsync(
        SaleHistoryQuery query,
        CancellationToken cancellationToken = default)
    {
        var schemaName = await GetQuotedCurrentSchemaNameAsync(cancellationToken);

        return await ExecuteWithSaleReferenceNumberRecoveryAsync(
            schemaName,
            () => GetSalesHistoryCoreAsync(schemaName, query, cancellationToken),
            cancellationToken);
    }

    private async Task<PagedResult<SaleHistoryItemDto>> GetSalesHistoryCoreAsync(
        string schemaName,
        SaleHistoryQuery query,
        CancellationToken cancellationToken)
    {
        var scope = await GetCurrentInventoryScopeAsync(schemaName, cancellationToken);
        var whereClause = BuildSalesHistoryWhereClause(query, scope);
        var sortColumn = GetSalesHistorySortColumn(query.SortBy);
        var sortDirection = string.Equals(query.SortDirection, "asc", StringComparison.OrdinalIgnoreCase)
            ? "ASC"
            : "DESC";
        var offset = ((long)query.Page - 1L) * query.PageSize;
        var sales = new List<SaleHistoryItemDto>();

        await using var countCommand = await CreateCommandAsync($"""
            SELECT COUNT(*)
            FROM {schemaName}.sales s
            LEFT JOIN {schemaName}.markets m ON m.id = s.market_id
            LEFT JOIN public.users u ON u.id = s.created_by_user_id
            {whereClause};
            """, cancellationToken);
        AddSalesHistoryParameters(countCommand, query);
        AddInventoryScopeParameters(countCommand, scope);
        var totalCount = Convert.ToInt32(await countCommand.ExecuteScalarAsync(cancellationToken));

        await using var command = await CreateCommandAsync($"""
            SELECT s.id,
                   s.reference_number,
                   s.sale_date,
                   s.created_at,
                   s.market_id,
                   m.name AS market_name,
                   s.created_by_user_id,
                   u.full_name AS cashier_name,
                   s.status,
                   s.payment_method,
                   s.total_amount,
                   COALESCE(items.item_count, 0) AS item_count
            FROM {schemaName}.sales s
            LEFT JOIN {schemaName}.markets m ON m.id = s.market_id
            LEFT JOIN public.users u ON u.id = s.created_by_user_id
            LEFT JOIN (
                SELECT sale_id, COUNT(*)::int AS item_count
                FROM {schemaName}.sale_items
                GROUP BY sale_id
            ) items ON items.sale_id = s.id
            {whereClause}
            ORDER BY {sortColumn} {sortDirection}, s.id DESC
            LIMIT @page_size OFFSET @offset;
            """, cancellationToken);
        AddSalesHistoryParameters(command, query);
        AddInventoryScopeParameters(command, scope);
        command.Parameters.AddWithValue("page_size", query.PageSize);
        command.Parameters.AddWithValue("offset", offset);

        await using var reader = await command.ExecuteReaderAsync(cancellationToken);

        while (await reader.ReadAsync(cancellationToken))
        {
            sales.Add(new SaleHistoryItemDto
            {
                Id = reader.GetInt32(0),
                ReferenceNumber = reader.GetString(1),
                SaleDate = reader.GetFieldValue<DateOnly>(2),
                CreatedAt = reader.GetFieldValue<DateTimeOffset>(3),
                MarketId = reader.GetInt32(4),
                MarketName = reader.IsDBNull(5) ? null : reader.GetString(5),
                CashierUserId = reader.GetInt32(6),
                CashierName = reader.IsDBNull(7) ? null : reader.GetString(7),
                Status = reader.GetString(8),
                PaymentMethod = reader.GetString(9),
                TotalAmount = reader.GetDecimal(10),
                ItemCount = reader.GetInt32(11)
            });
        }

        return new PagedResult<SaleHistoryItemDto>
        {
            Items = sales,
            Page = query.Page,
            PageSize = query.PageSize,
            TotalCount = totalCount,
            TotalPages = totalCount == 0 ? 0 : (int)Math.Ceiling(totalCount / (double)query.PageSize)
        };
    }

    public async Task<SalesSummaryDto> GetSalesSummaryAsync(
        SalesSummaryQuery query,
        CancellationToken cancellationToken = default)
    {
        var schemaName = await GetQuotedCurrentSchemaNameAsync(cancellationToken);

        return await ExecuteWithSaleReferenceNumberRecoveryAsync(
            schemaName,
            () => GetSalesSummaryCoreAsync(schemaName, query, cancellationToken),
            cancellationToken);
    }

    private async Task<SalesSummaryDto> GetSalesSummaryCoreAsync(
        string schemaName,
        SalesSummaryQuery query,
        CancellationToken cancellationToken)
    {
        var scope = await GetCurrentInventoryScopeAsync(schemaName, cancellationToken);
        var whereClause = BuildSalesSummaryWhereClause(query, scope);

        await using var command = await CreateCommandAsync($"""
            WITH filtered_sales AS (
                SELECT s.id,
                       s.total_amount
                FROM {schemaName}.sales s
                {whereClause}
            ),
            filtered_sale_items AS (
                SELECT si.sale_id,
                       SUM(si.quantity)::bigint AS total_quantity
                FROM {schemaName}.sale_items si
                INNER JOIN filtered_sales fs ON fs.id = si.sale_id
                GROUP BY si.sale_id
            )
            SELECT COALESCE(SUM(fs.total_amount), 0) AS total_revenue,
                   COUNT(*)::bigint AS total_sales,
                   COALESCE(SUM(COALESCE(items.total_quantity, 0)), 0)::bigint AS total_items_sold
            FROM filtered_sales fs
            LEFT JOIN filtered_sale_items items ON items.sale_id = fs.id;
            """, cancellationToken);
        AddSalesSummaryParameters(command, query);
        AddInventoryScopeParameters(command, scope);

        await using var reader = await command.ExecuteReaderAsync(cancellationToken);

        if (!await reader.ReadAsync(cancellationToken))
        {
            return new SalesSummaryDto();
        }

        var totalRevenue = reader.GetDecimal(0);
        var totalSales = reader.GetInt64(1);
        var totalItemsSold = reader.GetInt64(2);

        return new SalesSummaryDto
        {
            TotalRevenue = totalRevenue,
            TotalSales = totalSales,
            TotalItemsSold = totalItemsSold,
            AverageSaleAmount = totalSales == 0 ? 0 : totalRevenue / totalSales
        };
    }

    public async Task<AiBusinessDataDto> GetAiBusinessDataAsync(
        AiBusinessDataQuery query,
        CancellationToken cancellationToken = default)
    {
        var schemaName = await GetQuotedCurrentSchemaNameAsync(cancellationToken);
        var scope = await GetCurrentInventoryScopeAsync(schemaName, cancellationToken);

        return new AiBusinessDataDto
        {
            Filters = new AiBusinessDataFilterDto
            {
                From = query.From,
                To = query.To,
                MarketId = query.MarketId,
                DepartmentId = query.DepartmentId
            },
            SalesMetrics = await GetAiSalesMetricsAsync(schemaName, query, scope, cancellationToken),
            TopSellingProducts = await GetAiTopSellingProductsAsync(schemaName, query, scope, cancellationToken),
            LowStockProducts = await GetAiLowStockProductsAsync(schemaName, query, scope, cancellationToken),
            InventoryMovementMetrics = await GetAiInventoryMovementMetricsAsync(schemaName, query, scope, cancellationToken),
            SupplierPurchaseMetrics = await GetAiSupplierPurchaseMetricsAsync(schemaName, query, scope, cancellationToken)
        };
    }

    private async Task<AiSalesMetricsDto> GetAiSalesMetricsAsync(
        string schemaName,
        AiBusinessDataQuery query,
        InventoryScope scope,
        CancellationToken cancellationToken)
    {
        var whereClause = BuildAiSalesWhereClause(query, scope);

        await using var command = await CreateCommandAsync($"""
            WITH filtered_sales AS (
                SELECT s.id,
                       s.total_amount
                FROM {schemaName}.sales s
                {whereClause}
            ),
            filtered_sale_items AS (
                SELECT si.sale_id,
                       SUM(si.quantity)::bigint AS total_quantity
                FROM {schemaName}.sale_items si
                INNER JOIN filtered_sales fs ON fs.id = si.sale_id
                GROUP BY si.sale_id
            )
            SELECT COALESCE(SUM(fs.total_amount), 0) AS total_revenue,
                   COUNT(*)::bigint AS total_sales,
                   COALESCE(SUM(COALESCE(items.total_quantity, 0)), 0)::bigint AS total_items_sold
            FROM filtered_sales fs
            LEFT JOIN filtered_sale_items items ON items.sale_id = fs.id;
            """, cancellationToken);
        AddAiBusinessDataParameters(command, query);
        AddInventoryScopeParameters(command, scope);

        await using var reader = await command.ExecuteReaderAsync(cancellationToken);

        if (!await reader.ReadAsync(cancellationToken))
        {
            return new AiSalesMetricsDto();
        }

        var totalRevenue = reader.GetDecimal(0);
        var totalSales = reader.GetInt64(1);

        return new AiSalesMetricsDto
        {
            TotalRevenue = totalRevenue,
            TotalSales = totalSales,
            TotalItemsSold = reader.GetInt64(2),
            AverageSaleAmount = totalSales == 0 ? 0 : totalRevenue / totalSales
        };
    }

    private async Task<IReadOnlyCollection<AiTopSellingProductDto>> GetAiTopSellingProductsAsync(
        string schemaName,
        AiBusinessDataQuery query,
        InventoryScope scope,
        CancellationToken cancellationToken)
    {
        var products = new List<AiTopSellingProductDto>();
        var whereClause = BuildAiSalesWhereClause(query, scope);

        await using var command = await CreateCommandAsync($"""
            SELECT si.product_id,
                   COALESCE(p.name, '') AS product_name,
                   SUM(si.quantity)::bigint AS quantity_sold,
                   COALESCE(SUM(si.line_total), 0) AS revenue
            FROM {schemaName}.sale_items si
            INNER JOIN {schemaName}.sales s ON s.id = si.sale_id
            LEFT JOIN {schemaName}.products p ON p.id = si.product_id
            {whereClause}
            GROUP BY si.product_id, p.name
            ORDER BY quantity_sold DESC, revenue DESC, product_name ASC
            LIMIT @top_products_limit;
            """, cancellationToken);
        AddAiBusinessDataParameters(command, query);
        AddInventoryScopeParameters(command, scope);
        command.Parameters.AddWithValue("top_products_limit", query.TopProductsLimit);

        await using var reader = await command.ExecuteReaderAsync(cancellationToken);

        while (await reader.ReadAsync(cancellationToken))
        {
            products.Add(new AiTopSellingProductDto
            {
                ProductId = reader.GetInt32(0),
                ProductName = reader.GetString(1),
                QuantitySold = reader.GetInt64(2),
                Revenue = reader.GetDecimal(3)
            });
        }

        return products;
    }

    private async Task<IReadOnlyCollection<AiLowStockProductDto>> GetAiLowStockProductsAsync(
        string schemaName,
        AiBusinessDataQuery query,
        InventoryScope scope,
        CancellationToken cancellationToken)
    {
        var products = new List<AiLowStockProductDto>();
        var whereClause = BuildAiInventoryWhereClause(query, scope);

        await using var command = await CreateCommandAsync($"""
            SELECT i.product_id,
                   p.name,
                   i.market_id,
                   m.name,
                   i.department_id,
                   d.name AS department_name,
                   i.quantity,
                   i.reserved_quantity,
                   i.quantity - i.reserved_quantity AS available_quantity,
                   p.min_stock_alert,
                   GREATEST(p.min_stock_alert - i.quantity, 0) AS suggested_restock_quantity
            FROM {schemaName}.inventory i
            INNER JOIN {schemaName}.products p ON p.id = i.product_id
            INNER JOIN {schemaName}.markets m ON m.id = i.market_id
            LEFT JOIN {schemaName}.departments d ON d.id = i.department_id
            {whereClause}
            ORDER BY suggested_restock_quantity DESC, p.name ASC, i.id ASC
            LIMIT @low_stock_limit;
            """, cancellationToken);
        AddAiBusinessDataParameters(command, query);
        AddInventoryScopeParameters(command, scope);
        command.Parameters.AddWithValue("low_stock_limit", query.LowStockLimit);

        await using var reader = await command.ExecuteReaderAsync(cancellationToken);

        while (await reader.ReadAsync(cancellationToken))
        {
            products.Add(new AiLowStockProductDto
            {
                ProductId = reader.GetInt32(0),
                ProductName = reader.GetString(1),
                MarketId = reader.GetInt32(2),
                MarketName = reader.GetString(3),
                DepartmentId = reader.IsDBNull(4) ? null : reader.GetInt32(4),
                DepartmentName = reader.IsDBNull(5) ? null : reader.GetString(5),
                Quantity = reader.GetInt32(6),
                ReservedQuantity = reader.GetInt32(7),
                AvailableQuantity = reader.GetInt32(8),
                MinimumStockAlert = reader.GetInt32(9),
                SuggestedRestockQuantity = reader.GetInt32(10)
            });
        }

        return products;
    }

    private async Task<IReadOnlyCollection<AiInventoryMovementMetricDto>> GetAiInventoryMovementMetricsAsync(
        string schemaName,
        AiBusinessDataQuery query,
        InventoryScope scope,
        CancellationToken cancellationToken)
    {
        var metrics = new List<AiInventoryMovementMetricDto>();
        var whereClause = BuildAiInventoryMovementWhereClause(query, scope);

        await using var command = await CreateCommandAsync($"""
            SELECT im.movement_type,
                   COUNT(*)::bigint AS movement_count,
                   COALESCE(SUM(ABS(im.quantity_changed)), 0)::bigint AS total_quantity_changed
            FROM {schemaName}.inventory_movements im
            INNER JOIN {schemaName}.inventory i ON i.id = im.inventory_id
            {whereClause}
            GROUP BY im.movement_type
            ORDER BY movement_count DESC, im.movement_type ASC;
            """, cancellationToken);
        AddAiBusinessDataParameters(command, query);
        AddInventoryScopeParameters(command, scope);

        await using var reader = await command.ExecuteReaderAsync(cancellationToken);

        while (await reader.ReadAsync(cancellationToken))
        {
            metrics.Add(new AiInventoryMovementMetricDto
            {
                MovementType = reader.GetString(0),
                MovementCount = reader.GetInt64(1),
                TotalQuantityChanged = reader.GetInt64(2)
            });
        }

        return metrics;
    }

    private async Task<IReadOnlyCollection<AiSupplierPurchaseMetricDto>> GetAiSupplierPurchaseMetricsAsync(
        string schemaName,
        AiBusinessDataQuery query,
        InventoryScope scope,
        CancellationToken cancellationToken)
    {
        var metrics = new List<AiSupplierPurchaseMetricDto>();
        var whereClause = BuildAiPurchaseWhereClause(query, scope);

        await using var command = await CreateCommandAsync($"""
            WITH filtered_purchases AS (
                SELECT p.id,
                       p.supplier_id,
                       p.total_amount
                FROM {schemaName}.purchases p
                {whereClause}
            ),
            purchase_quantities AS (
                SELECT pi.purchase_id,
                       SUM(pi.quantity)::bigint AS purchased_quantity
                FROM {schemaName}.purchase_items pi
                INNER JOIN filtered_purchases fp ON fp.id = pi.purchase_id
                GROUP BY pi.purchase_id
            )
            SELECT fp.supplier_id,
                   COALESCE(s.name, '') AS supplier_name,
                   COUNT(fp.id)::bigint AS purchase_count,
                   COALESCE(SUM(fp.total_amount), 0) AS total_purchase_amount,
                   COALESCE(SUM(COALESCE(pq.purchased_quantity, 0)), 0)::bigint AS total_purchased_quantity
            FROM filtered_purchases fp
            LEFT JOIN {schemaName}.suppliers s ON s.id = fp.supplier_id
            LEFT JOIN purchase_quantities pq ON pq.purchase_id = fp.id
            GROUP BY fp.supplier_id, s.name
            ORDER BY total_purchase_amount DESC, purchase_count DESC, supplier_name ASC;
            """, cancellationToken);
        AddAiBusinessDataParameters(command, query);
        AddInventoryScopeParameters(command, scope);

        await using var reader = await command.ExecuteReaderAsync(cancellationToken);

        while (await reader.ReadAsync(cancellationToken))
        {
            metrics.Add(new AiSupplierPurchaseMetricDto
            {
                SupplierId = reader.GetInt32(0),
                SupplierName = reader.GetString(1),
                PurchaseCount = reader.GetInt64(2),
                TotalPurchaseAmount = reader.GetDecimal(3),
                TotalPurchasedQuantity = reader.GetInt64(4)
            });
        }

        return metrics;
    }

    public async Task<IReadOnlyCollection<AiInventoryForecastDataDto>> GetAiInventoryForecastDataAsync(
        AiInventoryForecastRequest request,
        CancellationToken cancellationToken = default)
    {
        var schemaName = await GetQuotedCurrentSchemaNameAsync(cancellationToken);
        var scope = await GetCurrentInventoryScopeAsync(schemaName, cancellationToken);
        var inventoryWhereClause = BuildAiInventoryForecastInventoryWhereClause(request, scope);
        var salesWhereClause = BuildAiInventoryForecastSalesWhereClause(request, scope);
        var products = new List<AiInventoryForecastDataDto>();

        await using var command = await CreateCommandAsync($"""
            WITH inventory_totals AS (
                SELECT p.id AS product_id,
                       p.name AS product_name,
                       COALESCE(SUM(i.quantity), 0)::int AS current_stock
                FROM {schemaName}.inventory i
                INNER JOIN {schemaName}.products p ON p.id = i.product_id
                {inventoryWhereClause}
                GROUP BY p.id, p.name
            ),
            sales_totals AS (
                SELECT si.product_id,
                       COALESCE(SUM(si.quantity), 0)::bigint AS total_quantity_sold
                FROM {schemaName}.sale_items si
                INNER JOIN {schemaName}.sales s ON s.id = si.sale_id
                INNER JOIN inventory_totals it ON it.product_id = si.product_id
                {salesWhereClause}
                GROUP BY si.product_id
            )
            SELECT it.product_id,
                   it.product_name,
                   it.current_stock,
                   COALESCE(st.total_quantity_sold, 0)::bigint AS total_quantity_sold
            FROM inventory_totals it
            LEFT JOIN sales_totals st ON st.product_id = it.product_id
            ORDER BY it.product_name ASC, it.product_id ASC;
            """, cancellationToken);
        AddAiInventoryForecastParameters(command, request);
        AddInventoryScopeParameters(command, scope);

        await using var reader = await command.ExecuteReaderAsync(cancellationToken);

        while (await reader.ReadAsync(cancellationToken))
        {
            products.Add(new AiInventoryForecastDataDto
            {
                ProductId = reader.GetInt32(0),
                ProductName = reader.GetString(1),
                CurrentStock = reader.GetInt32(2),
                TotalQuantitySold = reader.GetInt64(3)
            });
        }

        return products;
    }

    public async Task<IReadOnlyCollection<AiInventoryInsightDataDto>> GetAiInventoryInsightDataAsync(
        AiInventoryRecommendationRequest request,
        CancellationToken cancellationToken = default)
    {
        var schemaName = await GetQuotedCurrentSchemaNameAsync(cancellationToken);
        var scope = await GetCurrentInventoryScopeAsync(schemaName, cancellationToken);
        var inventoryWhereClause = BuildAiInventoryRecommendationInventoryWhereClause(request, scope);
        var salesWhereClause = BuildAiInventoryRecommendationSalesWhereClause(request, scope);
        var products = new List<AiInventoryInsightDataDto>();

        await using var command = await CreateCommandAsync($"""
            WITH inventory_totals AS (
                SELECT p.id AS product_id,
                       p.name AS product_name,
                       COALESCE(SUM(i.quantity), 0)::int AS current_stock,
                       p.min_stock_alert
                FROM {schemaName}.inventory i
                INNER JOIN {schemaName}.products p ON p.id = i.product_id
                {inventoryWhereClause}
                GROUP BY p.id, p.name, p.min_stock_alert
            ),
            sales_totals AS (
                SELECT si.product_id,
                       COALESCE(SUM(si.quantity), 0)::bigint AS total_quantity_sold
                FROM {schemaName}.sale_items si
                INNER JOIN {schemaName}.sales s ON s.id = si.sale_id
                INNER JOIN inventory_totals it ON it.product_id = si.product_id
                {salesWhereClause}
                GROUP BY si.product_id
            )
            SELECT it.product_id,
                   it.product_name,
                   it.current_stock,
                   it.min_stock_alert,
                   COALESCE(st.total_quantity_sold, 0)::bigint AS total_quantity_sold
            FROM inventory_totals it
            LEFT JOIN sales_totals st ON st.product_id = it.product_id
            ORDER BY it.product_name ASC, it.product_id ASC;
            """, cancellationToken);
        AddAiInventoryRecommendationParameters(command, request);
        AddInventoryScopeParameters(command, scope);

        await using var reader = await command.ExecuteReaderAsync(cancellationToken);

        while (await reader.ReadAsync(cancellationToken))
        {
            products.Add(new AiInventoryInsightDataDto
            {
                ProductId = reader.GetInt32(0),
                ProductName = reader.GetString(1),
                CurrentStock = reader.GetInt32(2),
                MinimumStockAlert = reader.GetInt32(3),
                TotalQuantitySold = reader.GetInt64(4)
            });
        }

        return products;
    }

    public async Task<SaleDetailsResponse?> GetSaleDetailsAsync(
        int id,
        CancellationToken cancellationToken = default)
    {
        var schemaName = await GetQuotedCurrentSchemaNameAsync(cancellationToken);

        return await ExecuteWithSaleReferenceNumberRecoveryAsync(
            schemaName,
            () => GetSaleDetailsCoreAsync(schemaName, id, cancellationToken),
            cancellationToken);
    }

    private async Task<SaleDetailsResponse?> GetSaleDetailsCoreAsync(
        string schemaName,
        int id,
        CancellationToken cancellationToken)
    {
        var scope = await GetCurrentInventoryScopeAsync(schemaName, cancellationToken);
        var scopeCondition = BuildSalesScopeCondition(scope, "AND");

        await using var saleCommand = await CreateCommandAsync($"""
            SELECT s.id,
                   s.reference_number,
                   s.total_amount,
                   s.created_at,
                   u.full_name AS cashier_name,
                   m.name AS market_name
            FROM {schemaName}.sales s
            LEFT JOIN public.users u ON u.id = s.created_by_user_id
            LEFT JOIN {schemaName}.markets m ON m.id = s.market_id
            WHERE s.id = @id
              {scopeCondition};
            """, cancellationToken);

        saleCommand.Parameters.AddWithValue("id", id);
        AddInventoryScopeParameters(saleCommand, scope);

        await using var saleReader = await saleCommand.ExecuteReaderAsync(cancellationToken);

        if (!await saleReader.ReadAsync(cancellationToken))
        {
            return null;
        }

        var saleDetails = new SaleDetailsResponse
        {
            Id = saleReader.GetInt32(0),
            ReferenceNumber = saleReader.GetString(1),
            TotalAmount = saleReader.GetDecimal(2),
            CreatedAt = saleReader.GetFieldValue<DateTimeOffset>(3),
            CashierName = saleReader.IsDBNull(4) ? null : saleReader.GetString(4),
            MarketName = saleReader.IsDBNull(5) ? null : saleReader.GetString(5)
        };

        await saleReader.DisposeAsync();

        await using var itemCommand = await CreateCommandAsync($"""
            SELECT si.product_id,
                   p.name,
                   si.quantity,
                   si.unit_price,
                   si.line_total
            FROM {schemaName}.sale_items si
            LEFT JOIN {schemaName}.products p ON p.id = si.product_id
            WHERE si.sale_id = @id
            ORDER BY si.id;
            """, cancellationToken);

        itemCommand.Parameters.AddWithValue("id", id);

        await using var itemReader = await itemCommand.ExecuteReaderAsync(cancellationToken);

        var items = new List<SaleItemResponse>();

        while (await itemReader.ReadAsync(cancellationToken))
        {
            items.Add(new SaleItemResponse
            {
                ProductId = itemReader.GetInt32(0),
                ProductName = itemReader.IsDBNull(1) ? null : itemReader.GetString(1),
                Quantity = itemReader.GetInt32(2),
                UnitPrice = itemReader.GetDecimal(3),
                LineTotal = itemReader.GetDecimal(4)
            });
        }

        saleDetails.Items = items;
        return saleDetails;
    }

    public async Task<SaleDto?> CreateSaleAsync(
        CreateSaleRequest request,
        int createdByUserId,
        CancellationToken cancellationToken = default)
    {
        var schemaName = await GetQuotedCurrentSchemaNameAsync(cancellationToken);

        return await ExecuteWithSaleReferenceNumberRecoveryAsync(
            schemaName,
            () => CreateSaleCoreAsync(schemaName, request, createdByUserId, cancellationToken),
            cancellationToken);
    }

    private async Task<SaleDto?> CreateSaleCoreAsync(
        string schemaName,
        CreateSaleRequest request,
        int createdByUserId,
        CancellationToken cancellationToken)
    {
        var scope = await GetCurrentInventoryScopeAsync(schemaName, cancellationToken);
        var departmentId = scope.Kind == InventoryScopeKind.Department
            ? scope.DepartmentId
            : null;

        if (!CanAccessInventoryTarget(scope, request.MarketId, departmentId))
        {
            return null;
        }

        await using var transaction = await BeginTransactionAsync(cancellationToken);
        await using var command = await CreateCommandAsync($"""
            INSERT INTO {schemaName}.sales (
                market_id,
                department_id,
                created_by_user_id,
                sale_date,
                status,
                payment_method,
                discount_amount,
                total_amount,
                notes)
            VALUES (
                @market_id,
                @department_id,
                @created_by_user_id,
                @sale_date,
                'Paid',
                @payment_method,
                @discount_amount,
                @total_amount,
                @notes)
            RETURNING id, reference_number, market_id, sale_date, payment_method, total_amount;
            """, cancellationToken, transaction);

        command.Parameters.AddWithValue("market_id", request.MarketId);
        command.Parameters.AddWithValue("department_id", DbValue(departmentId));
        command.Parameters.AddWithValue("created_by_user_id", createdByUserId);
        command.Parameters.AddWithValue("sale_date", request.SaleDate ?? DateOnly.FromDateTime(DateTime.UtcNow));
        command.Parameters.AddWithValue("payment_method", request.PaymentMethod.Trim());
        command.Parameters.AddWithValue("discount_amount", request.DiscountAmount);
        command.Parameters.AddWithValue("total_amount", request.TotalAmount);
        command.Parameters.AddWithValue("notes", DbValue(request.Notes));

        var sale = await ReadSaleAsync(command, cancellationToken)
            ?? throw new InvalidOperationException("Sale was not created.");

        foreach (var item in request.Items)
        {
            await InsertSaleItemAsync(
                schemaName,
                sale.Id,
                item,
                cancellationToken,
                transaction);

            var stockUpdated = await ApplyInventoryQuantityChangeByProductAsync(
                schemaName,
                item.ProductId,
                request.MarketId,
                departmentId,
                -item.Quantity,
                "SaleCompleted",
                createdByUserId,
                $"sale:{sale.Id}",
                cancellationToken,
                transaction);

            if (!stockUpdated)
            {
                return null;
            }
        }

        await transaction.CommitAsync(cancellationToken);

        return sale;
    }

    public async Task<SaleDto?> UpdateSaleAsync(
        int id,
        UpdateSaleRequest request,
        CancellationToken cancellationToken = default)
    {
        var schemaName = await GetQuotedCurrentSchemaNameAsync(cancellationToken);

        return await ExecuteWithSaleReferenceNumberRecoveryAsync(
            schemaName,
            () => UpdateSaleCoreAsync(schemaName, id, request, cancellationToken),
            cancellationToken);
    }

    private async Task<SaleDto?> UpdateSaleCoreAsync(
        string schemaName,
        int id,
        UpdateSaleRequest request,
        CancellationToken cancellationToken)
    {
        var scope = await GetCurrentInventoryScopeAsync(schemaName, cancellationToken);

        if (!CanAccessSaleMarket(scope, request.MarketId))
        {
            return null;
        }

        var scopeCondition = BuildSalesScopeCondition(scope, "AND");

        await using var command = await CreateCommandAsync($"""
            UPDATE {schemaName}.sales s
            SET market_id = @market_id,
                department_id = CASE
                    WHEN market_id = @market_id THEN department_id
                    ELSE NULL
                END,
                sale_date = @sale_date,
                payment_method = @payment_method,
                discount_amount = @discount_amount,
                total_amount = @total_amount,
                notes = @notes
            WHERE s.id = @id
              {scopeCondition}
            RETURNING id, reference_number, market_id, sale_date, payment_method, total_amount;
            """, cancellationToken);

        command.Parameters.AddWithValue("id", id);
        command.Parameters.AddWithValue("market_id", request.MarketId);
        command.Parameters.AddWithValue("sale_date", request.SaleDate);
        command.Parameters.AddWithValue("payment_method", request.PaymentMethod.Trim());
        command.Parameters.AddWithValue("discount_amount", request.DiscountAmount);
        command.Parameters.AddWithValue("total_amount", request.TotalAmount);
        command.Parameters.AddWithValue("notes", DbValue(request.Notes));
        AddInventoryScopeParameters(command, scope);

        return await ReadSaleAsync(command, cancellationToken);
    }

    public async Task<SaleDto?> PatchSaleAsync(
        int id,
        PatchSaleRequest request,
        CancellationToken cancellationToken = default)
    {
        var schemaName = await GetQuotedCurrentSchemaNameAsync(cancellationToken);

        return await ExecuteWithSaleReferenceNumberRecoveryAsync(
            schemaName,
            () => PatchSaleCoreAsync(schemaName, id, request, cancellationToken),
            cancellationToken);
    }

    private async Task<SaleDto?> PatchSaleCoreAsync(
        string schemaName,
        int id,
        PatchSaleRequest request,
        CancellationToken cancellationToken)
    {
        var scope = await GetCurrentInventoryScopeAsync(schemaName, cancellationToken);

        if (request.MarketId.HasValue && !CanAccessSaleMarket(scope, request.MarketId.Value))
        {
            return null;
        }

        var scopeCondition = BuildSalesScopeCondition(scope, "AND");

        await using var command = await CreateCommandAsync($"""
            UPDATE {schemaName}.sales s
            SET market_id = COALESCE(@market_id, market_id),
                department_id = CASE
                    WHEN @market_id IS NULL OR market_id = @market_id THEN department_id
                    ELSE NULL
                END,
                sale_date = COALESCE(@sale_date, sale_date),
                payment_method = COALESCE(@payment_method, payment_method),
                discount_amount = COALESCE(@discount_amount, discount_amount),
                total_amount = COALESCE(@total_amount, total_amount),
                notes = COALESCE(@notes, notes)
            WHERE s.id = @id
              {scopeCondition}
            RETURNING id, reference_number, market_id, sale_date, payment_method, total_amount;
            """, cancellationToken);

        command.Parameters.AddWithValue("id", id);
        command.Parameters.AddWithValue("market_id", DbValue(request.MarketId));
        command.Parameters.AddWithValue("sale_date", DbValue(request.SaleDate));
        command.Parameters.AddWithValue("payment_method", DbValue(request.PaymentMethod?.Trim()));
        command.Parameters.AddWithValue("discount_amount", DbValue(request.DiscountAmount));
        command.Parameters.AddWithValue("total_amount", DbValue(request.TotalAmount));
        command.Parameters.AddWithValue("notes", DbValue(request.Notes));
        AddInventoryScopeParameters(command, scope);

        return await ReadSaleAsync(command, cancellationToken);
    }

    public async Task<bool> DeleteSaleAsync(
        int id,
        CancellationToken cancellationToken = default)
    {
        var schemaName = await GetQuotedCurrentSchemaNameAsync(cancellationToken);
        var scope = await GetCurrentInventoryScopeAsync(schemaName, cancellationToken);
        var scopeCondition = BuildSalesScopeCondition(scope, "AND");

        await using var command = await CreateCommandAsync($"""
            DELETE FROM {schemaName}.sales s
            WHERE s.id = @id
              {scopeCondition};
            """, cancellationToken);
        command.Parameters.AddWithValue("id", id);
        AddInventoryScopeParameters(command, scope);

        return await command.ExecuteNonQueryAsync(cancellationToken) > 0;
    }

    public async Task<IReadOnlyCollection<PurchaseDto>> GetPurchasesAsync(
        CancellationToken cancellationToken = default)
    {
        var schemaName = await GetQuotedCurrentSchemaNameAsync(cancellationToken);
        await EnsurePurchaseExpectedDateColumnAsync(schemaName, cancellationToken);
        var purchases = new List<PurchaseDto>();

        await using var command = await CreateCommandAsync($"""
            SELECT p.id,
                   p.supplier_id,
                   COALESCE(s.name, '') AS supplier_name,
                   p.market_id,
                   COALESCE(m.name, '') AS market_name,
                   p.purchase_date,
                   p.expected_date,
                   p.status,
                   p.total_amount,
                   p.notes,
                   COUNT(pi.id)::int AS item_count,
                   COALESCE(SUM(pi.quantity), 0)::int AS total_quantity,
                   COALESCE(SUM(pi.received_quantity), 0)::int AS received_quantity
            FROM {schemaName}.purchases p
            LEFT JOIN {schemaName}.suppliers s ON s.id = p.supplier_id
            LEFT JOIN {schemaName}.markets m ON m.id = p.market_id
            LEFT JOIN {schemaName}.purchase_items pi ON pi.purchase_id = p.id
            GROUP BY p.id, p.supplier_id, s.name, p.market_id, m.name, p.purchase_date, p.expected_date, p.status, p.total_amount, p.notes
            ORDER BY p.purchase_date DESC, p.id DESC;
            """, cancellationToken);

        await using var reader = await command.ExecuteReaderAsync(cancellationToken);

        while (await reader.ReadAsync(cancellationToken))
        {
            purchases.Add(ReadPurchaseSummary(reader));
        }

        return purchases;
    }

    public async Task<PurchaseDto?> GetPurchaseAsync(
        int id,
        CancellationToken cancellationToken = default)
    {
        var schemaName = await GetQuotedCurrentSchemaNameAsync(cancellationToken);
        await EnsurePurchaseExpectedDateColumnAsync(schemaName, cancellationToken);
        return await GetPurchaseWithItemsAsync(schemaName, id, cancellationToken);
    }

    public async Task<PurchaseDto?> CreatePurchaseAsync(
        CreatePurchaseRequest request,
        int createdByUserId,
        CancellationToken cancellationToken = default)
    {
        var schemaName = await GetQuotedCurrentSchemaNameAsync(cancellationToken);
        await EnsurePurchaseExpectedDateColumnAsync(schemaName, cancellationToken);
        var status = NormalizePurchaseStatus(request.Status);
        var receivesPurchase = string.Equals(status, "Received", StringComparison.OrdinalIgnoreCase);

        if (receivesPurchase)
        {
            var scope = await GetCurrentInventoryScopeAsync(schemaName, cancellationToken);

            if (!CanAccessInventoryTarget(scope, request.MarketId, departmentId: null))
            {
                return null;
            }
        }

        await using var transaction = await BeginTransactionAsync(cancellationToken);

        if (!await PurchaseReferencesExistAsync(schemaName, request.SupplierId, request.MarketId, request.Items, cancellationToken, transaction))
        {
            return null;
        }

        await using var command = await CreateCommandAsync($"""
            INSERT INTO {schemaName}.purchases (
                supplier_id,
                market_id,
                created_by_user_id,
                purchase_date,
                expected_date,
                status,
                total_amount,
                notes)
            VALUES (
                @supplier_id,
                @market_id,
                @created_by_user_id,
                @purchase_date,
                @expected_date,
                @status,
                @total_amount,
                @notes)
            RETURNING id, supplier_id, market_id, purchase_date, expected_date, status, total_amount;
            """, cancellationToken, transaction);

        command.Parameters.AddWithValue("supplier_id", request.SupplierId);
        command.Parameters.AddWithValue("market_id", request.MarketId);
        command.Parameters.AddWithValue("created_by_user_id", createdByUserId);
        command.Parameters.AddWithValue("purchase_date", request.PurchaseDate ?? DateOnly.FromDateTime(DateTime.UtcNow));
        command.Parameters.AddWithValue("expected_date", DbValue(request.ExpectedDate));
        command.Parameters.AddWithValue("status", status);
        command.Parameters.AddWithValue("total_amount", request.TotalAmount);
        command.Parameters.AddWithValue("notes", DbValue(request.Notes));

        var purchase = await ReadPurchaseAsync(command, cancellationToken)
            ?? throw new InvalidOperationException("Purchase was not created.");

        foreach (var item in request.Items)
        {
            await InsertPurchaseItemAsync(
                schemaName,
                purchase.Id,
                item,
                cancellationToken,
                transaction);
        }

        if (receivesPurchase)
        {
            foreach (var item in request.Items)
            {
                await using var updateItemCommand = await CreateCommandAsync($"""
                    UPDATE {schemaName}.purchase_items
                    SET received_quantity = quantity
                    WHERE purchase_id = @purchase_id
                      AND product_id = @product_id;
                    """, cancellationToken, transaction);
                updateItemCommand.Parameters.AddWithValue("purchase_id", purchase.Id);
                updateItemCommand.Parameters.AddWithValue("product_id", item.ProductId);
                await updateItemCommand.ExecuteNonQueryAsync(cancellationToken);

                await ApplyInventoryQuantityChangeByProductAsync(
                    schemaName,
                    item.ProductId,
                    request.MarketId,
                    departmentId: null,
                    item.Quantity,
                    "PurchaseReceived",
                    createdByUserId,
                    $"purchase:{purchase.Id}",
                    cancellationToken,
                    transaction);
            }
        }

        await transaction.CommitAsync(cancellationToken);

        return await GetPurchaseWithItemsAsync(schemaName, purchase.Id, cancellationToken);
    }

    public async Task<PurchaseDto?> UpdatePurchaseAsync(
        int id,
        UpdatePurchaseRequest request,
        int? updatedByUserId,
        CancellationToken cancellationToken = default)
    {
        var schemaName = await GetQuotedCurrentSchemaNameAsync(cancellationToken);
        await EnsurePurchaseExpectedDateColumnAsync(schemaName, cancellationToken);
        var status = NormalizePurchaseStatus(request.Status);

        await using var transaction = await BeginTransactionAsync(cancellationToken);
        var currentPurchase = await GetPurchaseReceiptStateForUpdateAsync(schemaName, id, cancellationToken, transaction);

        if (currentPurchase is null)
        {
            return null;
        }

        if (!CanUpdatePurchase(currentPurchase.Status))
        {
            return null;
        }

        var itemsToValidate = request.Items ?? await GetPurchaseItemsAsCreateRequestsAsync(
            schemaName,
            id,
            cancellationToken,
            transaction);

        if (!await PurchaseReferencesExistAsync(schemaName, request.SupplierId, request.MarketId, itemsToValidate, cancellationToken, transaction))
        {
            return null;
        }

        if (ShouldReceivePurchase(currentPurchase.Status, status, request.MarketId))
        {
            var scope = await GetCurrentInventoryScopeAsync(schemaName, cancellationToken);

            if (!CanAccessInventoryTarget(scope, request.MarketId, departmentId: null))
            {
                return null;
            }
        }

        await using var command = await CreateCommandAsync($"""
            UPDATE {schemaName}.purchases
            SET supplier_id = @supplier_id,
                market_id = @market_id,
                purchase_date = @purchase_date,
                expected_date = @expected_date,
                status = @status,
                total_amount = @total_amount,
                notes = @notes
            WHERE id = @id
            RETURNING id, supplier_id, market_id, purchase_date, expected_date, status, total_amount;
            """, cancellationToken, transaction);

        command.Parameters.AddWithValue("id", id);
        command.Parameters.AddWithValue("supplier_id", request.SupplierId);
        command.Parameters.AddWithValue("market_id", request.MarketId);
        command.Parameters.AddWithValue("purchase_date", request.PurchaseDate);
        command.Parameters.AddWithValue("expected_date", DbValue(request.ExpectedDate));
        command.Parameters.AddWithValue("status", status);
        command.Parameters.AddWithValue("total_amount", request.TotalAmount);
        command.Parameters.AddWithValue("notes", DbValue(request.Notes));

        var purchase = await ReadPurchaseAsync(command, cancellationToken);

        if (purchase is not null)
        {
            if (request.Items is not null)
            {
                await ReplacePurchaseItemsAsync(schemaName, id, request.Items, cancellationToken, transaction);
            }

            await ApplyPurchaseReceiptIfNeededAsync(
                schemaName,
                purchase,
                currentPurchase.Status,
                updatedByUserId,
                cancellationToken,
                transaction);
        }

        await transaction.CommitAsync(cancellationToken);

        return purchase is null
            ? null
            : await GetPurchaseWithItemsAsync(schemaName, purchase.Id, cancellationToken);
    }

    public async Task<PurchaseDto?> PatchPurchaseAsync(
        int id,
        PatchPurchaseRequest request,
        int? updatedByUserId,
        CancellationToken cancellationToken = default)
    {
        var schemaName = await GetQuotedCurrentSchemaNameAsync(cancellationToken);
        await EnsurePurchaseExpectedDateColumnAsync(schemaName, cancellationToken);

        await using var transaction = await BeginTransactionAsync(cancellationToken);
        var currentPurchase = await GetPurchaseReceiptStateForUpdateAsync(schemaName, id, cancellationToken, transaction);

        if (currentPurchase is null)
        {
            return null;
        }

        var effectiveStatus = request.Status?.Trim() ?? currentPurchase.Status;
        effectiveStatus = NormalizePurchaseStatus(effectiveStatus);
        var effectiveMarketId = request.MarketId ?? currentPurchase.MarketId;

        if (!CanPatchPurchase(currentPurchase.Status, effectiveStatus))
        {
            return null;
        }

        if (ShouldReceivePurchase(currentPurchase.Status, effectiveStatus, effectiveMarketId))
        {
            var scope = await GetCurrentInventoryScopeAsync(schemaName, cancellationToken);

            if (!CanAccessInventoryTarget(scope, effectiveMarketId, departmentId: null))
            {
                return null;
            }
        }

        await using var command = await CreateCommandAsync($"""
            UPDATE {schemaName}.purchases
            SET supplier_id = COALESCE(@supplier_id, supplier_id),
                market_id = COALESCE(@market_id, market_id),
                purchase_date = COALESCE(@purchase_date, purchase_date),
                expected_date = COALESCE(@expected_date, expected_date),
                status = COALESCE(@status, status),
                total_amount = COALESCE(@total_amount, total_amount),
                notes = COALESCE(@notes, notes)
            WHERE id = @id
            RETURNING id, supplier_id, market_id, purchase_date, expected_date, status, total_amount;
            """, cancellationToken, transaction);

        command.Parameters.AddWithValue("id", id);
        command.Parameters.AddWithValue("supplier_id", DbValue(request.SupplierId));
        command.Parameters.AddWithValue("market_id", DbValue(request.MarketId));
        command.Parameters.AddWithValue("purchase_date", DbValue(request.PurchaseDate));
        command.Parameters.AddWithValue("expected_date", DbValue(request.ExpectedDate));
        command.Parameters.AddWithValue("status", DbValue(request.Status is null ? null : effectiveStatus));
        command.Parameters.AddWithValue("total_amount", DbValue(request.TotalAmount));
        command.Parameters.AddWithValue("notes", DbValue(request.Notes));

        var purchase = await ReadPurchaseAsync(command, cancellationToken);

        if (purchase is not null)
        {
            await ApplyPurchaseReceiptIfNeededAsync(
                schemaName,
                purchase,
                currentPurchase.Status,
                updatedByUserId,
                cancellationToken,
                transaction);
        }

        await transaction.CommitAsync(cancellationToken);

        return purchase is null
            ? null
            : await GetPurchaseWithItemsAsync(schemaName, purchase.Id, cancellationToken);
    }

    public async Task<PurchaseDto?> ReceivePurchaseAsync(
        int id,
        ReceivePurchaseRequest request,
        int? updatedByUserId,
        CancellationToken cancellationToken = default)
    {
        var schemaName = await GetQuotedCurrentSchemaNameAsync(cancellationToken);
        await EnsurePurchaseExpectedDateColumnAsync(schemaName, cancellationToken);

        await using var transaction = await BeginTransactionAsync(cancellationToken);
        var currentPurchase = await GetPurchaseReceiptStateForUpdateAsync(schemaName, id, cancellationToken, transaction);

        if (currentPurchase is null ||
            string.Equals(currentPurchase.Status, "Cancelled", StringComparison.OrdinalIgnoreCase) ||
            string.Equals(currentPurchase.Status, "Received", StringComparison.OrdinalIgnoreCase))
        {
            return null;
        }

        var scope = await GetCurrentInventoryScopeAsync(schemaName, cancellationToken);

        if (!CanAccessInventoryTarget(scope, currentPurchase.MarketId, departmentId: null))
        {
            return null;
        }

        var items = await GetPurchaseItemsForReceivingAsync(schemaName, id, cancellationToken, transaction);
        if (items.Count == 0)
        {
            return null;
        }

        var receiptQuantities = BuildReceiptQuantities(request, items);
        if (receiptQuantities.Count == 0)
        {
            return null;
        }

        foreach (var (productId, quantityToReceive) in receiptQuantities)
        {
            var matchingItems = items
                .Where(candidate => candidate.ProductId == productId)
                .ToList();
            var remainingForProduct = matchingItems.Sum(item => item.Quantity - item.ReceivedQuantity);

            if (matchingItems.Count == 0 || quantityToReceive <= 0 || quantityToReceive > remainingForProduct)
            {
                return null;
            }

            var quantityLeftToAllocate = quantityToReceive;
            foreach (var item in matchingItems)
            {
                if (quantityLeftToAllocate == 0)
                {
                    break;
                }

                var itemRemaining = item.Quantity - item.ReceivedQuantity;
                var itemReceiptQuantity = Math.Min(itemRemaining, quantityLeftToAllocate);

                if (itemReceiptQuantity == 0)
                {
                    continue;
                }

                await using var updateItemCommand = await CreateCommandAsync($"""
                    UPDATE {schemaName}.purchase_items
                    SET received_quantity = received_quantity + @quantity_to_receive
                    WHERE id = @item_id;
                    """, cancellationToken, transaction);
                updateItemCommand.Parameters.AddWithValue("item_id", item.Id);
                updateItemCommand.Parameters.AddWithValue("quantity_to_receive", itemReceiptQuantity);
                await updateItemCommand.ExecuteNonQueryAsync(cancellationToken);

                quantityLeftToAllocate -= itemReceiptQuantity;
            }

            await ApplyInventoryQuantityChangeByProductAsync(
                schemaName,
                productId,
                currentPurchase.MarketId,
                departmentId: null,
                quantityToReceive,
                "PurchaseReceived",
                updatedByUserId,
                $"purchase:{id}",
                cancellationToken,
                transaction);
        }

        var newStatus = await GetPurchaseReceiptStatusAsync(schemaName, id, cancellationToken, transaction);
        await using var updatePurchaseCommand = await CreateCommandAsync($"""
            UPDATE {schemaName}.purchases
            SET status = @status,
                received_at = CASE WHEN @status = 'Received' THEN COALESCE(received_at, NOW()) ELSE received_at END
            WHERE id = @id;
            """, cancellationToken, transaction);
        updatePurchaseCommand.Parameters.AddWithValue("id", id);
        updatePurchaseCommand.Parameters.AddWithValue("status", newStatus);
        await updatePurchaseCommand.ExecuteNonQueryAsync(cancellationToken);

        await transaction.CommitAsync(cancellationToken);

        return await GetPurchaseWithItemsAsync(schemaName, id, cancellationToken);
    }

    public async Task<PurchaseDto?> CancelPurchaseAsync(
        int id,
        CancellationToken cancellationToken = default)
    {
        var schemaName = await GetQuotedCurrentSchemaNameAsync(cancellationToken);
        await EnsurePurchaseExpectedDateColumnAsync(schemaName, cancellationToken);

        await using var command = await CreateCommandAsync($"""
            UPDATE {schemaName}.purchases
            SET status = 'Cancelled'
            WHERE id = @id
              AND status IN ('Draft', 'Ordered', 'PartiallyReceived')
            RETURNING id, supplier_id, market_id, purchase_date, expected_date, status, total_amount;
            """, cancellationToken);
        command.Parameters.AddWithValue("id", id);

        var purchase = await ReadPurchaseAsync(command, cancellationToken);

        return purchase is null
            ? null
            : await GetPurchaseWithItemsAsync(schemaName, id, cancellationToken);
    }

    public async Task<bool> DeletePurchaseAsync(
        int id,
        CancellationToken cancellationToken = default)
    {
        var schemaName = await GetQuotedCurrentSchemaNameAsync(cancellationToken);

        await using var command = await CreateCommandAsync($"""
            DELETE FROM {schemaName}.purchases
            WHERE id = @id;
            """, cancellationToken);
        command.Parameters.AddWithValue("id", id);

        return await command.ExecuteNonQueryAsync(cancellationToken) > 0;
    }

    public async Task<InventoryItemDto?> GetInventoryItemAsync(
        int id,
        CancellationToken cancellationToken = default)
    {
        var schemaName = await GetQuotedCurrentSchemaNameAsync(cancellationToken);
        var scope = await GetCurrentInventoryScopeAsync(schemaName, cancellationToken);
        var scopeCondition = BuildInventoryScopeCondition(scope, "AND");

        await using var command = await CreateCommandAsync($"""
            SELECT i.id,
                   i.product_id,
                   p.name,
                   p.barcode,
                   p.category_id,
                   c.name AS category_name,
                   p.unit_price,
                   p.min_stock_alert,
                   GREATEST(p.min_stock_alert - i.quantity, 0) AS suggested_restock_quantity,
                   i.market_id,
                   m.name,
                   i.department_id,
                   d.name AS department_name,
                   i.quantity,
                   i.reserved_quantity,
                   i.quantity - i.reserved_quantity AS available_quantity,
                   i.quantity <= p.min_stock_alert AS is_low_stock,
                   i.updated_at,
                   i.last_updated_by
            FROM {schemaName}.inventory i
            INNER JOIN {schemaName}.products p ON p.id = i.product_id
            INNER JOIN {schemaName}.markets m ON m.id = i.market_id
            LEFT JOIN {schemaName}.departments d ON d.id = i.department_id
            LEFT JOIN {schemaName}.categories c ON c.id = p.category_id
            WHERE i.id = @id
              {scopeCondition};
            """, cancellationToken);
        command.Parameters.AddWithValue("id", id);
        AddInventoryScopeParameters(command, scope);

        await using var reader = await command.ExecuteReaderAsync(cancellationToken);

        return await reader.ReadAsync(cancellationToken) ? ReadInventoryItem(reader) : null;
    }

    public async Task<PagedResult<InventoryMovementDto>> GetInventoryMovementsAsync(
        InventoryMovementListQuery query,
        CancellationToken cancellationToken = default)
    {
        return await GetInventoryMovementsAsync(
            query,
            inventoryId: null,
            cancellationToken);
    }

    public async Task<PagedResult<InventoryMovementDto>> GetInventoryMovementsForInventoryAsync(
        int inventoryId,
        InventoryMovementListQuery query,
        CancellationToken cancellationToken = default)
    {
        return await GetInventoryMovementsAsync(
            query,
            inventoryId,
            cancellationToken);
    }

    private async Task<PagedResult<InventoryMovementDto>> GetInventoryMovementsAsync(
        InventoryMovementListQuery query,
        int? inventoryId,
        CancellationToken cancellationToken)
    {
        var schemaName = await GetQuotedCurrentSchemaNameAsync(cancellationToken);
        var scope = await GetCurrentInventoryScopeAsync(schemaName, cancellationToken);
        var whereClause = BuildInventoryMovementWhereClause(query, scope, inventoryId);
        var movements = new List<InventoryMovementDto>();
        var offset = ((long)query.Page - 1L) * query.PageSize;

        await using var countCommand = await CreateCommandAsync($"""
            SELECT COUNT(*)
            FROM {schemaName}.inventory_movements im
            INNER JOIN {schemaName}.inventory i ON i.id = im.inventory_id
            INNER JOIN {schemaName}.products p ON p.id = i.product_id
            INNER JOIN {schemaName}.markets m ON m.id = i.market_id
            {whereClause};
            """, cancellationToken);
        AddInventoryMovementListParameters(countCommand, query, inventoryId);
        AddInventoryScopeParameters(countCommand, scope);
        var totalCount = Convert.ToInt32(await countCommand.ExecuteScalarAsync(cancellationToken));

        await using var command = await CreateCommandAsync($"""
            SELECT im.id,
                   im.inventory_id,
                   i.product_id,
                   p.name,
                   i.market_id,
                   m.name,
                   i.department_id,
                   im.movement_type,
                   im.quantity_changed,
                   im.reference_number,
                   im.reason,
                   im.note,
                   im.created_by_user_id,
                   im.created_at
            FROM {schemaName}.inventory_movements im
            INNER JOIN {schemaName}.inventory i ON i.id = im.inventory_id
            INNER JOIN {schemaName}.products p ON p.id = i.product_id
            INNER JOIN {schemaName}.markets m ON m.id = i.market_id
            {whereClause}
            ORDER BY im.created_at DESC, im.id DESC
            LIMIT @page_size OFFSET @offset;
            """, cancellationToken);
        AddInventoryMovementListParameters(command, query, inventoryId);
        AddInventoryScopeParameters(command, scope);
        command.Parameters.AddWithValue("page_size", query.PageSize);
        command.Parameters.AddWithValue("offset", offset);

        await using var reader = await command.ExecuteReaderAsync(cancellationToken);

        while (await reader.ReadAsync(cancellationToken))
        {
            movements.Add(new InventoryMovementDto
            {
                Id = reader.GetInt32(0),
                InventoryId = reader.GetInt32(1),
                ProductId = reader.GetInt32(2),
                ProductName = reader.GetString(3),
                MarketId = reader.GetInt32(4),
                MarketName = reader.GetString(5),
                DepartmentId = reader.IsDBNull(6) ? null : reader.GetInt32(6),
                MovementType = reader.GetString(7),
                QuantityChanged = reader.GetInt32(8),
                ReferenceNumber = reader.IsDBNull(9) ? null : reader.GetString(9),
                Reason = reader.IsDBNull(10) ? null : reader.GetString(10),
                Note = reader.IsDBNull(11) ? null : reader.GetString(11),
                CreatedByUserId = reader.IsDBNull(12) ? null : reader.GetInt32(12),
                CreatedAt = reader.GetFieldValue<DateTimeOffset>(13)
            });
        }

        return new PagedResult<InventoryMovementDto>
        {
            Items = movements,
            Page = query.Page,
            PageSize = query.PageSize,
            TotalCount = totalCount,
            TotalPages = totalCount == 0 ? 0 : (int)Math.Ceiling(totalCount / (double)query.PageSize)
        };
    }

    private async Task<InventoryScope> GetCurrentInventoryScopeAsync(
        string quotedSchemaName,
        CancellationToken cancellationToken)
    {
        if (_currentUserService.UserId is not { } userId)
        {
            return InventoryScope.None;
        }

        var role = RoleAssignmentRules.NormalizeRoleName(
            await GetCurrentPersistedRoleNameAsync(userId, cancellationToken) ?? string.Empty);

        if (string.Equals(role, RoleAssignmentRules.CompanyAdmin, StringComparison.OrdinalIgnoreCase))
        {
            return InventoryScope.Company;
        }

        if (string.Equals(role, RoleAssignmentRules.RootAdmin, StringComparison.OrdinalIgnoreCase))
        {
            return InventoryScope.None;
        }

        var assignment = await GetCurrentStaffAssignmentAsync(quotedSchemaName, userId, cancellationToken);

        if (assignment is null)
        {
            return InventoryScope.None;
        }

        if (string.Equals(role, RoleAssignmentRules.DepartmentManager, StringComparison.OrdinalIgnoreCase))
        {
            return assignment.DepartmentId.HasValue
                ? InventoryScope.Department(assignment.MarketId, assignment.DepartmentId.Value)
                : InventoryScope.None;
        }

        if (string.Equals(role, RoleAssignmentRules.MainOperator, StringComparison.OrdinalIgnoreCase) ||
            string.Equals(role, RoleAssignmentRules.Seller, StringComparison.OrdinalIgnoreCase))
        {
            if (string.Equals(role, RoleAssignmentRules.Seller, StringComparison.OrdinalIgnoreCase) &&
                assignment.DepartmentId.HasValue)
            {
                return InventoryScope.Department(assignment.MarketId, assignment.DepartmentId.Value);
            }

            return InventoryScope.Market(assignment.MarketId);
        }

        if (string.Equals(role, RoleAssignmentRules.InventoryEmployee, StringComparison.OrdinalIgnoreCase))
        {
            return assignment.DepartmentId.HasValue
                ? InventoryScope.Department(assignment.MarketId, assignment.DepartmentId.Value)
                : InventoryScope.Market(assignment.MarketId);
        }

        return InventoryScope.None;
    }

    private async Task<string?> GetCurrentPersistedRoleNameAsync(
        int userId,
        CancellationToken cancellationToken)
    {
        return await _dbContext.Users
            .AsNoTracking()
            .Where(x => x.Id == userId && x.IsActive && x.Company.IsActive)
            .Select(x => x.Role.Name)
            .FirstOrDefaultAsync(cancellationToken);
    }

    private async Task<StaffAssignmentScope?> GetCurrentStaffAssignmentAsync(
        string quotedSchemaName,
        int userId,
        CancellationToken cancellationToken)
    {
        await using var command = await CreateCommandAsync($"""
            SELECT sa.market_id,
                   sa.department_id
            FROM {quotedSchemaName}.staff_assignments sa
            WHERE sa.is_active = TRUE
              AND sa.user_id = @user_id
            ORDER BY sa.assigned_at DESC, sa.id DESC
            LIMIT 1;
            """, cancellationToken);
        command.Parameters.AddWithValue("user_id", userId);

        await using var reader = await command.ExecuteReaderAsync(cancellationToken);

        return await reader.ReadAsync(cancellationToken)
            ? new StaffAssignmentScope(
                reader.GetInt32(0),
                reader.IsDBNull(1) ? null : reader.GetInt32(1))
            : null;
    }

    private static string BuildInventoryScopeCondition(
        InventoryScope scope,
        string prefix = "WHERE",
        string columnQualifier = "i.")
    {
        return scope.Kind switch
        {
            InventoryScopeKind.Company => string.Empty,
            InventoryScopeKind.Market => $"{prefix} {columnQualifier}market_id = @scope_market_id",
            InventoryScopeKind.Department => $"{prefix} {columnQualifier}market_id = @scope_market_id AND {columnQualifier}department_id = @scope_department_id",
            _ => $"{prefix} FALSE"
        };
    }

    private static string BuildSalesScopeCondition(
        InventoryScope scope,
        string prefix = "WHERE")
    {
        return scope.Kind switch
        {
            InventoryScopeKind.Company => string.Empty,
            InventoryScopeKind.Market => $"{prefix} s.market_id = @scope_market_id",
            InventoryScopeKind.Department =>
                $"{prefix} s.market_id = @scope_market_id AND s.department_id = @scope_department_id",
            _ => $"{prefix} FALSE"
        };
    }

    private static string BuildSalesHistoryWhereClause(SaleHistoryQuery query, InventoryScope scope)
    {
        var conditions = new List<string>();

        switch (scope.Kind)
        {
            case InventoryScopeKind.Company:
                break;
            case InventoryScopeKind.Market:
                conditions.Add("s.market_id = @scope_market_id");
                break;
            case InventoryScopeKind.Department:
                conditions.Add("s.market_id = @scope_market_id");
                conditions.Add("s.department_id = @scope_department_id");
                break;
            default:
                conditions.Add("FALSE");
                break;
        }

        if (query.DateFrom.HasValue)
        {
            conditions.Add("s.sale_date >= @date_from");
        }

        if (query.DateTo.HasValue)
        {
            conditions.Add("s.sale_date <= @date_to");
        }

        if (query.MarketId.HasValue)
        {
            conditions.Add("s.market_id = @market_id");
        }

        if (query.CashierUserId.HasValue)
        {
            conditions.Add("s.created_by_user_id = @cashier_user_id");
        }

        if (!string.IsNullOrWhiteSpace(query.Status))
        {
            conditions.Add("s.status = @status");
        }

        return conditions.Count == 0
            ? string.Empty
            : $"WHERE {string.Join(" AND ", conditions)}";
    }

    private static string BuildSalesSummaryWhereClause(SalesSummaryQuery query, InventoryScope scope)
    {
        var conditions = new List<string>();

        switch (scope.Kind)
        {
            case InventoryScopeKind.Company:
                break;
            case InventoryScopeKind.Market:
                conditions.Add("s.market_id = @scope_market_id");
                break;
            case InventoryScopeKind.Department:
                conditions.Add("s.market_id = @scope_market_id");
                conditions.Add("s.department_id = @scope_department_id");
                break;
            default:
                conditions.Add("FALSE");
                break;
        }

        if (query.From.HasValue)
        {
            conditions.Add("s.sale_date >= @from");
        }

        if (query.To.HasValue)
        {
            conditions.Add("s.sale_date <= @to");
        }

        return conditions.Count == 0
            ? string.Empty
            : $"WHERE {string.Join(" AND ", conditions)}";
    }

    private static string BuildAiSalesWhereClause(AiBusinessDataQuery query, InventoryScope scope)
    {
        var conditions = BuildSalesScopeConditions(scope);

        if (query.From.HasValue)
        {
            conditions.Add("s.sale_date >= @from");
        }

        if (query.To.HasValue)
        {
            conditions.Add("s.sale_date <= @to");
        }

        if (query.MarketId.HasValue)
        {
            conditions.Add("s.market_id = @market_id");
        }

        if (query.DepartmentId.HasValue)
        {
            conditions.Add("s.department_id = @department_id");
        }

        return conditions.Count == 0
            ? string.Empty
            : $"WHERE {string.Join(" AND ", conditions)}";
    }

    private static string BuildAiInventoryWhereClause(AiBusinessDataQuery query, InventoryScope scope)
    {
        var conditions = BuildInventoryScopeConditions(scope);
        conditions.Add("i.quantity <= p.min_stock_alert");

        if (query.MarketId.HasValue)
        {
            conditions.Add("i.market_id = @market_id");
        }

        if (query.DepartmentId.HasValue)
        {
            conditions.Add("i.department_id = @department_id");
        }

        return conditions.Count == 0
            ? string.Empty
            : $"WHERE {string.Join(" AND ", conditions)}";
    }

    private static string BuildAiInventoryForecastInventoryWhereClause(
        AiInventoryForecastRequest request,
        InventoryScope scope)
    {
        var conditions = BuildInventoryScopeConditions(scope);

        if (request.MarketId.HasValue)
        {
            conditions.Add("i.market_id = @market_id");
        }

        if (request.DepartmentId.HasValue)
        {
            conditions.Add("i.department_id = @department_id");
        }

        return conditions.Count == 0
            ? string.Empty
            : $"WHERE {string.Join(" AND ", conditions)}";
    }

    private static string BuildAiInventoryForecastSalesWhereClause(
        AiInventoryForecastRequest request,
        InventoryScope scope)
    {
        var conditions = BuildSalesScopeConditions(scope);
        conditions.Add("s.sale_date >= CURRENT_DATE - (@sales_history_days - 1) * INTERVAL '1 day'");
        conditions.Add("s.sale_date <= CURRENT_DATE");

        if (request.MarketId.HasValue)
        {
            conditions.Add("s.market_id = @market_id");
        }

        if (request.DepartmentId.HasValue)
        {
            conditions.Add("s.department_id = @department_id");
        }

        return $"WHERE {string.Join(" AND ", conditions)}";
    }

    private static string BuildAiInventoryRecommendationInventoryWhereClause(
        AiInventoryRecommendationRequest request,
        InventoryScope scope)
    {
        var conditions = BuildInventoryScopeConditions(scope);

        if (request.MarketId.HasValue)
        {
            conditions.Add("i.market_id = @market_id");
        }

        if (request.DepartmentId.HasValue)
        {
            conditions.Add("i.department_id = @department_id");
        }

        return conditions.Count == 0
            ? string.Empty
            : $"WHERE {string.Join(" AND ", conditions)}";
    }

    private static string BuildAiInventoryRecommendationSalesWhereClause(
        AiInventoryRecommendationRequest request,
        InventoryScope scope)
    {
        var conditions = BuildSalesScopeConditions(scope);
        conditions.Add("s.sale_date >= CURRENT_DATE - (@sales_history_days - 1) * INTERVAL '1 day'");
        conditions.Add("s.sale_date <= CURRENT_DATE");

        if (request.MarketId.HasValue)
        {
            conditions.Add("s.market_id = @market_id");
        }

        if (request.DepartmentId.HasValue)
        {
            conditions.Add("s.department_id = @department_id");
        }

        return $"WHERE {string.Join(" AND ", conditions)}";
    }

    private static string BuildAiInventoryMovementWhereClause(AiBusinessDataQuery query, InventoryScope scope)
    {
        var conditions = BuildInventoryScopeConditions(scope);

        if (query.From.HasValue)
        {
            conditions.Add("im.created_at >= @from");
        }

        if (query.To.HasValue)
        {
            conditions.Add("im.created_at < @to + INTERVAL '1 day'");
        }

        if (query.MarketId.HasValue)
        {
            conditions.Add("i.market_id = @market_id");
        }

        if (query.DepartmentId.HasValue)
        {
            conditions.Add("i.department_id = @department_id");
        }

        return conditions.Count == 0
            ? string.Empty
            : $"WHERE {string.Join(" AND ", conditions)}";
    }

    private static string BuildAiPurchaseWhereClause(AiBusinessDataQuery query, InventoryScope scope)
    {
        var conditions = new List<string>();

        switch (scope.Kind)
        {
            case InventoryScopeKind.Company:
                break;
            case InventoryScopeKind.Market:
            case InventoryScopeKind.Department:
                conditions.Add("p.market_id = @scope_market_id");
                break;
            default:
                conditions.Add("FALSE");
                break;
        }

        if (query.From.HasValue)
        {
            conditions.Add("p.purchase_date >= @from");
        }

        if (query.To.HasValue)
        {
            conditions.Add("p.purchase_date <= @to");
        }

        if (query.MarketId.HasValue)
        {
            conditions.Add("p.market_id = @market_id");
        }

        return conditions.Count == 0
            ? string.Empty
            : $"WHERE {string.Join(" AND ", conditions)}";
    }

    private static List<string> BuildSalesScopeConditions(InventoryScope scope)
    {
        return scope.Kind switch
        {
            InventoryScopeKind.Company => [],
            InventoryScopeKind.Market => ["s.market_id = @scope_market_id"],
            InventoryScopeKind.Department => ["s.market_id = @scope_market_id", "s.department_id = @scope_department_id"],
            _ => ["FALSE"]
        };
    }

    private static List<string> BuildInventoryScopeConditions(InventoryScope scope)
    {
        return scope.Kind switch
        {
            InventoryScopeKind.Company => [],
            InventoryScopeKind.Market => ["i.market_id = @scope_market_id"],
            InventoryScopeKind.Department => ["i.market_id = @scope_market_id", "i.department_id = @scope_department_id"],
            _ => ["FALSE"]
        };
    }

    private static void AddSalesHistoryParameters(NpgsqlCommand command, SaleHistoryQuery query)
    {
        if (query.DateFrom.HasValue)
        {
            command.Parameters.AddWithValue("date_from", query.DateFrom.Value);
        }

        if (query.DateTo.HasValue)
        {
            command.Parameters.AddWithValue("date_to", query.DateTo.Value);
        }

        if (query.MarketId.HasValue)
        {
            command.Parameters.AddWithValue("market_id", query.MarketId.Value);
        }

        if (query.CashierUserId.HasValue)
        {
            command.Parameters.AddWithValue("cashier_user_id", query.CashierUserId.Value);
        }

        if (!string.IsNullOrWhiteSpace(query.Status))
        {
            command.Parameters.AddWithValue("status", query.Status.Trim());
        }
    }

    private static void AddSalesSummaryParameters(NpgsqlCommand command, SalesSummaryQuery query)
    {
        if (query.From.HasValue)
        {
            command.Parameters.AddWithValue("from", query.From.Value);
        }

        if (query.To.HasValue)
        {
            command.Parameters.AddWithValue("to", query.To.Value);
        }
    }

    private static void AddAiBusinessDataParameters(NpgsqlCommand command, AiBusinessDataQuery query)
    {
        if (query.From.HasValue)
        {
            command.Parameters.AddWithValue("from", query.From.Value);
        }

        if (query.To.HasValue)
        {
            command.Parameters.AddWithValue("to", query.To.Value);
        }

        if (query.MarketId.HasValue)
        {
            command.Parameters.AddWithValue("market_id", query.MarketId.Value);
        }

        if (query.DepartmentId.HasValue)
        {
            command.Parameters.AddWithValue("department_id", query.DepartmentId.Value);
        }
    }

    private static void AddAiInventoryForecastParameters(
        NpgsqlCommand command,
        AiInventoryForecastRequest request)
    {
        command.Parameters.AddWithValue("sales_history_days", request.SalesHistoryDays);

        if (request.MarketId.HasValue)
        {
            command.Parameters.AddWithValue("market_id", request.MarketId.Value);
        }

        if (request.DepartmentId.HasValue)
        {
            command.Parameters.AddWithValue("department_id", request.DepartmentId.Value);
        }
    }

    private static void AddAiInventoryRecommendationParameters(
        NpgsqlCommand command,
        AiInventoryRecommendationRequest request)
    {
        command.Parameters.AddWithValue("sales_history_days", request.SalesHistoryDays);

        if (request.MarketId.HasValue)
        {
            command.Parameters.AddWithValue("market_id", request.MarketId.Value);
        }

        if (request.DepartmentId.HasValue)
        {
            command.Parameters.AddWithValue("department_id", request.DepartmentId.Value);
        }
    }

    private static string GetSalesHistorySortColumn(string? sortBy)
    {
        return sortBy?.Trim() switch
        {
            { } value when string.Equals(value, SalesHistorySortFields.ReferenceNumber, StringComparison.OrdinalIgnoreCase) => "s.reference_number",
            { } value when string.Equals(value, SalesHistorySortFields.MarketName, StringComparison.OrdinalIgnoreCase) => "m.name",
            { } value when string.Equals(value, SalesHistorySortFields.CashierName, StringComparison.OrdinalIgnoreCase) => "u.full_name",
            { } value when string.Equals(value, SalesHistorySortFields.Status, StringComparison.OrdinalIgnoreCase) => "s.status",
            { } value when string.Equals(value, SalesHistorySortFields.PaymentMethod, StringComparison.OrdinalIgnoreCase) => "s.payment_method",
            { } value when string.Equals(value, SalesHistorySortFields.TotalAmount, StringComparison.OrdinalIgnoreCase) => "s.total_amount",
            { } value when string.Equals(value, SalesHistorySortFields.ItemCount, StringComparison.OrdinalIgnoreCase) => "item_count",
            _ => "s.sale_date"
        };
    }

    private static void AddInventoryScopeParameters(NpgsqlCommand command, InventoryScope scope)
    {
        if (scope.MarketId.HasValue)
        {
            command.Parameters.AddWithValue("scope_market_id", scope.MarketId.Value);
        }

        if (scope.DepartmentId.HasValue)
        {
            command.Parameters.AddWithValue("scope_department_id", scope.DepartmentId.Value);
        }
    }

    private static bool CanAccessInventoryTarget(
        InventoryScope scope,
        int marketId,
        int? departmentId)
    {
        return scope.Kind switch
        {
            InventoryScopeKind.Company => true,
            InventoryScopeKind.Market => scope.MarketId == marketId,
            InventoryScopeKind.Department => scope.MarketId == marketId && scope.DepartmentId == departmentId,
            _ => false
        };
    }

    private static bool CanAccessSaleMarket(
        InventoryScope scope,
        int marketId)
    {
        return scope.Kind switch
        {
            InventoryScopeKind.Company => true,
            InventoryScopeKind.Market => scope.MarketId == marketId,
            InventoryScopeKind.Department => scope.MarketId == marketId,
            _ => false
        };
    }

    private static PosInventoryTarget? GetPosInventoryTarget(
        InventoryScope scope,
        int marketId)
    {
        return scope.Kind switch
        {
            InventoryScopeKind.Company => new PosInventoryTarget(marketId),
            InventoryScopeKind.Market when scope.MarketId == marketId => new PosInventoryTarget(marketId),
            InventoryScopeKind.Department when scope.MarketId == marketId && scope.DepartmentId.HasValue =>
                new PosInventoryTarget(marketId, scope.DepartmentId.Value),
            _ => null
        };
    }

    private async Task RecordInventoryMovementAsync(
        string quotedSchemaName,
        int inventoryId,
        string movementType,
        int quantityChanged,
        int? createdByUserId,
        string referenceNumber,
        CancellationToken cancellationToken,
        NpgsqlTransaction transaction,
        string? reason = null,
        string? note = null)
    {
        var savepointCreated = false;

        if (InventoryMovementTableRepairCache.ContainsKey(quotedSchemaName))
        {
            await transaction.SaveAsync(InventoryMovementSavepointName, cancellationToken);
            savepointCreated = true;

            try
            {
                await InsertInventoryMovementAsync(
                    quotedSchemaName,
                    inventoryId,
                    movementType,
                    quantityChanged,
                    createdByUserId,
                    referenceNumber,
                    cancellationToken,
                    transaction,
                    reason,
                    note);

                return;
            }
            catch (PostgresException exception) when (IsMissingInventoryMovementObject(exception))
            {
                await transaction.RollbackAsync(InventoryMovementSavepointName, cancellationToken);
                InventoryMovementTableRepairCache.TryRemove(quotedSchemaName, out _);
            }
        }

        if (!savepointCreated)
        {
            await transaction.SaveAsync(InventoryMovementSavepointName, cancellationToken);
        }

        try
        {
            await InsertInventoryMovementAsync(
                quotedSchemaName,
                inventoryId,
                movementType,
                quantityChanged,
                createdByUserId,
                referenceNumber,
                cancellationToken,
                transaction,
                reason,
                note);

            InventoryMovementTableRepairCache.TryAdd(quotedSchemaName, 0);
        }
        catch (PostgresException exception) when (IsMissingInventoryMovementObject(exception))
        {
            await transaction.RollbackAsync(InventoryMovementSavepointName, cancellationToken);
            await EnsureInventoryMovementsTableAsync(
                quotedSchemaName,
                cancellationToken,
                transaction);
            await InsertInventoryMovementAsync(
                quotedSchemaName,
                inventoryId,
                movementType,
                quantityChanged,
                createdByUserId,
                referenceNumber,
                cancellationToken,
                transaction,
                reason,
                note);

            InventoryMovementTableRepairCache.TryAdd(quotedSchemaName, 0);
        }
    }

    private async Task InsertInventoryMovementAsync(
        string quotedSchemaName,
        int inventoryId,
        string movementType,
        int quantityChanged,
        int? createdByUserId,
        string referenceNumber,
        CancellationToken cancellationToken,
        NpgsqlTransaction transaction,
        string? reason,
        string? note)
    {
        await using var command = await CreateCommandAsync($"""
            INSERT INTO {quotedSchemaName}.inventory_movements (
                inventory_id,
                movement_type,
                quantity_changed,
                reference_number,
                reason,
                note,
                created_by_user_id)
            VALUES (
                @inventory_id,
                @movement_type,
                @quantity_changed,
                @reference_number,
                @reason,
                @note,
                @created_by_user_id);
            """, cancellationToken, transaction);
        command.Parameters.AddWithValue("inventory_id", inventoryId);
        command.Parameters.AddWithValue("movement_type", movementType);
        command.Parameters.AddWithValue("quantity_changed", quantityChanged);
        command.Parameters.AddWithValue("reference_number", referenceNumber);
        command.Parameters.AddWithValue("reason", DbValue(reason));
        command.Parameters.AddWithValue("note", DbValue(note));
        command.Parameters.AddWithValue("created_by_user_id", DbValue(createdByUserId));

        await command.ExecuteNonQueryAsync(cancellationToken);
    }

    private async Task EnsureInventoryMovementsTableAsync(
        string quotedSchemaName,
        CancellationToken cancellationToken,
        NpgsqlTransaction transaction)
    {
        await using (var lockCommand = await CreateCommandAsync(
            "SELECT pg_advisory_xact_lock(hashtext(@lock_key));",
            cancellationToken,
            transaction))
        {
            lockCommand.Parameters.AddWithValue(
                "lock_key",
                $"tenant:{quotedSchemaName}:inventory_movements");

            await lockCommand.ExecuteNonQueryAsync(cancellationToken);
        }

        await using var command = await CreateCommandAsync($"""
            CREATE TABLE IF NOT EXISTS {quotedSchemaName}.inventory_movements (
                id                  SERIAL PRIMARY KEY,
                inventory_id        INT         NOT NULL REFERENCES {quotedSchemaName}.inventory(id) ON DELETE CASCADE,
                movement_type       VARCHAR(30) NOT NULL,
                quantity_changed    INT         NOT NULL,
                reference_number    VARCHAR(100),
                reason              VARCHAR(100),
                note                TEXT,
                created_by_user_id  INT,
                created_at          TIMESTAMPTZ NOT NULL DEFAULT NOW()
            );
            ALTER TABLE {quotedSchemaName}.inventory_movements
                ADD COLUMN IF NOT EXISTS reason VARCHAR(100),
                ADD COLUMN IF NOT EXISTS note TEXT;
            CREATE INDEX IF NOT EXISTS idx_inventory_movements_inventory
                ON {quotedSchemaName}.inventory_movements(inventory_id);
            CREATE INDEX IF NOT EXISTS idx_inventory_movements_created
                ON {quotedSchemaName}.inventory_movements(created_at);
            """, cancellationToken, transaction);

        await command.ExecuteNonQueryAsync(cancellationToken);
    }

    private static bool IsMissingInventoryMovementObject(PostgresException exception)
    {
        return exception.SqlState is
            "42P01" or // undefined_table
            "42703";   // undefined_column
    }

    private async Task EnsureSaleReferenceNumbersAsync(
        string quotedSchemaName,
        CancellationToken cancellationToken)
    {
        if (TryUseSaleReferenceNumberRepairCache(quotedSchemaName))
        {
            _logger.LogDebug(
                "Skipping sale reference number repair for tenant schema {SchemaName} because it is already cached.",
                quotedSchemaName);
            return;
        }

        var preState = await GetSaleReferenceNumberSchemaDiagnosticsAsync(
            quotedSchemaName,
            cancellationToken);

        _logger.LogInformation(
            "Ensuring sale reference number infrastructure for tenant schema {SchemaName}. Existing state: {SchemaState}",
            quotedSchemaName,
            preState);

        if (preState.HasCompleteInfrastructure)
        {
            _logger.LogDebug(
                "Skipping sale reference number repair for tenant schema {SchemaName} because it is already healthy.",
                quotedSchemaName);
            MarkSaleReferenceNumberRepairCacheHealthy(quotedSchemaName);
            return;
        }

        await using var transaction = await BeginTransactionAsync(cancellationToken);

        await using var lockCommand = await CreateCommandAsync(
            "SELECT pg_advisory_xact_lock(hashtext(@lock_key));",
            cancellationToken,
            transaction);
        lockCommand.Parameters.AddWithValue(
            "lock_key",
            $"tenant:{quotedSchemaName}:sale_reference_numbers");

        await lockCommand.ExecuteNonQueryAsync(cancellationToken);

        string? currentRepairStep = null;

        try
        {
            var lockedState = await GetSaleReferenceNumberSchemaDiagnosticsAsync(
                quotedSchemaName,
                cancellationToken,
                transaction);

            if (lockedState.HasCompleteInfrastructure)
            {
                _logger.LogDebug(
                    "Skipping sale reference number repair for tenant schema {SchemaName} because it became healthy before the repair lock was acquired.",
                    quotedSchemaName);
                await transaction.CommitAsync(cancellationToken);
                MarkSaleReferenceNumberRepairCacheHealthy(quotedSchemaName);
                return;
            }

            var referenceNumberColumnWasMissing = !lockedState.ColumnExists;
            var shouldBackfillReferenceNumbers =
                referenceNumberColumnWasMissing ||
                !lockedState.UniqueIndexExists ||
                lockedState.ReferenceNumbersNeedBackfill;
            var shouldSetReferenceNumberNotNull =
                referenceNumberColumnWasMissing ||
                lockedState.ReferenceNumberColumnIsNullable ||
                lockedState.ReferenceNumbersNeedBackfill;

            if (!lockedState.TriggerFunctionExists)
            {
                currentRepairStep = "CreateReferenceNumberTriggerFunction";
                await ExecuteSchemaRepairCommandAsync(
                    quotedSchemaName,
                    currentRepairStep,
                    $"CREATE FUNCTION public.assign_sale_reference_number()\n" +
                    "RETURNS trigger\n" +
                    "LANGUAGE plpgsql\n" +
                    "AS $$\n" +
                    "BEGIN\n" +
                    "    IF NEW.reference_number IS NULL OR btrim(NEW.reference_number) = '' THEN\n" +
                    "        NEW.reference_number := 'SALE-' || lpad(NEW.id::text, 6, '0');\n" +
                    "    END IF;\n" +
                    "    RETURN NEW;\n" +
                    "END;\n" +
                    "$$;\n",
                    transaction,
                    cancellationToken);
            }

            if (referenceNumberColumnWasMissing)
            {
                currentRepairStep = "AddReferenceNumberColumn";
                await ExecuteSchemaRepairCommandAsync(
                    quotedSchemaName,
                    currentRepairStep,
                    $"ALTER TABLE {quotedSchemaName}.sales\n" +
                    "    ADD COLUMN IF NOT EXISTS reference_number VARCHAR(50);\n",
                    transaction,
                    cancellationToken);
            }

            if (shouldBackfillReferenceNumbers)
            {
                currentRepairStep = "BackfillReferenceNumbers";
                await ExecuteSchemaRepairCommandAsync(
                    quotedSchemaName,
                    currentRepairStep,
                    $"UPDATE {quotedSchemaName}.sales\n" +
                    "SET reference_number = 'SALE-' || lpad(id::text, 6, '0')\n" +
                    "WHERE reference_number IS NULL OR btrim(reference_number) = '';\n",
                    transaction,
                    cancellationToken);

                if (shouldSetReferenceNumberNotNull)
                {
                    currentRepairStep = "SetReferenceNumberNotNull";
                    await ExecuteSchemaRepairCommandAsync(
                        quotedSchemaName,
                        currentRepairStep,
                        $"ALTER TABLE {quotedSchemaName}.sales\n" +
                        "    ALTER COLUMN reference_number SET NOT NULL;\n",
                        transaction,
                        cancellationToken);
                }
            }
            else if (shouldSetReferenceNumberNotNull)
            {
                currentRepairStep = "SetReferenceNumberNotNull";
                await ExecuteSchemaRepairCommandAsync(
                    quotedSchemaName,
                    currentRepairStep,
                    $"ALTER TABLE {quotedSchemaName}.sales\n" +
                    "    ALTER COLUMN reference_number SET NOT NULL;\n",
                    transaction,
                    cancellationToken);
            }

            if (!lockedState.UniqueIndexExists)
            {
                currentRepairStep = "CreateUniqueReferenceNumberIndex";
                await ExecuteSchemaRepairCommandAsync(
                    quotedSchemaName,
                    currentRepairStep,
                    $"CREATE UNIQUE INDEX IF NOT EXISTS ux_sales_reference_number\n" +
                    $"    ON {quotedSchemaName}.sales(reference_number);\n",
                    transaction,
                    cancellationToken);
            }

            if (!lockedState.TriggerExists)
            {
                currentRepairStep = "CreateTrigger";
                await ExecuteSchemaRepairCommandAsync(
                    quotedSchemaName,
                    currentRepairStep,
                    $"CREATE TRIGGER trg_sales_assign_reference_number\n" +
                    $"    BEFORE INSERT ON {quotedSchemaName}.sales\n" +
                    $"    FOR EACH ROW\n" +
                    $"    EXECUTE FUNCTION public.assign_sale_reference_number();\n",
                    transaction,
                    cancellationToken);
            }

            await transaction.CommitAsync(cancellationToken);

            var postState = await GetSaleReferenceNumberSchemaDiagnosticsAsync(
                quotedSchemaName,
                cancellationToken);

            if (postState.HasCompleteInfrastructure)
            {
                MarkSaleReferenceNumberRepairCacheHealthy(quotedSchemaName);
            }

            _logger.LogInformation(
                "Sale reference number infrastructure ensured for tenant schema {SchemaName}. New state: {SchemaState}",
                quotedSchemaName,
                postState);
        }
        catch (PostgresException postgresException)
        {
            await RollBackFailedSaleReferenceNumberRepairAsync(
                transaction,
                quotedSchemaName,
                currentRepairStep,
                postgresException,
                cancellationToken);

            var currentState = await GetSaleReferenceNumberSchemaDiagnosticsAfterFailureAsync(
                quotedSchemaName,
                cancellationToken,
                postgresException);

            _logger.LogError(
                postgresException,
                "Sale reference number infrastructure repair failed for tenant schema {SchemaName} during step {Step}. Pre-state: {PreState}. Current-state: {CurrentState}. SqlState={SqlState}. Detail={Detail}. Hint={Hint}.",
                quotedSchemaName,
                currentRepairStep,
                preState,
                currentState,
                postgresException.SqlState,
                postgresException.Detail,
                postgresException.Hint);

            if (IsPermissionDenied(postgresException))
            {
                throw new SaleReferenceNumberRepairException(
                    quotedSchemaName,
                    "Tenant sales schema repair failed because the database user does not have sufficient privileges to alter the tenant schema or create triggers.",
                    postgresException);
            }

            throw new SaleReferenceNumberRepairException(
                quotedSchemaName,
                "Tenant sales schema repair failed while creating or updating sale reference number infrastructure.",
                postgresException);
        }
    }

    private async Task RollBackFailedSaleReferenceNumberRepairAsync(
        NpgsqlTransaction transaction,
        string quotedSchemaName,
        string? currentRepairStep,
        PostgresException rootCause,
        CancellationToken cancellationToken)
    {
        try
        {
            await transaction.RollbackAsync(cancellationToken);
        }
        catch (Exception rollbackException)
        {
            _logger.LogWarning(
                rollbackException,
                "Failed to roll back sale reference number repair transaction for tenant schema {SchemaName} after step {Step}. Original SqlState={SqlState}.",
                quotedSchemaName,
                currentRepairStep,
                rootCause.SqlState);
        }
    }

    private async Task<SaleReferenceNumberSchemaDiagnostics> GetSaleReferenceNumberSchemaDiagnosticsAfterFailureAsync(
        string quotedSchemaName,
        CancellationToken cancellationToken,
        PostgresException rootCause)
    {
        try
        {
            return await GetSaleReferenceNumberSchemaDiagnosticsAsync(
                quotedSchemaName,
                cancellationToken);
        }
        catch (Exception diagnosticsException)
        {
            _logger.LogWarning(
                diagnosticsException,
                "Failed to collect sale reference number diagnostics for tenant schema {SchemaName} after repair failure. Original SqlState={SqlState}.",
                quotedSchemaName,
                rootCause.SqlState);

            return new SaleReferenceNumberSchemaDiagnostics(false, false, false, false, false, false);
        }
    }

    private async Task<T> ExecuteWithSaleReferenceNumberRecoveryAsync<T>(
        string quotedSchemaName,
        Func<Task<T>> operation,
        CancellationToken cancellationToken)
    {
        await EnsureSaleReferenceNumbersAsync(quotedSchemaName, cancellationToken);

        try
        {
            return await operation();
        }
        catch (PostgresException postgresException)
            when (IsSaleReferenceNumberInfrastructureFailure(postgresException))
        {
            _logger.LogWarning(
                postgresException,
                "Sale reference number operation failed for tenant schema {SchemaName}; clearing cached schema state, repairing, and retrying once.",
                quotedSchemaName);

            SaleReferenceNumberRepairCache.TryRemove(quotedSchemaName, out _);
            await EnsureSaleReferenceNumbersAsync(quotedSchemaName, cancellationToken);

            return await operation();
        }
        catch (InvalidCastException invalidCastException)
        {
            _logger.LogWarning(
                invalidCastException,
                "Sale reference number operation failed for tenant schema {SchemaName} while reading sale data; clearing cached schema state, repairing, and retrying once.",
                quotedSchemaName);

            SaleReferenceNumberRepairCache.TryRemove(quotedSchemaName, out _);
            await EnsureSaleReferenceNumbersAsync(quotedSchemaName, cancellationToken);

            return await operation();
        }
    }

    private static bool TryUseSaleReferenceNumberRepairCache(string quotedSchemaName)
    {
        if (!SaleReferenceNumberRepairCache.TryGetValue(quotedSchemaName, out var cachedAt))
        {
            return false;
        }

        if (DateTimeOffset.UtcNow - cachedAt < SaleReferenceNumberRepairCacheTtl)
        {
            return true;
        }

        SaleReferenceNumberRepairCache.TryRemove(quotedSchemaName, out _);
        return false;
    }

    private static void MarkSaleReferenceNumberRepairCacheHealthy(string quotedSchemaName)
    {
        SaleReferenceNumberRepairCache[quotedSchemaName] = DateTimeOffset.UtcNow;
    }

    private async Task ExecuteSchemaRepairCommandAsync(
        string quotedSchemaName,
        string stepName,
        string commandText,
        NpgsqlTransaction transaction,
        CancellationToken cancellationToken)
    {
        await using var command = await CreateCommandAsync(commandText, cancellationToken, transaction);

        _logger.LogDebug(
            "Executing sale reference number repair step {StepName} for tenant schema {SchemaName}.",
            stepName,
            quotedSchemaName);

        await command.ExecuteNonQueryAsync(cancellationToken);
    }

    private static bool IsPermissionDenied(PostgresException exception)
    {
        return exception.SqlState == "42501" ||
            exception.Message.Contains("permission denied", StringComparison.OrdinalIgnoreCase);
    }

    private static bool IsSaleReferenceNumberInfrastructureFailure(PostgresException exception)
    {
        if (exception.SqlState == PostgresErrorCodes.NotNullViolation &&
            string.Equals(exception.ColumnName, "reference_number", StringComparison.Ordinal))
        {
            return true;
        }

        if (exception.SqlState == PostgresErrorCodes.UndefinedColumn &&
            ExceptionMentionsSaleReferenceNumberInfrastructure(exception))
        {
            return true;
        }

        if (exception.SqlState is "42704" or "42883" &&
            ExceptionMentionsSaleReferenceNumberInfrastructure(exception))
        {
            return true;
        }

        return false;
    }

    private static bool ExceptionMentionsSaleReferenceNumberInfrastructure(PostgresException exception)
    {
        return exception.MessageText.Contains("reference_number", StringComparison.OrdinalIgnoreCase) ||
            exception.MessageText.Contains("assign_sale_reference_number", StringComparison.OrdinalIgnoreCase);
    }

    private async Task<SaleReferenceNumberSchemaDiagnostics> GetSaleReferenceNumberSchemaDiagnosticsAsync(
        string quotedSchemaName,
        CancellationToken cancellationToken,
        NpgsqlTransaction? transaction = null)
    {
        var actualSchemaName = quotedSchemaName.Trim('"');

        await using var command = await CreateCommandAsync($"""
            SELECT EXISTS (
                       SELECT 1
                       FROM information_schema.columns
                       WHERE table_schema = @schema_name
                         AND table_name = 'sales'
                         AND column_name = 'reference_number'
                   ) AS column_exists,
                   EXISTS (
                       SELECT 1
                       FROM information_schema.columns
                       WHERE table_schema = @schema_name
                         AND table_name = 'sales'
                         AND column_name = 'reference_number'
                         AND is_nullable = 'YES'
                   ) AS reference_number_column_is_nullable,
                   EXISTS (
                       SELECT 1
                       FROM pg_catalog.pg_trigger trigger
                       INNER JOIN pg_catalog.pg_class relation ON relation.oid = trigger.tgrelid
                       INNER JOIN pg_catalog.pg_namespace namespace ON namespace.oid = relation.relnamespace
                       WHERE namespace.nspname = @schema_name
                         AND relation.relname = 'sales'
                         AND trigger.tgname = 'trg_sales_assign_reference_number'
                   ) AS trigger_exists,
                   to_regclass(format('%I.ux_sales_reference_number', @schema_name)) IS NOT NULL AS unique_index_exists,
                   to_regprocedure('public.assign_sale_reference_number()') IS NOT NULL AS trigger_function_exists;
            """,
            cancellationToken,
            transaction);
        command.Parameters.AddWithValue("schema_name", actualSchemaName);

        await using var reader = await command.ExecuteReaderAsync(cancellationToken);
        var diagnostics = await reader.ReadAsync(cancellationToken)
            ? new SaleReferenceNumberSchemaDiagnostics(
                reader.GetBoolean(0),
                reader.GetBoolean(1),
                reader.GetBoolean(2),
                reader.GetBoolean(3),
                reader.GetBoolean(4),
                ReferenceNumbersNeedBackfill: false)
            : new SaleReferenceNumberSchemaDiagnostics(false, false, false, false, false, false);

        await reader.DisposeAsync();

        if (!diagnostics.ColumnExists)
        {
            return diagnostics;
        }

        await using var nullReferenceCommand = await CreateCommandAsync($"""
            SELECT EXISTS (
                SELECT 1
                FROM {quotedSchemaName}.sales
                WHERE reference_number IS NULL OR btrim(reference_number) = ''
            );
            """,
            cancellationToken,
            transaction);

        var needsBackfill = (bool)(await nullReferenceCommand.ExecuteScalarAsync(cancellationToken) ?? false);
        return diagnostics with { ReferenceNumbersNeedBackfill = needsBackfill };
    }

    private sealed record SaleReferenceNumberSchemaDiagnostics(
        bool ColumnExists,
        bool ReferenceNumberColumnIsNullable,
        bool TriggerExists,
        bool UniqueIndexExists,
        bool TriggerFunctionExists,
        bool ReferenceNumbersNeedBackfill)
    {
        public bool HasCompleteInfrastructure =>
            ColumnExists &&
            !ReferenceNumberColumnIsNullable &&
            TriggerExists &&
            UniqueIndexExists &&
            TriggerFunctionExists &&
            !ReferenceNumbersNeedBackfill;
    }

    private async Task InsertSaleItemAsync(
        string quotedSchemaName,
        int saleId,
        CreateSaleItemRequest item,
        CancellationToken cancellationToken,
        NpgsqlTransaction transaction)
    {
        await using var command = await CreateCommandAsync($"""
            INSERT INTO {quotedSchemaName}.sale_items (
                sale_id,
                product_id,
                quantity,
                unit_price)
            VALUES (
                @sale_id,
                @product_id,
                @quantity,
                @unit_price);
            """, cancellationToken, transaction);
        command.Parameters.AddWithValue("sale_id", saleId);
        command.Parameters.AddWithValue("product_id", item.ProductId);
        command.Parameters.AddWithValue("quantity", item.Quantity);
        command.Parameters.AddWithValue("unit_price", item.UnitPrice);

        await command.ExecuteNonQueryAsync(cancellationToken);
    }

    private async Task InsertPurchaseItemAsync(
        string quotedSchemaName,
        int purchaseId,
        CreatePurchaseItemRequest item,
        CancellationToken cancellationToken,
        NpgsqlTransaction transaction)
    {
        await using var command = await CreateCommandAsync($"""
            INSERT INTO {quotedSchemaName}.purchase_items (
                purchase_id,
                product_id,
                quantity,
                unit_cost)
            VALUES (
                @purchase_id,
                @product_id,
                @quantity,
                @unit_cost);
            """, cancellationToken, transaction);
        command.Parameters.AddWithValue("purchase_id", purchaseId);
        command.Parameters.AddWithValue("product_id", item.ProductId);
        command.Parameters.AddWithValue("quantity", item.Quantity);
        command.Parameters.AddWithValue("unit_cost", item.UnitCost);

        await command.ExecuteNonQueryAsync(cancellationToken);
    }

    private async Task ReplacePurchaseItemsAsync(
        string quotedSchemaName,
        int purchaseId,
        IReadOnlyCollection<CreatePurchaseItemRequest> items,
        CancellationToken cancellationToken,
        NpgsqlTransaction transaction)
    {
        await using var deleteCommand = await CreateCommandAsync($"""
            DELETE FROM {quotedSchemaName}.purchase_items
            WHERE purchase_id = @purchase_id;
            """, cancellationToken, transaction);
        deleteCommand.Parameters.AddWithValue("purchase_id", purchaseId);
        await deleteCommand.ExecuteNonQueryAsync(cancellationToken);

        foreach (var item in items)
        {
            await InsertPurchaseItemAsync(
                quotedSchemaName,
                purchaseId,
                item,
                cancellationToken,
                transaction);
        }
    }

    private async Task<bool> PurchaseReferencesExistAsync(
        string quotedSchemaName,
        int supplierId,
        int marketId,
        IReadOnlyCollection<CreatePurchaseItemRequest> items,
        CancellationToken cancellationToken,
        NpgsqlTransaction transaction)
    {
        await using var command = await CreateCommandAsync($"""
            SELECT EXISTS (SELECT 1 FROM {quotedSchemaName}.suppliers WHERE id = @supplier_id AND is_active = TRUE)
               AND EXISTS (SELECT 1 FROM {quotedSchemaName}.markets WHERE id = @market_id AND is_active = TRUE)
               AND (
                   SELECT COUNT(DISTINCT product_id)::int
                   FROM UNNEST(@product_ids) AS requested(product_id)
                   JOIN {quotedSchemaName}.products p ON p.id = requested.product_id AND p.is_active = TRUE
               ) = @product_count;
            """, cancellationToken, transaction);
        command.Parameters.AddWithValue("supplier_id", supplierId);
        command.Parameters.AddWithValue("market_id", marketId);
        command.Parameters.AddWithValue("product_ids", items.Select(item => item.ProductId).Distinct().ToArray());
        command.Parameters.AddWithValue("product_count", items.Select(item => item.ProductId).Distinct().Count());

        return (bool)(await command.ExecuteScalarAsync(cancellationToken) ?? false);
    }

    private async Task<bool> ApplyInventoryQuantityChangeByProductAsync(
        string quotedSchemaName,
        int productId,
        int marketId,
        int? departmentId,
        int quantityChange,
        string movementType,
        int? updatedByUserId,
        string referenceNumber,
        CancellationToken cancellationToken,
        NpgsqlTransaction transaction)
    {
        var inventoryId = await GetInventoryIdForUpdateAsync(
            quotedSchemaName,
            productId,
            marketId,
            departmentId,
            cancellationToken,
            transaction);

        if (inventoryId is null && quantityChange > 0)
        {
            inventoryId = await CreateEmptyInventoryItemAsync(
                quotedSchemaName,
                productId,
                marketId,
                departmentId,
                updatedByUserId,
                cancellationToken,
                transaction);
        }

        await using var command = await CreateCommandAsync($"""
            UPDATE {quotedSchemaName}.inventory
            SET quantity = quantity + @quantity_change,
                last_updated_by = @last_updated_by,
                updated_at = NOW()
            WHERE id = @inventory_id
              AND (@quantity_change >= 0 OR quantity + @quantity_change >= reserved_quantity)
            RETURNING id;
            """, cancellationToken, transaction);
        command.Parameters.AddWithValue("inventory_id", DbValue(inventoryId));
        command.Parameters.AddWithValue("quantity_change", quantityChange);
        command.Parameters.AddWithValue("last_updated_by", DbValue(updatedByUserId));

        var updatedInventoryId = (int?)await command.ExecuteScalarAsync(cancellationToken);

        if (updatedInventoryId is null)
        {
            return false;
        }

        var productBarcode = await GetProductBarcodeByIdAsync(
            quotedSchemaName,
            productId,
            cancellationToken,
            transaction);

        await RecordInventoryMovementAsync(
            quotedSchemaName,
            updatedInventoryId.Value,
            movementType,
            quantityChange,
            updatedByUserId,
            referenceNumber,
            cancellationToken,
            transaction);

        await InvalidateBarcodeLookupAsync(productBarcode, cancellationToken);

        return true;
    }

    private async Task<int?> GetInventoryIdForUpdateAsync(
        string quotedSchemaName,
        int productId,
        int marketId,
        int? departmentId,
        CancellationToken cancellationToken,
        NpgsqlTransaction transaction)
    {
        await using var command = await CreateCommandAsync($"""
            SELECT id
            FROM {quotedSchemaName}.inventory
            WHERE product_id = @product_id
              AND market_id = @market_id
              AND department_id IS NOT DISTINCT FROM @department_id
            ORDER BY id
            LIMIT 1
            FOR UPDATE;
            """, cancellationToken, transaction);
        command.Parameters.AddWithValue("product_id", productId);
        command.Parameters.AddWithValue("market_id", marketId);
        command.Parameters.AddWithValue("department_id", DbValue(departmentId));

        return (int?)await command.ExecuteScalarAsync(cancellationToken);
    }

    private async Task<int> CreateEmptyInventoryItemAsync(
        string quotedSchemaName,
        int productId,
        int marketId,
        int? departmentId,
        int? updatedByUserId,
        CancellationToken cancellationToken,
        NpgsqlTransaction transaction)
    {
        await using var command = await CreateCommandAsync($"""
            INSERT INTO {quotedSchemaName}.inventory (
                product_id,
                market_id,
                department_id,
                quantity,
                reserved_quantity,
                last_updated_by)
            VALUES (
                @product_id,
                @market_id,
                @department_id,
                0,
                0,
                @last_updated_by)
            RETURNING id;
            """, cancellationToken, transaction);
        command.Parameters.AddWithValue("product_id", productId);
        command.Parameters.AddWithValue("market_id", marketId);
        command.Parameters.AddWithValue("department_id", DbValue(departmentId));
        command.Parameters.AddWithValue("last_updated_by", DbValue(updatedByUserId));

        return (int?)await command.ExecuteScalarAsync(cancellationToken)
            ?? throw new InvalidOperationException("Inventory item was not created.");
    }

    private async Task<PurchaseReceiptState?> GetPurchaseReceiptStateForUpdateAsync(
        string quotedSchemaName,
        int purchaseId,
        CancellationToken cancellationToken,
        NpgsqlTransaction transaction)
    {
        await using var command = await CreateCommandAsync($"""
            SELECT status,
                   market_id
            FROM {quotedSchemaName}.purchases
            WHERE id = @purchase_id
            FOR UPDATE;
            """, cancellationToken, transaction);
        command.Parameters.AddWithValue("purchase_id", purchaseId);

        await using var reader = await command.ExecuteReaderAsync(cancellationToken);

        return await reader.ReadAsync(cancellationToken)
            ? new PurchaseReceiptState(reader.GetString(0), reader.GetInt32(1))
            : null;
    }

    private static bool ShouldReceivePurchase(
        string previousStatus,
        string status,
        int marketId)
    {
        return marketId > 0 &&
            string.Equals(status, "Received", StringComparison.OrdinalIgnoreCase) &&
            !string.Equals(previousStatus, "Received", StringComparison.OrdinalIgnoreCase);
    }

    private async Task ApplyPurchaseReceiptIfNeededAsync(
        string quotedSchemaName,
        PurchaseDto purchase,
        string previousStatus,
        int? updatedByUserId,
        CancellationToken cancellationToken,
        NpgsqlTransaction transaction)
    {
        if (!string.Equals(purchase.Status, "Received", StringComparison.OrdinalIgnoreCase) ||
            string.Equals(previousStatus, "Received", StringComparison.OrdinalIgnoreCase))
        {
            return;
        }

        var items = await GetPurchaseItemsAsync(quotedSchemaName, purchase.Id, cancellationToken, transaction);

        foreach (var item in items)
        {
            await using var updateItemCommand = await CreateCommandAsync($"""
                UPDATE {quotedSchemaName}.purchase_items
                SET received_quantity = quantity
                WHERE purchase_id = @purchase_id
                  AND product_id = @product_id;
                """, cancellationToken, transaction);
            updateItemCommand.Parameters.AddWithValue("purchase_id", purchase.Id);
            updateItemCommand.Parameters.AddWithValue("product_id", item.ProductId);
            await updateItemCommand.ExecuteNonQueryAsync(cancellationToken);

            await ApplyInventoryQuantityChangeByProductAsync(
                quotedSchemaName,
                item.ProductId,
                purchase.MarketId,
                departmentId: null,
                item.Quantity,
                "PurchaseReceived",
                updatedByUserId,
                $"purchase:{purchase.Id}",
                cancellationToken,
                transaction);
        }
    }

    private async Task<IReadOnlyCollection<PurchaseStockItem>> GetPurchaseItemsAsync(
        string quotedSchemaName,
        int purchaseId,
        CancellationToken cancellationToken,
        NpgsqlTransaction transaction)
    {
        var items = new List<PurchaseStockItem>();

        await using var command = await CreateCommandAsync($"""
            SELECT product_id,
                   quantity
            FROM {quotedSchemaName}.purchase_items
            WHERE purchase_id = @purchase_id;
            """, cancellationToken, transaction);
        command.Parameters.AddWithValue("purchase_id", purchaseId);

        await using var reader = await command.ExecuteReaderAsync(cancellationToken);

        while (await reader.ReadAsync(cancellationToken))
        {
            items.Add(new PurchaseStockItem(
                reader.GetInt32(0),
                reader.GetInt32(1)));
        }

        return items;
    }

    private async Task<IReadOnlyCollection<CreatePurchaseItemRequest>> GetPurchaseItemsAsCreateRequestsAsync(
        string quotedSchemaName,
        int purchaseId,
        CancellationToken cancellationToken,
        NpgsqlTransaction transaction)
    {
        var items = new List<CreatePurchaseItemRequest>();

        await using var command = await CreateCommandAsync($"""
            SELECT product_id,
                   quantity,
                   unit_cost
            FROM {quotedSchemaName}.purchase_items
            WHERE purchase_id = @purchase_id;
            """, cancellationToken, transaction);
        command.Parameters.AddWithValue("purchase_id", purchaseId);

        await using var reader = await command.ExecuteReaderAsync(cancellationToken);

        while (await reader.ReadAsync(cancellationToken))
        {
            items.Add(new CreatePurchaseItemRequest
            {
                ProductId = reader.GetInt32(0),
                Quantity = reader.GetInt32(1),
                UnitCost = reader.GetDecimal(2)
            });
        }

        return items;
    }

    private async Task<IReadOnlyCollection<PurchaseReceivingItem>> GetPurchaseItemsForReceivingAsync(
        string quotedSchemaName,
        int purchaseId,
        CancellationToken cancellationToken,
        NpgsqlTransaction transaction)
    {
        var items = new List<PurchaseReceivingItem>();

        await using var command = await CreateCommandAsync($"""
            SELECT id,
                   product_id,
                   quantity,
                   received_quantity
            FROM {quotedSchemaName}.purchase_items
            WHERE purchase_id = @purchase_id
            ORDER BY id
            FOR UPDATE;
            """, cancellationToken, transaction);
        command.Parameters.AddWithValue("purchase_id", purchaseId);

        await using var reader = await command.ExecuteReaderAsync(cancellationToken);

        while (await reader.ReadAsync(cancellationToken))
        {
            items.Add(new PurchaseReceivingItem(
                reader.GetInt32(0),
                reader.GetInt32(1),
                reader.GetInt32(2),
                reader.GetInt32(3)));
        }

        return items;
    }

    private static IReadOnlyDictionary<int, int> BuildReceiptQuantities(
        ReceivePurchaseRequest request,
        IReadOnlyCollection<PurchaseReceivingItem> items)
    {
        if (request.Items.Count == 0)
        {
            return items
                .GroupBy(item => item.ProductId)
                .Select(group => new
                {
                    ProductId = group.Key,
                    Quantity = group.Sum(item => item.Quantity - item.ReceivedQuantity)
                })
                .Where(item => item.Quantity > 0)
                .ToDictionary(item => item.ProductId, item => item.Quantity);
        }

        return request.Items
            .GroupBy(item => item.ProductId)
            .ToDictionary(group => group.Key, group => group.Sum(item => item.Quantity));
    }

    private async Task<string> GetPurchaseReceiptStatusAsync(
        string quotedSchemaName,
        int purchaseId,
        CancellationToken cancellationToken,
        NpgsqlTransaction transaction)
    {
        await using var command = await CreateCommandAsync($"""
            SELECT CASE
                WHEN COALESCE(SUM(quantity), 0) = COALESCE(SUM(received_quantity), 0) THEN 'Received'
                WHEN COALESCE(SUM(received_quantity), 0) > 0 THEN 'PartiallyReceived'
                ELSE 'Ordered'
            END
            FROM {quotedSchemaName}.purchase_items
            WHERE purchase_id = @purchase_id;
            """, cancellationToken, transaction);
        command.Parameters.AddWithValue("purchase_id", purchaseId);

        return (string)(await command.ExecuteScalarAsync(cancellationToken) ?? "Ordered");
    }

    private async Task<string?> GetProductBarcodeByIdAsync(
        string quotedSchemaName,
        int productId,
        CancellationToken cancellationToken,
        NpgsqlTransaction? transaction = null)
    {
        await using var command = await CreateCommandAsync($"""
            SELECT barcode
            FROM {quotedSchemaName}.products
            WHERE id = @product_id;
            """, cancellationToken, transaction);
        command.Parameters.AddWithValue("product_id", productId);

        return await command.ExecuteScalarAsync(cancellationToken) as string;
    }

    private async Task<string?> GetInventoryItemBarcodeByIdAsync(
        string quotedSchemaName,
        int inventoryId,
        CancellationToken cancellationToken)
    {
        await using var command = await CreateCommandAsync($"""
            SELECT p.barcode
            FROM {quotedSchemaName}.inventory i
            INNER JOIN {quotedSchemaName}.products p ON p.id = i.product_id
            WHERE i.id = @inventory_id;
            """, cancellationToken);
        command.Parameters.AddWithValue("inventory_id", inventoryId);

        return await command.ExecuteScalarAsync(cancellationToken) as string;
    }

    private async Task<BarcodeLookupCacheEntry?> GetBarcodeLookupCacheAsync(
        string cacheKey,
        CancellationToken cancellationToken)
    {
        return _cacheService is null
            ? null
            : await _cacheService.GetAsync<BarcodeLookupCacheEntry>(cacheKey, cancellationToken);
    }

    private async Task SetBarcodeLookupCacheAsync(
        string cacheKey,
        BarcodeLookupCacheEntry lookup,
        CancellationToken cancellationToken)
    {
        if (_cacheService is not null)
        {
            await _cacheService.SetAsync(cacheKey, lookup, _barcodeLookupCacheTtl, cancellationToken);
        }
    }

    private async Task InvalidateBarcodeLookupAsync(
        string? barcode,
        CancellationToken cancellationToken)
    {
        if (_cacheService is null || string.IsNullOrWhiteSpace(barcode))
        {
            return;
        }

        var cacheKey = CreateBarcodeCacheKey(
            await GetCurrentTenantIdAsync(cancellationToken),
            barcode);

        await _cacheService.RemoveAsync(cacheKey, cancellationToken);
    }

    private async Task<string> GetCurrentTenantIdAsync(CancellationToken cancellationToken)
    {
        return await _tenantProvider.GetCurrentSchemaNameAsync(cancellationToken);
    }

    private static string CreateBarcodeCacheKey(string tenantId, string barcode)
    {
        return $"tenant:{tenantId}:barcode:{barcode.Trim()}";
    }

    private static string CreateProductBarcodeLookupVariantKey(ProductListQuery query)
    {
        return string.Join(
            ':',
            "products",
            $"page={query.Page}",
            $"size={query.PageSize}",
            $"sort={NormalizeCacheKeyPart(query.SortBy)}",
            $"dir={NormalizeCacheKeyPart(query.SortDirection)}");
    }

    private static string CreateInventoryBarcodeLookupVariantKey(InventoryListQuery query, InventoryScope scope)
    {
        return string.Join(
            ':',
            "inventory",
            $"scope={CreateInventoryScopeCacheKeyPart(scope)}",
            $"page={query.Page}",
            $"size={query.PageSize}",
            $"sort={NormalizeCacheKeyPart(query.SortBy)}",
            $"dir={NormalizeCacheKeyPart(query.SortDirection)}");
    }

    private static string CreateInventoryScopeCacheKeyPart(InventoryScope scope)
    {
        return scope.Kind switch
        {
            InventoryScopeKind.Company => "company",
            InventoryScopeKind.Market => $"market:{scope.MarketId}",
            InventoryScopeKind.Department => $"department:{scope.MarketId}:{scope.DepartmentId}",
            _ => "none"
        };
    }

    private static string NormalizeCacheKeyPart(string? value)
    {
        return string.IsNullOrWhiteSpace(value)
            ? string.Empty
            : value.Trim().ToLowerInvariant();
    }

    private static int GetBarcodeLookupCacheTtlSeconds(IConfiguration? configuration)
    {
        var configuredValue = configuration?["Redis:BarcodeLookupTtlSeconds"];

        return int.TryParse(configuredValue, out var seconds) && seconds > 0
            ? seconds
            : DefaultBarcodeLookupCacheTtlSeconds;
    }

    private static async Task<ProductDto?> ReadProductAsync(
        NpgsqlCommand command,
        CancellationToken cancellationToken)
    {
        await using var reader = await command.ExecuteReaderAsync(cancellationToken);

        return await reader.ReadAsync(cancellationToken) ? ReadProduct(reader) : null;
    }

    private static async Task<MarketDto?> ReadMarketAsync(
        NpgsqlCommand command,
        CancellationToken cancellationToken)
    {
        await using var reader = await command.ExecuteReaderAsync(cancellationToken);

        return await reader.ReadAsync(cancellationToken) ? ReadMarket(reader) : null;
    }

    private static async Task<DepartmentDto?> ReadDepartmentAsync(
        NpgsqlCommand command,
        CancellationToken cancellationToken)
    {
        await using var reader = await command.ExecuteReaderAsync(cancellationToken);

        return await reader.ReadAsync(cancellationToken) ? ReadDepartment(reader) : null;
    }

    private static async Task<SupplierDto?> ReadSupplierAsync(
        NpgsqlCommand command,
        CancellationToken cancellationToken)
    {
        await using var reader = await command.ExecuteReaderAsync(cancellationToken);

        return await reader.ReadAsync(cancellationToken) ? ReadSupplier(reader) : null;
    }

    private async Task<bool> MarketNameExistsAsync(
        string schemaName,
        string name,
        int? excludedMarketId,
        CancellationToken cancellationToken,
        NpgsqlTransaction? transaction = null)
    {
        await using var command = await CreateCommandAsync($"""
            SELECT EXISTS (
                SELECT 1
                FROM {schemaName}.markets
                WHERE lower(name) = lower(@name)
                  AND (@excluded_market_id::integer IS NULL OR id <> @excluded_market_id::integer)
            );
            """, cancellationToken, transaction);
        command.Parameters.AddWithValue("name", name.Trim());
        command.Parameters.Add(new NpgsqlParameter("excluded_market_id", NpgsqlTypes.NpgsqlDbType.Integer)
        {
            Value = excludedMarketId ?? (object)DBNull.Value
        });

        return (bool)(await command.ExecuteScalarAsync(cancellationToken) ?? false);
    }

    private async Task<bool> MarketExistsAsync(
        string schemaName,
        int id,
        CancellationToken cancellationToken,
        NpgsqlTransaction? transaction = null)
    {
        await using var command = await CreateCommandAsync($"""
            SELECT EXISTS (
                SELECT 1
                FROM {schemaName}.markets
                WHERE id = @id
            );
            """, cancellationToken, transaction);
        command.Parameters.AddWithValue("id", id);

        return (bool)(await command.ExecuteScalarAsync(cancellationToken) ?? false);
    }

    private async Task<bool> DepartmentNameExistsAsync(
        string schemaName,
        int marketId,
        string name,
        int? excludedDepartmentId,
        CancellationToken cancellationToken,
        NpgsqlTransaction? transaction = null)
    {
        await using var command = await CreateCommandAsync($"""
            SELECT EXISTS (
                SELECT 1
                FROM {schemaName}.departments
                WHERE market_id = @market_id
                  AND lower(name) = lower(@name)
                  AND (@excluded_department_id::integer IS NULL OR id <> @excluded_department_id::integer)
            );
            """, cancellationToken, transaction);
        command.Parameters.AddWithValue("market_id", marketId);
        command.Parameters.AddWithValue("name", name.Trim());
        command.Parameters.Add(new NpgsqlParameter("excluded_department_id", NpgsqlTypes.NpgsqlDbType.Integer)
        {
            Value = excludedDepartmentId ?? (object)DBNull.Value
        });

        return (bool)(await command.ExecuteScalarAsync(cancellationToken) ?? false);
    }

    private async Task LockMarketsTableAsync(
        string schemaName,
        NpgsqlTransaction transaction,
        CancellationToken cancellationToken)
    {
        await using var command = await CreateCommandAsync(
            $"LOCK TABLE {schemaName}.markets IN SHARE ROW EXCLUSIVE MODE;",
            cancellationToken,
            transaction);

        await command.ExecuteNonQueryAsync(cancellationToken);
    }

    private async Task LockDepartmentsTableAsync(
        string schemaName,
        NpgsqlTransaction transaction,
        CancellationToken cancellationToken)
    {
        await using var command = await CreateCommandAsync(
            $"LOCK TABLE {schemaName}.departments IN SHARE ROW EXCLUSIVE MODE;",
            cancellationToken,
            transaction);

        await command.ExecuteNonQueryAsync(cancellationToken);
    }

    private static string BuildProductWhereClause(ProductListQuery query)
    {
        var conditions = new List<string>();

        if (!string.IsNullOrWhiteSpace(query.Search))
        {
            conditions.Add("(p.name ILIKE @search ESCAPE '\\' OR p.barcode ILIKE @search ESCAPE '\\')");
        }

        if (!string.IsNullOrWhiteSpace(query.Name))
        {
            conditions.Add("p.name ILIKE @name ESCAPE '\\'");
        }

        if (!string.IsNullOrWhiteSpace(query.Barcode))
        {
            conditions.Add("p.barcode ILIKE @barcode ESCAPE '\\'");
        }

        if (query.CategoryId.HasValue)
        {
            conditions.Add("p.category_id = @category_id");
        }

        if (!string.IsNullOrWhiteSpace(query.Category))
        {
            conditions.Add("c.name ILIKE @category ESCAPE '\\'");
        }

        if (query.IsActive.HasValue)
        {
            conditions.Add("p.is_active = @is_active");
        }
        else if (!query.IncludeInactive)
        {
            conditions.Add("p.is_active = TRUE");
        }

        return conditions.Count == 0
            ? string.Empty
            : $"WHERE {string.Join(" AND ", conditions)}";
    }

    private static void AddProductListParameters(NpgsqlCommand command, ProductListQuery query)
    {
        if (!string.IsNullOrWhiteSpace(query.Search))
        {
            command.Parameters.AddWithValue("search", LikePattern(query.Search));
        }

        if (!string.IsNullOrWhiteSpace(query.Name))
        {
            command.Parameters.AddWithValue("name", LikePattern(query.Name));
        }

        if (!string.IsNullOrWhiteSpace(query.Barcode))
        {
            command.Parameters.AddWithValue("barcode", LikePattern(query.Barcode));
        }

        if (query.CategoryId.HasValue)
        {
            command.Parameters.AddWithValue("category_id", query.CategoryId.Value);
        }

        if (!string.IsNullOrWhiteSpace(query.Category))
        {
            command.Parameters.AddWithValue("category", LikePattern(query.Category));
        }

        if (query.IsActive.HasValue)
        {
            command.Parameters.AddWithValue("is_active", query.IsActive.Value);
        }
    }

    private static bool IsProductBarcodeLookup(ProductListQuery query)
    {
        return !string.IsNullOrWhiteSpace(query.Barcode) &&
            string.IsNullOrWhiteSpace(query.Search) &&
            string.IsNullOrWhiteSpace(query.Name) &&
            string.IsNullOrWhiteSpace(query.Category) &&
            !query.CategoryId.HasValue &&
            !query.IsActive.HasValue &&
            !query.IncludeInactive &&
            query.Page == 1;
    }

    private static string GetProductSortColumn(string? sortBy)
    {
        return sortBy?.Trim() switch
        {
            { } value when string.Equals(value, ProductSortFields.Id, StringComparison.OrdinalIgnoreCase) => "p.id",
            { } value when string.Equals(value, ProductSortFields.Barcode, StringComparison.OrdinalIgnoreCase) => "p.barcode",
            { } value when string.Equals(value, ProductSortFields.Category, StringComparison.OrdinalIgnoreCase) => "c.name",
            { } value when string.Equals(value, ProductSortFields.CategoryName, StringComparison.OrdinalIgnoreCase) => "c.name",
            { } value when string.Equals(value, ProductSortFields.UnitPrice, StringComparison.OrdinalIgnoreCase) => "p.unit_price",
            { } value when string.Equals(value, ProductSortFields.CostPrice, StringComparison.OrdinalIgnoreCase) => "p.cost_price",
            { } value when string.Equals(value, ProductSortFields.TaxRate, StringComparison.OrdinalIgnoreCase) => "p.tax_rate",
            { } value when string.Equals(value, ProductSortFields.MinStockAlert, StringComparison.OrdinalIgnoreCase) => "p.min_stock_alert",
            { } value when string.Equals(value, ProductSortFields.IsActive, StringComparison.OrdinalIgnoreCase) => "p.is_active",
            _ => "p.name"
        };
    }

    private static string BuildInventoryWhereClause(InventoryListQuery query, InventoryScope scope)
    {
        var conditions = new List<string>();

        switch (scope.Kind)
        {
            case InventoryScopeKind.Company:
                break;
            case InventoryScopeKind.Market:
                conditions.Add("i.market_id = @scope_market_id");
                break;
            case InventoryScopeKind.Department:
                conditions.Add("i.market_id = @scope_market_id");
                conditions.Add("i.department_id = @scope_department_id");
                break;
            default:
                conditions.Add("FALSE");
                break;
        }

        if (!string.IsNullOrWhiteSpace(query.Search))
        {
            conditions.Add("(p.name ILIKE @search ESCAPE '\\' OR p.barcode ILIKE @search ESCAPE '\\')");
        }

        if (query.ProductId.HasValue)
        {
            conditions.Add("i.product_id = @product_id");
        }

        if (!string.IsNullOrWhiteSpace(query.Barcode))
        {
            conditions.Add("p.barcode ILIKE @barcode ESCAPE '\\'");
        }

        if (query.CategoryId.HasValue)
        {
            conditions.Add("p.category_id = @category_id");
        }

        if (query.MarketId.HasValue)
        {
            conditions.Add("i.market_id = @market_id");
        }

        if (query.DepartmentId.HasValue)
        {
            conditions.Add("i.department_id = @department_id");
        }

        if (query.LowStockOnly)
        {
            conditions.Add("i.quantity <= p.min_stock_alert");
        }

        return conditions.Count == 0
            ? string.Empty
            : $"WHERE {string.Join(" AND ", conditions)}";
    }

    private static void AddInventoryListParameters(NpgsqlCommand command, InventoryListQuery query)
    {
        if (!string.IsNullOrWhiteSpace(query.Search))
        {
            command.Parameters.AddWithValue("search", LikePattern(query.Search));
        }

        if (query.ProductId.HasValue)
        {
            command.Parameters.AddWithValue("product_id", query.ProductId.Value);
        }

        if (!string.IsNullOrWhiteSpace(query.Barcode))
        {
            command.Parameters.AddWithValue("barcode", LikePattern(query.Barcode));
        }

        if (query.CategoryId.HasValue)
        {
            command.Parameters.AddWithValue("category_id", query.CategoryId.Value);
        }

        if (query.MarketId.HasValue)
        {
            command.Parameters.AddWithValue("market_id", query.MarketId.Value);
        }

        if (query.DepartmentId.HasValue)
        {
            command.Parameters.AddWithValue("department_id", query.DepartmentId.Value);
        }
    }

    private static bool IsInventoryBarcodeLookup(InventoryListQuery query)
    {
        return !string.IsNullOrWhiteSpace(query.Barcode) &&
            string.IsNullOrWhiteSpace(query.Search) &&
            !query.ProductId.HasValue &&
            !query.CategoryId.HasValue &&
            !query.MarketId.HasValue &&
            !query.DepartmentId.HasValue &&
            !query.LowStockOnly &&
            query.Page == 1;
    }

    private static string BuildInventoryMovementWhereClause(
        InventoryMovementListQuery query,
        InventoryScope scope,
        int? inventoryId)
    {
        var conditions = new List<string>();

        switch (scope.Kind)
        {
            case InventoryScopeKind.Company:
                break;
            case InventoryScopeKind.Market:
                conditions.Add("i.market_id = @scope_market_id");
                break;
            case InventoryScopeKind.Department:
                conditions.Add("i.market_id = @scope_market_id");
                conditions.Add("i.department_id = @scope_department_id");
                break;
            default:
                conditions.Add("FALSE");
                break;
        }

        if (inventoryId.HasValue)
        {
            conditions.Add("im.inventory_id = @inventory_id");
        }

        if (query.ProductId.HasValue)
        {
            conditions.Add("i.product_id = @product_id");
        }

        if (query.MarketId.HasValue)
        {
            conditions.Add("i.market_id = @market_id");
        }

        if (query.DepartmentId.HasValue)
        {
            conditions.Add("i.department_id = @department_id");
        }

        if (!string.IsNullOrWhiteSpace(query.MovementType))
        {
            conditions.Add("im.movement_type = @movement_type");
        }

        if (query.DateFrom.HasValue)
        {
            conditions.Add("im.created_at >= @date_from");
        }

        if (query.DateTo.HasValue)
        {
            conditions.Add("im.created_at <= @date_to");
        }

        return conditions.Count == 0
            ? string.Empty
            : $"WHERE {string.Join(" AND ", conditions)}";
    }

    private static void AddInventoryMovementListParameters(
        NpgsqlCommand command,
        InventoryMovementListQuery query,
        int? inventoryId)
    {
        if (inventoryId.HasValue)
        {
            command.Parameters.AddWithValue("inventory_id", inventoryId.Value);
        }

        if (query.ProductId.HasValue)
        {
            command.Parameters.AddWithValue("product_id", query.ProductId.Value);
        }

        if (query.MarketId.HasValue)
        {
            command.Parameters.AddWithValue("market_id", query.MarketId.Value);
        }

        if (query.DepartmentId.HasValue)
        {
            command.Parameters.AddWithValue("department_id", query.DepartmentId.Value);
        }

        if (!string.IsNullOrWhiteSpace(query.MovementType))
        {
            command.Parameters.AddWithValue("movement_type", query.MovementType.Trim());
        }

        if (query.DateFrom.HasValue)
        {
            command.Parameters.AddWithValue("date_from", query.DateFrom.Value);
        }

        if (query.DateTo.HasValue)
        {
            command.Parameters.AddWithValue("date_to", query.DateTo.Value);
        }
    }

    private static string GetInventorySortColumn(string? sortBy)
    {
        return sortBy?.Trim() switch
        {
            { } value when string.Equals(value, InventorySortFields.Barcode, StringComparison.OrdinalIgnoreCase) => "p.barcode",
            { } value when string.Equals(value, InventorySortFields.MarketName, StringComparison.OrdinalIgnoreCase) => "m.name",
            { } value when string.Equals(value, InventorySortFields.DepartmentName, StringComparison.OrdinalIgnoreCase) => "d.name",
            { } value when string.Equals(value, InventorySortFields.Quantity, StringComparison.OrdinalIgnoreCase) => "i.quantity",
            { } value when string.Equals(value, InventorySortFields.AvailableQuantity, StringComparison.OrdinalIgnoreCase) => "available_quantity",
            { } value when string.Equals(value, InventorySortFields.UpdatedAt, StringComparison.OrdinalIgnoreCase) => "i.updated_at",
            _ => "p.name"
        };
    }

    private static string LikePattern(string value)
    {
        var escaped = value.Trim()
            .Replace(@"\", @"\\", StringComparison.Ordinal)
            .Replace("%", @"\%", StringComparison.Ordinal)
            .Replace("_", @"\_", StringComparison.Ordinal);

        return $"%{escaped}%";
    }

    private static InventoryItemDto ReadInventoryItem(NpgsqlDataReader reader)
    {
        return new InventoryItemDto
        {
            Id = reader.GetInt32(0),
            ProductId = reader.GetInt32(1),
            ProductName = reader.GetString(2),
            Barcode = reader.IsDBNull(3) ? null : reader.GetString(3),
            CategoryId = reader.IsDBNull(4) ? null : reader.GetInt32(4),
            CategoryName = reader.IsDBNull(5) ? null : reader.GetString(5),
            UnitPrice = reader.GetDecimal(6),
            MinStockAlert = reader.GetInt32(7),
            SuggestedRestockQuantity = reader.GetInt32(8),
            MarketId = reader.GetInt32(9),
            MarketName = reader.GetString(10),
            DepartmentId = reader.IsDBNull(11) ? null : reader.GetInt32(11),
            DepartmentName = reader.IsDBNull(12) ? null : reader.GetString(12),
            Quantity = reader.GetInt32(13),
            ReservedQuantity = reader.GetInt32(14),
            AvailableQuantity = reader.GetInt32(15),
            IsLowStock = reader.GetBoolean(16),
            UpdatedAt = reader.GetFieldValue<DateTimeOffset>(17),
            LastUpdatedBy = reader.IsDBNull(18) ? null : reader.GetInt32(18)
        };
    }

    private static ProductDto ReadProduct(NpgsqlDataReader reader)
    {
        return new ProductDto
        {
            Id = reader.GetInt32(0),
            Name = reader.GetString(1),
            Description = reader.IsDBNull(2) ? null : reader.GetString(2),
            Barcode = reader.IsDBNull(3) ? null : reader.GetString(3),
            CategoryId = reader.IsDBNull(4) ? null : reader.GetInt32(4),
            CategoryName = reader.IsDBNull(5) ? null : reader.GetString(5),
            UnitPrice = reader.GetDecimal(6),
            CostPrice = reader.GetDecimal(7),
            TaxRate = reader.GetDecimal(8),
            MinStockAlert = reader.GetInt32(9),
            IsActive = reader.GetBoolean(10)
        };
    }

    private static MarketDto ReadMarket(NpgsqlDataReader reader)
    {
        return new MarketDto
        {
            Id = reader.GetInt32(0),
            Name = reader.GetString(1),
            City = reader.IsDBNull(2) ? null : reader.GetString(2),
            Address = reader.IsDBNull(3) ? null : reader.GetString(3),
            IsActive = reader.GetBoolean(4)
        };
    }

    private static DepartmentDto ReadDepartment(NpgsqlDataReader reader)
    {
        return new DepartmentDto
        {
            Id = reader.GetInt32(0),
            MarketId = reader.GetInt32(1),
            Name = reader.GetString(2),
            Description = reader.IsDBNull(3) ? null : reader.GetString(3),
            IsActive = reader.GetBoolean(4)
        };
    }

    private static SupplierDto ReadSupplier(NpgsqlDataReader reader)
    {
        return new SupplierDto
        {
            Id = reader.GetInt32(0),
            Name = reader.GetString(1),
            Phone = reader.IsDBNull(2) ? null : reader.GetString(2),
            Email = reader.IsDBNull(3) ? null : reader.GetString(3),
            Address = reader.IsDBNull(4) ? null : reader.GetString(4),
            IsActive = reader.GetBoolean(5),
            CreatedAt = reader.GetFieldValue<DateTimeOffset>(6)
        };
    }

    private static async Task<SaleDto?> ReadSaleAsync(
        NpgsqlCommand command,
        CancellationToken cancellationToken)
    {
        await using var reader = await command.ExecuteReaderAsync(cancellationToken);

        return await reader.ReadAsync(cancellationToken)
            ? new SaleDto
            {
                Id = reader.GetInt32(0),
                ReferenceNumber = reader.GetString(1),
                MarketId = reader.GetInt32(2),
                SaleDate = reader.GetFieldValue<DateOnly>(3),
                PaymentMethod = reader.GetString(4),
                TotalAmount = reader.GetDecimal(5)
            }
            : null;
    }

    private async Task<PurchaseDto?> GetPurchaseWithItemsAsync(
        string quotedSchemaName,
        int id,
        CancellationToken cancellationToken)
    {
        await EnsurePurchaseExpectedDateColumnAsync(quotedSchemaName, cancellationToken);

        await using var command = await CreateCommandAsync($"""
            SELECT p.id,
                   p.supplier_id,
                   COALESCE(s.name, '') AS supplier_name,
                   p.market_id,
                   COALESCE(m.name, '') AS market_name,
                   p.purchase_date,
                   p.expected_date,
                   p.status,
                   p.total_amount,
                   p.notes,
                   COUNT(pi.id)::int AS item_count,
                   COALESCE(SUM(pi.quantity), 0)::int AS total_quantity,
                   COALESCE(SUM(pi.received_quantity), 0)::int AS received_quantity
            FROM {quotedSchemaName}.purchases p
            LEFT JOIN {quotedSchemaName}.suppliers s ON s.id = p.supplier_id
            LEFT JOIN {quotedSchemaName}.markets m ON m.id = p.market_id
            LEFT JOIN {quotedSchemaName}.purchase_items pi ON pi.purchase_id = p.id
            WHERE p.id = @id
            GROUP BY p.id, p.supplier_id, s.name, p.market_id, m.name, p.purchase_date, p.expected_date, p.status, p.total_amount, p.notes;
            """, cancellationToken);
        command.Parameters.AddWithValue("id", id);

        await using var reader = await command.ExecuteReaderAsync(cancellationToken);

        if (!await reader.ReadAsync(cancellationToken))
        {
            return null;
        }

        var purchase = ReadPurchaseSummary(reader);
        await reader.DisposeAsync();

        var items = new List<PurchaseItemDto>();
        await using var itemsCommand = await CreateCommandAsync($"""
            SELECT pi.id,
                   pi.product_id,
                   COALESCE(p.name, '') AS product_name,
                   pi.quantity,
                   pi.received_quantity,
                   pi.unit_cost,
                   pi.line_total
            FROM {quotedSchemaName}.purchase_items pi
            LEFT JOIN {quotedSchemaName}.products p ON p.id = pi.product_id
            WHERE pi.purchase_id = @purchase_id
            ORDER BY pi.id;
            """, cancellationToken);
        itemsCommand.Parameters.AddWithValue("purchase_id", id);

        await using var itemsReader = await itemsCommand.ExecuteReaderAsync(cancellationToken);

        while (await itemsReader.ReadAsync(cancellationToken))
        {
            items.Add(new PurchaseItemDto
            {
                Id = itemsReader.GetInt32(0),
                ProductId = itemsReader.GetInt32(1),
                ProductName = itemsReader.GetString(2),
                Quantity = itemsReader.GetInt32(3),
                ReceivedQuantity = itemsReader.GetInt32(4),
                UnitCost = itemsReader.GetDecimal(5),
                LineTotal = itemsReader.GetDecimal(6)
            });
        }

        purchase.Items = items;
        return purchase;
    }

    private async Task EnsurePurchaseExpectedDateColumnAsync(
        string quotedSchemaName,
        CancellationToken cancellationToken)
    {
        await using var command = await CreateCommandAsync($"""
            ALTER TABLE {quotedSchemaName}.purchases
                ADD COLUMN IF NOT EXISTS expected_date DATE;
            """, cancellationToken);

        await command.ExecuteNonQueryAsync(cancellationToken);
    }

    private static PurchaseDto ReadPurchaseSummary(NpgsqlDataReader reader)
    {
        return new PurchaseDto
        {
            Id = reader.GetInt32(0),
            SupplierId = reader.GetInt32(1),
            SupplierName = reader.GetString(2),
            MarketId = reader.GetInt32(3),
            MarketName = reader.GetString(4),
            PurchaseDate = reader.GetFieldValue<DateOnly>(5),
            ExpectedDate = reader.IsDBNull(6) ? null : reader.GetFieldValue<DateOnly>(6),
            Status = reader.GetString(7),
            TotalAmount = reader.GetDecimal(8),
            Notes = reader.IsDBNull(9) ? null : reader.GetString(9),
            ItemCount = reader.GetInt32(10),
            TotalQuantity = reader.GetInt32(11),
            ReceivedQuantity = reader.GetInt32(12)
        };
    }

    private static async Task<PurchaseDto?> ReadPurchaseAsync(
        NpgsqlCommand command,
        CancellationToken cancellationToken)
    {
        await using var reader = await command.ExecuteReaderAsync(cancellationToken);

        return await reader.ReadAsync(cancellationToken)
            ? new PurchaseDto
            {
                Id = reader.GetInt32(0),
                SupplierId = reader.GetInt32(1),
                MarketId = reader.GetInt32(2),
                PurchaseDate = reader.GetFieldValue<DateOnly>(3),
                ExpectedDate = reader.IsDBNull(4) ? null : reader.GetFieldValue<DateOnly>(4),
                Status = reader.GetString(5),
                TotalAmount = reader.GetDecimal(6)
            }
            : null;
    }

    private static void AddProductParameters(NpgsqlCommand command, CreateProductRequest request)
    {
        command.Parameters.AddWithValue("name", request.Name.Trim());
        command.Parameters.AddWithValue("description", DbValue(request.Description));
        command.Parameters.AddWithValue("barcode", DbValue(request.Barcode));
        command.Parameters.AddWithValue("category_id", DbValue(request.CategoryId));
        command.Parameters.AddWithValue("unit_price", request.UnitPrice);
        command.Parameters.AddWithValue("cost_price", request.CostPrice);
        command.Parameters.AddWithValue("tax_rate", request.TaxRate);
        command.Parameters.AddWithValue("image_url", DbValue(request.ImageUrl));
        command.Parameters.AddWithValue("min_stock_alert", request.MinStockAlert);
    }

    private static void AddProductParameters(NpgsqlCommand command, UpdateProductRequest request)
    {
        command.Parameters.AddWithValue("name", request.Name.Trim());
        command.Parameters.AddWithValue("description", DbValue(request.Description));
        command.Parameters.AddWithValue("barcode", DbValue(request.Barcode));
        command.Parameters.AddWithValue("category_id", DbValue(request.CategoryId));
        command.Parameters.AddWithValue("unit_price", request.UnitPrice);
        command.Parameters.AddWithValue("cost_price", request.CostPrice);
        command.Parameters.AddWithValue("tax_rate", request.TaxRate);
        command.Parameters.AddWithValue("image_url", DbValue(request.ImageUrl));
        command.Parameters.AddWithValue("min_stock_alert", request.MinStockAlert);
    }

    private static void AddMarketParameters(NpgsqlCommand command, CreateMarketRequest request)
    {
        command.Parameters.AddWithValue("name", request.Name.Trim());
        command.Parameters.AddWithValue("city", DbValue(request.City));
        command.Parameters.AddWithValue("address", DbValue(request.Address));
    }

    private static void AddMarketParameters(NpgsqlCommand command, UpdateMarketRequest request)
    {
        command.Parameters.AddWithValue("name", request.Name.Trim());
        command.Parameters.AddWithValue("city", DbValue(request.City));
        command.Parameters.AddWithValue("address", DbValue(request.Address));
    }

    private static void AddDepartmentParameters(NpgsqlCommand command, CreateDepartmentRequest request)
    {
        command.Parameters.AddWithValue("market_id", request.MarketId);
        command.Parameters.AddWithValue("name", request.Name.Trim());
        command.Parameters.AddWithValue("description", DbValue(request.Description));
    }

    private static void AddDepartmentParameters(NpgsqlCommand command, UpdateDepartmentRequest request)
    {
        command.Parameters.AddWithValue("name", request.Name.Trim());
        command.Parameters.AddWithValue("description", DbValue(request.Description));
    }

    private static void AddSupplierParameters(
        NpgsqlCommand command,
        string name,
        string? phone,
        string? email,
        string? address)
    {
        command.Parameters.AddWithValue("name", name.Trim());
        command.Parameters.AddWithValue("phone", DbValue(phone));
        command.Parameters.AddWithValue("email", DbValue(email));
        command.Parameters.AddWithValue("address", DbValue(address));
    }

    private static object DbValue<T>(T? value)
    {
        return value is null ? DBNull.Value : value;
    }

    private static string NormalizePurchaseStatus(string status)
    {
        var trimmed = status.Trim();
        return string.Equals(trimmed, "Pending", StringComparison.OrdinalIgnoreCase)
            ? "Ordered"
            : trimmed;
    }

    private static bool CanUpdatePurchase(string currentStatus)
    {
        return string.Equals(currentStatus, "Draft", StringComparison.OrdinalIgnoreCase) ||
            string.Equals(currentStatus, "Ordered", StringComparison.OrdinalIgnoreCase) ||
            string.Equals(currentStatus, "Pending", StringComparison.OrdinalIgnoreCase);
    }

    private static bool CanPatchPurchase(string currentStatus, string effectiveStatus)
    {
        if (string.Equals(currentStatus, "PartiallyReceived", StringComparison.OrdinalIgnoreCase))
        {
            return false;
        }

        if (string.Equals(currentStatus, "Cancelled", StringComparison.OrdinalIgnoreCase) ||
            string.Equals(currentStatus, "Received", StringComparison.OrdinalIgnoreCase))
        {
            return string.Equals(currentStatus, effectiveStatus, StringComparison.OrdinalIgnoreCase);
        }

        return true;
    }

    private static bool IsUniqueViolation(PostgresException exception)
    {
        return exception.SqlState == PostgresErrorCodes.UniqueViolation;
    }

    private static bool IsForeignKeyViolation(PostgresException exception)
    {
        return exception.SqlState == PostgresErrorCodes.ForeignKeyViolation;
    }

    private async Task<NpgsqlCommand> CreateCommandAsync(
        string commandText,
        CancellationToken cancellationToken,
        NpgsqlTransaction? transaction = null)
    {
        var connection = (NpgsqlConnection)_dbContext.Database.GetDbConnection();

        if (connection.State != ConnectionState.Open)
        {
            await connection.OpenAsync(cancellationToken);
        }

        return new NpgsqlCommand(commandText, connection, transaction);
    }

    private async Task<NpgsqlTransaction> BeginTransactionAsync(CancellationToken cancellationToken)
    {
        var connection = (NpgsqlConnection)_dbContext.Database.GetDbConnection();

        if (connection.State != ConnectionState.Open)
        {
            await connection.OpenAsync(cancellationToken);
        }

        return await connection.BeginTransactionAsync(cancellationToken);
    }

    private async Task<string> GetQuotedCurrentSchemaNameAsync(CancellationToken cancellationToken)
    {
        return QuoteIdentifier(await _tenantProvider.GetCurrentSchemaNameAsync(cancellationToken));
    }

    private static string QuoteIdentifier(string identifier)
    {
        return "\"" + identifier.Replace("\"", "\"\"", StringComparison.Ordinal) + "\"";
    }

    private sealed record StaffAssignmentScope(int MarketId, int? DepartmentId);

    private sealed record PosInventoryTarget(int MarketId, int? DepartmentId = null);

    private sealed record PurchaseReceiptState(string Status, int MarketId);

    private sealed record PurchaseStockItem(int ProductId, int Quantity);

    private sealed record PurchaseReceivingItem(int Id, int ProductId, int Quantity, int ReceivedQuantity);

    private sealed class BarcodeLookupCacheEntry
    {
        public Dictionary<string, PagedResult<ProductDto>> ProductLookups { get; set; } = [];

        public Dictionary<string, PagedResult<InventoryItemDto>> InventoryLookups { get; set; } = [];
    }

    private sealed record InventoryScope(
        InventoryScopeKind Kind,
        int? MarketId = null,
        int? DepartmentId = null)
    {
        public static InventoryScope None { get; } = new(InventoryScopeKind.None);

        public static InventoryScope Company { get; } = new(InventoryScopeKind.Company);

        public static InventoryScope Market(int marketId)
        {
            return new InventoryScope(InventoryScopeKind.Market, marketId);
        }

        public static InventoryScope Department(int marketId, int departmentId)
        {
            return new InventoryScope(InventoryScopeKind.Department, marketId, departmentId);
        }
    }

    private enum InventoryScopeKind
    {
        None,
        Company,
        Market,
        Department
    }
}
