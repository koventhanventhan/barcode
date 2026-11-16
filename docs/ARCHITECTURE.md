# Architecture

EduFlow is a multi-tenant school SaaS. Phase 0 is the repository skeleton only.

## Projects

- `EduFlow.Domain` — entities and constants. No infrastructure references.
- `EduFlow.Application` — use cases. References Domain.
- `EduFlow.Infrastructure` — EF Core, storage, and providers. References Application.
- `EduFlow.Api` — HTTP host, Swagger, health checks, exception handling. References Application and Infrastructure.
- `web` — React client.
- `tests` — xUnit unit tests and WebApplicationFactory / Testcontainers integration tests.

`ProductName.Value` in the domain and `ProductName` in `web/src/config/brand.ts` are the only product-name constants.

## Request pipeline

Unhandled exceptions become RFC 7807 problem details. `/health` reports process liveness. Tenant resolution, auth, and persistence start in Phase 1.
