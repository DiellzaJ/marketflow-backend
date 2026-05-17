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
