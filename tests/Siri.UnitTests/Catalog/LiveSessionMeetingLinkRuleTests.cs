using FluentValidation;
using FluentValidation.TestHelper;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.Abstractions;
using Microsoft.AspNetCore.Mvc.Filters;
using Microsoft.AspNetCore.Routing;
using Microsoft.Extensions.DependencyInjection;
using Siri.Modules.Catalog.Contracts;
using Siri.Modules.Catalog.Domain;
using Siri.Modules.Catalog.Features.CancelLiveSession;
using Siri.Modules.Catalog.Features.CreateLiveSession;
using Siri.Modules.Catalog.Features.GetCourseDetail;
using Siri.Modules.Catalog.Features.UpdateLiveSession;
using Siri.Modules.Catalog.Infrastructure;
using Siri.SharedKernel;

namespace Siri.UnitTests.Catalog;

/// <summary>
/// D3 (integrator-qa): a live session's title/description/cancel reason must never carry a Meet/Zoom/Teams link — they are shown on the PUBLIC course
/// detail and mailed to every learner. Write side: the three validators reject (400, stable reason). Read side: the public detail mapper and the
/// schedule reader scrub rows stored before the rule existed.
/// </summary>
public class LiveSessionMeetingLinkRuleTests
{
    private const string Reason = "live.session_text_contains_meeting_link";

    private static readonly DateTime Start = new(2026, 10, 1, 9, 0, 0, DateTimeKind.Utc);
    private static readonly DateTime End = new(2026, 10, 1, 11, 0, 0, DateTimeKind.Utc);

    private readonly CreateLiveSessionCommandValidator _create = new();
    private readonly UpdateLiveSessionCommandValidator _update = new();
    private readonly CancelLiveSessionCommandValidator _cancel = new();

    public static TheoryData<string> Bypasses => new()
    {
        "https://meet.google.com/abc-defg-hij",
        "เข้าห้องที่ https://zoom.us/j/987654321?pwd=SECRET ครับ",
        "https://us02web.zoom.us/j/1",
        "https://teams.microsoft.com/l/meetup-join/19%3ameeting_x/0",
        "https://teams.live.com/meet/9876543210",
        "meet.google.com/abc-defg-hij", // scheme-less
        "MEET.GOOGLE.COM/ABC-DEFG-HIJ", // case
        "https%3A%2F%2Fmeet.google.com%2Fabc-defg-hij", // encoded
        "meet%2Egoogle%2Ecom%2Fabc-defg-hij",
        "meet.goo\u200Bgle.com/abc-defg-hij", // zero-width inside the host
        "meet.goo\tgle.com/abc-defg-hij", // tab inside the host
        "ｍｅｅｔ.ｇｏｏｇｌｅ.ｃｏｍ/abc-defg-hij", // full-width
    };

    // ---- Create -----------------------------------------------------------------------------------------

    [Theory]
    [MemberData(nameof(Bypasses))]
    public void Create_MeetingLinkInTitle_IsRejected_WithTheStableReason(string text)
    {
        var result = _create.TestValidate(new CreateLiveSessionCommand(text, null, Start, End));

        result.ShouldHaveValidationErrorFor(x => x.Title).WithErrorCode(Reason);
    }

    [Theory]
    [MemberData(nameof(Bypasses))]
    public void Create_MeetingLinkInDescription_IsRejected_WithTheStableReason(string text)
    {
        var result = _create.TestValidate(new CreateLiveSessionCommand("คาบที่ 1", text, Start, End));

        result.ShouldHaveValidationErrorFor(x => x.Description).WithErrorCode(Reason);
        result.ShouldNotHaveValidationErrorFor(x => x.Title);
    }

    [Theory]
    [InlineData("คาบที่ 1 — แนะนำคอร์ส", "สรุปบทที่ 1 พร้อม Q&A")]
    [InlineData("Live Q&A", "ดูสไลด์ที่ https://example.com/slides/week-1.pdf")]
    [InlineData("Week 3", "เรียนผ่านแพลตฟอร์ม https://app.example.test/live/0b6f/join")]
    [InlineData("Zoom, Teams และ Google Meet แตกต่างกันอย่างไร", null)] // product names are not links
    [InlineData("Intro", "https://evilzoom.us/j/1")] // look-alike domain is not a meeting host
    public void Create_OrdinaryTextAndGenericLinks_AreAccepted(string title, string? description)
    {
        var result = _create.TestValidate(new CreateLiveSessionCommand(title, description, Start, End));

        result.ShouldNotHaveAnyValidationErrors();
    }

