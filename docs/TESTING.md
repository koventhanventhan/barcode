# Testing

Backend:

```bash
dotnet test
```

- `EduFlow.UnitTests` tests password hashing, password policy, EF Core `TenantId` stamping, soft delete, cross-tenant write blocking, and global query filter registration across all `ITenantEntity` types.
- `EduFlow.IntegrationTests` runs end-to-end HTTP integration tests via `WebApplicationFactory<Program>` against `Testcontainers.MsSql` when Docker is available (such as in GitHub Actions CI) and automatically falls back to an in-memory relational SQLite connection when Docker is not installed on the local machine.
- Covers reflection-based tenant isolation across all `ITenantEntity` types, feature ON/OFF switches (`FEATURE_DISABLED`, `FEATURE_NOT_IN_PLAN`, overrides), plan limits (`LIMIT_REACHED`), subscription lifecycle (`Trial`, `Active`, `Expired` read-only, `Suspended`), Super Admin read-only impersonation, refresh-token rotation + reuse detection, lockout, password reset, user invitations, and granular permission policies.

Frontend, from `web`:

```bash
npm run lint
npm run typecheck
npm test
```
