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
using Siri.Modules.Commerce.Domain;
using Siri.Modules.Commerce.Infrastructure;
using Siri.Modules.Identity.Domain;
using Siri.Modules.Identity.Infrastructure;
using Siri.Modules.Learning.Domain;
using Siri.Modules.Learning.Infrastructure;
using Siri.Modules.Live.Domain;
using Siri.Modules.Live.Infrastructure;
using Siri.Persistence;
using Siri.SharedKernel;

namespace Siri.IntegrationTests;

/// <summary>
/// P11-12 (docs/contracts/P11-12-refund-hard-block-after-live-join.md section 5) end to end over real HTTP against the production composition root
/// (<see cref="SiriApiFactory"/>: real PostgreSQL + Redis from Testcontainers). A learner who has been handed the room link of a live session of a course in an
/// order can no longer get that course's share refunded — <c>POST /api/commerce/refunds</c> answers <b>409 <c>refund.live_attended</c></b> and creates nothing —
/// while the other courses of the same order stay refundable up to the ceiling the server computes. What this proves:
/// <list type="bullet">
/// <item>the join really writes the evidence row the refund rule reads (the learner joins through the real <c>POST …/join</c> endpoint);</item>
/// <item>the exact JSON of the 409 (appendix section D) and the ceiling to the satang: a Live course of 1,000 and a recorded course of 500 in one payment of 1,500 —
/// 500 goes through (201), 500.01 and 1,500 do not;</item>
/// <item>a learner who never joined, and a buyer whose only "join" was as the course's <em>instructor</em>, are not blocked;</item>
/// <item>only the order's owner can ask (the unchanged 403) and the admin approve path is unchanged — a request filed before the learner entered a room can still be approved after.</item>
/// </list>
/// The paid state (order, succeeded payment, enrollments) is written directly: it is exactly what the Stripe webhook fulfillment leaves behind and is covered by
/// <c>PaymentFulfillmentIntegrationTests</c>; this class is about what the refund endpoint does with it.
/// <para>
/// Requires Docker like every test in this collection — without it these end in <c>DockerUnavailableException</c> (not an assertion).
/// </para>
/// </summary>
[Collection(ContainersCollection.Name)]
public sealed class RefundLiveAttendanceIntegrationTests : IAsyncLifetime
{
    private const string RoomUrl = "https://zoom.us/j/987654321?pwd=REFUNDTESTSECRET";
    private const string LiveTitle = "คอร์สสดสำหรับทดสอบ"; // CreateLiveCourseAsync's fixed course title — what the order line snapshot carries below

    private readonly ContainersFixture _containers;
    private SiriApiFactory _factory = null!;
    private HttpClient _client = null!;

    public RefundLiveAttendanceIntegrationTests(ContainersFixture containers)
    {
        _containers = containers;
    }

    public async Task InitializeAsync()
    {
        _factory = new SiriApiFactory(_containers);

        await using var scope = _factory.Services.CreateAsyncScope();
        await scope.ServiceProvider.GetRequiredService<AppDbContext>().Database.MigrateAsync();

        _client = _factory.CreateClient();
    }

    public async Task DisposeAsync()
    {
        _client.Dispose();
        await _factory.DisposeAsync();
    }

    // ---- Arrange helpers ------------------------------------------------------------------------------------

    private sealed record Actor(Guid UserId, string Token);

    private sealed record Reply(HttpStatusCode Status, string Body)
    {
        public JsonDocument Json => JsonDocument.Parse(string.IsNullOrWhiteSpace(Body) ? "{}" : Body);
    }

    /// <summary>An instructor (approved profile), a Live course whose one session starts in five minutes (inside the join window) with a pasted room link, and an OnDemand course.</summary>
    private sealed record Scene(Actor Instructor, Guid LiveCourseId, Guid RecordedCourseId, Guid SessionId);

