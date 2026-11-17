using EduFlow.Application.Academics;
using EduFlow.Application.Attendance;
using EduFlow.Application.Auth;
using EduFlow.Application.Branding;
using EduFlow.Application.Common.Interfaces;
using EduFlow.Application.Features;
using EduFlow.Application.Fees;
using EduFlow.Application.Limits;
using EduFlow.Application.Notifications;
using EduFlow.Application.Staff;
using EduFlow.Application.Students;
using EduFlow.Application.SuperAdmin;
using EduFlow.Application.Tenancy;
using EduFlow.Infrastructure.Documents;
using EduFlow.Infrastructure.Fees;
using EduFlow.Infrastructure.Notifications;
using EduFlow.Infrastructure.Payments;
using EduFlow.Infrastructure.Persistence;
using EduFlow.Infrastructure.Security;
using EduFlow.Infrastructure.Services;
using EduFlow.Infrastructure.Storage;
using EduFlow.Infrastructure.Tenancy;
using Hangfire;
using Hangfire.InMemory;
using Hangfire.SqlServer;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;

namespace EduFlow.Infrastructure;

public static class DependencyInjection
{
    public static IServiceCollection AddInfrastructure(
        this IServiceCollection services,
        IConfiguration configuration)
    {
        services.AddMemoryCache();
        services.AddDataProtection();

        var jwtSection = configuration.GetSection(JwtOptions.SectionName);
        services.Configure<JwtOptions>(opts =>
        {
            opts.Issuer = jwtSection["Issuer"] ?? opts.Issuer;
            opts.Audience = jwtSection["Audience"] ?? opts.Audience;
            opts.SigningKey = jwtSection["SigningKey"] ?? opts.SigningKey;
            if (int.TryParse(jwtSection["AccessTokenMinutes"], out var mins))
            {
                opts.AccessTokenMinutes = mins;
            }
        });

        var notificationSection = configuration.GetSection(NotificationOptions.SectionName);
        services.Configure<NotificationOptions>(opts =>
        {
            opts.Sms.Provider = notificationSection["Sms:Provider"] ?? opts.Sms.Provider;
            opts.Sms.NotifyLk.Endpoint = notificationSection["Sms:NotifyLk:Endpoint"] ?? opts.Sms.NotifyLk.Endpoint;
            opts.Sms.NotifyLk.UserId = notificationSection["Sms:NotifyLk:UserId"] ?? opts.Sms.NotifyLk.UserId;
            opts.Sms.NotifyLk.ApiKey = notificationSection["Sms:NotifyLk:ApiKey"] ?? opts.Sms.NotifyLk.ApiKey;
            opts.Sms.NotifyLk.SenderId = notificationSection["Sms:NotifyLk:SenderId"] ?? opts.Sms.NotifyLk.SenderId;

            opts.Email.Provider = notificationSection["Email:Provider"] ?? opts.Email.Provider;
            opts.Email.FromEmail = notificationSection["Email:FromEmail"] ?? opts.Email.FromEmail;
            opts.Email.FromName = notificationSection["Email:FromName"] ?? opts.Email.FromName;
            opts.Email.Smtp.Host = notificationSection["Email:Smtp:Host"] ?? opts.Email.Smtp.Host;
            if (int.TryParse(notificationSection["Email:Smtp:Port"], out var smtpPort))
            {
                opts.Email.Smtp.Port = smtpPort;
            }
            opts.Email.Smtp.Username = notificationSection["Email:Smtp:Username"] ?? opts.Email.Smtp.Username;
            opts.Email.Smtp.Password = notificationSection["Email:Smtp:Password"] ?? opts.Email.Smtp.Password;
            opts.Email.Brevo.Endpoint = notificationSection["Email:Brevo:Endpoint"] ?? opts.Email.Brevo.Endpoint;
            opts.Email.Brevo.ApiKey = notificationSection["Email:Brevo:ApiKey"] ?? opts.Email.Brevo.ApiKey;

            opts.WhatsApp.Provider = notificationSection["WhatsApp:Provider"] ?? opts.WhatsApp.Provider;
            opts.WhatsApp.CloudApi.Endpoint = notificationSection["WhatsApp:CloudApi:Endpoint"] ?? opts.WhatsApp.CloudApi.Endpoint;
            opts.WhatsApp.CloudApi.PhoneNumberId = notificationSection["WhatsApp:CloudApi:PhoneNumberId"] ?? opts.WhatsApp.CloudApi.PhoneNumberId;
            opts.WhatsApp.CloudApi.AccessToken = notificationSection["WhatsApp:CloudApi:AccessToken"] ?? opts.WhatsApp.CloudApi.AccessToken;
        });

        var connectionString = configuration.GetConnectionString("Default");
        services.AddDbContext<AppDbContext>(options =>
        {
            if (!string.IsNullOrWhiteSpace(connectionString))
            {
                options.UseSqlServer(connectionString, sql =>
                {
                    sql.MigrationsAssembly(typeof(AppDbContext).Assembly.FullName);
                });
            }
        });

        var hangfireStorage = configuration["Hangfire:Storage"] ?? "InMemory";
        services.AddHangfire(hangfireConfig =>
        {
            hangfireConfig
                .SetDataCompatibilityLevel(CompatibilityLevel.Version_180)
                .UseSimpleAssemblyNameTypeSerializer()
                .UseRecommendedSerializerSettings();

            if (string.Equals(hangfireStorage, "SqlServer", StringComparison.OrdinalIgnoreCase)
                && !string.IsNullOrWhiteSpace(connectionString))
            {
                hangfireConfig.UseSqlServerStorage(connectionString, new SqlServerStorageOptions
                {
                    PrepareSchemaIfNecessary = true,
                    QueuePollInterval = TimeSpan.FromSeconds(15)
                });
            }
            else
            {
                hangfireConfig.UseInMemoryStorage(new InMemoryStorageOptions());
            }
        });

        services.AddScoped<IAppDbContext>(sp => sp.GetRequiredService<AppDbContext>());
        services.AddScoped<ITenantContext, TenantContext>();
        services.AddScoped<IAuditService, AuditService>();
        services.AddSingleton<InMemoryEmailStore>();
        services.AddSingleton<FakeSmsStore>();
        services.AddSingleton<FakeWhatsAppStore>();
        services.AddSingleton<HttpClient>();
        services.AddSingleton<ISmsSegmentCalculator, SmsSegmentCalculator>();

        var smsProviderName = notificationSection["Sms:Provider"] ?? "Fake";
        services.AddScoped<FakeSmsProvider>();
        services.AddScoped<NotifyLkSmsProvider>();
        services.AddScoped<ISmsProvider>(sp =>
            string.Equals(smsProviderName, "NotifyLk", StringComparison.OrdinalIgnoreCase)
                ? sp.GetRequiredService<NotifyLkSmsProvider>()
                : sp.GetRequiredService<FakeSmsProvider>());

        var whatsAppProviderName = notificationSection["WhatsApp:Provider"] ?? "Fake";
        services.AddScoped<FakeWhatsAppProvider>();
        services.AddScoped<MetaCloudWhatsAppProvider>();
        services.AddScoped<IWhatsAppProvider>(sp =>
            string.Equals(whatsAppProviderName, "CloudApi", StringComparison.OrdinalIgnoreCase)
                ? sp.GetRequiredService<MetaCloudWhatsAppProvider>()
                : sp.GetRequiredService<FakeWhatsAppProvider>());

        services.AddScoped<InMemoryEmailSender>();
        services.AddScoped<ConfigurableEmailSender>();
        services.AddScoped<IEmailSender>(sp => sp.GetRequiredService<ConfigurableEmailSender>());

        services.AddScoped<ITokenService, JwtTokenService>();

        services.AddScoped<IFeatureService, FeatureService>();
        services.AddScoped<ILimitService, LimitService>();
        services.AddScoped<IAuthService, AuthService>();
        services.AddScoped<ISuperAdminService, SuperAdminService>();
        services.AddScoped<ITenantManagementService, TenantManagementService>();
        services.AddScoped<IBrandingService, BrandingService>();
        services.AddScoped<IAcademicService, AcademicService>();
        services.AddScoped<IStudentService, StudentService>();
        services.AddScoped<IStaffDirectoryService, StaffDirectoryService>();
        services.AddScoped<IAttendanceService, AttendanceService>();
        services.AddScoped<IFeeService, FeeService>();
        services.AddScoped<NotificationService>();
        services.AddScoped<INotificationService>(sp => sp.GetRequiredService<NotificationService>());
        services.AddScoped<INotificationDispatcher>(sp => sp.GetRequiredService<NotificationService>());
        services.AddScoped<NotificationOutboxHangfireJob>();
        services.AddScoped<MonthlyFeeInvoiceHangfireJob>();
        services.AddScoped<FeeReminderHangfireJob>();

        services.AddSingleton<IPaymentGateway, PayHerePaymentGateway>();
        services.AddSingleton<IBarcodeEncoder, ZxingBarcodeEncoder>();
        services.AddSingleton<IAcademicDocumentService, AcademicPdfService>();
        services.AddSingleton<IFeeDocumentService, FeePdfService>();
        services.AddSingleton<IWorkbookFile, ClosedXmlWorkbookFile>();
        services.Configure<FileStorageOptions>(opts =>
        {
            opts.Root = configuration["FileStorage:Root"] ?? string.Empty;
            opts.PublicPath = configuration["FileStorage:PublicPath"] ?? "/files";
        });
        services.AddSingleton<IFileStorage, LocalFileStorage>();
        services.AddScoped<DbSeeder>();

        return services;
    }
}
