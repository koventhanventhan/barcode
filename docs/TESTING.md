# Testing

## 1. Backend Unit & Integration Tests

```bash
dotnet test EduFlow.sln --configuration Release
```

- **UnitTests (`tests/EduFlow.UnitTests`)**: Password hashing, password policy, EF Core `TenantId` stamping, soft delete, cross-tenant write blocking, global query filter registration across all `ITenantEntity` types, magic-byte file upload + AV signature validation, input XSS/script payload detection, PII query/audit log redaction.
- **IntegrationTests (`tests/EduFlow.IntegrationTests`)**: End-to-end HTTP integration tests via `WebApplicationFactory<Program>` against `Testcontainers.MsSql` when Docker is available (such as in GitHub Actions CI), automatically falling back to an isolated relational SQLite connection when Docker is unavailable locally. Covers:
  - Reflection-based cross-tenant isolation across all `ITenantEntity` types
  - Feature ON/OFF switches (`FEATURE_DISABLED`, `FEATURE_NOT_IN_PLAN`, overrides), plan limits (`LIMIT_REACHED`), subscription lifecycle (`Trial`, `Active`, `Expired` read-only, `Suspended`)
  - Auth flows, refresh-token rotation + reuse detection, lockout, CSRF origin enforcement, security headers, cache control headers, input sanitization, malicious upload rejection, pagination headers, hot-query composite index performance (`< 300 ms` scan latency)
  - Full module coverage: branding, academics, attendance & SignalR hub auth, notifications & SMS segments/quiet hours, fees & PayHere webhook signatures, exams & ranking with ties, parent portal scoping, backups/restore, timetable clashes, marketing/demo reset.

### Backend Coverage Gate (`>= 70%` on `EduFlow.Application`)
```bash
dotnet test EduFlow.sln -c Release --collect:"XPlat Code Coverage" --results-directory TestResults
```

---

## 2. Frontend Lint, Typecheck, Unit & Component Tests

From `web/`:

```bash
npm run lint
npm run typecheck
npm test
```

### Frontend Coverage Gate (`>= 60%` lines & statements)
```bash
npx vitest run --coverage
```

Vitest + React Testing Library covers:
- WCAG AA brand color-scale generation and contrast pairs (`lib/color.test.ts`)
- `DataTable` sorting, filtering, pagination, column visibility, CSV export (`DataTable.test.tsx`)
- `FeatureGate`, `PermissionGate`, `ConfirmDialog`, `EmptyState`, `StatCard`, UI primitives
- API client auth token refresh retry and session helpers (`lib/api-session.test.ts`)
- Route role guards, SMS segment calculation, PayHere payment UI, CSV student import, parent model, help articles.

---

## 3. Playwright End-to-End Tests (`e2e/`)

From `e2e/` (requires a built `EduFlow.Api` Release binary, `npm ci`, and Chrome/Playwright Chromium):

```bash
dotnet build src/EduFlow.Api/EduFlow.Api.csproj -c Release
cd e2e
npm ci
npx playwright install chromium   # once on CI / machines without local Chrome
npm test
```

`e2e/playwright.config.ts` starts an isolated SQLite-backed `EduFlow.Api` (`http://127.0.0.1:5089`) and Vite dev server (`http://127.0.0.1:5179`) automatically and tests all 7 core end-to-end flows:
1. Self-service trial signup (`Growth` plan) -> add student -> print barcode ID card PDF (`%PDF-`)
2. Start class session -> scan `S000001` -> stop class -> verify `AttendancePresent` and `AttendanceAbsent` entries in Message Log
3. Open pending fee invoice -> initiate PayHere checkout -> complete sandbox payment -> download official receipt PDF
4. Create parent account -> toggle `ParentPortal` OFF (parent login blocked) -> toggle ON -> parent dashboard loads on 360px mobile viewport
5. Update tenant branding primary color -> verify `--primary` CSS variable updates on login page in fresh context
6. Verify tenant isolation in the Students UI across separate institutes
7. Reach Starter plan subject limit (`5`) -> verify plan limit error message shown in UI

---

## 4. Load Testing (`k6`)

Target: `/api/attendance/scan` **p95 latency < 300 ms** at steady barcode-scanner concurrency.

```bash
k6 run -e BASE_URL=http://localhost:8080 -e TENANT=demo tests/load/scan-k6.js
```
