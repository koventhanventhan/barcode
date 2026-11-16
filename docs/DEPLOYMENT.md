# Deployment

Local stack (SQL Server Express, API, web):

```bash
cp .env.example .env
docker compose up --build
```

- Web: http://localhost:5173
- API: http://localhost:8080
- Swagger: http://localhost:8080/swagger
- SQL Server: localhost,1433 (password in `.env`)

Production hosting, TLS, and backups are Phase 11. A single VPS needs at least 4 GB RAM because of SQL Server.
