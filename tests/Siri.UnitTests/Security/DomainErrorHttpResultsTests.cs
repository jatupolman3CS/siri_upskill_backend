using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Http.HttpResults;
using Siri.SharedKernel;

namespace Siri.UnitTests.Security;

public sealed class DomainErrorHttpResultsTests
{
    private static ProblemHttpResult ToProblem(DomainError error) =>
        Assert.IsType<ProblemHttpResult>(error.ToProblemHttpResult(new DefaultHttpContext()));

    [Theory]
    [InlineData("not_found", StatusCodes.Status404NotFound)]
    [InlineData("validation", StatusCodes.Status400BadRequest)]
    [InlineData("forbidden", StatusCodes.Status403Forbidden)]
    [InlineData("conflict", StatusCodes.Status409Conflict)]
    [InlineData("some.unknown_code", StatusCodes.Status400BadRequest)]
    public void ToProblemHttpResult_KnownCodes_MapToTheirStatus(string code, int expectedStatus)
    {
        Assert.Equal(expectedStatus, ToProblem(new DomainError(code, "msg")).StatusCode);
    }

    [Theory]
    [InlineData("video.provider_not_configured")]
    [InlineData("payment.provider_not_configured")]
    [InlineData("email.provider_not_configured")]
    [InlineData("video.cdn_not_configured")]
    [InlineData("video.token_auth_not_configured")]
    public void ToProblemHttpResult_NotConfiguredCodes_Map503(string code)
    {
        // Operator misconfiguration (missing Bunny/Stripe/SMTP settings) is a server-side condition, not
        // a client mistake: 503, never the generic 400.
        var problem = ToProblem(new DomainError(code, "Provider is not configured."));

        Assert.Equal(StatusCodes.Status503ServiceUnavailable, problem.StatusCode);
        Assert.Equal(code, problem.ProblemDetails.Extensions["errorCode"]);
        Assert.True(problem.ProblemDetails.Extensions.ContainsKey("traceId"));
    }

    [Fact]
    public void NotConfiguredCodeSuffix_IsTheSharedConvention()
    {
        Assert.Equal("_not_configured", DomainErrorHttpResults.NotConfiguredCodeSuffix);
    }

    [Theory]
    [InlineData("google.not_configured")]
    [InlineData("anything.not_configured")]
    public void ToProblemHttpResult_DotNotConfiguredCodes_Map503(string code)
    {
        // "google.not_configured" (Siri.Integrations.Google) ends in ".not_configured", which the "_not_configured" suffix check misses.
        var problem = ToProblem(new DomainError(code, "Google integration is not configured."));

        Assert.Equal(StatusCodes.Status503ServiceUnavailable, problem.StatusCode);
        Assert.Equal(code, problem.ProblemDetails.Extensions["errorCode"]);
    }

    [Theory]
    [InlineData("not_configured_yet")]
    [InlineData("google.notconfigured")]
    [InlineData("google.configured")]
    public void ToProblemHttpResult_CodesThatMerelyResembleNotConfigured_StayGeneric400(string code)
    {
        Assert.Equal(StatusCodes.Status400BadRequest, ToProblem(new DomainError(code, "msg")).StatusCode);
    }

    [Fact]
    public void Unavailable_MapsTo503_AndCarriesTheUnavailableCode()
    {
        var error = DomainError.Unavailable("Store is down.");

        var problem = ToProblem(error);

        Assert.Equal("unavailable", error.Code);
        Assert.Equal(StatusCodes.Status503ServiceUnavailable, problem.StatusCode);
        Assert.Equal("Store is down.", problem.ProblemDetails.Title);
        Assert.Equal("unavailable", problem.ProblemDetails.Extensions["errorCode"]);
    }

    [Fact]
    public void ToProblemHttpResult_ErrorWithoutReason_EmitsOnlyTraceIdAndErrorCode()
    {
        // Regression: every pre-existing error (no Reason/Extensions) must produce the exact body it
        // always did — no "reason" member, no extra extension keys.
        var problem = ToProblem(DomainError.NotFound("nope"));

        Assert.Equal(StatusCodes.Status404NotFound, problem.StatusCode);
        Assert.Equal(["errorCode", "traceId"], problem.ProblemDetails.Extensions.Keys.Order().ToArray());
        Assert.False(problem.ProblemDetails.Extensions.ContainsKey("reason"));
    }

