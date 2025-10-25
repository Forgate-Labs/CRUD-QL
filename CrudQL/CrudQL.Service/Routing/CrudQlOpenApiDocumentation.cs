using System.Collections.Generic;
using CrudQL.Service.Authorization;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.OpenApi.Any;
using Microsoft.OpenApi.Models;

namespace CrudQL.Service.Routing;

internal static class CrudQlOpenApiDocumentation
{
    private const string CrudQlTag = "CrudQL";

    public static RouteHandlerBuilder WithCrudQlDocumentation(this RouteHandlerBuilder builder, CrudAction action)
    {
        return action switch
        {
            CrudAction.Create => DescribeCreate(builder),
            CrudAction.Read => DescribeRead(builder),
            CrudAction.Update => DescribeUpdate(builder),
            CrudAction.Delete => DescribeDelete(builder),
            _ => builder
        };
    }

    private static RouteHandlerBuilder DescribeCreate(RouteHandlerBuilder builder)
    {
        var responses = new[]
        {
            new CrudQlOperationResponseDocumentation(StatusCodes.Status201Created, new[] { "data" })
        };

        ApplyMetadata(builder, "POST", new[] { "entity", "input", "returning" }, responses,
            "Create entities through CrudQL",
            "Send the registered entity name, the JSON payload under 'input', and optionally the projection fields under 'returning'.");

        builder.WithOpenApi(operation =>
        {
            operation.RequestBody = CreateRequestBody(true, CreateCreateRequestSchema(), CreateCreateRequestExample());
            operation.Responses[StatusCodes.Status201Created.ToString()] = CreateResponse("Created", CreateDataEnvelopeSchema(isArray: false), CreateCreateResponseExample());
            AppendCommonErrors(operation);
            return operation;
        });

        return builder;
    }

    private static RouteHandlerBuilder DescribeRead(RouteHandlerBuilder builder)
    {
        var responses = new[]
        {
            new CrudQlOperationResponseDocumentation(StatusCodes.Status200OK, new[] { "data" })
        };

        ApplyMetadata(builder, "GET", new[] { "entity", "filter", "select", "returning" }, responses,
            "Query entities through CrudQL",
            "Provide the entity name, optional filter criteria, and projection fields using either the body or query string.");

        builder.WithOpenApi(operation =>
        {
            operation.Parameters = new List<OpenApiParameter>
            {
                CreateEntityQueryParameter(),
                new()
                {
                    Name = "select",
                    In = ParameterLocation.Query,
                    Description = "Comma separated list of fields when not sending the body.",
                    Schema = new OpenApiSchema { Type = "string" },
                    Required = false
                }
            };

            operation.RequestBody = CreateRequestBody(false, CreateReadRequestSchema(), CreateReadRequestExample());
            operation.Responses[StatusCodes.Status200OK.ToString()] = CreateResponse("OK", CreateDataEnvelopeSchema(isArray: true), CreateReadResponseExample());
            AppendCommonErrors(operation);
            return operation;
        });

        return builder;
    }

    private static RouteHandlerBuilder DescribeUpdate(RouteHandlerBuilder builder)
    {
        var responses = new[]
        {
            new CrudQlOperationResponseDocumentation(StatusCodes.Status200OK, new[] { "affectedRows" })
        };

        ApplyMetadata(builder, "PUT", new[] { "entity", "condition", "update" }, responses,
            "Update entities through CrudQL",
            "Specify the entity name, a condition describing the records to update, and the JSON payload under 'update'.");

        builder.WithOpenApi(operation =>
        {
            operation.RequestBody = CreateRequestBody(true, CreateUpdateRequestSchema(), CreateUpdateRequestExample());
            operation.Responses[StatusCodes.Status200OK.ToString()] = CreateResponse("OK", CreateAffectedRowsSchema(), CreateUpdateResponseExample());
            AppendCommonErrors(operation);
            return operation;
        });

        return builder;
    }

    private static RouteHandlerBuilder DescribeDelete(RouteHandlerBuilder builder)
    {
        var responses = new[]
        {
            new CrudQlOperationResponseDocumentation(StatusCodes.Status204NoContent, null)
        };

        ApplyMetadata(builder, "DELETE", new[] { "entity", "key" }, responses,
            "Delete entities through CrudQL",
            "Pass the entity name and the identifier under 'key', or use the query string parameters 'entity' and 'id'.");

        builder.WithOpenApi(operation =>
        {
            operation.Parameters = new List<OpenApiParameter>
            {
                CreateEntityQueryParameter(),
                new()
                {
                    Name = "id",
                    In = ParameterLocation.Query,
                    Description = "Identifier to delete when not sending the body.",
                    Schema = new OpenApiSchema { Type = "integer", Format = "int32" },
                    Required = false
                }
            };

            operation.RequestBody = CreateRequestBody(false, CreateDeleteRequestSchema(), CreateDeleteRequestExample());
            operation.Responses[StatusCodes.Status204NoContent.ToString()] = new OpenApiResponse { Description = "No Content" };
            AppendCommonErrors(operation);
            return operation;
        });

        return builder;
    }

