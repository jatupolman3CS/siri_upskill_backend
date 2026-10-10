using System.Net;
using System.Reflection;
using System.Security.Claims;
using System.Text;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Http.HttpResults;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.Routing;
using Microsoft.AspNetCore.RateLimiting;
using Microsoft.AspNetCore.Routing;
using Siri.Api.Configuration;
using Siri.Api.Controllers.Catalog;
using Siri.Api.Controllers.Live;
using Siri.Modules.Catalog.Contracts;
using Siri.Modules.Live.Application;
using Siri.Modules.Live.Domain;
using Siri.SharedKernel;

namespace Siri.UnitTests.Live;

/// <summary>
/// Structure and behaviour of the P11-05 controllers: every action states its authorization (there is no fallback policy, so a forgotten attribute would make an
/// endpoint public), every one has a <em>partitioned</em> rate limit (the join gate its own <c>live-join</c>, never the app-wide "default"), no action takes a user id from
/// the request, the routes are exactly the contract's, and the join endpoint's headers (<c>Cache-Control: no-store</c>, <c>Retry-After</c>) ride on success and error alike.
/// </summary>
public class LiveJoinControllersTests
{
    private static readonly Type[] Controllers = [typeof(LiveLearnerController), typeof(LiveInstructorSessionsController)];

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

    private static IReadOnlyList<ActionInfo> LiveActions => Actions(Controllers);

    // ---- Structure -------------------------------------------------------------------------------------------

    [Fact]
    public void Routes_AreExactlyTheContractsEndpoints_SevenFromP1105AndTheRecordingImportRetryFromP1113()
    {
        var actual = LiveActions.Select(a => $"{a.HttpMethod} {a.Route}").Order().ToArray();

        string[] expected =
        [
            "GET api/live/courses/{courseId:guid}/my-sessions",
            "GET api/live/me/sessions/upcoming",
            "POST api/live/sessions/{sessionId:guid}/join",
            "GET api/live/sessions/{sessionId:guid}/calendar.ics",
            "GET api/live/instructor/sessions",
            "GET api/live/instructor/sessions/{sessionId:guid}",
            "GET api/live/instructor/sessions/{sessionId:guid}/roster",
            "POST api/live/instructor/sessions/{sessionId:guid}/recording-import/retry",
        ];

        Assert.Equal(expected.Order().ToArray(), actual);
    }

    [Fact]
    public void EveryAction_StatesItsAuthorization_AndNoneIsAnonymous()
    {
        foreach (var action in LiveActions)
        {
            Assert.Null(action.Method.GetCustomAttribute<AllowAnonymousAttribute>());
            Assert.Null(action.Controller.GetCustomAttribute<AllowAnonymousAttribute>());

            var authorize = action.Method.GetCustomAttributes<AuthorizeAttribute>().Concat(action.Controller.GetCustomAttributes<AuthorizeAttribute>()).ToArray();
            Assert.True(authorize.Length > 0, $"{action.Controller.Name}.{action.Method.Name} has no [Authorize].");
        }
    }

    [Fact]
    public void TheInstructorEndpoints_RequireTheInstructorOnlyPolicy_AndTheLearnerOnesJustASignedInUser()
    {
        foreach (var action in Actions(typeof(LiveInstructorSessionsController)))
        {
            var policies = action.Controller.GetCustomAttributes<AuthorizeAttribute>().Select(a => a.Policy).ToArray();
            Assert.Contains(AuthorizationPolicyNames.InstructorOnly, policies);
        }

        // The learner endpoints deliberately use no role policy: "may I join" is decided per session by the service (enrollment or ownership), not by a role.
        foreach (var action in Actions(typeof(LiveLearnerController)))
        {
            var authorize = action.Controller.GetCustomAttributes<AuthorizeAttribute>().Single();
            Assert.True(string.IsNullOrEmpty(authorize.Policy));
            Assert.True(string.IsNullOrEmpty(authorize.Roles));
        }
    }

