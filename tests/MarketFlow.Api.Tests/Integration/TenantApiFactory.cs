using System.Net.Http.Headers;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Logging;

namespace MarketFlow.Api.Tests.Integration;

public sealed class TenantApiFactory : WebApplicationFactory<Program>
{
    private readonly TenantIntegrationTestOptions _options;
    private readonly ILoggerProvider? _loggerProvider;

    public TenantApiFactory(
        TenantIntegrationTestOptions options,
        ILoggerProvider? loggerProvider = null)
    {
        _options = options;
        _loggerProvider = loggerProvider;
    }

    public HttpClient CreateAuthenticatedClient(TenantIntegrationTestDatabase database, TenantTestUser user)
    {
        var client = CreateClient();
        AttachBearerToken(client, database.GenerateAccessToken(user));

        return client;
    }

    public static void AttachBearerToken(HttpClient client, string accessToken)
    {
        client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", accessToken);
    }

    protected override void ConfigureWebHost(IWebHostBuilder builder)
    {
        builder.UseEnvironment("Testing");

        builder.ConfigureAppConfiguration((_, configurationBuilder) =>
        {
            configurationBuilder.AddInMemoryCollection(new Dictionary<string, string?>
            {
                ["ConnectionStrings:DefaultConnection"] = _options.ConnectionString,
                ["Jwt:Issuer"] = _options.JwtIssuer,
                ["Jwt:Audience"] = _options.JwtAudience,
                ["Jwt:Secret"] = _options.JwtSecret,
                ["Jwt:AccessTokenMinutes"] = _options.AccessTokenMinutes.ToString(),
                ["Ai:Provider"] = "Fake"
            });
        });

        if (_loggerProvider is not null)
        {
            builder.ConfigureLogging(logging =>
            {
                logging.SetMinimumLevel(LogLevel.Debug);
                logging.AddProvider(_loggerProvider);
            });
        }
    }
}
