using Microsoft.AspNetCore.Http;

namespace CrudQL.Service.Lifecycle;

public delegate void EntityLifecycleHook(object entity, HttpContext httpContext);
