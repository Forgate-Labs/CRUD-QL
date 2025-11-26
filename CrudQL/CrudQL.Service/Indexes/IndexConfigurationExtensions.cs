using System;
using System.Linq;
using CrudQL.Service.Entities;
using Microsoft.EntityFrameworkCore;

namespace CrudQL.Service.Indexes;

public static class IndexConfigurationExtensions
{
    public static void ApplyCrudQlIndexes(this ModelBuilder modelBuilder, ICrudEntityRegistry registry)
    {
        ArgumentNullException.ThrowIfNull(modelBuilder);
        ArgumentNullException.ThrowIfNull(registry);

        foreach (var registration in registry.Entities)
        {
            if (registration.IndexConfig == null)
            {
                continue;
            }

            var entityTypeBuilder = modelBuilder.Entity(registration.ClrType);

            foreach (var indexDef in registration.IndexConfig.Indexes)
            {
                var propertyNames = indexDef.Fields.Select(f => f.FieldName).ToArray();

                var indexBuilder = entityTypeBuilder.HasIndex(propertyNames)
                    .HasDatabaseName(indexDef.Name);

                if (indexDef.IsUnique)
                {
                    indexBuilder.IsUnique();
                }

                if (!string.IsNullOrWhiteSpace(indexDef.Filter))
                {
                    indexBuilder.HasFilter(indexDef.Filter);
                }

                var descendingFields = indexDef.Fields
                    .Where(f => f.SortOrder == IndexSortOrder.Descending)
                    .Select(f => f.FieldName)
                    .ToArray();

                if (descendingFields.Length > 0)
                {
                    indexBuilder.IsDescending(descendingFields.Select(_ => true).ToArray());
                }
            }
        }
    }
}