    private static void ApplyMetadata(
        RouteHandlerBuilder builder,
        string method,
        IReadOnlyCollection<string> requestFields,
        IReadOnlyCollection<CrudQlOperationResponseDocumentation> responses,
        string summary,
        string description)
    {
        builder.WithTags(CrudQlTag);
        builder.WithSummary(summary);
        builder.WithDescription(description);
        builder.WithMetadata(new CrudQlOperationDocumentation(method, requestFields, responses));
    }

    private static OpenApiRequestBody CreateRequestBody(bool required, OpenApiSchema schema, IOpenApiAny example)
    {
        return new OpenApiRequestBody
        {
            Required = required,
            Content =
            {
                ["application/json"] = new OpenApiMediaType
                {
                    Schema = schema,
                    Example = example
                }
            }
        };
    }

    private static OpenApiResponse CreateResponse(string description, OpenApiSchema schema, IOpenApiAny example)
    {
        return new OpenApiResponse
        {
            Description = description,
            Content =
            {
                ["application/json"] = new OpenApiMediaType
                {
                    Schema = schema,
                    Example = example
                }
            }
        };
    }

    private static void AppendCommonErrors(OpenApiOperation operation)
    {
        if (!operation.Responses.ContainsKey(StatusCodes.Status400BadRequest.ToString()))
        {
            operation.Responses[StatusCodes.Status400BadRequest.ToString()] = new OpenApiResponse { Description = "Bad Request" };
        }

        if (!operation.Responses.ContainsKey(StatusCodes.Status401Unauthorized.ToString()))
        {
            operation.Responses[StatusCodes.Status401Unauthorized.ToString()] = new OpenApiResponse { Description = "Unauthorized" };
        }

        if (!operation.Responses.ContainsKey(StatusCodes.Status404NotFound.ToString()))
        {
            operation.Responses[StatusCodes.Status404NotFound.ToString()] = new OpenApiResponse { Description = "Not Found" };
        }
    }

    private static OpenApiSchema CreateCreateRequestSchema()
    {
        return new OpenApiSchema
        {
            Type = "object",
            Required = new HashSet<string> { "entity", "input" },
            Properties = new Dictionary<string, OpenApiSchema>
            {
                ["entity"] = CreateEntitySchema(),
                ["input"] = new OpenApiSchema { Type = "object", Description = "JSON payload that maps to the entity fields." },
                ["returning"] = CreateStringArraySchema("Fields that should be projected in the response.")
            }
        };
    }

    private static OpenApiSchema CreateReadRequestSchema()
    {
        return new OpenApiSchema
        {
            Type = "object",
            Required = new HashSet<string> { "entity" },
            Properties = new Dictionary<string, OpenApiSchema>
            {
                ["entity"] = CreateEntitySchema(),
                ["filter"] = new OpenApiSchema { Type = "object", Description = "Optional filter tree." },
                ["select"] = CreateStringArraySchema("Fields to select when executing the query."),
                ["returning"] = CreateStringArraySchema("Projection template for nested projections.")
            }
        };
    }

    private static OpenApiSchema CreateUpdateRequestSchema()
    {
        return new OpenApiSchema
        {
            Type = "object",
            Required = new HashSet<string> { "entity", "condition", "update" },
            Properties = new Dictionary<string, OpenApiSchema>
            {
                ["entity"] = CreateEntitySchema(),
                ["condition"] = new OpenApiSchema { Type = "object", Description = "Filter describing which records will change." },
                ["update"] = new OpenApiSchema { Type = "object", Description = "JSON payload containing the fields to update." }
            }
        };
    }

    private static OpenApiSchema CreateDeleteRequestSchema()
    {
        return new OpenApiSchema
        {
            Type = "object",
            Properties = new Dictionary<string, OpenApiSchema>
            {
                ["entity"] = CreateEntitySchema(),
                ["key"] = new OpenApiSchema
                {
                    Type = "object",
                    Description = "Identifier map used as an alternative to the 'id' query string parameter.",
                    Properties = new Dictionary<string, OpenApiSchema>
                    {
                        ["id"] = new OpenApiSchema { Type = "integer", Format = "int32", Description = "Numeric identifier." }
                    }
                }
            }
        };
    }