    private async Task<Actor> CreateActorAsync(string roleName)
    {
        var builder = new TestUserBuilder().WithRole(roleName);

        await using var scope = _factory.Services.CreateAsyncScope();
        var user = await builder.BuildAsync(scope.ServiceProvider);
        var (token, _) = scope.ServiceProvider.GetRequiredService<IAccessTokenGenerator>().Generate(user, Guid.NewGuid());

        return new Actor(user.Id, token);
    }

    private async Task<Scene> CreateSceneAsync()
    {
        var builder = new TestUserBuilder().WithRole(ROLE.InstructorName);
        Actor instructor;
        Guid profileId;
        await using (var scope = _factory.Services.CreateAsyncScope())
        {
            var user = await builder.BuildAsync(scope.ServiceProvider);
            var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
            var clock = scope.ServiceProvider.GetRequiredService<IClock>();

            var profile = INSTRUCTOR_PROFILE.Apply(user.Id, "Test Instructor", "Headline", "Bio");
            profile.Approve(clock);
            db.InstructorProfiles().Add(profile);
            await db.SaveChangesAsync();

            var (token, _) = scope.ServiceProvider.GetRequiredService<IAccessTokenGenerator>().Generate(user, Guid.NewGuid());
            instructor = new Actor(user.Id, token);
            profileId = profile.Id;
        }

        var (liveCourseId, _) = await LiveIntegrationSupport.CreateLiveCourseAsync(_factory, profileId);
        var (recordedCourseId, _) = await LiveIntegrationSupport.CreateLiveCourseAsync(_factory, profileId, DeliveryFormat.OnDemand);
        var sessionId = await LiveIntegrationSupport.CreateSessionAsync(_client, instructor.Token, liveCourseId, daysAhead: 2);

        var link = await SendAsync(HttpMethod.Put, $"/api/live/instructor/sessions/{sessionId}/meeting-link", instructor, new { meetUrl = RoomUrl });
        Assert.Equal(HttpStatusCode.OK, link.Status);

        await MoveSessionAsync(sessionId, DateTime.UtcNow.AddMinutes(5));
        return new Scene(instructor, liveCourseId, recordedCourseId, sessionId);
    }

    private async Task MoveSessionAsync(Guid sessionId, DateTime startsAtUtc)
    {
        await using var scope = _factory.Services.CreateAsyncScope();
        var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();

        var session = await db.CourseLiveSessions().SingleAsync(s => s.Id == sessionId);
        db.Entry(session).Property(s => s.StartsAtUtc).CurrentValue = startsAtUtc;
        db.Entry(session).Property(s => s.EndsAtUtc).CurrentValue = startsAtUtc.AddHours(2);
        await db.SaveChangesAsync();
    }

    /// <summary>The state the Stripe webhook leaves behind: a Paid order, a Succeeded payment and an active enrollment for every course line.</summary>
    private async Task<(Guid OrderId, Guid PaymentId)> CreatePaidOrderAsync(
        Guid buyerUserId, decimal paymentAmount, params (Guid CourseId, string Title, decimal LineTotal)[] lines)
    {
        await using var scope = _factory.Services.CreateAsyncScope();
        var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
        var clock = scope.ServiceProvider.GetRequiredService<IClock>();

        var order = ORDER.Create($"RF-{Guid.NewGuid():N}"[..20], buyerUserId, lines.Sum(l => l.LineTotal), 0m, 0m, paymentAmount);
        foreach (var (courseId, title, lineTotal) in lines)
        {
            order.AddItem(courseId, title, lineTotal, lineTotal);
        }

        order.MarkAwaitingPayment();
        order.MarkPaid(clock);
        db.Orders().Add(order);

        var payment = PAYMENT.Create(order.ORDER_ID, PaymentMethod.PromptPay, $"pi_{Guid.NewGuid():N}", paymentAmount, clock);
        payment.MarkSucceeded(clock);
        db.Payments().Add(payment);

        foreach (var (courseId, _, _) in lines)
        {
            db.Enrollments().Add(ENROLLMENT.Create(buyerUserId, courseId, order.ORDER_ID, EnrollmentSource.Purchase, null, clock));
        }

        await db.SaveChangesAsync();
        return (order.ORDER_ID, payment.PAYMENT_ID);
    }

