using System.Security.Cryptography;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using Siri.Integrations.Google;
using Siri.Modules.Catalog.Contracts;
using Siri.Modules.Live.Application;
using Siri.Modules.Live.Domain;
using Siri.SharedKernel;

namespace Siri.UnitTests.Live;

/// <summary>Shared fakes/builders for the Live module's unit tests. Everything is in-memory and records what it was asked to do.</summary>
internal static class LiveTestData
{
    public static readonly DateTime Now = new(2026, 10, 7, 3, 0, 0, DateTimeKind.Utc);

    public static LiveOptions Options(Action<LiveOptions>? configure = null)
    {
        var options = new LiveOptions { PublicBaseUrl = "https://app.example.test", Provider = LiveProviderMode.GoogleMeet };
        LiveOptions.ApplyDefaults(options, fallbackPublicBaseUrl: null);
        configure?.Invoke(options);
        return options;
    }

    public static IOptions<LiveOptions> OptionsOf(Action<LiveOptions>? configure = null) => Microsoft.Extensions.Options.Options.Create(Options(configure));

    /// <summary>The real AES-GCM protector with a throwaway key — so "is not plaintext" and "decrypts back" are tested for real.</summary>
    public static ISensitiveDataProtector Protector(byte[]? key = null) =>
        new SensitiveDataProtector(Microsoft.Extensions.Options.Options.Create(new DataProtectionOptions
        {
            EncryptionKeyBase64 = Convert.ToBase64String(key ?? RandomNumberGenerator.GetBytes(32)),
        }));

    public static LiveSessionContext Context(
        Guid? sessionId = null,
        Guid? instructorUserId = null,
        LiveSessionStatus status = LiveSessionStatus.Scheduled,
        DateTime? startsAtUtc = null,
        DateTime? endsAtUtc = null,
        string title = "คาบที่ 1",
        string courseTitle = "คอร์สทดสอบ",
        string? description = null) =>
        new(
            sessionId ?? Guid.NewGuid(),
            CourseId: Guid.NewGuid(),
            courseTitle,
            "test-course",
            title,
            description,
            startsAtUtc ?? Now.AddDays(1),
            endsAtUtc ?? Now.AddDays(1).AddHours(2),
            status,
            CancelReason: null,
            RecordingEpisodeId: null,
            InstructorProfileId: Guid.NewGuid(),
            instructorUserId ?? Guid.NewGuid(),
            "Test Instructor",
            GoogleAttendeeSyncEnabled: false);
}

internal sealed class InMemorySessionMeetingRepository : ISessionMeetingRepository
{
    public List<SESSION_MEETING> Meetings { get; } = [];

    public int SaveCount { get; private set; }

    public int ClearTrackingCount { get; private set; }

    /// <summary>Set to make the next <see cref="SaveChangesAsync"/> throw (then clears itself).</summary>
    public Exception? ThrowOnNextSave { get; set; }

    public Task<SESSION_MEETING?> GetBySessionIdAsync(Guid sessionId, CancellationToken cancellationToken) =>
        Task.FromResult(Meetings.FirstOrDefault(m => m.SESSION_ID == sessionId));

    public Task<IReadOnlyList<SESSION_MEETING>> GetBySessionIdsAsync(IReadOnlyCollection<Guid> sessionIds, CancellationToken cancellationToken) =>
        Task.FromResult<IReadOnlyList<SESSION_MEETING>>(Meetings.Where(m => sessionIds.Contains(m.SESSION_ID)).ToList());

    public Task<IReadOnlySet<Guid>> GetExistingSessionIdsAsync(IReadOnlyCollection<Guid> sessionIds, CancellationToken cancellationToken) =>
        Task.FromResult<IReadOnlySet<Guid>>(Meetings.Where(m => sessionIds.Contains(m.SESSION_ID)).Select(m => m.SESSION_ID).ToHashSet());

    public Task<IReadOnlyList<Guid>> GetDueSessionIdsAsync(DateTime nowUtc, int take, CancellationToken cancellationToken) =>
        Task.FromResult<IReadOnlyList<Guid>>(Meetings
            .Where(m => m.SYNC_STATUS is MeetingSyncStatus.Pending or MeetingSyncStatus.PendingDelete
                && (m.NEXT_RETRY_AT_UTC is null || m.NEXT_RETRY_AT_UTC <= nowUtc))
            .Take(take)
            .Select(m => m.SESSION_ID)
            .ToList());

