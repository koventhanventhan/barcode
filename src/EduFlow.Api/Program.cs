using System.Text;
using System.Text.Json.Serialization;
using System.Threading.RateLimiting;
using DotNetEnv;
using EduFlow.Api.Authorization;
using EduFlow.Api.Endpoints;
using EduFlow.Api.ExceptionHandling;
using EduFlow.Api.Middleware;
using EduFlow.Api.Swagger;
using EduFlow.Application.Common.Security;
using EduFlow.Domain;
using EduFlow.Domain.Constants;
using EduFlow.Domain.Enums;
using EduFlow.Infrastructure;
using EduFlow.Infrastructure.Fees;
using EduFlow.Infrastructure.Notifications;
using EduFlow.Infrastructure.Persistence;
using EduFlow.Infrastructure.Security;
using EduFlow.Infrastructure.Storage;
using Hangfire;
using Microsoft.AspNetCore.Authentication.JwtBearer;
using Microsoft.AspNetCore.Authorization;
using Microsoft.Extensions.FileProviders;
using Microsoft.Extensions.Options;
using Microsoft.EntityFrameworkCore;
using Microsoft.IdentityModel.Tokens;
using Microsoft.OpenApi.Models;
using Serilog;

LoadEnvFile();

var builder = WebApplication.CreateBuilder(args);
builder.WebHost.ConfigureKestrel(options => options.AddServerHeader = false);

builder.Host.UseSerilog((context, services, configuration) =>
    configuration
        .ReadFrom.Configuration(context.Configuration)
        .ReadFrom.Services(services)
        .Enrich.FromLogContext());

builder.Services.ConfigureHttpJsonOptions(options =>
{
    options.SerializerOptions.Converters.Add(new JsonStringEnumConverter());
});
builder.Services.AddProblemDetails();
builder.Services.AddExceptionHandler<GlobalExceptionHandler>();
builder.Services.AddHealthChecks();
builder.Services.AddResponseCaching();
builder.Services.AddInfrastructure(builder.Configuration);

builder.Services
    .AddAuthentication(JwtBearerDefaults.AuthenticationScheme)
    .AddJwtBearer();

builder.Services
    .AddOptions<JwtBearerOptions>(JwtBearerDefaults.AuthenticationScheme)
    .Configure<IOptions<JwtOptions>>((options, jwtAccessor) =>
    {
        var jwtOptions = jwtAccessor.Value;
        var signingKeyBytes = Encoding.UTF8.GetBytes(jwtOptions.SigningKey);
        options.MapInboundClaims = false;
        options.TokenValidationParameters = new TokenValidationParameters
        {
            ValidateIssuer = true,
            ValidIssuer = jwtOptions.Issuer,
            ValidateAudience = true,
            ValidAudience = jwtOptions.Audience,
            ValidateIssuerSigningKey = true,
            IssuerSigningKey = new SymmetricSecurityKey(signingKeyBytes),
            ValidateLifetime = true,
            ClockSkew = TimeSpan.FromSeconds(30)
        };
        options.Events = new JwtBearerEvents
        {
            OnMessageReceived = context =>
            {
                var accessToken = context.Request.Query["access_token"];
                if (!string.IsNullOrEmpty(accessToken) && context.HttpContext.Request.Path.StartsWithSegments("/hubs/attendance"))
                {
                    context.Token = accessToken;
                }

                return Task.CompletedTask;
            }
        };
    });
builder.Services.AddSignalR();
builder.Services.AddScoped<EduFlow.Application.Attendance.IAttendanceBoard, EduFlow.Api.Hubs.SignalRAttendanceBoard>();

