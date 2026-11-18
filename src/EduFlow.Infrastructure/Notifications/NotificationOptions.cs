namespace EduFlow.Infrastructure.Notifications;

public sealed class NotificationOptions
{
    public const string SectionName = "Notifications";

    public SmsProviderOptions Sms { get; set; } = new();
    public EmailProviderOptions Email { get; set; } = new();
    public WhatsAppProviderOptions WhatsApp { get; set; } = new();
}

public sealed class SmsProviderOptions
{
    public const string SectionName = "Sms";

    public string Provider { get; set; } = "Fake";
    public string UserId { get; set; } = string.Empty;
    public string ApiKey { get; set; } = string.Empty;
    public string SenderId { get; set; } = "Admino";
    public string PromotionalSenderId { get; set; } = string.Empty;
    public string BaseUrl { get; set; } = "https://app.notify.lk/api/v1";
    public bool? TestMode { get; set; }
    public string TestAllowList { get; set; } = string.Empty;
    public decimal LowBalanceThreshold { get; set; } = 500m;
}

public sealed class EmailProviderOptions
{
    public string Provider { get; set; } = "Fake";
    public string FromEmail { get; set; } = "noreply@admino.lk";
    public string FromName { get; set; } = "Admino";
    public SmtpOptions Smtp { get; set; } = new();
    public BrevoOptions Brevo { get; set; } = new();
}

public sealed class SmtpOptions
{
    public string Host { get; set; } = string.Empty;
    public int Port { get; set; } = 587;
    public string Username { get; set; } = string.Empty;
    public string Password { get; set; } = string.Empty;
    public bool EnableSsl { get; set; } = true;
}

public sealed class BrevoOptions
{
    public string Endpoint { get; set; } = "https://api.brevo.com/v3/smtp/email";
    public string ApiKey { get; set; } = string.Empty;
}

public sealed class WhatsAppProviderOptions
{
    public string Provider { get; set; } = "Fake";
    public WhatsAppCloudApiOptions CloudApi { get; set; } = new();
}

public sealed class WhatsAppCloudApiOptions
{
    public string Endpoint { get; set; } = "https://graph.facebook.com/v19.0";
    public string PhoneNumberId { get; set; } = string.Empty;
    public string AccessToken { get; set; } = string.Empty;
}