    private static OpenApiSchema CreateEntitySchema()
    {
        return new OpenApiSchema
        {
            Type = "string",
            Description = "Name of the registered entity."
        };
    }

    private static OpenApiSchema CreateStringArraySchema(string description)
    {
        return new OpenApiSchema
        {
            Type = "array",
            Description = description,
            Items = new OpenApiSchema { Type = "string" }
        };
    }

    private static OpenApiSchema CreateDataEnvelopeSchema(bool isArray)
    {
        return new OpenApiSchema
        {
            Type = "object",
            Properties = new Dictionary<string, OpenApiSchema>
            {
                ["data"] = isArray
                    ? new OpenApiSchema { Type = "array", Items = new OpenApiSchema { Type = "object" } }
                    : new OpenApiSchema { Type = "object" }
            }
        };
    }

    private static OpenApiSchema CreateAffectedRowsSchema()
    {
        return new OpenApiSchema
        {
            Type = "object",
            Properties = new Dictionary<string, OpenApiSchema>
            {
                ["affectedRows"] = new OpenApiSchema
                {
                    Type = "integer",
                    Format = "int32",
                    Description = "Number of rows affected by the update."
                }
            }
        };
    }

    private static IOpenApiAny CreateCreateRequestExample()
    {
        return new OpenApiObject
        {
            ["entity"] = new OpenApiString("Product"),
            ["input"] = new OpenApiObject
            {
                ["name"] = new OpenApiString("Mouse Pro"),
                ["price"] = new OpenApiDouble(129.9),
                ["currency"] = new OpenApiString("USD")
            },
            ["returning"] = new OpenApiArray
            {
                new OpenApiString("id"),
                new OpenApiString("name"),
                new OpenApiString("price")
            }
        };
    }

    private static IOpenApiAny CreateReadRequestExample()
    {
        return new OpenApiObject
        {
            ["entity"] = new OpenApiString("Product"),
            ["filter"] = new OpenApiObject
            {
                ["field"] = new OpenApiString("price"),
                ["op"] = new OpenApiString("gte"),
                ["value"] = new OpenApiDouble(100)
            },
            ["select"] = new OpenApiArray
            {
                new OpenApiString("id"),
                new OpenApiString("name"),
                new OpenApiString("price")
            }
        };
    }

    private static IOpenApiAny CreateUpdateRequestExample()
    {
        return new OpenApiObject
        {
            ["entity"] = new OpenApiString("Product"),
            ["condition"] = new OpenApiObject
            {
                ["field"] = new OpenApiString("id"),
                ["op"] = new OpenApiString("eq"),
                ["value"] = new OpenApiInteger(42)
            },
            ["update"] = new OpenApiObject
            {
                ["price"] = new OpenApiDouble(149.9),
                ["description"] = new OpenApiString("Updated description")
            }
        };
    }

    private static IOpenApiAny CreateDeleteRequestExample()
    {
        return new OpenApiObject
        {
            ["entity"] = new OpenApiString("Product"),
            ["key"] = new OpenApiObject
            {
                ["id"] = new OpenApiInteger(42)
            }
        };
    }

    private static IOpenApiAny CreateCreateResponseExample()
    {
        return new OpenApiObject
        {
            ["data"] = new OpenApiObject
            {
                ["id"] = new OpenApiInteger(101),
                ["name"] = new OpenApiString("Mouse Pro"),
                ["price"] = new OpenApiDouble(129.9),
                ["currency"] = new OpenApiString("USD")
            }
        };
    }

    private static IOpenApiAny CreateReadResponseExample()
    {
        return new OpenApiObject
        {
            ["data"] = new OpenApiArray
            {
                new OpenApiObject
                {
                    ["id"] = new OpenApiInteger(101),
                    ["name"] = new OpenApiString("Mouse Pro"),
                    ["price"] = new OpenApiDouble(129.9)
                }
            }
        };
    }

    private static IOpenApiAny CreateUpdateResponseExample()
    {
        return new OpenApiObject
        {
            ["affectedRows"] = new OpenApiInteger(1)
        };
    }

    private static OpenApiParameter CreateEntityQueryParameter()
    {
        return new OpenApiParameter
        {
            Name = "entity",
            In = ParameterLocation.Query,
            Description = "Entity name when provided through the query string instead of the body.",
            Schema = CreateEntitySchema(),
            Required = false
        };
    }
}
