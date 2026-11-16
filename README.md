# EduFlow

Multi-tenant student management for Sri Lankan schools and tuition institutes.

## Prerequisites

- .NET 8 SDK
- Node.js 22
- Docker (for the full stack and the SQL Server integration test)

## Run the API and web app

```bash
cp .env.example .env
dotnet run --project src/EduFlow.Api
```

```bash
cd web
npm ci
npm run dev
```

Web: http://localhost:5173  
API health: http://localhost:8080/health  
Swagger: http://localhost:8080/swagger

## Run everything in Docker

```bash
cp .env.example .env
docker compose up --build
```

Web: http://localhost:5173  
API: http://localhost:8080

## Tests

```bash
dotnet test
cd web && npm run lint && npm run typecheck && npm test
```

See `docs/TESTING.md` and `PROJECT_SPEC.md`.
