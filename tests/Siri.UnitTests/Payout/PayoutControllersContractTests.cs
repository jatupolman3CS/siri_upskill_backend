using System.Reflection;
using System.Security.Claims;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Http.HttpResults;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.Routing;
using Microsoft.AspNetCore.RateLimiting;
using Microsoft.AspNetCore.Routing;
using Microsoft.Extensions.Options;
using Siri.Api.Configuration;
using Siri.Api.Controllers.Payout;
using Siri.Modules.Catalog.Contracts;
using Siri.Modules.Payout;
using Siri.Modules.Payout.Application;
using Siri.SharedKernel;
using Xunit;

namespace Siri.UnitTests.Payout;

/// <summary>
/// Structural guarantees for the Payout controllers (there is no fallback authorization policy, so an action with neither <c>[Authorize]</c> nor
/// <c>[AllowAnonymous]</c> would be silently public) plus the behaviour of <c>GET /api/payout/policy</c>: signed-in only, the user id from <see cref="IUserContext"/>
/// only, <c>Cache-Control: no-store</c>, its own partitioned per-user rate limit (never the app-wide "default" window) and the exact JSON contract.
/// </summary>
public sealed class PayoutControllersContractTests
{
    private static readonly Type[] AllPayoutControllers =
        [typeof(AdminPayoutController), typeof(InstructorPayoutController), typeof(PayoutPolicyController)];

    private sealed record ActionInfo(Type Controller, MethodInfo Method, string HttpMethod, string Route);

    private static IReadOnlyList<ActionInfo> Actions(params Type[] controllers)
    {
        var actions = new List<ActionInfo>();
        foreach (var controller in controllers)
        {
            var prefix = controller.GetCustomAttribute<RouteAttribute>()!.Template;
            foreach (var method in controller.GetMethods(BindingFlags.Public | BindingFlags.Instance | BindingFlags.DeclaredOnly))
            {
                var http = method.GetCustomAttributes().OfType<HttpMethodAttribute>().SingleOrDefault();
                if (http is null)
                {
                    continue;
                }

                var route = string.IsNullOrEmpty(http.Template) ? prefix : $"{prefix}/{http.Template}";
                actions.Add(new ActionInfo(controller, method, http.HttpMethods.Single(), route));
            }
        }

        return actions;
    }

    // ---- Structure -------------------------------------------------------------------------------------------

    [Fact]
    public void EveryPayoutAction_StatesItsAuthorization_AndNoneIsAnonymous()
    {
        var actions = Actions(AllPayoutControllers);
        Assert.NotEmpty(actions);

        foreach (var action in actions)
        {
            var anonymous = action.Method.GetCustomAttribute<AllowAnonymousAttribute>() is not null
                || action.Controller.GetCustomAttribute<AllowAnonymousAttribute>() is not null;
            var authorize = action.Method.GetCustomAttributes<AuthorizeAttribute>()
                .Concat(action.Controller.GetCustomAttributes<AuthorizeAttribute>())
                .ToArray();

            Assert.False(anonymous, $"{action.Controller.Name}.{action.Method.Name} must not be anonymous (money endpoints).");
            Assert.True(authorize.Length > 0, $"{action.Controller.Name}.{action.Method.Name} has no [Authorize].");
        }
    }

    [Fact]
    public void TheAdminEndpoints_RequireTheAdminOnlyPolicy()
    {
        foreach (var action in Actions(typeof(AdminPayoutController)))
        {
            var policies = action.Method.GetCustomAttributes<AuthorizeAttribute>()
                .Concat(action.Controller.GetCustomAttributes<AuthorizeAttribute>())
                .Select(a => a.Policy)
                .ToArray();

            Assert.Contains(AuthorizationPolicyNames.AdminOnly, policies);
        }
    }

    [Fact]
    public void NoPayoutAction_AcceptsAUserIdFromTheRequest_AndOnlyTheAdminOnesTakeAnInstructorId()
    {
        foreach (var action in Actions(AllPayoutControllers))
        {
            foreach (var parameter in action.Method.GetParameters())
            {
                Assert.DoesNotContain("userid", parameter.Name!, StringComparison.OrdinalIgnoreCase);

                // The admin screens address a specific instructor profile by id; every signed-in-user endpoint must derive "who" from IUserContext instead.
                if (action.Controller != typeof(AdminPayoutController))
                {
                    Assert.DoesNotContain("instructorid", parameter.Name!, StringComparison.OrdinalIgnoreCase);
                }
            }
        }
    }

