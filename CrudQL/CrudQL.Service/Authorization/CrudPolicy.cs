using System;
using System.Collections.Generic;
using System.Linq;
using System.Linq.Expressions;
using System.Reflection;
using System.Security.Claims;
using System.Text.Json;

namespace CrudQL.Service.Authorization;

public abstract class CrudPolicy<TEntity> : ICrudPolicy<TEntity>, ICrudProjectionPolicy, ISoftDeletePolicy
{
    private static readonly JsonNamingPolicy NamingPolicy = JsonNamingPolicy.CamelCase;
    private readonly Dictionary<CrudAction, ActionRule> rules = new();
    private string suppressionValue = "****";

    protected CrudPolicy<TEntity> UseSupression(string value)
    {
        if (string.IsNullOrWhiteSpace(value))
        {
            throw new ArgumentException("Suppression value cannot be empty.", nameof(value));
        }

        suppressionValue = value;
        return this;
    }

    protected CrudActionConfigurator Allow(CrudAction action)
    {
        if (!rules.TryGetValue(action, out var rule))
        {
            rule = new ActionRule();
            rules[action] = rule;
        }

        return new CrudActionConfigurator(this, action, rule);
    }

    protected CrudPolicy<TEntity> AllowAnonymous(CrudAction action)
    {
        if (!rules.TryGetValue(action, out var rule))
        {
            rule = new ActionRule();
            rules[action] = rule;
        }

        rule.IsAnonymous = true;
        return this;
    }

    bool ICrudPolicy.IsAuthorized(ClaimsPrincipal user, CrudAction action)
    {
        if (!rules.TryGetValue(action, out var rule))
        {
            return false;
        }

        if (rule.IsAnonymous)
        {
            return true;
        }

        ArgumentNullException.ThrowIfNull(user);

        if (rule.AllRoles.Count == 0)
        {
            return false;
        }

        return rule.AllRoles.Any(user.IsInRole);
    }

    CrudProjectionRule? ICrudProjectionPolicy.ResolveProjection(ClaimsPrincipal user, CrudAction action)
    {
        ArgumentNullException.ThrowIfNull(user);

        if (!rules.TryGetValue(action, out var rule))
        {
            return null;
        }

        var matches = rule.Assignments.Where(assignment => assignment.Roles.Any(user.IsInRole)).ToList();
        if (matches.Count == 0)
        {
            return null;
        }

        if (matches.Any(match => match.Fields == null || match.Fields.Count == 0))
        {
            return null;
        }

        var restricted = matches.FirstOrDefault(match => match.Fields is { Count: > 0 });
        if (restricted == null)
        {
            return null;
        }

        return new CrudProjectionRule(restricted.Fields!.ToArray(), suppressionValue);
    }

    CrudSoftDeleteRule? ISoftDeletePolicy.ResolveSoftDelete(CrudAction action)
    {
        if (!rules.TryGetValue(action, out var rule))
        {
            return null;
        }

        return rule.SoftDeleteRule;
    }

    private string ResolveFieldName(Expression<Func<TEntity, object>> selector)
    {
        ArgumentNullException.ThrowIfNull(selector);

        var member = selector.Body switch
        {
            MemberExpression expression => expression,
            UnaryExpression { Operand: MemberExpression inner } => inner,
            _ => null
        };

        if (member == null || member.Member is not PropertyInfo property || !property.CanRead)
        {
            throw new ArgumentException("Field selectors must target readable properties.", nameof(selector));
        }

        return NamingPolicy.ConvertName(property.Name);
    }

    private PropertyInfo ResolveFlagProperty(Expression<Func<TEntity, bool>> selector)
    {
        ArgumentNullException.ThrowIfNull(selector);

        var property = ResolveProperty(selector, nameof(selector));
        if (property.PropertyType != typeof(bool))
        {
            throw new ArgumentException("Soft delete flag must target a boolean property.", nameof(selector));
        }

        return property;
    }

    private PropertyInfo ResolveTimestampProperty(Expression<Func<TEntity, DateTime?>> selector)
    {
        ArgumentNullException.ThrowIfNull(selector);

        var property = ResolveProperty(selector, nameof(selector));
        if (property.PropertyType != typeof(DateTime) && property.PropertyType != typeof(DateTime?))
        {
            throw new ArgumentException("Soft delete timestamp must target a DateTime property.", nameof(selector));
        }

        return property;
    }

