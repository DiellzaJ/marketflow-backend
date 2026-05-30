using System.Net;
using System.Net.Http.Json;
using MarketFlow.Application.Common.Models;
using MarketFlow.Application.Features.Sales.DTOs;

namespace MarketFlow.Api.Tests.Integration;

public sealed class SaleDetailsIntegrationTests
{
    [PostgresIntegrationFact]
    public async Task GetSaleDetailsAsync_WhenSaleExists_ReturnsSaleDetailsWithItems()
    {
        var options = TenantIntegrationTestOptions.FromEnvironment();

        await using var database = new TenantIntegrationTestDatabase(options);
        using var apiFactory = new TenantApiFactory(options);

        await database.EnsureRequiredRolesAsync();

        var company = await database.CreateCompanyAsync(
            database.CreateUniqueSchemaName("sale_details"),
            name: "sale_details",
            dropSchemaOnDispose: true);
        var market = await database.InsertMarketAsync(company.SchemaName, "Main Market");
        var product = await database.InsertProductAsync(company.SchemaName, name: "Coca Cola");
        await database.InsertInventoryAsync(
            company.SchemaName,
            product.Id,
            market.Id,
            quantity: 10);
        var user = await database.CreateUserAsync(company, roleName: "CompanyAdmin");

        using var authenticatedClient = apiFactory.CreateAuthenticatedClient(database, user);

        var createSaleResponse = await authenticatedClient.PostAsJsonAsync("/api/sales", new CreateSaleRequest
        {
            MarketId = market.Id,
            PaymentMethod = "Cash",
            TotalAmount = 3.00m,
            Items = [
                new CreateSaleItemRequest
                {
                    ProductId = product.Id,
                    Quantity = 2,
                    UnitPrice = 1.50m
                }
            ]
        });
        createSaleResponse.EnsureSuccessStatusCode();

        var createResult = await createSaleResponse.Content.ReadFromJsonAsync<ServiceResult<SaleDto>>();
        Assert.NotNull(createResult?.Data);
        Assert.NotEmpty(createResult.Data.ReferenceNumber);

        var detailsResponse = await authenticatedClient.GetAsync($"/api/sales/{createResult.Data.Id}");
        detailsResponse.EnsureSuccessStatusCode();

        var detailsResult = await detailsResponse.Content.ReadFromJsonAsync<ServiceResult<SaleDetailsResponse>>();
        Assert.NotNull(detailsResult?.Data);
        Assert.True(detailsResult.Succeeded);
        Assert.Equal(createResult.Data.Id, detailsResult.Data.Id);
        Assert.Equal(createResult.Data.ReferenceNumber, detailsResult.Data.ReferenceNumber);
        Assert.Equal("Main Market", detailsResult.Data.MarketName);
        Assert.Equal(user.FullName, detailsResult.Data.CashierName);
        Assert.Equal(3.00m, detailsResult.Data.TotalAmount);
        var item = Assert.Single(detailsResult.Data.Items);
        Assert.Equal(product.Id, item.ProductId);
        Assert.Equal("Coca Cola", item.ProductName);
        Assert.Equal(2, item.Quantity);
        Assert.Equal(1.50m, item.UnitPrice);
        Assert.Equal(3.00m, item.LineTotal);
    }

    [PostgresIntegrationFact]
    public async Task GetSaleDetailsAsync_WhenSaleDoesNotExist_ReturnsNotFound()
    {
        var options = TenantIntegrationTestOptions.FromEnvironment();

        await using var database = new TenantIntegrationTestDatabase(options);
        using var apiFactory = new TenantApiFactory(options);

        await database.EnsureRequiredRolesAsync();

        var company = await database.CreateCompanyAsync(
            database.CreateUniqueSchemaName("sale_details_missing"),
            name: "sale_details_missing",
            dropSchemaOnDispose: true);
        var user = await database.CreateUserAsync(company, roleName: "CompanyAdmin");

        using var client = apiFactory.CreateAuthenticatedClient(database, user);

        var response = await client.GetAsync("/api/sales/9999");

        Assert.Equal(HttpStatusCode.NotFound, response.StatusCode);
    }

    [PostgresIntegrationFact]
    public async Task GetSaleDetailsAsync_WhenNotAuthenticated_ReturnsUnauthorized()
    {
        var options = TenantIntegrationTestOptions.FromEnvironment();

        await using var database = new TenantIntegrationTestDatabase(options);
        using var apiFactory = new TenantApiFactory(options);

        await database.EnsureRequiredRolesAsync();

        var company = await database.CreateCompanyAsync(
            database.CreateUniqueSchemaName("sale_details_unauthorized"),
            name: "sale_details_unauthorized",
            dropSchemaOnDispose: true);
        await database.CreateUserAsync(company, roleName: "CompanyAdmin");

        using var client = apiFactory.CreateClient();

        var response = await client.GetAsync("/api/sales/1");

        Assert.Equal(HttpStatusCode.Unauthorized, response.StatusCode);
    }
}
