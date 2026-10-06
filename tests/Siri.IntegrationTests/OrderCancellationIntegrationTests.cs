using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Text.Json;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Siri.IntegrationTests.Fixtures;
using Siri.IntegrationTests.TestData;
using Siri.Modules.Catalog.Domain;
using Siri.Modules.Catalog.Infrastructure;
using Siri.Modules.Commerce.Application;
using Siri.Modules.Commerce.Domain;
using Siri.Modules.Commerce.Infrastructure;
using Siri.Modules.Identity.Features.Login;
using Siri.Persistence;
using Siri.SharedKernel;

namespace Siri.IntegrationTests;

/// <summary>
/// <c>POST /api/commerce/orders/{orderId}/cancel</c> — exercised through <see cref="SiriApiFactory"/>
/// (the real <c>Program.cs</c> composition root, real MVC routing via <see cref="Siri.Api.Controllers.Commerce.OrdersController"/>),
/// same reasoning as <see cref="PaymentConfigIntegrationTests"/>: this is the one place that proves the
/// MVC action added on top of the pre-existing <see cref="OrderService.CancelAsync"/> (already covered
/// for its seat-release/promo-revert transaction by <see cref="SeatCapAndEnrollmentDeadlineTests"/>) is
/// actually reachable end to end, with the right auth/ownership/status-conflict behavior.
/// </summary>
[Collection(ContainersCollection.Name)]
public sealed class OrderCancellationIntegrationTests : IAsyncLifetime
{
    private static readonly JsonSerializerOptions JsonOptions = new(JsonSerializerDefaults.Web)
    {
        Converters = { new System.Text.Json.Serialization.JsonStringEnumConverter() },
    };

    private readonly ContainersFixture _containers;
    private SiriApiFactory _factory = null!;

    public OrderCancellationIntegrationTests(ContainersFixture containers)
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

    /// <summary>Seeds+logs in a fresh confirmed user through the real HTTP login endpoint, mirroring
    /// <see cref="PaymentConfigIntegrationTests.LoginAsNewUserAsync"/>.</summary>
    private async Task<(Guid UserId, string AccessToken)> CreateUserAndLoginAsync(HttpClient client)
    {
        var builder = new TestUserBuilder();
        Guid userId;
        await using (var scope = _factory.Services.CreateAsyncScope())
        {
            var user = await builder.BuildAsync(scope.ServiceProvider);
            userId = user.Id;
        }

        var response = await client.PostAsJsonAsync("/api/identity/login", new
        {
            email = builder.Email,
            password = builder.Password,
            deviceId = $"order-cancel-test-device-{Guid.NewGuid():N}",
            deviceName = "Order Cancel Test Device",
        });

        response.EnsureSuccessStatusCode();
        var body = await response.Content.ReadFromJsonAsync<LoginResponse>(JsonOptions);
        Assert.NotNull(body);
        return (userId, body.AccessToken);
    }

    /// <summary>Seeds a published course directly against the DB (same reasoning as
    /// <see cref="SeatCapAndEnrollmentDeadlineTests.CreatePublishedCourseAsync"/>: no need to
    /// round-trip the instructor-facing HTTP API just to get a purchasable course).</summary>
    private async Task<COURSE> CreatePublishedCourseAsync(decimal price = 1000m)
    {
        await using var scope = _factory.Services.CreateAsyncScope();
        var dbContext = scope.ServiceProvider.GetRequiredService<AppDbContext>();
        var clock = scope.ServiceProvider.GetRequiredService<IClock>();

        var category = CATEGORY.Create($"cat-{Guid.NewGuid():N}", "หมวดหมู่ทดสอบ", "Test Cat", null, null, 0);
        dbContext.Categories().Add(category);

        var profile = INSTRUCTOR_PROFILE.Apply(Guid.NewGuid(), "Test Instructor", "Headline", "Bio");
        profile.Approve(clock);
        dbContext.InstructorProfiles().Add(profile);

        var course = COURSE.Create($"course-{Guid.NewGuid():N}", "Test COURSE", profile.Id, category.Id, CourseLevel.Beginner, CourseLanguage.Thai, price);
        var section = course.AddSection("Section 1");
        section.AddEpisode("Episode 1", null, isFreePreview: false).AttachMedia(Guid.NewGuid(), 600);
        course.Publish(clock);

        dbContext.Courses().Add(course);
        await dbContext.SaveChangesAsync();
        return course;
    }

    private static HttpRequestMessage AuthenticatedRequest(HttpMethod method, string path, string accessToken)
    {
        var request = new HttpRequestMessage(method, path);
        request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", accessToken);
        return request;
    }

