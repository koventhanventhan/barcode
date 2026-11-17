using EduFlow.Application.Common.Interfaces;
using EduFlow.Domain.Enums;

namespace EduFlow.Api.Filters;

[AttributeUsage(AttributeTargets.Method | AttributeTargets.Class | AttributeTargets.Delegate, AllowMultiple = true)]
public sealed class RequireFeatureAttribute(FeatureKey feature) : Attribute
{
    public FeatureKey Feature { get; } = feature;
}

public sealed class RequireFeatureEndpointFilter : IEndpointFilter
{
    public async ValueTask<object?> InvokeAsync(
        EndpointFilterInvocationContext context,
        EndpointFilterDelegate next)
    {
        var httpContext = context.HttpContext;
        var endpoint = httpContext.GetEndpoint();
        var attributes = endpoint?.Metadata.GetOrderedMetadata<RequireFeatureAttribute>();

        if (attributes is null || attributes.Count == 0)
        {
            return await next(context);
        }

        var tenantContext = httpContext.RequestServices.GetRequiredService<ITenantContext>();
        if (!tenantContext.TenantId.HasValue || tenantContext.TenantId.Value == Guid.Empty)
        {
            return Results.Json(
                new { code = "TENANT_REQUIRED", message = "Tenant context is required for this feature." },
                statusCode: StatusCodes.Status400BadRequest);
        }

        var featureService = httpContext.RequestServices.GetRequiredService<IFeatureService>();
        var tenantId = tenantContext.TenantId.Value;

        foreach (var attr in attributes)
        {
            var evaluation = await featureService.EvaluateAsync(tenantId, attr.Feature, httpContext.RequestAborted);
            if (!evaluation.IsInPlan)
            {
                return Results.Json(
                    new { code = "FEATURE_NOT_IN_PLAN", feature = attr.Feature.ToString() },
                    statusCode: StatusCodes.Status403Forbidden);
            }

            if (!evaluation.IsEnabled)
            {
                return Results.Json(
                    new { code = "FEATURE_DISABLED", feature = attr.Feature.ToString() },
                    statusCode: StatusCodes.Status403Forbidden);
            }
        }

        return await next(context);
    }
}

public static class FeatureEndpointExtensions
{
    public static RouteHandlerBuilder RequireFeature(this RouteHandlerBuilder builder, FeatureKey feature)
    {
        builder.WithMetadata(new RequireFeatureAttribute(feature));
        builder.AddEndpointFilter<RequireFeatureEndpointFilter>();
        return builder;
    }

    public static RouteGroupBuilder RequireFeature(this RouteGroupBuilder builder, FeatureKey feature)
    {
        builder.WithMetadata(new RequireFeatureAttribute(feature));
        builder.AddEndpointFilter<RequireFeatureEndpointFilter>();
        return builder;
    }
}
