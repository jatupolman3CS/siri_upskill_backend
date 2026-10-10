using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;

namespace Siri.IntegrationTests.Fixtures;

/// <summary>
/// Boots the REAL <c>Siri.Api</c> host — <c>Program.cs</c> top to bottom: Serilog, JwtBearer wiring,
/// every module registration, Hangfire, the full middleware pipeline and endpoint routing — against
/// the throwaway Testcontainers MSSQL/Redis instances from <see cref="ContainersFixture"/> (task
/// P0-20's WebApplicationFactory piece).
/// <para>
/// <b>Why this is safe against the timing concern documented in
/// <c>AuthorizationPolicyHttpTests</c>:</b> that file avoided <c>WebApplicationFactory</c> because,
/// if configuration overrides landed <i>after</i> <c>Program.cs</c>'s own top-level configuration
/// reads, the host could silently boot against whatever <c>ConnectionStrings:Default</c> the machine
/// already has (this repo's dev user-secrets point at the real Contabo SQL Server — CLAUDE.md). This
/// factory closes that hole three ways, in order of mechanism:
/// </para>
/// <list type="number">
/// <item><b>Overrides ride in as command-line arguments.</b> Every value below is set via
/// <see cref="Microsoft.AspNetCore.Hosting.HostingAbstractionsWebHostBuilderExtensions.UseSetting"/>,
/// never <c>ConfigureAppConfiguration</c>. For minimal-hosting apps, Mvc.Testing's
/// <c>DeferredHostBuilder</c> forwards host-configuration settings to the app's entry point as
/// <c>--key=value</c> command-line arguments (its source states: "Hosting configuration is being
/// provided by args so that we can impact WebApplicationBuilder based apps"), and
/// <c>WebApplication.CreateBuilder(args)</c> registers the command-line provider as the
/// highest-precedence configuration source. So these values exist — and win over user-secrets —
/// from the very first line of <c>Program.cs</c>, including its top-level JWT signing-key check and
/// <c>AddWorkers</c>' connection-string read. <c>ConfigureAppConfiguration</c> callbacks, by
/// contrast, are only replayed at host-build time, i.e. after those reads — that path stays unused
/// here on purpose.</item>
/// <item><b>The environment is <c>IntegrationTest</c>, not <c>Development</c>.</b>
/// <c>WebApplicationFactory</c>'s default is <c>Development</c>, which is precisely what would make
/// <c>WebApplication.CreateBuilder</c> load this machine's user-secrets (they are only ever loaded
/// in Development). Under <c>IntegrationTest</c> the real Contabo connection string is never even
/// present in the configuration stack — the committed <c>appsettings.json</c> placeholders plus the
/// args above are all there is.</item>
/// <item><b>A fail-fast guard that runs before any connection can exist.</b> The
/// <c>ConfigureServices</c> callback below executes during host build — after <c>Program.cs</c>'s
/// registrations, but before the <c>ServiceProvider</c> is built, and therefore before EF Core opens
/// a connection (first query) or Hangfire's storage does (<c>IRecurringJobManager</c> resolution in
/// <c>Program.cs</c>, which happens after <c>Build()</c>). If either mechanism above ever regresses,
/// the host refuses to boot with a loud error instead of touching a non-container database.</item>
/// </list>
/// </summary>
public sealed class SiriApiFactory : WebApplicationFactory<Program>
{
    /// <summary>Never "Development" — see this class's doc comment, mechanism 2.</summary>
    public const string EnvironmentName = "IntegrationTest";

    private readonly ContainersFixture _containers;
    private readonly IReadOnlyDictionary<string, string?> _extraSettings;

    /// <param name="extraSettings">Additional configuration for tests that need a non-default feature setup (for example
    /// <c>Live:Provider=Logging</c>). Applied with the same <c>UseSetting</c> mechanism as everything else here, after the defaults.</param>
    public SiriApiFactory(ContainersFixture containers, IReadOnlyDictionary<string, string?>? extraSettings = null)
    {
        _containers = containers;
        _extraSettings = extraSettings ?? new Dictionary<string, string?>();
    }

