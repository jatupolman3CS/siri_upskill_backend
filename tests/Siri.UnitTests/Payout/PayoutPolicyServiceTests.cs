using Microsoft.Extensions.Options;
using Siri.Modules.Catalog.Contracts;
using Siri.Modules.Catalog.Domain;
using Siri.Modules.Payout;
using Siri.Modules.Payout.Application;
using Siri.Modules.Payout.Domain;
using Xunit;

namespace Siri.UnitTests.Payout;

/// <summary>
/// <see cref="PayoutPolicyService"/> — the read-only "what rules apply to me" answer behind <c>GET /api/payout/policy</c>. The revenue share is the caller's own
/// instructor-profile rate (default 70 for everyone else); the other three numbers are <see cref="PayoutOptions"/> verbatim. The user id is the only input and it
/// is resolved to a profile through <see cref="IInstructorProfileReader"/>, so one caller can never see another instructor's rate.
/// </summary>
public sealed class PayoutPolicyServiceTests
{
    private static readonly Guid UserId = Guid.NewGuid();

    private sealed class FakeCatalogPriceContract : ICatalogPriceContract
    {
        public readonly Dictionary<Guid, decimal> SharePercentsByProfile = [];

        /// <summary>Every list of profile ids the service asked the catalog for.</summary>
        public readonly List<IReadOnlyList<Guid>> SharePercentRequests = [];

        public Task<IReadOnlyDictionary<Guid, decimal>> GetInstructorRevenueSharePercentsAsync(IEnumerable<Guid> instructorIds, CancellationToken cancellationToken)
        {
            var ids = instructorIds.ToList();
            SharePercentRequests.Add(ids);

            IReadOnlyDictionary<Guid, decimal> result = ids
                .Where(SharePercentsByProfile.ContainsKey)
                .ToDictionary(id => id, id => SharePercentsByProfile[id]);
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

    private readonly FakeInstructorProfileReader _profiles = new();
    private readonly FakeCatalogPriceContract _catalog = new();

    private PayoutPolicyService Service(PayoutOptions? options = null) =>
        new(_profiles, _catalog, Options.Create(options ?? new PayoutOptions()));

    [Fact]
    public async Task GetForUserAsync_UserWithoutInstructorProfile_GetsTheDefaultShareAndNeverAsksTheCatalogForARate()
    {
        var response = await Service().GetForUserAsync(UserId, CancellationToken.None);

        Assert.Equal(70m, response.RevenueSharePercent);
        Assert.Equal(30m, response.PlatformSharePercent);
        Assert.Empty(_catalog.SharePercentRequests);
    }

    [Fact]
    public async Task GetForUserAsync_InstructorWithOwnRate_GetsThatRateAndThePlatformRemainder()
    {
        var profileId = _profiles.Map(UserId, Guid.NewGuid());
        _catalog.SharePercentsByProfile[profileId] = 80.00m;

        var response = await Service().GetForUserAsync(UserId, CancellationToken.None);

        Assert.Equal(80.00m, response.RevenueSharePercent);
        Assert.Equal(20.00m, response.PlatformSharePercent);
    }

    [Fact]
    public async Task GetForUserAsync_FractionalRate_PlatformShareIs100MinusRateExactly()
    {
        var profileId = _profiles.Map(UserId, Guid.NewGuid());
        _catalog.SharePercentsByProfile[profileId] = 72.50m;

        var response = await Service().GetForUserAsync(UserId, CancellationToken.None);

        Assert.Equal(72.50m, response.RevenueSharePercent);
        Assert.Equal(27.50m, response.PlatformSharePercent);
        Assert.Equal(100m, response.RevenueSharePercent + response.PlatformSharePercent);
    }

    [Theory]
    [InlineData(0)]
    [InlineData(100)]
    public async Task GetForUserAsync_BoundaryRates_AreReportedAsStored(int ratePercent)
    {
        var profileId = _profiles.Map(UserId, Guid.NewGuid());
        _catalog.SharePercentsByProfile[profileId] = ratePercent;

        var response = await Service().GetForUserAsync(UserId, CancellationToken.None);

        Assert.Equal(ratePercent, response.RevenueSharePercent);
        Assert.Equal(100 - ratePercent, response.PlatformSharePercent);
    }

    [Fact]
    public async Task GetForUserAsync_ProfileWithNoRateRowInTheCatalog_FallsBackToTheDefault()
    {
        _profiles.Map(UserId, Guid.NewGuid());

        var response = await Service().GetForUserAsync(UserId, CancellationToken.None);

        Assert.Equal(REVENUE_SPLIT.DefaultRevenueSharePercent, response.RevenueSharePercent);
        Assert.Equal(100m - REVENUE_SPLIT.DefaultRevenueSharePercent, response.PlatformSharePercent);
    }

    [Fact]
    public async Task GetForUserAsync_OnlyEverAsksForTheCallersOwnProfile_NeverAnotherInstructorsRate()
    {
        var ownProfile = _profiles.Map(UserId, Guid.NewGuid());
        var otherUser = Guid.NewGuid();
        var otherProfile = _profiles.Map(otherUser, Guid.NewGuid());
        _catalog.SharePercentsByProfile[ownProfile] = 65m;
        _catalog.SharePercentsByProfile[otherProfile] = 90m;

        var response = await Service().GetForUserAsync(UserId, CancellationToken.None);

        Assert.Equal(65m, response.RevenueSharePercent);
        var request = Assert.Single(_catalog.SharePercentRequests);
        Assert.Equal(new[] { ownProfile }, request);
    }

    [Fact]
    public async Task GetForUserAsync_PayoutOptions_AreReportedVerbatim()
    {
        var options = new PayoutOptions { WithholdingTaxPercent = 5.5m, MinimumPayoutAmount = 1250.75m, HoldDays = 7 };

        var response = await Service(options).GetForUserAsync(UserId, CancellationToken.None);

        Assert.Equal(5.5m, response.WithholdingTaxPercent);
        Assert.Equal(1250.75m, response.MinimumPayoutAmount);
        Assert.Equal(7, response.HoldDays);
    }

    [Fact]
    public async Task GetForUserAsync_DefaultConfiguration_ReportsTheRealPolicyThatTheUiMustQuote()
    {
        // The numbers the screens used to hardcode ("70%", "3%", "฿500", "14 วัน") are exactly the shipped defaults — and the UI no longer owns them.
        var response = await Service().GetForUserAsync(UserId, CancellationToken.None);

        Assert.Equal(new PayoutPolicyResponse(70m, 30m, 3m, 500m, 14), response);
    }

    [Fact]
    public void DefaultRevenueSharePercent_MatchesTheCatalogProfileDefault_SoNewInstructorsAreSplitAtTheRateTheUiShows()
    {
        Assert.Equal(INSTRUCTOR_PROFILE.DefaultRevenueSharePercent, REVENUE_SPLIT.DefaultRevenueSharePercent);
    }
}