builder.Services.AddSingleton<IAuthorizationHandler, PermissionAuthorizationHandler>();
builder.Services.AddAuthorization(options =>
{
    options.AddPolicy(AuthorizationPolicies.SuperAdminOnly, policy =>
        policy.RequireAuthenticatedUser()
              .RequireAssertion(ctx =>
                  ctx.User.Claims.Any(c =>
                      (c.Type == System.Security.Claims.ClaimTypes.Role || c.Type == "role")
                      && string.Equals(c.Value, nameof(RoleType.SuperAdmin), StringComparison.OrdinalIgnoreCase))
                  && !ctx.User.Claims.Any(c => c.Type == "impersonating" && c.Value == "true")));

    options.AddPolicy(AuthorizationPolicies.ParentOnly, policy =>
        policy.RequireAuthenticatedUser()
              .RequireAssertion(ctx =>
                  ctx.User.Claims.Any(c =>
                      (c.Type == System.Security.Claims.ClaimTypes.Role || c.Type == "role")
                      && string.Equals(c.Value, nameof(RoleType.Parent), StringComparison.OrdinalIgnoreCase))));

    options.AddPolicy(AuthorizationPolicies.InstituteAdminOnly, policy =>
        policy.RequireAuthenticatedUser()
              .RequireAssertion(ctx =>
                  ctx.User.Claims.Any(c =>
                      (c.Type == System.Security.Claims.ClaimTypes.Role || c.Type == "role")
                      && (string.Equals(c.Value, nameof(RoleType.InstituteAdmin), StringComparison.OrdinalIgnoreCase)
                          || string.Equals(c.Value, nameof(RoleType.SuperAdmin), StringComparison.OrdinalIgnoreCase)))));

    foreach (var perm in Permissions.All)
    {
        options.AddPolicy(
            AuthorizationPolicies.ForPermission(perm.Key),
            policy => policy.RequireAuthenticatedUser().AddRequirements(new PermissionRequirement(perm.Key)));
    }
});

var authPermitLimit = builder.Configuration.GetValue("RateLimiting:AuthPermitLimit", 60);
var publicPermitLimit = builder.Configuration.GetValue("RateLimiting:PublicPermitLimit", 120);
var scanPermitLimit = builder.Configuration.GetValue("RateLimiting:ScanPermitLimit", 600);
var authWindowSeconds = builder.Configuration.GetValue("RateLimiting:WindowSeconds", 60);

builder.Services.AddRateLimiter(options =>
{
    options.RejectionStatusCode = StatusCodes.Status429TooManyRequests;
    options.OnRejected = async (context, cancellationToken) =>
    {
        context.HttpContext.Response.StatusCode = StatusCodes.Status429TooManyRequests;
        await context.HttpContext.Response.WriteAsJsonAsync(
            new { code = "RATE_LIMIT_EXCEEDED", message = "Too many requests. Please try again later." },
            cancellationToken);
    };

    static string ResolveClientKey(HttpContext httpContext) =>
        httpContext.Request.Headers["X-Forwarded-For"].FirstOrDefault()
        ?? httpContext.Connection.RemoteIpAddress?.ToString()
        ?? "local";

    options.AddPolicy("auth", httpContext =>
        RateLimitPartition.GetFixedWindowLimiter(
            ResolveClientKey(httpContext),
            _ => new FixedWindowRateLimiterOptions
            {
                PermitLimit = authPermitLimit,
                Window = TimeSpan.FromSeconds(authWindowSeconds),
                QueueLimit = 0
            }));

    options.AddPolicy("public", httpContext =>
        RateLimitPartition.GetFixedWindowLimiter(
            ResolveClientKey(httpContext),
            _ => new FixedWindowRateLimiterOptions
            {
                PermitLimit = publicPermitLimit,
                Window = TimeSpan.FromSeconds(authWindowSeconds),
                QueueLimit = 0
            }));

    options.AddPolicy("scan", httpContext =>
        RateLimitPartition.GetFixedWindowLimiter(
            ResolveClientKey(httpContext),
            _ => new FixedWindowRateLimiterOptions
            {
                PermitLimit = scanPermitLimit,
                Window = TimeSpan.FromSeconds(authWindowSeconds),
                QueueLimit = 0
            }));
});

builder.Services.AddEndpointsApiExplorer();
builder.Services.AddSwaggerGen(options =>
{
    options.SwaggerDoc("v1", new() { Title = ProductName.Value, Version = "v1" });
    options.AddSecurityDefinition("Bearer", new OpenApiSecurityScheme
    {
        Description = "JWT Authorization header using the Bearer scheme.",
        Name = "Authorization",
        In = ParameterLocation.Header,
        Type = SecuritySchemeType.Http,
        Scheme = "bearer",
        BearerFormat = "JWT"
    });
    options.OperationFilter<FeatureAndPermissionOperationFilter>();
});

var origins = builder.Configuration.GetSection("Cors:Origins").Get<string[]>() ?? [];
builder.Services.AddCors(options =>
{
    options.AddPolicy("Frontend", policy =>
        policy.WithOrigins(origins).AllowAnyHeader().AllowAnyMethod().AllowCredentials());
});

var app = builder.Build();

