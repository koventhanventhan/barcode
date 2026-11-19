using System.Net.Mail;
using EduFlow.Application.Academics;
using EduFlow.Application.Common.Interfaces;
using EduFlow.Application.Common.Security;
using EduFlow.Application.Tenancy;
using EduFlow.Domain;
using EduFlow.Domain.Entities;
using EduFlow.Domain.Enums;
using EduFlow.Domain.Exceptions;
using Microsoft.EntityFrameworkCore;

namespace EduFlow.Application.Staff;

public interface IStaffRegistrationService
{
    Task<StaffRegistrationLinkDto> GetRegistrationLinkAsync(CancellationToken cancellationToken = default);
    Task<StaffRegistrationLinkDto> UpsertRegistrationLinkAsync(UpdateStaffRegistrationLinkRequest request, CancellationToken cancellationToken = default);
    Task<StaffRegistrationLinkDto> RotateRegistrationLinkAsync(CancellationToken cancellationToken = default);
    Task<StaffRegistrationPageDto> GetRequestsAsync(StaffRegistrationStatus? status = null, int page = 1, int pageSize = 25, CancellationToken cancellationToken = default);
    Task<StaffRegistrationRequestDto> ApproveAsync(Guid id, ApproveStaffRegistrationRequest request, CancellationToken cancellationToken = default);
    Task<StaffRegistrationRequestDto> RejectAsync(Guid id, RejectStaffRegistrationRequest request, CancellationToken cancellationToken = default);

    Task<PublicStaffRegistrationInfoDto> GetPublicInfoAsync(string token, CancellationToken cancellationToken = default);
    Task<PublicStaffRegistrationSubmittedDto> SubmitPublicAsync(string token, SubmitStaffRegistrationRequest request, CancellationToken cancellationToken = default);
}

