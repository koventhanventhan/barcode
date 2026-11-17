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
