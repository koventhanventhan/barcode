# Deployment

## 1. Local Development Stack

Local stack (`sqlserver`, `api`, `web`):

```bash
cp .env.example .env
docker compose up --build
```

- Web: http://localhost:5173
- API: http://localhost:8080
- Swagger (Development only): http://localhost:8080/swagger
- Health check: http://localhost:8080/health
- SQL Server: `localhost,1433`

---

## 2. Single-VPS Production Deployment (`docker-compose.prod.yml`)

### Server Requirements
- **Minimum 4 GB RAM** (2.5 GB allocated to SQL Server container + 1.5 GB for `.NET 8 API`, Caddy, Nginx, and OS).
- **2 vCPU**, 40 GB SSD, Ubuntu 22.04 LTS / 24.04 LTS with Docker Engine + Docker Compose v2.
- Open firewall ports `80/tcp` and `443/tcp` only (SQL Server port `1433` is NOT published externally).

### Wildcard DNS Setup (`*.eduflow.lk`)
Configure DNS A/AAAA records at your DNS provider (e.g., Cloudflare, Route 53, or Namecheap):
- `@` (`eduflow.lk`) -> `<VPS_PUBLIC_IP>`
- `*` (`*.eduflow.lk`) -> `<VPS_PUBLIC_IP>`

> **Note on Wildcard TLS in Caddy**: The included [Caddyfile](../docker/Caddyfile) automatically provisions Let's Encrypt TLS certificates on demand. For wildcard `*.eduflow.lk` SAN certificates without HTTP-01 per subdomain, build Caddy with the Cloudflare/Route53 DNS plugin or add Caddy `on_demand_tls` with an internal ask endpoint.

### Fresh-Clone Production Launch Steps
```bash
git clone <repo-url> eduflow && cd eduflow
cp .env.example .env
```
Edit `.env` for production (never commit `.env`):
```env
DOMAIN=eduflow.lk
ACME_EMAIL=ops@eduflow.lk
MSSQL_PID=Express
MSSQL_SA_PASSWORD=<strong-32-char-password>
MSSQL_MEMORY_LIMIT_MB=2048
Jwt__SigningKey=<random-64-char-secret>
SUPERADMIN_EMAIL=superadmin@eduflow.lk
SUPERADMIN_PASSWORD=<strong-superadmin-password>
```
Start the production stack:
```bash
docker compose -f docker-compose.prod.yml up -d --build
```
Verify:
```bash
docker compose -f docker-compose.prod.yml ps
curl -fsS https://eduflow.lk/health
```
The API applies EF Core migrations (`MigrateAsync`) and runs `DbSeeder.SeedAsync()` automatically on startup.

---

## 3. Environment Variable Reference

| Variable | Required | Default | Purpose |
|---|---|---|---|
| `DOMAIN` | Yes (prod) | `eduflow.lk` | Apex domain for CORS, Caddy TLS, and wildcard subdomains |
| `ACME_EMAIL` | Yes (prod) | `ops@eduflow.lk` | Let's Encrypt registration email for Caddy |
| `MSSQL_SA_PASSWORD` | Yes | — | SQL Server `sa` password (strong complexity required) |
| `MSSQL_PID` | No | `Express` | SQL Server edition (`Express`, `Developer`, `Standard`) |
| `MSSQL_MEMORY_LIMIT_MB` | No | `2048` | SQL Server internal memory cap in MB |
| `ConnectionStrings__Default` | Yes (managed SQL) | Container SQL Server | Overrides the connection string for managed SQL Server / Azure SQL |
| `Jwt__SigningKey` | Yes | Dev key | HMAC-SHA256 JWT signing secret (at least 32 bytes) |
| `SUPERADMIN_EMAIL` | Yes | `superadmin@eduflow.lk` | Seeded Super Admin login email |
| `SUPERADMIN_PASSWORD` | Yes | `SuperAdmin@123!` | Seeded Super Admin password |
| `Cors__Origins__0` | Yes | `https://${DOMAIN}` | Allowed frontend CORS origin |
| `App__PublicWebUrl` | Yes | `https://${DOMAIN}` | Base URL used in password reset, invite, and billing links |
| `Hangfire__Storage` | No | `SqlServer` | `SqlServer` in production; `InMemory` for local dev |
| `Hangfire__EnableRecurringJobs` | No | `true` | Enables outbox, monthly fee, reminder, backup, and demo reset jobs |
| `FileStorage__Root` | No | `/app/storage` | Directory or mounted volume for uploads and local backups |
| `Notifications__Sms__Provider` | No | `Fake` / `NotifyLk` | `Fake` or `NotifyLk` |
| `Notifications__Sms__NotifyLk__UserId` | Conditional | — | Notify.lk User ID |
| `Notifications__Sms__NotifyLk__ApiKey` | Conditional | — | Notify.lk API Key |
| `Notifications__Sms__NotifyLk__SenderId` | No | `EduFlow` | Registered SMS Sender ID |
| `Notifications__Email__Provider` | No | `Fake` / `Smtp` | `Fake`, `Smtp`, or `Brevo` |
| `Notifications__Email__FromEmail` | No | `noreply@eduflow.lk` | Outgoing From address |
| `Notifications__Email__Smtp__Host` | Conditional | — | SMTP host (when `Provider=Smtp`) |
| `Notifications__Email__Smtp__Port` | No | `587` | SMTP port |
| `Notifications__Email__Smtp__Username` | Conditional | — | SMTP username |
| `Notifications__Email__Smtp__Password` | Conditional | — | SMTP password |
| `Notifications__Email__Brevo__ApiKey` | Conditional | — | Brevo transactional API key (when `Provider=Brevo`) |
| `Notifications__WhatsApp__Provider` | No | `Fake` | `Fake` or `CloudApi` |
| `Notifications__WhatsApp__CloudApi__PhoneNumberId` | Conditional | — | Meta Cloud API Phone Number ID |
| `Notifications__WhatsApp__CloudApi__AccessToken` | Conditional | — | Meta Cloud API Bearer token |
| `Monitoring__SeqUrl` | No | Empty | Optional Seq ingestion endpoint for structured Serilog logs |

