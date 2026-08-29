using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Text;
using System.Text.Json;
using Microsoft.AspNetCore.Authentication.JwtBearer;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.TestHost;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.IdentityModel.Tokens;
using Siri.Api.Authorization;
using Siri.IntegrationTests.Fixtures;
using Siri.Modules.Catalog;
using Siri.Modules.Catalog.Domain;
using Siri.Modules.Catalog.Features.Wishlist;
using Siri.Modules.Catalog.Infrastructure;
using Siri.Modules.Identity;
using Siri.Modules.Identity.Domain;
using Siri.Modules.Identity.Features.Login;
using Siri.Modules.Identity.Infrastructure;
using Siri.Modules.Notification;
using Siri.Persistence;
using Siri.Persistence.DependencyInjection;
using Siri.SharedKernel;
using Xunit;

namespace Siri.IntegrationTests;

[Collection(ContainersCollection.Name)]
public sealed class WishlistIntegrationTests : IAsyncLifetime
{
    private const string KnownPassword = "Correct-Horse-Battery-Staple-9";
    private const string TestIssuer = "https://api.siriupskill.test";
    private const string TestAudience = "siriupskill-frontend-test";
    private const string TestSigningKey = "wishlist-integration-tests-signing-key-0123456789";

    private static readonly JsonSerializerOptions JsonOptions = new(JsonSerializerDefaults.Web)
    {
        Converters = { new System.Text.Json.Serialization.JsonStringEnumConverter() },
    };

    private readonly ContainersFixture _containers;
    private WebApplication _app = null!;
    private HttpClient _client = null!;

    private Guid _userAId;
    private Guid _userBId;
    private Guid _course1Id;
    private Guid _course2Id;

    public WishlistIntegrationTests(ContainersFixture containers)
    {
        _containers = containers;
    }

    public async Task InitializeAsync()
    {
        var builder = WebApplication.CreateBuilder();
        builder.WebHost.UseTestServer();

        builder.Configuration.AddInMemoryCollection(new Dictionary<string, string?>
        {
            ["ConnectionStrings:Default"] = _containers.SqlConnectionString,
            ["Redis:ConnectionString"] = _containers.RedisConnectionString,
            ["Seo:PublicBaseUrl"] = "https://example.test",
            ["Identity:EmailConfirmation:ConfirmEmailUrl"] = "https://example.test/confirm-email",
            ["Identity:PasswordReset:ResetPasswordUrl"] = "https://example.test/reset-password",
            ["Identity:Security:MaxConcurrentSessions"] = "10",
            ["Identity:Jwt:Issuer"] = TestIssuer,
            ["Identity:Jwt:Audience"] = TestAudience,
            ["Identity:Jwt:SigningKey"] = TestSigningKey,
            ["Identity:Jwt:AccessTokenLifetimeMinutes"] = "15",
            ["Email:Provider"] = "Log",
        });

        builder.Services
            .AddAuthentication(JwtBearerDefaults.AuthenticationScheme)
            .AddJwtBearer(options =>
            {
                options.TokenValidationParameters = new TokenValidationParameters
                {
                    ValidateIssuer = true,
                    ValidIssuer = TestIssuer,
                    ValidateAudience = true,
                    ValidAudience = TestAudience,
                    ValidateIssuerSigningKey = true,
                    IssuerSigningKey = new SymmetricSecurityKey(Encoding.UTF8.GetBytes(TestSigningKey)),
                    ValidateLifetime = true,
                    ClockSkew = TimeSpan.Zero,
                };
            });

        builder.Services.AddSiriAuthorizationPolicies();
        builder.Services.AddPersistence(builder.Configuration);
        builder.Services.AddSharedRedis(builder.Configuration);
        builder.Services.AddIdentityModule(builder.Configuration);
        builder.Services.AddNotificationModule(builder.Configuration);
        builder.Services.AddCatalogModule(builder.Configuration);

        _app = builder.Build();

        _app.UseAuthentication();
        _app.UseAuthorization();

        _app.MapCatalogEndpoints();

        await _app.StartAsync();
        _client = _app.GetTestClient();

        await using var scope = _app.Services.CreateAsyncScope();
        var dbContext = scope.ServiceProvider.GetRequiredService<AppDbContext>();
        await dbContext.Database.MigrateAsync();
        await SeedDataAsync(scope.ServiceProvider, dbContext);
    }

    public async Task DisposeAsync()
    {
        _client.Dispose();
        await _app.DisposeAsync();
    }

    [Fact]
    public async Task GetWishlist_Anonymous_ReturnsUnauthorized()
    {
        var response = await _client.GetAsync("/api/catalog/wishlist");
        Assert.Equal(HttpStatusCode.Unauthorized, response.StatusCode);
    }

