using System.Linq.Expressions;
using Microsoft.EntityFrameworkCore;

namespace Siri.Persistence.Conventions;

public static class ModelBuilderExtensions
{
    /// <summary>
    /// Adds a global query filter (<c>WHERE IsDeleted = 0</c>) to every entity implementing
    /// <see cref="ISoftDelete"/>, so soft-deleted rows are excluded from normal queries without
    /// every module having to repeat the filter by hand. Call once from
    /// <see cref="Microsoft.EntityFrameworkCore.DbContext.OnModelCreating"/> after module
    /// configurations have been applied.
    /// </summary>
    public static void ApplySoftDeleteQueryFilter(this ModelBuilder modelBuilder)
    {
        foreach (var entityType in modelBuilder.Model.GetEntityTypes())
        {
            if (!typeof(ISoftDelete).IsAssignableFrom(entityType.ClrType))
            {
                continue;
            }

            var parameter = Expression.Parameter(entityType.ClrType, "e");
            var isDeletedProperty = Expression.Property(parameter, nameof(ISoftDelete.IsDeleted));
            var notDeleted = Expression.Equal(isDeletedProperty, Expression.Constant(false));
            var lambda = Expression.Lambda(notDeleted, parameter);

            modelBuilder.Entity(entityType.ClrType).HasQueryFilter(lambda);
        }
    }
}
