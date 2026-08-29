using System.Reflection;
using Microsoft.EntityFrameworkCore;
using Siri.Persistence.Conventions;

namespace Siri.Persistence;

/// <summary>
/// The single EF Core context for the whole modular monolith (one physical database, schema per
/// module — see ARCHITECTURE.md section 1). Intentionally has no <c>DbSet</c> properties: each
/// module owns its own entities and adds them by placing an <see cref="IEntityTypeConfiguration{TEntity}"/>
/// under its own <c>Infrastructure/</c> folder. Persistence never references module projects
/// (that would invert the dependency direction), so configurations are picked up by scanning the
/// already-loaded <c>Siri.Modules.*</c> assemblies instead.
/// </summary>
public sealed class AppDbContext(DbContextOptions<AppDbContext> options) : DbContext(options)
{
    protected override void OnModelCreating(ModelBuilder modelBuilder)
    {
        base.OnModelCreating(modelBuilder);

        foreach (var moduleAssembly in GetModuleAssemblies())
        {
            modelBuilder.ApplyConfigurationsFromAssembly(moduleAssembly);
        }

        modelBuilder.ApplySoftDeleteQueryFilter();
        modelBuilder.ApplyUppercaseNamingConventions();
    }

    private static IEnumerable<Assembly> GetModuleAssemblies() =>
        AppDomain.CurrentDomain.GetAssemblies()
            .Where(assembly => assembly.GetName().Name?.StartsWith("Siri.Modules.", StringComparison.Ordinal) == true);
}
