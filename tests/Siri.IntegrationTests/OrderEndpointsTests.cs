using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Text.Json;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Siri.IntegrationTests.Fixtures;
using Siri.Modules.Commerce.Application;
using Siri.Modules.Commerce.Domain;
using Siri.Modules.Commerce.Infrastructure;
using Siri.Modules.Identity.Domain;
using Siri.Modules.Identity.Features.Login;
using Siri.Modules.Identity.Infrastructure;
using Siri.Persistence;
using Siri.SharedKernel;

namespace Siri.IntegrationTests;

/// <summary>
/// Exercises <c>Siri.Api.Controllers.Commerce.OrdersController</c>/<c>TaxInvoicesController</c>
/// (D-19: MVC Controllers replaced Minimal API's <c>MapCommerceEndpoints()</c>) through the real
/// <see cref="SiriApiFactory"/> composition root, rather than a hand-rolled host wired to the now-dead
/// <c>MapCommerceEndpoints()</c>/<c>MapCatalogEndpoints()</c> extension methods.
/// </summary>
[Collection(ContainersCollection.Name)]
public sealed class OrderEndpointsTests : IAsyncLifetime
{
    private const string KnownPassword = "Correct-Horse-Battery-Staple-9";

    private static readonly JsonSerializerOptions JsonOptions = new(JsonSerializerDefaults.Web)
    {
        Converters = { new System.Text.Json.Serialization.JsonStringEnumConverter() },
    };

    private readonly ContainersFixture _containers;
    private SiriApiFactory _factory = null!;
    private HttpClient _client = null!;

    public OrderEndpointsTests(ContainersFixture containers)
    {
        _containers = containers;
    }

    public async Task InitializeAsync()
    {
        _factory = new SiriApiFactory(_containers);

        await using var scope = _factory.Services.CreateAsyncScope();
        var dbContext = scope.ServiceProvider.GetRequiredService<AppDbContext>();
        await dbContext.Database.MigrateAsync();

        _client = _factory.CreateClient();
    }

    public async Task DisposeAsync()
    {
        _client.Dispose();
        await _factory.DisposeAsync();
    }

    private static async Task<USER> CreateUserAsync(IServiceProvider services, AppDbContext dbContext, string email, string password)
    {
        var passwordHasher = services.GetRequiredService<IUserPasswordHasher>();
        var clock = services.GetRequiredService<IClock>();

        var normalizedEmail = email.ToUpperInvariant();
        var throwaway = USER.Register(email, normalizedEmail, "placeholder", "Test USER");
        var hash = passwordHasher.HashPassword(throwaway, password);
        var user = USER.Register(email, normalizedEmail, hash, "Test USER");
        user.ConfirmEmail(clock);

        dbContext.Users().Add(user);
        await dbContext.SaveChangesAsync();

        return user;
    }

    private static async Task<string> LoginAndGetAccessTokenAsync(IServiceProvider services, string email)
    {
        var loginHandler = services.GetRequiredService<LoginHandler>();
        var loginResult = await loginHandler.HandleAsync(
            new LoginCommand(email, KnownPassword, "TestDevice", "Test Agent"),
            "UA",
            "127.0.0.1",
            CancellationToken.None);

        if (!loginResult.IsSuccess)
        {
            throw new InvalidOperationException($"Login failed: {loginResult.Error.Code}");
        }

        return loginResult.Value.AccessToken;
    }

    [Fact]
    public async Task ListOrders_WhenUnauthorized_Returns401()
    {
        var response = await _client.GetAsync("/api/commerce/orders");
        Assert.Equal(HttpStatusCode.Unauthorized, response.StatusCode);
    }