    [Fact]
    public void Create_TitleTooLong_ReportsOnlyTheLength_AndTheLinkScanNeverRunsOnOversizedText()
    {
        var result = _create.TestValidate(new CreateLiveSessionCommand(new string('a', 201) + " https://zoom.us/j/1", null, Start, End));

        var failure = Assert.Single(result.Errors, e => e.PropertyName == nameof(CreateLiveSessionCommand.Title));
        Assert.Equal("MaximumLengthValidator", failure.ErrorCode);
    }

    // ---- Update -----------------------------------------------------------------------------------------

    [Theory]
    [MemberData(nameof(Bypasses))]
    public void Update_MeetingLinkInTitleOrDescription_IsRejected_WithTheStableReason(string text)
    {
        _update.TestValidate(new UpdateLiveSessionCommand(text, null, Start, End))
            .ShouldHaveValidationErrorFor(x => x.Title).WithErrorCode(Reason);
        _update.TestValidate(new UpdateLiveSessionCommand("คาบที่ 1", text, Start, End))
            .ShouldHaveValidationErrorFor(x => x.Description).WithErrorCode(Reason);
    }

    [Fact]
    public void Update_OrdinaryText_IsAccepted()
    {
        _update.TestValidate(new UpdateLiveSessionCommand("Updated title", "Updated description https://example.com/x", Start, End))
            .ShouldNotHaveAnyValidationErrors();
    }

    // ---- Cancel -----------------------------------------------------------------------------------------

    [Theory]
    [MemberData(nameof(Bypasses))]
    public void Cancel_MeetingLinkInReason_IsRejected_WithTheStableReason(string text)
    {
        _cancel.TestValidate(new CancelLiveSessionCommand(text))
            .ShouldHaveValidationErrorFor(x => x.Reason).WithErrorCode(Reason);
    }

    [Fact]
    public void Cancel_OrdinaryReason_IsAccepted()
    {
        _cancel.TestValidate(new CancelLiveSessionCommand("ผู้สอนติดภารกิจเร่งด่วน — เลื่อนไปสัปดาห์หน้า"))
            .ShouldNotHaveAnyValidationErrors();
    }

    // ---- The reason reaches the client -------------------------------------------------------------------

    private sealed record Probe(string Title);

    private sealed class ProbeValidator : AbstractValidator<Probe>
    {
        public ProbeValidator()
        {
            RuleFor(p => p.Title).NotEmpty().WithMessage("Title is required.");
        }
    }

    private static async Task<ActionExecutingContext> RunFilterAsync(object argument)
    {
        var services = new ServiceCollection()
            .AddScoped<IValidator<CreateLiveSessionCommand>, CreateLiveSessionCommandValidator>()
            .AddScoped<IValidator<Probe>, ProbeValidator>()
            .BuildServiceProvider();
        var httpContext = new DefaultHttpContext { RequestServices = services, TraceIdentifier = "trace-123" };
        var actionContext = new ActionContext(httpContext, new RouteData(), new ActionDescriptor());
        var executing = new ActionExecutingContext(
            actionContext,
            [],
            new Dictionary<string, object?> { ["command"] = argument },
            controller: new object());

        await new ValidationActionFilter().OnActionExecutionAsync(
            executing,
            () => Task.FromResult(new ActionExecutedContext(actionContext, [], new object())));
        return executing;
    }

    [Fact]
    public async Task ValidationFilter_AMeetingLinkRejection_CarriesErrorCodeReasonAndTraceId_AndTheFieldError()
    {
        var executing = await RunFilterAsync(new CreateLiveSessionCommand("https://meet.google.com/abc-defg-hij", null, Start, End));

        var bad = Assert.IsType<BadRequestObjectResult>(executing.Result);
        Assert.Equal(StatusCodes.Status400BadRequest, bad.StatusCode);
        var problem = Assert.IsType<ValidationProblemDetails>(bad.Value);
        Assert.Equal("validation", problem.Extensions["errorCode"]);
        Assert.Equal(Reason, problem.Extensions["reason"]);
        Assert.Equal("trace-123", problem.Extensions["traceId"]);
        Assert.Contains("Title", problem.Errors.Keys, StringComparer.OrdinalIgnoreCase);
    }

    [Fact]
    public async Task ValidationFilter_AnOrdinaryRuleFailure_IsUnchanged_NoReasonExtension()
    {
        var executing = await RunFilterAsync(new Probe(string.Empty));

        var problem = Assert.IsType<ValidationProblemDetails>(Assert.IsType<BadRequestObjectResult>(executing.Result).Value);
        Assert.False(problem.Extensions.ContainsKey("reason"));
        Assert.False(problem.Extensions.ContainsKey("errorCode"));
        Assert.False(problem.Extensions.ContainsKey("traceId"));
    }

