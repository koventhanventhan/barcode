using System.Net;
using EduFlow.Domain;

namespace EduFlow.Application.Marketing;

public sealed record EmailBrand(string InstituteName, string PrimaryColor, bool ProductLevel);

public sealed record RenderedEmail(string Subject, string HtmlBody, string PlainTextBody);

public static class TransactionalEmail
{
    public static RenderedEmail Welcome(
        EmailBrand brand,
        string adminName,
        string subdomain,
        DateTimeOffset trialEndsAt)
    {
        var subject = $"Your {ProductName.Value} trial for {brand.InstituteName} is ready";
        var text =
            $"Hello {adminName},\n\n" +
            $"{brand.InstituteName} is on a 14-day {ProductName.Value} trial. It ends on {trialEndsAt:yyyy-MM-dd}.\n" +
            $"Sign in with institute code {subdomain}.\n" +
            "Add your logo, batches, and students, then print ID cards and turn on parent messages.\n";
        return new RenderedEmail(subject, Layout(brand, "Trial started", Paragraphs(text)), text);
    }

    public static RenderedEmail TrialEnding(EmailBrand brand, string adminName, DateTimeOffset trialEndsAt)
    {
        var subject = $"{brand.InstituteName} trial ends in 3 days";
        var text =
            $"Hello {adminName},\n\n" +
            $"The free trial for {brand.InstituteName} ends on {trialEndsAt:yyyy-MM-dd}. " +
            "After that the institute becomes read-only until a subscription is activated.\n" +
            "Reply to this email if you want a plan turned on.\n";
        return new RenderedEmail(subject, Layout(brand, "Trial ends in 3 days", Paragraphs(text)), text);
    }

    public static RenderedEmail TrialExpired(EmailBrand brand, string adminName, DateTimeOffset trialEndsAt)
    {
        var subject = $"{brand.InstituteName} trial has ended";
        var text =
            $"Hello {adminName},\n\n" +
            $"The free trial for {brand.InstituteName} ended on {trialEndsAt:yyyy-MM-dd}. " +
            "Staff can still view records. Adding or changing records stays blocked until a subscription is activated.\n";
        return new RenderedEmail(subject, Layout(brand, "Trial ended", Paragraphs(text)), text);
    }

    public static RenderedEmail Invoice(
        EmailBrand brand,
        string invoiceNumber,
        decimal amount,
        string currency,
        DateOnly dueDate,
        string status)
    {
        var subject = $"{ProductName.Value} invoice {invoiceNumber}";
        var text =
            $"{brand.InstituteName}\n" +
            $"Invoice {invoiceNumber}\n" +
            $"Amount {amount:0.00} {currency}\n" +
            $"Due {dueDate:yyyy-MM-dd}\n" +
            $"Status {status}\n";
        return new RenderedEmail(subject, Layout(brand, $"Invoice {invoiceNumber}", Paragraphs(text)), text);
    }

    public static RenderedEmail PasswordReset(
        EmailBrand brand,
        string fullName,
        string token,
        string resetUrl,
        DateTimeOffset expiresAt)
    {
        var subject = $"Reset your {brand.InstituteName} password";
        var text =
            $"Hello {fullName},\n\n" +
            $"Use this link to reset your password before {expiresAt:yyyy-MM-dd HH:mm} UTC:\n{resetUrl}\n\n" +
            $"Reset token: {token}\n" +
            "If you did not ask for this, ignore the email.\n";
        return new RenderedEmail(subject, Layout(brand, "Password reset", Paragraphs(text)), text);
    }

    public static RenderedEmail DemoRequestToAdmin(
        string instituteName,
        string contactName,
        string email,
        string? phone,
        string? city,
        int? students,
        string? message)
    {
        var subject = $"Demo request from {instituteName}";
        var text =
            $"Institute: {instituteName}\n" +
            $"Contact: {contactName}\n" +
            $"Email: {email}\n" +
            $"Phone: {phone}\n" +
            $"City: {city}\n" +
            $"Approximate students: {students?.ToString() ?? "not given"}\n" +
            $"Message: {message}\n";
        var brand = new EmailBrand(ProductName.Value, "#1F4E46", true);
        return new RenderedEmail(subject, Layout(brand, "Demo request", Paragraphs(text)), text);
    }

    private static string Layout(EmailBrand brand, string heading, string body)
    {
        var color = string.IsNullOrWhiteSpace(brand.PrimaryColor) ? "#1F4E46" : brand.PrimaryColor;
        var name = Encode(brand.InstituteName);
        var product = brand.ProductLevel ? "" : $"<p style=\"margin:16px 0 0;font-size:12px;color:#5d6b65\">{Encode(ProductName.Value)}</p>";
        return
            "<div style=\"margin:0;padding:24px;background:#f4f6f5;font-family:Inter,Segoe UI,sans-serif;color:#14211c\">" +
            "<div style=\"max-width:560px;margin:0 auto;background:#ffffff;border:1px solid #d5ddd8;border-radius:8px;overflow:hidden\">" +
            $"<div style=\"background:{Encode(color)};color:#f8fafc;padding:16px 20px;font-size:14px;font-weight:600\">{name}</div>" +
            "<div style=\"padding:20px\">" +
            $"<h1 style=\"margin:0 0 12px;font-size:20px;line-height:1.3\">{Encode(heading)}</h1>" +
            body +
            product +
            "</div></div></div>";
    }

    private static string Paragraphs(string text)
    {
        var blocks = text.Replace("\r\n", "\n").Split('\n');
        return string.Concat(blocks.Select(line =>
            $"<p style=\"margin:0 0 8px;font-size:14px;line-height:1.5\">{Encode(line)}</p>"));
    }

    private static string Encode(string? value) => WebUtility.HtmlEncode(value ?? string.Empty);
}