    protected override void ConfigureWebHost(IWebHostBuilder builder)
    {
        builder.UseEnvironment(EnvironmentName);

        // Same keys/shape the self-contained integration tests (LoginAndRefreshTests.cs etc.)
        // already use for the same module registrations — kept value-identical where possible so a
        // behavior difference between the two harness styles can never come from configuration.
        builder.UseSetting("ConnectionStrings:Default", _containers.SqlConnectionString);
        builder.UseSetting("Redis:ConnectionString", _containers.RedisConnectionString);
        builder.UseSetting("Identity:EmailConfirmation:ConfirmEmailUrl", "https://example.test/confirm-email");
        builder.UseSetting("Identity:PasswordReset:ResetPasswordUrl", "https://example.test/reset-password");
        builder.UseSetting("Identity:Security:MaxConcurrentSessions", "2");
        builder.UseSetting("Identity:Jwt:Issuer", "https://api.siriupskill.test");
        builder.UseSetting("Identity:Jwt:Audience", "siriupskill-frontend-test");
        builder.UseSetting("Identity:Jwt:SigningKey", new string('k', 64));
        builder.UseSetting("Identity:Jwt:AccessTokenLifetimeMinutes", "15");
        builder.UseSetting("Email:Provider", "Log"); // never a real SMTP send
        builder.UseSetting("Seo:PublicBaseUrl", "https://example.test");

        // Commerce + Payout are unconditionally registered by Program.cs (D-17) — their
        // ValidateOnStart()-gated Options need real-looking values for the host to boot at all,
        // regardless of whether a given test file actually exercises Stripe/payout behavior.
        // Values are test placeholders only, never real keys (security.md). Key names must match
        // each Options class's SectionName exactly: StripeOptions binds "Payment:Stripe" (NOT
        // "Stripe" — a mismatch some older hand-rolled test hosts used, harmless there only because
        // CommerceModule.AddCommerceModule falls back to its own placeholder when the section binds
        // empty).
        builder.UseSetting("Payment:Stripe:SecretKey", "sk_test_placeholder_key_for_testing_purposes_only");
        builder.UseSetting("Payment:Stripe:PublishableKey", "pk_test_placeholder_key_for_testing_purposes_only");
        builder.UseSetting("Payment:Stripe:WebhookSecret", "whsec_test_placeholder_webhook_secret_for_tests");
        builder.UseSetting("Commerce:OrderExpiry:ExpiryMinutes", "30");
        builder.UseSetting("Commerce:OrderExpiry:BatchSize", "50");
        builder.UseSetting("Payout:WithholdingTaxPercent", "3.00");
        builder.UseSetting("Payout:PayerCompanyName", "SIRI UPSKILL CO., LTD.");
        builder.UseSetting("Payout:PayerTaxId", "0105566000000");
        builder.UseSetting("Payout:PayerAddress", "Bangkok, Thailand");

        // No background jobs in the test host: the API would otherwise run a Hangfire server (Hangfire:ServerInApi defaults to true) that starts
        // executing recurring jobs against the throwaway database on its own schedule and makes every test non-deterministic. Tests run the jobs they
        // care about by hand (they resolve the job class and call RunAsync). A test of the hosting itself opts in through extraSettings.
        builder.UseSetting("Hangfire:ServerInApi", "false");

        foreach (var (key, value) in _extraSettings)
        {
            builder.UseSetting(key, value);
        }

        builder.ConfigureServices((context, _) =>
        {
            // Fail-fast guard (mechanism 3 in the class doc comment). Deliberately compares against
            // the container's exact connection string rather than just "not the real server" — any
            // unexpected value at all, placeholder included, means the args mechanism broke and this
            // harness can no longer vouch for where the host would connect.
            var actual = context.Configuration.GetConnectionString("Default");
            if (!string.Equals(actual, _containers.SqlConnectionString, StringComparison.Ordinal))
            {
                throw new InvalidOperationException(
                    "SiriApiFactory refuses to boot: 'ConnectionStrings:Default' is not the Testcontainers " +
                    "MSSQL connection string, so the UseSetting→command-line-args override mechanism this " +
                    "factory depends on has regressed. Aborting before the ServiceProvider is built — no " +
                    "code path has opened a database connection yet. See SiriApiFactory's doc comment.");
            }

            // External services mode (no Docker): the fixture already refused non-loopback endpoints before
            // creating anything; assert it again on what the host will REALLY use, right before it can connect.
            if (_containers.IsExternal)
            {
                ExternalTestServices.AssertLoopbackHost(
                    new Npgsql.NpgsqlConnectionStringBuilder(actual).Host, "ConnectionStrings:Default");
                foreach (var endpoint in ExternalTestServices.ParseRedis(
                             context.Configuration["Redis:ConnectionString"]).EndPoints)
                {
                    ExternalTestServices.AssertLoopbackHost(
                        endpoint is System.Net.DnsEndPoint dns ? dns.Host : (endpoint as System.Net.IPEndPoint)?.Address.ToString(),
                        "Redis:ConnectionString");
                }
            }

            if (!context.HostingEnvironment.IsEnvironment(EnvironmentName))
            {
                throw new InvalidOperationException(
                    $"SiriApiFactory refuses to boot: environment is '{context.HostingEnvironment.EnvironmentName}' " +
                    $"instead of '{EnvironmentName}'. Development would load this machine's user-secrets " +
                    "(real Contabo connection string — CLAUDE.md). See SiriApiFactory's doc comment.");
            }
        });
    }
}