    public Task<IReadOnlyList<SESSION_MEETING>> GetResettableByInstructorAsync(Guid instructorUserId, CancellationToken cancellationToken) =>
        Task.FromResult<IReadOnlyList<SESSION_MEETING>>(Meetings
            .Where(m => m.INSTRUCTOR_USER_ID == instructorUserId
                && m.MEET_URL_ENCRYPTED is null
                && m.SYNC_STATUS is MeetingSyncStatus.AwaitingLink or MeetingSyncStatus.NeedsReconnect or MeetingSyncStatus.Failed)
            .ToList());

    public void Add(SESSION_MEETING meeting) => Meetings.Add(meeting);

    public Task SaveChangesAsync(CancellationToken cancellationToken)
    {
        if (ThrowOnNextSave is { } exception)
        {
            ThrowOnNextSave = null;
            throw exception;
        }

        SaveCount++;
        return Task.CompletedTask;
    }

    public void ClearTracking() => ClearTrackingCount++;
}

internal sealed class InMemoryAccountRepository : IInstructorGoogleAccountRepository
{
    public List<INSTRUCTOR_GOOGLE_ACCOUNT> Accounts { get; } = [];

    public int SaveCount { get; private set; }

    public Task<INSTRUCTOR_GOOGLE_ACCOUNT?> GetByInstructorUserIdAsync(Guid instructorUserId, CancellationToken cancellationToken) =>
        Task.FromResult(Accounts.FirstOrDefault(a => a.INSTRUCTOR_USER_ID == instructorUserId));

    /// <summary>Same rule as the real query: active, a stored Workspace domain, and (here exactly, there by substring) both recording scopes.</summary>
    public Task<IReadOnlyList<Guid>> GetRecordingCandidateInstructorIdsAsync(CancellationToken cancellationToken) =>
        Task.FromResult<IReadOnlyList<Guid>>(Accounts
            .Where(a => a.IsActive && !string.IsNullOrEmpty(a.HOSTED_DOMAIN) && a.HasRecordingScopes)
            .Select(a => a.INSTRUCTOR_USER_ID)
            .ToList());

    public int RecordValidationCalls { get; private set; }

    public Task RecordValidationAsync(INSTRUCTOR_GOOGLE_ACCOUNT account, DateTime validatedAtUtc, CancellationToken cancellationToken)
    {
        RecordValidationCalls++;
        account.MarkValidated(new FakeClock(validatedAtUtc));
        return Task.CompletedTask;
    }

    public void Add(INSTRUCTOR_GOOGLE_ACCOUNT account) => Accounts.Add(account);

    public Task SaveChangesAsync(CancellationToken cancellationToken)
    {
        SaveCount++;
        return Task.CompletedTask;
    }
}

internal sealed class FakeStateStore : IGoogleOAuthStateStore
{
    private readonly Dictionary<string, GoogleOAuthState> _states = [];

    public bool Available { get; set; } = true;

    public Dictionary<string, GoogleOAuthState> States => _states;

    public TimeSpan? LastTimeToLive { get; private set; }

    public Task<bool> TrySaveAsync(string state, GoogleOAuthState payload, TimeSpan timeToLive, CancellationToken cancellationToken)
    {
        if (!Available)
        {
            return Task.FromResult(false);
        }

        _states[state] = payload;
        LastTimeToLive = timeToLive;
        return Task.FromResult(true);
    }

    public Task<GoogleOAuthState?> TryConsumeAsync(string state, CancellationToken cancellationToken)
    {
        if (!Available || !_states.Remove(state, out var payload))
        {
            return Task.FromResult<GoogleOAuthState?>(null);
        }

        return Task.FromResult<GoogleOAuthState?>(payload);
    }
}

internal sealed class FakeGoogleOAuth : IGoogleOAuthService
{
    public bool Configured { get; set; } = true;

    public bool IsConfigured => Configured;

    public Result<GoogleTokenSet> ExchangeResult { get; set; } = Result.Success(new GoogleTokenSet(
        "access-token-1", LiveTestData.Now.AddHours(1), "refresh-token-1", GoogleScopes.Calendar + " openid email"));

    public Result<GoogleTokenSet> RefreshResult { get; set; } = Result.Success(new GoogleTokenSet(
        "access-token-2", LiveTestData.Now.AddHours(1), null, GoogleScopes.Calendar));

    public Result<GoogleUserInfo> UserInfoResult { get; set; } = Result.Success(new GoogleUserInfo("sub-1", "teacher@gmail.test", true));

    public List<string> RevokedTokens { get; } = [];

    public int RefreshCalls { get; private set; }

    public string? LastCodeVerifier { get; private set; }

    public string? LastState { get; private set; }

    /// <summary>Set to make <see cref="BuildRecordingAccessAuthorizationUrl"/> behave like an <see cref="IGoogleOAuthService"/> that predates P11-13.</summary>
    public bool RecordingAccessSupported { get; set; } = true;

