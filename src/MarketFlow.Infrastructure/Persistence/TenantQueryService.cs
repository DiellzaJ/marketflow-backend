using System.Data;
using MarketFlow.Application.Common.Interfaces;
using MarketFlow.Application.Features.Inventory.DTOs;
using MarketFlow.Application.Features.Products.DTOs;
using MarketFlow.Application.Features.Purchases.DTOs;
using MarketFlow.Application.Features.Sales.DTOs;
using MarketFlow.Infrastructure.MultiTenancy;
using Microsoft.EntityFrameworkCore;
using Npgsql;

namespace MarketFlow.Infrastructure.Persistence;

public sealed class TenantQueryService : ITenantQueryService
{
    private readonly ApplicationDbContext _dbContext;
    private readonly TenantProvider _tenantProvider;

    public TenantQueryService(
        ApplicationDbContext dbContext,
        TenantProvider tenantProvider)
    {
        _dbContext = dbContext;
        _tenantProvider = tenantProvider;
    }

    public async Task<IReadOnlyCollection<ProductDto>> GetProductsAsync(
        CancellationToken cancellationToken = default)
    {
        var schemaName = QuoteIdentifier(_tenantProvider.GetCurrentSchemaName());
        var products = new List<ProductDto>();

        await using var command = await CreateCommandAsync($"""
            SELECT id, name, description, barcode, unit_price
            FROM {schemaName}.products
            WHERE is_active = TRUE
            ORDER BY name;
            """, cancellationToken);

        await using var reader = await command.ExecuteReaderAsync(cancellationToken);

        while (await reader.ReadAsync(cancellationToken))
        {
            products.Add(new ProductDto
            {
                Id = reader.GetInt32(0),
                Name = reader.GetString(1),
                Description = reader.IsDBNull(2) ? null : reader.GetString(2),
                Barcode = reader.IsDBNull(3) ? null : reader.GetString(3),
                UnitPrice = reader.GetDecimal(4)
            });
        }

        return products;
    }

    public async Task<ProductDto?> GetProductAsync(
        int id,
        CancellationToken cancellationToken = default)
    {
        var schemaName = QuoteIdentifier(_tenantProvider.GetCurrentSchemaName());

        await using var command = await CreateCommandAsync($"""
            SELECT id, name, description, barcode, unit_price
            FROM {schemaName}.products
            WHERE id = @id AND is_active = TRUE;
            """, cancellationToken);
        command.Parameters.AddWithValue("id", id);

        return await ReadProductAsync(command, cancellationToken);
    }

    public async Task<ProductDto> CreateProductAsync(
        CreateProductRequest request,
        CancellationToken cancellationToken = default)
    {
        var schemaName = QuoteIdentifier(_tenantProvider.GetCurrentSchemaName());

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
            RETURNING id, name, description, barcode, unit_price;
            """, cancellationToken);

        AddProductParameters(command, request);

        return await ReadProductAsync(command, cancellationToken)
            ?? throw new InvalidOperationException("Product was not created.");
    }

    public async Task<ProductDto?> UpdateProductAsync(
        int id,
        UpdateProductRequest request,
        CancellationToken cancellationToken = default)
    {
        var schemaName = QuoteIdentifier(_tenantProvider.GetCurrentSchemaName());

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
                min_stock_alert = @min_stock_alert,
                is_active = @is_active
            WHERE id = @id
            RETURNING id, name, description, barcode, unit_price;
            """, cancellationToken);

        command.Parameters.AddWithValue("id", id);
        AddProductParameters(command, request);
        command.Parameters.AddWithValue("is_active", request.IsActive);