    [Fact]
    public void EveryAction_IsRateLimited_WithAPartitionedLivePolicy_AndOnlyTheJoinGateUsesLiveJoin()
    {
        foreach (var action in LiveActions)
        {
            var policy = (action.Method.GetCustomAttribute<EnableRateLimitingAttribute>() ?? action.Controller.GetCustomAttribute<EnableRateLimitingAttribute>())?.PolicyName;

            Assert.NotNull(policy);
            Assert.NotEqual("default", policy);

            var expected = action.Method.Name == nameof(LiveLearnerController.Join)
                ? RateLimiterConfiguration.LiveJoinPolicyName
                : RateLimiterConfiguration.LiveUserPolicyName;
            Assert.Equal(expected, policy);
        }
    }

    [Fact]
    public void EveryAction_HasAnEndpointNameSummaryAndResponseTypes_AndNamesAreUniqueAcrossLive()
    {
        foreach (var action in LiveActions)
        {
            Assert.NotNull(action.Method.GetCustomAttribute<EndpointNameAttribute>());
            Assert.NotNull(action.Method.GetCustomAttribute<EndpointSummaryAttribute>());
            Assert.NotEmpty(action.Method.GetCustomAttributes<ProducesResponseTypeAttribute>());
        }

        var all = Actions(typeof(LiveGoogleController), typeof(LiveMeetingsController), typeof(LiveLearnerController), typeof(LiveInstructorSessionsController))
            .Select(a => a.Method.GetCustomAttribute<EndpointNameAttribute>()!.EndpointName)
            .ToArray();
        Assert.Equal(all.Length, all.Distinct().Count());
    }

    [Fact]
    public void TheRecordingImportRetry_IsOwnerCheckedByTheService_RateLimitedLikeTheRoomEndpoints_AndDocumentsEveryStatus()
    {
        var method = typeof(LiveInstructorSessionsController).GetMethod(nameof(LiveInstructorSessionsController.RetryRecordingImport))!;

        Assert.Equal(
            RateLimiterConfiguration.LiveUserPolicyName,
            typeof(LiveInstructorSessionsController).GetCustomAttribute<EnableRateLimitingAttribute>()!.PolicyName);
        Assert.Null(method.GetCustomAttribute<AllowAnonymousAttribute>());

        var statuses = method.GetCustomAttributes<ProducesResponseTypeAttribute>().Select(a => a.StatusCode).ToArray();
        foreach (var status in new[] { 200, 401, 403, 404, 409, 429 })
        {
            Assert.Contains(status, statuses);
        }

        // Only the route's session id comes from the caller; the owner is always the authenticated user.
        Assert.DoesNotContain(method.GetParameters(), p => p.Name!.Equals("userId", StringComparison.OrdinalIgnoreCase) || p.Name.Contains("instructorId", StringComparison.OrdinalIgnoreCase));
    }

    [Fact]
    public void NoAction_AcceptsAUserIdFromTheRequest()
    {
        foreach (var action in LiveActions)
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
        foreach (var action in LiveActions)
        {
            Assert.Equal(typeof(Task<IResult>), action.Method.ReturnType);
        }
    }

    [Fact]
    public void TheJoinEndpoint_DocumentsEveryStatusItCanAnswer()
    {
        var join = typeof(LiveLearnerController).GetMethod(nameof(LiveLearnerController.Join))!;
        var statuses = join.GetCustomAttributes<ProducesResponseTypeAttribute>().Select(a => a.StatusCode).ToArray();

        foreach (var status in new[] { 200, 401, 404, 409, 429, 503 })
        {
            Assert.Contains(status, statuses);
        }
    }

    [Fact]
    public void TheCatalogLiveScheduleEndpoint_IsInstructorOnly_RateLimited_AndShapedAsTheContractSays()
    {
        var method = typeof(InstructorCoursesController).GetMethod(nameof(InstructorCoursesController.GetCourseLiveSchedule))!;

        Assert.Equal("api/catalog/instructor/courses", typeof(InstructorCoursesController).GetCustomAttribute<RouteAttribute>()!.Template);
        Assert.Equal("{courseId:guid}/live-schedule", method.GetCustomAttribute<HttpGetAttribute>()!.Template);
        Assert.Equal(RateLimiterConfiguration.LiveUserPolicyName, method.GetCustomAttribute<EnableRateLimitingAttribute>()!.PolicyName);
        Assert.Contains(AuthorizationPolicyNames.InstructorOnly, typeof(InstructorCoursesController).GetCustomAttributes<AuthorizeAttribute>().Select(a => a.Policy));
        Assert.Null(method.GetCustomAttribute<AllowAnonymousAttribute>());
        Assert.Equal(typeof(Task<IResult>), method.ReturnType);

        var statuses = method.GetCustomAttributes<ProducesResponseTypeAttribute>().Select(a => a.StatusCode).ToArray();
        Assert.Contains(200, statuses);
        Assert.Contains(403, statuses);
        Assert.Contains(404, statuses);
    }