    private async Task<Reply> SendAsync(HttpMethod method, string uri, Actor? actor, object? json = null)
    {
        using var request = new HttpRequestMessage(method, uri);
        if (actor is not null)
        {
            request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", actor.Token);
        }

        if (json is not null)
        {
            request.Content = JsonContent.Create(json);
        }

        using var response = await _client.SendAsync(request);
        return new Reply(response.StatusCode, await response.Content.ReadAsStringAsync());
    }

    private Task<Reply> JoinAsync(Actor actor, Guid sessionId) => SendAsync(HttpMethod.Post, $"/api/live/sessions/{sessionId}/join", actor);

    private Task<Reply> RefundAsync(Actor? actor, Guid paymentId, decimal amount) =>
        SendAsync(HttpMethod.Post, "/api/commerce/refunds", actor, new { paymentId, amount, reason = "เนื้อหาไม่ตรงกับที่คาดหวัง" });

    private async Task<int> RefundCountAsync(Guid paymentId)
    {
        await using var scope = _factory.Services.CreateAsyncScope();
        var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
        return await db.Refunds().AsNoTracking().CountAsync(r => r.PAYMENT_ID == paymentId);
    }

    private async Task<int> JoinLogCountAsync(Guid sessionId, Guid userId)
    {
        await using var scope = _factory.Services.CreateAsyncScope();
        var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
        return await db.SessionJoinLogs().AsNoTracking().CountAsync(l => l.SESSION_ID == sessionId && l.USER_ID == userId);
    }

    private static void AssertLiveAttended(Reply reply, decimal maxRefundable, decimal blockedAmount, params string[] blockedTitles)
    {
        Assert.Equal(HttpStatusCode.Conflict, reply.Status);
        using var json = reply.Json;
        var root = json.RootElement;
        Assert.Equal(409, root.GetProperty("status").GetInt32());
        Assert.Equal("conflict", root.GetProperty("errorCode").GetString());
        Assert.Equal("refund.live_attended", root.GetProperty("reason").GetString());
        Assert.False(string.IsNullOrWhiteSpace(root.GetProperty("title").GetString()));
        Assert.False(string.IsNullOrWhiteSpace(root.GetProperty("traceId").GetString()));
        Assert.Equal(maxRefundable, root.GetProperty("maxRefundableAmount").GetDecimal());
        Assert.Equal(blockedAmount, root.GetProperty("blockedAmount").GetDecimal());
        Assert.Equal(blockedTitles, root.GetProperty("blockedCourseTitles").EnumerateArray().Select(e => e.GetString()).ToArray());

        // Nothing about the session itself leaks: no id, time or instructor — only the buyer's own course titles and two amounts.
        foreach (var name in root.EnumerateObject().Select(p => p.Name))
        {
            Assert.DoesNotContain("session", name, StringComparison.OrdinalIgnoreCase);
            Assert.DoesNotContain("instructor", name, StringComparison.OrdinalIgnoreCase);
            Assert.DoesNotContain("meet", name, StringComparison.OrdinalIgnoreCase);
        }

        Assert.DoesNotContain("zoom.us", reply.Body, StringComparison.OrdinalIgnoreCase);
    }

    // ---- The hard block ----------------------------------------------------------------------------------------

