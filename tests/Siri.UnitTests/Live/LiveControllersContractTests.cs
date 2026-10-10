using System.Net;
using System.Reflection;
using System.Security.Claims;
using System.Threading.RateLimiting;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.Routing;
using Microsoft.AspNetCore.RateLimiting;
using Microsoft.AspNetCore.Routing;
using Siri.Api.Configuration;
using Siri.Api.Controllers.Live;
using Siri.SharedKernel;

namespace Siri.UnitTests.Live;

/// <summary>
/// Structural guarantees for the Live controllers, so a regression cannot slip in unnoticed: every action states its authorization (there is no
/// fallback policy, so a forgotten attribute would silently make an endpoint public), only the OAuth callback is anonymous, every limited endpoint
/// uses a <em>partitioned</em> Live policy (never the app-wide "default" window), no action takes a user id from the request, and the routes are
/// exactly the P11-03 contract's. Plus behavioural tests of the partitioned limiters themselves.
/// </summary>
public class LiveControllersContractTests
{
    private static readonly Type[] Controllers = [typeof(LiveGoogleController), typeof(LiveMeetingsController)];

    private sealed record ActionInfo(Type Controller, MethodInfo Method, string HttpMethod, string Route);

    private static IReadOnlyList<ActionInfo> Actions()
    {
        var actions = new List<ActionInfo>();
        foreach (var controller in Controllers)
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

    [Fact]
    public void Routes_AreExactlyTheContractsEndpoints_SevenFromP1103AndTheRecordingAccessConnectFromP1113()
    {
        var actual = Actions().Select(a => $"{a.HttpMethod} {a.Route}").Order().ToArray();

        string[] expected =
        [
            "DELETE api/live/instructor/google",
            "GET api/live/instructor/courses/{courseId:guid}/meetings",
            "GET api/live/instructor/google/callback",
            "GET api/live/instructor/google/status",
            "POST api/live/instructor/google/connect",
            "POST api/live/instructor/google/recording-access/connect",
            "POST api/live/instructor/sessions/{sessionId:guid}/meeting/resync",
            "PUT api/live/instructor/sessions/{sessionId:guid}/meeting-link",
        ];

        Assert.Equal(expected.Order().ToArray(), actual);
    }

    [Fact]
    public void EveryAction_StatesItsAuthorization_AndOnlyTheOAuthCallbackIsAnonymous()
    {
        foreach (var action in Actions())
        {
            var anonymous = action.Method.GetCustomAttribute<AllowAnonymousAttribute>() is not null
                || action.Controller.GetCustomAttribute<AllowAnonymousAttribute>() is not null;
            var authorize = action.Method.GetCustomAttributes<AuthorizeAttribute>().Concat(action.Controller.GetCustomAttributes<AuthorizeAttribute>()).ToArray();

            Assert.True(anonymous || authorize.Length > 0, $"{action.Controller.Name}.{action.Method.Name} has neither [Authorize] nor [AllowAnonymous].");

            if (action.Method.Name == nameof(LiveGoogleController.Callback))
            {
                Assert.True(anonymous);
                continue;
            }

            Assert.False(anonymous, $"{action.Method.Name} must not be anonymous.");
            Assert.Contains(authorize, a => a.Policy == AuthorizationPolicyNames.InstructorOnly);
        }
    }

    [Fact]
    public void EveryAction_IsRateLimited_WithAPartitionedLivePolicy_NeverTheGlobalDefault()
    {
        var allowed = new[]
        {
            RateLimiterConfiguration.LiveUserPolicyName,
            RateLimiterConfiguration.LiveJoinPolicyName,
            RateLimiterConfiguration.LiveGoogleCallbackPolicyName,
        };

        foreach (var action in Actions())
        {
            var policy = (action.Method.GetCustomAttribute<EnableRateLimitingAttribute>()
                ?? action.Controller.GetCustomAttribute<EnableRateLimitingAttribute>())?.PolicyName;

            Assert.NotNull(policy);
            Assert.Contains(policy, allowed);
            Assert.NotEqual("default", policy);
        }

        var callback = typeof(LiveGoogleController).GetMethod(nameof(LiveGoogleController.Callback))!;
        Assert.Equal(RateLimiterConfiguration.LiveGoogleCallbackPolicyName, callback.GetCustomAttribute<EnableRateLimitingAttribute>()!.PolicyName);
    }

    [Fact]
    public void EveryAction_HasAnEndpointNameAndSummary_ForTheGeneratedClient()
    {
        foreach (var action in Actions())
        {
            Assert.NotNull(action.Method.GetCustomAttribute<EndpointNameAttribute>());
            Assert.NotNull(action.Method.GetCustomAttribute<EndpointSummaryAttribute>());
            Assert.NotEmpty(action.Method.GetCustomAttributes<ProducesResponseTypeAttribute>());
        }

        var names = Actions().Select(a => a.Method.GetCustomAttribute<EndpointNameAttribute>()!.EndpointName).ToArray();
        Assert.Equal(names.Length, names.Distinct().Count());
    }

    [Fact]
    public void TheRecordingAccessConnect_IsInstructorOnly_SharesTheCalendarConnectsRateLimit_AndDocumentsEveryStatus()
    {
        var method = typeof(LiveGoogleController).GetMethod(nameof(LiveGoogleController.ConnectRecordingAccess))!;
        var connect = typeof(LiveGoogleController).GetMethod(nameof(LiveGoogleController.Connect))!;

        Assert.Equal(AuthorizationPolicyNames.InstructorOnly, method.GetCustomAttribute<AuthorizeAttribute>()!.Policy);
        Assert.Equal(
            connect.GetCustomAttribute<EnableRateLimitingAttribute>()!.PolicyName,
            method.GetCustomAttribute<EnableRateLimitingAttribute>()!.PolicyName);

        var statuses = method.GetCustomAttributes<ProducesResponseTypeAttribute>().Select(a => a.StatusCode).ToArray();
        foreach (var status in new[] { 200, 400, 401, 403, 409, 429, 503 })
        {
            Assert.Contains(status, statuses);
        }
    }

    [Fact]
    public void NoAction_AcceptsAUserIdFromTheRequest()
    {
        foreach (var action in Actions())
        {
            foreach (var parameter in action.Method.GetParameters())
            {
                Assert.DoesNotContain("userid", parameter.Name!, StringComparison.OrdinalIgnoreCase);
                Assert.DoesNotContain("instructorid", parameter.Name!, StringComparison.OrdinalIgnoreCase);
            }
        }
    }

    [Fact]
    public void EveryAction_ReturnsIResult_NotAnEntity()
    {
        foreach (var action in Actions())
        {
            Assert.Equal(typeof(Task<IResult>), action.Method.ReturnType);
        }
    }

    // ---- Partitioned rate limiters --------------------------------------------------------------------------

    private static DefaultHttpContext UserContext(string userId)
    {
        var context = new DefaultHttpContext();
        context.User = new ClaimsPrincipal(new ClaimsIdentity([new Claim(ClaimTypes.NameIdentifier, userId)], "test"));
        return context;
    }

    [Fact]
    public void LiveUser_Allows60PerMinutePerUser_AndOneUsersTrafficNeverUsesUpAnothers()
    {
        using var limiter = PartitionedRateLimiter.Create<HttpContext, string>(RateLimiterConfiguration.CreateLiveUserPartition);
        var alice = UserContext("alice");
        var bob = UserContext("bob");

        for (var i = 0; i < 60; i++)
        {
            using var lease = limiter.AttemptAcquire(alice);
            Assert.True(lease.IsAcquired);
        }

        using (var over = limiter.AttemptAcquire(alice))
        {
            Assert.False(over.IsAcquired);
        }

        using var bobLease = limiter.AttemptAcquire(bob);
        Assert.True(bobLease.IsAcquired);
    }

    [Fact]
    public void LiveJoin_Allows6PerMinutePerUser()
    {
        using var limiter = PartitionedRateLimiter.Create<HttpContext, string>(RateLimiterConfiguration.CreateLiveJoinPartition);
        var alice = UserContext("alice");

        for (var i = 0; i < 6; i++)
        {
            using var lease = limiter.AttemptAcquire(alice);
            Assert.True(lease.IsAcquired);
        }

        using var over = limiter.AttemptAcquire(alice);
        Assert.False(over.IsAcquired);

        using var other = limiter.AttemptAcquire(UserContext("bob"));
        Assert.True(other.IsAcquired);
    }

    [Fact]
    public void LiveBudgets_AreIndependentPerPolicy_ForTheSameUser()
    {
        var alice = UserContext("alice");

        Assert.NotEqual(
            RateLimiterConfiguration.CreateLiveJoinPartition(alice).PartitionKey,
            RateLimiterConfiguration.CreateLiveUserPartition(alice).PartitionKey);
    }

    [Fact]
    public void LiveGoogleCallback_IsPartitionedByClientAddress_60PerMinute()
    {
        using var limiter = PartitionedRateLimiter.Create<HttpContext, string>(RateLimiterConfiguration.CreateLiveGoogleCallbackPartition);

        DefaultHttpContext From(string ip)
        {
            var context = new DefaultHttpContext();
            context.Connection.RemoteIpAddress = IPAddress.Parse(ip);
            return context;
        }

        var first = From("203.0.113.10");
        for (var i = 0; i < 60; i++)
        {
            using var lease = limiter.AttemptAcquire(first);
            Assert.True(lease.IsAcquired);
        }

        using (var over = limiter.AttemptAcquire(first))
        {
            Assert.False(over.IsAcquired);
        }

        using var elsewhere = limiter.AttemptAcquire(From("203.0.113.11"));
        Assert.True(elsewhere.IsAcquired);
    }

    [Fact]
    public void LivePolicies_UseTheClaimsNameIdentifierOrSub_SoTheKeyIsTheUserNotTheConnection()
    {
        var bySub = new DefaultHttpContext
        {
            User = new ClaimsPrincipal(new ClaimsIdentity([new Claim("sub", "user-by-sub")], "test")),
        };

        Assert.Equal("live-user:user-by-sub", RateLimiterConfiguration.CreateLiveUserPartition(bySub).PartitionKey);
        Assert.Equal("live-user:alice", RateLimiterConfiguration.CreateLiveUserPartition(UserContext("alice")).PartitionKey);
    }
}
