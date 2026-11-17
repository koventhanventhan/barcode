# Testing

Backend:

```bash
dotnet test
```

- `EduFlow.UnitTests` tests password hashing, password policy, EF Core `TenantId` stamping, soft delete, cross-tenant write blocking, and global query filter registration across all `ITenantEntity` types.
- `EduFlow.IntegrationTests` runs end-to-end HTTP integration tests via `WebApplicationFactory<Program>` against `Testcontainers.MsSql` when Docker is available (such as in GitHub Actions CI) and automatically falls back to an in-memory relational SQLite connection when Docker is not installed on the local machine.
- Covers reflection-based tenant isolation across all `ITenantEntity` types, feature ON/OFF switches (`FEATURE_DISABLED`, `FEATURE_NOT_IN_PLAN`, overrides), plan limits (`LIMIT_REACHED`), subscription lifecycle (`Trial`, `Active`, `Expired` read-only, `Suspended`), Super Admin read-only impersonation, refresh-token rotation + reuse detection, lockout, password reset, user invitations, granular permission policies, tenant-scoped branding (including logo upload), academic plan limits, student import validation, per-tenant student-code uniqueness, barcode PDF gating, class-session lifecycle, duplicate scans, absent finalization, attendance switch-off paths, and SignalR hub authentication.

Frontend, from `web`:

```bash
npm run lint
npm run typecheck
npm test
```

Vitest covers the brand color scale (WCAG AA contrast), `DataTable` sorting, filtering, pagination, column visibility, and CSV export, `FeatureGate`, and role route guards.
