# Architecture

EduFlow is a multi-tenant student management SaaS built on .NET 8 Clean Architecture and React 18.

## Projects

- `EduFlow.Domain` — Entities (`BaseEntity`, `TenantEntity`, `ITenantEntity`), enums (`FeatureKey`, `LimitKey`, `RoleType`, `SubscriptionStatus`), permission catalog, and domain exceptions.
- `EduFlow.Application` — Multi-tenant interfaces (`ITenantContext`, `IAppDbContext`, `IFeatureService`, `ILimitService`, `IAuditService`, `IEmailSender`, `ITokenService`), PBKDF2 password hashing, password policy, and application services (`AuthService`, `SuperAdminService`, `TenantManagementService`, `FeatureService`, `LimitService`).
- `EduFlow.Infrastructure` — EF Core 8 `AppDbContext` (global query filters on `TenantId` and `!IsDeleted`, automatic `TenantId` stamping, soft delete, cross-tenant write protection), SQL Server migrations, `JwtTokenService`, `DbSeeder`, and providers.
- `EduFlow.Api` — Minimal API endpoints, `TenantResolutionMiddleware` (subdomain -> `X-Tenant` -> JWT claim with cross-tenant mismatch protection), `SubscriptionGuardMiddleware` (suspended -> blocked; expired/impersonating -> read-only), `RequireFeatureEndpointFilter`, permission authorization policies, rate limiting, and RFC 7807 / structured error handling.
- `web` — React 18 + TypeScript + Vite client.
- `tests` — `EduFlow.UnitTests` and `EduFlow.IntegrationTests`.