        return await ReadProductAsync(command, cancellationToken);
    }

    public async Task<ProductDto?> PatchProductAsync(
        int id,
        PatchProductRequest request,
        CancellationToken cancellationToken = default)
    {
        var schemaName = QuoteIdentifier(_tenantProvider.GetCurrentSchemaName());

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
                min_stock_alert = COALESCE(@min_stock_alert, min_stock_alert),
                is_active = COALESCE(@is_active, is_active)
            WHERE id = @id
            RETURNING id, name, description, barcode, unit_price;
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
        command.Parameters.AddWithValue("is_active", DbValue(request.IsActive));

        return await ReadProductAsync(command, cancellationToken);
    }

    public async Task<bool> DeleteProductAsync(
        int id,
        CancellationToken cancellationToken = default)
    {
        var schemaName = QuoteIdentifier(_tenantProvider.GetCurrentSchemaName());

        await using var command = await CreateCommandAsync($"""
            UPDATE {schemaName}.products
            SET is_active = FALSE
            WHERE id = @id AND is_active = TRUE;
            """, cancellationToken);
        command.Parameters.AddWithValue("id", id);

        return await command.ExecuteNonQueryAsync(cancellationToken) > 0;
    }

    public async Task<IReadOnlyCollection<InventoryItemDto>> GetInventoryAsync(
        CancellationToken cancellationToken = default)
    {
        var schemaName = QuoteIdentifier(_tenantProvider.GetCurrentSchemaName());
        var inventory = new List<InventoryItemDto>();

        await using var command = await CreateCommandAsync($"""
            SELECT i.id,
                   i.product_id,
                   p.name,
                   i.market_id,
                   m.name,
                   i.quantity,
                   i.reserved_quantity
            FROM {schemaName}.inventory i
            INNER JOIN {schemaName}.products p ON p.id = i.product_id
            INNER JOIN {schemaName}.markets m ON m.id = i.market_id
            ORDER BY m.name, p.name;
            """, cancellationToken);

        await using var reader = await command.ExecuteReaderAsync(cancellationToken);

        while (await reader.ReadAsync(cancellationToken))
        {
            inventory.Add(new InventoryItemDto
            {
                Id = reader.GetInt32(0),
                ProductId = reader.GetInt32(1),
                ProductName = reader.GetString(2),
                MarketId = reader.GetInt32(3),
                MarketName = reader.GetString(4),
                Quantity = reader.GetInt32(5),
                ReservedQuantity = reader.GetInt32(6)
            });
        }

        return inventory;
    }

    public async Task<InventoryItemDto?> UpdateInventoryItemAsync(
        int id,
        UpdateInventoryItemRequest request,
        int? updatedByUserId,
        CancellationToken cancellationToken = default)
    {
        var schemaName = QuoteIdentifier(_tenantProvider.GetCurrentSchemaName());

        await using var command = await CreateCommandAsync($"""
            UPDATE {schemaName}.inventory
            SET quantity = @quantity,
                reserved_quantity = @reserved_quantity,
                last_updated_by = @last_updated_by,
                updated_at = NOW()
            WHERE id = @id
            RETURNING id;
            """, cancellationToken);

        command.Parameters.AddWithValue("id", id);
        command.Parameters.AddWithValue("quantity", request.Quantity);
        command.Parameters.AddWithValue("reserved_quantity", request.ReservedQuantity);
        command.Parameters.AddWithValue("last_updated_by", DbValue(updatedByUserId));

        var updatedId = await command.ExecuteScalarAsync(cancellationToken);

        return updatedId is null ? null : await GetInventoryItemAsync(id, cancellationToken);
    }

    public async Task<InventoryItemDto?> PatchInventoryItemAsync(
        int id,
        PatchInventoryItemRequest request,
        int? updatedByUserId,
        CancellationToken cancellationToken = default)
    {
        var schemaName = QuoteIdentifier(_tenantProvider.GetCurrentSchemaName());

        await using var command = await CreateCommandAsync($"""
            UPDATE {schemaName}.inventory
            SET quantity = COALESCE(@quantity, quantity),
                reserved_quantity = COALESCE(@reserved_quantity, reserved_quantity),
                last_updated_by = @last_updated_by,
                updated_at = NOW()
            WHERE id = @id
            RETURNING id;
            """, cancellationToken);

        command.Parameters.AddWithValue("id", id);
        command.Parameters.AddWithValue("quantity", DbValue(request.Quantity));
        command.Parameters.AddWithValue("reserved_quantity", DbValue(request.ReservedQuantity));
        command.Parameters.AddWithValue("last_updated_by", DbValue(updatedByUserId));

        var updatedId = await command.ExecuteScalarAsync(cancellationToken);

        return updatedId is null ? null : await GetInventoryItemAsync(id, cancellationToken);
    }

    public async Task<IReadOnlyCollection<SaleDto>> GetSalesAsync(
        CancellationToken cancellationToken = default)
    {
        var schemaName = QuoteIdentifier(_tenantProvider.GetCurrentSchemaName());
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

    public async Task<SaleDto> CreateSaleAsync(
        CreateSaleRequest request,
        int createdByUserId,
        CancellationToken cancellationToken = default)
    {
        var schemaName = QuoteIdentifier(_tenantProvider.GetCurrentSchemaName());

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
            """, cancellationToken);

        command.Parameters.AddWithValue("market_id", request.MarketId);
        command.Parameters.AddWithValue("created_by_user_id", createdByUserId);
        command.Parameters.AddWithValue("sale_date", request.SaleDate ?? DateOnly.FromDateTime(DateTime.UtcNow));
        command.Parameters.AddWithValue("payment_method", request.PaymentMethod.Trim());
        command.Parameters.AddWithValue("discount_amount", request.DiscountAmount);
        command.Parameters.AddWithValue("total_amount", request.TotalAmount);
        command.Parameters.AddWithValue("notes", DbValue(request.Notes));

        return await ReadSaleAsync(command, cancellationToken)
            ?? throw new InvalidOperationException("Sale was not created.");
    }

    public async Task<IReadOnlyCollection<PurchaseDto>> GetPurchasesAsync(
        CancellationToken cancellationToken = default)
    {
        var schemaName = QuoteIdentifier(_tenantProvider.GetCurrentSchemaName());
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

    public async Task<PurchaseDto> CreatePurchaseAsync(
        CreatePurchaseRequest request,
        int createdByUserId,
        CancellationToken cancellationToken = default)
    {
        var schemaName = QuoteIdentifier(_tenantProvider.GetCurrentSchemaName());

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
            """, cancellationToken);

        command.Parameters.AddWithValue("supplier_id", request.SupplierId);
        command.Parameters.AddWithValue("market_id", request.MarketId);
        command.Parameters.AddWithValue("created_by_user_id", createdByUserId);
        command.Parameters.AddWithValue("purchase_date", request.PurchaseDate ?? DateOnly.FromDateTime(DateTime.UtcNow));
        command.Parameters.AddWithValue("status", request.Status.Trim());
        command.Parameters.AddWithValue("total_amount", request.TotalAmount);
        command.Parameters.AddWithValue("notes", DbValue(request.Notes));

        return await ReadPurchaseAsync(command, cancellationToken)
            ?? throw new InvalidOperationException("Purchase was not created.");
    }

    public async Task<PurchaseDto?> UpdatePurchaseAsync(
        int id,
        UpdatePurchaseRequest request,
        CancellationToken cancellationToken = default)
    {
        var schemaName = QuoteIdentifier(_tenantProvider.GetCurrentSchemaName());

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
            """, cancellationToken);

        command.Parameters.AddWithValue("id", id);
        command.Parameters.AddWithValue("supplier_id", request.SupplierId);
        command.Parameters.AddWithValue("market_id", request.MarketId);
        command.Parameters.AddWithValue("purchase_date", request.PurchaseDate);
        command.Parameters.AddWithValue("status", request.Status.Trim());
        command.Parameters.AddWithValue("total_amount", request.TotalAmount);
        command.Parameters.AddWithValue("notes", DbValue(request.Notes));

        return await ReadPurchaseAsync(command, cancellationToken);
    }

    public async Task<PurchaseDto?> PatchPurchaseAsync(
        int id,
        PatchPurchaseRequest request,
        CancellationToken cancellationToken = default)
    {
        var schemaName = QuoteIdentifier(_tenantProvider.GetCurrentSchemaName());

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
            """, cancellationToken);

        command.Parameters.AddWithValue("id", id);
        command.Parameters.AddWithValue("supplier_id", DbValue(request.SupplierId));
        command.Parameters.AddWithValue("market_id", DbValue(request.MarketId));
        command.Parameters.AddWithValue("purchase_date", DbValue(request.PurchaseDate));
        command.Parameters.AddWithValue("status", DbValue(request.Status?.Trim()));
        command.Parameters.AddWithValue("total_amount", DbValue(request.TotalAmount));
        command.Parameters.AddWithValue("notes", DbValue(request.Notes));

        return await ReadPurchaseAsync(command, cancellationToken);
    }

    private async Task<InventoryItemDto?> GetInventoryItemAsync(
        int id,
        CancellationToken cancellationToken)
    {
        var schemaName = QuoteIdentifier(_tenantProvider.GetCurrentSchemaName());

        await using var command = await CreateCommandAsync($"""
            SELECT i.id,
                   i.product_id,
                   p.name,
                   i.market_id,
                   m.name,
                   i.quantity,
                   i.reserved_quantity
            FROM {schemaName}.inventory i
            INNER JOIN {schemaName}.products p ON p.id = i.product_id
            INNER JOIN {schemaName}.markets m ON m.id = i.market_id
            WHERE i.id = @id;
            """, cancellationToken);
        command.Parameters.AddWithValue("id", id);

        await using var reader = await command.ExecuteReaderAsync(cancellationToken);

        return await reader.ReadAsync(cancellationToken)
            ? new InventoryItemDto
            {
                Id = reader.GetInt32(0),
                ProductId = reader.GetInt32(1),
                ProductName = reader.GetString(2),
                MarketId = reader.GetInt32(3),
                MarketName = reader.GetString(4),
                Quantity = reader.GetInt32(5),
                ReservedQuantity = reader.GetInt32(6)
            }
            : null;
    }

    private static async Task<ProductDto?> ReadProductAsync(
        NpgsqlCommand command,
        CancellationToken cancellationToken)
    {
        await using var reader = await command.ExecuteReaderAsync(cancellationToken);

        return await reader.ReadAsync(cancellationToken)
            ? new ProductDto
            {
                Id = reader.GetInt32(0),
                Name = reader.GetString(1),
                Description = reader.IsDBNull(2) ? null : reader.GetString(2),
                Barcode = reader.IsDBNull(3) ? null : reader.GetString(3),
                UnitPrice = reader.GetDecimal(4)
            }
            : null;
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

    private async Task<NpgsqlCommand> CreateCommandAsync(
        string commandText,
        CancellationToken cancellationToken)
    {
        var connection = (NpgsqlConnection)_dbContext.Database.GetDbConnection();

        if (connection.State != ConnectionState.Open)
        {
            await connection.OpenAsync(cancellationToken);
        }

        return new NpgsqlCommand(commandText, connection);
    }

    private static string QuoteIdentifier(string identifier)
    {
        return "\"" + identifier.Replace("\"", "\"\"", StringComparison.Ordinal) + "\"";
    }
}