    private PropertyInfo ResolveProperty<TProperty>(Expression<Func<TEntity, TProperty>> selector, string parameterName)
    {
        var member = selector.Body switch
        {
            MemberExpression expression => expression,
            UnaryExpression { Operand: MemberExpression inner } => inner,
            _ => null
        };

        if (member == null || member.Member is not PropertyInfo property || !property.CanRead || !property.CanWrite)
        {
            throw new ArgumentException("Soft delete selectors must target readable and writable properties.", parameterName);
        }

        return property;
    }

    internal sealed class ActionRule
    {
        public HashSet<string> AllRoles { get; } = new(StringComparer.OrdinalIgnoreCase);

        public List<RoleAssignment> Assignments { get; } = new();

        public CrudSoftDeleteRule? SoftDeleteRule { get; set; }

        public bool IsAnonymous { get; set; }
    }

    internal sealed class RoleAssignment
    {
        public RoleAssignment()
        {
            Roles = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        }

        public HashSet<string> Roles { get; }

        public HashSet<string>? Fields { get; private set; }

        public void AssignFields(IEnumerable<string> names)
        {
            Fields ??= new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            foreach (var name in names)
            {
                Fields.Add(name);
            }
        }
    }

    protected sealed class CrudActionConfigurator
    {
        private readonly CrudPolicy<TEntity> policy;
        private readonly CrudAction action;
        private readonly ActionRule rule;
        private RoleAssignment? currentAssignment;

        internal CrudActionConfigurator(CrudPolicy<TEntity> policy, CrudAction action, ActionRule rule)
        {
            this.policy = policy;
            this.action = action;
            this.rule = rule;
        }

        public CrudActionConfigurator ForRoles(params string[] values)
        {
            if (values == null || values.Length == 0)
            {
                throw new ArgumentException("At least one role must be provided.", nameof(values));
            }

            currentAssignment ??= AddAssignment();
            foreach (var value in values)
            {
                if (string.IsNullOrWhiteSpace(value))
                {
                    throw new ArgumentException("Role names cannot be empty.", nameof(values));
                }

                var role = value.Trim();
                currentAssignment.Roles.Add(role);
                rule.AllRoles.Add(role);
            }

            return this;
        }

        public CrudActionConfigurator ForFields(params Expression<Func<TEntity, object>>[] selectors)
        {
            if (currentAssignment == null)
            {
                throw new InvalidOperationException("ForFields must be chained after ForRoles.");
            }

            if (selectors == null || selectors.Length == 0)
            {
                throw new ArgumentException("At least one field must be provided.", nameof(selectors));
            }

            if (action is not CrudAction.Read and not CrudAction.Create)
            {
                throw new InvalidOperationException("Field projections are only supported for read and create actions.");
            }

            var fields = selectors.Select(policy.ResolveFieldName);
            currentAssignment.AssignFields(fields);
            currentAssignment = null;
            return this;
        }

        public CrudActionConfigurator DeleteWithColumn(Expression<Func<TEntity, bool>> flagSelector, Expression<Func<TEntity, DateTime?>>? timestampSelector = null, bool useUtc = true)
        {
            if (action != CrudAction.Delete)
            {
                throw new InvalidOperationException("Soft delete configuration is only supported for delete actions.");
            }

            if (rule.SoftDeleteRule != null)
            {
                throw new InvalidOperationException("Soft delete has already been configured for this action.");
            }

            var flagProperty = policy.ResolveFlagProperty(flagSelector);
            PropertyInfo? timestampProperty = null;
            if (timestampSelector != null)
            {
                timestampProperty = policy.ResolveTimestampProperty(timestampSelector);
            }

            rule.SoftDeleteRule = new CrudSoftDeleteRule(flagProperty, timestampProperty, useUtc);
            return this;
        }

        private RoleAssignment AddAssignment()
        {
            var assignment = new RoleAssignment();
            rule.Assignments.Add(assignment);
            return assignment;
        }
    }
}