    /// <summary>The state of the last <see cref="BuildRecordingAccessAuthorizationUrl"/> call (the calendar one sets <see cref="LastState"/>).</summary>
    public string? LastRecordingAccessState { get; private set; }

    public string BuildAuthorizationUrl(string state, string codeChallenge)
    {
        LastState = state;
        return $"https://accounts.example.test/auth?state={Uri.EscapeDataString(state)}&code_challenge={Uri.EscapeDataString(codeChallenge)}";
    }

    public string BuildRecordingAccessAuthorizationUrl(string state, string codeChallenge)
    {
        if (!RecordingAccessSupported)
        {
            throw new NotSupportedException();
        }

        LastState = state;
        LastRecordingAccessState = state;
        return $"https://accounts.example.test/auth-recording?state={Uri.EscapeDataString(state)}&code_challenge={Uri.EscapeDataString(codeChallenge)}";
    }

    public Task<Result<GoogleTokenSet>> ExchangeCodeAsync(string code, string codeVerifier, CancellationToken ct)
    {
        LastCodeVerifier = codeVerifier;
        return Task.FromResult(ExchangeResult);
    }

    public Task<Result<GoogleTokenSet>> RefreshAccessTokenAsync(string refreshToken, CancellationToken ct)
    {
        RefreshCalls++;
        return Task.FromResult(RefreshResult);
    }

    public int UserInfoCalls { get; private set; }

    public Task<Result<GoogleUserInfo>> GetUserInfoAsync(string accessToken, CancellationToken ct)
    {
        UserInfoCalls++;
        return Task.FromResult(UserInfoResult);
    }

    public Task<Result> RevokeAsync(string token, CancellationToken ct)
    {
        RevokedTokens.Add(token);
        return Task.FromResult(Result.Success());
    }
}

internal sealed class FakeSchedule : ILiveScheduleReader
{
    public List<LiveSessionContext> Contexts { get; } = [];

    public Task<IReadOnlyList<LiveSessionInfo>> GetSessionsForCourseAsync(Guid courseId, CancellationToken cancellationToken) =>
        Task.FromResult<IReadOnlyList<LiveSessionInfo>>(Contexts
            .Where(c => c.CourseId == courseId)
            .OrderBy(c => c.StartsAtUtc)
            .Select(c => new LiveSessionInfo(c.SessionId, c.CourseId, c.Title, c.StartsAtUtc, c.EndsAtUtc, c.Status, c.RecordingEpisodeId))
            .ToList());

    public Task<IReadOnlyList<LiveSessionInfo>> GetUpcomingSessionsAsync(DateTime fromUtc, DateTime toUtc, CancellationToken cancellationToken) =>
        throw new NotSupportedException();

    public Task<LiveSessionInfo?> GetSessionAsync(Guid sessionId, CancellationToken cancellationToken) => throw new NotSupportedException();

    public Task<IReadOnlyList<LiveSessionContext>> GetSessionContextsAsync(IReadOnlyCollection<Guid> sessionIds, CancellationToken cancellationToken) =>
        Task.FromResult<IReadOnlyList<LiveSessionContext>>(Contexts.Where(c => sessionIds.Contains(c.SessionId)).ToList());

    public Task<IReadOnlyList<LiveSessionContext>> GetSessionContextsInWindowAsync(
        DateTime fromUtc, DateTime toUtc, bool includeCancelled, CancellationToken cancellationToken) =>
        Task.FromResult<IReadOnlyList<LiveSessionContext>>(Contexts
            .Where(c => c.StartsAtUtc >= fromUtc && c.StartsAtUtc < toUtc && (includeCancelled || c.Status == LiveSessionStatus.Scheduled))
            .ToList());

    public Task<LiveSessionContextPage> GetInstructorSessionContextsAsync(
        Guid instructorUserId, DateTime? fromUtc, DateTime? toUtc, bool includeCancelled, bool newestFirst, int skip, int take, CancellationToken cancellationToken)
    {
        var items = Contexts
            .Where(c => c.InstructorUserId == instructorUserId
                && (fromUtc is null || c.EndsAtUtc > fromUtc)
                && (toUtc is null || c.StartsAtUtc < toUtc)
                && (includeCancelled || c.Status == LiveSessionStatus.Scheduled))
            .OrderBy(c => c.StartsAtUtc)
            .ToList();

        return Task.FromResult(new LiveSessionContextPage(items.Skip(skip).Take(take).ToList(), items.Count));
    }
}

/// <summary>Only the members Live uses; everything else fails loudly if a test accidentally relies on it.</summary>
internal sealed class FakeCatalog : ICatalogPriceContract
{
    public HashSet<(Guid CourseId, Guid UserId)> Owners { get; } = [];

