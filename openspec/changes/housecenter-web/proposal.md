# SDD Proposal — HouseCenter Web (Frontend v2)

## Change name
`housecenter-web`

## Status
`implemented` (2026-07-02 — T1–T15 complete, verified wire-level (curl) against the live API.
Deviations from this proposal: UI kit migrated from Untitled UI to MUI v9 (frontend PR #1);
API dev port is 5080, not 5000 (macOS AirPlay squats 5000); unknown routes redirect to `/`
instead of a dedicated 404 page. Pending: real README.md (still Vite template default) and
browser click-through verification of the newest UI.)

---

## Problem

The legacy `house-mui-v1` frontend is a React 18 CRA app that:

- Talks to a **GraphQL** backend (TypeScript/Express) that no longer exists
- Uses **Apollo Client** — entirely wrong layer for a REST API
- Has no TypeScript — no type safety, no IntelliSense for API contracts
- Uses **MUI v5 + Semantic UI + Froala** simultaneously — 3 competing design systems
- State management is `useReducer` + raw Context — no persistence, no cache, no optimistic UI
- No route protection per role — `allowedRoles` array exists but is never enforced
- No pagination — all lists load everything at once
- Missing entire modules: WorkRoutes (no page), Collaborators (no page), Invitations (no page),
  Sessions, Consultations, Reports, Notifications — none exist

The new HouseCenter .NET REST API is complete and production-ready. It needs a frontend
that actually matches its surface area and data model.

---

## Proposed change

A new React 20 + TypeScript + Vite + pnpm project (`housecenter-web`) built from scratch,
consuming all endpoints of the HouseCenter REST API via TanStack Query.

**UI Kit:** Untitled UI for React (commercial Tailwind-based component library).
Consistent, professional design without multiple competing libraries.

---

## Tech stack

| Concern | Choice | Why |
|---|---|---|
| Bundler | Vite 6 | Fast HMR, native ESM, no CRA bloat |
| Language | TypeScript 5.x strict | API contracts enforced at compile time |
| Package manager | pnpm | Faster installs, disk-efficient |
| UI | Untitled UI for React | One design system, Tailwind-based, no MUI/Semantic conflict |
| Routing | React Router v7 | File-based routing, loader pattern, nested layouts |
| Server state | TanStack Query v5 | Cache, background refetch, pagination, optimistic updates |
| Forms | React Hook Form + Zod | Schema-driven validation, mirrors API DTOs |
| HTTP client | Axios with interceptors | Auto-attach Bearer, auto-refresh on 401, typed responses |
| Auth | JWT in memory (access) + httpOnly cookie (refresh) | Secure token storage |
| Dark mode | Tailwind `dark:` classes | User preference from API `PATCH /users/me/theme` |

---

## API surface consumed

### Auth
| Method | Endpoint | Used for |
|---|---|---|
| POST | `/auth/login` | Login form |
| POST | `/auth/refresh` | Interceptor auto-refresh |
| GET | `/auth/me` | Bootstrap current user on app load |
| POST | `/auth/signup` | Invite-based registration page |
| POST | `/auth/password/request` | Forgot password |
| POST | `/auth/password/reset` | Reset password with token |

### Users + Roles
| Method | Endpoint | Used for |
|---|---|---|
| GET | `/users` | User management table |
| GET | `/users/{id}` | User detail/edit |
| PUT | `/users/{id}` | Update user |
| DELETE | `/users/{id}` | Deactivate user |
| PUT | `/users/{id}/roles` | Role assignment (Owner only) |
| PATCH | `/users/me/theme` | Dark mode toggle |
| GET | `/roles` | Role selector in invite/user forms |

### Invitations
| Method | Endpoint | Used for |
|---|---|---|
| POST | `/invitations` | Invite new user (Owner/Admin) |
| POST | `/invitations/{id}/resend` | Resend invite |
| DELETE | `/invitations/{id}` | Revoke invite |
| GET | `/invitations/validate?token=` | Validate invite token before signup |

### Clinics
| Method | Endpoint | Used for |
|---|---|---|
| GET | `/api/v1/clinics` | Clinic list |
| GET | `/api/v1/clinics/{id}` | Clinic detail |
| POST | `/api/v1/clinics` | Create clinic (Admin+) |
| PUT | `/api/v1/clinics/{id}` | Edit clinic |
| DELETE | `/api/v1/clinics/{id}` | Deactivate clinic |

### Work Routes
| Method | Endpoint | Used for |
|---|---|---|
| GET | `/work-routes` | Routes list |
| GET | `/work-routes/{id}` | Route detail with destinations |
| POST | `/work-routes` | Create route |
| PUT | `/work-routes/{id}` | Edit route + destinations |
| DELETE | `/work-routes/{id}` | Deactivate route |

### Collaborators
| Method | Endpoint | Used for |
|---|---|---|
| GET | `/collaborators` | Staff directory |
| GET | `/collaborators/{id}` | Collaborator profile + positions |
| POST | `/collaborators` | Register collaborator |
| PUT | `/collaborators/{id}` | Edit collaborator |
| DELETE | `/collaborators/{id}` | Deactivate collaborator |

### Patients (full surface)
| Method | Endpoint | Used for |
|---|---|---|
| GET | `/patients?page=&pageSize=` | Paginated patient list |
| GET | `/patients/{id}` | Patient detail |
| GET | `/patients/{id}/full-summary` | Patient profile page (parallel load) |
| POST | `/patients` | New patient |
| PUT | `/patients/{id}` | Edit patient |
| DELETE | `/patients/{id}` | Deactivate patient |
| POST | `/patients/{id}/collaborators/{userId}` | Assign collaborator |
| POST | `/patients/{id}/doctors/{userId}` | Assign remote doctor |
| DELETE | `/patients/{id}/doctors/{userId}` | Unassign doctor |
| GET | `/patients/{id}/treatments` | Treatment list tab |
| POST | `/patients/{id}/treatments` | New treatment |
| PUT | `/patients/{id}/treatments/{treatmentId}` | Edit treatment |
| PATCH | `/patients/{id}/treatments/{treatmentId}/status` | Change treatment status |
| DELETE | `/patients/{id}/treatments/{treatmentId}` | Delete treatment |
| POST | `/treatments/{id}/details` | Add treatment detail |
| PUT | `/treatments/{id}/details/{detailId}` | Edit detail |
| DELETE | `/treatments/{id}/details/{detailId}` | Delete detail |
| POST | `/treatments/{id}/comments` | Add treatment comment |
| PUT | `/treatments/{id}/comments/{commentId}` | Edit comment |
| DELETE | `/treatments/{id}/comments/{commentId}` | Delete comment |
| POST | `/patients/{id}/comments` | Patient-level comment |
| PUT | `/patients/{id}/comments/{commentId}` | Edit patient comment |
| DELETE | `/patients/{id}/comments/{commentId}` | Delete patient comment |

### Sessions
| Method | Endpoint | Used for |
|---|---|---|
| POST | `/patients/{id}/sessions` | Log attention session |
| GET | `/patients/{id}/sessions` | Session history tab |
| PATCH | `/patients/{id}/sessions/{sessionId}/status` | Update session status |
| DELETE | `/patients/{id}/sessions/{sessionId}` | Delete session |

### Consultations
| Method | Endpoint | Used for |
|---|---|---|
| POST | `/consultations` | Open consultation thread |
| GET | `/consultations?page=&status=` | Consultation inbox |
| GET | `/consultations/{id}` | Thread view with messages |
| POST | `/consultations/{id}/messages` | Reply in thread |
| PATCH | `/consultations/{id}/status` | Resolve / change status |

### Reports
| Method | Endpoint | Used for |
|---|---|---|
| GET | `/reports/summary` | Dashboard overview cards |
| GET | `/reports/clinics/{id}` | Per-clinic report |
| GET | `/reports/work-routes/{id}` | Per-route report |
| GET | `/reports/sessions?from=&to=` | Session trend chart |

### Notifications
| Method | Endpoint | Used for |
|---|---|---|
| GET | `/notifications?unreadOnly=` | Notification bell dropdown |
| GET | `/notifications/unread-count` | Badge count |
| PATCH | `/notifications/{id}/read` | Mark read |
| POST | `/notifications/read-all` | Mark all read |
| POST | `/notifications/push-subscriptions` | Register push token |
| DELETE | `/notifications/push-subscriptions/{token}` | Unregister push |

### Attachments
| Method | Endpoint | Used for |
|---|---|---|
| POST | `/attachments` | Upload file |
| GET | `/attachments?ownerType=&ownerId=` | List attachments for entity |
| GET | `/attachments/{id}` | Download/view |
| DELETE | `/attachments/{id}` | Delete attachment |

---

## Pages / routes

| Route | Component | Roles |
|---|---|---|
| `/login` | LoginPage | Public |
| `/signup?token=` | SignupPage | Public (requires valid invite token) |
| `/forgot-password` | ForgotPasswordPage | Public |
| `/reset-password?token=` | ResetPasswordPage | Public |
| `/` | DashboardPage (reports/summary) | All roles |
| `/clinics` | ClinicsPage | All roles |
| `/clinics/:id` | ClinicDetailPage | All roles |
| `/work-routes` | WorkRoutesPage | All roles |
| `/work-routes/:id` | WorkRouteDetailPage | All roles |
| `/patients` | PatientsPage (paginated) | All roles |
| `/patients/:id` | PatientProfilePage (tabs) | All roles |
| `/collaborators` | CollaboratorsPage | Admin+ |
| `/consultations` | ConsultationsPage (inbox) | Doctor / Member / Admin / Owner |
| `/consultations/:id` | ConsultationDetailPage | Doctor / Member / Admin / Owner |
| `/reports` | ReportsPage | All roles (projected by role) |
| `/management/users` | UsersPage | Admin+ |
| `/management/invitations` | InvitationsPage | Admin+ |
| `/settings` | SettingsPage (theme, profile) | All roles |

---

## What this replaces from legacy

| Legacy capability | Status in new app |
|---|---|
| Login (GraphQL) | Replaced with `POST /auth/login` |
| `GET /me` on boot | Replaced with `GET /auth/me` |
| Theme toggle (localStorage) | Replaced with `PATCH /users/me/theme` + Tailwind dark: |
| Clinic CRUD | Feature-complete, same surface |
| Patient list | Paginated (was all-at-once) |
| Patient profile (tabs) | Added Sessions tab, Consultations tab |
| Treatment CRUD | Same + status transitions |
| Work Routes | Actually built now (was missing) |
| Collaborators | Actually built now (was missing) |
| User management | Same + role assignment |
| Invitations | Actually built now (was missing) |
| Sessions | New — didn't exist in legacy |
| Consultations | New — didn't exist in legacy |
| Reports / dashboard | Real aggregated data (was static home) |
| Notifications | New — didn't exist in legacy |
| Attachments | New — didn't exist in legacy |
| Apollo / GraphQL | Removed entirely — REST + TanStack Query |
| MUI + Semantic UI | Removed — Untitled UI only |

---

## Out of scope

- Mobile app / React Native
- PDF/Excel export from reports
- Real-time WebSocket — polling for notifications only
- i18n — English only for now
- E2E tests (Playwright/Cypress) — unit + integration only for hooks/utils

---

## Why this is the right move

The API is complete. Building on house-mui-v1 would mean:
1. Gutting Apollo → REST migration across every hook
2. Adding TypeScript retroactively to ~100 JS files
3. Replacing MUI + Semantic with one system
4. Adding 7 missing pages from scratch anyway

Starting fresh with Vite + TS + Untitled UI takes the same effort as the migration,
produces cleaner code, and avoids carrying forward technical debt.
