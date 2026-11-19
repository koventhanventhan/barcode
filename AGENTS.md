# AGENTS.md — Admino (multi-tenant school/tuition SaaS)

> Read by Antigravity and Cursor. Full spec: `PROJECT_SPEC.md` (open it only when a phase is named).

## Product
Cloud multi-tenant SaaS for Sri Lankan schools and tuition institutes: students, barcode attendance, fees (PayHere), exams, automatic parent SMS/email/WhatsApp, parent portal, backups. Sold per institute (tenant) on plans.
The brand name is a single constant (`ProductName` in backend config, `web/src/config/brand.ts`). Never hardcode it elsewhere.
Full spec is in `PROJECT_SPEC.md`. Open it only when the user names a phase; do not read the whole file for ordinary tasks.

## Stack (fixed, do not substitute)
- Backend: C# .NET 8 Web API, Clean Architecture (Domain / Application / Infrastructure / Api), EF Core 8 + SQL Server, FluentValidation, Serilog, Hangfire (SQL Server), SignalR.
- Frontend: React 18 + TypeScript + Vite, Tailwind, shadcn/ui, TanStack Query/Table, React Hook Form + Zod, lucide-react.
- Tests: xUnit + Testcontainers (backend), Vitest + RTL (frontend), Playwright (e2e).

## Non-negotiable rules
1. Multi-tenancy: every tenant-owned table has `TenantId`, protected by an EF global query filter; TenantId is stamped in SaveChanges. Never bypass filters except in explicit, audited Super Admin services. Every new tenant entity needs an isolation test.
2. Feature switches: effective availability = plan entitlement AND tenant ON/OFF switch. Enforce on the server (`[RequireFeature]`), in background jobs (at enqueue AND at send time), and hide in the UI. OFF must really stop the feature.
3. Plans and limits are data (seeded, editable by Super Admin). Never write `if (plan == "Pro")`; use the feature and limit services.
4. UI text is English only (no i18n library, no language switcher). SMS/email message templates stay multi-language (en/ta/si).
5. External providers (SMS, email, WhatsApp, payments, file storage) sit behind interfaces with a Fake implementation for dev/tests.
6. Money is `decimal`, dates are stored in UTC, business logic uses `Asia/Colombo`.

## Design
Calm, professional admin UI built on design tokens (CSS variables). One brand colour scale generated from the tenant primary colour. No gradients, glassmorphism, emoji icons or placeholder text. Check 360px and 1440px widths.

## Working rules
- Edit files directly. Never paste whole files in chat. Keep replies under 15 lines.
- Do not rewrite files that already work. Read only what you need.
- No TODO/mock code in delivered features.
- Write tests with the code. Before finishing run: `dotnet build`, `dotnet test`, and in `web/`: `npm run lint`, `npm run typecheck`, `npm test`. Fix until green.
- If something is ambiguous, pick the most reasonable option, add one line to `docs/DECISIONS.md`, and continue.

## Security and production safety

### Secrets and configuration
- Never commit secrets, API keys, passwords, tokens or real phone numbers. Real values come from environment variables or user-secrets. `.env.example` has placeholders only.
- No hardcoded default secrets in code (JWT signing key, super admin password, SMS key). Outside Development/Testing the app must fail fast at startup when a required secret is missing, too short, or equal to a known dev default.
- Do not seed default or demo credentials outside Development, unless `Demo:Enabled=true`.
- `AllowedHosts` must never be `*` in Production.
- SMS: one config section (`Sms:*`). `TestMode` defaults to true in Development and on first deploy; real parents are messaged only when it is explicitly false. API keys are write-only in the UI and never logged or returned by any API.

### Requests and data
- Validate every input (FluentValidation / Zod). Never trust client-sent TenantId, role, branch or price.
- Authorisation on every endpoint: permission policy plus `[RequireFeature]` where relevant. Parents may only access their own linked children.
- Webhooks (PayHere etc.): verify the signature, validate amount and order, process idempotently.
- File uploads: allow-list type and size, check magic bytes, sanitise SVG, resize images server-side.
- Rate-limit auth, public and scan endpoints. Public endpoints return the minimum data (no tenant ids, no existence leaks).
- Logs and audit entries must not contain passwords, tokens, API keys or full phone numbers (use the PII redactor).
- Every security-relevant rule above needs a test.

## Backend conventions (src/**, tests/**)

- Respect layers: Domain has no dependencies; Application holds use cases and interfaces; Infrastructure implements them (EF Core, providers); Api only wires HTTP.
- Controllers/endpoints stay thin; put logic in Application services. Return RFC 7807 problem details with stable `code` values (`FEATURE_DISABLED`, `FEATURE_NOT_IN_PLAN`, `LIMIT_REACHED`, ...).
- EF Core: SQL Server, `AsNoTracking` for reads, paging on every list endpoint, indexes starting with `TenantId` for hot queries, migrations named `PhaseX_Description`. Avoid N+1 queries.
- Background work uses the outbox + Hangfire with retry and idempotency keys. Re-check the feature switch at send time.
- Add a migration for every model change; never edit an applied migration.
- Tests: unit tests for pure rules, integration tests (Testcontainers) for endpoints. Every new tenant entity gets a tenant-isolation test; every gated endpoint gets a feature ON/OFF test.

## Frontend conventions (web/src/**)

- Use the existing design tokens, shadcn components and shared components (DataTable, PageHeader, EmptyState, FeatureGate, PermissionGate). Do not create new one-off styles or colours; use CSS variables so tenant branding works.
- Every list page: loading skeleton, empty state with an action, error state, pagination, sorting/filters.
- Forms: React Hook Form + Zod, inline validation, disabled submit while saving, success/error toasts, confirm dialog for destructive actions.
- Data fetching with TanStack Query; invalidate related queries after mutations. Handle `FEATURE_DISABLED`, `FEATURE_NOT_IN_PLAN` and `LIMIT_REACHED` errors with clear messages (upgrade link where relevant).
- Never store secrets in the frontend. Never trust UI hiding as security (the API enforces it).
- Mobile-first for scanner, parent portal and teacher attendance; admin tables become card lists on small screens. Keyboard accessible, visible focus, WCAG AA contrast.
- English only UI text; no language switcher.
- Add or update Vitest tests for new components and logic.