    [Fact]
    public async Task ListOrders_WhenAuthorized_ReturnsPagedOrdersForCurrentUserOnly()
    {
        await using var scope = _factory.Services.CreateAsyncScope();
        var dbContext = scope.ServiceProvider.GetRequiredService<AppDbContext>();
        var clock = scope.ServiceProvider.GetRequiredService<IClock>();

        var user1 = await CreateUserAsync(_factory.Services, dbContext, $"user1_{Guid.NewGuid():N}@test.local", KnownPassword);
        var user2 = await CreateUserAsync(_factory.Services, dbContext, $"user2_{Guid.NewGuid():N}@test.local", KnownPassword);

        var order1 = ORDER.Create($"SU-{Guid.NewGuid().ToString("N")[..8]}", user1.Id, 1000m, 100m, 58.88m, 900m);
        order1.AddItem(Guid.NewGuid(), "Angular Pro COURSE", 1000m, 900m);
        order1.MarkAwaitingPayment();
        order1.MarkPaid(clock);

        var order2 = ORDER.Create($"SU-{Guid.NewGuid().ToString("N")[..8]}", user2.Id, 2000m, 0m, 130.84m, 2000m);
        order2.AddItem(Guid.NewGuid(), "DotNet 10 Masterclass", 2000m, 2000m);

        dbContext.Orders().AddRange(order1, order2);
        await dbContext.SaveChangesAsync();

        var token = await LoginAndGetAccessTokenAsync(_factory.Services, user1.Email);
        using var request = new HttpRequestMessage(HttpMethod.Get, "/api/commerce/orders?page=1&pageSize=10");
        request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", token);

        var response = await _client.SendAsync(request);
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);

        var pagedResult = await response.Content.ReadFromJsonAsync<PagedResult<OrderResponse>>(JsonOptions);
        Assert.NotNull(pagedResult);
        Assert.Contains(pagedResult!.Items, o => o.Id == order1.ORDER_ID);
        Assert.DoesNotContain(pagedResult.Items, o => o.Id == order2.ORDER_ID);
    }

    [Fact]
    public async Task GetOrderById_WhenNotOwnOrder_Returns404NotFound()
    {
        await using var scope = _factory.Services.CreateAsyncScope();
        var dbContext = scope.ServiceProvider.GetRequiredService<AppDbContext>();

        var user1 = await CreateUserAsync(_factory.Services, dbContext, $"owner_{Guid.NewGuid():N}@test.local", KnownPassword);
        var user2 = await CreateUserAsync(_factory.Services, dbContext, $"stranger_{Guid.NewGuid():N}@test.local", KnownPassword);

        var order = ORDER.Create($"SU-{Guid.NewGuid().ToString("N")[..8]}", user1.Id, 1000m, 0m, 65.42m, 1000m);
        dbContext.Orders().Add(order);
        await dbContext.SaveChangesAsync();

        var strangerToken = await LoginAndGetAccessTokenAsync(_factory.Services, user2.Email);
        using var request = new HttpRequestMessage(HttpMethod.Get, $"/api/commerce/orders/{order.ORDER_ID}");
        request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", strangerToken);

        var response = await _client.SendAsync(request);
        Assert.Equal(HttpStatusCode.NotFound, response.StatusCode);
    }

    [Fact]
    public async Task GetTaxInvoiceByOrder_WhenInvoiceExists_ReturnsTaxInvoice()
    {
        await using var scope = _factory.Services.CreateAsyncScope();
        var dbContext = scope.ServiceProvider.GetRequiredService<AppDbContext>();
        var clock = scope.ServiceProvider.GetRequiredService<IClock>();

        var user = await CreateUserAsync(_factory.Services, dbContext, $"taxuser_{Guid.NewGuid():N}@test.local", KnownPassword);
        var order = ORDER.Create($"SU-{Guid.NewGuid().ToString("N")[..8]}", user.Id, 1070m, 0m, 70m, 1070m);
        order.MarkAwaitingPayment();
        order.MarkPaid(clock);
        dbContext.Orders().Add(order);

        var invoice = TAX_INVOICE.Issue(order.ORDER_ID, "0105558123456", "Test Company Ltd", "INV-260825-999", clock);
        dbContext.TaxInvoices().Add(invoice);
        await dbContext.SaveChangesAsync();

        var token = await LoginAndGetAccessTokenAsync(_factory.Services, user.Email);
        using var request = new HttpRequestMessage(HttpMethod.Get, $"/api/commerce/tax-invoices/by-order/{order.ORDER_ID}");
        request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", token);

        var response = await _client.SendAsync(request);
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);

        var invoiceResponse = await response.Content.ReadFromJsonAsync<TaxInvoiceResponse>(JsonOptions);
        Assert.NotNull(invoiceResponse);
        Assert.Equal("INV-260825-999", invoiceResponse!.InvoiceNo);
        Assert.Equal("Test Company Ltd", invoiceResponse.BuyerName);
    }
}
