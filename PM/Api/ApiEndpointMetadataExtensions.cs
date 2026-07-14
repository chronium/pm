using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.OpenApi;

namespace PM.Api;

public static class ApiEndpointMetadataExtensions
{
    public static RouteHandlerBuilder WithRevisionedReadMetadata(this RouteHandlerBuilder builder)
    {
        builder.Produces(StatusCodes.Status304NotModified);
        return builder.AddOpenApiOperationTransformer((operation, _, _) =>
        {
            operation.Parameters ??= [];
            operation.Parameters.Add(HeaderParameter(
                "If-None-Match",
                "Return 304 when this resource revision still matches.",
                required: false));
            AddETagHeader(operation, StatusCodes.Status200OK);
            AddETagHeader(operation, StatusCodes.Status304NotModified);
            return Task.CompletedTask;
        });
    }

    public static RouteHandlerBuilder WithRevisionedMutationMetadata(this RouteHandlerBuilder builder)
    {
        builder
            .Produces<ApiProblemDetails>(StatusCodes.Status412PreconditionFailed, "application/problem+json")
            .Produces<ApiProblemDetails>(StatusCodes.Status428PreconditionRequired, "application/problem+json");
        return builder.AddOpenApiOperationTransformer((operation, _, _) =>
        {
            operation.Parameters ??= [];
            operation.Parameters.Add(HeaderParameter(
                "If-Match",
                "Required current strong resource ETag. Use * to match any current representation.",
                required: true));
            AddETagHeader(operation, StatusCodes.Status200OK);
            return Task.CompletedTask;
        });
    }

    private static OpenApiParameter HeaderParameter(string name, string description, bool required) => new()
    {
        Name = name,
        In = ParameterLocation.Header,
        Description = description,
        Required = required,
        Schema = new OpenApiSchema { Type = JsonSchemaType.String },
    };

    private static void AddETagHeader(OpenApiOperation operation, int statusCode)
    {
        if (operation.Responses == null ||
            !operation.Responses.TryGetValue(statusCode.ToString(), out var response))
            return;

        if (response is not OpenApiResponse mutableResponse) return;
        mutableResponse.Headers ??= new Dictionary<string, IOpenApiHeader>();
        mutableResponse.Headers["ETag"] = new OpenApiHeader
        {
            Description = "Strong ETag for the returned resource revision.",
            Schema = new OpenApiSchema { Type = JsonSchemaType.String },
        };
    }
}
