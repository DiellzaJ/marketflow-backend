using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Design;
using Npgsql;

namespace MarketFlow.Infrastructure.Persistence;

public sealed class ApplicationDbContextFactory : IDesignTimeDbContextFactory<ApplicationDbContext>
{
    public ApplicationDbContext CreateDbContext(string[] args)
    {
        LoadDotEnv();

        var optionsBuilder = new DbContextOptionsBuilder<ApplicationDbContext>();
        optionsBuilder.UseNpgsql(CreatePostgresConnectionString());

        return new ApplicationDbContext(optionsBuilder.Options);
    }

    private static string CreatePostgresConnectionString()
    {
        var configuredConnectionString =
            GetEnvironmentValue("ConnectionStrings__DefaultConnection") ??
            GetEnvironmentValue("DefaultConnection");

        if (!string.IsNullOrWhiteSpace(configuredConnectionString))
        {
            return configuredConnectionString;
        }

        var portValue = GetRequiredEnvironmentValue("DB_PORT", "Database__Port");

        if (!int.TryParse(portValue, out var port))
        {
            throw new InvalidOperationException("Database port must be a valid integer.");
        }

        return new NpgsqlConnectionStringBuilder
        {
            Host = GetRequiredEnvironmentValue("DB_HOST", "Database__Host"),
            Port = port,
            Database = GetRequiredEnvironmentValue("DB_NAME", "Database__Name"),
            Username = GetRequiredEnvironmentValue("DB_USERNAME", "Database__Username"),
            Password = GetEnvironmentValue("DB_PASSWORD", "Database__Password") ?? string.Empty
        }.ConnectionString;
    }

    private static void LoadDotEnv()
    {
        var currentDirectory = Directory.GetCurrentDirectory();
        var candidates = new[]
        {
            Path.Combine(currentDirectory, ".env"),
            Path.GetFullPath(Path.Combine(currentDirectory, "..", "..", ".env")),
            Path.GetFullPath(Path.Combine(currentDirectory, "src", "MarketFlow.Api", ".env"))
        };

        foreach (var path in candidates.Where(File.Exists))
        {
            foreach (var line in File.ReadAllLines(path))
            {
                var trimmedLine = line.Trim();

                if (string.IsNullOrWhiteSpace(trimmedLine) || trimmedLine.StartsWith('#'))
                {
                    continue;
                }

                var separatorIndex = trimmedLine.IndexOf('=', StringComparison.Ordinal);

                if (separatorIndex <= 0)
                {
                    continue;
                }

                var key = trimmedLine[..separatorIndex].Trim();
                var value = trimmedLine[(separatorIndex + 1)..].Trim().Trim('"');

                if (string.IsNullOrWhiteSpace(Environment.GetEnvironmentVariable(key)))
                {
                    Environment.SetEnvironmentVariable(key, value);
                }
            }
        }
    }

    private static string GetRequiredEnvironmentValue(params string[] names)
    {
        return GetEnvironmentValue(names)
            ?? throw new InvalidOperationException(
                $"Missing required database configuration. Set one of: {string.Join(", ", names)}.");
    }

    private static string? GetEnvironmentValue(params string[] names)
    {
        foreach (var name in names)
        {
            var value = Environment.GetEnvironmentVariable(name);

            if (!string.IsNullOrWhiteSpace(value))
            {
                return value;
            }
        }

        return null;
    }
}
