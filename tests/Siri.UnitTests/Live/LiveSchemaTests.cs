using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata;
using Siri.Modules.Catalog.Domain;
using Siri.Modules.Live;
using Siri.Modules.Live.Domain;
using Siri.Modules.Notification.Domain;
using Siri.Persistence;
using Siri.Persistence.Conventions;

namespace Siri.UnitTests.Live;

/// <summary>
/// Pins the database shape the Live contracts freeze (docs/contracts/P11-03-live-module-google-meetings.md
/// §2, P11-04-live-invites-ics-reminders.md §2, P11-05-live-learner-instructor-api-join-gate.md §2) by
/// building the real <see cref="AppDbContext"/> model — including <c>ApplyUppercaseNamingConventions</c> —
/// and asserting table/column/index/FK names and shapes. No database is opened (only model metadata is
/// read), so this runs without Docker. It exists because the naming convention rewrites names after the
/// configurations run (e.g. it would turn <c>REMINDER_24H_…</c> into <c>REMINDER_24_H_…</c>), so the
/// configuration source alone does not prove what lands in the migration.
/// </summary>
public class LiveSchemaTests
{
    private static readonly IModel Model = BuildModel();

    private static IModel BuildModel()
    {
        // .NET loads assemblies lazily and AppDbContext only scans the ones already loaded.
        _ = typeof(LiveModule);
        _ = typeof(COURSE);
        _ = typeof(EMAIL_OUTBOX_MESSAGE);

        var options = new DbContextOptionsBuilder<AppDbContext>()
            .UseNpgsql("Host=localhost;Port=1;Database=live-schema-test;Username=none;Password=none")
            .Options;

        using var context = new AppDbContext(options);
        return context.Model;
    }

    private static IEntityType Entity<T>() => Model.FindEntityType(typeof(T))
        ?? throw new InvalidOperationException($"{typeof(T).Name} is not part of the model.");

    private static IProperty Column(IEntityType entity, string columnName) =>
        entity.GetProperties().SingleOrDefault(p => p.GetColumnName() == columnName)
        ?? throw new InvalidOperationException($"{entity.ClrType.Name} has no column {columnName}.");

    private static IIndex Index(IEntityType entity, string name) =>
        entity.GetIndexes().SingleOrDefault(i => i.GetDatabaseName() == name)
        ?? throw new InvalidOperationException($"{entity.ClrType.Name} has no index {name}.");

    // ---- Tables --------------------------------------------------------------------------------

    [Theory]
    [InlineData(typeof(INSTRUCTOR_GOOGLE_ACCOUNT), "INSTRUCTOR_GOOGLE_ACCOUNTS")]
    [InlineData(typeof(SESSION_MEETING), "SESSION_MEETINGS")]
    [InlineData(typeof(SESSION_INVITE), "SESSION_INVITES")]
    [InlineData(typeof(SESSION_JOIN_LOG), "SESSION_JOIN_LOGS")]
    [InlineData(typeof(SESSION_RECORDING_IMPORT), "SESSION_RECORDING_IMPORTS")]
    public void LiveEntities_LiveInTheLiveSchemaUnderTheContractedTableName(Type clrType, string table)
    {
        var entity = Model.FindEntityType(clrType)!;

        Assert.Equal("LIVE", entity.GetSchema());
        Assert.Equal(table, entity.GetTableName());
        Assert.Equal($"PK_{table}", entity.FindPrimaryKey()!.GetName());
    }

    // ---- Columns -------------------------------------------------------------------------------