    public Task<bool> IsInstructorOwnerOfCourseAsync(Guid courseId, Guid instructorUserId, CancellationToken cancellationToken) =>
        Task.FromResult(Owners.Contains((courseId, instructorUserId)));

    public Task<IReadOnlyDictionary<Guid, CoursePriceInfo>> GetPublishedCoursePricesAsync(IEnumerable<Guid> courseIds, CancellationToken cancellationToken) =>
        throw new NotSupportedException();

    public Task<bool> IsEpisodeFreePreviewAsync(Guid episodeId, CancellationToken cancellationToken) => throw new NotSupportedException();

    public Task<Guid?> GetCourseIdForEpisodeAsync(Guid episodeId, CancellationToken cancellationToken) => throw new NotSupportedException();

    public Task<bool> IsInstructorOwnerOfEpisodeAsync(Guid episodeId, Guid instructorUserId, CancellationToken cancellationToken) =>
        throw new NotSupportedException();

    public Task<int> GetPendingReviewsCountAsync(CancellationToken cancellationToken) => throw new NotSupportedException();

    public Task<IReadOnlyDictionary<Guid, string>> GetCourseTitlesAsync(IEnumerable<Guid> courseIds, CancellationToken cancellationToken) =>
        throw new NotSupportedException();

    public Task<IReadOnlyDictionary<Guid, decimal>> GetInstructorRevenueSharePercentsAsync(IEnumerable<Guid> instructorIds, CancellationToken cancellationToken) =>
        throw new NotSupportedException();
}

internal sealed class RecordingAlertSender : IInstructorAlertSender
{
    public List<(Guid InstructorUserId, int AffectedCount)> ReconnectAlerts { get; } = [];

    public List<(Guid InstructorUserId, Guid SessionId)> NeedsLinkAlerts { get; } = [];

    public List<(Guid InstructorUserId, Guid SessionId)> FailedAlerts { get; } = [];

    public Task GoogleReconnectNeededAsync(Guid instructorUserId, int affectedCount, CancellationToken cancellationToken)
    {
        ReconnectAlerts.Add((instructorUserId, affectedCount));
        return Task.CompletedTask;
    }

    public Task MeetingNeedsLinkAsync(Guid instructorUserId, Guid sessionId, string sessionTitle, string courseTitle, CancellationToken cancellationToken)
    {
        NeedsLinkAlerts.Add((instructorUserId, sessionId));
        return Task.CompletedTask;
    }

    public Task MeetingFailedAsync(Guid instructorUserId, Guid sessionId, string sessionTitle, string courseTitle, CancellationToken cancellationToken)
    {
        FailedAlerts.Add((instructorUserId, sessionId));
        return Task.CompletedTask;
    }

    public List<(Guid InstructorUserId, Guid SessionId)> RecordingImportedAlerts { get; } = [];

    public List<(Guid InstructorUserId, Guid SessionId)> RecordingImportFailedAlerts { get; } = [];

    public List<(Guid InstructorUserId, Guid SessionId)> RecordingNeedsReconnectAlerts { get; } = [];

    public Task RecordingImportedAsync(Guid instructorUserId, Guid sessionId, string sessionTitle, string courseTitle, CancellationToken cancellationToken)
    {
        RecordingImportedAlerts.Add((instructorUserId, sessionId));
        return Task.CompletedTask;
    }

    public Task RecordingImportFailedAsync(Guid instructorUserId, Guid sessionId, string sessionTitle, string courseTitle, CancellationToken cancellationToken)
    {
        RecordingImportFailedAlerts.Add((instructorUserId, sessionId));
        return Task.CompletedTask;
    }

    public Task RecordingNeedsReconnectAsync(Guid instructorUserId, Guid sessionId, string sessionTitle, string courseTitle, CancellationToken cancellationToken)
    {
        RecordingNeedsReconnectAlerts.Add((instructorUserId, sessionId));
        return Task.CompletedTask;
    }
}

/// <summary>Captures every formatted log message (and exception text), so tests can assert that no secret was logged.</summary>
internal sealed class ListLogger<T> : ILogger<T>
{
    public List<string> Messages { get; } = [];

    public IDisposable? BeginScope<TState>(TState state) where TState : notnull => null;

    public bool IsEnabled(LogLevel logLevel) => true;

    public void Log<TState>(LogLevel logLevel, EventId eventId, TState state, Exception? exception, Func<TState, Exception?, string> formatter)
    {
        Messages.Add(formatter(state, exception));
        if (exception is not null)
        {
            Messages.Add(exception.ToString());
        }
    }

    public string All => string.Join("\n", Messages);
}