    [Fact]
    public void EveryPayoutAction_HasAnEndpointNameSummaryAndResponseTypes_AndNamesAreUnique()
    {
        var actions = Actions(AllPayoutControllers);

        foreach (var action in actions)
        {
            Assert.NotNull(action.Method.GetCustomAttribute<EndpointNameAttribute>());
            Assert.NotNull(action.Method.GetCustomAttribute<EndpointSummaryAttribute>());
            Assert.NotEmpty(action.Method.GetCustomAttributes<ProducesResponseTypeAttribute>());
        }

        var names = actions.Select(a => a.Method.GetCustomAttribute<EndpointNameAttribute>()!.EndpointName).ToArray();
        Assert.Equal(names.Length, names.Distinct().Count());
    }

    [Fact]
    public void EveryPayoutAction_ReturnsIResult_NotAnEntity()
    {
        foreach (var action in Actions(AllPayoutControllers))
        {
            Assert.Equal(typeof(Task<IResult>), action.Method.ReturnType);
        }
    }

    [Fact]
    public void ThePolicyEndpoint_IsExactlyGetApiPayoutPolicy_SignedInOnly_AndNotAdminOrInstructorGated()
    {
        var action = Assert.Single(Actions(typeof(PayoutPolicyController)));

        Assert.Equal("GET", action.HttpMethod);
        Assert.Equal("api/payout/policy", action.Route);

        var authorize = typeof(PayoutPolicyController).GetCustomAttributes<AuthorizeAttribute>().Single();
        Assert.True(string.IsNullOrEmpty(authorize.Policy), "any signed-in user may read the policy");
        Assert.True(string.IsNullOrEmpty(authorize.Roles));
        Assert.Null(action.Method.GetCustomAttribute<AllowAnonymousAttribute>());

        var statuses = action.Method.GetCustomAttributes<ProducesResponseTypeAttribute>().Select(a => a.StatusCode).ToArray();
        Assert.Contains(200, statuses);
        Assert.Contains(401, statuses);
        Assert.Contains(429, statuses);
        Assert.Equal(typeof(PayoutPolicyResponse), action.Method.GetCustomAttributes<ProducesResponseTypeAttribute>().First(a => a.StatusCode == 200).Type);
    }

    [Fact]
    public void ThePolicyEndpoint_UsesItsOwnPartitionedRateLimit_NeverTheGlobalDefault()
    {
        var policy = typeof(PayoutPolicyController).GetCustomAttribute<EnableRateLimitingAttribute>()?.PolicyName;

        Assert.Equal(RateLimiterConfiguration.PayoutReadPolicyName, policy);
        Assert.NotEqual("default", policy);
    }

    // ---- The partitioned limiter ------------------------------------------------------------------------------

    private static DefaultHttpContext ContextFor(string? userId)
    {
        var context = new DefaultHttpContext();
        if (userId is not null)
        {
            context.User = new ClaimsPrincipal(new ClaimsIdentity([new Claim(ClaimTypes.NameIdentifier, userId)], "TestAuth"));
        }

        return context;
    }

    [Fact]
    public void PayoutReadPartition_IsPerUser_AndIndependentOfTheOtherPartitionedPolicies()
    {
        var alice = RateLimiterConfiguration.CreatePayoutReadPartition(ContextFor("usr-alice"));
        var bob = RateLimiterConfiguration.CreatePayoutReadPartition(ContextFor("usr-bob"));
        var aliceLive = RateLimiterConfiguration.CreateLiveUserPartition(ContextFor("usr-alice"));

        Assert.NotEqual(alice.PartitionKey, bob.PartitionKey);
        Assert.NotEqual(alice.PartitionKey, aliceLive.PartitionKey);
    }

    [Fact]
    public void PayoutReadLimiter_Allows60RequestsPerMinute_RejectsTheNext_AndOneUsersBurstNeverTouchesAnother()
    {
        var alice = RateLimiterConfiguration.CreatePayoutReadPartition(ContextFor("usr-alice-limit"));
        var aliceLimiter = alice.Factory(alice.PartitionKey);

        for (var i = 1; i <= 60; i++)
        {
            using var lease = aliceLimiter.AttemptAcquire();
            Assert.True(lease.IsAcquired, $"request {i} should be allowed");
        }

        using (var rejected = aliceLimiter.AttemptAcquire())
        {
            Assert.False(rejected.IsAcquired);
        }

        var bob = RateLimiterConfiguration.CreatePayoutReadPartition(ContextFor("usr-bob-limit"));
        using var bobLease = bob.Factory(bob.PartitionKey).AttemptAcquire();
        Assert.True(bobLease.IsAcquired);
    }

    // ---- Behaviour ---------------------------------------------------------------------------------------------

    private sealed class FakeUserContext(Guid? userId) : IUserContext
    {
        public Guid? UserId { get; } = userId;

        public IReadOnlyCollection<string> Roles { get; } = [];

        public bool IsAuthenticated => UserId is not null;
    }

