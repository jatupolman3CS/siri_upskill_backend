using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Diagnostics;
using Siri.Integrations.Google;
using Siri.Modules.Live.Application;
using Siri.Modules.Live.Domain;
using Siri.Modules.Live.Infrastructure;
using Siri.Persistence;
using Siri.SharedKernel;

namespace Siri.UnitTests.Live;

/// <summary>
/// N1 of the P11-13 re-QA: the token refresh used to stamp <c>LAST_VALIDATED_AT_UTC</c> as a <b>tracked, unsaved edit</b> of the account. The scoped <c>AppDbContext</c> is shared by
/// every module of a job, so the next unrelated <c>SaveChanges</c> (Media's upload bookkeeping, a meeting row) flushed that edit as an UPDATE carrying this scope's row version -
/// and lost against a parallel job that refreshed the same instructor's token, failing an operation that had nothing to do with the account (a transfer burned an attempt and
/// a 10-minute backoff). These tests model the shared row with a version and prove the refresh now leaves nothing pending, so nothing can collide.
/// </summary>
public class GoogleTokenRefreshConcurrencyTests
{
    private static readonly DateTime Now = LiveTestData.Now;
    private static readonly Guid InstructorId = RecordingJobHarness.InstructorId;

    private readonly ISensitiveDataProtector _protector = LiveTestData.Protector();
    private readonly FakeGoogleOAuth _oauth = new();

    private VersionedAccountStore NewStore() => new(
        InstructorId,
        _protector.Encrypt("refresh-token-1"),
        string.Join(' ', GoogleScopes.CalendarEventsOwned, GoogleScopes.MeetSpaceReadonly, GoogleScopes.DriveMeetReadonly),
        "school.example.test");

    private InstructorGoogleAccountService ServiceOver(IInstructorGoogleAccountRepository accounts) => new(
        accounts,
        new InMemorySessionMeetingRepository(),
        _oauth,
        new FakeStateStore(),
        _protector,
        new FakeSchedule(),
        new RecordingAlertSender(),
        new FakeClock(Now),
        LiveTestData.OptionsOf(),
        Microsoft.Extensions.Options.Options.Create(new GoogleOAuthOptions()),
        new ListLogger<InstructorGoogleAccountService>());

    // ---- The refresh leaves nothing pending ----------------------------------------------------------------------------

    [Fact]
    public async Task TokenRefresh_LeavesNoPendingChangeOnTheSharedContext_AndStillRecordsTheValidationTime()
    {
        var store = NewStore();
        var scope = new VersionedAccountRepository(store);

        var token = await ServiceOver(scope).TryGetAccessTokenAsync(InstructorId, CancellationToken.None);

        Assert.True(token.IsSuccess);
        Assert.False(scope.HasPendingChanges, "a tracked-but-unsaved edit would be flushed by the next unrelated SaveChanges of the shared context");
        Assert.Equal(Now, store.LastValidatedAtUtc);
        Assert.Equal(0, store.Version); // the set-based stamp does not rotate the row version
    }

    [Fact]
    public async Task TokenRefresh_TheCallersAccountObjectStillShowsTheNewTime()
    {
        var store = NewStore();
        var scope = new VersionedAccountRepository(store);
        var service = ServiceOver(scope);

        await service.TryGetAccessTokenAsync(InstructorId, CancellationToken.None);
        var account = await service.GetAccountAsync(InstructorId, CancellationToken.None);

        Assert.Equal(Now, account!.LAST_VALIDATED_AT_UTC);
    }

    [Fact]
    public async Task TwoParallelRefreshes_OfTheSameInstructor_ThenEachScopesNextUnrelatedSave_BothSucceed()
    {
        var store = NewStore();
        var scopeA = new VersionedAccountRepository(store);
        var scopeB = new VersionedAccountRepository(store);

        var tokenA = await ServiceOver(scopeA).TryGetAccessTokenAsync(InstructorId, CancellationToken.None);
        var tokenB = await ServiceOver(scopeB).TryGetAccessTokenAsync(InstructorId, CancellationToken.None);

        Assert.True(tokenA.IsSuccess);
        Assert.True(tokenB.IsSuccess);

        // What Media's ingest (or any other module on the shared context) does next: a SaveChanges. Neither may throw, because neither holds a pending edit.
        await scopeA.SaveChangesAsync(CancellationToken.None);
        await scopeB.SaveChangesAsync(CancellationToken.None);
        Assert.Equal(0, store.Version);
    }

