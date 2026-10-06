using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Text.Json;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Siri.IntegrationTests.Fixtures;
using Siri.IntegrationTests.TestData;
using Siri.Modules.Commerce.Application;
using Siri.Modules.Identity.Features.Login;
using Siri.Persistence;

namespace Siri.IntegrationTests;

/// <summary>
/// P11-09 §5 integration checklist: <c>GET /api/commerce/payments/config</c> must be proven through
/// <see cref="SiriApiFactory"/> (the real <c>Program.cs</c> composition root, real MVC routing via
/// <c>PaymentsController</c>) rather than the legacy <c>_app.MapCommerceEndpoints()</c> minimal-API
/// harness other Commerce tests still use — this is the one place that proves the MVC action is
/// actually reachable end to end and that <see cref="Siri.Modules.Commerce.PaymentOptions"/> binds
/// from real configuration, not just that the handler method behaves correctly in isolation.
/// </summary>
[Collection(ContainersCollection.Name)]
public sealed class PaymentConfigIntegrationTests : IAsyncLifetime
{
    private static readonly JsonSerializerOptions JsonOptions = new(JsonSerializerDefaults.Web)
    {
        Converters = { new System.Text.Json.Serialization.JsonStringEnumConverter() },
    };

    private readonly ContainersFixture _containers;
    private SiriApiFactory _factory = null!;

    public PaymentConfigIntegrationTests(ContainersFixture containers)
    {
        _containers = containers;
    }

    public async Task InitializeAsync()
    {
        _factory = new SiriApiFactory(_containers);

        await using var scope = _factory.Services.CreateAsyncScope();
        var dbContext = scope.ServiceProvider.GetRequiredService<AppDbContext>();
        await dbContext.Database.MigrateAsync();
    }

    public async Task DisposeAsync()
    {
        await _factory.DisposeAsync();
    }

    [Fact]
    public async Task GetConfig_DefaultConfiguration_ReturnsPromptPayOnlyAndPublishableKey()
    {
        var client = _factory.CreateClient();
        var accessToken = await LoginAsNewUserAsync(_factory.Services, client);
        client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", accessToken);

        var response = await client.GetAsync("/api/commerce/payments/config");

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        var body = await response.Content.ReadFromJsonAsync<PaymentConfigResponse>(JsonOptions);
        Assert.NotNull(body);
        Assert.False(string.IsNullOrWhiteSpace(body.PublishableKey));
        Assert.Equal(["PromptPay"], body.EnabledMethods.Select(m => m.ToString()));
    }

    [Fact]
    public async Task GetConfig_WithoutBearerToken_Returns401ThroughRealPipeline()
    {
        var client = _factory.CreateClient();

        var response = await client.GetAsync("/api/commerce/payments/config");

        Assert.Equal(HttpStatusCode.Unauthorized, response.StatusCode);
    }

    [Fact]
    public async Task GetConfig_PaymentEnabledMethodsOverriddenToIncludeCard_ReturnsBothMethods()
    {
        await using var cardFactory = _factory.WithWebHostBuilder(builder =>
        {
            builder.UseSetting("Payment:EnabledMethods:0", "PromptPay");
            builder.UseSetting("Payment:EnabledMethods:1", "Card");
        });

        var client = cardFactory.CreateClient();
        var accessToken = await LoginAsNewUserAsync(cardFactory.Services, client);
        client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", accessToken);

        var response = await client.GetAsync("/api/commerce/payments/config");

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        var body = await response.Content.ReadFromJsonAsync<PaymentConfigResponse>(JsonOptions);
        Assert.NotNull(body);
        Assert.Equal(["PromptPay", "Card"], body.EnabledMethods.Select(m => m.ToString()));
    }

    /// <summary>Seeds a fresh confirmed user directly against the given factory's DI container (same
    /// pattern as <see cref="Siri.IntegrationTests.ApiHostSmokeTests"/>), then logs in over real HTTP
    /// through <paramref name="client"/> to obtain a bearer-usable access token. A new user is seeded
    /// per call so the two factories in <see cref="GetConfig_PaymentEnabledMethodsOverriddenToIncludeCard_ReturnsBothMethods"/>
    /// (sharing the same Testcontainers database) never collide on email.</summary>
    private static async Task<string> LoginAsNewUserAsync(IServiceProvider services, HttpClient client)
    {
        var builder = new TestUserBuilder();
        await using (var scope = services.CreateAsyncScope())
        {
            await builder.BuildAsync(scope.ServiceProvider);
        }

        var response = await client.PostAsJsonAsync("/api/identity/login", new
        {
            email = builder.Email,
            password = builder.Password,
            deviceId = "payment-config-test-device",
            deviceName = "Payment Config Test Device",
        });

        response.EnsureSuccessStatusCode();
        var body = await response.Content.ReadFromJsonAsync<LoginResponse>(JsonOptions);
        Assert.NotNull(body);
        return body.AccessToken;
    }
}
