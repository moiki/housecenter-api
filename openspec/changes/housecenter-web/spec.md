# SDD Spec — HouseCenter Web (Frontend v2)

## Change name
`housecenter-web`

## Status
`spec`

---

## Requirements

### R1 — Auth & session management

**R1.1** The app must authenticate via `POST /auth/login`, store the access token in
memory only (never localStorage), and store the refresh token in an httpOnly cookie
or — if the API doesn't set cookies — in memory as a fallback.

**R1.2** An Axios interceptor must detect 401 responses, call `POST /auth/refresh`,
retry the original request, and redirect to `/login` only if refresh also fails.

**R1.3** On app load (`/auth/me`), the current user must be loaded and stored in a
global auth context. If the call fails, redirect to `/login`.

**R1.4** Role-based route guards must prevent access to pages the user's role cannot reach.
A Member who navigates to `/management/users` must be redirected to `/`.

**R1.5** The signup page (`/signup?token=`) must validate the invite token via
`GET /invitations/validate?token=` before showing the registration form.
An invalid or expired token shows an error state, not the form.

---

### R2 — Layout & navigation

**R2.1** Authenticated pages share a shell layout: collapsible sidebar + top bar with
notification bell and user avatar menu (theme toggle, logout).

**R2.2** Sidebar navigation items must be filtered by role. A Sponsor sees:
Dashboard, Clinics, Work Routes, Patients (read-only), Reports.
A Doctor additionally sees Consultations.
Admin/Owner additionally see Collaborators, Management.

**R2.3** Dark mode must be toggled via the top bar, persisted via `PATCH /users/me/theme`,
and applied using Tailwind `dark:` classes on `<html>` on app load based on
the `isDarkMode` field returned by `GET /auth/me`.

---

### R3 — Data fetching

**R3.1** All server state must be managed by TanStack Query.
Direct `useEffect` + `fetch` patterns are not allowed.

**R3.2** All list endpoints that support `?page=&pageSize=` must use TanStack Query
`keepPreviousData` to avoid flashing empty states between pages.

**R3.3** All mutations must invalidate or update the relevant query cache on success
(no manual refetch calls).

**R3.4** Loading states must show skeleton components (Untitled UI Skeleton), not spinners
in isolation.

---

### R4 — Forms & validation

**R4.1** All forms must use React Hook Form with Zod schemas.

**R4.2** Zod schemas must mirror the API's validation rules exactly
(e.g., clinic name max length matches `CreateClinicRequest` validator).

**R4.3** Server-side validation errors (422 ProblemDetails from FluentValidation)
must be surfaced as field-level errors, not only as a toast.

**R4.4** Destructive actions (deactivate patient, delete session, resolve consultation)
must show a confirmation dialog before the mutation fires.

---

### R5 — Patient profile

**R5.1** The patient profile page must use a tab layout:
Overview | Treatments | Sessions | Comments | Consultations | Attachments.

**R5.2** The Overview tab must call `GET /patients/{id}/full-summary` and render:
patient info, assigned collaborators, assigned doctors, treatment count,
last session date.

**R5.3** The Sessions tab must list sessions paginated, allow creating a new session
(with AttentionType + ClinicId or WorkRouteId), and allow updating session status.