    [Fact]
    public async Task ARefresh_NeverCollidesWithALegitimateWriterOfTheSameRow()
    {
        var store = NewStore();
        var refresher = new VersionedAccountRepository(store);
        var writer = new VersionedAccountRepository(store);

        await ServiceOver(refresher).TryGetAccessTokenAsync(InstructorId, CancellationToken.None);

        // Meanwhile somebody really edits the credential (a reconnect/revoke): this one is a genuine, versioned write.
        var edited = (await writer.GetByInstructorUserIdAsync(InstructorId, CancellationToken.None))!;
        edited.MarkRevoked(GoogleAccountRevokedReason.UserDisconnected, new FakeClock(Now));
        await writer.SaveChangesAsync(CancellationToken.None);

        // The refresher's context saves for an unrelated reason afterwards: with nothing pending it is a no-op instead of a stale UPDATE.
        await refresher.SaveChangesAsync(CancellationToken.None);
        Assert.Equal(1, store.Version);
    }

    [Fact]
    public async Task ARefreshThatCannotRecordTheValidationTime_StillReturnsTheToken_AndLogsOnlyTheExceptionType()
    {
        var store = NewStore();
        var failing = new VersionedAccountRepository(store) { ThrowOnRecordValidation = new InvalidOperationException("connection string Host=secret-db.example.test") };
        var log = new ListLogger<InstructorGoogleAccountService>();
        var service = new InstructorGoogleAccountService(
            failing, new InMemorySessionMeetingRepository(), _oauth, new FakeStateStore(), _protector, new FakeSchedule(), new RecordingAlertSender(),
            new FakeClock(Now), LiveTestData.OptionsOf(), Microsoft.Extensions.Options.Options.Create(new GoogleOAuthOptions()), log);

        var token = await service.TryGetAccessTokenAsync(InstructorId, CancellationToken.None);

        Assert.True(token.IsSuccess);
        Assert.False(failing.HasPendingChanges);
        Assert.DoesNotContain("secret-db", log.All);
        Assert.Contains(nameof(InvalidOperationException), log.All);
    }

    // ---- The import job: two transfers of one instructor in parallel ---------------------------------------------------------

    [Fact]
    public async Task TwoTransferJobsOfTheSameInstructor_RunningInParallel_BothSucceed_NeitherBurnsAnAttempt()
    {
        var h = new RecordingJobHarness();
        h.Account(); // the tick's own view of the account
        var first = h.Session(endedAgo: TimeSpan.FromHours(3));
        var second = h.Session(endedAgo: TimeSpan.FromHours(2));
        var importA = h.Import(first);
        var importB = h.Import(second);
        h.GoogleHasRecording(RecordingJobHarness.Finished(LiveTestData.Now.AddHours(-4), TimeSpan.FromMinutes(55)));

        await h.RunTickAsync(); // claims both and queues one transfer job each
        Assert.Equal(2, h.Scheduler.Enqueued.Count);

        // Each transfer job runs in its own scope: its own view of the shared, row-versioned account row.
        var store = new VersionedAccountStore(
            InstructorId,
            h.Accounts.Accounts[0].REFRESH_TOKEN_ENCRYPTED!,
            h.Accounts.Accounts[0].SCOPES,
            "school.example.test");
        var scopeA = new VersionedAccountRepository(store);
        var scopeB = new VersionedAccountRepository(store);

        // Media's ingest saves on the shared context in the middle of the copy. Both copies are in flight at the same time; then A finishes, then B.
        var arrived = 0;
        var releaseA = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var releaseB = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var startedA = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var startedB = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        h.Ingest.Handler = async _ =>
        {
            var which = Interlocked.Increment(ref arrived);
            var (scope, started, release) = which == 1 ? (scopeA, startedA, releaseA) : (scopeB, startedB, releaseB);
            started.SetResult();
            await release.Task;
            await scope.SaveChangesAsync(CancellationToken.None); // <- the unrelated save that used to flush the account edit
            return Result.Success(Guid.NewGuid());
        };

        h.AccountRepository = scopeA;
        var jobA = h.TransferJob().RunAsync(importA.SESSION_RECORDING_IMPORT_ID, CancellationToken.None);
        await startedA.Task.WaitAsync(TimeSpan.FromSeconds(5));

        h.AccountRepository = scopeB;
        var jobB = h.TransferJob().RunAsync(importB.SESSION_RECORDING_IMPORT_ID, CancellationToken.None);
        await startedB.Task.WaitAsync(TimeSpan.FromSeconds(5));

        releaseA.SetResult();
        await jobA.WaitAsync(TimeSpan.FromSeconds(5));
        releaseB.SetResult();
        await jobB.WaitAsync(TimeSpan.FromSeconds(5));

        Assert.Equal(RecordingImportStatus.Processing, importA.STATUS);
        Assert.Equal(RecordingImportStatus.Processing, importB.STATUS);
        Assert.Equal(0, importA.ATTEMPTS);
        Assert.Equal(0, importB.ATTEMPTS);
        Assert.Null(importB.ERROR_CODE);
        Assert.Empty(h.Alerts.RecordingImportFailedAlerts);
    }