    [Theory]
    // INSTRUCTOR_GOOGLE_ACCOUNTS (P11-03 §2.1)
    [InlineData(typeof(INSTRUCTOR_GOOGLE_ACCOUNT), "INSTRUCTOR_GOOGLE_ACCOUNT_ID", false)]
    [InlineData(typeof(INSTRUCTOR_GOOGLE_ACCOUNT), "INSTRUCTOR_USER_ID", false)]
    [InlineData(typeof(INSTRUCTOR_GOOGLE_ACCOUNT), "GOOGLE_SUBJECT", false)]
    [InlineData(typeof(INSTRUCTOR_GOOGLE_ACCOUNT), "GOOGLE_EMAIL", false)]
    [InlineData(typeof(INSTRUCTOR_GOOGLE_ACCOUNT), "REFRESH_TOKEN_ENCRYPTED", true)]
    [InlineData(typeof(INSTRUCTOR_GOOGLE_ACCOUNT), "SCOPES", false)]
    [InlineData(typeof(INSTRUCTOR_GOOGLE_ACCOUNT), "CONNECTED_AT_UTC", false)]
    [InlineData(typeof(INSTRUCTOR_GOOGLE_ACCOUNT), "LAST_VALIDATED_AT_UTC", true)]
    [InlineData(typeof(INSTRUCTOR_GOOGLE_ACCOUNT), "REVOKED_AT_UTC", true)]
    [InlineData(typeof(INSTRUCTOR_GOOGLE_ACCOUNT), "REVOKED_REASON", true)]
    [InlineData(typeof(INSTRUCTOR_GOOGLE_ACCOUNT), "ROW_VERSION", false)]
    [InlineData(typeof(INSTRUCTOR_GOOGLE_ACCOUNT), "CREATED_AT_UTC", false)]
    [InlineData(typeof(INSTRUCTOR_GOOGLE_ACCOUNT), "CREATED_BY", true)]
    [InlineData(typeof(INSTRUCTOR_GOOGLE_ACCOUNT), "UPDATED_AT_UTC", true)]
    [InlineData(typeof(INSTRUCTOR_GOOGLE_ACCOUNT), "UPDATED_BY", true)]
    // SESSION_MEETINGS (P11-03 §2.2)
    [InlineData(typeof(SESSION_MEETING), "SESSION_MEETING_ID", false)]
    [InlineData(typeof(SESSION_MEETING), "SESSION_ID", false)]
    [InlineData(typeof(SESSION_MEETING), "INSTRUCTOR_USER_ID", true)]
    [InlineData(typeof(SESSION_MEETING), "PROVIDER", true)]
    [InlineData(typeof(SESSION_MEETING), "INSTRUCTOR_GOOGLE_ACCOUNT_ID", true)]
    [InlineData(typeof(SESSION_MEETING), "PROVIDER_EVENT_ID", true)]
    [InlineData(typeof(SESSION_MEETING), "MEET_URL_ENCRYPTED", true)]
    [InlineData(typeof(SESSION_MEETING), "SYNC_STATUS", false)]
    [InlineData(typeof(SESSION_MEETING), "ICS_SEQUENCE", false)]
    [InlineData(typeof(SESSION_MEETING), "ATTEMPTS", false)]
    [InlineData(typeof(SESSION_MEETING), "NEXT_RETRY_AT_UTC", true)]
    [InlineData(typeof(SESSION_MEETING), "LAST_SYNC_AT_UTC", true)]
    [InlineData(typeof(SESSION_MEETING), "ERROR", true)]
    [InlineData(typeof(SESSION_MEETING), "MEETING_ALERT_SENT_AT_UTC", true)]
    [InlineData(typeof(SESSION_MEETING), "READINESS_ALERT_SENT_AT_UTC", true)]
    [InlineData(typeof(SESSION_MEETING), "ATTENDEE_SYNC_ALERT_SENT_AT_UTC", true)]
    [InlineData(typeof(SESSION_MEETING), "ROW_VERSION", false)]
    [InlineData(typeof(SESSION_MEETING), "CREATED_AT_UTC", false)]
    // SESSION_INVITES (P11-04 §2.2)
    [InlineData(typeof(SESSION_INVITE), "SESSION_INVITE_ID", false)]
    [InlineData(typeof(SESSION_INVITE), "SESSION_ID", false)]
    [InlineData(typeof(SESSION_INVITE), "USER_ID", false)]
    [InlineData(typeof(SESSION_INVITE), "ROLE", false)]
    [InlineData(typeof(SESSION_INVITE), "STATUS", false)]
    [InlineData(typeof(SESSION_INVITE), "ICS_SEQUENCE_SENT", true)]
    [InlineData(typeof(SESSION_INVITE), "INVITE_SENT_AT_UTC", true)]
    [InlineData(typeof(SESSION_INVITE), "CANCEL_SENT_AT_UTC", true)]
    [InlineData(typeof(SESSION_INVITE), "REMINDER_24H_SENT_AT_UTC", true)]
    [InlineData(typeof(SESSION_INVITE), "REMINDER_1H_SENT_AT_UTC", true)]
    [InlineData(typeof(SESSION_INVITE), "GOOGLE_ATTENDEE_SYNCED_AT_UTC", true)]
    [InlineData(typeof(SESSION_INVITE), "ERROR", true)]
    [InlineData(typeof(SESSION_INVITE), "ROW_VERSION", false)]
    [InlineData(typeof(SESSION_INVITE), "CREATED_AT_UTC", false)]
    // SESSION_JOIN_LOGS (P11-05 §2)
    [InlineData(typeof(INSTRUCTOR_GOOGLE_ACCOUNT), "HOSTED_DOMAIN", true)]
    [InlineData(typeof(INSTRUCTOR_GOOGLE_ACCOUNT), "ACCOUNT_KIND_CHECKED_AT_UTC", true)]
    // SESSION_RECORDING_IMPORTS (P11-13 §3)
    [InlineData(typeof(SESSION_RECORDING_IMPORT), "SESSION_RECORDING_IMPORT_ID", false)]
    [InlineData(typeof(SESSION_RECORDING_IMPORT), "SESSION_ID", false)]
    [InlineData(typeof(SESSION_RECORDING_IMPORT), "COURSE_ID", false)]
    [InlineData(typeof(SESSION_RECORDING_IMPORT), "INSTRUCTOR_USER_ID", false)]
    [InlineData(typeof(SESSION_RECORDING_IMPORT), "STATUS", false)]
    [InlineData(typeof(SESSION_RECORDING_IMPORT), "ATTEMPTS", false)]
    [InlineData(typeof(SESSION_RECORDING_IMPORT), "NEXT_ATTEMPT_AT_UTC", true)]
    [InlineData(typeof(SESSION_RECORDING_IMPORT), "LEASE_UNTIL_UTC", true)]
    [InlineData(typeof(SESSION_RECORDING_IMPORT), "GOOGLE_RECORDING_NAME", true)]
    [InlineData(typeof(SESSION_RECORDING_IMPORT), "GOOGLE_FILE_ID", true)]
    [InlineData(typeof(SESSION_RECORDING_IMPORT), "MEDIA_ASSET_ID", true)]
    [InlineData(typeof(SESSION_RECORDING_IMPORT), "EPISODE_ID", true)]
    [InlineData(typeof(SESSION_RECORDING_IMPORT), "ERROR_CODE", true)]
    [InlineData(typeof(SESSION_RECORDING_IMPORT), "SEARCH_UNTIL_UTC", false)]
    [InlineData(typeof(SESSION_RECORDING_IMPORT), "COMPLETED_AT_UTC", true)]
    [InlineData(typeof(SESSION_RECORDING_IMPORT), "ROW_VERSION", false)]
    [InlineData(typeof(SESSION_RECORDING_IMPORT), "CREATED_AT_UTC", false)]
    [InlineData(typeof(SESSION_RECORDING_IMPORT), "CREATED_BY", true)]
    [InlineData(typeof(SESSION_RECORDING_IMPORT), "UPDATED_AT_UTC", true)]
    [InlineData(typeof(SESSION_RECORDING_IMPORT), "UPDATED_BY", true)]
    [InlineData(typeof(SESSION_JOIN_LOG), "SESSION_JOIN_LOG_ID", false)]
    [InlineData(typeof(SESSION_JOIN_LOG), "SESSION_ID", false)]
    [InlineData(typeof(SESSION_JOIN_LOG), "COURSE_ID", false)]
    [InlineData(typeof(SESSION_JOIN_LOG), "USER_ID", false)]
    [InlineData(typeof(SESSION_JOIN_LOG), "ROLE", false)]
    [InlineData(typeof(SESSION_JOIN_LOG), "AUTH_SESSION_ID", true)]
    [InlineData(typeof(SESSION_JOIN_LOG), "JOINED_AT_UTC", false)]
    [InlineData(typeof(SESSION_JOIN_LOG), "IP_ADDRESS", true)]
    [InlineData(typeof(SESSION_JOIN_LOG), "USER_AGENT", true)]
    public void Columns_ExistWithTheContractedNameAndNullability(Type clrType, string column, bool nullable)
    {
        var entity = Model.FindEntityType(clrType)!;

        Assert.Equal(nullable, Column(entity, column).IsNullable);
    }