    private async Task<OrderResponse> CreateOrderAsync(HttpClient client, string accessToken, Guid courseId)
    {
        using var request = AuthenticatedRequest(HttpMethod.Post, "/api/commerce/orders", accessToken);
        request.Content = JsonContent.Create(new CreateOrderCommand([courseId]));

        using var response = await client.SendAsync(request);
        Assert.Equal(HttpStatusCode.Created, response.StatusCode);

        var order = await response.Content.ReadFromJsonAsync<OrderResponse>(JsonOptions);
        Assert.NotNull(order);
        return order!;
    }

    [Fact]
    public async Task CancelOrder_Owner_CancelsPendingOrderAndReturnsCancelledStatus()
    {
        var client = _factory.CreateClient();
        var (_, accessToken) = await CreateUserAndLoginAsync(client);
        var course = await CreatePublishedCourseAsync();
        var order = await CreateOrderAsync(client, accessToken, course.Id);

        using var cancelRequest = AuthenticatedRequest(HttpMethod.Post, $"/api/commerce/orders/{order.Id}/cancel", accessToken);
        using var cancelResponse = await client.SendAsync(cancelRequest);

        Assert.Equal(HttpStatusCode.OK, cancelResponse.StatusCode);
        var cancelled = await cancelResponse.Content.ReadFromJsonAsync<OrderResponse>(JsonOptions);
        Assert.NotNull(cancelled);
        Assert.Equal(OrderStatus.Cancelled, cancelled!.Status);
    }

    [Fact]
    public async Task CancelOrder_NonOwner_Returns404NotFoundNotForbidden()
    {
        var client = _factory.CreateClient();
        var (_, ownerToken) = await CreateUserAndLoginAsync(client);
        var (_, strangerToken) = await CreateUserAndLoginAsync(client);
        var course = await CreatePublishedCourseAsync();
        var order = await CreateOrderAsync(client, ownerToken, course.Id);

        using var cancelRequest = AuthenticatedRequest(HttpMethod.Post, $"/api/commerce/orders/{order.Id}/cancel", strangerToken);
        using var cancelResponse = await client.SendAsync(cancelRequest);

        // This codebase's IDOR-hiding convention (see OrdersController.GetById): a resource that
        // exists but belongs to someone else answers 404, the same as an id that doesn't exist at
        // all — never 403, which would confirm the id's existence to an attacker.
        Assert.Equal(HttpStatusCode.NotFound, cancelResponse.StatusCode);

        await using var scope = _factory.Services.CreateAsyncScope();
        var dbContext = scope.ServiceProvider.GetRequiredService<AppDbContext>();
        var orderDb = await dbContext.Orders().AsNoTracking().SingleAsync(o => o.ORDER_ID == order.Id);
        Assert.Equal(OrderStatus.Pending, orderDb.STATUS);
    }

    [Fact]
    public async Task CancelOrder_PaidOrder_Returns409ConflictAndLeavesOrderPaid()
    {
        var client = _factory.CreateClient();
        var (_, accessToken) = await CreateUserAndLoginAsync(client);
        var course = await CreatePublishedCourseAsync();
        var order = await CreateOrderAsync(client, accessToken, course.Id);

        // No HTTP path exists to mark an order Paid outside the real Stripe webhook flow (verified
        // separately by PaymentFulfillmentIntegrationTests) — transition the domain entity directly,
        // the same way SeatCapAndEnrollmentDeadlineTests seeds course state directly rather than
        // round-tripping through an unrelated flow just to reach the status this test cares about.
        await using (var scope = _factory.Services.CreateAsyncScope())
        {
            var dbContext = scope.ServiceProvider.GetRequiredService<AppDbContext>();
            var clock = scope.ServiceProvider.GetRequiredService<IClock>();
            var orderEntity = await dbContext.Orders().SingleAsync(o => o.ORDER_ID == order.Id);
            orderEntity.MarkAwaitingPayment();
            orderEntity.MarkPaid(clock);
            await dbContext.SaveChangesAsync();
        }

        using var cancelRequest = AuthenticatedRequest(HttpMethod.Post, $"/api/commerce/orders/{order.Id}/cancel", accessToken);
        using var cancelResponse = await client.SendAsync(cancelRequest);

        Assert.Equal(HttpStatusCode.Conflict, cancelResponse.StatusCode);

        await using var verifyScope = _factory.Services.CreateAsyncScope();
        var verifyDbContext = verifyScope.ServiceProvider.GetRequiredService<AppDbContext>();
        var orderDb = await verifyDbContext.Orders().AsNoTracking().SingleAsync(o => o.ORDER_ID == order.Id);
        Assert.Equal(OrderStatus.Paid, orderDb.STATUS);
    }
}
