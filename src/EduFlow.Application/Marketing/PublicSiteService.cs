using System.Net.Mail;
using System.Text.RegularExpressions;
using EduFlow.Application.Auth;
using EduFlow.Application.Common.Interfaces;
using EduFlow.Application.SuperAdmin;
using EduFlow.Domain.Entities;
using EduFlow.Domain.Enums;
using EduFlow.Domain.Exceptions;
using Microsoft.EntityFrameworkCore;

namespace EduFlow.Application.Marketing;

public sealed record SubdomainAvailabilityDto(string Subdomain, bool Available, string? Reason);

public sealed record SignupRequest(
    string InstituteName,
    string Subdomain,
    string AdminFullName,
    string AdminEmail,
    string AdminPassword,
    string? PlanCode,
    string? Phone);

public sealed record SignupResultDto(
    Guid TenantId,
    string Subdomain,
    string PlanCode,
    DateTimeOffset TrialEndsAt,
    string AdminEmail);

public sealed record DemoRequestInput(
    string InstituteName,
    string ContactName,
    string Email,
    string? Phone,
    string? City,
    int? ApproximateStudents,
    string? Message);

public sealed record DemoRequestResultDto(Guid Id, string Message);

public sealed record OnboardingStepDto(string Key, string Title, bool Done, string Href);

public sealed record OnboardingChecklistDto(IReadOnlyList<OnboardingStepDto> Steps, int Completed, int Total);

public interface IPublicSiteService
{
    Task<SubdomainAvailabilityDto> CheckSubdomainAsync(string subdomain, CancellationToken cancellationToken = default);
    Task<SignupResultDto> SignupAsync(SignupRequest request, CancellationToken cancellationToken = default);
    Task<DemoRequestResultDto> RequestDemoAsync(DemoRequestInput request, string notifyEmail, CancellationToken cancellationToken = default);
    Task<AuthResponse> StartDemoSessionAsync(CancellationToken cancellationToken = default);
    Task<OnboardingChecklistDto> GetOnboardingAsync(CancellationToken cancellationToken = default);
    Task MarkCardsPrintedAsync(CancellationToken cancellationToken = default);
}