    [Theory]
    [InlineData(typeof(INSTRUCTOR_GOOGLE_ACCOUNT), 17)] // 11 business columns + HOSTED_DOMAIN + ACCOUNT_KIND_CHECKED_AT_UTC (P11-13) + 4 audit columns
    [InlineData(typeof(SESSION_MEETING), 21)] // 17 business columns (P11-04 added READINESS_ALERT_SENT_AT_UTC + ATTENDEE_SYNC_ALERT_SENT_AT_UTC) + 4 audit columns
    [InlineData(typeof(SESSION_INVITE), 17)] // 13 business columns + 4 audit columns
    [InlineData(typeof(SESSION_JOIN_LOG), 9)]
    [InlineData(typeof(SESSION_RECORDING_IMPORT), 20)] // 16 business columns (P11-13) + 4 audit columns
    public void Entities_HaveNoColumnsBeyondTheContract(Type clrType, int expectedColumnCount)
    {
        var entity = Model.FindEntityType(clrType)!;

        // Derived members (IsActive/IsUsable/CanRequestResync) and shadow foreign keys must not appear.
        Assert.Equal(expectedColumnCount, entity.GetProperties().Count());
        Assert.DoesNotContain(entity.GetProperties(), p => p.IsShadowProperty());
    }

    [Theory]
    [InlineData(typeof(INSTRUCTOR_GOOGLE_ACCOUNT), "REFRESH_TOKEN_ENCRYPTED", "text", null)]
    [InlineData(typeof(INSTRUCTOR_GOOGLE_ACCOUNT), "GOOGLE_SUBJECT", null, 64)]
    [InlineData(typeof(INSTRUCTOR_GOOGLE_ACCOUNT), "GOOGLE_EMAIL", null, 320)]
    [InlineData(typeof(INSTRUCTOR_GOOGLE_ACCOUNT), "SCOPES", null, 500)]
    [InlineData(typeof(INSTRUCTOR_GOOGLE_ACCOUNT), "REVOKED_REASON", null, 40)]
    [InlineData(typeof(INSTRUCTOR_GOOGLE_ACCOUNT), "HOSTED_DOMAIN", null, 255)]
    [InlineData(typeof(SESSION_RECORDING_IMPORT), "STATUS", null, 24)]
    [InlineData(typeof(SESSION_RECORDING_IMPORT), "GOOGLE_RECORDING_NAME", null, 200)]
    [InlineData(typeof(SESSION_RECORDING_IMPORT), "GOOGLE_FILE_ID", null, 200)]
    [InlineData(typeof(SESSION_RECORDING_IMPORT), "ERROR_CODE", null, 60)]
    [InlineData(typeof(SESSION_MEETING), "MEET_URL_ENCRYPTED", "text", null)]
    [InlineData(typeof(SESSION_MEETING), "PROVIDER", null, 20)]
    [InlineData(typeof(SESSION_MEETING), "SYNC_STATUS", null, 20)]
    [InlineData(typeof(SESSION_MEETING), "PROVIDER_EVENT_ID", null, 200)]
    [InlineData(typeof(SESSION_MEETING), "ERROR", null, 500)]
    [InlineData(typeof(SESSION_INVITE), "ROLE", null, 16)]
    [InlineData(typeof(SESSION_INVITE), "STATUS", null, 16)]
    [InlineData(typeof(SESSION_INVITE), "ERROR", null, 300)]
    [InlineData(typeof(SESSION_JOIN_LOG), "ROLE", null, 16)]
    [InlineData(typeof(SESSION_JOIN_LOG), "IP_ADDRESS", null, 64)]
    [InlineData(typeof(SESSION_JOIN_LOG), "USER_AGENT", null, 300)]
    public void Columns_HaveTheContractedTypeOrLength(Type clrType, string column, string? columnType, int? maxLength)
    {
        var property = Column(Model.FindEntityType(clrType)!, column);

        if (columnType is not null)
        {
            Assert.Equal(columnType, property.GetColumnType());
        }

        Assert.Equal(maxLength, property.GetMaxLength());
    }