    // ---- The real repository's tracker handling (no database needed) ---------------------------------------------------------

    private static AppDbContext Context() =>
        new(new DbContextOptionsBuilder<AppDbContext>()
            .UseNpgsql("Host=127.0.0.1;Port=1;Database=unit-test;Username=none;Password=none;Pooling=false")
            .AddInterceptors(new StopAtConnectionOpen())
            .Options);

    private sealed class ReachedDatabaseException : Exception;

    private sealed class StopAtConnectionOpen : Microsoft.EntityFrameworkCore.Diagnostics.DbConnectionInterceptor
    {
        public override InterceptionResult ConnectionOpening(System.Data.Common.DbConnection connection, ConnectionEventData eventData, InterceptionResult result) =>
            throw new ReachedDatabaseException();

        public override ValueTask<InterceptionResult> ConnectionOpeningAsync(
            System.Data.Common.DbConnection connection, ConnectionEventData eventData, InterceptionResult result, CancellationToken cancellationToken = default) =>
            throw new ReachedDatabaseException();
    }

    private INSTRUCTOR_GOOGLE_ACCOUNT NewAccount() =>
        INSTRUCTOR_GOOGLE_ACCOUNT.Connect(InstructorId, "sub", "t@school.example.test", _protector.Encrypt("r"), GoogleScopes.CalendarEventsOwned, new FakeClock(Now), "school.example.test");

    [Fact]
    public void TrackedAccount_AfterTheSetBasedWrite_IsUpToDateAndNotModified()
    {
        using var context = Context();
        var account = NewAccount();
        context.InstructorGoogleAccounts().Attach(account);
        Assert.Equal(EntityState.Unchanged, context.Entry(account).State);

        InstructorGoogleAccountRepository.SyncTrackedValidation(context.Entry(account), Now.AddMinutes(5));

        Assert.Equal(Now.AddMinutes(5), account.LAST_VALIDATED_AT_UTC);
        Assert.Equal(EntityState.Unchanged, context.Entry(account).State);
        Assert.DoesNotContain(context.ChangeTracker.Entries(), e => e.State == EntityState.Modified);
        Assert.Equal(Now.AddMinutes(5), context.Entry(account).Property(a => a.LAST_VALIDATED_AT_UTC).OriginalValue);
    }

    [Fact]
    public void TrackedAccount_OtherPendingEditsAreLeftExactlyAsTheyWere()
    {
        using var context = Context();
        var account = NewAccount();
        context.InstructorGoogleAccounts().Attach(account);
        account.MarkRevoked(GoogleAccountRevokedReason.InvalidGrant, new FakeClock(Now)); // a genuine pending edit

        InstructorGoogleAccountRepository.SyncTrackedValidation(context.Entry(account), Now.AddMinutes(5));

        var entry = context.Entry(account);
        Assert.Equal(EntityState.Modified, entry.State);
        Assert.True(entry.Property(a => a.REVOKED_AT_UTC).IsModified);
        Assert.False(entry.Property(a => a.LAST_VALIDATED_AT_UTC).IsModified);
    }

    [Fact]
    public void ADetachedAccount_IsLeftAlone()
    {
        using var context = Context();
        var account = NewAccount();

        InstructorGoogleAccountRepository.SyncTrackedValidation(context.Entry(account), Now.AddMinutes(5));

        Assert.Null(account.LAST_VALIDATED_AT_UTC);
    }

    [Fact]
    public async Task RecordValidation_IsASetBasedStatement_ThatTranslates_AndNeverQueuesAnythingOnTheContext()
    {
        using var context = Context();
        var account = NewAccount();
        context.InstructorGoogleAccounts().Attach(account);

        var exception = await Record.ExceptionAsync(() =>
            new InstructorGoogleAccountRepository(context).RecordValidationAsync(account, Now.AddMinutes(5), CancellationToken.None));

        // The statement was translated and handed to the connection (which the test refuses to open): proof it is an ExecuteUpdate, not a tracked edit...
        Assert.True(Chain(exception!).Any(e => e is ReachedDatabaseException), exception!.ToString());

        // ...and the failed attempt left the tracked entity exactly as it was: nothing to flush later.
        Assert.Equal(EntityState.Unchanged, context.Entry(account).State);
        Assert.Null(account.LAST_VALIDATED_AT_UTC);
    }