StartupSecurityValidator.ValidateStartupConfiguration(app.Configuration, app.Environment);
await InitializeDatabaseAsync(app.Services);
await LogSmsStartupSummaryAsync(app.Services, app.Logger);

app.UseExceptionHandler();
app.UseMiddleware<SecurityHeadersMiddleware>();
app.UseSerilogRequestLogging(options =>
{
    options.MessageTemplate = "HTTP {RequestMethod} {RequestPath}{RedactedQueryString} responded {StatusCode} in {Elapsed:0.0000} ms";
    options.EnrichDiagnosticContext = (diagnosticContext, httpContext) =>
    {
        diagnosticContext.Set("RedactedQueryString", PiiRedactor.RedactQueryString(httpContext.Request.QueryString.Value));
    };
});

if (app.Environment.IsDevelopment())
{
    app.UseSwagger();
    app.UseSwaggerUI();
}

app.UseCors("Frontend");
app.UseResponseCaching();
UseBrandingFiles(app);
app.UseRateLimiter();
app.UseMiddleware<CookieCsrfMiddleware>();
app.UseMiddleware<InputSanitizationMiddleware>();
app.UseAuthentication();
app.UseMiddleware<TenantResolutionMiddleware>();
app.UseMiddleware<BranchScopeMiddleware>();
app.UseMiddleware<SubscriptionGuardMiddleware>();
app.UseAuthorization();

app.MapHealthChecks("/health");

app.MapGet("/api/info", (HttpContext httpContext) =>
    {
        httpContext.Response.Headers.CacheControl = "public, max-age=60, must-revalidate";
        return Results.Ok(new { product = ProductName.Value });
    })
    .WithName("GetProductInfo")
    .WithTags("System")
    .WithOpenApi();

app.MapAuthEndpoints();
app.MapSuperAdminEndpoints();
app.MapTenantEndpoints();
app.MapBrandingEndpoints();
app.MapAcademicEndpoints();
app.MapNotificationEndpoints();
app.MapBroadcastEndpoints();
app.MapAttendanceEndpoints();
app.MapFeeEndpoints();
app.MapExamEndpoints();
app.MapParentEndpoints();
app.MapOperationsEndpoints();
app.MapPublicEndpoints();
app.MapHub<EduFlow.Api.Hubs.AttendanceHub>("/hubs/attendance");

if (builder.Configuration.GetValue("Hangfire:EnableRecurringJobs", false))
{
    var recurringJobs = app.Services.GetService<IRecurringJobManager>();
    recurringJobs?.AddOrUpdate<NotificationOutboxHangfireJob>(
        NotificationOutboxHangfireJob.RecurringJobId,
        job => job.ExecuteAsync(CancellationToken.None),
        Cron.Minutely());
    recurringJobs?.AddOrUpdate<LowBalanceAlertHangfireJob>(
        LowBalanceAlertHangfireJob.RecurringJobId,
        job => job.ExecuteAsync(CancellationToken.None),
        Cron.Daily(3, 0));
    recurringJobs?.AddOrUpdate<MonthlyFeeInvoiceHangfireJob>(
        MonthlyFeeInvoiceHangfireJob.RecurringJobId,
        job => job.ExecuteAsync(null, CancellationToken.None),
        Cron.Daily(1, 0));
    recurringJobs?.AddOrUpdate<FeeReminderHangfireJob>(
        FeeReminderHangfireJob.RecurringJobId,
        job => job.ExecuteAsync(null, CancellationToken.None),
        Cron.Hourly());
    recurringJobs?.AddOrUpdate<EduFlow.Infrastructure.Exams.ExamReminderHangfireJob>(
        EduFlow.Infrastructure.Exams.ExamReminderHangfireJob.RecurringJobId,
        job => job.ExecuteAsync(null, CancellationToken.None),
        Cron.Hourly());
    recurringJobs?.AddOrUpdate<EduFlow.Infrastructure.Backups.TenantAutoBackupHangfireJob>(
        EduFlow.Infrastructure.Backups.TenantAutoBackupHangfireJob.RecurringJobId,
        job => job.ExecuteAsync(CancellationToken.None),
        Cron.Daily(20, 0));
    recurringJobs?.AddOrUpdate<EduFlow.Infrastructure.Backups.PlatformDatabaseBackupHangfireJob>(
        EduFlow.Infrastructure.Backups.PlatformDatabaseBackupHangfireJob.RecurringJobId,
        job => job.ExecuteAsync(CancellationToken.None),
        Cron.Daily(21, 0));
    recurringJobs?.AddOrUpdate<EduFlow.Infrastructure.Marketing.TrialLifecycleHangfireJob>(
        EduFlow.Infrastructure.Marketing.TrialLifecycleHangfireJob.RecurringJobId,
        job => job.ExecuteAsync(CancellationToken.None),
        Cron.Daily(2, 30));
    recurringJobs?.AddOrUpdate<EduFlow.Infrastructure.Marketing.DemoTenantResetHangfireJob>(
        EduFlow.Infrastructure.Marketing.DemoTenantResetHangfireJob.RecurringJobId,
        job => job.ExecuteAsync(CancellationToken.None),
        Cron.Daily(18, 30));
}

