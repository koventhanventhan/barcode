using EduFlow.Domain.Common;

namespace EduFlow.Domain.Entities;

public sealed class Plan : BaseEntity
{
    public string Code { get; set; } = string.Empty;
    public string Name { get; set; } = string.Empty;
    public string? Description { get; set; }
    public decimal? PriceMonthlyLkr { get; set; }
    public decimal? PriceYearlyLkr { get; set; }
    public decimal? SetupFeeLkr { get; set; }
    public decimal? ExtraSmsPerSegmentLkr { get; set; }
    public decimal? ExtraStoragePerGbLkr { get; set; }
    public string Currency { get; set; } = "LKR";
    public bool IsActive { get; set; } = true;
    public int SortOrder { get; set; }

    public ICollection<PlanFeature> Features { get; set; } = new List<PlanFeature>();
    public ICollection<PlanLimit> Limits { get; set; } = new List<PlanLimit>();
}
