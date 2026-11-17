# Architecture

EduFlow is a multi-tenant student management SaaS built on .NET 8 Clean Architecture and React 18.

## Projects

- `EduFlow.Domain` — Entities (`BaseEntity`, `TenantEntity`, `ITenantEntity`), enums (`FeatureKey`, `LimitKey`, `RoleType`, `SubscriptionStatus`), permission catalog, and domain exceptions.
- `EduFlow.Application` — Multi-tenant interfaces (`ITenantContext`, `IAppDbContext`, `IFeatureService`, `ILimitService`, `IAuditService`, `IEmailSender`, `ITokenService`, `IAntivirusScanner`), PBKDF2 password hashing, password policy, security helpers (`FileUploadValidator`, `InputSanitizer`, `PiiRedactor`), and application services (`AuthService`, `SuperAdminService`, `TenantManagementService`, `FeatureService`, `LimitService`, etc.).
- `EduFlow.Infrastructure` — EF Core 8 `AppDbContext` (global query filters on `TenantId` and `!IsDeleted`, automatic `TenantId` stamping, soft delete, cross-tenant write protection, composite `(TenantId, ...)` indexes), SQL Server migrations, `JwtTokenService`, `SignatureAntivirusScanner`, `DbSeeder`, and providers.
- `EduFlow.Api` — Minimal API endpoints, middleware pipeline (`GlobalExceptionHandler` -> `SecurityHeadersMiddleware` -> `SerilogRequestLogging` with PII query redaction -> `Cors` -> `ResponseCaching` -> `UseRateLimiter` (`auth`/`public`/`scan`) -> `CookieCsrfMiddleware` -> `InputSanitizationMiddleware` -> `Authentication` -> `TenantResolutionMiddleware` -> `SubscriptionGuardMiddleware` -> `Authorization`), `RequireFeatureEndpointFilter`, permission authorization policies, and RFC 7807 / structured error handling.
- `web` — React 18 + TypeScript + Vite client: app shell, auth screens, branded theme (HSL scale injected as CSS variables), feature and permission gates, parent PWA, and the super-admin console.
- Branding files and uploads are written via `IFileStorage` (`LocalFileStorage`) and served from `/files`.
- `tests` — `EduFlow.UnitTests`, `EduFlow.IntegrationTests`, and `tests/load/scan-k6.js`.
- `e2e` — Playwright end-to-end suite validating signup, ID card PDFs, barcode attendance, PayHere sandbox + receipts, ParentPortal kill-switch, live login branding, tenant isolation, and plan limit enforcement.

## Domain Flows

- **Attendance & Live Board**: Class sessions and attendance marks are tenant entities. Starting, stopping, or cancelling a session enqueues parent messages through the notification outbox. The scanner publishes a live board on `/hubs/attendance`. Hot paths (`GetRegisterAsync`, `MapSessionsAsync`, `MarkByCodeAsync`) batch DB reads and hit composite `(TenantId, ...)` indexes.
- **Parent Portal**: Parent accounts are tenant users linked to students through `ParentStudentLink`. `/api/parent/*` requires the `Parent` role and `ParentPortal`. Each child endpoint checks that link before returning attendance, classes, exams, invoices, or messages.
- **Exams**: Exams, grading schemes, marks, and published results are tenant entities. Publishing calculates batch and level ranks and enqueues `MarksReleased` once per student. Report cards are QuestPDF documents behind `PerformanceReports`.
- **Academics**: Education levels (global catalog), batches, subjects, class schedules, students, and enrollments are tenant entities. ID cards and spreadsheets are produced in Infrastructure behind `IAcademicDocumentService`, `IBarcodeEncoder`, and `IWorkbookFile`.
- **Marketing & Onboarding**: The public site calls `/api/plans`, `/api/public/signup`, and `/api/public/demo-requests`. Demo login issues `read_only` + `demo_session`. `TrialLifecycleHangfireJob` emails expiring/expired trials; `DemoTenantResetHangfireJob` rebuilds the `demo` institute nightly.
- **Backups & Enterprise Modules**: Institute backups (`AutoBackup`), report cleanup (`ReportCleanup`), timetable (`Timetable`), and staff attendance/leave (`StaffLeave`) are enforced server-side via `[RequireFeature]` and `IFeatureService`. Platform backups (`PlatformBackupRecord`), announcements (`PlatformAnnouncement`), and maintenance mode (`PlatformSetting`) are managed by `SuperAdmin`.

## OWASP Top 10 Security Checklist (Phase 11)

| Category | Control Implemented |
|---|---|
| **A01 Broken Access Control** | Global EF `TenantId` + `!IsDeleted` query filters, cross-tenant write blocking in `AppDbContext`, `[RequireFeature]`, granular permission policies, parent-child link validation |
| **A02 Cryptographic Failures** | PBKDF2-SHA256 password hashing, ASP.NET Data Protection encryption for PayHere merchant secrets, TLS enforced at Caddy proxy with HSTS |
| **A03 Injection** | Parameterized EF Core queries everywhere; `InputSanitizationMiddleware` + `InputSanitizer` rejecting `<script>`, `javascript:`, event handlers at API boundary |
| **A04 Insecure Design** | Server-side `IFeatureService` + `ILimitService`, refresh token reuse detection revoking family tokens, idempotent webhooks & invoices |
| **A05 Security Misconfiguration** | Kestrel `Server` header disabled, `SecurityHeadersMiddleware` (`CSP`, `X-Frame-Options: DENY`, `X-Content-Type-Options: nosniff`, `Referrer-Policy`, `Permissions-Policy`), Swagger disabled in Production |
| **A06 Vulnerable Components** | NuGet (`dotnet list --vulnerable`) and npm (`npm audit`) audited; vulnerable transitive packages pinned/resolved; CI audit gates |
| **A07 Auth & Session Failures** | 15-minute JWT + rotating `HttpOnly`, `SameSite=Strict` refresh cookie; `CookieCsrfMiddleware` origin/referer validation; lockout & per-IP rate limiting (`auth`, `public`, `scan`) |
| **A08 Software & Data Integrity** | Constant-time `md5sig` webhook verification for PayHere; `FileUploadValidator` magic-byte checks + `SignatureAntivirusScanner` (ELF/MZ/EICAR/PHP/script) |
| **A09 Logging & Monitoring** | `AuditService` + `PiiRedactor` masking passwords/tokens/secrets in audit details and HTTP query strings; Serilog rolling logs + optional Seq |
| **A10 SSRF & File Abuse** | Strict upload MIME/extension/size limits, filename sanitization, no arbitrary user-controlled outbound URL fetching |