    [Fact]
    public async Task Refund_AfterTheLearnerJoinedTheLiveRoom_IsBlockedForTheLiveCoursesShare_ButTheRecordedCoursesShareStillGoesThrough()
    {
        var scene = await CreateSceneAsync();
        var learner = await CreateActorAsync(ROLE.LearnerName);
        var (_, paymentId) = await CreatePaidOrderAsync(
            learner.UserId, 1500m, (scene.LiveCourseId, LiveTitle, 1000m), (scene.RecordedCourseId, "คอร์สบันทึก", 500m));

        var join = await JoinAsync(learner, scene.SessionId);
        Assert.Equal(HttpStatusCode.OK, join.Status);
        Assert.Equal(1, await JoinLogCountAsync(scene.SessionId, learner.UserId)); // the evidence the rule reads exists by the time the link was handed out

        var full = await RefundAsync(learner, paymentId, 1500m);
        var oneSatangOver = await RefundAsync(learner, paymentId, 500.01m);

        AssertLiveAttended(full, maxRefundable: 500m, blockedAmount: 1000m, LiveTitle);
        AssertLiveAttended(oneSatangOver, maxRefundable: 500m, blockedAmount: 1000m, LiveTitle);
        Assert.Equal(0, await RefundCountAsync(paymentId)); // a blocked request creates nothing

        var allowed = await RefundAsync(learner, paymentId, 500m);

        Assert.Equal(HttpStatusCode.Created, allowed.Status);
        using var json = allowed.Json;
        Assert.Equal(500m, json.RootElement.GetProperty("amount").GetDecimal());
        Assert.Equal("Requested", json.RootElement.GetProperty("status").GetString());
        Assert.Equal(1, await RefundCountAsync(paymentId));
    }

    [Fact]
    public async Task Refund_SingleLiveCourseOrderAfterTheJoin_EvenOneSatangIsRefused()
    {
        var scene = await CreateSceneAsync();
        var learner = await CreateActorAsync(ROLE.LearnerName);
        var (_, paymentId) = await CreatePaidOrderAsync(learner.UserId, 1000m, (scene.LiveCourseId, LiveTitle, 1000m));
        Assert.Equal(HttpStatusCode.OK, (await JoinAsync(learner, scene.SessionId)).Status);

        var reply = await RefundAsync(learner, paymentId, 0.01m);

        AssertLiveAttended(reply, maxRefundable: 0m, blockedAmount: 1000m, LiveTitle);
        Assert.Equal(0, await RefundCountAsync(paymentId));
    }

    [Fact]
    public async Task Refund_LearnerWhoNeverJoinedAnyRoom_IsNotBlocked_EvenForTheFullAmount()
    {
        var scene = await CreateSceneAsync();
        var joiner = await CreateActorAsync(ROLE.LearnerName);
        var abstainer = await CreateActorAsync(ROLE.LearnerName);
        await CreatePaidOrderAsync(joiner.UserId, 1000m, (scene.LiveCourseId, LiveTitle, 1000m));
        var (_, abstainerPayment) = await CreatePaidOrderAsync(abstainer.UserId, 1000m, (scene.LiveCourseId, LiveTitle, 1000m));
        Assert.Equal(HttpStatusCode.OK, (await JoinAsync(joiner, scene.SessionId)).Status); // someone else joining the same class changes nothing for this buyer

        var reply = await RefundAsync(abstainer, abstainerPayment, 1000m);

        Assert.Equal(HttpStatusCode.Created, reply.Status);
        Assert.Equal(1, await RefundCountAsync(abstainerPayment));
    }

    [Fact]
    public async Task Refund_BuyerWhoseOnlyJoinWasAsTheCoursesInstructor_IsNotBlocked()
    {
        // The instructor is also the buyer of an order containing their own course (their join is logged with the Instructor role, which never counts).
        var scene = await CreateSceneAsync();
        var (_, paymentId) = await CreatePaidOrderAsync(scene.Instructor.UserId, 1000m, (scene.LiveCourseId, LiveTitle, 1000m));
        Assert.Equal(HttpStatusCode.OK, (await JoinAsync(scene.Instructor, scene.SessionId)).Status);
        await using (var scope = _factory.Services.CreateAsyncScope())
        {
            var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
            var row = await db.SessionJoinLogs().AsNoTracking().SingleAsync(l => l.SESSION_ID == scene.SessionId && l.USER_ID == scene.Instructor.UserId);
            Assert.Equal(LiveParticipantRole.Instructor, row.ROLE);
        }

        var reply = await RefundAsync(scene.Instructor, paymentId, 1000m);

        Assert.Equal(HttpStatusCode.Created, reply.Status);
    }

