# SDD Tasks — Sponsor Reports

## Prerequisite
`attention-sessions` change must be complete (reports aggregate session data).

---

### T1 — Report DTOs

- [x] Create `ReportDtos.cs` in `Features/Reports/`:
  - `SummaryReportResponse`
  - `ClinicReportResponse`
  - `WorkRouteReportResponse`
  - `SessionPeriodReportResponse` (grouped by week + AttentionType)

---

### T2 — CQRS query handlers (all AsNoTracking, no mutations)

- [x] `GetSummaryReport.cs` — aggregates across all active entities
- [x] `GetClinicReport.cs` — filters by ClinicId, role-based projection
- [x] `GetWorkRouteReport.cs` — filters by WorkRouteId
- [x] `GetSessionPeriodReport.cs` — date-range grouping query

Each handler receives the `ClaimsPrincipal` to apply role-based projection.

---

### T3 — Endpoints

- [x] `ReportEndpoints.cs` — 4 GET routes, all `RequireAuthorization()`
- [x] Sponsors get access to all 4 but receive anonymized response
- [x] Register in `Program.cs`

---

### T4 — Integration tests

- [x] `ReportTests.cs`:
  - `GetSummary_ReturnsCorrectCounts`
  - `GetSummary_AsSponsor_Returns200` (sponsors are allowed)
  - `GetClinicReport_AdminSeesCollaboratorNames`
  - `GetSessionPeriod_EmptyRange_ReturnsZeroCounts`
  - `GetClinicReport_AsSponsor_NamesAreNull` (R2: anonymized projection)
  - `GetClinicReport_UnknownId_Returns404`
  - `GetWorkRouteReport_ReturnsCounts` (destinations from owned JSON collection)
  - `GetWorkRouteReport_AsDoctor_Returns403` (R3)
  - `GetSessionPeriod_InvalidRange_Returns400` (from > to)
  - `GetSessionPeriod_AsMember_SeesOnlyOwnSessions` (R4 role scoping)

---

## Estimated effort
2–3 hours (pure reads, no domain complexity).