    [Fact]
    public async Task ValidationFilter_ValidCommand_ContinuesToTheAction()
    {
        var executing = await RunFilterAsync(new CreateLiveSessionCommand("คาบที่ 1", "รายละเอียด", Start, End));

        Assert.Null(executing.Result);
    }

    // ---- Read side: already-stored rows are scrubbed -------------------------------------------------------

    private static COURSE_LIVE_SESSION StoredSession(string title)
    {
        var course = COURSE.Create("live-course", "Live course", Guid.NewGuid(), Guid.NewGuid(), CourseLevel.Beginner, CourseLanguage.Thai, 990m);
        course.SetDeliveryFormat(DeliveryFormat.Live);
        var clock = new FakeClock(new DateTime(2026, 9, 16, 10, 0, 0, DateTimeKind.Utc));
        // The domain method does not know about meeting links (the validator is the write-side gate) — this is exactly a row stored before the rule.
        return course.AddLiveSession(title, "รายละเอียด", clock.UtcNow.AddDays(1), clock.UtcNow.AddDays(1).AddHours(2), clock);
    }

    [Theory]
    [InlineData("เข้าห้อง https://meet.google.com/abc-defg-hij นะ")]
    [InlineData("zoom.us/j/987654321?pwd=SECRET")]
    [InlineData("meet.goo\u200Bgle.com/abc-defg-hij")]
    [InlineData("https%3A%2F%2Fmeet.google.com%2Fabc-defg-hij")]
    public void PublicDetail_AStoredTitleWithAMeetingLink_IsScrubbed(string storedTitle)
    {
        var detail = GetCourseDetailHandler.ToDetailSession(StoredSession(storedTitle), new DateTime(2026, 9, 16, 10, 0, 0, DateTimeKind.Utc));

        Assert.Contains(MeetingLinkText.Placeholder, detail.Title);
        Assert.DoesNotContain("abc-defg-hij", detail.Title);
        Assert.DoesNotContain("987654321", detail.Title);
        Assert.False(MeetingLinkText.ContainsLink(detail.Title));
    }

    [Fact]
    public void PublicDetail_AnOrdinaryTitle_IsUnchanged()
    {
        var detail = GetCourseDetailHandler.ToDetailSession(StoredSession("คาบที่ 1 — แนะนำคอร์ส"), new DateTime(2026, 9, 16, 10, 0, 0, DateTimeKind.Utc));

        Assert.Equal("คาบที่ 1 — แนะนำคอร์ส", detail.Title);
    }

    [Fact]
    public void ScheduleReader_Context_ScrubsTitleDescriptionAndCancelReason()
    {
        var context = LiveScheduleReader.ToContext(
            Guid.NewGuid(), Guid.NewGuid(), "คอร์ส", "slug",
            "ห้อง https://zoom.us/j/111", "ลิงก์ meet.google.com/aaa-bbbb-ccc", Start, End, CourseLiveSessionStatus.Cancelled,
            "ย้ายไป https://teams.live.com/meet/222", null, Guid.NewGuid(), Guid.NewGuid(), "ผู้สอน", googleAttendeeSyncEnabled: false);

        Assert.DoesNotContain("zoom.us", context.Title);
        Assert.DoesNotContain("meet.google.com", context.Description);
        Assert.DoesNotContain("teams.live.com", context.CancelReason);
        Assert.Contains(MeetingLinkText.Placeholder, context.Title);
        Assert.Equal(LiveSessionStatus.Cancelled, context.Status);
        Assert.Equal("คอร์ส", context.CourseTitle); // only the instructor free text is touched
    }

    [Fact]
    public void ScheduleReader_Context_LeavesOrdinaryTextAndNullsAlone()
    {
        var context = LiveScheduleReader.ToContext(
            Guid.NewGuid(), Guid.NewGuid(), "คอร์ส", "slug", "คาบที่ 1", null, Start, End, CourseLiveSessionStatus.Scheduled,
            null, null, Guid.NewGuid(), Guid.NewGuid(), "ผู้สอน", googleAttendeeSyncEnabled: true);

        Assert.Equal("คาบที่ 1", context.Title);
        Assert.Null(context.Description);
        Assert.Null(context.CancelReason);
    }

    [Fact]
    public void ScheduleReader_SessionInfo_ScrubsTheTitle()
    {
        var info = LiveScheduleReader.ToLiveSessionInfo(StoredSession("ที่ https://meet.google.com/abc-defg-hij"));

        Assert.DoesNotContain("meet.google.com", info.Title);
    }
}
