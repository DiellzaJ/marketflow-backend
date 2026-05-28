using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using MarketFlow.Application.Common.Models;
using MarketFlow.Application.Features.Suppliers.DTOs;

namespace MarketFlow.Api.Tests.Integration;

public sealed class SuppliersIntegrationTests
{
    private const string TenantSchemaHeaderName = "X-Tenant-Schema";
    private const string SchemaNameHeaderName = "X-Schema-Name";

    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        PropertyNameCaseInsensitive = true
    };

    [PostgresIntegrationFact]
    public async Task GetSuppliers_ReturnsOnlyActiveSuppliersFromAuthenticatedUsersTenant()
    {
        var options = TenantIntegrationTestOptions.FromEnvironment();

        await using var database = new TenantIntegrationTestDatabase(options);
        using var apiFactory = new TenantApiFactory(options);

        await database.EnsureRequiredRolesAsync();

        var companyA = await database.CreateCompanyAsync(
            database.CreateUniqueSchemaName("suppliers_lookup_a"),
            name: "Suppliers Lookup A",
            dropSchemaOnDispose: true);
        var companyB = await database.CreateCompanyAsync(
            database.CreateUniqueSchemaName("suppliers_lookup_b"),
            name: "Suppliers Lookup B",
            dropSchemaOnDispose: true);
        var userA = await database.CreateUserAsync(companyA, roleName: "CompanyAdmin");

        var activeSupplier = await database.InsertSupplierAsync(
            companyA.SchemaName,
            name: "Tenant A Active Supplier");
        var inactiveSupplier = await database.InsertSupplierAsync(
            companyA.SchemaName,
            name: "Tenant A Inactive Supplier",
            isActive: false);
        var otherTenantSupplier = await database.InsertSupplierAsync(
            companyB.SchemaName,
            name: "Tenant B Active Supplier");

        using var client = apiFactory.CreateAuthenticatedClient(database, userA);
        client.DefaultRequestHeaders.Add(TenantSchemaHeaderName, companyB.SchemaName);
        client.DefaultRequestHeaders.Add(SchemaNameHeaderName, companyB.SchemaName);

        var response = await client.GetAsync("/api/Suppliers");
        response.EnsureSuccessStatusCode();
        var result = await response.Content.ReadFromJsonAsync<ServiceResult<IReadOnlyCollection<SupplierDto>>>(
            JsonOptions);

        Assert.NotNull(result);
        Assert.True(result.Succeeded);
        var suppliers = Assert.IsAssignableFrom<IReadOnlyCollection<SupplierDto>>(result.Data);
        var supplier = Assert.Single(suppliers);
        Assert.Equal(activeSupplier.Id, supplier.Id);
        Assert.Equal(activeSupplier.Name, supplier.Name);
        Assert.True(supplier.IsActive);
        Assert.DoesNotContain(suppliers, x => x.Id == inactiveSupplier.Id);
        Assert.DoesNotContain(suppliers, x => x.Name == otherTenantSupplier.Name);

        var includeInactiveResponse = await client.GetAsync("/api/Suppliers?includeInactive=true");
        includeInactiveResponse.EnsureSuccessStatusCode();
        var includeInactiveResult = await includeInactiveResponse.Content
            .ReadFromJsonAsync<ServiceResult<IReadOnlyCollection<SupplierDto>>>(JsonOptions);
        var allTenantSuppliers = Assert.IsAssignableFrom<IReadOnlyCollection<SupplierDto>>(
            includeInactiveResult?.Data);
        Assert.Contains(allTenantSuppliers, x => x.Id == inactiveSupplier.Id && !x.IsActive);
        Assert.DoesNotContain(allTenantSuppliers, x => x.Name == otherTenantSupplier.Name);
    }

    [PostgresIntegrationFact]
    public async Task SupplierCrud_AllowsCompanyAdminToManageSuppliers()
    {
        var options = TenantIntegrationTestOptions.FromEnvironment();

        await using var database = new TenantIntegrationTestDatabase(options);
        using var apiFactory = new TenantApiFactory(options);

        await database.EnsureRequiredRolesAsync();

        var company = await database.CreateCompanyAsync(
            database.CreateUniqueSchemaName("suppliers_crud"),
            name: "Suppliers Crud",
            dropSchemaOnDispose: true);
        var user = await database.CreateUserAsync(company, roleName: "CompanyAdmin");

        using var client = apiFactory.CreateAuthenticatedClient(database, user);

        var createResponse = await client.PostAsJsonAsync("/api/Suppliers", new CreateSupplierRequest
        {
            Name = " Fresh Supplier ",
            Phone = " 044 100 200 ",
            Email = " supplier@example.com ",
            Address = " Main Street "
        });

        Assert.Equal(HttpStatusCode.Created, createResponse.StatusCode);
        var createResult = await createResponse.Content.ReadFromJsonAsync<ServiceResult<SupplierDto>>(JsonOptions);
        Assert.NotNull(createResult);
        Assert.True(createResult.Succeeded);
        Assert.Equal("Fresh Supplier", createResult.Data?.Name);
        Assert.Equal("044 100 200", createResult.Data?.Phone);
        Assert.Equal("supplier@example.com", createResult.Data?.Email);
        Assert.Equal("Main Street", createResult.Data?.Address);
        Assert.True(createResult.Data?.IsActive);

        var supplierId = createResult.Data!.Id;
        var updateResponse = await client.PutAsJsonAsync(
            $"/api/Suppliers/{supplierId}",
            new UpdateSupplierRequest
            {
                Name = "Updated Supplier",
                Phone = "045 300 400",
                Email = "updated@example.com",
                Address = "Updated Address"
            });
        updateResponse.EnsureSuccessStatusCode();

        var deactivateResponse = await client.PatchAsJsonAsync(
            $"/api/Suppliers/{supplierId}/status",
            new PatchSupplierStatusRequest { IsActive = false });
        deactivateResponse.EnsureSuccessStatusCode();
        var deactivateResult = await deactivateResponse.Content.ReadFromJsonAsync<ServiceResult<SupplierDto>>(
            JsonOptions);
        Assert.False(deactivateResult?.Data?.IsActive);

        var defaultList = await client.GetFromJsonAsync<ServiceResult<IReadOnlyCollection<SupplierDto>>>(
            "/api/Suppliers",
            JsonOptions);
        Assert.DoesNotContain(defaultList?.Data ?? [], x => x.Id == supplierId);

        var includeInactiveList = await client.GetFromJsonAsync<ServiceResult<IReadOnlyCollection<SupplierDto>>>(
            "/api/Suppliers?includeInactive=true",
            JsonOptions);
        Assert.Contains(includeInactiveList?.Data ?? [], x => x.Id == supplierId && !x.IsActive);
    }
}