public sealed partial class PublicSiteService(
    IAppDbContext db,
    ITenantContext tenantContext,
    ISuperAdminService superAdminService,
    IAuthService authService,
    ITransactionalEmailService transactionalEmail,
    IAuditService auditService) : IPublicSiteService
{
    private static readonly HashSet<string> ReservedSubdomains = new(StringComparer.OrdinalIgnoreCase)
    {
        "www", "api", "app", "admin", "demo", "mail", "static", "files", "support", "help", "status"
    };

    [GeneratedRegex("^[a-z0-9](?:[a-z0-9-]{0,61}[a-z0-9])?$")]
    private static partial Regex SubdomainRegex();

    public async Task<SubdomainAvailabilityDto> CheckSubdomainAsync(
        string subdomain,
        CancellationToken cancellationToken = default)
    {
        var normalized = (subdomain ?? string.Empty).Trim().ToLowerInvariant();
        if (!SubdomainRegex().IsMatch(normalized))
        {
            return new SubdomainAvailabilityDto(normalized, false, "Use lowercase letters, numbers, and hyphens.");
        }

        if (ReservedSubdomains.Contains(normalized))
        {
            return new SubdomainAvailabilityDto(normalized, false, "This address is reserved.");
        }

        var taken = await db.Tenants
            .IgnoreQueryFilters()
            .AnyAsync(t => !t.IsDeleted && t.Subdomain == normalized, cancellationToken);
        return taken
            ? new SubdomainAvailabilityDto(normalized, false, "This address is already in use.")
            : new SubdomainAvailabilityDto(normalized, true, null);
    }

    public async Task<SignupResultDto> SignupAsync(SignupRequest request, CancellationToken cancellationToken = default)
    {
        var institute = (request.InstituteName ?? string.Empty).Trim();
        if (institute.Length < 2 || institute.Length > 120)
        {
            throw new DomainValidationException("INVALID_INSTITUTE", "Enter the institute name (2–120 characters).");
        }

        var availability = await CheckSubdomainAsync(request.Subdomain, cancellationToken);
        if (!availability.Available)
        {
            throw new DomainValidationException(
                "SUBDOMAIN_UNAVAILABLE",
                availability.Reason ?? "Choose a different institute address.",
                409);
        }

        var adminName = (request.AdminFullName ?? string.Empty).Trim();
        if (adminName.Length < 2)
        {
            throw new DomainValidationException("INVALID_ADMIN_NAME", "Enter the admin name.");
        }

        var email = (request.AdminEmail ?? string.Empty).Trim();
        if (!IsEmail(email))
        {
            throw new DomainValidationException("INVALID_EMAIL", "Enter a valid admin email.");
        }

        var planCode = string.IsNullOrWhiteSpace(request.PlanCode) ? "Starter" : request.PlanCode.Trim();
        var planActive = await db.Plans
            .IgnoreQueryFilters()
            .AnyAsync(p => !p.IsDeleted && p.IsActive && p.Code.ToLower() == planCode.ToLower(), cancellationToken);
        if (!planActive)
        {
            throw new DomainValidationException("PLAN_NOT_AVAILABLE", "Choose an active plan.", 404);
        }

        var created = await superAdminService.CreateTenantAsync(
            new CreateTenantRequest(
                institute,
                availability.Subdomain,
                email,
                adminName,
                request.AdminPassword,
                planCode,
                email,
                request.Phone),
            cancellationToken);

        var trialEnds = created.Subscription?.TrialEndsAt ?? DateTimeOffset.UtcNow.AddDays(14);
        await transactionalEmail.SendWelcomeAsync(
            email,
            adminName,
            institute,
            created.Subdomain,
            trialEnds,
            cancellationToken);

        await auditService.LogAsync(
            "Public.Signup",
            nameof(Tenant),
            created.Id.ToString(),
            new { created.Subdomain, planCode, email },
            tenantIdOverride: created.Id,
            cancellationToken: cancellationToken);
        await db.SaveChangesAsync(cancellationToken);

        return new SignupResultDto(created.Id, created.Subdomain, planCode, trialEnds, email);
    }

    public async Task<DemoRequestResultDto> RequestDemoAsync(
        DemoRequestInput request,
        string notifyEmail,
        CancellationToken cancellationToken = default)
    {
        var institute = (request.InstituteName ?? string.Empty).Trim();
        var contact = (request.ContactName ?? string.Empty).Trim();
        var email = (request.Email ?? string.Empty).Trim();
        if (institute.Length < 2)
        {
            throw new DomainValidationException("INVALID_INSTITUTE", "Enter the institute name.");
        }

        if (contact.Length < 2)
        {
            throw new DomainValidationException("INVALID_CONTACT", "Enter your name.");
        }

        if (!IsEmail(email))
        {
            throw new DomainValidationException("INVALID_EMAIL", "Enter a valid email.");
        }

        var message = TrimTo(request.Message, 2000);
        var row = new DemoRequest
        {
            InstituteName = institute,
            ContactName = contact,
            Email = email,
            Phone = TrimTo(request.Phone, 40),
            City = TrimTo(request.City, 80),
            ApproximateStudents = request.ApproximateStudents is > 0 and < 100000 ? request.ApproximateStudents : null,
            Message = message
        };
        db.DemoRequests.Add(row);
        await db.SaveChangesAsync(cancellationToken);

        var support = string.IsNullOrWhiteSpace(notifyEmail) ? "superadmin@eduflow.lk" : notifyEmail.Trim();
        await transactionalEmail.SendDemoRequestAsync(
            support,
            row.InstituteName,
            row.ContactName,
            row.Email,
            row.Phone,
            row.City,
            row.ApproximateStudents,
            row.Message,
            cancellationToken);

        return new DemoRequestResultDto(row.Id, "Request received. We will email you to arrange the demo.");
    }

    public Task<AuthResponse> StartDemoSessionAsync(CancellationToken cancellationToken = default) =>
        authService.LoginDemoReadOnlyAsync(cancellationToken);

    public async Task<OnboardingChecklistDto> GetOnboardingAsync(CancellationToken cancellationToken = default)
    {
        var tenantId = RequireTenant();
        var logo = await db.BrandingProfiles
            .IgnoreQueryFilters()
            .AnyAsync(
                b => !b.IsDeleted && b.TenantId == tenantId && b.LogoUrl != null && b.LogoUrl != "",
                cancellationToken);
        var batches = await db.AcademicBatches
            .IgnoreQueryFilters()
            .AnyAsync(b => !b.IsDeleted && b.TenantId == tenantId, cancellationToken);
        var students = await db.Students
            .IgnoreQueryFilters()
            .AnyAsync(s => !s.IsDeleted && s.TenantId == tenantId, cancellationToken);
        var cards = await db.TenantOnboardings
            .IgnoreQueryFilters()
            .AnyAsync(o => !o.IsDeleted && o.TenantId == tenantId && o.CardsPrintedAt != null, cancellationToken);
        var messages = await db.TenantNotificationTypeSettings
            .IgnoreQueryFilters()
            .AnyAsync(s => !s.IsDeleted && s.TenantId == tenantId && s.IsEnabled, cancellationToken);

        var steps = new List<OnboardingStepDto>
        {
            new("logo", "Add your logo", logo, "/app/settings/branding"),
            new("batches", "Add batches", batches, "/app/academics/batches"),
            new("students", "Add students", students, "/app/students"),
            new("cards", "Print ID cards", cards, "/app/students"),
            new("messages", "Enable messages", messages, "/app/settings/features")
        };
        var completed = steps.Count(step => step.Done);
        return new OnboardingChecklistDto(steps, completed, steps.Count);
    }

    public async Task MarkCardsPrintedAsync(CancellationToken cancellationToken = default)
    {
        if (!tenantContext.TenantId.HasValue || tenantContext.TenantId.Value == Guid.Empty || tenantContext.IsReadOnly)
        {
            return;
        }

        var tenantId = tenantContext.TenantId.Value;
        var row = await db.TenantOnboardings
            .IgnoreQueryFilters()
            .FirstOrDefaultAsync(o => !o.IsDeleted && o.TenantId == tenantId, cancellationToken);
        if (row is null)
        {
            db.TenantOnboardings.Add(new TenantOnboarding
            {
                TenantId = tenantId,
                CardsPrintedAt = DateTimeOffset.UtcNow
            });
        }
        else if (row.CardsPrintedAt is null)
        {
            row.CardsPrintedAt = DateTimeOffset.UtcNow;
        }
        else
        {
            return;
        }

        await db.SaveChangesAsync(cancellationToken);
    }

    private Guid RequireTenant()
    {
        if (!tenantContext.TenantId.HasValue || tenantContext.TenantId.Value == Guid.Empty)
        {
            throw new DomainValidationException("TENANT_REQUIRED", "Sign in to an institute to continue.", 400);
        }

        return tenantContext.TenantId.Value;
    }

    private static bool IsEmail(string email)
    {
        try
        {
            var parsed = new MailAddress(email);
            return parsed.Address.Equals(email, StringComparison.OrdinalIgnoreCase) && email.Contains('@');
        }
        catch (FormatException)
        {
            return false;
        }
    }

    private static string? TrimTo(string? value, int max)
    {
        if (string.IsNullOrWhiteSpace(value))
        {
            return null;
        }

        var trimmed = value.Trim();
        return trimmed.Length <= max ? trimmed : trimmed[..max];
    }
}
