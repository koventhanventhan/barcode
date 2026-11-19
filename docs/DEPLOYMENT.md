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

### Generic Ubuntu 24.04 VPS (Azure, Hetzner, DigitalOcean, Vultr)
Any cloud provider running **Ubuntu 24.04 LTS** works identically:
- **Sizing**: **2 vCPU**, **4 GB RAM** minimum (2.5 GB allocated to SQL Server + 1.5 GB for `.NET 8 API`, Caddy, Nginx, and OS), **40+ GB SSD** (e.g., Azure B2s, Hetzner CX22/CPX21, DigitalOcean 4 GB Droplet, or Vultr 4 GB).
- **Firewall / Security Group (Provider Firewall + `ufw`)**:
  - Allow inbound `80/tcp` and `443/tcp` (optionally restrict to [Cloudflare IP ranges](https://www.cloudflare.com/ips/)).
  - Restrict `22/tcp` (SSH) to the **admin IP only**.
  - Do **not** open SQL Server port `1433/tcp` externally (internal Docker network only).
- **Runtime**: Install Docker Engine and Docker Compose v2 (`docker compose`).
- **Provider VM Backups**: Enable automated VM/volume snapshots at your VPS provider (e.g., Azure Backup, Hetzner Backups, DigitalOcean Backups, or Vultr Auto Backups).

### Wildcard DNS & Cloudflare Origin Certificate Setup (`*.admino.lk`)
1. In Cloudflare DNS for your domain, create proxied (**Orange Cloud**) `A`/`AAAA` records pointing to `<VPS_PUBLIC_IP>`:
   - `@` (`admino.lk`) -> `<VPS_PUBLIC_IP>` (Proxied)
   - `*` (`*.admino.lk`) -> `<VPS_PUBLIC_IP>` (Proxied)
2. In Cloudflare **SSL/TLS -> Overview**, set encryption mode to **Full (strict)**.
3. In Cloudflare **SSL/TLS -> Origin Server**, create an **Origin Certificate** covering `admino.lk` and `*.admino.lk` (PEM format, 15-year validity).
4. On the VPS inside the cloned repo, save the certificate and private key under `certs/` (gitignored; mounted read-only into Caddy at `/certs`):
   ```bash
   mkdir -p certs
   # Paste Origin Certificate into certs/origin.pem and Private Key into certs/origin.key
   chmod 644 certs/origin.pem
   chmod 600 certs/origin.key
   ```
   [Caddyfile](../docker/Caddyfile) uses `tls /certs/origin.pem /certs/origin.key` (no ACME), trusts Cloudflare's IPv4/IPv6 ranges (`trusted_proxies static`), and forwards `{client_ip}` to the API (`UseForwardedHeaders` trusts only Docker networks).

### Fresh-Clone Production Launch Steps
```bash
git clone <repo-url> admino && cd admino
cp .env.example .env
```
Edit `.env` for production (never commit `.env`):
```env
DOMAIN=admino.lk
MSSQL_PID=Express
MSSQL_SA_PASSWORD=<strong-32-char-password>
MSSQL_MEMORY_LIMIT_MB=2048
Jwt__SigningKey=<random-64-char-secret>
SUPERADMIN_EMAIL=superadmin@admino.lk
SUPERADMIN_PASSWORD=<strong-superadmin-password>
```
Start the production stack:
```bash
docker compose -f docker-compose.prod.yml up -d --build
```
Verify:
```bash
docker compose -f docker-compose.prod.yml ps
DOMAIN=admino.lk bash scripts/smoke.sh
```
The API applies EF Core migrations (`MigrateAsync`) and runs `DbSeeder.SeedAsync()` automatically on startup.

---

## 3. Environment Variable Reference

| Variable | Required | Default | Purpose |
|---|---|---|---|
| `DOMAIN` | Yes (prod) | `admino.lk` | Apex domain for CORS, Caddy Origin TLS, and wildcard subdomains |
| `MSSQL_SA_PASSWORD` | Yes | — | SQL Server `sa` password (strong complexity required) |
| `MSSQL_PID` | No | `Express` | SQL Server edition (`Express`, `Developer`, `Standard`) |
| `MSSQL_MEMORY_LIMIT_MB` | No | `2048` | SQL Server internal memory cap in MB |
| `ConnectionStrings__Default` | Yes (managed SQL) | Container SQL Server | Overrides the connection string for managed SQL Server / Azure SQL |
| `Jwt__SigningKey` | Yes | Dev key | HMAC-SHA256 JWT signing secret (at least 32 bytes) |
| `SUPERADMIN_EMAIL` | Yes | `superadmin@admino.lk` | Seeded Super Admin login email |
| `SUPERADMIN_PASSWORD` | Yes | `SuperAdmin@123!` | Seeded Super Admin password |
| `Cors__Origins__0` | Yes | `https://${DOMAIN}` | Allowed frontend CORS origin |
| `App__PublicWebUrl` | Yes | `https://${DOMAIN}` | Base URL used in password reset, invite, and billing links |
| `AllowedHosts` | Yes (prod) | `admino.lk;*.admino.lk` | Semicolon-delimited allowed HTTP `Host` headers (must not be `*` in Production) |
| `Demo__Enabled` | No | `false` | Enables seeding the `demo` tenant outside Development (`false` in Production by default) |
| `Hangfire__Storage` | No | `SqlServer` | `SqlServer` in production; `InMemory` for local dev |
| `Hangfire__EnableRecurringJobs` | No | `true` | Enables outbox, monthly fee, reminder, backup, and demo reset jobs |
| `FileStorage__Root` | No | `/app/storage` | Directory or mounted volume for uploads and local backups |
| `Sms__Provider` | No | `Fake` / `NotifyLk` | `Fake` or `NotifyLk` (`Sms:*` is the single source of truth; legacy `Notifications__Sms__*` keys emit a startup warning) |
| `Sms__UserId` | Conditional | — | Notify.lk User ID |
| `Sms__ApiKey` | Conditional | — | Notify.lk API Key |
| `Sms__SenderId` | No | `Admino` | Approved Transactional SMS Sender ID |
| `Sms__PromotionalSenderId` | No | — | Optional Promotional Sender ID for Broadcast SMS |
| `Sms__BaseUrl` | No | `https://app.notify.lk/api/v1` | Notify.lk API v1 base URL |
| `Sms__TestMode` | No | `true` | Defaults to `true` on first deploy in `docker-compose.prod.yml` and in Development; set `false` after verifying live SMS |
| `Sms__TestAllowList` | No | — | Comma-separated Sri Lankan mobile numbers allowed when `Sms__TestMode=true` |
| `Notifications__Email__Provider` | No | `Fake` / `Smtp` | `Fake`, `Smtp`, or `Brevo` |
| `Notifications__Email__FromEmail` | No | `noreply@admino.lk` | Outgoing From address |
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
  - Production profile ([appsettings.Production.json](../src/EduFlow.Api/appsettings.Production.json)) writes rolling daily JSON/text logs to `/app/logs/admino-.log` (30-day retention, 100 MB rollover) and console stdout.
  - Sensitive query parameters (`token`, `email`, `phone`, `code`, `secret`, etc.) are automatically masked by `PiiRedactor.RedactQueryString`.
  - Set `Monitoring__SeqUrl=http://seq:5341` to ship structured events to Seq.
- **Background Jobs**: Recurring jobs (notification outbox, fee invoices, reminders, backups, trial expiry, demo reset) run in Hangfire persisted to SQL Server (`Hangfire__Storage=SqlServer`). Failed jobs and tenant quotas are visible in the Super Admin **Platform Dashboard** (`/admin/dashboard`).

---

## 5. Zero-Downtime Update Steps

1. Pull the latest release and inspect changes:
   ```bash
   git pull origin master
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
5. Verify endpoints with `DOMAIN=admino.lk bash scripts/smoke.sh`.

---

## 6. Backup & Restore Runbook

> **Important — Volume Storage & Off-Host Protection**: Tenant uploads and platform/institute backup archives currently live on the VPS Docker volume (`admino_storage` via `LocalFileStorage` at `/app/storage`, alongside `sqlserver_data` and `sqlserver_backups`). Until an S3/Azure Blob `IFileStorage` adapter is added, these files are **not** replicated to external object storage and are protected **only by provider-level VM/volume backups** (e.g., Azure Backup, Hetzner/DigitalOcean/Vultr backups) or manual off-host archive copies.

### Platform-Level Database Backups (Super Admin)
- **Schedule**: Nightly Hangfire job (`platform-db-backup`, 21:00 UTC) or on-demand via `POST /api/super-admin/platform-backups/run` (Platform Operations dashboard).
- **Retention**: Configurable between **14 and 30 days** (`PUT /api/super-admin/platform-backups/retention`). Expired archives are pruned from `IFileStorage` automatically.
- **SQL Server Restore Procedure**:
  1. Enable platform maintenance mode (`PUT /api/super-admin/maintenance` with `{ "isMaintenanceMode": true }`) so tenant traffic receives `503 MAINTENANCE_MODE`.
  2. Download the `.bak.zip` archive from `/api/super-admin/platform-backups/{id}/download` (or object storage `platform-backups/`) and extract `admino.bak` into `/var/opt/mssql/backup/`.
  3. Restore the SQL Server database:
     ```sql
     ALTER DATABASE [Admino] SET SINGLE_USER WITH ROLLBACK IMMEDIATE;
     RESTORE DATABASE [Admino] FROM DISK = N'/var/opt/mssql/backup/admino.bak' WITH REPLACE;
     ALTER DATABASE [Admino] SET MULTI_USER;
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
1. **Compute**: Deploy `docker/Dockerfile.api` and `docker/Dockerfile.web` on **Azure Container Apps** (or Azure App Service Linux) behind **Azure Front Door** configured for `*.admino.lk` wildcard TLS and WebSockets (`/hubs/*`).
2. **Database**: Provision **Azure SQL Database** (General Purpose / Serverless) and supply its connection string via `ConnectionStrings__Default` (remove the `sqlserver` container from production compose).
3. **Storage & Data Protection**: Store uploaded assets/backups in **Cloudflare R2** / Azure Blob (`IFileStorage`), and persist ASP.NET Data Protection keys in a shared volume or Azure Key Vault + Blob so multi-replica PayHere secret decryption and cookie encryption stay consistent across API instances.
4. **Logs & Secrets**: Ship Serilog to Azure Monitor / Seq and inject secrets via Azure Key Vault references.
