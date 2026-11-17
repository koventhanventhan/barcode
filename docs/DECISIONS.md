# Decisions

- Phase 0 `/health` is process liveness. A SQL health check arrives with EF Core.
- The API listens on HTTP. TLS terminates at the reverse proxy in Phase 11.
- Swagger UI is enabled only when the host environment is Development.
- React is pinned to 18.3. The Vite scaffold defaults to 19.
- FluentAssertions stays on 7.2.2 so the test suite does not depend on the v8 commercial license.
- The Testcontainers SQL sample is skipped when Docker is not installed, so `dotnet test` stays green on a machine without Docker.
- `Diagnostics:ThrowOnGet` registers `/api/diagnostics/throw` only when set. It is off in config and used by the problem-details test.
- Tailwind is v4 via `@tailwindcss/vite`. shadcn/ui is initialized for Vite with Radix and CSS variables in `web/src/styles/globals.css`.
- Tailwind 4 is loaded by `@tailwindcss/vite`. shadcn/ui is initialized with CSS variables in `web/src/styles/globals.css` and the `Button` component.
- `ApiFactory` uses `Testcontainers.MsSql` when Docker is running and falls back to an in-memory SQLite relational connection when Docker is unavailable so all integration tests execute locally and in CI.
- `User` and `RefreshToken` inherit `TenantEntity` with `TenantId = Guid.Empty` reserved for platform `SuperAdmin` accounts; tenant queries require `CurrentTenantId.HasValue && e.TenantId == CurrentTenantId.Value`.
- `BrandingProfile` is not an `ITenantEntity`. `TenantId = Guid.Empty` is the platform default, and every read filters `TenantId` so institutes cannot see each other.
- `RemoveProductBranding` is an Enterprise plan feature. Hiding the product name is allowed only when that feature is effectively on.
- Dashboard charts in Phase 2 use plan limits and feature availability. Attendance and fee figures arrive with those modules.
- The browser keeps the access token in memory and refreshes it with the httpOnly cookie. A super-admin impersonation token stays in memory; the previous platform token is kept in `sessionStorage` so the operator can return.
- Branding images are cropped in the browser and stored as PNG, JPEG, or WebP. The API does not accept SVG.
- Five education levels are a global catalog. A new institute enables the first N allowed by its plan. `StudentCode` is `S` plus six digits, unique per tenant, and is the Code128 value. Bulk ID cards are 8-up on A4 and capped at 500. Import saves valid rows and returns a row report; the file is rejected when the valid rows would pass the student limit. Staff templates reuse a named role. Class times are minutes from midnight, Monday = 1. Student photo bytes count toward cloud storage.
- A class session is one schedule on one date: Scheduled, Started, then Stopped or Cancelled. A scan without a session uses the single started class the student is enrolled in. Present or late is sent on the first scan; students still unmarked when the class stops become absent when that setting is on. A repeat scan returns the original Colombo time. Online attendance is a join code on a started class and requires `OnlineAttendance`. The live board is a SignalR group per tenant.
- Notification outbox items check both category `FeatureKey` and per-message-type switches at enqueue time and again at dispatch time (`Skipped:FeatureOff`). Non-payment SMS during quiet hours (21:00–06:00 `Asia/Colombo` by default) are deferred to the end of quiet hours (`Deferred:QuietHours`), while `PaymentReceivedParent` and `PaymentReceivedAdmin` send immediately. Over-quota SMS default to `AllowAndRecordExtra` (`ExtraSegments` tracked on `NotificationOutbox`), with an option to `Block` (`Skipped:QuotaExceeded`). `FakeSmsProvider` and `NotifyLkSmsProvider` implement `ISmsProvider`.
- Monthly fee invoice generation keys idempotency by `(TenantId, StudentId, FeeStructureId, BillingPeriod)` for non-voided invoices. Sequential receipt numbers (`RCP-000001`) are allocated per tenant under a per-tenant lock with unique index retry. PayHere merchant secrets are encrypted at rest via ASP.NET Core Data Protection API (`EduFlow.PayHere.MerchantSecret.v1`) and webhooks verify `md5sig` in constant time before idempotent settlement.
- Exam results use competition ranking: tied percentages share a rank and the next rank is skipped. An absent paper scores 0 in the total and is left out of that subject rank. Level rank groups published exams that share a series key and education level. Reminders fire `days + hours` before the paper (tenant default, overridable per exam). `{marks}` is `86% (A)` or `results published`. Report-card PDFs require `PerformanceReports`.


