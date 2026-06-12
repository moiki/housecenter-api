# SDD Proposal — Sponsor Reports

## Change name
`sponsor-reports`

## Status
`implemented` (2026-06-12 — all tasks T1–T4 complete, 67/67 tests green)

## Problem

Sponsors need visibility into the NGO's work to justify continued financial support,
but they must not access individual patient clinical records (privacy, trust).

Currently there is no layer between "I can see all patients" and "I can see nothing".
A user with Sponsor role would need access to individual patient endpoints to see
any data — which exposes clinical details they should not see.

Additionally, no role can get an operational overview: how many patients are being
attended, how sessions are distributed across clinics and routes, treatment progress
at a high level.

## Proposed change

A read-only `Reports` module with endpoints that:

1. Return **aggregate data only** — no individual patient records
2. Apply **role-based projection** — sponsors see anonymized summaries; admins see
   more operational detail
3. Are designed to be displayed in a dashboard (counts, breakdowns, trends)

All queries are read-only CQRS handlers with `AsNoTracking()` throughout.

## Scope

### Summary report (all roles that have access)
```
GET /reports/summary
→ total patients, active patients, sessions this month,
  collaborators, clinics, work routes, treatments active,
  breakdown by AttentionType
```

### Clinic report
```
GET /reports/clinics/{id}
→ patients attending this clinic, collaborators assigned,
  sessions count this month, treatments active
```

### Work route report
```
GET /reports/work-routes/{id}
→ destinations count, collaborators on route, sessions last 30 days,
  patients visited
```

### Period report (monthly trend)
```
GET /reports/sessions?from=&to=
→ sessions per week grouped by AttentionType and Status
  (for sponsors: anonymized; for admin: includes collaborator breakdown)
```

## Access control

| Endpoint | Sponsor | Doctor | Member | Admin/Owner |
|---|---|---|---|---|
| `/reports/summary` | ✅ | ✅ | ✅ | ✅ |
| `/reports/clinics/{id}` | ✅ (anonymized) | ✅ (their clinic only) | ✅ (their clinic only) | ✅ |
| `/reports/work-routes/{id}` | ✅ (anonymized) | ✗ | ✅ (their route only) | ✅ |
| `/reports/sessions` | ✅ (no names) | ✅ (assigned patients) | ✅ (own sessions) | ✅ |

## Out of scope

- PDF/Excel export
- Scheduled email delivery
- Comparative reports between periods
- Individual patient dashboards

## Why this is P3

Depends on sessions data existing to be meaningful.
A summary with zero sessions is not useful to sponsors.
Implement after `attention-sessions` is running in production.
