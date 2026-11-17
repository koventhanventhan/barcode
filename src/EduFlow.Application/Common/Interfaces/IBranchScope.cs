using EduFlow.Domain.Exceptions;

namespace EduFlow.Application.Common.Interfaces;

public interface IBranchScope
{
    bool Unrestricted { get; }
    Guid? SelectedBranchId { get; }
    Guid MainBranchId { get; }
    List<Guid> AllowedBranchIds { get; }

    void Apply(bool unrestricted, Guid? selectedBranchId, Guid mainBranchId, IReadOnlyCollection<Guid> allowedBranchIds);

    void EnsureCanUse(Guid? branchId);
}

public sealed class BranchScope : IBranchScope
{
    public bool Unrestricted { get; private set; } = true;
    public Guid? SelectedBranchId { get; private set; }
    public Guid MainBranchId { get; private set; }
    public List<Guid> AllowedBranchIds { get; } = [];

    public void Apply(bool unrestricted, Guid? selectedBranchId, Guid mainBranchId, IReadOnlyCollection<Guid> allowedBranchIds)
    {
        Unrestricted = unrestricted;
        SelectedBranchId = selectedBranchId;
        MainBranchId = mainBranchId;
        AllowedBranchIds.Clear();
        AllowedBranchIds.AddRange(allowedBranchIds.Distinct());
    }

    public void EnsureCanUse(Guid? branchId)
    {
        if (Unrestricted && SelectedBranchId is null)
        {
            return;
        }

        var effective = branchId ?? MainBranchId;
        if (effective == Guid.Empty)
        {
            return;
        }

        if (!Unrestricted && !AllowedBranchIds.Contains(effective))
        {
            throw new DomainValidationException("BRANCH_FORBIDDEN", "That branch is outside your access.", 403);
        }

        if (SelectedBranchId.HasValue && effective != SelectedBranchId.Value)
        {
            throw new DomainValidationException("BRANCH_FORBIDDEN", "Switch to that branch before changing its records.", 403);
        }
    }
}
