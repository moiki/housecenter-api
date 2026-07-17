# SDD Spec — Sponsor Reports

## Requirements

### R1 — Summary report collaborator count
`GET /reports/summary` MUST report a `Collaborators` count reflecting collaborators
resolvable through the User link, consistent with the session-derived collaborator activity
counted elsewhere in the same response.

#### Scenario: Summary count matches session-derived collaborator activity
- GIVEN N distinct linked Collaborators generated sessions this month
- WHEN `GET /reports/summary` and `GET /reports/sessions` are both called
- THEN the collaborator population implied by both MUST be derived from the same link-based source

### R2 — Clinic report
- `GET /reports/clinics/{id}` returns aggregates for one clinic:
  patients attending, collaborators assigned, sessions count this month, active treatments
- Collaborator identity/counts MUST resolve through the `Collaborator.UserId` link when
  producing Administrator/Owner detail projections; anonymized projections for other roles
  are unchanged.
- Role-based projection:
  - Administrator / Owner → full detail including collaborator names
  - Everyone else (Sponsor, Doctor, Member) → anonymized: counts only,
    `collaboratorNames` is null
  - Note: "own clinic only" was dropped — `User` accounts are not linked to
    clinics in the domain model (`Collaborator` is a separate directory entity),
    so per-clinic membership cannot be enforced
- Unknown clinic id → 404

#### Scenario: Admin views clinic detail including a legacy collaborator
- GIVEN a clinic has one linked and one legacy (unlinked) Collaborator
- WHEN Administrator calls `GET /reports/clinics/{id}`
- THEN both MUST be counted in `collaboratorsAssigned`; the legacy row's name MUST still
  appear (roster membership, not session activity, drives this count)

### R3 — Work route report
- `GET /reports/work-routes/{id}` returns: destinations count, collaborators on route,
  sessions in last 30 days, distinct patients visited
- Collaborator identity/counts MUST resolve through the `Collaborator.UserId` link when
  producing Administrator/Owner detail projections; anonymized projections for other roles
  are unchanged.
- Role-based access:
  - Doctor → 403 (doctors are not tied to routes)
  - Administrator / Owner → full detail including collaborator names
  - Sponsor / Member → anonymized (counts only, names null; same reason as R2)
- Sessions count and patients-visited both use a last-30-days window
- Unknown route id → 404

### R4 — Session period report
- `GET /reports/sessions?from=&to=` returns sessions grouped per week,
  broken down by `AttentionType` and `SessionStatus`
- Collaborator identity in the Admin/Owner-only `SessionsByCollaborator` breakdown MUST
  be resolved by joining `AttentionSession.Collaborator` (User) through the link back to its
  `Collaborator` roster identity, so names/labels match the roster used elsewhere.
- Role-based projection:
  - Sponsor → totals only, no names
  - Doctor → only sessions of their assigned patients
  - Member → only their own sessions
  - Administrator / Owner → includes per-collaborator breakdown
- Empty range returns zeroed series, not 404

#### Scenario: Session breakdown label matches roster name
- GIVEN a linked Collaborator recorded 3 sessions this period
- WHEN Administrator calls `GET /reports/sessions`
- THEN the per-collaborator label in the breakdown MUST match the collaborator's roster
  name (`Collaborator.FirstName`/`LastName`), not the raw `User` display name

### R5 — Access control summary

| Endpoint | Sponsor | Doctor | Member | Admin/Owner |
|---|---|---|---|---|
| `/reports/summary` | ✅ | ✅ | ✅ | ✅ |
| `/reports/clinics/{id}` | ✅ (anonymized) | ✅ (anonymized) | ✅ (anonymized) | ✅ |
| `/reports/work-routes/{id}` | ✅ (anonymized) | ✗ | ✅ (anonymized) | ✅ |
| `/reports/sessions` | ✅ (no names) | ✅ (assigned patients) | ✅ (own sessions) | ✅ |

### R6 — Read-only guarantee
- The Reports module contains Query handlers only — no commands, no mutations
- Every EF query uses `AsNoTracking()`
- Aggregates inside a handler run sequentially on the scoped `DbContext` —
  EF contexts are not thread-safe, so `Task.WhenAll` over one context is a
  concurrency bug; true parallelism would require `IDbContextFactory`

## Scenarios

```
SCENARIO: sponsor requests the global summary
  GIVEN user has Sponsor role
  WHEN GET /reports/summary
  THEN 200 is returned with aggregate counts only — no patient or collaborator names

SCENARIO: admin requests a clinic report
  GIVEN user has Administrator role and clinic exists with 2 collaborators
  WHEN GET /reports/clinics/{id}
  THEN 200 is returned including collaborator names

SCENARIO: sponsor requests the same clinic report
  GIVEN user has Sponsor role
  WHEN GET /reports/clinics/{id}
  THEN 200 is returned with counts only — collaborator names omitted

SCENARIO: doctor requests a work route report
  GIVEN user has Doctor role
  WHEN GET /reports/work-routes/{id}
  THEN 403 is returned

SCENARIO: period report over an empty range
  GIVEN no sessions exist between from and to
  WHEN GET /reports/sessions?from=2026-01-01&to=2026-01-31
  THEN 200 is returned with zeroed weekly series

SCENARIO: session breakdown label matches roster name
  GIVEN a linked Collaborator recorded 3 sessions this period
  WHEN Administrator calls GET /reports/sessions
  THEN the per-collaborator label in the breakdown MUST match the collaborator's roster
    name (Collaborator.FirstName/LastName), not the raw User display name
```

## API surface

```
GET /reports/summary                    → 200 SummaryReportResponse
GET /reports/clinics/{id}               → 200 ClinicReportResponse | 403 | 404
GET /reports/work-routes/{id}           → 200 WorkRouteReportResponse | 403 | 404
GET /reports/sessions?from=&to=         → 200 SessionPeriodReportResponse | 400
```

## DTOs

```csharp
record SummaryReportResponse(
    int TotalPatients, int ActivePatients, int SessionsThisMonth,
    int Collaborators, int Clinics, int WorkRoutes, int ActiveTreatments,
    IReadOnlyDictionary<string, int> SessionsByAttentionType);

record ClinicReportResponse(
    Guid ClinicId, string ClinicName, int PatientsAttending,
    int CollaboratorsAssigned, int SessionsThisMonth, int ActiveTreatments,
    IReadOnlyList<string>? CollaboratorNames);   // null for Sponsor projection

record WorkRouteReportResponse(
    Guid WorkRouteId, string RouteName, int DestinationsCount,
    int CollaboratorsOnRoute, int SessionsLast30Days, int PatientsVisited,
    IReadOnlyList<string>? CollaboratorNames);   // null for Sponsor projection

record WeeklySessionBucket(
    DateOnly WeekStart, string AttentionType, string Status, int Count);

record SessionPeriodReportResponse(
    DateOnly From, DateOnly To,
    IReadOnlyList<WeeklySessionBucket> Weeks,
    IReadOnlyDictionary<string, int>? SessionsByCollaborator);  // Admin/Owner only
```

## Validation rules

| Field | Rule |
|---|---|
| `from` / `to` | Both required for `/reports/sessions`; `from <= to` |
| Range length | Max 366 days |
| `id` route params | Valid Guid (404 if entity not found) |
