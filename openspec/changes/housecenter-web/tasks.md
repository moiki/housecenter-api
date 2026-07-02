# SDD Tasks — HouseCenter Web (Frontend v2)

## Tasks must be done in order — each depends on the previous.
## Acceptance criterion per task: `pnpm build` must pass with zero TypeScript errors.

> **Implementation notes (2026-07-02)** — all tasks complete except the README (see T15).
> Deviations from the original plan:
> - **UI kit**: Untitled UI was installed initially but the whole app was migrated to
>   **MUI v9** (PR #1, `feat/mui-migration`); the dead Untitled UI kit was removed.
>   References to Untitled UI components below (DataTable, EmptyState, collapse) were
>   delivered with their MUI equivalents.
> - **API port**: the API now runs on `http://localhost:5080` (AirPlay squats port 5000).
>   `.env.example` still says 5000 — stale.
> - **404 page**: delivered as a catch-all `*` route redirecting to `/`, not a dedicated page.
> - Verification was wire-level (curl + `pnpm build`); no browser click-through yet.

---

### T1 — Project scaffold

- [x] `pnpm create vite housecenter-web --template react-ts` in `hoh-project-net/`
- [x] Configure `tsconfig.json` for strict mode + path aliases (`@/` → `src/`)
- [x] Install core dependencies:
  ```
  pnpm add react-router-dom @tanstack/react-query axios zustand zod react-hook-form
  pnpm add @hookform/resolvers
  pnpm add -D tailwindcss @tailwindcss/forms autoprefixer
  ```
- [x] Install Untitled UI for React (follow vendor install instructions)
- [x] Configure `tailwind.config.ts` with Untitled UI preset + `darkMode: 'class'`
- [x] Create folder structure: `api/`, `types/`, `hooks/`, `schemas/`, `store/`,
  `layouts/`, `pages/`, `components/`, `lib/`
- [x] Create `.env.example` with `VITE_API_BASE_URL=http://localhost:5000`
- [x] Create `.gitignore` (node_modules, dist, .env)

**Acceptance:** `pnpm dev` starts, blank white page with no console errors.

---

### T2 — HTTP client + auth store

- [x] `src/api/client.ts` — Axios instance:
  - `baseURL` from `import.meta.env.VITE_API_BASE_URL`
  - Request interceptor: attach `Authorization: Bearer {token}` from Zustand store
  - Response interceptor: on 401, call `/auth/refresh`, retry, else logout + redirect
  - Typed `ApiError` class extracted from ProblemDetails response
- [x] `src/store/auth.store.ts` — Zustand:
  - `user: UserResponse | null`
  - `accessToken: string | null`
  - `refreshToken: string | null`
  - `setAuth(user, accessToken, refreshToken): void`
  - `logout(): void`
- [x] `src/types/auth.types.ts` — all auth DTOs (matches spec.md DTO section)
- [x] `src/types/common.types.ts` — `PagedResult<T>`, `PageQuery`, `ProblemDetails`

**Acceptance:** TypeScript compiles, no `any` types in client.ts.

---

### T3 — Auth pages + routing skeleton

- [x] `src/lib/queryClient.ts` — TanStack Query client (staleTime: 30s, retry: 1)
- [x] `src/App.tsx` — `RouterProvider` + `QueryClientProvider` + `AuthProvider`
- [x] `src/layouts/AuthLayout.tsx` — centered card layout for public pages
- [x] `src/layouts/AppLayout.tsx` — sidebar + topbar shell (sidebar content placeholder)
- [x] `src/components/guards/RequireAuth.tsx`
- [x] `src/components/guards/RequireRole.tsx`
- [x] Route tree:
  ```
  / → AppLayout (RequireAuth)
    index → DashboardPage (placeholder)
  /login → AuthLayout → LoginPage
  /signup → AuthLayout → SignupPage
  /forgot-password → AuthLayout → ForgotPasswordPage
  /reset-password → AuthLayout → ResetPasswordPage
  ```
- [x] `src/api/modules/auth.api.ts` — all auth endpoint functions
- [x] `src/hooks/auth/useMe.ts` — `GET /auth/me` on app boot
- [x] `src/pages/auth/LoginPage.tsx` — form with email + password, calls `POST /auth/login`,
  stores tokens in Zustand, navigates to `/`
- [x] `src/pages/auth/SignupPage.tsx` — reads `?token=`, validates via
  `GET /invitations/validate`, shows form only if valid
- [x] `src/pages/auth/ForgotPasswordPage.tsx` — calls `POST /auth/password/request`
- [x] `src/pages/auth/ResetPasswordPage.tsx` — reads `?token=`, calls `POST /auth/password/reset`

**Acceptance:** Can log in, token stored in memory, `/` redirects to login when not auth'd.

---

### T4 — App shell: sidebar + topbar + dark mode

- [x] `src/lib/constants.ts` — `ROLE_NAMES`, `NAV_ITEMS` array with roles filter
- [x] `src/layouts/AppLayout.tsx` — complete:
  - Sidebar renders `NAV_ITEMS` filtered by `user.role.name`
  - Active link highlight
  - Collapse/expand sidebar (Untitled UI)
  - Mobile: drawer overlay
- [x] `src/components/notifications/NotificationBell.tsx`:
  - Badge with unread count (polling 60s)
  - Dropdown on click: list of recent notifications + "Mark all read" button
- [x] Top bar: user avatar dropdown → Settings, Logout, theme toggle
- [x] Dark mode: `document.documentElement.classList` toggle on `user.isDarkMode` +
  `PATCH /users/me/theme` mutation on toggle

**Acceptance:** Sidebar renders, dark mode toggles visually + persists across refresh
(via `GET /auth/me` returning `isDarkMode: true`).

---

### T5 — Clinics module

- [x] `src/types/clinic.types.ts`
- [x] `src/schemas/clinic.schema.ts`
- [x] `src/api/modules/clinics.api.ts`
- [x] `src/hooks/clinics/` — `useClinics`, `useClinic`, `useCreateClinic`,
  `useUpdateClinic`, `useDeactivateClinic`
- [x] `src/pages/clinics/ClinicsPage.tsx` — table with Untitled UI DataTable,
  "New Clinic" button (slide-over form), row actions: Edit / Deactivate
- [x] `src/pages/clinics/ClinicDetailPage.tsx` — detail card + Edit form

**Acceptance:** Full CRUD works against running API.

---

### T6 — Work Routes module

- [x] `src/types/workroute.types.ts` (includes `DestinationPoint`)
- [x] `src/schemas/workroute.schema.ts`
- [x] `src/api/modules/workroutes.api.ts`
- [x] `src/hooks/workroutes/` — list, detail, create, update, delete
- [x] `src/pages/work-routes/WorkRoutesPage.tsx` — table + create slide-over
- [x] `src/pages/work-routes/WorkRouteDetailPage.tsx` — destination points list (add/remove)

**Acceptance:** Routes with multi-destination editing work.

---

### T7 — Collaborators module

- [x] `src/types/collaborator.types.ts` (includes `Position`)
- [x] `src/api/modules/collaborators.api.ts`
- [x] `src/hooks/collaborators/` — list, detail, create, update, delete
- [x] `src/pages/collaborators/CollaboratorsPage.tsx` — staff directory with positions

**Acceptance:** Collaborator CRUD works, visible to Admin/Owner only.

---

### T8 — Users + Invitations (Management section)

- [x] `src/types/user.types.ts`, `src/types/invitation.types.ts`
- [x] `src/api/modules/users.api.ts`, `src/api/modules/invitations.api.ts`
- [x] `src/hooks/users/`, `src/hooks/invitations/`
- [x] `src/pages/management/UsersPage.tsx`:
  - User table: name, email, role badge, status
  - Edit user slide-over
  - Deactivate action
  - Role assignment (Owner only — hide button for non-Owner)
- [x] `src/pages/management/InvitationsPage.tsx`:
  - Active invitations list
  - "Invite user" form (email + role selector)
  - Resend / Revoke actions

**Acceptance:** Invite flow works end-to-end (invite → signup page reads token).

---

### T9 — Patients list + profile shell

- [x] `src/types/patient.types.ts` (Patient, Treatment, TreatmentDetail, Comments)
- [x] `src/schemas/patient.schema.ts`
- [x] `src/api/modules/patients.api.ts`
- [x] `src/hooks/patients/` — `usePatients` (paginated), `usePatientFullSummary`
- [x] `src/pages/patients/PatientsPage.tsx`:
  - Paginated table with `keepPreviousData`
  - `PaginationBar` component
  - "New Patient" slide-over form
- [x] `src/pages/patients/PatientProfilePage.tsx`:
  - Tab layout: Overview | Treatments | Sessions | Comments | Consultations | Attachments
  - Overview tab: calls `full-summary`, renders patient info + assigned users

**Acceptance:** Patient list paginates, profile page loads summary.

---

### T10 — Treatments + comments (patient profile tabs)

- [x] Treatments tab:
  - Paginated treatment list with status badge (Active/Completed/Paused)
  - Expandable row → TreatmentDetails + TreatmentComments inline
  - Add treatment form, edit treatment, `PATCH status`, delete
  - Add/edit/delete treatment details
  - Add/edit/delete treatment comments
- [x] Comments tab (patient-level):
  - PatientComment list with type badge (Route/Medical/Simple)
  - Add/edit/delete comment
- [x] Assign collaborator action on Overview tab (`POST /patients/{id}/collaborators/{userId}`)
- [x] Assign/unassign doctor (`POST/DELETE /patients/{id}/doctors/{userId}`)

**Acceptance:** Full treatment lifecycle works from UI.

---

### T11 — Sessions (patient profile tab)

- [x] `src/types/session.types.ts`
- [x] `src/schemas/session.schema.ts`
- [x] `src/api/modules/sessions.api.ts`
- [x] `src/hooks/sessions/` — `useSessions`, `useCreateSession`,
  `useUpdateSessionStatus`, `useDeleteSession`
- [x] Sessions tab in PatientProfilePage:
  - Paginated session list with status badge + AttentionType badge
  - "New Session" form: date, AttentionType, ClinicId OR WorkRouteId (at least one required),
    notes — cross-field validation via Zod `.refine()`
  - Update status action (Scheduled → Completed/Missed)
  - Delete with confirmation dialog

**Acceptance:** Session creation validates at least one of clinic/route is selected.

---

### T12 — Consultations module

- [x] `src/types/consultation.types.ts`
- [x] `src/schemas/consultation.schema.ts`
- [x] `src/api/modules/consultations.api.ts`
- [x] `src/hooks/consultations/`
- [x] `src/pages/consultations/ConsultationsPage.tsx`:
  - Inbox tabs: Open / Under Review / Resolved
  - "New Consultation" form: patient selector, doctor selector (only assigned doctors),
    title, opening message
- [x] `src/pages/consultations/ConsultationDetailPage.tsx`:
  - Thread view: messages in order with role badges
  - Reply form at bottom
  - Status badge + Resolve button (Doctor/Admin only)
- [x] Consultations tab on PatientProfilePage: mini-list of consultations for this patient

**Acceptance:** Doctor can open, reply, resolve. Sponsor sees no consultations section.

---

### T13 — Reports + Dashboard

- [x] `src/types/report.types.ts`
- [x] `src/api/modules/reports.api.ts`
- [x] `src/hooks/reports/`
- [x] `src/pages/DashboardPage.tsx`:
  - Summary cards from `GET /reports/summary`
  - 6 stat cards: active patients, sessions this month, collaborators, clinics, routes, treatments
- [x] `src/pages/reports/ReportsPage.tsx`:
  - Date range picker header
  - Weekly sessions bar chart (Recharts or Chart.js) from `GET /reports/sessions`
  - Per-clinic summary card from `GET /reports/clinics/{id}`
  - Per-route summary card from `GET /reports/work-routes/{id}`

**Acceptance:** Dashboard shows live data. Sponsor and Admin see same page (API handles projection).

---

### T14 — Notifications + Attachments

- [x] Notifications (NotificationBell complete from T4 — this task: mark-read + navigate)
  - Clicking notification → `PATCH read` + navigate to linked entity
  - "Mark all read" button
- [x] `src/types/attachment.types.ts`
- [x] `src/api/modules/attachments.api.ts`
- [x] `src/hooks/attachments/`
- [x] Attachments tab on PatientProfilePage:
  - File list with type icon + file name + uploader + date
  - Upload button: `input[type=file]` → `POST /attachments` with `ownerType=Patient`
  - Upload progress bar via `onUploadProgress`
  - Image files: inline preview thumbnail
  - Non-image: download link
  - Delete with confirmation

**Acceptance:** File upload shows progress, image previews work, list refreshes after upload.

---

### T15 — Polish + Settings page

- [x] `src/pages/settings/SettingsPage.tsx`:
  - Profile section: display name, email (read-only)
  - Theme section: dark/light toggle (same action as topbar toggle)
- [x] 404 page for unknown routes
- [x] Empty states for all list pages (Untitled UI EmptyState component)
- [x] Error boundaries per route section
- [x] `pnpm build` — zero TypeScript errors, no unused imports
- [ ] README.md with: setup steps, env vars, how to run, API base URL config
  *(still the default Vite template README — pending)*

**Acceptance:** `pnpm build` clean. App navigates without console errors across all pages.

---

## Estimated effort by phase

| Tasks | Focus | Hours |
|---|---|---|
| T1–T3 | Foundation + auth | 4–6h |
| T4 | Shell + navigation | 3–4h |
| T5–T7 | Simple CRUD modules | 4–6h |
| T8 | Users + invitations | 3–4h |
| T9–T10 | Patient + treatments | 6–8h |
| T11–T12 | Sessions + consultations | 5–7h |
| T13 | Reports + charts | 4–5h |
| T14–T15 | Notifications, attachments, polish | 4–6h |
| **Total** | | **33–46h** |

---

## New concepts this project introduces

| Task | What you'll learn |
|---|---|
| T2 | Axios interceptors for auto-refresh — real production auth pattern |
| T2 | Zustand — minimal global state (vs Redux/Context) |
| T3 | React Router v7 loaders — data fetching tied to navigation |
| T4 | TanStack Query polling — `refetchInterval` |
| T5–T8 | TanStack Query key factory pattern |
| T9 | `keepPreviousData` for flicker-free pagination |
| T11 | Zod cross-field validation with `.refine()` |
| T12 | Role-based UI rendering (not just route guards) |
| T13 | ECharts/Recharts integration for data viz |
| T14 | Multipart upload + progress tracking |
