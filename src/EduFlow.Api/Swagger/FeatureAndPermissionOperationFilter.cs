using EduFlow.Api.Filters;
using Microsoft.AspNetCore.Authorization;
using Microsoft.OpenApi.Models;
using Swashbuckle.AspNetCore.SwaggerGen;

namespace EduFlow.Api.Swagger;

public sealed class FeatureAndPermissionOperationFilter : IOperationFilter
{
    public void Apply(OpenApiOperation operation, OperationFilterContext context)
    {
        var metadata = context.ApiDescription.ActionDescriptor.EndpointMetadata;

        var features = metadata.OfType<RequireFeatureAttribute>().Select(f => f.Feature.ToString()).Distinct().ToList();
        if (features.Count > 0)
        {
            var featureNote = $"Requires Feature(s): {string.Join(", ", features)}";
            operation.Description = string.IsNullOrWhiteSpace(operation.Description)
                ? featureNote
                : $"{operation.Description} | {featureNote}";
        }

        var authorizeData = metadata.OfType<IAuthorizeData>().ToList();
        if (authorizeData.Count > 0)
        {
            var policies = authorizeData
                .Where(a => !string.IsNullOrWhiteSpace(a.Policy))
                .Select(a => a.Policy!)
                .Distinct()
                .ToList();

            if (policies.Count > 0)
            {
                var policyNote = $"Requires Policy: {string.Join(", ", policies)}";
                operation.Description = string.IsNullOrWhiteSpace(operation.Description)
                    ? policyNote
                    : $"{operation.Description} | {policyNote}";
            }

            operation.Security ??= new List<OpenApiSecurityRequirement>();
            operation.Security.Add(new OpenApiSecurityRequirement
            {
                [new OpenApiSecurityScheme
                {
                    Reference = new OpenApiReference
                    {
                        Type = ReferenceType.SecurityScheme,
                        Id = "Bearer"
                    }
                }] = Array.Empty<string>()
            });
        }
    }
}
