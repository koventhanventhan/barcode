# Architecture

EduFlow is a multi-tenant student management SaaS built on .NET 8 Clean Architecture and React 18.

## Projects

- `EduFlow.Domain` — Entities (`BaseEntity`, `TenantEntity`, `ITenantEntity`), enums (`FeatureKey`, `LimitKey`, `RoleType`, `SubscriptionStatus`), permission catalog, and domain exceptions.
- `EduFlow.Application` — Multi-tenant interfaces (`ITenantContext`, `IAppDbContext`, `IFeatureService`, `ILimitService`, `IAuditService`, `IEmailSender`, `ITokenService`), PBKDF2 password hashing, password policy, and application services (`AuthService`, `SuperAdminService`, `TenantManagementService`, `FeatureService`, `LimitService`).
- `EduFlow.Infrastructure` — EF Core 8 `AppDbContext` (global query filters on `TenantId` and `!IsDeleted`, automatic `TenantId` stamping, soft delete, cross-tenant write protection), SQL Server migrations, `JwtTokenService`, `DbSeeder`, and providers.
- `EduFlow.Api` — Minimal API endpoints, `TenantResolutionMiddleware` (subdomain -> `X-Tenant` -> JWT claim with cross-tenant mismatch protection), `SubscriptionGuardMiddleware` (suspended -> blocked; expired/impersonating -> read-only), `RequireFeatureEndpointFilter`, permission authorization policies, rate limiting, and RFC 7807 / structured error handling.
- `web` — React 18 + TypeScript + Vite client: app shell, auth screens, branded theme (OKLCH-free HSL scale injected as CSS variables), feature and permission gates, and the super-admin console.
- Branding files are written by `IFileStorage` (`LocalFileStorage`) and served from `/files`.
- `tests` — `EduFlow.UnitTests` and `EduFlow.IntegrationTests`.
- Class sessions and attendance marks are tenant entities. Starting, stopping, or cancelling a session enqueues parent messages through the notification outbox. The scanner publishes a live board on `/hubs/attendance`.
- Parent accounts are tenant users linked to students through `ParentStudentLink`. `/api/parent/*` requires the Parent role and `ParentPortal`. Each child endpoint checks that link before returning attendance, classes, exams, invoices, or messages.
- Exams, grading schemes, marks, and published results are tenant entities. Publishing calculates batch and level ranks and enqueues `MarksReleased` once per student. Report cards are QuestPDF documents behind `PerformanceReports`.
- Academic records (levels, batches, subjects, class schedules, students, enrollments) are tenant entities. The five education levels are a shared catalog. ID cards and spreadsheets are produced in Infrastructure behind `IAcademicDocumentService`, `IBarcodeEncoder`, and `IWorkbookFile`.