if (builder.Configuration.GetValue<bool>("Diagnostics:ThrowOnGet"))
{
    app.MapGet("/api/diagnostics/throw", ThrowDiagnostic)
        .WithTags("Diagnostics")
        .ExcludeFromDescription();
}

app.Run();

static IResult ThrowDiagnostic() => throw new InvalidOperationException("diagnostic failure");

static void UseBrandingFiles(WebApplication app)
{
    var storage = app.Services.GetRequiredService<IOptions<FileStorageOptions>>().Value;
    if (string.IsNullOrWhiteSpace(storage.Root))
    {
        storage.Root = Path.Combine(app.Environment.ContentRootPath, "storage");
    }
    else if (!Path.IsPathRooted(storage.Root))
    {
        storage.Root = Path.Combine(app.Environment.ContentRootPath, storage.Root);
    }

    Directory.CreateDirectory(storage.Root);
    app.UseStaticFiles(new StaticFileOptions
    {
        FileProvider = new PhysicalFileProvider(storage.Root),
        RequestPath = storage.PublicPath,
        OnPrepareResponse = ctx =>
        {
            ctx.Context.Response.Headers.TryAdd("X-Content-Type-Options", "nosniff");
            if (ctx.File.Name.EndsWith(".svg", StringComparison.OrdinalIgnoreCase))
            {
                ctx.Context.Response.Headers["Content-Security-Policy"] = "default-src 'none'; style-src 'unsafe-inline'";
            }
        }
    });
}

static async Task InitializeDatabaseAsync(IServiceProvider services)
{
    using var scope = services.CreateScope();
    var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();

    bool hasProvider;
    try
    {
        hasProvider = !string.IsNullOrWhiteSpace(db.Database.ProviderName);
    }
    catch (InvalidOperationException)
    {
        hasProvider = false;
    }

    if (!hasProvider)
    {
        return;
    }

    if (db.Database.IsSqlServer())
    {
        var connString = db.Database.GetConnectionString();
        if (string.IsNullOrWhiteSpace(connString))
        {
            return;
        }

        await db.Database.MigrateAsync();
    }
    else
    {
        await db.Database.EnsureCreatedAsync();
    }

    var seeder = scope.ServiceProvider.GetRequiredService<DbSeeder>();
    await seeder.SeedAsync();
}

static async Task LogSmsStartupSummaryAsync(IServiceProvider services, Microsoft.Extensions.Logging.ILogger logger)
{
    var metadata = services.GetService<SmsConfigurationMetadata>();
    if (metadata is not null && metadata.DeprecatedKeysDetected.Count > 0)
    {
        logger.LogWarning(
            "Deprecated SMS configuration keys detected ({Keys}). Migrate to Sms:* as the single source of truth.",
            string.Join(", ", metadata.DeprecatedKeysDetected));
    }

    using var scope = services.CreateScope();
    var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
    var hasProvider = !string.IsNullOrWhiteSpace(db.Database.ProviderName);
    if (hasProvider)
    {
        var configProvider = scope.ServiceProvider.GetRequiredService<EduFlow.Application.Notifications.ISmsGatewayConfigProvider>();
        var effective = await configProvider.GetEffectiveConfigAsync();
        logger.LogInformation(
            "SMS gateway startup configuration: Provider={Provider}, TestMode={TestMode}",
            effective.Provider,
            effective.TestMode);
    }
}

static void LoadEnvFile()
{
    var directory = new DirectoryInfo(Directory.GetCurrentDirectory());
    while (directory is not null)
    {
        var candidate = Path.Combine(directory.FullName, ".env");
        if (File.Exists(candidate))
        {
            Env.NoClobber().Load(candidate);
            return;
        }

        directory = directory.Parent;
    }
}

public partial class Program;
