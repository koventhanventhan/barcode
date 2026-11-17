using EduFlow.Application.Common.Interfaces;
using EduFlow.Domain.Enums;
using Microsoft.EntityFrameworkCore;

namespace EduFlow.Api.Middleware;

public sealed class BranchScopeMiddleware(RequestDelegate next)
{
    public const string HeaderName = "X-Branch";

    public async Task InvokeAsync(
        HttpContext context,
        ITenantContext tenantContext,
        IBranchScope branchScope,
        IAppDbContext db,
        IFeatureService features)
    {
        if (tenantContext.IsSystemScope || tenantContext.TenantId is null || tenantContext.UserId is null)
        {
            await next(context);
            return;
        }

        var mainBranchId = await db.Branches.AsNoTracking()
            .OrderByDescending(branch => branch.IsMain)
            .Select(branch => branch.Id)
            .FirstOrDefaultAsync(context.RequestAborted);

        if (mainBranchId == Guid.Empty)
        {
            branchScope.Apply(true, null, Guid.Empty, []);
            await next(context);
            return;
        }

        var multiBranch = await features.IsEnabledAsync(tenantContext.TenantId.Value, FeatureKey.MultiBranch, context.RequestAborted);
        var role = tenantContext.Role;
        var consolidated = multiBranch && role is RoleType.InstituteAdmin or RoleType.SuperAdmin or RoleType.Parent;

        if (!multiBranch || role == RoleType.Parent)
        {
            Guid? selected = multiBranch && role == RoleType.Parent ? null : mainBranchId;
            var unrestricted = role == RoleType.Parent && multiBranch;
            branchScope.Apply(unrestricted, selected, mainBranchId, unrestricted ? [] : [mainBranchId]);
            await next(context);
            return;
        }

        if (consolidated)
        {
            var selected = ReadBranchHeader(context);
            if (selected == Guid.Empty)
            {
                await WriteAsync(context, StatusCodes.Status400BadRequest, "INVALID_BRANCH", "X-Branch must be a branch id or 'all'.");
                return;
            }

            if (selected.HasValue && !await db.Branches.AnyAsync(branch => branch.Id == selected.Value, context.RequestAborted))
            {
                await WriteAsync(context, StatusCodes.Status403Forbidden, "BRANCH_FORBIDDEN", "That branch is outside your access.");
                return;
            }

            branchScope.Apply(true, selected, mainBranchId, []);
            await next(context);
            return;
        }

        var allowed = await db.StaffBranchAssignments.AsNoTracking()
            .Where(item => item.UserId == tenantContext.UserId)
            .Select(item => item.BranchId)
            .ToListAsync(context.RequestAborted);

        if (allowed.Count == 0)
        {
            var home = await db.Users.AsNoTracking()
                .Where(user => user.Id == tenantContext.UserId)
                .Select(user => user.BranchId)
                .FirstOrDefaultAsync(context.RequestAborted);
            allowed.Add(home ?? mainBranchId);
        }

        var staffSelected = ReadBranchHeader(context);
        if (staffSelected == Guid.Empty)
        {
            await WriteAsync(context, StatusCodes.Status400BadRequest, "INVALID_BRANCH", "X-Branch must be a branch id or 'all'.");
            return;
        }

        staffSelected ??= allowed[0];
        if (!allowed.Contains(staffSelected.Value))
        {
            await WriteAsync(context, StatusCodes.Status403Forbidden, "BRANCH_FORBIDDEN", "That branch is outside your access.");
            return;
        }

        branchScope.Apply(false, staffSelected, mainBranchId, allowed);
        await next(context);
    }

    private static Guid? ReadBranchHeader(HttpContext context)
    {
        if (!context.Request.Headers.TryGetValue(HeaderName, out var raw))
        {
            return null;
        }

        var value = raw.ToString().Trim();
        if (value.Length == 0 || value.Equals("all", StringComparison.OrdinalIgnoreCase))
        {
            return null;
        }

        return Guid.TryParse(value, out var id) ? id : Guid.Empty;
    }

    private static Task WriteAsync(HttpContext context, int status, string code, string message)
    {
        context.Response.StatusCode = status;
        return context.Response.WriteAsJsonAsync(new { code, message });
    }
}
