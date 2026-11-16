# API

Phase 0 endpoints (no auth yet):

| Method | Path | Response |
|---|---|---|
| GET | `/health` | `200` text `Healthy` |
| GET | `/api/info` | `200` `{ "product": "EduFlow" }` |

Swagger UI is available at `/swagger` when `ASPNETCORE_ENVIRONMENT` is Development.

Unhandled errors return `application/problem+json` with `type` `https://datatracker.ietf.org/doc/html/rfc7807`.
