using System.Collections.Generic;
using System.IO;
using System.Text.Json;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Routing;

namespace CrudQL.Service.Routing;

public static class CrudQlEndpointRouteBuilderExtensions
{
    private const string CrudRoute = "/crud";
    private static readonly object OkPayload = new { message = "ok" };
    private static readonly IReadOnlyDictionary<string, string[]> ExpectedKeysByMethod =
        new Dictionary<string, string[]>(StringComparer.OrdinalIgnoreCase)
        {
            [HttpMethods.Get] = new[] { "entity" },
            [HttpMethods.Post] = new[] { "entity", "input" }
        };

    public static IEndpointRouteBuilder MapCrudQl(this IEndpointRouteBuilder endpoints)
    {
        ArgumentNullException.ThrowIfNull(endpoints);

        endpoints.MapPost(CrudRoute, HandleCrudRequest);
        endpoints.MapPut(CrudRoute, HandleCrudRequest);
        endpoints.MapGet(CrudRoute, HandleCrudRequest);
        endpoints.MapDelete(CrudRoute, HandleCrudRequest);

        return endpoints;
    }

    private static async Task HandleCrudRequest(HttpContext context)
    {
        if (context.Request == null)
        {
            await Results.Ok(OkPayload).ExecuteAsync(context);
            return;
        }

        context.Request.EnableBuffering();
        context.Request.Body.Position = 0;

        using var reader = new StreamReader(context.Request.Body, leaveOpen: true);
        var content = await reader.ReadToEndAsync();
        context.Request.Body.Position = 0;

        if (!string.IsNullOrWhiteSpace(content))
        {
            try
            {
                using var document = JsonDocument.Parse(content);

                if (ExpectedKeysByMethod.TryGetValue(context.Request.Method, out var expectedKeys))
                {
                    var root = document.RootElement;
                    if (root.ValueKind != JsonValueKind.Object)
                    {
                        await Results.BadRequest(new { message = "Payload must be a JSON object" }).ExecuteAsync(context);
                        return;
                    }

                    foreach (var key in expectedKeys)
                    {
                        if (!root.TryGetProperty(key, out _))
                        {
                            await Results.BadRequest(new { message = $"Missing '{key}' property" }).ExecuteAsync(context);
                            return;
                        }
                    }
                }
            }
            catch (JsonException)
            {
                await Results.BadRequest(new { message = "Invalid JSON payload" }).ExecuteAsync(context);
                return;
            }
        }

        await Results.Ok(OkPayload).ExecuteAsync(context);
    }
}