    // ---- Behaviour of the join endpoint -------------------------------------------------------------------------

    private sealed class FakeUserContext(Guid? userId) : IUserContext
    {
        public Guid? UserId { get; } = userId;

        public IReadOnlyCollection<string> Roles { get; } = [];

        public bool IsAuthenticated => UserId is not null;
    }

    private static readonly Guid LearnerId = Guid.NewGuid();
    private static readonly Guid InstructorId = Guid.NewGuid();

    private readonly JoinHarness _h = new();

    private static LiveLearnerController ControllerFor(
        string? sid = null, string? ip = "198.51.100.9", string? userAgent = "UnitTest/1.0")
    {
        var http = new DefaultHttpContext();
        var identity = new ClaimsIdentity(
            sid is null ? Array.Empty<Claim>() : new[] { new Claim("sid", sid) },
            authenticationType: "test");
        http.User = new ClaimsPrincipal(identity);

        if (ip is not null)
        {
            http.Connection.RemoteIpAddress = IPAddress.Parse(ip);
        }

        if (userAgent is not null)
        {
            http.Request.Headers.UserAgent = userAgent;
        }

        return new LiveLearnerController { ControllerContext = new ControllerContext { HttpContext = http } };
    }

    private LiveSessionContext ReadySession(bool enrolled = true, bool withMeeting = true, DateTime? starts = null)
    {
        var context = _h.AddSession(InstructorId, startsAtUtc: starts ?? LiveTestData.Now.AddMinutes(5));
        if (enrolled)
        {
            _h.Enroll(LearnerId, context.CourseId);
        }

        if (withMeeting)
        {
            _h.AddMeeting(context.SessionId);
        }

        return context;
    }

    private static void AssertNoStore(ControllerBase controller)
    {
        var headers = controller.Response.Headers;
        Assert.Equal("no-store, no-cache", headers.CacheControl.ToString());
        Assert.Equal("no-cache", headers.Pragma.ToString());
    }

    [Fact]
    public async Task Join_Success_IsOk_NoStore_AndTheLogCarriesTheSidIpAndUserAgent()
    {
        var context = ReadySession();
        var sid = Guid.NewGuid();
        var controller = ControllerFor(sid.ToString());

        var result = await controller.Join(context.SessionId, _h.JoinService(), new FakeUserContext(LearnerId), CancellationToken.None);

        var ok = Assert.IsType<Ok<JoinLiveSessionResponse>>(result);
        Assert.Equal(JoinHarness.RoomUrl, ok.Value!.MeetUrl);
        AssertNoStore(controller);
        Assert.False(controller.Response.Headers.ContainsKey("Retry-After"));

        var row = Assert.Single(_h.JoinLogs.Committed);
        Assert.Equal(sid, row.AUTH_SESSION_ID);
        Assert.Equal("198.51.100.9", row.IP_ADDRESS);
        Assert.Equal("UnitTest/1.0", row.USER_AGENT);
        Assert.Equal(LearnerId, row.USER_ID); // from IUserContext, never from the request
    }

    [Theory]
    [InlineData(null)]
    [InlineData("not-a-guid")]
    [InlineData("")]
    public async Task Join_AMissingOrMalformedSidClaim_IsRecordedAsNull_NotAnError(string? sid)
    {
        var context = ReadySession();
        var controller = ControllerFor(sid);

        var result = await controller.Join(context.SessionId, _h.JoinService(), new FakeUserContext(LearnerId), CancellationToken.None);

        Assert.IsType<Ok<JoinLiveSessionResponse>>(result);
        Assert.Null(Assert.Single(_h.JoinLogs.Committed).AUTH_SESSION_ID);
    }

    [Fact]
    public async Task Join_NoAddressOrAgent_StillWorks()
    {
        var context = ReadySession();
        var controller = ControllerFor(ip: null, userAgent: null);

        var result = await controller.Join(context.SessionId, _h.JoinService(), new FakeUserContext(LearnerId), CancellationToken.None);

        Assert.IsType<Ok<JoinLiveSessionResponse>>(result);
        var row = Assert.Single(_h.JoinLogs.Committed);
        Assert.Null(row.IP_ADDRESS);
        Assert.Null(row.USER_AGENT);
    }

