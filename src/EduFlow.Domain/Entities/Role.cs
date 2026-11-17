using EduFlow.Domain.Common;

namespace EduFlow.Domain.Entities;

public sealed class Role : TenantEntity
{
    public string Name { get; set; } = string.Empty;
    public string? Description { get; set; }
    public bool IsSystemRole { get; set; }

    public ICollection<RolePermission> Permissions { get; set; } = new List<RolePermission>();
}

public sealed class RolePermission : BaseEntity
{
    public Guid RoleId { get; set; }
    public Role? Role { get; set; }
    public string PermissionKey { get; set; } = string.Empty;
}