    [Theory]
    [InlineData(typeof(INSTRUCTOR_GOOGLE_ACCOUNT), "CONNECTED_AT_UTC")]
    [InlineData(typeof(INSTRUCTOR_GOOGLE_ACCOUNT), "LAST_VALIDATED_AT_UTC")]
    [InlineData(typeof(INSTRUCTOR_GOOGLE_ACCOUNT), "REVOKED_AT_UTC")]
    [InlineData(typeof(INSTRUCTOR_GOOGLE_ACCOUNT), "ACCOUNT_KIND_CHECKED_AT_UTC")]
    [InlineData(typeof(SESSION_RECORDING_IMPORT), "NEXT_ATTEMPT_AT_UTC")]
    [InlineData(typeof(SESSION_RECORDING_IMPORT), "LEASE_UNTIL_UTC")]
    [InlineData(typeof(SESSION_RECORDING_IMPORT), "SEARCH_UNTIL_UTC")]
    [InlineData(typeof(SESSION_RECORDING_IMPORT), "COMPLETED_AT_UTC")]
    [InlineData(typeof(SESSION_MEETING), "NEXT_RETRY_AT_UTC")]
    [InlineData(typeof(SESSION_MEETING), "LAST_SYNC_AT_UTC")]
    [InlineData(typeof(SESSION_MEETING), "MEETING_ALERT_SENT_AT_UTC")]
    [InlineData(typeof(SESSION_MEETING), "READINESS_ALERT_SENT_AT_UTC")]
    [InlineData(typeof(SESSION_MEETING), "ATTENDEE_SYNC_ALERT_SENT_AT_UTC")]
    [InlineData(typeof(SESSION_INVITE), "INVITE_SENT_AT_UTC")]
    [InlineData(typeof(SESSION_INVITE), "CANCEL_SENT_AT_UTC")]
    [InlineData(typeof(SESSION_INVITE), "REMINDER_24H_SENT_AT_UTC")]
    [InlineData(typeof(SESSION_INVITE), "REMINDER_1H_SENT_AT_UTC")]
    [InlineData(typeof(SESSION_INVITE), "GOOGLE_ATTENDEE_SYNCED_AT_UTC")]
    [InlineData(typeof(SESSION_JOIN_LOG), "JOINED_AT_UTC")]
    public void Timestamps_AreTimestamptzWithMillisecondPrecision(Type clrType, string column)
    {
        Assert.Equal(3, Column(Model.FindEntityType(clrType)!, column).GetPrecision());
    }

