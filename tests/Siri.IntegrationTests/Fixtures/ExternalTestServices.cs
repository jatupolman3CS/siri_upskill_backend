using System.Net;
using System.Security.Cryptography;
using Npgsql;
using StackExchange.Redis;

namespace Siri.IntegrationTests.Fixtures;

/// <summary>
/// Opt-in "external services" mode for the integration suite: instead of Testcontainers (Docker), use a
/// PostgreSQL server and a Redis-protocol server that are already running on this machine — the project's own
/// native dev stack (<c>scripts/dev.ps1</c>: PostgreSQL on 127.0.0.1:5433, Garnet on 127.0.0.1:6380).
/// <para>
/// Enabled only when <see cref="PostgresAdminConnectionEnvVar"/> is set; with it unset every fixture behaves
/// exactly as before (Testcontainers).
/// </para>
/// <list type="bullet">
/// <item><see cref="PostgresAdminConnectionEnvVar"/> — Npgsql connection string of a role that can create
/// roles/databases (the dev cluster's admin role), pointed at the maintenance database, e.g.
/// <c>Host=127.0.0.1;Port=5433;Database=postgres;Username=siri_dev_admin;Password=…</c>.</item>
/// <item><see cref="RedisEnvVar"/> — StackExchange.Redis configuration string that MUST name a dedicated,
/// non-zero database index so test keys can never collide with (or flush) dev data, e.g.
/// <c>127.0.0.1:6380,defaultDatabase=14</c>.</item>
/// </list>
/// <para>
/// PowerShell, from the repo root, with ONLY the dev PostgreSQL (<c>pg_ctl -D .dev\postgres start</c>) and Garnet
/// (<c>.dev\tools\garnet\net10.0\GarnetServer.exe --bind 127.0.0.1 --port 6380</c>) running — the password is read from the
/// git-ignored <c>.dev\settings.json</c> into this process's environment only and is never echoed:
/// <code>
/// $s = Get-Content .dev\settings.json -Raw | ConvertFrom-Json; $env:SIRI_IT_POSTGRES_ADMIN_CONNECTION = "Host=127.0.0.1;Port=5433;Database=postgres;Username=siri_dev_admin;Password=$($s.AdminPassword)"; $env:SIRI_IT_REDIS = '127.0.0.1:6380,defaultDatabase=14,abortConnect=false'; dotnet test tests/Siri.IntegrationTests -c Release
/// </code>
/// </para>
/// <para>
/// <b>Safety.</b> The backend <c>.env</c> points the app at a remote shared database; this mode must never be
/// able to reach anything except local throwaway state. So (1) every endpoint named by either env var must be
/// a loopback address — anything else is refused before a single connection is opened; (2) each fixture gets
/// its OWN freshly created <c>siri_it_&lt;random&gt;</c> database owned by its OWN freshly created,
/// non-superuser <c>siri_it_&lt;random&gt;</c> role with a random password — the application under test never
/// sees the admin credentials, and (like production's <c>siriupskill_app</c>) it owns only its own database; (3)
/// only that database/role are dropped on dispose and only the dedicated Redis database is flushed.
/// </para>
/// </summary>
public static class ExternalTestServices
{
    public const string PostgresAdminConnectionEnvVar = "SIRI_IT_POSTGRES_ADMIN_CONNECTION";
    public const string RedisEnvVar = "SIRI_IT_REDIS";

    /// <summary>True when the opt-in mode is requested via environment.</summary>
    public static bool PostgresRequested => !string.IsNullOrWhiteSpace(
        Environment.GetEnvironmentVariable(PostgresAdminConnectionEnvVar));

    /// <summary>Throws unless <paramref name="host"/> is a literal loopback address or <c>localhost</c>.
    /// No DNS lookups: a hostname that merely resolves to loopback today is not trusted.</summary>
    public static void AssertLoopbackHost(string? host, string what)
    {
        if (string.IsNullOrWhiteSpace(host) ||
            !(string.Equals(host, "localhost", StringComparison.OrdinalIgnoreCase) ||
              (IPAddress.TryParse(host, out var address) && IPAddress.IsLoopback(address))))
        {
            throw new InvalidOperationException(
                $"External test services mode refuses {what}: host '{host}' is not a loopback address " +
                "(127.0.0.1 / ::1 / localhost). The integration suite must never touch a remote database or cache.");
        }
    }

