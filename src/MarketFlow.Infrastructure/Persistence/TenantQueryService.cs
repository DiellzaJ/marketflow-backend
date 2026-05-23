using System.Data;
using MarketFlow.Application.Common.Interfaces;
using MarketFlow.Application.Common.Models;
using MarketFlow.Application.Features.Categories.DTOs;
using MarketFlow.Application.Features.Departments.DTOs;
using MarketFlow.Application.Features.Departments.Interfaces;
using MarketFlow.Application.Features.Inventory.Configuration;
using MarketFlow.Application.Features.Inventory.DTOs;
using MarketFlow.Application.Features.Markets.DTOs;
using MarketFlow.Application.Features.Markets.Interfaces;
using MarketFlow.Application.Features.Products.Configuration;
using MarketFlow.Application.Features.Products.DTOs;
using MarketFlow.Application.Features.Products.Exceptions;
using MarketFlow.Application.Features.Purchases.DTOs;
using MarketFlow.Application.Features.Sales.DTOs;
using MarketFlow.Infrastructure.Caching;
using MarketFlow.Application.Features.Users.Configuration;
using MarketFlow.Infrastructure.MultiTenancy;
using Microsoft.Extensions.Configuration;
using Microsoft.EntityFrameworkCore;
using Npgsql;

namespace MarketFlow.Infrastructure.Persistence;

public sealed class TenantQueryService : ITenantQueryService, IMarketQueryService, IDepartmentQueryService
{
    private const int DefaultBarcodeLookupCacheTtlSeconds = 300;

    private readonly ApplicationDbContext _dbContext;
    private readonly TenantProvider _tenantProvider;
    private readonly ICurrentUserService _currentUserService;
    private readonly RedisCacheService? _cacheService;
    private readonly TimeSpan _barcodeLookupCacheTtl;

    public TenantQueryService(
        ApplicationDbContext dbContext,
        TenantProvider tenantProvider,
        ICurrentUserService currentUserService,
        RedisCacheService? cacheService = null,
        IConfiguration? configuration = null)
    {
        _dbContext = dbContext;
        _tenantProvider = tenantProvider;
        _currentUserService = currentUserService;
        _cacheService = cacheService;
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

        await using var command = await CreateCommandAsync($"""
            SELECT EXISTS (
                SELECT 1
                FROM {schemaName}.products
                WHERE barcode = @barcode
                  AND (@excluded_product_id IS NULL OR id <> @excluded_product_id)
            );
            """, cancellationToken);
        command.Parameters.AddWithValue("barcode", barcode.Trim());
        command.Parameters.AddWithValue("excluded_product_id", DbValue(excludedProductId));

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
        var sales = new List<SaleDto>();

        await using var command = await CreateCommandAsync($"""
            SELECT id, market_id, sale_date, payment_method, total_amount
            FROM {schemaName}.sales
            ORDER BY sale_date DESC, id DESC;
            """, cancellationToken);

        await using var reader = await command.ExecuteReaderAsync(cancellationToken);

        while (await reader.ReadAsync(cancellationToken))
        {
            sales.Add(new SaleDto
            {
                Id = reader.GetInt32(0),
                MarketId = reader.GetInt32(1),
                SaleDate = reader.GetFieldValue<DateOnly>(2),
                PaymentMethod = reader.GetString(3),
                TotalAmount = reader.GetDecimal(4)
            });
        }

        return sales;
    }