    [Fact]
    public async Task AddToWishlist_AuthenticatedUser_AddsAndReturnsOk()
    {
        var userToken = await LoginAndGetAccessTokenAsync(_app.Services, "wishlist-a@example.test");
        var userClient = _app.GetTestClient();
        userClient.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", userToken);

        var addResponse = await userClient.PostAsync($"/api/catalog/wishlist/{_course1Id}", null);
        Assert.Equal(HttpStatusCode.OK, addResponse.StatusCode);

        var getResponse = await userClient.GetAsync("/api/catalog/wishlist");
        Assert.Equal(HttpStatusCode.OK, getResponse.StatusCode);

        var wishlist = await getResponse.Content.ReadFromJsonAsync<IReadOnlyList<WishlistCourseItemResponse>>(JsonOptions);
        Assert.NotNull(wishlist);
        Assert.Contains(wishlist, w => w.CourseId == _course1Id);
    }

    [Fact]
    public async Task Wishlist_IsUserScoped_UserBDoesNotSeeUserAWishlist()
    {
        var userAToken = await LoginAndGetAccessTokenAsync(_app.Services, "wishlist-a@example.test");
        var userAClient = _app.GetTestClient();
        userAClient.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", userAToken);

        await userAClient.PostAsync($"/api/catalog/wishlist/{_course2Id}", null);

        var userBToken = await LoginAndGetAccessTokenAsync(_app.Services, "wishlist-b@example.test");
        var userBClient = _app.GetTestClient();
        userBClient.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", userBToken);

        var getResponse = await userBClient.GetAsync("/api/catalog/wishlist");
        Assert.Equal(HttpStatusCode.OK, getResponse.StatusCode);

        var wishlistB = await getResponse.Content.ReadFromJsonAsync<IReadOnlyList<WishlistCourseItemResponse>>(JsonOptions);
        Assert.NotNull(wishlistB);
        Assert.DoesNotContain(wishlistB, w => w.CourseId == _course2Id);
    }

    [Fact]
    public async Task RemoveFromWishlist_RemovesCourseFromUserWishlist()
    {
        var userToken = await LoginAndGetAccessTokenAsync(_app.Services, "wishlist-a@example.test");
        var userClient = _app.GetTestClient();
        userClient.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", userToken);

        await userClient.PostAsync($"/api/catalog/wishlist/{_course1Id}", null);

        var deleteResponse = await userClient.DeleteAsync($"/api/catalog/wishlist/{_course1Id}");
        Assert.Equal(HttpStatusCode.OK, deleteResponse.StatusCode);

        var getResponse = await userClient.GetAsync("/api/catalog/wishlist");
        var wishlist = await getResponse.Content.ReadFromJsonAsync<IReadOnlyList<WishlistCourseItemResponse>>(JsonOptions);
        Assert.NotNull(wishlist);
        Assert.DoesNotContain(wishlist, w => w.CourseId == _course1Id);
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
        var result = await loginHandler.HandleAsync(
            new LoginCommand(email, KnownPassword, "device-1", "Test Device"), "UA", "203.0.113.50", CancellationToken.None);

        Assert.True(result.IsSuccess);
        return result.Value.AccessToken;
    }

    private async Task SeedDataAsync(IServiceProvider services, AppDbContext db)
    {
        var clock = services.GetRequiredService<IClock>();

        var userA = await CreateUserAsync(services, db, "wishlist-a@example.test", KnownPassword);
        userA.AssignRole(new ROLE(ROLE.LearnerId, ROLE.LearnerName));

        var userB = await CreateUserAsync(services, db, "wishlist-b@example.test", KnownPassword);
        userB.AssignRole(new ROLE(ROLE.LearnerId, ROLE.LearnerName));

        var instructor = await CreateUserAsync(services, db, "wishlist-inst@example.test", KnownPassword);
        instructor.AssignRole(new ROLE(ROLE.InstructorId, ROLE.InstructorName));

        await db.SaveChangesAsync();

        _userAId = userA.Id;
        _userBId = userB.Id;

        var profile = INSTRUCTOR_PROFILE.Apply(instructor.Id, "Inst Wish Profile", "Headline", "Bio");
        profile.Approve(clock);
        db.InstructorProfiles().Add(profile);

        var category = CATEGORY.Create("wishlist-dev", "Wishlist Dev", "Wishlist Dev En", null, null, 0);
        db.Categories().Add(category);
        await db.SaveChangesAsync();

        var course1 = COURSE.Create("c1-wish", "COURSE 1 Wish", profile.Id, category.Id, CourseLevel.Beginner, CourseLanguage.Thai, 990m);
        var s1 = course1.AddSection("Sec 1");
        s1.AddEpisode("Ep 1", null, false).AttachMedia(Guid.NewGuid(), 600);
        course1.SubmitForReview();
        course1.Publish(clock);

        var course2 = COURSE.Create("c2-wish", "COURSE 2 Wish", profile.Id, category.Id, CourseLevel.Intermediate, CourseLanguage.Thai, 1290m);
        var s2 = course2.AddSection("Sec 2");
        s2.AddEpisode("Ep 2", null, false).AttachMedia(Guid.NewGuid(), 600);
        course2.SubmitForReview();
        course2.Publish(clock);

        db.Courses().AddRange(course1, course2);
        await db.SaveChangesAsync();

        _course1Id = course1.Id;
        _course2Id = course2.Id;
    }
}