public sealed class StaffRegistrationService(
    IAppDbContext db,
    ITenantContext tenantContext,
    ITenantManagementService tenants,
    IFeatureService featureService,
    ILimitService limitService,
    IAuditService auditService,
    IEmailSender emailSender,
    IAppLinks links,
    IQrCodeEncoder qrEncoder) : IStaffRegistrationService
{
    public async Task<StaffRegistrationLinkDto> GetRegistrationLinkAsync(CancellationToken cancellationToken = default)
    {
        TenantGuard.Require(tenantContext);
        var link = await db.StaffRegistrationLinks
            .AsNoTracking()
            .OrderByDescending(x => x.CreatedAt)
            .FirstOrDefaultAsync(cancellationToken);

        return MapLink(link);
    }

    public async Task<StaffRegistrationLinkDto> UpsertRegistrationLinkAsync(
        UpdateStaffRegistrationLinkRequest request,
        CancellationToken cancellationToken = default)
    {
        var tenantId = TenantGuard.Require(tenantContext);

        if (request.DefaultBranchId.HasValue)
        {
            var branchExists = await db.Branches
                .AsNoTracking()
                .AnyAsync(b => b.Id == request.DefaultBranchId.Value && b.IsActive, cancellationToken);
            if (!branchExists)
            {
                throw new EntityNotFoundException(nameof(Branch), request.DefaultBranchId.Value.ToString());
            }
        }

        var link = await db.StaffRegistrationLinks
            .OrderByDescending(x => x.CreatedAt)
            .FirstOrDefaultAsync(cancellationToken);

        if (link is null)
        {
            link = new StaffRegistrationLink
            {
                TenantId = tenantId,
                Token = PasswordHasher.GenerateSecureToken(24),
                IsEnabled = request.IsEnabled,
                DefaultBranchId = request.DefaultBranchId
            };
            db.StaffRegistrationLinks.Add(link);
        }
        else
        {
            link.IsEnabled = request.IsEnabled;
            link.DefaultBranchId = request.DefaultBranchId;
        }

        await auditService.LogAsync(
            "StaffRegistrationLink.Updated",
            nameof(StaffRegistrationLink),
            link.Id.ToString(),
            new { link.IsEnabled, link.DefaultBranchId },
            cancellationToken: cancellationToken);

        await db.SaveChangesAsync(cancellationToken);
        return MapLink(link);
    }

    public async Task<StaffRegistrationLinkDto> RotateRegistrationLinkAsync(CancellationToken cancellationToken = default)
    {
        var tenantId = TenantGuard.Require(tenantContext);

        var link = await db.StaffRegistrationLinks
            .OrderByDescending(x => x.CreatedAt)
            .FirstOrDefaultAsync(cancellationToken);

        var nextToken = PasswordHasher.GenerateSecureToken(24);
        if (link is null)
        {
            link = new StaffRegistrationLink
            {
                TenantId = tenantId,
                Token = nextToken,
                IsEnabled = true
            };
            db.StaffRegistrationLinks.Add(link);
        }
        else
        {
            link.Token = nextToken;
            link.IsEnabled = true;
        }

        await auditService.LogAsync(
            "StaffRegistrationLink.Rotated",
            nameof(StaffRegistrationLink),
            link.Id.ToString(),
            new { link.IsEnabled, link.DefaultBranchId },
            cancellationToken: cancellationToken);

        await db.SaveChangesAsync(cancellationToken);
        return MapLink(link);
    }

    public async Task<StaffRegistrationPageDto> GetRequestsAsync(
        StaffRegistrationStatus? status = null,
        int page = 1,
        int pageSize = 25,
        CancellationToken cancellationToken = default)
    {
        TenantGuard.Require(tenantContext);

        var safePage = Math.Max(1, page);
        var safePageSize = Math.Clamp(pageSize, 1, 100);

        var baseQuery = db.StaffRegistrationRequests.AsNoTracking();
        var pendingCount = await baseQuery.CountAsync(r => r.Status == StaffRegistrationStatus.Pending, cancellationToken);

        if (status.HasValue)
        {
            baseQuery = baseQuery.Where(r => r.Status == status.Value);
        }

        var totalCount = await baseQuery.CountAsync(cancellationToken);
        var rows = await baseQuery
            .OrderBy(r => r.Status == StaffRegistrationStatus.Pending ? 0 : 1)
            .ThenByDescending(r => r.CreatedAt)
            .Skip((safePage - 1) * safePageSize)
            .Take(safePageSize)
            .ToListAsync(cancellationToken);

        var items = await MapRequestsAsync(rows, cancellationToken);
        return new StaffRegistrationPageDto(items, totalCount, pendingCount, safePage, safePageSize);
    }

    public async Task<StaffRegistrationRequestDto> ApproveAsync(
        Guid id,
        ApproveStaffRegistrationRequest request,
        CancellationToken cancellationToken = default)
    {
        var tenantId = TenantGuard.Require(tenantContext);

        var reg = await db.StaffRegistrationRequests.FirstOrDefaultAsync(r => r.Id == id, cancellationToken)
            ?? throw new EntityNotFoundException(nameof(StaffRegistrationRequest), id.ToString());

        if (reg.Status != StaffRegistrationStatus.Pending)
        {
            throw new DomainValidationException("REGISTRATION_ALREADY_REVIEWED", "Only pending staff registrations can be reviewed.");
        }

        await limitService.EnsureWithinLimitAsync(tenantId, LimitKey.StaffAccounts, cancellationToken: cancellationToken);

        var templateKey = string.IsNullOrWhiteSpace(request.TemplateKey)
            && string.IsNullOrWhiteSpace(request.RoleType)
            && (request.Permissions is null || request.Permissions.Count == 0)
            ? (reg.RequestedRoleType == RoleType.Teacher ? "class-teacher" : "front-desk")
            : request.TemplateKey;

        var (roleType, roleId) = await StaffRoleResolver.ResolveAsync(
            db,
            templateKey,
            request.RoleType,
            request.Permissions,
            request.RoleName,
            reg.FullName,
            cancellationToken);

        var branchIds = request.BranchIds is { Count: > 0 }
            ? request.BranchIds
            : request.BranchId.HasValue
                ? [request.BranchId.Value]
                : reg.BranchId.HasValue
                    ? [reg.BranchId.Value]
                    : Array.Empty<Guid>();

        var createdUser = await tenants.CreateUserFromHashAsync(
            new CreateTenantUserFromHashRequest(
                reg.Email,
                reg.FullName,
                reg.PasswordHash,
                roleType,
                reg.Phone,
                roleId,
                branchIds.FirstOrDefault() is Guid first && first != Guid.Empty ? first : null,
                branchIds),
            cancellationToken);

        reg.Status = StaffRegistrationStatus.Approved;
        reg.ReviewedByUserId = tenantContext.UserId;
        reg.ReviewedAt = DateTimeOffset.UtcNow;
        reg.ReviewNotes = TrimNotes(request.ReviewNotes);
        reg.CreatedUserId = createdUser.Id;

        await auditService.LogAsync(
            "StaffRegistration.Approved",
            nameof(StaffRegistrationRequest),
            reg.Id.ToString(),
            new
            {
                Email = PiiRedactor.MaskEmail(reg.Email),
                RoleType = roleType.ToString(),
                CreatedUserId = createdUser.Id
            },
            cancellationToken: cancellationToken);

        await db.SaveChangesAsync(cancellationToken);

        var tenant = await db.Tenants
            .IgnoreQueryFilters()
            .AsNoTracking()
            .FirstOrDefaultAsync(t => t.Id == tenantId, cancellationToken);
        var instituteLabel = tenant?.Name ?? ProductName.Value;

        await emailSender.SendAsync(
            new EmailMessage(
                To: reg.Email,
                Subject: $"Your {instituteLabel} staff account is approved",
                HtmlBody: $"<p>Hello {System.Net.WebUtility.HtmlEncode(reg.FullName)},</p><p>Your staff registration for <strong>{System.Net.WebUtility.HtmlEncode(instituteLabel)}</strong> has been approved. You can now sign in with your email and password.</p>",
                PlainTextBody: $"Hello {reg.FullName}, your staff registration for {instituteLabel} has been approved. You can now sign in with your email and password.",
                Metadata: new Dictionary<string, string>
                {
                    ["Type"] = "StaffRegistrationApproved",
                    ["TenantId"] = tenantId.ToString()
                }),
            cancellationToken);

        return (await MapRequestsAsync([reg], cancellationToken)).Single();
    }

    public async Task<StaffRegistrationRequestDto> RejectAsync(
        Guid id,
        RejectStaffRegistrationRequest request,
        CancellationToken cancellationToken = default)
    {
        TenantGuard.Require(tenantContext);

        var reg = await db.StaffRegistrationRequests.FirstOrDefaultAsync(r => r.Id == id, cancellationToken)
            ?? throw new EntityNotFoundException(nameof(StaffRegistrationRequest), id.ToString());

        if (reg.Status != StaffRegistrationStatus.Pending)
        {
            throw new DomainValidationException("REGISTRATION_ALREADY_REVIEWED", "Only pending staff registrations can be reviewed.");
        }

        reg.Status = StaffRegistrationStatus.Rejected;
        reg.ReviewedByUserId = tenantContext.UserId;
        reg.ReviewedAt = DateTimeOffset.UtcNow;
        reg.ReviewNotes = TrimNotes(request.ReviewNotes);

        await auditService.LogAsync(
            "StaffRegistration.Rejected",
            nameof(StaffRegistrationRequest),
            reg.Id.ToString(),
            new { Email = PiiRedactor.MaskEmail(reg.Email), reg.ReviewNotes },
            cancellationToken: cancellationToken);

        await db.SaveChangesAsync(cancellationToken);
        return (await MapRequestsAsync([reg], cancellationToken)).Single();
    }

    public async Task<PublicStaffRegistrationInfoDto> GetPublicInfoAsync(
        string token,
        CancellationToken cancellationToken = default)
    {
        var (link, tenant) = await ResolveActiveLinkAsync(token, cancellationToken);

        var profile = await db.BrandingProfiles
            .IgnoreQueryFilters()
            .AsNoTracking()
            .FirstOrDefaultAsync(b => !b.IsDeleted && b.TenantId == link.TenantId, cancellationToken);

        var multiBranchEnabled = await featureService.IsEnabledAsync(link.TenantId, FeatureKey.MultiBranch, cancellationToken);
        var branches = multiBranchEnabled
            ? await db.Branches
                .IgnoreQueryFilters()
                .AsNoTracking()
                .Where(b => !b.IsDeleted && b.TenantId == link.TenantId && b.IsActive)
                .OrderByDescending(b => b.IsMain)
                .ThenBy(b => b.Name)
                .Select(b => new PublicBranchOptionDto(b.Id, b.Name))
                .ToListAsync(cancellationToken)
            : [];

        return new PublicStaffRegistrationInfoDto(
            string.IsNullOrWhiteSpace(profile?.InstituteDisplayName) ? tenant.Name : profile.InstituteDisplayName,
            profile?.Tagline,
            profile?.LogoUrl,
            string.IsNullOrWhiteSpace(profile?.PrimaryColor) ? BrandingProfile.DefaultPrimaryColor : profile.PrimaryColor,
            string.IsNullOrWhiteSpace(profile?.AccentColor) ? BrandingProfile.DefaultAccentColor : profile.AccentColor,
            multiBranchEnabled ? link.DefaultBranchId : null,
            branches);
    }

    public async Task<PublicStaffRegistrationSubmittedDto> SubmitPublicAsync(
        string token,
        SubmitStaffRegistrationRequest request,
        CancellationToken cancellationToken = default)
    {
        var (link, _) = await ResolveActiveLinkAsync(token, cancellationToken);

        var fullName = (request.FullName ?? string.Empty).Trim();
        if (fullName.Length < 2 || fullName.Length > 200)
        {
            throw new DomainValidationException("INVALID_NAME", "Enter your full name (2–200 characters).");
        }

        var email = (request.Email ?? string.Empty).Trim().ToLowerInvariant();
        if (email.Length > 256 || !IsValidEmail(email))
        {
            throw new DomainValidationException("INVALID_EMAIL", "Enter a valid email address.");
        }

        PasswordPolicy.ValidateOrThrow(request.Password ?? string.Empty);

        if (request.RequestedRoleType is not (RoleType.Staff or RoleType.Teacher))
        {
            throw new DomainValidationException("INVALID_ROLE", "Self-registration supports Staff or Teacher roles only.");
        }

        var phone = string.IsNullOrWhiteSpace(request.Phone) ? null : request.Phone.Trim();
        if (phone is { Length: > 32 })
        {
            throw new DomainValidationException("INVALID_PHONE", "Phone number must be 32 characters or fewer.");
        }

        Guid? branchId = link.DefaultBranchId;
        if (request.BranchId.HasValue && request.BranchId.Value != Guid.Empty)
        {
            var validBranch = await db.Branches
                .IgnoreQueryFilters()
                .AsNoTracking()
                .AnyAsync(
                    b => !b.IsDeleted && b.TenantId == link.TenantId && b.Id == request.BranchId.Value && b.IsActive,
                    cancellationToken);
            if (!validBranch)
            {
                throw new DomainValidationException("INVALID_BRANCH", "Selected branch is not available.");
            }

            branchId = request.BranchId.Value;
        }

        var userExists = await db.Users
            .IgnoreQueryFilters()
            .AnyAsync(
                u => !u.IsDeleted && u.TenantId == link.TenantId && u.Email.ToLower() == email,
                cancellationToken);
        if (userExists)
        {
            throw new DomainValidationException(
                "EMAIL_ALREADY_EXISTS",
                "An account with this email already exists in this institute.",
                409);
        }

        var pendingExists = await db.StaffRegistrationRequests
            .IgnoreQueryFilters()
            .AnyAsync(
                r => !r.IsDeleted
                     && r.TenantId == link.TenantId
                     && r.Status == StaffRegistrationStatus.Pending
                     && r.Email.ToLower() == email,
                cancellationToken);
        if (pendingExists)
        {
            throw new DomainValidationException(
                "REGISTRATION_ALREADY_PENDING",
                "A registration request for this email is already awaiting admin approval.",
                409);
        }

        var entity = new StaffRegistrationRequest
        {
            TenantId = link.TenantId,
            FullName = fullName,
            Email = email,
            Phone = phone,
            PasswordHash = PasswordHasher.HashPassword(request.Password!),
            RequestedRoleType = request.RequestedRoleType,
            BranchId = branchId,
            Notes = TrimNotes(request.Notes),
            Status = StaffRegistrationStatus.Pending
        };

        using (tenantContext.BeginSystemScope())
        {
            db.StaffRegistrationRequests.Add(entity);
            await auditService.LogAsync(
                "StaffRegistration.Submitted",
                nameof(StaffRegistrationRequest),
                entity.Id.ToString(),
                new { Email = PiiRedactor.MaskEmail(email), RequestedRoleType = entity.RequestedRoleType.ToString() },
                tenantIdOverride: link.TenantId,
                cancellationToken: cancellationToken);
            await db.SaveChangesAsync(cancellationToken);
        }

        return new PublicStaffRegistrationSubmittedDto(
            entity.Id,
            entity.Status.ToString(),
            "Registration submitted. An institute administrator will review and approve your account.");
    }

    private async Task<(StaffRegistrationLink Link, Tenant Tenant)> ResolveActiveLinkAsync(
        string token,
        CancellationToken cancellationToken)
    {
        var normalizedToken = (token ?? string.Empty).Trim();
        if (string.IsNullOrWhiteSpace(normalizedToken) || normalizedToken.Length > 128)
        {
            throw new DomainValidationException("REGISTRATION_LINK_INVALID", "This staff registration link is invalid or disabled.", 404);
        }

        var link = await db.StaffRegistrationLinks
            .IgnoreQueryFilters()
            .AsNoTracking()
            .FirstOrDefaultAsync(l => !l.IsDeleted && l.IsEnabled && l.Token == normalizedToken, cancellationToken);

        if (link is null)
        {
            throw new DomainValidationException("REGISTRATION_LINK_INVALID", "This staff registration link is invalid or disabled.", 404);
        }

        var tenant = await db.Tenants
            .IgnoreQueryFilters()
            .AsNoTracking()
            .FirstOrDefaultAsync(t => !t.IsDeleted && t.IsActive && t.Id == link.TenantId, cancellationToken);

        if (tenant is null)
        {
            throw new DomainValidationException("REGISTRATION_LINK_INVALID", "This staff registration link is invalid or disabled.", 404);
        }

        var subscription = await db.Subscriptions
            .IgnoreQueryFilters()
            .AsNoTracking()
            .Where(s => !s.IsDeleted && s.TenantId == tenant.Id)
            .OrderByDescending(s => s.CreatedAt)
            .FirstOrDefaultAsync(cancellationToken);

        if (subscription is not null
            && (subscription.Status == SubscriptionStatus.Suspended || subscription.IsEffectivelyExpired(DateTimeOffset.UtcNow)))
        {
            throw new DomainValidationException("REGISTRATION_LINK_INVALID", "This staff registration link is invalid or disabled.", 404);
        }

        return (link, tenant);
    }

    private StaffRegistrationLinkDto MapLink(StaffRegistrationLink? link)
    {
        if (link is null)
        {
            return new StaffRegistrationLinkDto(false, false, null, null, null, null, null);
        }

        var url = links.StaffRegistrationUrl(link.Token);
        var qr = qrEncoder.EncodeQr(url);
        return new StaffRegistrationLinkDto(
            true,
            link.IsEnabled,
            link.Token,
            url,
            qr,
            link.DefaultBranchId,
            link.UpdatedAt ?? link.CreatedAt);
    }

    private async Task<IReadOnlyList<StaffRegistrationRequestDto>> MapRequestsAsync(
        IReadOnlyList<StaffRegistrationRequest> rows,
        CancellationToken cancellationToken)
    {
        var branchIds = rows.Where(r => r.BranchId.HasValue).Select(r => r.BranchId!.Value).Distinct().ToList();
        var branchNames = branchIds.Count == 0
            ? new Dictionary<Guid, string>()
            : await db.Branches
                .AsNoTracking()
                .Where(b => branchIds.Contains(b.Id))
                .ToDictionaryAsync(b => b.Id, b => b.Name, cancellationToken);

        return rows.Select(r => new StaffRegistrationRequestDto(
            r.Id,
            r.FullName,
            r.Email,
            r.Phone,
            r.RequestedRoleType.ToString(),
            r.BranchId,
            r.BranchId.HasValue && branchNames.TryGetValue(r.BranchId.Value, out var branchName) ? branchName : null,
            r.Notes,
            r.Status.ToString(),
            r.ReviewedByUserId,
            r.ReviewedAt,
            r.ReviewNotes,
            r.CreatedUserId,
            r.CreatedAt)).ToList();
    }

    private static string? TrimNotes(string? value)
    {
        if (string.IsNullOrWhiteSpace(value))
        {
            return null;
        }

        var trimmed = value.Trim();
        return trimmed.Length > 500 ? trimmed[..500] : trimmed;
    }

    private static bool IsValidEmail(string value)
    {
        if (string.IsNullOrWhiteSpace(value))
        {
            return false;
        }

        try
        {
            var address = new MailAddress(value);
            return string.Equals(address.Address, value, StringComparison.OrdinalIgnoreCase);
        }
        catch
        {
            return false;
        }
    }
}
