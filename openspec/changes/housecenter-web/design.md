# SDD Design — HouseCenter Web (Frontend v2)

## Change name
`housecenter-web`

## Status
`design`

---

## Project scaffold

```
housecenter-web/           ← sits alongside HouseCenter/ in hoh-project-net/
├── index.html
├── vite.config.ts
├── tsconfig.json
├── tailwind.config.ts
├── package.json           ← pnpm
│
├── src/
│   ├── main.tsx
│   ├── App.tsx            ← router + providers
│   │
│   ├── api/               ← HTTP client + typed API functions
│   │   ├── client.ts      ← Axios instance + interceptors
│   │   └── modules/
│   │       ├── auth.api.ts
│   │       ├── clinics.api.ts
│   │       ├── patients.api.ts
│   │       ├── sessions.api.ts
│   │       ├── consultations.api.ts
│   │       ├── reports.api.ts
│   │       ├── notifications.api.ts
│   │       └── ...
│   │
│   ├── types/             ← TypeScript interfaces mirroring API DTOs
│   │   ├── auth.types.ts
│   │   ├── patient.types.ts
│   │   ├── consultation.types.ts
│   │   └── ...
│   │
│   ├── hooks/             ← TanStack Query hooks (one per resource)
│   │   ├── auth/
│   │   │   └── useMe.ts
│   │   ├── clinics/
│   │   │   ├── useClinics.ts
│   │   │   └── useCreateClinic.ts
│   │   ├── patients/
│   │   │   ├── usePatients.ts
│   │   │   ├── usePatientFullSummary.ts
│   │   │   └── ...
│   │   ├── sessions/
│   │   ├── consultations/
│   │   ├── reports/
│   │   └── notifications/
│   │
│   ├── schemas/           ← Zod schemas (one file per domain)
│   │   ├── auth.schema.ts
│   │   ├── clinic.schema.ts
│   │   ├── patient.schema.ts
│   │   ├── session.schema.ts
│   │   └── consultation.schema.ts
│   │
│   ├── store/             ← Zustand (auth context only — nothing else)
│   │   └── auth.store.ts  ← currentUser, accessToken, setUser, logout
│   │
│   ├── layouts/
│   │   ├── AuthLayout.tsx    ← login/signup/reset pages (centered card)
│   │   └── AppLayout.tsx     ← sidebar + topbar + <Outlet />
│   │
│   ├── pages/
│   │   ├── auth/
│   │   │   ├── LoginPage.tsx
│   │   │   ├── SignupPage.tsx
│   │   │   ├── ForgotPasswordPage.tsx
│   │   │   └── ResetPasswordPage.tsx
│   │   ├── DashboardPage.tsx
│   │   ├── clinics/
│   │   │   ├── ClinicsPage.tsx
│   │   │   └── ClinicDetailPage.tsx
│   │   ├── work-routes/
│   │   │   ├── WorkRoutesPage.tsx
│   │   │   └── WorkRouteDetailPage.tsx
│   │   ├── patients/
│   │   │   ├── PatientsPage.tsx
│   │   │   └── PatientProfilePage.tsx  ← tabs
│   │   ├── collaborators/
│   │   │   └── CollaboratorsPage.tsx
│   │   ├── consultations/
│   │   │   ├── ConsultationsPage.tsx
│   │   │   └── ConsultationDetailPage.tsx
│   │   ├── reports/
│   │   │   └── ReportsPage.tsx
│   │   └── management/
│   │       ├── UsersPage.tsx
│   │       └── InvitationsPage.tsx
│   │
│   ├── components/        ← shared UI components
│   │   ├── guards/
│   │   │   ├── RequireAuth.tsx      ← redirect to /login if no user
│   │   │   └── RequireRole.tsx      ← redirect to / if insufficient role
│   │   ├── notifications/
│   │   │   └── NotificationBell.tsx
│   │   └── shared/
│   │       ├── ConfirmDialog.tsx
│   │       ├── PageHeader.tsx
│   │       └── PaginationBar.tsx
│   │
│   └── lib/
│       ├── queryClient.ts   ← TanStack Query client config
│       └── constants.ts     ← role names, policy names
```

---

## Auth architecture

```
Access token  → Zustand store (memory only, cleared on tab close)
Refresh token → sent by API in response body → stored in memory alongside access

On 401:
  Axios interceptor → POST /auth/refresh with refreshToken from store
  → success: update accessToken in store, retry original request
  → failure: logout() → clear store → navigate('/login')

On app load:
  GET /auth/me with stored accessToken
  → success: hydrate store with user + role
  → failure: if no accessToken → /login; if 401 → try refresh
```

