using System.Security.Claims;
using EduFlow.Domain.Enums;
using Microsoft.AspNetCore.Authorization;

namespace EduFlow.Api.Authorization;

public sealed class PermissionRequirement(string permission) : IAuthorizationRequirement
{
    public string Permission { get; } = permission;
}

public sealed class PermissionAuthorizationHandler : AuthorizationHandler<PermissionRequirement>
{
    protected override Task HandleRequirementAsync(
        AuthorizationHandlerContext context,
        PermissionRequirement requirement)
    {
        var roleClaim = context.User.FindFirstValue(ClaimTypes.Role)
            ?? context.User.FindFirstValue("role");

        if (string.Equals(roleClaim, nameof(RoleType.SuperAdmin), StringComparison.OrdinalIgnoreCase)
            || string.Equals(roleClaim, nameof(RoleType.InstituteAdmin), StringComparison.OrdinalIgnoreCase))
        {
            context.Succeed(requirement);
            return Task.CompletedTask;
        }

        var hasPermission = context.User.FindAll("permission")
            .Any(c => string.Equals(c.Value, requirement.Permission, StringComparison.OrdinalIgnoreCase));

        if (hasPermission)
        {
            context.Succeed(requirement);
        }

        return Task.CompletedTask;
    }
}

public static class AuthorizationPolicies
{
    public const string SuperAdminOnly = "Role:SuperAdmin";
    public const string InstituteAdminOnly = "Role:InstituteAdmin";
    public const string ParentOnly = "Role:Parent";
    public const string PermissionPrefix = "Permission:";

    public static string ForPermission(string permission) => $"{PermissionPrefix}{permission}";

    public static TBuilder RequirePermission<TBuilder>(this TBuilder builder, string permission)
        where TBuilder : IEndpointConventionBuilder
    {
        builder.RequireAuthorization(ForPermission(permission));
        return builder;
    }

    public static TBuilder RequireSuperAdmin<TBuilder>(this TBuilder builder)
        where TBuilder : IEndpointConventionBuilder
    {
        builder.RequireAuthorization(SuperAdminOnly);
        return builder;
    }
}
