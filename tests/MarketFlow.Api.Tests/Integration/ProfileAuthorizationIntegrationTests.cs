using System.Net;
using System.Net.Http.Json;
using MarketFlow.Application.Features.Profile.DTOs;

namespace MarketFlow.Api.Tests.Integration;

public sealed class ProfileAuthorizationIntegrationTests
{
    [PostgresIntegrationFact]
    public async Task ProfileEndpoints_AllowActiveUser()
    {
        var options = TenantIntegrationTestOptions.FromEnvironment();

        await using var database = new TenantIntegrationTestDatabase(options);
        using var apiFactory = new TenantApiFactory(options);

        var company = await database.CreateCompanyAsync(
            database.CreateUniqueSchemaName("profile_active"),
            dropSchemaOnDispose: true);
        var user = await database.CreateUserAsync(company);

        using var client = apiFactory.CreateAuthenticatedClient(database, user);

        var getResponse = await client.GetAsync("/api/Profile");
        var updateResponse = await client.PutAsJsonAsync(
            "/api/Profile",
            new UpdateProfileRequest { FullName = "Updated Profile User" });
        var passwordResponse = await client.PutAsJsonAsync(
            "/api/Profile/password",
            new ChangePasswordRequest
            {
                CurrentPassword = "User12345",
                NewPassword = "User23456"
            });

        Assert.Equal(HttpStatusCode.OK, getResponse.StatusCode);
        Assert.Equal(HttpStatusCode.OK, updateResponse.StatusCode);
        Assert.Equal(HttpStatusCode.OK, passwordResponse.StatusCode);
    }

    [PostgresIntegrationFact]
    public async Task GetProfile_RejectsInactiveUserWithValidToken()
    {
        var options = TenantIntegrationTestOptions.FromEnvironment();

        await using var database = new TenantIntegrationTestDatabase(options);
        using var apiFactory = new TenantApiFactory(options);

        var company = await database.CreateCompanyAsync(
            database.CreateUniqueSchemaName("profile_inactive_get"),
            dropSchemaOnDispose: true);
        var user = await database.CreateUserAsync(company, isActive: false);

        using var client = apiFactory.CreateAuthenticatedClient(database, user);

        var response = await client.GetAsync("/api/Profile");

        Assert.Equal(HttpStatusCode.Forbidden, response.StatusCode);
    }

    [PostgresIntegrationFact]
    public async Task ChangePassword_RejectsInactiveUserWithValidToken()
    {
        var options = TenantIntegrationTestOptions.FromEnvironment();

        await using var database = new TenantIntegrationTestDatabase(options);
        using var apiFactory = new TenantApiFactory(options);

        var company = await database.CreateCompanyAsync(
            database.CreateUniqueSchemaName("profile_inactive_password"),
            dropSchemaOnDispose: true);
        var user = await database.CreateUserAsync(company, isActive: false);

        using var client = apiFactory.CreateAuthenticatedClient(database, user);

        var response = await client.PutAsJsonAsync(
            "/api/Profile/password",
            new ChangePasswordRequest
            {
                CurrentPassword = "User12345",
                NewPassword = "User23456"
            });

        Assert.Equal(HttpStatusCode.Forbidden, response.StatusCode);
    }
}
