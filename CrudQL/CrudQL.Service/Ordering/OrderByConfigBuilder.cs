using System;
using System.Collections.Generic;
using System.Linq;
using System.Linq.Expressions;

namespace CrudQL.Service.Ordering;

public sealed class OrderByConfigBuilder<TEntity>
{
    private readonly HashSet<string> allowedFields = new(StringComparer.OrdinalIgnoreCase);
    private string? defaultField;
    private OrderDirection defaultDirection = OrderDirection.Ascending;

    public OrderByConfigBuilder<TEntity> AllowOrderBy(Expression<Func<TEntity, object>> fieldExpression)
    {
        var fieldName = GetPropertyName(fieldExpression);
        allowedFields.Add(fieldName);
        return this;
    }

    public OrderByConfigBuilder<TEntity> AllowOrderBy(params Expression<Func<TEntity, object>>[] fieldExpressions)
    {
        foreach (var expr in fieldExpressions)
        {
            AllowOrderBy(expr);
        }
        return this;
    }

    public OrderByConfigBuilder<TEntity> WithDefault(
        Expression<Func<TEntity, object>> fieldExpression,
        OrderDirection direction = OrderDirection.Ascending)
    {
        defaultField = GetPropertyName(fieldExpression);
        defaultDirection = direction;
        return this;
    }

    public OrderByConfig Build()
    {
        return new OrderByConfig(
            allowedFields.Count > 0 ? allowedFields.ToArray() : null,
            defaultField,
            defaultDirection);
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