    private sealed class FakeCatalog : ICatalogPriceContract
    {
        public readonly Dictionary<Guid, decimal> Shares = [];

        public Task<IReadOnlyDictionary<Guid, decimal>> GetInstructorRevenueSharePercentsAsync(IEnumerable<Guid> instructorIds, CancellationToken cancellationToken)
        {
            IReadOnlyDictionary<Guid, decimal> result = instructorIds.Where(Shares.ContainsKey).ToDictionary(id => id, id => Shares[id]);
            return Task.FromResult(result);
        }

        public Task<IReadOnlyDictionary<Guid, CoursePriceInfo>> GetPublishedCoursePricesAsync(IEnumerable<Guid> courseIds, CancellationToken cancellationToken) =>
            Task.FromResult<IReadOnlyDictionary<Guid, CoursePriceInfo>>(new Dictionary<Guid, CoursePriceInfo>());

        public Task<bool> IsEpisodeFreePreviewAsync(Guid episodeId, CancellationToken cancellationToken) => Task.FromResult(false);

        public Task<Guid?> GetCourseIdForEpisodeAsync(Guid episodeId, CancellationToken cancellationToken) => Task.FromResult<Guid?>(null);

        public Task<bool> IsInstructorOwnerOfEpisodeAsync(Guid episodeId, Guid instructorUserId, CancellationToken cancellationToken) => Task.FromResult(false);

        public Task<bool> IsInstructorOwnerOfCourseAsync(Guid courseId, Guid instructorUserId, CancellationToken cancellationToken) => Task.FromResult(false);

        public Task<int> GetPendingReviewsCountAsync(CancellationToken cancellationToken) => Task.FromResult(0);

        public Task<IReadOnlyDictionary<Guid, string>> GetCourseTitlesAsync(IEnumerable<Guid> courseIds, CancellationToken cancellationToken) =>
            Task.FromResult<IReadOnlyDictionary<Guid, string>>(new Dictionary<Guid, string>());
    }

    private static PayoutPolicyController NewController() =>
        new() { ControllerContext = new ControllerContext { HttpContext = new DefaultHttpContext() } };

    [Fact]
    public async Task GetPolicy_SignedInLearner_IsOkWithTheDefaultShare_NoStore_AndOnlyTheDocumentedFields()
    {
        var controller = NewController();
        var service = new PayoutPolicyService(new FakeInstructorProfileReader(), new FakeCatalog(), Options.Create(new PayoutOptions()));

        var result = await controller.GetPolicy(service, new FakeUserContext(Guid.NewGuid()), CancellationToken.None);

        var ok = Assert.IsType<Ok<PayoutPolicyResponse>>(result);
        Assert.Equal(new PayoutPolicyResponse(70m, 30m, 3m, 500m, 14), ok.Value);
        Assert.Equal("no-store", controller.Response.Headers.CacheControl.ToString());

        // The JSON contract is exactly these five camelCase fields.
        var json = System.Text.Json.JsonSerializer.SerializeToElement(ok.Value, new System.Text.Json.JsonSerializerOptions(System.Text.Json.JsonSerializerDefaults.Web));
        Assert.Equal(
            ["holdDays", "minimumPayoutAmount", "platformSharePercent", "revenueSharePercent", "withholdingTaxPercent"],
            json.EnumerateObject().Select(p => p.Name).Order().ToArray());
    }

    [Fact]
    public async Task GetPolicy_InstructorWithOwnRate_IsOkWithThatRate_KeyedByTheCallersUserIdFromTheContext()
    {
        var userId = Guid.NewGuid();
        var profiles = new FakeInstructorProfileReader();
        var catalog = new FakeCatalog();
        catalog.Shares[profiles.Map(userId, Guid.NewGuid())] = 75m;
        var service = new PayoutPolicyService(profiles, catalog, Options.Create(new PayoutOptions()));

        var result = await NewController().GetPolicy(service, new FakeUserContext(userId), CancellationToken.None);

        var ok = Assert.IsType<Ok<PayoutPolicyResponse>>(result);
        Assert.Equal(75m, ok.Value!.RevenueSharePercent);
        Assert.Equal(25m, ok.Value.PlatformSharePercent);
    }

    [Fact]
    public async Task GetPolicy_NoUserInTheContext_Is401_StillNoStore_AndNothingIsRead()
    {
        var controller = NewController();
        var profiles = new FakeInstructorProfileReader();
        var service = new PayoutPolicyService(profiles, new FakeCatalog(), Options.Create(new PayoutOptions()));

        var result = await controller.GetPolicy(service, new FakeUserContext(null), CancellationToken.None);

        Assert.IsType<UnauthorizedHttpResult>(result);
        Assert.Equal("no-store", controller.Response.Headers.CacheControl.ToString());
        Assert.Equal(0, profiles.Calls);
    }
}