    [Theory]
    [InlineData(typeof(SESSION_MEETING), "PROVIDER")]
    [InlineData(typeof(SESSION_MEETING), "SYNC_STATUS")]
    [InlineData(typeof(SESSION_INVITE), "ROLE")]
    [InlineData(typeof(SESSION_INVITE), "STATUS")]
    [InlineData(typeof(SESSION_JOIN_LOG), "ROLE")]
    [InlineData(typeof(SESSION_RECORDING_IMPORT), "STATUS")]
    public void Enums_AreStoredAsStrings(Type clrType, string column)
    {
        var property = Column(Model.FindEntityType(clrType)!, column);

        Assert.Equal(typeof(string), property.GetProviderClrType() ?? property.GetValueConverter()?.ProviderClrType);
    }

    [Theory]
    [InlineData(typeof(INSTRUCTOR_GOOGLE_ACCOUNT))]
    [InlineData(typeof(SESSION_MEETING))]
    [InlineData(typeof(SESSION_INVITE))]
    [InlineData(typeof(SESSION_RECORDING_IMPORT))]
    public void MutableLiveEntities_HaveAByteaConcurrencyToken(Type clrType)
    {
        var token = Column(Model.FindEntityType(clrType)!, "ROW_VERSION");

        Assert.True(token.IsConcurrencyToken);
        Assert.Equal("bytea", token.GetColumnType());
    }

