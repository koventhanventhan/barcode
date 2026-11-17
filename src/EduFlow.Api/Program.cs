using System.Text;
using System.Text.Json.Serialization;
using System.Threading.RateLimiting;
using DotNetEnv;
using EduFlow.Api.Authorization;
using EduFlow.Api.Endpoints;
using EduFlow.Api.ExceptionHandling;
using EduFlow.Api.Middleware;
using EduFlow.Api.Swagger;
using EduFlow.Domain;
using EduFlow.Domain.Constants;
using EduFlow.Domain.Enums;
using EduFlow.Infrastructure;
using EduFlow.Infrastructure.Persistence;
using EduFlow.Infrastructure.Security;
using EduFlow.Infrastructure.Storage;
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
builder.Services.AddInfrastructure(builder.Configuration);

var jwtOptions = builder.Configuration.GetSection(JwtOptions.SectionName).Get<JwtOptions>() ?? new JwtOptions();
var signingKeyBytes = Encoding.UTF8.GetBytes(jwtOptions.SigningKey);

builder.Services
    .AddAuthentication(JwtBearerDefaults.AuthenticationScheme)
    .AddJwtBearer(options =>
    {
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
    });

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

    options.AddPolicy("auth", httpContext =>
    {
        var clientKey = httpContext.Request.Headers["X-Forwarded-For"].FirstOrDefault()
            ?? httpContext.Connection.RemoteIpAddress?.ToString()
            ?? "local";

        return RateLimitPartition.GetFixedWindowLimiter(
            clientKey,
            _ => new FixedWindowRateLimiterOptions
            {
                PermitLimit = authPermitLimit,
                Window = TimeSpan.FromSeconds(authWindowSeconds),
                QueueLimit = 0
            });
    });
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

await InitializeDatabaseAsync(app.Services);

app.UseExceptionHandler();
app.UseSerilogRequestLogging();

if (app.Environment.IsDevelopment())
{
    app.UseSwagger();
    app.UseSwaggerUI();
}

app.UseCors("Frontend");
UseBrandingFiles(app);
app.UseRateLimiter();
app.UseAuthentication();
app.UseMiddleware<TenantResolutionMiddleware>();
app.UseMiddleware<SubscriptionGuardMiddleware>();
app.UseAuthorization();

app.MapHealthChecks("/health");

app.MapGet("/api/info", () => Results.Ok(new { product = ProductName.Value }))
    .WithName("GetProductInfo")
    .WithTags("System")
    .WithOpenApi();

app.MapAuthEndpoints();
app.MapSuperAdminEndpoints();
app.MapTenantEndpoints();
app.MapBrandingEndpoints();
app.MapAcademicEndpoints();

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
        RequestPath = storage.PublicPath
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

static void LoadEnvFile()
{
    var directory = new DirectoryInfo(Directory.GetCurrentDirectory());
    while (directory is not null)
    {
        var candidate = Path.Combine(directory.FullName, ".env");
        if (File.Exists(candidate))
        {
            Env.Load(candidate);
            return;
        }

        directory = directory.Parent;
    }
}

public partial class Program;
