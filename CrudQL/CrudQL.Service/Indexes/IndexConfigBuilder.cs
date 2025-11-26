using System;
using System.Collections.Generic;
using System.Linq;
using System.Linq.Expressions;
using System.Reflection;

namespace CrudQL.Service.Indexes;

public sealed class IndexConfigBuilder<TEntity>
{
    private readonly List<IndexDefinition> indexes = new();

    public IndexConfigBuilder<TEntity> HasIndex(
        Expression<Func<TEntity, object>> fieldExpression,
        string? indexName = null,
        IndexSortOrder sortOrder = IndexSortOrder.Ascending)
    {
        var fieldName = GetPropertyName(fieldExpression);
        var name = indexName ?? $"IX_{typeof(TEntity).Name}_{fieldName}";

        indexes.Add(new IndexDefinition(
            name,
            new[] { new IndexField(fieldName, sortOrder) }
        ));

        return this;
    }

    public IndexConfigBuilder<TEntity> HasIndex(
        Action<IndexBuilder<TEntity>> configure,
        string? indexName = null)
    {
        var builder = new IndexBuilder<TEntity>(indexName ?? $"IX_{typeof(TEntity).Name}_Composite");
        configure(builder);
        indexes.Add(builder.Build());
        return this;
    }

    public IndexConfig Build()
    {
        return new IndexConfig(indexes);
    }

    private static string GetPropertyName(Expression<Func<TEntity, object>> expression)
    {
        if (expression.Body is MemberExpression memberExpression)
        {
            return memberExpression.Member.Name;
        }

        if (expression.Body is UnaryExpression unaryExpression &&
            unaryExpression.Operand is MemberExpression operand)
        {
            return operand.Member.Name;
        }

        throw new ArgumentException("Expression must be a member access expression", nameof(expression));
    }
}

public sealed class IndexBuilder<TEntity>
{
    private readonly string indexName;
    private readonly List<IndexField> fields = new();
    private bool isUnique;
    private string? filter;

    internal IndexBuilder(string indexName)
    {
        this.indexName = indexName;
    }

    public IndexBuilder<TEntity> HasField(
        Expression<Func<TEntity, object>> fieldExpression,
        IndexSortOrder sortOrder = IndexSortOrder.Ascending)
    {
        var fieldName = GetPropertyName(fieldExpression);
        fields.Add(new IndexField(fieldName, sortOrder));
        return this;
    }

    public IndexBuilder<TEntity> IsUnique()
    {
        isUnique = true;
        return this;
    }

    public IndexBuilder<TEntity> HasFilter(string filterExpression)
    {
        filter = filterExpression;
        return this;
    }

    internal IndexDefinition Build()
    {
        if (fields.Count == 0)
        {
            throw new InvalidOperationException("Index must have at least one field");
        }

        return new IndexDefinition(indexName, fields, isUnique, filter);
    }

    private static string GetPropertyName(Expression<Func<TEntity, object>> expression)
    {
        if (expression.Body is MemberExpression memberExpression)
        {
            return memberExpression.Member.Name;
        }

        if (expression.Body is UnaryExpression unaryExpression &&
            unaryExpression.Operand is MemberExpression operand)
        {
            return operand.Member.Name;
        }

        throw new ArgumentException("Expression must be a member access expression", nameof(expression));
    }
}