**Why not httpOnly cookies:** The .NET API returns tokens in the response body, not Set-Cookie headers.
Cookie-based storage would require API changes. Memory storage is the correct approach for a SPA
without a BFF layer.

---

## TanStack Query conventions

```ts
// Query key factory — one per module
export const clinicKeys = {
  all: ['clinics'] as const,
  list: () => [...clinicKeys.all, 'list'] as const,
  detail: (id: string) => [...clinicKeys.all, 'detail', id] as const,
}

// Query hook
export function useClinics() {
  return useQuery({ queryKey: clinicKeys.list(), queryFn: clinicsApi.getAll })
}

// Mutation hook — invalidates list on success
export function useCreateClinic() {
  const qc = useQueryClient()
  return useMutation({
    mutationFn: clinicsApi.create,
    onSuccess: () => qc.invalidateQueries({ queryKey: clinicKeys.list() })
  })
}

// Paginated query
export function usePatients(page: number, pageSize = 20) {
  return useQuery({
    queryKey: ['patients', 'list', page, pageSize],
    queryFn: () => patientsApi.getAll({ page, pageSize }),
    placeholderData: keepPreviousData,
  })
}
```

---

## Route protection

```tsx
// In router definition
{
  path: '/management',
  element: <RequireRole roles={['Owner', 'Administrator']} />,
  children: [
    { path: 'users', element: <UsersPage /> },
    { path: 'invitations', element: <InvitationsPage /> },
  ]
}

// RequireRole.tsx
export function RequireRole({ roles, children }: Props) {
  const user = useAuthStore(s => s.user)
  if (!user) return <Navigate to="/login" replace />
  if (!roles.includes(user.role.name)) return <Navigate to="/" replace />
  return <>{children}</>
}
```

---

## Sidebar navigation definition

```ts
export const navItems: NavItem[] = [
  { label: 'Dashboard',      path: '/',                  roles: ALL_ROLES },
  { label: 'Patients',       path: '/patients',          roles: ALL_ROLES },
  { label: 'Clinics',        path: '/clinics',           roles: ALL_ROLES },
  { label: 'Work Routes',    path: '/work-routes',       roles: ALL_ROLES },
  { label: 'Consultations',  path: '/consultations',     roles: STAFF_ONLY },  // no Sponsor
  { label: 'Reports',        path: '/reports',           roles: ALL_ROLES },
  { label: 'Collaborators',  path: '/collaborators',     roles: ADMIN_ABOVE },
  { label: 'Management',     path: '/management/users',  roles: ADMIN_ABOVE },
]
```

---

## Notification polling

```ts
// Polling every 60s — no WebSocket needed for v1
export function useUnreadCount() {
  return useQuery({
    queryKey: ['notifications', 'unread-count'],
    queryFn: notificationsApi.getUnreadCount,
    refetchInterval: 60_000,
  })
}
```

---

## Error handling

All API responses that fail map to ProblemDetails (RFC 9457). The Axios response interceptor
extracts `detail` and `errors` fields and throws a typed `ApiError`.

```ts
// In forms — server errors surfaced as field errors
mutation.mutate(data, {
  onError: (err) => {
    if (err instanceof ApiError && err.status === 422) {
      // FluentValidation errors in err.errors object
      Object.entries(err.errors).forEach(([field, msgs]) =>
        form.setError(field as keyof typeof data, { message: msgs[0] })
      )
    }
  }
})
```

---

## Dark mode

```tsx
// On app load — read from user.isDarkMode
useEffect(() => {
  if (user?.isDarkMode) {
    document.documentElement.classList.add('dark')
  } else {
    document.documentElement.classList.remove('dark')
  }
}, [user?.isDarkMode])

// On toggle — optimistic update + mutation
function toggleTheme() {
  const next = !user.isDarkMode
  setUser({ ...user, isDarkMode: next })  // Zustand — instant UI
  document.documentElement.classList.toggle('dark', next)
  updateThemeMutation.mutate({ isDarkMode: next })  // PATCH /users/me/theme
}
```

---

## State management decision

**Zustand** for auth state only (user object + access token).
**TanStack Query** for all server state.
**React Hook Form** local state per form.
No Redux, no MobX, no global `useReducer` + Context for server data.

Reason: the legacy app's `useReducer` + Context caused stale data issues (SET_CLINIC_LIST
in app boot meant clinics never refetched). TanStack Query's cache + background refetch
eliminates the entire category of "data is stale" bugs.