    [Fact]
    public void SessionJoinLog_IsAppendOnly_NoAuditColumnsNoConcurrencyToken()
    {
        var entity = Entity<SESSION_JOIN_LOG>();

        Assert.False(typeof(IAuditable).IsAssignableFrom(entity.ClrType));
        Assert.False(typeof(ISoftDelete).IsAssignableFrom(entity.ClrType));
        Assert.DoesNotContain(entity.GetProperties(), p => p.IsConcurrencyToken);
        Assert.Null(entity.FindProperty("CreatedAtUtc"));
        Assert.Null(entity.FindProperty("UpdatedAtUtc"));
    }

    [Theory]
    [InlineData(typeof(INSTRUCTOR_GOOGLE_ACCOUNT))]
    [InlineData(typeof(SESSION_MEETING))]
    [InlineData(typeof(SESSION_INVITE))]
    [InlineData(typeof(SESSION_RECORDING_IMPORT))]
    public void AuditableLiveEntities_KeepPascalCaseAuditPropertiesWithUppercaseColumns(Type clrType)
    {
        var entity = Model.FindEntityType(clrType)!;

        Assert.Equal("CREATED_AT_UTC", entity.FindProperty(nameof(IAuditable.CreatedAtUtc))!.GetColumnName());
        Assert.Equal("CREATED_BY", entity.FindProperty(nameof(IAuditable.CreatedBy))!.GetColumnName());
        Assert.Equal("UPDATED_AT_UTC", entity.FindProperty(nameof(IAuditable.UpdatedAtUtc))!.GetColumnName());
        Assert.Equal("UPDATED_BY", entity.FindProperty(nameof(IAuditable.UpdatedBy))!.GetColumnName());
    }

    [Fact]
    public void DefaultedCounters_HaveZeroAsTheDatabaseDefault()
    {
        var meeting = Entity<SESSION_MEETING>();

        Assert.Equal(0, Column(meeting, "ICS_SEQUENCE").GetDefaultValue());
        Assert.Equal(0, Column(meeting, "ATTEMPTS").GetDefaultValue());
    }

    // ---- Indexes -------------------------------------------------------------------------------

    [Fact]
    public void InstructorGoogleAccounts_HaveOneAccountPerInstructor()
    {
        var index = Index(Entity<INSTRUCTOR_GOOGLE_ACCOUNT>(), "IX_INSTR_GOOGLE_ACCT_USER_ID");

        Assert.True(index.IsUnique);
        Assert.Equal(new[] { "INSTRUCTOR_USER_ID" }, index.Properties.Select(p => p.GetColumnName()));
        Assert.Single(Entity<INSTRUCTOR_GOOGLE_ACCOUNT>().GetIndexes());
    }