    [Fact]
    public void WithReason_KeepsCodeAndMessage_AndEmitsReasonMember()
    {
        var error = DomainError.Conflict("window closed").WithReason("live.window_not_open");

        Assert.Equal("conflict", error.Code);
        Assert.Equal("window closed", error.Message);
        Assert.Equal("live.window_not_open", error.Reason);
        Assert.Null(error.Extensions);

        var problem = ToProblem(error);

        Assert.Equal(StatusCodes.Status409Conflict, problem.StatusCode);
        Assert.Equal("live.window_not_open", problem.ProblemDetails.Extensions["reason"]);
        Assert.Equal("conflict", problem.ProblemDetails.Extensions["errorCode"]);
    }

    [Fact]
    public void WithReason_Extensions_AreSpreadIntoTheProblemBody()
    {
        var sessionIds = new[] { Guid.NewGuid(), Guid.NewGuid() };
        var opensAtUtc = new DateTime(2026, 10, 8, 3, 0, 0, DateTimeKind.Utc);
        var error = DomainError.Validation("rooms not ready").WithReason(
            "live.meetings_not_ready",
            new Dictionary<string, object?> { ["sessionIds"] = sessionIds, ["opensAtUtc"] = opensAtUtc });

        var problem = ToProblem(error);

        Assert.Equal(StatusCodes.Status400BadRequest, problem.StatusCode);
        Assert.Equal("live.meetings_not_ready", problem.ProblemDetails.Extensions["reason"]);
        Assert.Same(sessionIds, problem.ProblemDetails.Extensions["sessionIds"]);
        Assert.Equal(opensAtUtc, problem.ProblemDetails.Extensions["opensAtUtc"]);
        Assert.True(problem.ProblemDetails.Extensions.ContainsKey("traceId"));
        Assert.Equal("validation", problem.ProblemDetails.Extensions["errorCode"]);
    }

    [Fact]
    public void WithReason_ExtensionsCannotOverrideReservedMembers()
    {
        var error = DomainError.Forbidden("no").WithReason(
            "x.reason",
            new Dictionary<string, object?>
            {
                ["traceId"] = "spoofed",
                ["errorCode"] = "spoofed",
                ["reason"] = "spoofed",
                ["status"] = 200,
                ["detail"] = "spoofed",
                ["safe"] = 1,
            });

        var problem = ToProblem(error);

        Assert.NotEqual("spoofed", problem.ProblemDetails.Extensions["traceId"]);
        Assert.Equal("forbidden", problem.ProblemDetails.Extensions["errorCode"]);
        Assert.Equal("x.reason", problem.ProblemDetails.Extensions["reason"]);
        Assert.Equal(StatusCodes.Status403Forbidden, problem.StatusCode);
        Assert.Null(problem.ProblemDetails.Detail);
        Assert.Equal(1, problem.ProblemDetails.Extensions["safe"]);
    }

    [Fact]
    public void WithReason_UnavailableWithReason_Maps503()
    {
        var problem = ToProblem(DomainError.Unavailable("Google is not configured.").WithReason("live.google_not_configured"));

        Assert.Equal(StatusCodes.Status503ServiceUnavailable, problem.StatusCode);
        Assert.Equal("live.google_not_configured", problem.ProblemDetails.Extensions["reason"]);
    }

    [Fact]
    public void ErrorsWithoutReason_StillCompareEqualToDomainErrorNone_WhenNone()
    {
        // The Result invariants compare against DomainError.None by value; the new members must not
        // break that (None has null Reason/Extensions).
        Assert.True(DomainError.None == new DomainError(string.Empty, string.Empty));
        Assert.True(Result.Success().IsSuccess);
        Assert.True(Result.Failure(DomainError.NotFound("x").WithReason("r")).IsFailure);
    }
}