**R5.4** The Consultations tab must list consultations linked to this patient,
and allow opening a new consultation thread (doctor must be in patient's assigned doctors).

**R5.5** Treatment entries must be expandable to show treatment details and comments inline.

---

### R6 — Consultations

**R6.1** The consultations page is an inbox: tabs for Open / UnderReview / Resolved.

**R6.2** A consultation detail shows the thread: original message + all replies in
chronological order, with role badge per author.

**R6.3** A Doctor replying to a consultation must trigger status → UnderReview automatically
(handled by API; the frontend just posts the message and refetches).

**R6.4** Sponsors must not see the Consultations section at all (403 from API, route guard
on frontend).

---

### R7 — Reports / Dashboard

**R7.1** The dashboard (index route `/`) renders summary cards from `GET /reports/summary`:
active patients, sessions this month, collaborators, clinics, work routes,
treatment breakdowns.

**R7.2** The Reports page (`/reports`) renders `GET /reports/sessions?from=&to=` as a
weekly trend bar chart (grouped by AttentionType). Date range picker in the header.

**R7.3** Sponsor users see the same data but collaborator names are replaced by "—"
(role projection handled by API; frontend renders whatever the API returns).

---

### R8 — Notifications

**R8.1** The top bar shows an unread count badge via `GET /notifications/unread-count`,
polling every 60 seconds.

**R8.2** Clicking the bell opens a dropdown listing recent notifications
via `GET /notifications?unreadOnly=true`.

**R8.3** Clicking a notification marks it read (`PATCH /notifications/{id}/read`)
and navigates to the relevant entity (patient, consultation, session).

---

### R9 — Attachments

**R9.1** Attachments are scoped to an entity (patient, session, consultation).
The attachment list appears as a tab or section on the relevant detail page.

**R9.2** Upload uses `POST /attachments` with `multipart/form-data`, showing upload
progress via Axios `onUploadProgress`.

**R9.3** File previews: images render inline; other types show a download link.

---

## Scenarios

### Scenario A — Login and redirect
Given: user opens `/`
When: `GET /auth/me` fails (no token)
Then: redirect to `/login`, post-login redirect back to original URL

### Scenario B — Token auto-refresh
Given: user has valid refresh token, access token expired
When: any API call returns 401
Then: interceptor calls `/auth/refresh`, retries original call, user sees no error

### Scenario C — Role-blocked route
Given: user with role Member navigates to `/management/users`
Then: redirect to `/`, no flash of the protected page

### Scenario D — Invite signup
Given: user opens `/signup?token=INVALID`
Then: validation call returns 404/422, page shows "Invalid or expired invitation" message

### Scenario E — Patient session creation
Given: member on patient profile → Sessions tab → "New Session"
When: form submitted with AttentionType=Medical and ClinicId
Then: `POST /patients/{id}/sessions` called, list refreshes, new session appears at top

### Scenario F — Doctor consultation reply
Given: doctor opens consultation → types reply → submits
Then: `POST /consultations/{id}/messages` called, thread refetches, status badge changes to "Under Review"

### Scenario G — Pagination
Given: patient list loads page 1
When: user clicks next page
Then: previous data stays visible (no blank flash) while page 2 loads

---

## DTO shapes (TypeScript)

```ts
// Auth
interface LoginRequest { email: string; password: string }
interface TokenPairResponse { accessToken: string; refreshToken: string }
interface UserResponse {
  id: string; email: string; firstName: string; lastName: string;
  isDarkMode: boolean; role: RoleResponse; isActive: boolean
}

// Pagination
interface PagedResult<T> {
  items: T[]; page: number; pageSize: number; totalCount: number;
  totalPages: number; hasNextPage: boolean; hasPreviousPage: boolean
}
interface PageQuery { page?: number; pageSize?: number }

// Clinic
interface ClinicResponse { id: string; name: string; address: string; isActive: boolean }
interface CreateClinicRequest { name: string; address: string }

// Patient
type Gender = 'Male' | 'Female' | 'Other'
type AttentionType = 'Medical' | 'EducationalReinforcement'
interface PatientResponse {
  id: string; firstName: string; lastName: string; age: number;
  gender: Gender; primaryAttentionType: AttentionType;
  clinicId: string | null; workRouteId: string | null; isActive: boolean
}
interface PatientFullSummaryResponse {
  patient: PatientResponse; collaborators: CollaboratorResponse[];
  assignedDoctors: UserResponse[]; treatments: TreatmentResponse[];
  comments: PatientCommentResponse[]
}

// Session
type SessionStatus = 'Scheduled' | 'Completed' | 'Missed'
interface AttentionSessionResponse {
  id: string; patientId: string; conductedById: string; sessionDate: string;
  attendanceType: AttentionType; status: SessionStatus;
  clinicId: string | null; workRouteId: string | null; notes: string | null
}

// Consultation
type ConsultationStatus = 'Open' | 'UnderReview' | 'Resolved'
interface ConsultationResponse {
  id: string; patientId: string; assignedDoctorId: string; openedByUserId: string;
  title: string; status: ConsultationStatus; createdAt: string; resolvedAt: string | null
}
interface ConsultationDetailResponse extends ConsultationResponse {
  messages: ConsultationMessageResponse[]
}

// Reports
interface SummaryReportResponse {
  totalPatients: number; activePatients: number; sessionsThisMonth: number;
  collaborators: number; clinics: number; workRoutes: number;
  activeTreatments: number; medicalPatients: number; educationalPatients: number
}
```

---

## Validation rules (Zod — mirrors API validators)

```ts
const loginSchema = z.object({
  email: z.string().email(),
  password: z.string().min(8)
})

const clinicSchema = z.object({
  name: z.string().min(1).max(200),
  address: z.string().min(1).max(500)
})

const patientSchema = z.object({
  firstName: z.string().min(1).max(100),
  lastName: z.string().min(1).max(100),
  age: z.number().int().min(0).max(120),
  gender: z.enum(['Male', 'Female', 'Other']),
  primaryAttentionType: z.enum(['Medical', 'EducationalReinforcement']),
  clinicId: z.string().uuid().nullable().optional(),
  workRouteId: z.string().uuid().nullable().optional()
})

const sessionSchema = z.object({
  sessionDate: z.string().date(),
  attendanceType: z.enum(['Medical', 'EducationalReinforcement']),
  clinicId: z.string().uuid().optional(),
  workRouteId: z.string().uuid().optional(),
  notes: z.string().max(2000).optional()
}).refine(d => d.clinicId || d.workRouteId, {
  message: 'Either clinic or work route must be specified'
})

const consultationSchema = z.object({
  patientId: z.string().uuid(),
  assignedDoctorId: z.string().uuid(),
  title: z.string().min(1).max(300),
  body: z.string().min(1).max(5000)
})
```