---

## 4. Monitoring & Observability

- **Health Check**: `GET /health` returns HTTP `200 Healthy` when the API process is ready.
- **Structured Logging (Serilog)**:
  - Production profile ([appsettings.Production.json](../src/EduFlow.Api/appsettings.Production.json)) writes rolling daily JSON/text logs to `/app/logs/eduflow-.log` (30-day retention, 100 MB rollover) and console stdout.
  - Sensitive query parameters (`token`, `email`, `phone`, `code`, `secret`, etc.) are automatically masked by `PiiRedactor.RedactQueryString`.
  - Set `Monitoring__SeqUrl=http://seq:5341` to ship structured events to Seq.
- **Background Jobs**: Recurring jobs (notification outbox, fee invoices, reminders, backups, trial expiry, demo reset) run in Hangfire persisted to SQL Server (`Hangfire__Storage=SqlServer`). Failed jobs and tenant quotas are visible in the Super Admin **Platform Dashboard** (`/admin/dashboard`).

---

## 5. Zero-Downtime Update Steps

1. Pull the latest release and inspect changes:
   ```bash
   git pull origin main
   ```
2. Build new images without interrupting running traffic:
   ```bash
   docker compose -f docker-compose.prod.yml build api web
   ```
3. Run a pre-upgrade platform backup from `/admin/dashboard` (or `POST /api/super-admin/platform-backups/run`).
4. Roll out API first (backward-compatible schema migrations run automatically on boot) then Web:
   ```bash
   docker compose -f docker-compose.prod.yml up -d --no-deps api
   # Wait until API healthcheck passes
   docker compose -f docker-compose.prod.yml up -d --no-deps web
   ```
5. Verify `/health` and smoke-check `/api/info`.

---

## 6. Backup & Restore Runbook

### Platform-Level Database Backups (Super Admin)
- **Schedule**: Nightly Hangfire job (`platform-db-backup`, 21:00 UTC) or on-demand via `POST /api/super-admin/platform-backups/run` (Platform Operations dashboard).
- **Retention**: Configurable between **14 and 30 days** (`PUT /api/super-admin/platform-backups/retention`). Expired archives are pruned from `IFileStorage` automatically.
- **SQL Server Restore Procedure**:
  1. Enable platform maintenance mode (`PUT /api/super-admin/maintenance` with `{ "isMaintenanceMode": true }`) so tenant traffic receives `503 MAINTENANCE_MODE`.
  2. Download the `.bak.zip` archive from `/api/super-admin/platform-backups/{id}/download` (or object storage `platform-backups/`) and extract `eduflow.bak` into `/var/opt/mssql/backup/`.
  3. Restore the SQL Server database:
     ```sql
     ALTER DATABASE [EduFlow] SET SINGLE_USER WITH ROLLBACK IMMEDIATE;
     RESTORE DATABASE [EduFlow] FROM DISK = N'/var/opt/mssql/backup/eduflow.bak' WITH REPLACE;
     ALTER DATABASE [EduFlow] SET MULTI_USER;
     ```
  4. Verify `/health` and Super Admin dashboard counts, then disable maintenance mode (`{ "isMaintenanceMode": false }`).

### Institute-Level Backups & Support Restore
- **Institute Backup (`AutoBackup`)**: Exports tenant-scoped JSON (`data/snapshot.json`), CSV tables (`csv/*.csv`), and uploaded file manifest (`files_manifest.json`) to `backups/{tenantId}/` in `IFileStorage`, enforces tenant retention (`RetentionDays`, `MaxBackupsToKeep`), and emails `BackupResultAdmin` to the institute admin.
- **Support Restore (`SuperAdmin` only)**:
  1. Run dry-run validation first: `POST /api/super-admin/tenants/{tenantId}/backups/{backupId}/restore` with `{ "dryRun": true }`.
  2. Verify `isValid: true`, tenant isolation match, and `restorableCounts`.
  3. Execute live restore with `{ "dryRun": false }` to restore missing or soft-deleted records for that institute.

---

## 7. Azure Upgrade Path

When scaling beyond a single VPS (100+ active institutes):
1. **Compute**: Deploy `docker/Dockerfile.api` and `docker/Dockerfile.web` on **Azure Container Apps** (or Azure App Service Linux) behind **Azure Front Door** configured for `*.eduflow.lk` wildcard TLS and WebSockets (`/hubs/*`).
2. **Database**: Provision **Azure SQL Database** (General Purpose / Serverless) and supply its connection string via `ConnectionStrings__Default` (remove the `sqlserver` container from production compose).
3. **Storage & Data Protection**: Store uploaded assets/backups in **Cloudflare R2** / Azure Blob (`IFileStorage`), and persist ASP.NET Data Protection keys in a shared volume or Azure Key Vault + Blob so multi-replica PayHere secret decryption and cookie encryption stay consistent across API instances.
4. **Logs & Secrets**: Ship Serilog to Azure Monitor / Seq and inject secrets via Azure Key Vault references.
