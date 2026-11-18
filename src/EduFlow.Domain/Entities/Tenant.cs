using EduFlow.Domain.Common;

namespace EduFlow.Domain.Entities;

public sealed class Tenant : BaseEntity
{
    public string Name { get; set; } = string.Empty;
    public string Subdomain { get; set; } = string.Empty;
    public string? ContactEmail { get; set; }
    public string? ContactPhone { get; set; }
    public string? Address { get; set; }
    public string? LogoUrl { get; set; }
    public string TimeZone { get; set; } = "Asia/Colombo";
    public string? SmsSenderId { get; set; }
    public bool AllowCustomSmsSenderId { get; set; }
    public bool IsActive { get; set; } = true;
    public bool IsDemo { get; set; }

    public ICollection<Subscription> Subscriptions { get; set; } = new List<Subscription>();
}
