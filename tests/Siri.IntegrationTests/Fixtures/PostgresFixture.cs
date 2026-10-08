using System.Net;
using System.Reflection;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Npgsql;
using Siri.Modules.Notification;
using Siri.Persistence;
using Siri.Persistence.DependencyInjection;
using Testcontainers.PostgreSql;

namespace Siri.IntegrationTests.Fixtures;

/// <summary>A real PostgreSQL fixture for module integration tests that do not require Redis.</summary>
public sealed class PostgresFixture : IAsyncLifetime
{
    private PostgreSqlContainer? _container;
    private ExternalTestServices.ExternalPostgresDatabase? _externalDatabase;
    private ServiceProvider _services = null!;

    public IServiceScope CreateScope() => _services.CreateScope();

    public async Task InitializeAsync()
    {
        // Opt-in external mode (see ExternalTestServices): a throwaway local database per fixture instance.
        string? connectionString;
        if (ExternalTestServices.PostgresRequested)
        {
            _externalDatabase = await ExternalTestServices.CreatePostgresDatabaseAsync();
            connectionString = _externalDatabase.ConnectionString;
        }
        else
        {
            connectionString = Environment.GetEnvironmentVariable("SIRI_TEST_POSTGRES_CONNECTION");
        }

        if (string.IsNullOrWhiteSpace(connectionString))
        {
            _container = new PostgreSqlBuilder("postgres:17-alpine").Build();
            await _container.StartAsync();
            connectionString = _container.GetConnectionString();
        }
        else if (_externalDatabase is null)
        {
            var connection = new NpgsqlConnectionStringBuilder(connectionString);
            if (!IPAddress.TryParse(connection.Host, out var address) || !IPAddress.IsLoopback(address) ||
                connection.Database?.StartsWith("siri_test_", StringComparison.Ordinal) != true)
            {
                throw new InvalidOperationException(
                    "SIRI_TEST_POSTGRES_CONNECTION must use a loopback IP and a database named siri_test_*.");
            }
        }

        // AppDbContext discovers module configurations from loaded assemblies.
        foreach (var assembly in Directory.EnumerateFiles(AppContext.BaseDirectory, "Siri.Modules.*.dll"))
        {
            Assembly.LoadFrom(assembly);
        }

        var configuration = new ConfigurationBuilder().AddInMemoryCollection(new Dictionary<string, string?>
        {
            ["ConnectionStrings:Default"] = connectionString,
        }).Build();
        var services = new ServiceCollection();
        services.AddLogging();
        services.AddPersistence(configuration);
        services.AddNotificationModule(configuration);
        _services = services.BuildServiceProvider();

        using var scope = CreateScope();
        await scope.ServiceProvider.GetRequiredService<AppDbContext>().Database.MigrateAsync();
    }

    public async Task DisposeAsync()
    {
        if (_services is not null) await _services.DisposeAsync();
        if (_container is not null) await _container.DisposeAsync();
        if (_externalDatabase is not null) await _externalDatabase.DisposeAsync();
    }
}
