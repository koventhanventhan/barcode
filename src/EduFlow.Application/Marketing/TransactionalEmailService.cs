using EduFlow.Application.Common.Interfaces;
using EduFlow.Domain;
using EduFlow.Domain.Entities;
using EduFlow.Domain.Enums;
using Microsoft.EntityFrameworkCore;

namespace EduFlow.Application.Marketing;

public interface ITransactionalEmailService
{
    Task SendWelcomeAsync(
        string adminEmail,
        string adminName,
        string instituteName,
        string subdomain,
        DateTimeOffset trialEndsAt,
        CancellationToken cancellationToken = default);

    Task SendPasswordResetAsync(
        User user,
        string rawToken,
        DateTimeOffset expiresAt,
        CancellationToken cancellationToken = default);

    Task SendPlatformInvoiceAsync(
        Guid tenantId,
        string invoiceNumber,
        decimal amount,
        string currency,
        DateOnly dueDate,
        string status,
        CancellationToken cancellationToken = default);

    Task SendTrialEndingAsync(
        Guid tenantId,
        string adminEmail,
        string adminName,
        DateTimeOffset trialEndsAt,
        CancellationToken cancellationToken = default);

    Task SendTrialExpiredAsync(
        Guid tenantId,
        string adminEmail,
        string adminName,
        DateTimeOffset trialEndsAt,
        CancellationToken cancellationToken = default);

    Task SendDemoRequestAsync(
        string to,
        string instituteName,
        string contactName,
        string email,
        string? phone,
        string? city,
        int? students,
        string? message,
        CancellationToken cancellationToken = default);
}