    [Fact]
    public async Task Join_NotFound_Is404_WithNoStoreHeadersToo()
    {
        var context = ReadySession(enrolled: false);
        var controller = ControllerFor();

        var result = await controller.Join(context.SessionId, _h.JoinService(), new FakeUserContext(LearnerId), CancellationToken.None);

        var problem = Assert.IsType<ProblemHttpResult>(result);
        Assert.Equal(StatusCodes.Status404NotFound, problem.StatusCode);
        Assert.Equal(LiveReasons.NotFound, problem.ProblemDetails.Extensions["reason"]);
        AssertNoStore(controller);
        Assert.False(controller.Response.Headers.ContainsKey("Retry-After"));
    }

    [Fact]
    public async Task Join_WindowNotOpen_Is409_WithTheOpeningTime_AndNoStore()
    {
        var context = ReadySession(starts: LiveTestData.Now.AddDays(1));
        var controller = ControllerFor();

        var result = await controller.Join(context.SessionId, _h.JoinService(), new FakeUserContext(LearnerId), CancellationToken.None);

        var problem = Assert.IsType<ProblemHttpResult>(result);
        Assert.Equal(StatusCodes.Status409Conflict, problem.StatusCode);
        Assert.Equal(LiveReasons.WindowNotOpen, problem.ProblemDetails.Extensions["reason"]);
        Assert.True(problem.ProblemDetails.Extensions.ContainsKey("opensAtUtc"));
        Assert.True(problem.ProblemDetails.Extensions.ContainsKey("serverTimeUtc"));
        AssertNoStore(controller);
    }

    [Fact]
    public async Task Join_RoomNotReady_Is503_WithRetryAfter30_AndNoStore()
    {
        var context = ReadySession(withMeeting: false);
        var controller = ControllerFor();

        var result = await controller.Join(context.SessionId, _h.JoinService(), new FakeUserContext(LearnerId), CancellationToken.None);

        var problem = Assert.IsType<ProblemHttpResult>(result);
        Assert.Equal(StatusCodes.Status503ServiceUnavailable, problem.StatusCode);
        Assert.Equal(LiveReasons.MeetingNotReady, problem.ProblemDetails.Extensions["reason"]);
        Assert.Equal("30", controller.Response.Headers.RetryAfter.ToString());
        AssertNoStore(controller);
    }

    [Fact]
    public async Task Join_Anonymous_IsUnauthorized_AndNeverReachesTheService()
    {
        var context = ReadySession();
        var controller = ControllerFor();

        var result = await controller.Join(context.SessionId, _h.JoinService(), new FakeUserContext(null), CancellationToken.None);

        Assert.IsType<UnauthorizedHttpResult>(result);
        Assert.Empty(_h.JoinLogs.Committed);
        Assert.Equal(0, _h.Learning.HasActiveEnrollmentCalls);
    }

    // ---- calendar.ics ------------------------------------------------------------------------------------------

    [Fact]
    public async Task Calendar_Success_IsATextCalendarAttachment_WithTheExactDispositionAndNoStore()
    {
        var courseId = Guid.NewGuid();
        var context = _h.AddSession(InstructorId, courseId: courseId, startsAtUtc: LiveTestData.Now.AddDays(2));
        _h.Enroll(LearnerId, courseId);
        var controller = ControllerFor();

        var result = await controller.GetSessionCalendar(context.SessionId, _h.LearnerQueries(), new FakeUserContext(LearnerId), CancellationToken.None);

        var file = Assert.IsType<FileContentHttpResult>(result);
        Assert.Equal("text/calendar; charset=utf-8; method=PUBLISH", file.ContentType);
        Assert.Equal($"attachment; filename=\"live-{context.SessionId:N}.ics\"", controller.Response.Headers.ContentDisposition.ToString());
        AssertNoStore(controller);
        Assert.StartsWith("BEGIN:VCALENDAR", Encoding.UTF8.GetString(file.FileContents.Span));
    }

