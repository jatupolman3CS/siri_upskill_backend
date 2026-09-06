using Microsoft.EntityFrameworkCore;
using Siri.Modules.Catalog.Domain;
using Siri.Persistence;
using Siri.Persistence.Interceptors;

namespace Siri.UnitTests.Persistence;

/// <summary>
/// PostgreSQL has no <c>rowversion</c>, so <see cref="ConcurrencyTokenInterceptor"/> is what keeps
/// optimistic concurrency working after task P0-41 — if it silently stops rotating tokens, every
/// stale-write conflict this codebase relies on (course builder autosave, order and enrollment
/// updates) turns into a last-writer-wins overwrite with no error. These tests pin that behaviour.
/// <para>
/// No database is touched: the interceptor only reads and writes EF's change tracker, so a context
/// built over a never-opened connection string is enough.
/// </para>
/// </summary>
public class ConcurrencyTokenInterceptorTests
{
    private static AppDbContext CreateContext() =>
        new(new DbContextOptionsBuilder<AppDbContext>()
            .UseNpgsql("Host=localhost;Port=1;Database=unit-test;Username=none;Password=none")
            .Options);

    private static COURSE NewCourse() =>
        COURSE.Create(
            slug: "unit-test-course",
            title: "คอร์สทดสอบ",
            instructorId: Guid.CreateVersion7(),
            categoryId: Guid.CreateVersion7(),
            level: CourseLevel.Beginner,
            language: CourseLanguage.Thai,
            price: 990m);

    [Fact]
    public void Apply_AddedEntity_AssignsConcurrencyToken()
    {
        using var context = CreateContext();
        var course = NewCourse();
        context.Add(course);

        Assert.Empty(course.RowVersion);

        ConcurrencyTokenInterceptor.Apply(context);

        var token = (byte[]?)context.Entry(course).Property(nameof(COURSE.RowVersion)).CurrentValue;
        Assert.NotNull(token);
        Assert.Equal(ConcurrencyTokenInterceptor.TokenLengthBytes, token!.Length);
    }

    [Fact]
    public void Apply_ModifiedEntity_ReplacesTokenButLeavesOriginalValueAlone()
    {
        using var context = CreateContext();
        var course = NewCourse();
        context.Attach(course);

        var entry = context.Entry(course);
        var loadedToken = new byte[] { 1, 2, 3, 4, 5, 6, 7, 8 };
        entry.Property(nameof(COURSE.RowVersion)).OriginalValue = loadedToken;
        entry.Property(nameof(COURSE.RowVersion)).CurrentValue = loadedToken;
        entry.State = EntityState.Modified;

        ConcurrencyTokenInterceptor.Apply(context);

        var current = (byte[])entry.Property(nameof(COURSE.RowVersion)).CurrentValue!;
        var original = (byte[])entry.Property(nameof(COURSE.RowVersion)).OriginalValue!;

        Assert.Equal(ConcurrencyTokenInterceptor.TokenLengthBytes, current.Length);
        Assert.NotEqual(loadedToken, current);

        // The original value is the WHERE clause of the generated UPDATE — overwriting it here would
        // disable conflict detection everywhere, and would also clobber the value AutosaveCourseHandler
        // assigns from the token the client echoed back.
        Assert.Equal(loadedToken, original);
    }

    [Fact]
    public void Apply_UnchangedEntity_LeavesTokenAlone()
    {
        using var context = CreateContext();
        var course = NewCourse();
        context.Attach(course);

        var entry = context.Entry(course);
        var loadedToken = new byte[] { 9, 9, 9, 9, 9, 9, 9, 9 };
        entry.Property(nameof(COURSE.RowVersion)).OriginalValue = loadedToken;
        entry.Property(nameof(COURSE.RowVersion)).CurrentValue = loadedToken;

        // Writing CurrentValue on a tracked entity flips it to Modified, which would make this test
        // assert the Modified path by accident. Force it back so "Unchanged" is really under test.
        entry.State = EntityState.Unchanged;
        Assert.Equal(EntityState.Unchanged, entry.State);

        ConcurrencyTokenInterceptor.Apply(context);

        Assert.Equal(loadedToken, (byte[])entry.Property(nameof(COURSE.RowVersion)).CurrentValue!);
        Assert.Equal(EntityState.Unchanged, entry.State);
    }

    [Fact]
    public void Apply_EntityWithoutConcurrencyToken_IsNotModified()
    {
        using var context = CreateContext();
        var category = CATEGORY.Create("test", "ทดสอบ", "Test", null, null, 0);
        context.Add(category);

        var entry = context.Entry(category);

        ConcurrencyTokenInterceptor.Apply(context);

        // No byte[] concurrency token on this entity, so nothing to rotate — and nothing extra should
        // have been marked modified as a side effect.
        Assert.DoesNotContain(
            entry.Metadata.GetProperties(),
            p => p.IsConcurrencyToken && p.ClrType == typeof(byte[]));
        Assert.Equal(EntityState.Added, entry.State);
    }

    [Fact]
    public void Apply_NullContext_DoesNotThrow()
    {
        var exception = Record.Exception(() => ConcurrencyTokenInterceptor.Apply(null));
        Assert.Null(exception);
    }
}