public sealed class TransactionalEmailService(
    IAppDbContext db,
    IEmailSender emailSender,
    IAppLinks links) : ITransactionalEmailService
{
    public async Task SendWelcomeAsync(
        string adminEmail,
        string adminName,
        string instituteName,
        string subdomain,
        DateTimeOffset trialEndsAt,
        CancellationToken cancellationToken = default)
    {
        var brand = new EmailBrand(instituteName, BrandingProfile.DefaultPrimaryColor, false);
        var rendered = TransactionalEmail.Welcome(brand, adminName, subdomain, trialEndsAt);
        await SendAsync(adminEmail, rendered, "Welcome", cancellationToken);
    }

    public async Task SendPasswordResetAsync(
        User user,
        string rawToken,
        DateTimeOffset expiresAt,
        CancellationToken cancellationToken = default)
    {
        var brand = await LoadBrandAsync(user.TenantId, cancellationToken);
        var resetUrl = links.PasswordResetUrl(user.Email, rawToken);
        var rendered = TransactionalEmail.PasswordReset(brand, user.FullName, rawToken, resetUrl, expiresAt);
        await emailSender.SendAsync(
            new EmailMessage(
                user.Email,
                rendered.Subject,
                rendered.HtmlBody,
                rendered.PlainTextBody,
                new Dictionary<string, string>
                {
                    ["Type"] = "PasswordReset",
                    ["Token"] = rawToken,
                    ["Email"] = user.Email
                }),
            cancellationToken);
    }

    public async Task SendPlatformInvoiceAsync(
        Guid tenantId,
        string invoiceNumber,
        decimal amount,
        string currency,
        DateOnly dueDate,
        string status,
        CancellationToken cancellationToken = default)
    {
        var recipient = await ResolveAdminEmailAsync(tenantId, cancellationToken);
        if (recipient is null)
        {
            return;
        }

        var brand = await LoadBrandAsync(tenantId, cancellationToken);
        var rendered = TransactionalEmail.Invoice(brand, invoiceNumber, amount, currency, dueDate, status);
        await SendAsync(recipient, rendered, "Invoice", cancellationToken, invoiceNumber);
    }

    public async Task SendTrialEndingAsync(
        Guid tenantId,
        string adminEmail,
        string adminName,
        DateTimeOffset trialEndsAt,
        CancellationToken cancellationToken = default)
    {
        var brand = await LoadBrandAsync(tenantId, cancellationToken);
        var rendered = TransactionalEmail.TrialEnding(brand, adminName, trialEndsAt);
        await SendAsync(adminEmail, rendered, "TrialEnding", cancellationToken);
    }

    public async Task SendTrialExpiredAsync(
        Guid tenantId,
        string adminEmail,
        string adminName,
        DateTimeOffset trialEndsAt,
        CancellationToken cancellationToken = default)
    {
        var brand = await LoadBrandAsync(tenantId, cancellationToken);
        var rendered = TransactionalEmail.TrialExpired(brand, adminName, trialEndsAt);
        await SendAsync(adminEmail, rendered, "TrialExpired", cancellationToken);
    }

    public Task SendDemoRequestAsync(
        string to,
        string instituteName,
        string contactName,
        string email,
        string? phone,
        string? city,
        int? students,
        string? message,
        CancellationToken cancellationToken = default)
    {
        var rendered = TransactionalEmail.DemoRequestToAdmin(instituteName, contactName, email, phone, city, students, message);
        return SendAsync(to, rendered, "DemoRequest", cancellationToken);
    }

    private async Task SendAsync(
        string to,
        RenderedEmail rendered,
        string type,
        CancellationToken cancellationToken,
        string? reference = null)
    {
        var metadata = new Dictionary<string, string> { ["Type"] = type };
        if (!string.IsNullOrWhiteSpace(reference))
        {
            metadata["Reference"] = reference;
        }

        await emailSender.SendAsync(
            new EmailMessage(to, rendered.Subject, rendered.HtmlBody, rendered.PlainTextBody, metadata),
            cancellationToken);
    }

    private async Task<EmailBrand> LoadBrandAsync(Guid tenantId, CancellationToken cancellationToken)
    {
        if (tenantId == Guid.Empty)
        {
            return new EmailBrand(ProductName.Value, BrandingProfile.DefaultPrimaryColor, true);
        }

        var tenantName = await db.Tenants
            .IgnoreQueryFilters()
            .AsNoTracking()
            .Where(t => !t.IsDeleted && t.Id == tenantId)
            .Select(t => t.Name)
            .FirstOrDefaultAsync(cancellationToken);

        var profile = await db.BrandingProfiles
            .IgnoreQueryFilters()
            .AsNoTracking()
            .Where(b => !b.IsDeleted && b.TenantId == tenantId)
            .Select(b => new { b.InstituteDisplayName, b.PrimaryColor })
            .FirstOrDefaultAsync(cancellationToken);

        var name = profile is not null && !string.IsNullOrWhiteSpace(profile.InstituteDisplayName)
            ? profile.InstituteDisplayName
            : tenantName ?? ProductName.Value;
        var color = string.IsNullOrWhiteSpace(profile?.PrimaryColor)
            ? BrandingProfile.DefaultPrimaryColor
            : profile!.PrimaryColor;
        return new EmailBrand(name, color, false);
    }

    private async Task<string?> ResolveAdminEmailAsync(Guid tenantId, CancellationToken cancellationToken)
    {
        var contact = await db.Tenants
            .IgnoreQueryFilters()
            .AsNoTracking()
            .Where(t => !t.IsDeleted && t.Id == tenantId)
            .Select(t => t.ContactEmail)
            .FirstOrDefaultAsync(cancellationToken);
        if (!string.IsNullOrWhiteSpace(contact))
        {
            return contact.Trim();
        }

        return await db.Users
            .IgnoreQueryFilters()
            .AsNoTracking()
            .Where(u => !u.IsDeleted && u.IsActive && u.TenantId == tenantId && u.RoleType == RoleType.InstituteAdmin)
            .Select(u => u.Email)
            .FirstOrDefaultAsync(cancellationToken);
    }
}
