using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Siri.IntegrationTests.Fixtures;
using Siri.Persistence;

namespace Siri.IntegrationTests;

/// <summary>
/// Real data only: with no Bunny / Stripe configuration (the shipped appsettings carry none) the host
/// still boots through the real <c>Program.cs</c>, but provider-dependent endpoints answer 503
/// ProblemDetails with a stable <c>errorCode</c> instead of faking a response or trusting a publicly-known
/// placeholder secret. Both webhook endpoints are checked here because they are anonymous and
/// signature-authenticated — a placeholder secret would let anyone forge a "valid" signature.
/// </summary>
[Collection(ContainersCollection.Name)]
public sealed class ProviderNotConfiguredIntegrationTests : IAsyncLifetime
{
    private readonly ContainersFixture _containers;
    private SiriApiFactory _factory = null!;

    public ProviderNotConfiguredIntegrationTests(ContainersFixture containers)
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

    private static async Task<string?> ReadErrorCodeAsync(HttpResponseMessage response)
    {
        using var problem = JsonDocument.Parse(await response.Content.ReadAsStringAsync());
        return problem.RootElement.GetProperty("errorCode").GetString();
    }

    [Theory]
    [InlineData("")]
    [InlineData("CHANGE_ME_DEV_ONLY_bunny_readonly_api_key")]
    public async Task BunnyWebhook_ReadOnlyKeyMissingOrPlaceholder_Returns503(string readOnlyApiKey)
    {
        await using var factory = _factory.WithWebHostBuilder(builder =>
            builder.UseSetting("VideoProvider:ReadOnlyApiKey", readOnlyApiKey));
        var client = factory.CreateClient();
        using var request = new HttpRequestMessage(HttpMethod.Post, "/api/media/webhooks/bunny")
        {
            Content = JsonContent.Create(new { VideoLibraryId = 12345, VideoGuid = Guid.NewGuid().ToString(), Status = 3 }),
        };
        request.Headers.Add("X-BunnyStream-Signature-Version", "v1");
        request.Headers.Add("X-BunnyStream-Signature-Algorithm", "hmac-sha256");
        request.Headers.Add("X-BunnyStream-Signature", new string('a', 64));

        var response = await client.SendAsync(request);

        Assert.Equal(HttpStatusCode.ServiceUnavailable, response.StatusCode);
        Assert.Equal("video.provider_not_configured", await ReadErrorCodeAsync(response));
    }

    [Theory]
    [InlineData("")]
    [InlineData("CHANGE_ME_DEV_ONLY_whsec_placeholder_key")]
    public async Task StripeWebhook_SecretMissingOrPlaceholder_Returns503(string webhookSecret)
    {
        await using var factory = _factory.WithWebHostBuilder(builder =>
            builder.UseSetting("Payment:Stripe:WebhookSecret", webhookSecret));
        var client = factory.CreateClient();
        using var request = new HttpRequestMessage(HttpMethod.Post, "/api/commerce/webhooks/stripe")
        {
            Content = new StringContent("{}", System.Text.Encoding.UTF8, "application/json"),
        };
        request.Headers.Add("Stripe-Signature", "t=1700000000,v1=00");

        var response = await client.SendAsync(request);

        Assert.Equal(HttpStatusCode.ServiceUnavailable, response.StatusCode);
        Assert.Equal("payment.provider_not_configured", await ReadErrorCodeAsync(response));
    }
}
