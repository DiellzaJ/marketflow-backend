using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using MarketFlow.Application.Common.Models;
using MarketFlow.Application.Features.Products.DTOs;

namespace MarketFlow.Api.Tests.Integration;

public sealed class TenantOperationalIsolationIntegrationTests
{
    private const string TenantSchemaHeaderName = "X-Tenant-Schema";
    private const string SchemaNameHeaderName = "X-Schema-Name";
    private const string SchemaNameQueryParameter = "schemaName";
    private const string TenantSchemaQueryParameter = "tenantSchema";
    private const string SchemaNameBodyProperty = "schemaName";
    private const string TenantSchemaBodyProperty = "tenantSchema";

    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        PropertyNameCaseInsensitive = true
    };

    [PostgresIntegrationFact]
    public async Task ProductsEndpoint_ReturnsOnlyCurrentTenantProducts_WhenSchemaOverrideValuesAreSent()
    {
        var options = TenantIntegrationTestOptions.FromEnvironment();

        await using var database = new TenantIntegrationTestDatabase(options);
        using var apiFactory = new TenantApiFactory(options);

        var setup = await CreateTwoTenantProductSetupAsync(database);

        await AssertProductExistsOnlyInSchemaAsync(
            database,
            setup.CompanyA.SchemaName,
            setup.CompanyB.SchemaName,
            setup.CompanyAProduct.Barcode);
        await AssertProductExistsOnlyInSchemaAsync(
            database,
            setup.CompanyB.SchemaName,
            setup.CompanyA.SchemaName,
            setup.CompanyBProduct.Barcode);

        using var companyAClient = apiFactory.CreateAuthenticatedClient(database, setup.CompanyAUser);
        companyAClient.DefaultRequestHeaders.Add(TenantSchemaHeaderName, setup.CompanyB.SchemaName);
        companyAClient.DefaultRequestHeaders.Add(SchemaNameHeaderName, setup.CompanyB.SchemaName);

        var companyAResponse = await companyAClient.GetAsync(
            $"/api/products?{SchemaNameQueryParameter}={setup.CompanyB.SchemaName}" +
            $"&{TenantSchemaQueryParameter}={setup.CompanyB.SchemaName}");
        companyAResponse.EnsureSuccessStatusCode();
        var (companyABody, companyAProducts) =
            await ReadServiceResultWithBodyAsync<PagedResult<ProductDto>>(companyAResponse);
        var companyAData = Assert.IsType<PagedResult<ProductDto>>(companyAProducts.Data);

        Assert.Contains(
            companyAData.Items,
            product => product.Id == setup.CompanyAProduct.Id && product.Name == setup.CompanyAProduct.Name);
        Assert.DoesNotContain(
            companyAData.Items,
            product => product.Barcode == setup.CompanyBProduct.Barcode);
        AssertNoSchemaLeak(companyABody, setup);

        using var companyBClient = apiFactory.CreateAuthenticatedClient(database, setup.CompanyBUser);
        var companyBResponse = await companyBClient.GetAsync("/api/products");
        companyBResponse.EnsureSuccessStatusCode();
        var (companyBBody, companyBProducts) =
            await ReadServiceResultWithBodyAsync<PagedResult<ProductDto>>(companyBResponse);
        var companyBData = Assert.IsType<PagedResult<ProductDto>>(companyBProducts.Data);

        Assert.Contains(
            companyBData.Items,
            product => product.Id == setup.CompanyBProduct.Id && product.Name == setup.CompanyBProduct.Name);
        Assert.Contains(
            companyBData.Items,
            product => product.Id == setup.CompanyBProtectedProduct.Id &&
                product.Name == setup.CompanyBProtectedProduct.Name);
        Assert.DoesNotContain(
            companyBData.Items,
            product => product.Barcode == setup.CompanyAProduct.Barcode);
        AssertNoSchemaLeak(companyBBody, setup);
    }

    [PostgresIntegrationFact]
    public async Task ProductsEndpoint_BlocksCrossTenantProductReadUpdatePatchAndDelete()
    {
        var options = TenantIntegrationTestOptions.FromEnvironment();

        await using var database = new TenantIntegrationTestDatabase(options);
        using var apiFactory = new TenantApiFactory(options);

        var setup = await CreateTwoTenantProductSetupAsync(database);

        using var companyAClient = apiFactory.CreateAuthenticatedClient(database, setup.CompanyAUser);
        companyAClient.DefaultRequestHeaders.Add(TenantSchemaHeaderName, setup.CompanyB.SchemaName);

        var protectedProductBeforeAttempts = await ReadRequiredProductDetailsAsync(database, setup);

        var getResponse = await companyAClient.GetAsync(
            $"/api/products/{setup.CompanyBProtectedProduct.Id}" +
            $"?{SchemaNameQueryParameter}={setup.CompanyB.SchemaName}");
        await AssertNotFoundWithoutSchemaLeakAsync(getResponse, setup);

        var putResponse = await companyAClient.PutAsJsonAsync(
            $"/api/products/{setup.CompanyBProtectedProduct.Id}",
            new Dictionary<string, object?>
            {
                ["name"] = "Company A Override Attempt",
                ["barcode"] = "COMPANY-A-OVERRIDE",
                ["categoryId"] = setup.CompanyAProduct.CategoryId,
                ["unitPrice"] = 9.99m,
                ["costPrice"] = 4.99m,
                ["taxRate"] = 0m,
                ["minStockAlert"] = 0,
                [SchemaNameBodyProperty] = setup.CompanyB.SchemaName,
                [TenantSchemaBodyProperty] = setup.CompanyB.SchemaName
            });
        await AssertNotFoundWithoutSchemaLeakAsync(putResponse, setup);

        var patchResponse = await companyAClient.PatchAsJsonAsync(
            $"/api/products/{setup.CompanyBProtectedProduct.Id}",
            new Dictionary<string, object?>
            {
                ["name"] = "Company A Patch Attempt",
                [SchemaNameBodyProperty] = setup.CompanyB.SchemaName
            });
        await AssertNotFoundWithoutSchemaLeakAsync(patchResponse, setup);

        var deleteRequest = new HttpRequestMessage(
            HttpMethod.Delete,
            $"/api/products/{setup.CompanyBProtectedProduct.Id}");
        deleteRequest.Headers.Add(SchemaNameHeaderName, setup.CompanyB.SchemaName);
        var deleteResponse = await companyAClient.SendAsync(deleteRequest);
        await AssertNotFoundWithoutSchemaLeakAsync(deleteResponse, setup);

        await AssertProductDetailsUnchangedAsync(
            database,
            setup,
            protectedProductBeforeAttempts);

        using var companyBClient = apiFactory.CreateAuthenticatedClient(database, setup.CompanyBUser);
        var companyBReadResponse = await companyBClient.GetAsync(
            $"/api/products/{setup.CompanyBProtectedProduct.Id}");
        companyBReadResponse.EnsureSuccessStatusCode();
        var (companyBReadBody, companyBProduct) =
            await ReadServiceResultWithBodyAsync<ProductDto>(companyBReadResponse);
        var companyBProductData = Assert.IsType<ProductDto>(companyBProduct.Data);

        Assert.Equal(setup.CompanyBProtectedProduct.Name, companyBProductData.Name);
        Assert.Equal(setup.CompanyBProtectedProduct.Barcode, companyBProductData.Barcode);
        Assert.Equal(protectedProductBeforeAttempts.CategoryId, companyBProductData.CategoryId);
        Assert.Equal(protectedProductBeforeAttempts.UnitPrice, companyBProductData.UnitPrice);
        Assert.Equal(protectedProductBeforeAttempts.CostPrice, companyBProductData.CostPrice);
        Assert.Equal(protectedProductBeforeAttempts.TaxRate, companyBProductData.TaxRate);
        Assert.Equal(protectedProductBeforeAttempts.MinStockAlert, companyBProductData.MinStockAlert);
        Assert.True(companyBProductData.IsActive);
        AssertNoSchemaLeak(companyBReadBody, setup);
    }

    private static async Task<TwoTenantProductSetup> CreateTwoTenantProductSetupAsync(
        TenantIntegrationTestDatabase database)
    {
        await database.EnsureRequiredRolesAsync();

        var companyA = await database.CreateCompanyAsync(
            database.CreateUniqueSchemaName("tenant_isolation_a"),
            name: "Tenant Isolation A",
            dropSchemaOnDispose: true);
        var companyB = await database.CreateCompanyAsync(
            database.CreateUniqueSchemaName("tenant_isolation_b"),
            name: "Tenant Isolation B",
            dropSchemaOnDispose: true);
        var companyAUser = await database.CreateUserAsync(companyA, roleName: "CompanyAdmin");
        var companyBUser = await database.CreateUserAsync(companyB, roleName: "CompanyAdmin");

        var suffix = Guid.NewGuid().ToString("N")[..10];
        var companyAProduct = await database.InsertProductAsync(
            companyA.SchemaName,
            name: "Company A Product",
            barcode: $"ISO-A-{suffix}");
        var companyBProduct = await database.InsertProductAsync(
            companyB.SchemaName,
            name: "Company B Product",
            barcode: $"ISO-B-{suffix}");
        var companyBProtectedProduct = await database.InsertProductAsync(
            companyB.SchemaName,
            name: "Company B Protected Product",
            barcode: $"ISO-B-PROTECTED-{suffix}");

        Assert.NotEqual(companyA.SchemaName, companyB.SchemaName);
        Assert.NotEqual(companyAUser.Id, companyBUser.Id);
        Assert.NotEqual(companyAProduct.Id, companyBProtectedProduct.Id);

        return new TwoTenantProductSetup(
            companyA,
            companyB,
            companyAUser,
            companyBUser,
            companyAProduct,
            companyBProduct,
            companyBProtectedProduct);
    }

    private static async Task AssertProductExistsOnlyInSchemaAsync(
        TenantIntegrationTestDatabase database,
        string owningSchemaName,
        string otherSchemaName,
        string barcode)
    {
        Assert.Equal(1, await database.CountProductsByBarcodeAsync(owningSchemaName, barcode));
        Assert.Equal(0, await database.CountProductsByBarcodeAsync(otherSchemaName, barcode));
    }

    private static async Task AssertNotFoundWithoutSchemaLeakAsync(
        HttpResponseMessage response,
        TwoTenantProductSetup setup)
    {
        var responseBody = await response.Content.ReadAsStringAsync();

        Assert.Equal(HttpStatusCode.NotFound, response.StatusCode);
        AssertNoSchemaLeak(responseBody, setup);
    }

    private static async Task<TenantTestProductDetails> ReadRequiredProductDetailsAsync(
        TenantIntegrationTestDatabase database,
        TwoTenantProductSetup setup)
    {
        return await database.GetProductDetailsAsync(
            setup.CompanyB.SchemaName,
            setup.CompanyBProtectedProduct.Id)
            ?? throw new InvalidOperationException("Expected protected company B product to exist.");
    }

    private static async Task AssertProductDetailsUnchangedAsync(
        TenantIntegrationTestDatabase database,
        TwoTenantProductSetup setup,
        TenantTestProductDetails expectedProduct)
    {
        var actualProduct = await ReadRequiredProductDetailsAsync(database, setup);

        Assert.Equal(expectedProduct, actualProduct);
    }

    private static async Task<(string Body, ServiceResult<T> Result)> ReadServiceResultWithBodyAsync<T>(
        HttpResponseMessage response)
    {
        var body = await response.Content.ReadAsStringAsync();
        var result = JsonSerializer.Deserialize<ServiceResult<T>>(body, JsonOptions);

        return (
            body,
            result ?? throw new InvalidOperationException("Expected a service result response body."));
    }

    private static void AssertNoSchemaLeak(string responseBody, TwoTenantProductSetup setup)
    {
        Assert.DoesNotContain(setup.CompanyA.SchemaName, responseBody, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain(setup.CompanyB.SchemaName, responseBody, StringComparison.OrdinalIgnoreCase);
    }

    private sealed record TwoTenantProductSetup(
        TenantTestCompany CompanyA,
        TenantTestCompany CompanyB,
        TenantTestUser CompanyAUser,
        TenantTestUser CompanyBUser,
        TenantTestProduct CompanyAProduct,
        TenantTestProduct CompanyBProduct,
        TenantTestProduct CompanyBProtectedProduct);
}