    [Fact]
    public void SessionMeetings_HaveTheContractedIndexes()
    {
        var entity = Entity<SESSION_MEETING>();

        var bySession = Index(entity, "IX_SESSION_MEETINGS_SESSION_ID");
        Assert.True(bySession.IsUnique);
        Assert.Equal(new[] { "SESSION_ID" }, bySession.Properties.Select(p => p.GetColumnName()));

        var due = Index(entity, "IX_SESSION_MEETINGS_SYNC_DUE");
        Assert.False(due.IsUnique);
        Assert.Equal(new[] { "SYNC_STATUS", "NEXT_RETRY_AT_UTC" }, due.Properties.Select(p => p.GetColumnName()));
        Assert.Equal("\"SYNC_STATUS\" IN ('Pending','PendingDelete')", due.GetFilter());

        var byInstructor = Index(entity, "IX_SESSION_MEETINGS_INSTR_USER_ID");
        Assert.False(byInstructor.IsUnique);
        Assert.Equal(new[] { "INSTRUCTOR_USER_ID" }, byInstructor.Properties.Select(p => p.GetColumnName()));
    }

    [Fact]
    public void SessionInvites_HaveTheContractedIndexes()
    {
        var entity = Entity<SESSION_INVITE>();

        var pair = Index(entity, "IX_SESSION_INVITES_SESSION_USER");
        Assert.True(pair.IsUnique);
        Assert.Equal(new[] { "SESSION_ID", "USER_ID" }, pair.Properties.Select(p => p.GetColumnName()));

        var byUser = Index(entity, "IX_SESSION_INVITES_USER_ID");
        Assert.False(byUser.IsUnique);
        Assert.Equal(new[] { "USER_ID" }, byUser.Properties.Select(p => p.GetColumnName()));

        var pending = Index(entity, "IX_SESSION_INVITES_PENDING");
        Assert.Equal(new[] { "SESSION_ID" }, pending.Properties.Select(p => p.GetColumnName()));
        Assert.Equal("\"STATUS\" = 'Pending'", pending.GetFilter());

        Assert.Equal(3, entity.GetIndexes().Count());
    }

    [Fact]
    public void SessionJoinLogs_HaveTheContractedNonUniqueIndexes()
    {
        var entity = Entity<SESSION_JOIN_LOG>();

        var sessionUser = Index(entity, "IX_SESSION_JOIN_LOGS_SESSION_USER");
        Assert.False(sessionUser.IsUnique);
        Assert.Equal(new[] { "SESSION_ID", "USER_ID" }, sessionUser.Properties.Select(p => p.GetColumnName()));

        var userCourse = Index(entity, "IX_SESSION_JOIN_LOGS_USER_COURSE");
        Assert.False(userCourse.IsUnique);
        Assert.Equal(new[] { "USER_ID", "COURSE_ID" }, userCourse.Properties.Select(p => p.GetColumnName()));

        Assert.Equal(2, entity.GetIndexes().Count());
    }

    [Fact]
    public void SessionRecordingImports_HaveTheContractedIndexes()
    {
        var entity = Entity<SESSION_RECORDING_IMPORT>();

        var bySession = Index(entity, "IX_SESSION_RECORDING_IMPORTS_SESSION_ID");
        Assert.True(bySession.IsUnique);
        Assert.Equal(new[] { "SESSION_ID" }, bySession.Properties.Select(p => p.GetColumnName()));

        var due = Index(entity, "IX_SESSION_RECORDING_IMPORTS_DUE");
        Assert.False(due.IsUnique);
        Assert.Equal(new[] { "STATUS", "NEXT_ATTEMPT_AT_UTC" }, due.Properties.Select(p => p.GetColumnName()));

        Assert.Equal(2, entity.GetIndexes().Count());
    }

