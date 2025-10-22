using System;
using System.Collections.Generic;
using System.Linq;
using System.Security.Claims;

namespace CrudQL.Service.Authorization;

public abstract class CrudPolicy<TEntity> : ICrudPolicy<TEntity>
{
    private readonly Dictionary<CrudAction, HashSet<string>> roleMap = new();

    protected CrudActionConfigurator Allow(CrudAction action)
    {
        if (!roleMap.TryGetValue(action, out var roles))
        {
            roles = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            roleMap[action] = roles;
        }

        return new CrudActionConfigurator(roles);
    }

    bool ICrudPolicy.IsAuthorized(ClaimsPrincipal user, CrudAction action)
    {
        ArgumentNullException.ThrowIfNull(user);

        if (!roleMap.TryGetValue(action, out var roles) || roles.Count == 0)
        {
            return false;
        }

        return roles.Any(user.IsInRole);
    }

    protected sealed class CrudActionConfigurator
    {
        private readonly HashSet<string> roles;

        internal CrudActionConfigurator(HashSet<string> roles)
        {
            this.roles = roles;
        }

        public CrudActionConfigurator ForRoles(params string[] values)
        {
            if (values == null || values.Length == 0)
            {
                throw new ArgumentException("At least one role must be provided.", nameof(values));
            }

            foreach (var value in values)
            {
                if (string.IsNullOrWhiteSpace(value))
                {
                    throw new ArgumentException("Role names cannot be empty.", nameof(values));
                }

                roles.Add(value.Trim());
            }

            return this;
        }
    }
}