    public async Task<SaleDto?> CreateSaleAsync(
        CreateSaleRequest request,
        int createdByUserId,
        CancellationToken cancellationToken = default)
    {
        var schemaName = await GetQuotedCurrentSchemaNameAsync(cancellationToken);
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
                created_by_user_id,
                sale_date,
                payment_method,
                discount_amount,
                total_amount,
                notes)
            VALUES (
                @market_id,
                @created_by_user_id,
                @sale_date,
                @payment_method,
                @discount_amount,
                @total_amount,
                @notes)
            RETURNING id, market_id, sale_date, payment_method, total_amount;
            """, cancellationToken, transaction);

        command.Parameters.AddWithValue("market_id", request.MarketId);
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

        await using var command = await CreateCommandAsync($"""
            UPDATE {schemaName}.sales
            SET market_id = @market_id,
                sale_date = @sale_date,
                payment_method = @payment_method,
                discount_amount = @discount_amount,
                total_amount = @total_amount,
                notes = @notes
            WHERE id = @id
            RETURNING id, market_id, sale_date, payment_method, total_amount;
            """, cancellationToken);

        command.Parameters.AddWithValue("id", id);
        command.Parameters.AddWithValue("market_id", request.MarketId);
        command.Parameters.AddWithValue("sale_date", request.SaleDate);
        command.Parameters.AddWithValue("payment_method", request.PaymentMethod.Trim());
        command.Parameters.AddWithValue("discount_amount", request.DiscountAmount);
        command.Parameters.AddWithValue("total_amount", request.TotalAmount);
        command.Parameters.AddWithValue("notes", DbValue(request.Notes));

        return await ReadSaleAsync(command, cancellationToken);
    }

    public async Task<SaleDto?> PatchSaleAsync(
        int id,
        PatchSaleRequest request,
        CancellationToken cancellationToken = default)
    {
        var schemaName = await GetQuotedCurrentSchemaNameAsync(cancellationToken);

        await using var command = await CreateCommandAsync($"""
            UPDATE {schemaName}.sales
            SET market_id = COALESCE(@market_id, market_id),
                sale_date = COALESCE(@sale_date, sale_date),
                payment_method = COALESCE(@payment_method, payment_method),
                discount_amount = COALESCE(@discount_amount, discount_amount),
                total_amount = COALESCE(@total_amount, total_amount),
                notes = COALESCE(@notes, notes)
            WHERE id = @id
            RETURNING id, market_id, sale_date, payment_method, total_amount;
            """, cancellationToken);

        command.Parameters.AddWithValue("id", id);
        command.Parameters.AddWithValue("market_id", DbValue(request.MarketId));
        command.Parameters.AddWithValue("sale_date", DbValue(request.SaleDate));
        command.Parameters.AddWithValue("payment_method", DbValue(request.PaymentMethod?.Trim()));
        command.Parameters.AddWithValue("discount_amount", DbValue(request.DiscountAmount));
        command.Parameters.AddWithValue("total_amount", DbValue(request.TotalAmount));
        command.Parameters.AddWithValue("notes", DbValue(request.Notes));

        return await ReadSaleAsync(command, cancellationToken);
    }

    public async Task<bool> DeleteSaleAsync(
        int id,
        CancellationToken cancellationToken = default)
    {
        var schemaName = await GetQuotedCurrentSchemaNameAsync(cancellationToken);

        await using var command = await CreateCommandAsync($"""
            DELETE FROM {schemaName}.sales
            WHERE id = @id;
            """, cancellationToken);
        command.Parameters.AddWithValue("id", id);

        return await command.ExecuteNonQueryAsync(cancellationToken) > 0;
    }

    public async Task<IReadOnlyCollection<PurchaseDto>> GetPurchasesAsync(
        CancellationToken cancellationToken = default)
    {
        var schemaName = await GetQuotedCurrentSchemaNameAsync(cancellationToken);
        var purchases = new List<PurchaseDto>();

        await using var command = await CreateCommandAsync($"""
            SELECT id, supplier_id, market_id, purchase_date, status, total_amount
            FROM {schemaName}.purchases
            ORDER BY purchase_date DESC, id DESC;
            """, cancellationToken);

        await using var reader = await command.ExecuteReaderAsync(cancellationToken);

        while (await reader.ReadAsync(cancellationToken))
        {
            purchases.Add(new PurchaseDto
            {
                Id = reader.GetInt32(0),
                SupplierId = reader.GetInt32(1),
                MarketId = reader.GetInt32(2),
                PurchaseDate = reader.GetFieldValue<DateOnly>(3),
                Status = reader.GetString(4),
                TotalAmount = reader.GetDecimal(5)
            });
        }

        return purchases;
    }

    public async Task<PurchaseDto?> CreatePurchaseAsync(
        CreatePurchaseRequest request,
        int createdByUserId,
        CancellationToken cancellationToken = default)
    {
        var schemaName = await GetQuotedCurrentSchemaNameAsync(cancellationToken);
        var receivesPurchase = string.Equals(request.Status, "Received", StringComparison.OrdinalIgnoreCase);

        if (receivesPurchase)
        {
            var scope = await GetCurrentInventoryScopeAsync(schemaName, cancellationToken);

            if (!CanAccessInventoryTarget(scope, request.MarketId, departmentId: null))
            {
                return null;
            }
        }

        await using var transaction = await BeginTransactionAsync(cancellationToken);
        await using var command = await CreateCommandAsync($"""
            INSERT INTO {schemaName}.purchases (
                supplier_id,
                market_id,
                created_by_user_id,
                purchase_date,
                status,
                total_amount,
                notes)
            VALUES (
                @supplier_id,
                @market_id,
                @created_by_user_id,
                @purchase_date,
                @status,
                @total_amount,
                @notes)
            RETURNING id, supplier_id, market_id, purchase_date, status, total_amount;
            """, cancellationToken, transaction);

        command.Parameters.AddWithValue("supplier_id", request.SupplierId);
        command.Parameters.AddWithValue("market_id", request.MarketId);
        command.Parameters.AddWithValue("created_by_user_id", createdByUserId);
        command.Parameters.AddWithValue("purchase_date", request.PurchaseDate ?? DateOnly.FromDateTime(DateTime.UtcNow));
        command.Parameters.AddWithValue("status", request.Status.Trim());
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

        return purchase;
    }

    public async Task<PurchaseDto?> UpdatePurchaseAsync(
        int id,
        UpdatePurchaseRequest request,
        int? updatedByUserId,
        CancellationToken cancellationToken = default)
    {
        var schemaName = await GetQuotedCurrentSchemaNameAsync(cancellationToken);

        await using var transaction = await BeginTransactionAsync(cancellationToken);
        var currentPurchase = await GetPurchaseReceiptStateForUpdateAsync(schemaName, id, cancellationToken, transaction);

        if (currentPurchase is null)
        {
            return null;
        }

        if (ShouldReceivePurchase(currentPurchase.Status, request.Status, request.MarketId))
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
                status = @status,
                total_amount = @total_amount,
                notes = @notes
            WHERE id = @id
            RETURNING id, supplier_id, market_id, purchase_date, status, total_amount;
            """, cancellationToken, transaction);

        command.Parameters.AddWithValue("id", id);
        command.Parameters.AddWithValue("supplier_id", request.SupplierId);
        command.Parameters.AddWithValue("market_id", request.MarketId);
        command.Parameters.AddWithValue("purchase_date", request.PurchaseDate);
        command.Parameters.AddWithValue("status", request.Status.Trim());
        command.Parameters.AddWithValue("total_amount", request.TotalAmount);
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

        return purchase;
    }

    public async Task<PurchaseDto?> PatchPurchaseAsync(
        int id,
        PatchPurchaseRequest request,
        int? updatedByUserId,
        CancellationToken cancellationToken = default)
    {
        var schemaName = await GetQuotedCurrentSchemaNameAsync(cancellationToken);

        await using var transaction = await BeginTransactionAsync(cancellationToken);
        var currentPurchase = await GetPurchaseReceiptStateForUpdateAsync(schemaName, id, cancellationToken, transaction);

        if (currentPurchase is null)
        {
            return null;
        }

        var effectiveStatus = request.Status?.Trim() ?? currentPurchase.Status;
        var effectiveMarketId = request.MarketId ?? currentPurchase.MarketId;

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
                status = COALESCE(@status, status),
                total_amount = COALESCE(@total_amount, total_amount),
                notes = COALESCE(@notes, notes)
            WHERE id = @id
            RETURNING id, supplier_id, market_id, purchase_date, status, total_amount;
            """, cancellationToken, transaction);

        command.Parameters.AddWithValue("id", id);
        command.Parameters.AddWithValue("supplier_id", DbValue(request.SupplierId));
        command.Parameters.AddWithValue("market_id", DbValue(request.MarketId));
        command.Parameters.AddWithValue("purchase_date", DbValue(request.PurchaseDate));
        command.Parameters.AddWithValue("status", DbValue(request.Status?.Trim()));
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

        return purchase;
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
              AND quantity + @quantity_change >= 0
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

    private static async Task<SaleDto?> ReadSaleAsync(
        NpgsqlCommand command,
        CancellationToken cancellationToken)
    {
        await using var reader = await command.ExecuteReaderAsync(cancellationToken);

        return await reader.ReadAsync(cancellationToken)
            ? new SaleDto
            {
                Id = reader.GetInt32(0),
                MarketId = reader.GetInt32(1),
                SaleDate = reader.GetFieldValue<DateOnly>(2),
                PaymentMethod = reader.GetString(3),
                TotalAmount = reader.GetDecimal(4)
            }
            : null;
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
                Status = reader.GetString(4),
                TotalAmount = reader.GetDecimal(5)
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

    private static object DbValue<T>(T? value)
    {
        return value is null ? DBNull.Value : value;
    }

    private static bool IsUniqueViolation(PostgresException exception)
    {
        return exception.SqlState == PostgresErrorCodes.UniqueViolation;
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

    private sealed record PurchaseReceiptState(string Status, int MarketId);

    private sealed record PurchaseStockItem(int ProductId, int Quantity);

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
