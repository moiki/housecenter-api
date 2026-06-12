# SDD Spec — Sponsor Reports

## Requirements

### R1 — Summary report
- Any authenticated user can request `GET /reports/summary`
- Returns aggregate counts: total patients, active patients, sessions this month,
  collaborators, clinics, work routes, active treatments, breakdown by `AttentionType`
- No individual patient data in any projection
- All queries `AsNoTracking()`

### R2 — Clinic report
- `GET /reports/clinics/{id}` returns aggregates for one clinic:
  patients attending, collaborators assigned, sessions count this month, active treatments
- Role-based projection:
  - Sponsor → anonymized (counts only, no collaborator or patient names)
  - Doctor / Member → only for their own clinic; otherwise 403
  - Administrator / Owner → full detail including collaborator names
- Unknown clinic id → 404

### R3 — Work route report
- `GET /reports/work-routes/{id}` returns: destinations count, collaborators on route,
  sessions in last 30 days, distinct patients visited
- Role-based access:
  - Sponsor → anonymized
  - Doctor → 403 (doctors are not tied to routes)
  - Member → only their own route; otherwise 403
  - Administrator / Owner → full detail
- Unknown route id → 404

### R4 — Session period report
- `GET /reports/sessions?from=&to=` returns sessions grouped per week,
  broken down by `AttentionType` and `SessionStatus`
- Role-based projection:
  - Sponsor → totals only, no names
  - Doctor → only sessions of their assigned patients
  - Member → only their own sessions
  - Administrator / Owner → includes per-collaborator breakdown
- Empty range returns zeroed series, not 404

### R5 — Access control summary

| Endpoint | Sponsor | Doctor | Member | Admin/Owner |
|---|---|---|---|---|
| `/reports/summary` | ✅ | ✅ | ✅ | ✅ |
| `/reports/clinics/{id}` | ✅ (anonymized) | ✅ (their clinic only) | ✅ (their clinic only) | ✅ |
| `/reports/work-routes/{id}` | ✅ (anonymized) | ✗ | ✅ (their route only) | ✅ |
| `/reports/sessions` | ✅ (no names) | ✅ (assigned patients) | ✅ (own sessions) | ✅ |

### R6 — Read-only guarantee
- The Reports module contains Query handlers only — no commands, no mutations
- Every EF query uses `AsNoTracking()`
- Independent aggregates within one handler may run via `Task.WhenAll`

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
