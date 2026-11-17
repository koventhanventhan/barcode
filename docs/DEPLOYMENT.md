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

## Backup & Restore Runbook

### 1. Platform-Level Database Backups (Super Admin)
- **Schedule**: Nightly Hangfire job (`platform-db-backup`, 21:00 UTC) or on-demand via `POST /api/super-admin/platform-backups/run` (Platform Operations dashboard).
- **Retention**: Configurable between **14 and 30 days** (`PUT /api/super-admin/platform-backups/retention`). Expired archives are deleted from `IFileStorage` automatically.
- **SQL Server Restore Procedure**:
  1. Enable platform maintenance mode (`PUT /api/super-admin/maintenance` with `{ "isMaintenanceMode": true }`) so tenant traffic receives `503 MAINTENANCE_MODE`.
  2. Download the `.bak.zip` archive from `/api/super-admin/platform-backups/{id}/download` (or S3/R2 bucket `platform-backups/`) and extract `eduflow.bak`.
  3. Restore the SQL Server database:
     ```sql
     ALTER DATABASE [EduFlow] SET SINGLE_USER WITH ROLLBACK IMMEDIATE;
     RESTORE DATABASE [EduFlow] FROM DISK = N'/var/opt/mssql/backup/eduflow.bak' WITH REPLACE;
     ALTER DATABASE [EduFlow] SET MULTI_USER;
     ```
  4. Verify `/health` and Super Admin dashboard counts, then disable maintenance mode (`{ "isMaintenanceMode": false }`).

### 2. Institute-Level Backups & Support Restore
- **Institute Backup (`AutoBackup`)**: Exports tenant-scoped JSON (`data/snapshot.json`), CSV tables (`csv/*.csv`), and uploaded file manifest (`files_manifest.json`) to `backups/{tenantId}/` in `IFileStorage`, enforces tenant retention (`RetentionDays`, `MaxBackupsToKeep`), and emails `BackupResultAdmin` to the institute admin.
- **Support Restore (`SuperAdmin` only)**:
  1. Run dry-run validation first: `POST /api/super-admin/tenants/{tenantId}/backups/{backupId}/restore` with `{ "dryRun": true }`.
  2. Verify `isValid: true`, tenant isolation match, and `restorableCounts`.
  3. Execute live restore with `{ "dryRun": false }` to restore missing or soft-deleted records for that institute.

Production hosting and TLS reverse-proxy hardening are completed in Phase 11. A single VPS needs at least 4 GB RAM because of SQL Server.