    [Fact]
    public async Task Refund_OrderWithoutAnyLiveCourse_BehavesAsBefore()
    {
        var scene = await CreateSceneAsync();
        var learner = await CreateActorAsync(ROLE.LearnerName);
        var (_, paymentId) = await CreatePaidOrderAsync(learner.UserId, 500m, (scene.RecordedCourseId, "คอร์สบันทึก", 500m));

        var reply = await RefundAsync(learner, paymentId, 500m);

        Assert.Equal(HttpStatusCode.Created, reply.Status);
    }

    // ---- Unchanged behaviour -------------------------------------------------------------------------------------

    [Fact]
    public async Task Refund_SomeoneElsesPayment_IsStill403_AndTheBlockNeverRevealsAnotherBuyersAttendance()
    {
        var scene = await CreateSceneAsync();
        var buyer = await CreateActorAsync(ROLE.LearnerName);
        var stranger = await CreateActorAsync(ROLE.LearnerName);
        var (_, paymentId) = await CreatePaidOrderAsync(buyer.UserId, 1000m, (scene.LiveCourseId, LiveTitle, 1000m));
        Assert.Equal(HttpStatusCode.OK, (await JoinAsync(buyer, scene.SessionId)).Status);

        var reply = await RefundAsync(stranger, paymentId, 1000m);

        Assert.Equal(HttpStatusCode.Forbidden, reply.Status);
        Assert.DoesNotContain("refund.live_attended", reply.Body, StringComparison.Ordinal);
        Assert.Equal(HttpStatusCode.Unauthorized, (await RefundAsync(actor: null, paymentId, 1000m)).Status);
        Assert.Equal(0, await RefundCountAsync(paymentId));
    }

    [Fact]
    public async Task Refund_AmountOverThePayment_IsStillA400_NotALiveConflict()
    {
        var scene = await CreateSceneAsync();
        var learner = await CreateActorAsync(ROLE.LearnerName);
        var (_, paymentId) = await CreatePaidOrderAsync(learner.UserId, 1000m, (scene.LiveCourseId, LiveTitle, 1000m));
        Assert.Equal(HttpStatusCode.OK, (await JoinAsync(learner, scene.SessionId)).Status);

        var reply = await RefundAsync(learner, paymentId, 1000.01m);

        Assert.Equal(HttpStatusCode.BadRequest, reply.Status);
        Assert.DoesNotContain("refund.live_attended", reply.Body, StringComparison.Ordinal);
    }

    [Fact]
    public async Task AdminApprove_OfARequestFiledBeforeTheLearnerJoined_StillWorks_TheDecisionIsNotReChecked()
    {
        var scene = await CreateSceneAsync();
        var learner = await CreateActorAsync(ROLE.LearnerName);
        var admin = await CreateActorAsync(ROLE.AdminName);
        var (_, paymentId) = await CreatePaidOrderAsync(learner.UserId, 1000m, (scene.LiveCourseId, LiveTitle, 1000m));

        var filed = await RefundAsync(learner, paymentId, 1000m); // before any join: allowed
        Assert.Equal(HttpStatusCode.Created, filed.Status);
        Guid refundId;
        using (var filedJson = filed.Json)
        {
            refundId = filedJson.RootElement.GetProperty("id").GetGuid();
        }

        Assert.Equal(HttpStatusCode.OK, (await JoinAsync(learner, scene.SessionId)).Status); // the learner then enters the room

        var approved = await SendAsync(HttpMethod.Post, $"/api/commerce/admin/refunds/{refundId}/approve", admin, new { decisionNote = "ตรวจสอบแล้ว" });

        Assert.Equal(HttpStatusCode.OK, approved.Status);
        using var approvedJson = approved.Json;
        Assert.Equal("Approved", approvedJson.RootElement.GetProperty("status").GetString());
    }
}
