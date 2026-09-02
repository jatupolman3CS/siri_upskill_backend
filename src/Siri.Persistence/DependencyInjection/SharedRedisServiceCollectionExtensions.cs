using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Options;
using StackExchange.Redis;

namespace Siri.Persistence.DependencyInjection;

public static class SharedRedisServiceCollectionExtensions
{
    /// <summary>
    /// Registers the app-wide <see cref="IConnectionMultiplexer"/> singleton, shared by every module
    /// that needs Redis (today: Identity's session mirror, Catalog's category-tree cache). Call once,
    /// from <c>Siri.Api/Program.cs</c>, after <c>AddPersistence</c> and before any module registration
    /// that resolves <see cref="IConnectionMultiplexer"/>.
    /// </summary>
    public static IServiceCollection AddSharedRedis(this IServiceCollection services, IConfiguration configuration)
    {
        // ValidateOnStart() only checks the *configuration value* is present — it does not attempt a
        // real TCP connection at host startup (that only happens lazily, the first time
        // IConnectionMultiplexer below is actually resolved), so a missing/malformed config value
        // fails fast at boot the same way ConnectionStrings:Default/Jwt:SigningKey do, but an
        // unreachable Redis *server* does not block startup.
        services.AddOptions<RedisOptions>()
            .Bind(configuration.GetSection(RedisOptions.SectionName))
            .ValidateDataAnnotations()
            .ValidateOnStart();

        // Singleton: IConnectionMultiplexer is explicitly documented by StackExchange.Redis as safe
        // and intended to be shared/reused across the app's lifetime, never created per-request (and
        // never one-per-module — every consumer resolves this exact same instance).
        // AbortOnConnectFail = false is the load-bearing part of the fail-open design SE-03 (Identity's
        // session mirror) established and every subsequent Redis consumer inherits — it means
        // Connect() below returns a (possibly not-yet-connected) multiplexer instead of throwing when
        // Redis is temporarily unreachable, so resolving it can never itself turn "Redis is down" into
        // a request failure. Each consumer's own per-call try/catch (e.g. RedisSessionRegistry,
        // CategoryTreeCache) is what handles the still-possible command-level failures once resolution
        // succeeds.
        services.AddSingleton<IConnectionMultiplexer>(sp =>
        {
            var redisOptions = sp.GetRequiredService<IOptions<RedisOptions>>().Value;
            var configurationOptions = ConfigurationOptions.Parse(redisOptions.ConnectionString);
            configurationOptions.AbortOnConnectFail = false;
            configurationOptions.ConnectTimeout = 1000;
            configurationOptions.SyncTimeout = 1000;
            configurationOptions.AsyncTimeout = 1000;
            return ConnectionMultiplexer.Connect(configurationOptions);
        });

        return services;
    }
}
