using System.Text.Json;
using Siri.Modules.Identity.Domain;
using Siri.Modules.Identity.Features.GetMe;

namespace Siri.UnitTests.Identity;

/// <summary>
/// Pure unit tests for GET /api/identity/me — the existence/status decision and the DTO mapping that
/// <see cref="GetMeHandler"/> delegates to (<see cref="UserMeMappingExtensions.ToMeResult"/>), plus the
/// exact JSON contract the frontend codes against. The handler's own EF query needs a real database and
/// is covered by <c>MeEndpointTests</c> in the integration suite (same split as
/// <c>SessionSummaryMappingTests</c> / <c>DeviceManagementTests</c>).
/// </summary>
public class GetMeMappingTests
{
    private static USER ActiveUser(string displayName = "Student One")
    {
        var user = USER.Register("student@example.com", "STUDENT@EXAMPLE.COM", "hashed-password", displayName);
        user.ConfirmEmail(new FakeClock(DateTime.UtcNow));
        return user;
    }

    [Fact]
    public void ToMeResult_ActiveUser_MapsEveryFieldOfTheContract()
    {
        var user = ActiveUser();
        user.SetAvatarIfMissing("https://example.com/me.png");
        user.AssignRole(new ROLE(ROLE.LearnerId, ROLE.LearnerName));

        var result = user.ToMeResult();

        Assert.True(result.IsSuccess);
        Assert.Equal(user.Id, result.Value.Id);
        Assert.Equal("student@example.com", result.Value.Email);
        Assert.Equal("Student One", result.Value.DisplayName);
        Assert.Equal("https://example.com/me.png", result.Value.AvatarUrl);
        Assert.Equal([ROLE.LearnerName], result.Value.Roles);
    }

    [Fact]
    public void ToMeResult_UserWithoutAvatarOrRoles_ReturnsNullAvatarAndEmptyRoles()
    {
        var result = ActiveUser().ToMeResult();

        Assert.True(result.IsSuccess);
        Assert.Null(result.Value.AvatarUrl);
        Assert.Empty(result.Value.Roles);
    }

    [Fact]
    public void ToMeResult_MultipleRoles_AreReturnedAsStoredNamesInDeterministicOrdinalOrder()
    {
        var user = ActiveUser();
        user.AssignRole(new ROLE(ROLE.InstructorId, ROLE.InstructorName));
        user.AssignRole(new ROLE(ROLE.AdminId, ROLE.AdminName));
        user.AssignRole(new ROLE(ROLE.LearnerId, ROLE.LearnerName));

        var result = user.ToMeResult();

        Assert.Equal([ROLE.AdminName, ROLE.InstructorName, ROLE.LearnerName], result.Value.Roles);
    }

    [Fact]
    public void ToMeResult_NoSuchUser_IsNotFound()
    {
        var result = ((USER?)null).ToMeResult();

        Assert.True(result.IsFailure);
        Assert.Equal("not_found", result.Error.Code);
    }

    [Fact]
    public void ToMeResult_AnonymizedAccount_IsNotFoundAndLeaksNoPlaceholderIdentity()
    {
        // A token minted before the erasure is still cryptographically valid — it must not be able to
        // read the scrubbed row (PDPA anonymize-in-place keeps the row, status Deleted).
        var user = ActiveUser();
        user.Anonymize("anonymized_x@deleted.example", "Deleted USER", "unmatchable-hash");

        var result = user.ToMeResult();

        Assert.True(result.IsFailure);
        Assert.Equal("not_found", result.Error.Code);
        Assert.DoesNotContain("anonymized_x", result.Error.Message);
    }

    [Fact]
    public void ToMeResult_DeletedWithoutAnonymize_IsAlsoNotFound()
    {
        var user = ActiveUser();
        user.Delete();

        Assert.Equal("not_found", user.ToMeResult().Error.Code);
    }

    [Fact]
    public void ToMeResult_SuspendedUser_StillReturnsTheirOwnProfile()
    {
        // Suspension is enforced where sessions are (re)issued — Login/Refresh refuse non-Active
        // accounts; this is only the caller's own data for an access token that is still alive.
        var user = ActiveUser();
        user.Suspend("test");

        Assert.True(user.ToMeResult().IsSuccess);
    }

    [Fact]
    public void MeResponse_ExposesExactlyTheFiveContractFields_NothingSensitive()
    {
        // Data minimisation: this endpoint is hit on every page load, so a field added to the DTO
        // later (phone, status, password hash, ...) must be a deliberate, test-visible decision.
        var properties = typeof(MeResponse).GetProperties().Select(p => p.Name).Order().ToArray();

        Assert.Equal(["AvatarUrl", "DisplayName", "Email", "Id", "Roles"], properties);
    }

    [Fact]
    public void MeResponse_SerializesWithTheCamelCaseJsonContract()
    {
        var user = ActiveUser();
        user.AssignRole(new ROLE(ROLE.LearnerId, ROLE.LearnerName));

        var json = JsonSerializer.Serialize(user.ToMeResult().Value, new JsonSerializerOptions(JsonSerializerDefaults.Web));
        using var document = JsonDocument.Parse(json);
        var root = document.RootElement;

        Assert.Equal(
            ["avatarUrl", "displayName", "email", "id", "roles"],
            root.EnumerateObject().Select(p => p.Name).Order().ToArray());
        Assert.Equal(JsonValueKind.String, root.GetProperty("id").ValueKind);
        Assert.Equal(JsonValueKind.Null, root.GetProperty("avatarUrl").ValueKind);
        Assert.Equal(JsonValueKind.Array, root.GetProperty("roles").ValueKind);
        Assert.Equal("Learner", root.GetProperty("roles")[0].GetString());
    }

    [Fact]
    public void GetMeQuery_CarriesNothingButTheCallersOwnUserId()
    {
        // IDOR guard: the only input is the id the controller read from the verified token — there is
        // no bindable property a client could use to ask for somebody else's profile.
        var properties = typeof(GetMeQuery).GetProperties().Select(p => p.Name).ToArray();

        Assert.Equal(["UserId"], properties);
    }
}
