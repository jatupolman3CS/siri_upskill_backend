using System.Text;
using Microsoft.EntityFrameworkCore;
using Siri.Persistence;

namespace Siri.ArchitectureTests;

/// <summary>
/// PostgreSQL truncates any identifier longer than <see cref="MaxIdentifierBytes"/> bytes
/// <em>silently</em> (SQL Server allowed 128, which is why this never mattered before task P0-41).
/// Two long names that share a prefix therefore collapse into the same identifier, and the failure
/// shows up as a confusing duplicate-object error during migration — or, worse, as an index that
/// quietly went missing.
/// <para>
/// This codebase is unusually exposed to that: <c>ApplyUppercaseNamingConventions</c> rewrites every
/// name to UPPER_SNAKE_CASE, which <em>adds</em> characters, and EF's generated FK/index names already
/// concatenate table and column names. The longest name today is 49 bytes, so there is headroom — the
/// point of this test is to fail loudly the day someone adds a name that eats it, rather than
/// truncating automatically and leaving an unreadable identifier in the database.
/// </para>
/// </summary>
public class DatabaseIdentifierLengthTests
{
    private const int MaxIdentifierBytes = 63;

    [Fact]
    public void EveryDatabaseIdentifier_FitsWithinPostgresLimit()
    {
        // Touch the catalog so every Siri.Modules.* assembly is loaded before AppDbContext scans
        // AppDomain.CurrentDomain.GetAssemblies() for IEntityTypeConfiguration implementations —
        // .NET loads assemblies lazily, and a half-loaded model would make this test vacuous.
        var moduleCount = ModuleAssemblyCatalog.Modules.Count;
        Assert.Equal(11, moduleCount);

        // A connection string is required to build the model but is never opened: only Model metadata
        // is read here, so this test needs no database and no Docker.
        var options = new DbContextOptionsBuilder<AppDbContext>()
            .UseNpgsql("Host=localhost;Port=1;Database=architecture-test;Username=none;Password=none")
            .Options;

        using var context = new AppDbContext(options);

        var offenders = new List<string>();

        void Check(string kind, string? name)
        {
            if (string.IsNullOrEmpty(name))
            {
                return;
            }

            var byteLength = Encoding.UTF8.GetByteCount(name);
            if (byteLength > MaxIdentifierBytes)
            {
                offenders.Add($"{kind} '{name}' is {byteLength} bytes (max {MaxIdentifierBytes})");
            }
        }

        foreach (var entityType in context.Model.GetEntityTypes())
        {
            Check("schema", entityType.GetSchema());
            Check("table", entityType.GetTableName());

            foreach (var property in entityType.GetProperties())
            {
                Check("column", property.GetColumnName());
            }

            foreach (var key in entityType.GetKeys())
            {
                Check("key", key.GetName());
            }

            foreach (var foreignKey in entityType.GetForeignKeys())
            {
                Check("foreign key", foreignKey.GetConstraintName());
            }

            foreach (var index in entityType.GetIndexes())
            {
                Check("index", index.GetDatabaseName());
            }
        }

        Assert.True(
            offenders.Count == 0,
            $"PostgreSQL truncates identifiers over {MaxIdentifierBytes} bytes silently. Shorten these "
            + $"(e.g. with an explicit HasDatabaseName/HasConstraintName) rather than relying on truncation:"
            + Environment.NewLine
            + string.Join(Environment.NewLine, offenders.Order()));
    }
}
