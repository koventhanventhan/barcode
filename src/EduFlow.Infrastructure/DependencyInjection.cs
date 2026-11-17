using EduFlow.Application.Auth;
using EduFlow.Application.Common.Interfaces;
using EduFlow.Application.Features;
using EduFlow.Application.Limits;
using EduFlow.Application.SuperAdmin;
using EduFlow.Application.Tenancy;
using EduFlow.Infrastructure.Persistence;
using EduFlow.Infrastructure.Security;
using EduFlow.Infrastructure.Services;
using EduFlow.Infrastructure.Tenancy;
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

        services.AddScoped<IAppDbContext>(sp => sp.GetRequiredService<AppDbContext>());
        services.AddScoped<ITenantContext, TenantContext>();
        services.AddScoped<IAuditService, AuditService>();
        services.AddSingleton<InMemoryEmailStore>();
        services.AddScoped<IEmailSender, InMemoryEmailSender>();
        services.AddScoped<ITokenService, JwtTokenService>();

        services.AddScoped<IFeatureService, FeatureService>();
        services.AddScoped<ILimitService, LimitService>();
        services.AddScoped<IAuthService, AuthService>();
        services.AddScoped<ISuperAdminService, SuperAdminService>();
        services.AddScoped<ITenantManagementService, TenantManagementService>();
        services.AddScoped<DbSeeder>();

        return services;
    }
}
