using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using MarketFlow.Application.Common.Models;
using MarketFlow.Application.Features.Departments.DTOs;

namespace MarketFlow.Api.Tests.Integration;

public sealed class DepartmentsLookupIntegrationTests
{
    private const string TenantSchemaHeaderName = "X-Tenant-Schema";
    private const string SchemaNameHeaderName = "X-Schema-Name";

    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        PropertyNameCaseInsensitive = true
    };

    [PostgresIntegrationFact]
    public async Task GetDepartments_ReturnsOnlyActiveDepartmentsFromAuthenticatedUsersTenant()
    {
        var options = TenantIntegrationTestOptions.FromEnvironment();

        await using var database = new TenantIntegrationTestDatabase(options);
        using var apiFactory = new TenantApiFactory(options);

        await database.EnsureRequiredRolesAsync();

        var companyA = await database.CreateCompanyAsync(
            database.CreateUniqueSchemaName("departments_lookup_a"),
            name: "Departments Lookup A",
            dropSchemaOnDispose: true);
        var companyB = await database.CreateCompanyAsync(
            database.CreateUniqueSchemaName("departments_lookup_b"),
            name: "Departments Lookup B",
            dropSchemaOnDispose: true);
        var userA = await database.CreateUserAsync(companyA, roleName: "CompanyAdmin");

        var marketA = await database.InsertMarketAsync(companyA.SchemaName, name: "Tenant A Market");
        var otherMarketA = await database.InsertMarketAsync(companyA.SchemaName, name: "Tenant A Other Market");
        var marketB = await database.InsertMarketAsync(companyB.SchemaName, name: "Tenant B Market");

        var activeDepartment = await database.InsertDepartmentAsync(
            companyA.SchemaName,
            marketA.Id,
            name: "Tenant A Active Department",
            description: "Tenant A department description");
        var otherMarketDepartment = await database.InsertDepartmentAsync(
            companyA.SchemaName,
            otherMarketA.Id,
            name: "Tenant A Other Market Department");
        var nullDescriptionDepartment = await database.InsertDepartmentAsync(
            companyA.SchemaName,
            marketA.Id,
            name: "Tenant A Null Description Department");
        var inactiveDepartment = await database.InsertDepartmentAsync(
            companyA.SchemaName,
            marketA.Id,
            name: "Tenant A Inactive Department",
            isActive: false);
        var otherTenantDepartment = await database.InsertDepartmentAsync(
            companyB.SchemaName,
            marketB.Id,
            name: "Tenant B Active Department");

        using var client = apiFactory.CreateAuthenticatedClient(database, userA);
        client.DefaultRequestHeaders.Add(TenantSchemaHeaderName, companyB.SchemaName);
        client.DefaultRequestHeaders.Add(SchemaNameHeaderName, companyB.SchemaName);

        var encodedSchemaName = Uri.EscapeDataString(companyB.SchemaName);
        // Authenticated tenant resolution ignores untrusted schema hints from headers and query string.
        var response = await client.GetAsync($"/api/Departments?schemaName={encodedSchemaName}");

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        var responseBody = await response.Content.ReadAsStringAsync();
        var result = DeserializeDepartments(responseBody);

        var departments = Assert.IsAssignableFrom<IReadOnlyCollection<DepartmentDto>>(result.Data);
        Assert.Contains(departments, x => x.Id == activeDepartment.Id);
        Assert.Contains(departments, x => x.Id == otherMarketDepartment.Id);
        Assert.Contains(departments, x => x.Id == nullDescriptionDepartment.Id);
        Assert.DoesNotContain(departments, x => x.Id == inactiveDepartment.Id);
        Assert.DoesNotContain(departments, x => x.Name == otherTenantDepartment.Name);
        Assert.DoesNotContain(companyA.SchemaName, responseBody, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain(companyB.SchemaName, responseBody, StringComparison.OrdinalIgnoreCase);

        var department = departments.Single(x => x.Id == activeDepartment.Id);
        Assert.Equal(activeDepartment.MarketId, department.MarketId);
        Assert.Equal(activeDepartment.Name, department.Name);
        Assert.Equal(activeDepartment.Description, department.Description);
        Assert.True(department.IsActive);

        var departmentWithNullDescription = departments.Single(x => x.Id == nullDescriptionDepartment.Id);
        Assert.Null(departmentWithNullDescription.Description);
    }

    [PostgresIntegrationFact]
    public async Task GetDepartments_WithMarketIdFiltersDepartmentsInsideAuthenticatedUsersTenant()
    {
        var options = TenantIntegrationTestOptions.FromEnvironment();

        await using var database = new TenantIntegrationTestDatabase(options);
        using var apiFactory = new TenantApiFactory(options);

        await database.EnsureRequiredRolesAsync();

        var company = await database.CreateCompanyAsync(
            database.CreateUniqueSchemaName("departments_lookup_filter"),
            name: "Departments Lookup Filter",
            dropSchemaOnDispose: true);
        var user = await database.CreateUserAsync(company, roleName: "CompanyAdmin");
        var market = await database.InsertMarketAsync(company.SchemaName, name: "Filter Market");
        var otherMarket = await database.InsertMarketAsync(company.SchemaName, name: "Other Filter Market");
        var includedDepartment = await database.InsertDepartmentAsync(
            company.SchemaName,
            market.Id,
            name: "Included Department");
        var excludedDepartment = await database.InsertDepartmentAsync(
            company.SchemaName,
            otherMarket.Id,
            name: "Excluded Department");

        using var client = apiFactory.CreateAuthenticatedClient(database, user);
        var response = await client.GetAsync($"/api/Departments?marketId={market.Id}");

        response.EnsureSuccessStatusCode();
        var result = await response.Content.ReadFromJsonAsync<ServiceResult<IReadOnlyCollection<DepartmentDto>>>(
            JsonOptions);

        Assert.NotNull(result);
        Assert.True(result.Succeeded);
        var departments = Assert.IsAssignableFrom<IReadOnlyCollection<DepartmentDto>>(result.Data);
        var department = Assert.Single(departments);
        Assert.Equal(includedDepartment.Id, department.Id);
        Assert.Equal(market.Id, department.MarketId);
        Assert.DoesNotContain(departments, x => x.Id == excludedDepartment.Id);
    }

    [PostgresIntegrationFact]
    public async Task GetDepartments_AllowsSellerWithDepartmentsReadPermission()
    {
        var options = TenantIntegrationTestOptions.FromEnvironment();

        await using var database = new TenantIntegrationTestDatabase(options);
        using var apiFactory = new TenantApiFactory(options);

        await database.EnsureRequiredRolesAsync();

        var company = await database.CreateCompanyAsync(
            database.CreateUniqueSchemaName("departments_lookup_seller"),
            name: "Departments Lookup Seller",
            dropSchemaOnDispose: true);
        var seller = await database.CreateUserAsync(company, roleName: "Seller");
        var market = await database.InsertMarketAsync(company.SchemaName, name: "Seller Market");
        var department = await database.InsertDepartmentAsync(
            company.SchemaName,
            market.Id,
            name: "Seller Department");

        using var client = apiFactory.CreateAuthenticatedClient(database, seller);
        var response = await client.GetAsync("/api/Departments");

        response.EnsureSuccessStatusCode();
        var result = await response.Content.ReadFromJsonAsync<ServiceResult<IReadOnlyCollection<DepartmentDto>>>(
            JsonOptions);

        Assert.NotNull(result);
        Assert.True(result.Succeeded);
        var departments = Assert.IsAssignableFrom<IReadOnlyCollection<DepartmentDto>>(result.Data);
        Assert.Contains(departments, x => x.Id == department.Id && x.Name == department.Name);
    }

    [PostgresIntegrationFact]
    public async Task GetDepartments_WithAuthenticatedUserMissingDepartmentsReadReturnsForbidden()
    {
        var options = TenantIntegrationTestOptions.FromEnvironment();

        await using var database = new TenantIntegrationTestDatabase(options);
        using var apiFactory = new TenantApiFactory(options);

        await database.EnsureRequiredRolesAsync();

        var company = await database.CreateCompanyAsync(
            database.CreateUniqueSchemaName("departments_lookup_forbidden"),
            name: "Departments Lookup Forbidden",
            dropSchemaOnDispose: true);
        var userWithoutDepartmentsRead = await database.CreateUserAsync(company, roleName: "RootAdmin");

        using var client = apiFactory.CreateAuthenticatedClient(database, userWithoutDepartmentsRead);
        var response = await client.GetAsync("/api/Departments");

        Assert.Equal(HttpStatusCode.Forbidden, response.StatusCode);
    }

    [PostgresIntegrationFact]
    public async Task GetDepartments_WithoutAuthenticationReturnsUnauthorized()
    {
        var options = TenantIntegrationTestOptions.FromEnvironment();

        using var apiFactory = new TenantApiFactory(options);
        using var client = apiFactory.CreateClient();

        var response = await client.GetAsync("/api/Departments");

        Assert.Equal(HttpStatusCode.Unauthorized, response.StatusCode);
    }

    [PostgresIntegrationFact]
    public async Task DepartmentCrud_UsesAuthenticatedUsersTenantSchema()
    {
        var options = TenantIntegrationTestOptions.FromEnvironment();

        await using var database = new TenantIntegrationTestDatabase(options);
        using var apiFactory = new TenantApiFactory(options);

        await database.EnsureRequiredRolesAsync();

        var companyA = await database.CreateCompanyAsync(
            database.CreateUniqueSchemaName("departments_crud_a"),
            name: "Departments Crud A",
            dropSchemaOnDispose: true);
        var companyB = await database.CreateCompanyAsync(
            database.CreateUniqueSchemaName("departments_crud_b"),
            name: "Departments Crud B",
            dropSchemaOnDispose: true);
        var userA = await database.CreateUserAsync(companyA, roleName: "CompanyAdmin");
        var marketA = await database.InsertMarketAsync(companyA.SchemaName, name: "Tenant A Market");
        var marketB = await database.InsertMarketAsync(companyB.SchemaName, name: "Tenant B Market");

        using var client = apiFactory.CreateAuthenticatedClient(database, userA);
        client.DefaultRequestHeaders.Add(TenantSchemaHeaderName, companyB.SchemaName);
        client.DefaultRequestHeaders.Add(SchemaNameHeaderName, companyB.SchemaName);

        var createResponse = await client.PostAsJsonAsync(
            "/api/Departments",
            new CreateDepartmentRequest
            {
                MarketId = marketA.Id,
                Name = " Produce ",
                Description = " Fresh section "
            });

        Assert.Equal(HttpStatusCode.Created, createResponse.StatusCode);
        var createResult = await createResponse.Content.ReadFromJsonAsync<ServiceResult<DepartmentDto>>(JsonOptions);
        Assert.NotNull(createResult);
        Assert.True(createResult.Succeeded);
        Assert.Equal("Produce", createResult.Data?.Name);
        Assert.Equal("Fresh section", createResult.Data?.Description);
        Assert.True(createResult.Data?.IsActive);

        var departmentId = createResult.Data!.Id;
        Assert.Equal(1, await database.CountDepartmentsByNameAsync(companyA.SchemaName, marketA.Id, "Produce"));
        Assert.Equal(0, await database.CountDepartmentsByNameAsync(companyB.SchemaName, marketB.Id, "Produce"));

        var getResponse = await client.GetAsync($"/api/Departments/{departmentId}");
        getResponse.EnsureSuccessStatusCode();
        var getResult = await getResponse.Content.ReadFromJsonAsync<ServiceResult<DepartmentDto>>(JsonOptions);
        Assert.Equal(departmentId, getResult?.Data?.Id);
        Assert.Equal(marketA.Id, getResult?.Data?.MarketId);

        var updateResponse = await client.PutAsJsonAsync(
            $"/api/Departments/{departmentId}",
            new UpdateDepartmentRequest
            {
                Name = "Produce Plus",
                Description = "Updated section"
            });
        updateResponse.EnsureSuccessStatusCode();
        var updateResult = await updateResponse.Content.ReadFromJsonAsync<ServiceResult<DepartmentDto>>(JsonOptions);
        Assert.True(updateResult?.Succeeded);
        Assert.Equal("Produce Plus", updateResult?.Data?.Name);
        Assert.Equal("Updated section", updateResult?.Data?.Description);

        var deactivateResponse = await client.PatchAsync($"/api/Departments/{departmentId}/deactivate", null);
        deactivateResponse.EnsureSuccessStatusCode();
        var deactivateResult = await deactivateResponse.Content.ReadFromJsonAsync<ServiceResult<DepartmentDto>>(JsonOptions);
        Assert.False(deactivateResult?.Data?.IsActive);
        Assert.Equal(HttpStatusCode.NotFound, (await client.GetAsync($"/api/Departments/{departmentId}")).StatusCode);

        var activateResponse = await client.PatchAsync($"/api/Departments/{departmentId}/activate", null);
        activateResponse.EnsureSuccessStatusCode();
        var activateResult = await activateResponse.Content.ReadFromJsonAsync<ServiceResult<DepartmentDto>>(JsonOptions);
        Assert.True(activateResult?.Data?.IsActive);

        var storedDepartment = await database.GetDepartmentDetailsAsync(companyA.SchemaName, departmentId);
        Assert.NotNull(storedDepartment);
        Assert.Equal(marketA.Id, storedDepartment.MarketId);
        Assert.Equal("Produce Plus", storedDepartment.Name);
        Assert.Equal("Updated section", storedDepartment.Description);
        Assert.True(storedDepartment.IsActive);
        Assert.Equal(0, await database.CountDepartmentsByNameAsync(companyB.SchemaName, marketB.Id, "Produce Plus"));
    }

    [PostgresIntegrationFact]
    public async Task CreateDepartment_WithUnknownMarketReturnsNotFound()
    {
        var options = TenantIntegrationTestOptions.FromEnvironment();

        await using var database = new TenantIntegrationTestDatabase(options);
        using var apiFactory = new TenantApiFactory(options);

        await database.EnsureRequiredRolesAsync();

        var company = await database.CreateCompanyAsync(
            database.CreateUniqueSchemaName("departments_missing_market"),
            name: "Departments Missing Market",
            dropSchemaOnDispose: true);
        var user = await database.CreateUserAsync(company, roleName: "CompanyAdmin");

        using var client = apiFactory.CreateAuthenticatedClient(database, user);
        var response = await client.PostAsJsonAsync(
            "/api/Departments",
            new CreateDepartmentRequest
            {
                MarketId = 999999,
                Name = "Produce"
            });

        Assert.Equal(HttpStatusCode.NotFound, response.StatusCode);
        var result = await response.Content.ReadFromJsonAsync<ServiceResult<DepartmentDto>>(JsonOptions);
        Assert.False(result?.Succeeded);
        Assert.Equal(ServiceResultFailureType.NotFound, result?.FailureType);
        Assert.Equal("Department market was not found.", result?.Message);
    }

    [PostgresIntegrationFact]
    public async Task CreateDepartment_WithDuplicateNameInSameMarketReturnsConflict()
    {
        var options = TenantIntegrationTestOptions.FromEnvironment();

        await using var database = new TenantIntegrationTestDatabase(options);
        using var apiFactory = new TenantApiFactory(options);

        await database.EnsureRequiredRolesAsync();

        var company = await database.CreateCompanyAsync(
            database.CreateUniqueSchemaName("departments_duplicate"),
            name: "Departments Duplicate",
            dropSchemaOnDispose: true);
        var user = await database.CreateUserAsync(company, roleName: "CompanyAdmin");
        var market = await database.InsertMarketAsync(company.SchemaName, name: "Duplicate Market");
        await database.InsertDepartmentAsync(company.SchemaName, market.Id, name: "Produce");

        using var client = apiFactory.CreateAuthenticatedClient(database, user);
        var response = await client.PostAsJsonAsync(
            "/api/Departments",
            new CreateDepartmentRequest
            {
                MarketId = market.Id,
                Name = " produce "
            });

        Assert.Equal(HttpStatusCode.Conflict, response.StatusCode);
        var result = await response.Content.ReadFromJsonAsync<ServiceResult<DepartmentDto>>(JsonOptions);
        Assert.False(result?.Succeeded);
        Assert.Equal(ServiceResultFailureType.Conflict, result?.FailureType);
        Assert.Equal(
            "Department name is already used by another department in this market.",
            result?.Message);
        Assert.Equal(1, await database.CountDepartmentsByNameAsync(company.SchemaName, market.Id, "Produce"));
    }

    [PostgresIntegrationFact]
    public async Task SellerCannotCreateUpdateDeactivateOrActivateDepartments()
    {
        var options = TenantIntegrationTestOptions.FromEnvironment();

        await using var database = new TenantIntegrationTestDatabase(options);
        using var apiFactory = new TenantApiFactory(options);

        await database.EnsureRequiredRolesAsync();

        var company = await database.CreateCompanyAsync(
            database.CreateUniqueSchemaName("departments_seller_forbidden"),
            name: "Departments Seller Forbidden",
            dropSchemaOnDispose: true);
        var seller = await database.CreateUserAsync(company, roleName: "Seller");
        var market = await database.InsertMarketAsync(company.SchemaName, name: "Seller Market");
        var department = await database.InsertDepartmentAsync(company.SchemaName, market.Id, name: "Produce");

        using var client = apiFactory.CreateAuthenticatedClient(database, seller);

        var createResponse = await client.PostAsJsonAsync(
            "/api/Departments",
            new CreateDepartmentRequest
            {
                MarketId = market.Id,
                Name = "Blocked Department"
            });
        var updateResponse = await client.PutAsJsonAsync(
            $"/api/Departments/{department.Id}",
            new UpdateDepartmentRequest { Name = "Blocked Update" });
        var deactivateResponse = await client.PatchAsync($"/api/Departments/{department.Id}/deactivate", null);
        var activateResponse = await client.PatchAsync($"/api/Departments/{department.Id}/activate", null);

        Assert.Equal(HttpStatusCode.Forbidden, createResponse.StatusCode);
        Assert.Equal(HttpStatusCode.Forbidden, updateResponse.StatusCode);
        Assert.Equal(HttpStatusCode.Forbidden, deactivateResponse.StatusCode);
        Assert.Equal(HttpStatusCode.Forbidden, activateResponse.StatusCode);

        var storedDepartment = await database.GetDepartmentDetailsAsync(company.SchemaName, department.Id);
        Assert.NotNull(storedDepartment);
        Assert.Equal("Produce", storedDepartment.Name);
        Assert.True(storedDepartment.IsActive);
    }

    private static ServiceResult<IReadOnlyCollection<DepartmentDto>> DeserializeDepartments(string responseBody)
    {
        var result = JsonSerializer.Deserialize<ServiceResult<IReadOnlyCollection<DepartmentDto>>>(
            responseBody,
            JsonOptions);

        Assert.NotNull(result);
        Assert.True(result.Succeeded);

        return result;
    }
}