    [Fact]
    public async Task Calendar_NotEntitled_Is404_AndEndedIs409()
    {
        var stranger = _h.AddSession(InstructorId, startsAtUtc: LiveTestData.Now.AddDays(2));
        var courseId = Guid.NewGuid();
        var ended = _h.AddSession(InstructorId, courseId: courseId, startsAtUtc: LiveTestData.Now.AddDays(-2), endsAtUtc: LiveTestData.Now.AddDays(-2).AddHours(2));
        _h.Enroll(LearnerId, courseId);

        var notFound = await ControllerFor().GetSessionCalendar(stranger.SessionId, _h.LearnerQueries(), new FakeUserContext(LearnerId), CancellationToken.None);
        var gone = await ControllerFor().GetSessionCalendar(ended.SessionId, _h.LearnerQueries(), new FakeUserContext(LearnerId), CancellationToken.None);

        Assert.Equal(StatusCodes.Status404NotFound, Assert.IsType<ProblemHttpResult>(notFound).StatusCode);
        Assert.Equal(StatusCodes.Status409Conflict, Assert.IsType<ProblemHttpResult>(gone).StatusCode);
    }

    // ---- The instructor controller ------------------------------------------------------------------------------

    private static LiveInstructorSessionsController InstructorControllerFor() =>
        new() { ControllerContext = new ControllerContext { HttpContext = new DefaultHttpContext() } };

    [Fact]
    public async Task InstructorDetail_Owner_IsOkWithTheLink_AndNoStore()
    {
        var session = _h.AddSession(InstructorId, startsAtUtc: LiveTestData.Now.AddDays(1));
        _h.AddMeeting(session.SessionId);
        var controller = InstructorControllerFor();

        var result = await controller.GetSession(session.SessionId, _h.InstructorQueries(), new FakeUserContext(InstructorId), CancellationToken.None);

        var ok = Assert.IsType<Ok<InstructorSessionDetailResponse>>(result);
        Assert.Equal(JoinHarness.RoomUrl, ok.Value!.MeetUrl);
        AssertNoStore(controller);
    }

    [Fact]
    public async Task InstructorDetail_NotTheOwner_Is403_NotTheLink_AndUnknownIs404()
    {
        var session = _h.AddSession(Guid.NewGuid(), startsAtUtc: LiveTestData.Now.AddDays(1));
        _h.AddMeeting(session.SessionId);

        var forbidden = await InstructorControllerFor().GetSession(session.SessionId, _h.InstructorQueries(), new FakeUserContext(InstructorId), CancellationToken.None);
        var unknown = await InstructorControllerFor().GetSession(Guid.NewGuid(), _h.InstructorQueries(), new FakeUserContext(InstructorId), CancellationToken.None);

        Assert.Equal(StatusCodes.Status403Forbidden, Assert.IsType<ProblemHttpResult>(forbidden).StatusCode);
        Assert.Equal(StatusCodes.Status404NotFound, Assert.IsType<ProblemHttpResult>(unknown).StatusCode);
    }

    [Fact]
    public async Task InstructorEndpoints_Anonymous_AreUnauthorized()
    {
        var controller = InstructorControllerFor();

        Assert.IsType<UnauthorizedHttpResult>(await controller.GetSessions(_h.InstructorQueries(), new FakeUserContext(null)));
        Assert.IsType<UnauthorizedHttpResult>(await controller.GetSession(Guid.NewGuid(), _h.InstructorQueries(), new FakeUserContext(null), CancellationToken.None));
        Assert.IsType<UnauthorizedHttpResult>(await controller.GetRoster(Guid.NewGuid(), _h.InstructorQueries(), new FakeUserContext(null)));
    }

    [Fact]
    public async Task LearnerListEndpoints_Anonymous_AreUnauthorized()
    {
        var controller = ControllerFor();

        Assert.IsType<UnauthorizedHttpResult>(await controller.GetMySessions(Guid.NewGuid(), _h.LearnerQueries(), new FakeUserContext(null), CancellationToken.None));
        Assert.IsType<UnauthorizedHttpResult>(await controller.GetMyUpcomingSessions(_h.LearnerQueries(), new FakeUserContext(null)));
    }

    [Fact]
    public async Task LearnerListEndpoints_AreNoStore()
    {
        var controller = ControllerFor();

        await controller.GetMyUpcomingSessions(_h.LearnerQueries(), new FakeUserContext(LearnerId));

        AssertNoStore(controller);
    }
}