    private static IEnumerable<Exception> Chain(Exception exception)
    {
        for (var current = exception; current is not null; current = current.InnerException)
        {
            yield return current;
        }
    }
}

/// <summary>The shared database row of one instructor's Google account, with the row version every writer must present.</summary>
internal sealed class VersionedAccountStore(Guid instructorUserId, string refreshTokenEncrypted, string scopes, string? hostedDomain)
{
    public Guid InstructorUserId { get; } = instructorUserId;

    public string RefreshTokenEncrypted { get; } = refreshTokenEncrypted;

    public string Scopes { get; } = scopes;

    public string? HostedDomain { get; } = hostedDomain;

    public DateTime? LastValidatedAtUtc { get; set; }

    public DateTime? RevokedAtUtc { get; set; }

    /// <summary>Bumped by every versioned write (what <c>ROW_VERSION</c> is for). The set-based validation stamp does not bump it.</summary>
    public int Version { get; set; }
}

/// <summary>
/// One scope's (one <c>DbContext</c>'s) view of <see cref="VersionedAccountStore"/>: a loaded entity is "tracked"; <see cref="SaveChangesAsync"/> writes it back only if it differs from
/// what was loaded, and - like EF with a <c>ROW_VERSION</c> concurrency token - throws <see cref="DbUpdateConcurrencyException"/> if somebody bumped the version meanwhile.
/// </summary>
internal sealed class VersionedAccountRepository(VersionedAccountStore store) : IInstructorGoogleAccountRepository
{
    private INSTRUCTOR_GOOGLE_ACCOUNT? _tracked;
    private int _loadedVersion;
    private (DateTime? LastValidated, DateTime? Revoked, string? Token) _snapshot;

    public Exception? ThrowOnRecordValidation { get; set; }

    /// <summary>True when saving now would write something (a tracked edit nobody has saved yet).</summary>
    public bool HasPendingChanges => _tracked is not null && Current() != _snapshot;

    private (DateTime?, DateTime?, string?) Current() => (_tracked!.LAST_VALIDATED_AT_UTC, _tracked.REVOKED_AT_UTC, _tracked.REFRESH_TOKEN_ENCRYPTED);

    public Task<INSTRUCTOR_GOOGLE_ACCOUNT?> GetByInstructorUserIdAsync(Guid instructorUserId, CancellationToken cancellationToken)
    {
        if (instructorUserId != store.InstructorUserId)
        {
            return Task.FromResult<INSTRUCTOR_GOOGLE_ACCOUNT?>(null);
        }

        if (_tracked is null)
        {
            _tracked = INSTRUCTOR_GOOGLE_ACCOUNT.Connect(
                store.InstructorUserId, "sub", "teacher@school.example.test", store.RefreshTokenEncrypted, store.Scopes, new FakeClock(LiveTestData.Now), store.HostedDomain);
            if (store.LastValidatedAtUtc is { } validated)
            {
                _tracked.MarkValidated(new FakeClock(validated));
            }

            _loadedVersion = store.Version;
            _snapshot = Current();
        }

        return Task.FromResult<INSTRUCTOR_GOOGLE_ACCOUNT?>(_tracked);
    }

    public Task<IReadOnlyList<Guid>> GetRecordingCandidateInstructorIdsAsync(CancellationToken cancellationToken) =>
        Task.FromResult<IReadOnlyList<Guid>>([store.InstructorUserId]);

    public Task RecordValidationAsync(INSTRUCTOR_GOOGLE_ACCOUNT account, DateTime validatedAtUtc, CancellationToken cancellationToken)
    {
        if (ThrowOnRecordValidation is { } exception)
        {
            throw exception;
        }

        // Set-based: no version check, no version bump, nothing pending afterwards.
        store.LastValidatedAtUtc = validatedAtUtc;
        account.MarkValidated(new FakeClock(validatedAtUtc));
        _snapshot = (validatedAtUtc, _snapshot.Revoked, _snapshot.Token);
        return Task.CompletedTask;
    }

    public void Add(INSTRUCTOR_GOOGLE_ACCOUNT account) => throw new NotSupportedException();

    public Task SaveChangesAsync(CancellationToken cancellationToken)
    {
        if (!HasPendingChanges)
        {
            return Task.CompletedTask;
        }

        if (_loadedVersion != store.Version)
        {
            throw new DbUpdateConcurrencyException("The row was changed by another writer since it was read.");
        }

        store.LastValidatedAtUtc = _tracked!.LAST_VALIDATED_AT_UTC;
        store.RevokedAtUtc = _tracked.REVOKED_AT_UTC;
        store.Version++;
        _loadedVersion = store.Version;
        _snapshot = Current();
        return Task.CompletedTask;
    }
}
