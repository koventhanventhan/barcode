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
