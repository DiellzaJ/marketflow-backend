using System.Net.Http.Json;
using System.Text.Json;
using MarketFlow.Application.Features.Auth.DTOs;

namespace MarketFlow.Api.Tests.Integration;

public sealed class AuthAssignmentIntegrationTests
{
    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        PropertyNameCaseInsensitive = true
    };

    [PostgresIntegrationTheory]
    [InlineData("Seller", true)]
    [InlineData("MainOperator", false)]
    [InlineData("InventoryEmployee", false)]
    [InlineData("DepartmentManager", true)]
    public async Task Login_ReturnsActiveStaffAssignmentForOperationalRoles(
        string roleName,
        bool assignDepartment)
    {
        var options = TenantIntegrationTestOptions.FromEnvironment();

        await using var database = new TenantIntegrationTestDatabase(options);
        using var apiFactory = new TenantApiFactory(options);

        await database.EnsureRequiredRolesAsync();

        var company = await database.CreateCompanyAsync(
            database.CreateUniqueSchemaName("auth_assignment"),
            name: $"Auth Assignment {roleName}",
            dropSchemaOnDispose: true);
        var user = await database.CreateUserAsync(company, roleName: roleName);
        var market = await database.InsertMarketAsync(company.SchemaName, name: $"{roleName} Market");
        var department = assignDepartment
            ? await database.InsertDepartmentAsync(company.SchemaName, market.Id, name: $"{roleName} Department")
            : null;

        await database.InsertStaffAssignmentAsync(
            company.SchemaName,
            user.Id,
            market.Id,
            department?.Id);

        using var client = apiFactory.CreateClient();
        var response = await client.PostAsJsonAsync(
            "/api/Auth/login",
            new LoginRequest
            {
                Email = user.Email,
                Password = "User12345"
            });

        response.EnsureSuccessStatusCode();
        var authResponse = await response.Content.ReadFromJsonAsync<AuthResponse>(JsonOptions);

        Assert.NotNull(authResponse);
        Assert.Equal(user.Id, authResponse.UserId);
        Assert.Equal(roleName, authResponse.Role);
        Assert.Equal(company.Id, authResponse.CompanyId);
        Assert.Equal(company.SchemaName, authResponse.SchemaName);
        Assert.NotNull(authResponse.Assignment);
        Assert.Equal(market.Id, authResponse.Assignment.MarketId);
        Assert.Equal(department?.Id, authResponse.Assignment.DepartmentId);

        using var meClient = apiFactory.CreateClient();
        TenantApiFactory.AttachBearerToken(meClient, authResponse.AccessToken);

        var meResponse = await meClient.GetAsync("/api/Auth/me");
        meResponse.EnsureSuccessStatusCode();
        using var meDocument = JsonDocument.Parse(await meResponse.Content.ReadAsStringAsync());
        var meAssignment = meDocument.RootElement.GetProperty("assignment");

        Assert.Equal(market.Id, meAssignment.GetProperty("marketId").GetInt32());

        var meDepartmentId = meAssignment.GetProperty("departmentId");

        if (department is null)
        {
            Assert.Equal(JsonValueKind.Null, meDepartmentId.ValueKind);
        }
        else
        {
            Assert.Equal(department.Id, meDepartmentId.GetInt32());
        }
    }
}
