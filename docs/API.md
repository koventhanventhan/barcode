# API

## System & Public
| Method | Path | Auth | Description |
|---|---|---|---|
| GET | `/health` | Public | Process liveness |
| GET | `/api/info` | Public | Product metadata |
| GET | `/api/plans` | Public | Active subscription plans with features and limits |
| GET | `/api/public/branding` | Public | Institute or platform branding. Uses host subdomain or `X-Tenant`. |

## Authentication (`/api/auth`)
| Method | Path | Auth | Description |
|---|---|---|---|
| POST | `/api/auth/login` | Public | Authenticate user (issues 15m JWT + httpOnly refresh cookie) |
| POST | `/api/auth/refresh` | Public | Rotate refresh token and issue new JWT (with reuse detection) |
| POST | `/api/auth/logout` | Public | Revoke refresh token and clear cookie |
| POST | `/api/auth/change-password` | Bearer | Change current user's password |
| POST | `/api/auth/forgot-password` | Public | Issue password reset email token |
| POST | `/api/auth/reset-password` | Public | Reset password with token |
| POST | `/api/auth/invite` | `users.manage` | Invite staff, teacher, or admin user |
| POST | `/api/auth/accept-invite` | Public | Accept invitation and set password |
| GET | `/api/auth/me` | Bearer | Current user and effective permissions |

## Super Admin (`/api/super-admin`)
Requires `SuperAdmin` role:
- `GET/POST /api/super-admin/tenants`, `GET/PUT/DELETE /api/super-admin/tenants/{id}`
- `GET/POST /api/super-admin/plans`, `GET/PUT/DELETE /api/super-admin/plans/{id}`
- `POST /api/super-admin/tenants/{id}/subscription/{assign-plan|activate|suspend|extend}`
- `GET/PUT/DELETE /api/super-admin/tenants/{id}/feature-overrides/{featureKey}`
- `POST /api/super-admin/tenants/{id}/impersonate` (read-only audited token)
- `GET /api/super-admin/audit-logs`

## Institute Tenant Endpoints
- `GET /api/subscription` — Subscription status, features, and plan limit usage
- `GET /api/settings/features`, `PUT /api/settings/features/{featureKey}` (`settings.manage` to change)
- `GET/PUT /api/settings/branding`, `POST /api/settings/branding/reset`, `POST /api/settings/branding/assets` (`settings.manage` to change)
- `GET/PUT /api/super-admin/branding`, `POST /api/super-admin/branding/reset`, `POST /api/super-admin/branding/assets`
- `GET/POST/PUT/DELETE /api/branches` (`branches.view`, `branches.manage`)
- `GET /api/permissions`, `GET/POST/PUT/DELETE /api/roles` (`roles.view`, `roles.manage`)
- `GET/POST/DELETE /api/users`, `PUT /api/users/{id}/role` (`users.view`, `users.manage`)
- `GET /api/parent/portal` (`[RequireFeature(FeatureKey.ParentPortal)]`)
- `GET /api/payments/online/status` (`[RequireFeature(FeatureKey.OnlinePayments)]`)

## Academic core
- `GET /api/education-levels`, `PUT /api/education-levels/selection` (`academics.view` / `academics.manage`)
- `GET/POST/PUT/DELETE /api/batches`, `/api/subjects`, `/api/class-schedules` (`academics.view` / `academics.manage`)
- `GET /api/class-schedules/teachers`
- `GET/POST/PUT/DELETE /api/batch-groups` (`[RequireFeature(FeatureKey.BatchGroups)]`)
- `GET/POST/PUT/DELETE /api/students`, `POST /api/students/import`, `POST /api/students/{id}/photo` (`students.view` / `students.edit`)
- `GET /api/students/import-template`
- `GET /api/students/export?format=csv|xlsx|pdf` (`[RequireFeature(FeatureKey.DataExport)]`)
- `GET /api/students/{id}/id-card`, `POST /api/students/id-cards`, `POST /api/students/labels` (`[RequireFeature(FeatureKey.BarcodeGenerator)]`)
- `GET /api/staff/role-templates`, `POST /api/staff` (`users.view` / `users.manage`)
- `PUT /api/users/{id}/status` (`users.manage`)

## Notifications (`/api/notifications`)
- `GET /api/notifications/settings` — Workspace with quiet hours, reminder schedule, SMS quota, allowed channels, message type switches, and templates
- `PUT /api/notifications/settings/config` (`settings.manage`) — Update quiet hours, reminder timing, and over-quota SMS policy
- `PUT /api/notifications/settings/types/{messageType}` (`settings.manage`) — Toggle per-message-type switch and SMS/Email/WhatsApp channels
- `GET /api/notifications/templates`, `PUT /api/notifications/templates/{messageType}/{language}`, `POST /api/notifications/templates/{messageType}/{language}/reset` (`settings.manage`)
- `POST /api/notifications/preview` — Render template with variables and calculate SMS segments (`GSM-7` vs `UCS-2`)
- `GET /api/notifications/quota` — Monthly SMS segment quota, remaining segments, extra segments, and 80%/100% warning flags
- `GET /api/notifications/messages` — Filterable message log (`Queued/Sent/Delivered/Failed/Skipped`)
- `POST /api/notifications/messages/{id}/retry` (`settings.manage`) — Reset and retry a failed or skipped notification
- `POST /api/notifications/test-send`, `POST /api/notifications/dispatch` (`settings.manage`)

## Attendance
- `GET/PUT /api/attendance/policy` (`attendance.mark` / `settings.manage`)
- `GET/POST /api/attendance/sessions`, `POST /api/attendance/sessions/{id}/start|stop|cancel` (`attendance.mark`)
- `POST /api/attendance/scan`, `GET /api/attendance/register`, `POST /api/attendance/sessions/{id}/marks`, `POST .../undo` (`attendance.mark`)
- `GET /api/attendance/sessions/{id}/board`, SignalR `/hubs/attendance` event `boardUpdated`
- `GET /api/attendance/reports` (`reports.view`)
- `GET /api/attendance/students/{studentId}` (`students.view`)
- `POST /api/attendance/join` (`[RequireFeature(FeatureKey.OnlineAttendance)]`, tenant header, no staff login)

