using System.Net;
using System.Net.Http.Json;
using MarketFlow.Application.Common.Models;
using MarketFlow.Application.Features.Inventory.DTOs;
using MarketFlow.Application.Features.Purchases.DTOs;

namespace MarketFlow.Api.Tests.Integration;

public sealed class PurchaseReceiptIntegrationTests
{
    [PostgresIntegrationFact]
    public async Task PatchPurchaseToReceived_IncreasesStockAndCreatesMovementHistoryOnce()
    {
        var options = TenantIntegrationTestOptions.FromEnvironment();

        await using var database = new TenantIntegrationTestDatabase(options);
        using var apiFactory = new TenantApiFactory(options);

        await database.EnsureRequiredRolesAsync();

        var company = await database.CreateCompanyAsync(
            database.CreateUniqueSchemaName("purchase_receipt"),
            name: "purchase_receipt",
            dropSchemaOnDispose: true);
        var market = await database.InsertMarketAsync(company.SchemaName, "Market A");
        var supplier = await database.InsertSupplierAsync(company.SchemaName, "Receipt Supplier");
        var productA = await database.InsertProductAsync(company.SchemaName, name: "Receipt Product A");
        var productB = await database.InsertProductAsync(company.SchemaName, name: "Receipt Product B");
        var inventoryA = await database.InsertInventoryAsync(
            company.SchemaName,
            productA.Id,
            market.Id,
            quantity: 5);
        var inventoryB = await database.InsertInventoryAsync(
            company.SchemaName,
            productB.Id,
            market.Id,
            quantity: 1);
        var user = await database.CreateUserAsync(company, roleName: "CompanyAdmin");

        using var client = apiFactory.CreateAuthenticatedClient(database, user);

        var purchase = await database.InsertPurchaseAsync(
            company.SchemaName,
            supplier.Id,
            market.Id,
            user.Id,
            totalAmount: 100);
        await database.InsertPurchaseItemAsync(company.SchemaName, purchase.Id, productA.Id, quantity: 7, unitCost: 8);
        await database.InsertPurchaseItemAsync(company.SchemaName, purchase.Id, productB.Id, quantity: 3, unitCost: 9);

        var receiveResponse = await client.PatchAsJsonAsync(
            $"/api/purchases/{purchase.Id}",
            new PatchPurchaseRequest { Status = "Received" });
        receiveResponse.EnsureSuccessStatusCode();

        var updatedInventoryA = await database.GetInventoryDetailsAsync(company.SchemaName, inventoryA.Id);
        var updatedInventoryB = await database.GetInventoryDetailsAsync(company.SchemaName, inventoryB.Id);
        Assert.Equal(12, updatedInventoryA?.Quantity);
        Assert.Equal(4, updatedInventoryB?.Quantity);

        var movementReference = $"purchase:{purchase.Id}";
        var movementsA = await GetInventoryMovementsAsync(client, inventoryA.Id);
        var movementsB = await GetInventoryMovementsAsync(client, inventoryB.Id);
        Assert.Single(movementsA, movement =>
            movement.MovementType == "PurchaseReceived" &&
            movement.QuantityChanged == 7 &&
            movement.ReferenceNumber == movementReference);
        Assert.Single(movementsB, movement =>
            movement.MovementType == "PurchaseReceived" &&
            movement.QuantityChanged == 3 &&
            movement.ReferenceNumber == movementReference);

        var duplicateReceiveResponse = await client.PatchAsJsonAsync(
            $"/api/purchases/{purchase.Id}",
            new PatchPurchaseRequest { Status = "Received" });
        duplicateReceiveResponse.EnsureSuccessStatusCode();

        updatedInventoryA = await database.GetInventoryDetailsAsync(company.SchemaName, inventoryA.Id);
        updatedInventoryB = await database.GetInventoryDetailsAsync(company.SchemaName, inventoryB.Id);
        Assert.Equal(12, updatedInventoryA?.Quantity);
        Assert.Equal(4, updatedInventoryB?.Quantity);

        movementsA = await GetInventoryMovementsAsync(client, inventoryA.Id);
        movementsB = await GetInventoryMovementsAsync(client, inventoryB.Id);
        Assert.Single(movementsA, movement =>
            movement.MovementType == "PurchaseReceived" &&
            movement.ReferenceNumber == movementReference);
        Assert.Single(movementsB, movement =>
            movement.MovementType == "PurchaseReceived" &&
            movement.ReferenceNumber == movementReference);
    }

    [PostgresIntegrationFact]
    public async Task PatchPurchaseToReceived_FailsWhenDestinationMarketIsOutsideUserScope()
    {
        var options = TenantIntegrationTestOptions.FromEnvironment();

        await using var database = new TenantIntegrationTestDatabase(options);
        using var apiFactory = new TenantApiFactory(options);

        await database.EnsureRequiredRolesAsync();

        var company = await database.CreateCompanyAsync(
            database.CreateUniqueSchemaName("purchase_receipt_scope"),
            name: "purchase_receipt_scope",
            dropSchemaOnDispose: true);
        var marketA = await database.InsertMarketAsync(company.SchemaName, "Market A");
        var marketB = await database.InsertMarketAsync(company.SchemaName, "Market B");
        var supplier = await database.InsertSupplierAsync(company.SchemaName, "Receipt Supplier");
        var product = await database.InsertProductAsync(company.SchemaName, name: "Scoped Receipt Product");
        var inventory = await database.InsertInventoryAsync(
            company.SchemaName,
            product.Id,
            marketB.Id,
            quantity: 6);
        var companyAdmin = await database.CreateUserAsync(company, roleName: "CompanyAdmin");
        var operatorUser = await database.CreateUserAsync(company, roleName: "MainOperator");
        await database.InsertStaffAssignmentAsync(company.SchemaName, operatorUser.Id, marketA.Id);

        using var adminClient = apiFactory.CreateAuthenticatedClient(database, companyAdmin);
        var purchase = await database.InsertPurchaseAsync(
            company.SchemaName,
            supplier.Id,
            marketB.Id,
            companyAdmin.Id,
            totalAmount: 25);
        await database.InsertPurchaseItemAsync(company.SchemaName, purchase.Id, product.Id, quantity: 5, unitCost: 5);

        using var operatorClient = apiFactory.CreateAuthenticatedClient(database, operatorUser);
        var response = await operatorClient.PatchAsJsonAsync(
            $"/api/purchases/{purchase.Id}",
            new PatchPurchaseRequest { Status = "Received" });

        Assert.Equal(HttpStatusCode.NotFound, response.StatusCode);

        var unchangedInventory = await database.GetInventoryDetailsAsync(company.SchemaName, inventory.Id);
        Assert.Equal(6, unchangedInventory?.Quantity);

        var movements = await GetInventoryMovementsAsync(adminClient, inventory.Id);
        Assert.DoesNotContain(movements, movement =>
            movement.MovementType == "PurchaseReceived" &&
            movement.ReferenceNumber == $"purchase:{purchase.Id}");
    }

    private static async Task<IReadOnlyList<InventoryMovementDto>> GetInventoryMovementsAsync(
        HttpClient client,
        int inventoryId)
    {
        var response = await client.GetAsync($"/api/inventory/{inventoryId}/movements");
        response.EnsureSuccessStatusCode();

        var result = await response.Content.ReadFromJsonAsync<ServiceResult<PagedResult<InventoryMovementDto>>>();
        Assert.NotNull(result);
        Assert.True(result.Succeeded);
        Assert.NotNull(result.Data);

        return result.Data.Items.ToList();
    }
}
