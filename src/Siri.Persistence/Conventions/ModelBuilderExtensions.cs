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

    /// <summary>
    /// Enforces UPPERCASE schema, UPPER_SNAKE_CASE table/column names, and uppercase constraints without dots
    /// across all entity types in the model.
    /// </summary>
    public static void ApplyUppercaseNamingConventions(this ModelBuilder modelBuilder)
    {
        foreach (var entityType in modelBuilder.Model.GetEntityTypes())
        {
            var schema = entityType.GetSchema();
            if (!string.IsNullOrEmpty(schema))
            {
                entityType.SetSchema(schema.ToUpperInvariant());
            }

            var tableName = entityType.GetTableName();
            if (!string.IsNullOrEmpty(tableName))
            {
                entityType.SetTableName(ToSnakeCaseUpper(tableName));
            }

            foreach (var property in entityType.GetProperties())
            {
                var columnName = property.GetColumnName();
                if (!string.IsNullOrEmpty(columnName))
                {
                    property.SetColumnName(ToSnakeCaseUpper(columnName));
                }
            }

            foreach (var key in entityType.GetKeys())
            {
                var keyName = key.GetName();
                if (!string.IsNullOrEmpty(keyName))
                {
                    key.SetName(ToSnakeCaseUpper(keyName));
                }
            }

            foreach (var fk in entityType.GetForeignKeys())
            {
                var fkName = fk.GetConstraintName();
                if (!string.IsNullOrEmpty(fkName))
                {
                    fk.SetConstraintName(ToSnakeCaseUpper(fkName));
                }
            }

            foreach (var index in entityType.GetIndexes())
            {
                var indexName = index.GetDatabaseName();
                if (!string.IsNullOrEmpty(indexName))
                {
                    index.SetDatabaseName(ToSnakeCaseUpper(indexName));
                }
            }
        }
    }

    public static string ToSnakeCaseUpper(string input)
    {
        if (string.IsNullOrWhiteSpace(input))
        {
            return input;
        }

        var cleaned = input.Replace(".", "_");
        var snake = System.Text.RegularExpressions.Regex.Replace(cleaned, @"(?<=[a-z0-9])(?=[A-Z])|(?<=[A-Z])(?=[A-Z][a-z])", "_");
        return snake.ToUpperInvariant();
    }
}