    /// <summary>Creates a throwaway role + database on the admin server and returns a handle that owns them.</summary>
    public static async Task<ExternalPostgresDatabase> CreatePostgresDatabaseAsync()
    {
        var adminConnectionString = Environment.GetEnvironmentVariable(PostgresAdminConnectionEnvVar);
        if (string.IsNullOrWhiteSpace(adminConnectionString))
        {
            throw new InvalidOperationException($"{PostgresAdminConnectionEnvVar} is not set.");
        }

        var admin = new NpgsqlConnectionStringBuilder(adminConnectionString);
        // A comma-separated multi-host value is not a single loopback literal and is rejected here too.
        AssertLoopbackHost(admin.Host, $"{PostgresAdminConnectionEnvVar}");

        var suffix = Guid.NewGuid().ToString("N")[..12];
        var name = $"siri_it_{suffix}";
        var password = Convert.ToHexString(RandomNumberGenerator.GetBytes(16));

        var app = new NpgsqlConnectionStringBuilder
        {
            Host = admin.Host,
            Port = admin.Port,
            Database = name,
            Username = name,
            Password = password,
        };

        await using (var connection = new NpgsqlConnection(adminConnectionString))
        {
            await connection.OpenAsync();

            // Identifiers are generated here (hex only) and the password is hex — no user input reaches this SQL.
            await ExecuteAsync(connection,
                $"CREATE ROLE \"{name}\" LOGIN NOSUPERUSER NOCREATEDB NOCREATEROLE PASSWORD '{password}'");
            await ExecuteAsync(connection, $"CREATE DATABASE \"{name}\" OWNER \"{name}\"");
        }

        return new ExternalPostgresDatabase(adminConnectionString, name, app.ConnectionString);
    }

    /// <summary>Parses and validates the Redis env var; the returned options point at the dedicated database.</summary>
    public static ConfigurationOptions ParseRedis(string? connectionString)
    {
        if (string.IsNullOrWhiteSpace(connectionString))
        {
            throw new InvalidOperationException($"{RedisEnvVar} is not set.");
        }

        var options = ConfigurationOptions.Parse(connectionString);
        if (options.EndPoints.Count == 0)
        {
            throw new InvalidOperationException($"{RedisEnvVar} names no endpoint.");
        }

        foreach (var endpoint in options.EndPoints)
        {
            var host = endpoint switch
            {
                DnsEndPoint dns => dns.Host,
                IPEndPoint ip => ip.Address.ToString(),
                _ => null,
            };
            AssertLoopbackHost(host, $"{RedisEnvVar}");
        }

        if (options.DefaultDatabase is null or 0)
        {
            throw new InvalidOperationException(
                $"{RedisEnvVar} must name a dedicated non-zero database (e.g. ',defaultDatabase=14') so test keys " +
                "can never collide with dev data in database 0.");
        }

        return options;
    }

    /// <summary>Flushes ONLY the dedicated Redis database (never FLUSHALL).</summary>
    public static async Task FlushDedicatedRedisDatabaseAsync(ConfigurationOptions options)
    {
        var admin = options.Clone();
        admin.AllowAdmin = true;
        admin.AbortOnConnectFail = false;
        await using var multiplexer = await ConnectionMultiplexer.ConnectAsync(admin);
        foreach (var endpoint in multiplexer.GetEndPoints())
        {
            await multiplexer.GetServer(endpoint).FlushDatabaseAsync(options.DefaultDatabase!.Value);
        }
    }

    private static async Task ExecuteAsync(NpgsqlConnection connection, string sql)
    {
        await using var command = new NpgsqlCommand(sql, connection);
        await command.ExecuteNonQueryAsync();
    }

    /// <summary>A throwaway role + database that are dropped by <see cref="DisposeAsync"/>.</summary>
    public sealed class ExternalPostgresDatabase(string adminConnectionString, string name, string connectionString)
        : IAsyncDisposable
    {
        /// <summary>Connection string of the throwaway, non-superuser owner role (what the app under test uses).</summary>
        public string ConnectionString { get; } = connectionString;

        public async ValueTask DisposeAsync()
        {
            // Pooled connections of the app role would hold the database open; FORCE terminates them.
            NpgsqlConnection.ClearAllPools();
            try
            {
                await using var connection = new NpgsqlConnection(adminConnectionString);
                await connection.OpenAsync();
                await ExecuteAsync(connection, $"DROP DATABASE IF EXISTS \"{name}\" WITH (FORCE)");
                await ExecuteAsync(connection, $"DROP ROLE IF EXISTS \"{name}\"");
            }
            catch (Exception exception) when (exception is NpgsqlException or InvalidOperationException)
            {
                // Cleanup is best effort — surface it on stderr without ever failing the run for it.
                Console.Error.WriteLine(
                    $"[ExternalTestServices] could not drop throwaway database/role '{name}': {exception.GetType().Name}");
            }
        }
    }
}