    [Fact]
    public void SessionRecordingImports_AttemptsDefaultToZero_AndDerivedMembersAreNotColumns()
    {
        var entity = Entity<SESSION_RECORDING_IMPORT>();

        Assert.Equal(0, Column(entity, "ATTEMPTS").GetDefaultValue());
        Assert.Null(entity.FindProperty(nameof(SESSION_RECORDING_IMPORT.IsTerminal)));
        Assert.Null(entity.FindProperty(nameof(SESSION_RECORDING_IMPORT.CanRetry)));
    }

    [Fact]
    public void InstructorGoogleAccounts_DerivedKindMembersAreNotColumns()
    {
        var entity = Entity<INSTRUCTOR_GOOGLE_ACCOUNT>();

        Assert.Null(entity.FindProperty(nameof(INSTRUCTOR_GOOGLE_ACCOUNT.AccountKind)));
        Assert.Null(entity.FindProperty(nameof(INSTRUCTOR_GOOGLE_ACCOUNT.HasRecordingScopes)));
    }

    // ---- Foreign keys --------------------------------------------------------------------------

    [Fact]
    public void SessionMeetings_OnlyForeignKeyIsTheGoogleAccountAndItDoesNotCascade()
    {
        var foreignKeys = Entity<SESSION_MEETING>().GetForeignKeys().ToList();

        var fk = Assert.Single(foreignKeys);
        Assert.Equal("FK_SESSION_MEETINGS_GOOGLE_ACCT", fk.GetConstraintName());
        Assert.Equal(typeof(INSTRUCTOR_GOOGLE_ACCOUNT), fk.PrincipalEntityType.ClrType);
        Assert.Equal(new[] { "INSTRUCTOR_GOOGLE_ACCOUNT_ID" }, fk.Properties.Select(p => p.GetColumnName()));
        Assert.Equal(DeleteBehavior.NoAction, fk.DeleteBehavior);
    }

    [Theory]
    [InlineData(typeof(INSTRUCTOR_GOOGLE_ACCOUNT))]
    [InlineData(typeof(SESSION_INVITE))]
    [InlineData(typeof(SESSION_JOIN_LOG))]
    [InlineData(typeof(SESSION_RECORDING_IMPORT))]
    public void OtherLiveEntities_HaveNoForeignKeys(Type clrType)
    {
        // Every id points into another module's schema (or is the principal) — no cross-schema FKs, and
        // nothing may ever cascade into invite/join-log evidence.
        Assert.Empty(Model.FindEntityType(clrType)!.GetForeignKeys());
    }

    [Fact]
    public void NoLiveForeignKeyCascades()
    {
        var liveEntities = Model.GetEntityTypes().Where(e => e.GetSchema() == "LIVE");

        Assert.DoesNotContain(
            liveEntities.SelectMany(e => e.GetForeignKeys()),
            fk => fk.DeleteBehavior is DeleteBehavior.Cascade or DeleteBehavior.SetNull or DeleteBehavior.ClientCascade);
    }

    // ---- Columns added to other modules' tables ------------------------------------------------

    [Fact]
    public void EmailOutbox_HasTheOptionalCalendarPartColumns()
    {
        var entity = Entity<EMAIL_OUTBOX_MESSAGE>();

        var ics = Column(entity, "CALENDAR_ICS");
        Assert.True(ics.IsNullable);
        Assert.Equal("text", ics.GetColumnType());

        var method = Column(entity, "CALENDAR_METHOD");
        Assert.True(method.IsNullable);
        Assert.Equal(10, method.GetMaxLength());
    }

    [Fact]
    public void Courses_HaveTheGoogleAttendeeSyncColumnDefaultingToFalse()
    {
        var column = Column(Entity<COURSE>(), "GOOGLE_ATTENDEE_SYNC_ENABLED");

        Assert.False(column.IsNullable);
        Assert.Equal(typeof(bool), column.ClrType);
        Assert.Equal(false, column.GetDefaultValue());
    }
}
