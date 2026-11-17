using System.Security.Claims;
using EduFlow.Application.Attendance;
using EduFlow.Domain.Constants;
using EduFlow.Domain.Enums;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.SignalR;

namespace EduFlow.Api.Hubs;

[Authorize]
public sealed class AttendanceHub : Hub
{
    public const string EventName = "boardUpdated";

    public static string GroupName(Guid tenantId) => $"attendance:{tenantId:N}";

    public override async Task OnConnectedAsync()
    {
        var tenantValue = Context.User?.FindFirstValue("tenant_id");
        if (!Guid.TryParse(tenantValue, out var tenantId) || tenantId == Guid.Empty || !CanWatch(Context.User))
        {
            Context.Abort();
            return;
        }

        await Groups.AddToGroupAsync(Context.ConnectionId, GroupName(tenantId));
        await base.OnConnectedAsync();
    }

    private static bool CanWatch(ClaimsPrincipal? user)
    {
        var role = user?.FindFirstValue(ClaimTypes.Role) ?? user?.FindFirstValue("role");
        if (string.Equals(role, nameof(RoleType.InstituteAdmin), StringComparison.OrdinalIgnoreCase)
            || string.Equals(role, nameof(RoleType.SuperAdmin), StringComparison.OrdinalIgnoreCase))
        {
            return true;
        }

        var permissions = user?.FindAll("permission").Select(claim => claim.Value) ?? [];
        return permissions.Contains(Permissions.AttendanceMark) || permissions.Contains(Permissions.ReportsView);
    }
}

public sealed class SignalRAttendanceBoard(IHubContext<AttendanceHub> hub) : IAttendanceBoard
{
    public Task PublishAsync(Guid tenantId, AttendanceBoardDto board, CancellationToken cancellationToken = default) =>
        hub.Clients.Group(AttendanceHub.GroupName(tenantId)).SendAsync(AttendanceHub.EventName, board, cancellationToken);
}
