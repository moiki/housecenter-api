# SDD Tasks — Sponsor Reports

## Prerequisite
`attention-sessions` change must be complete (reports aggregate session data).

---

### T1 — Report DTOs

- [ ] Create `ReportDtos.cs` in `Features/Reports/`:
  - `SummaryReportResponse`
  - `ClinicReportResponse`
  - `WorkRouteReportResponse`
  - `SessionPeriodReportResponse` (grouped by week + AttentionType)

---

### T2 — CQRS query handlers (all AsNoTracking, no mutations)

- [ ] `GetSummaryReport.cs` — aggregates across all active entities
- [ ] `GetClinicReport.cs` — filters by ClinicId, role-based projection
- [ ] `GetWorkRouteReport.cs` — filters by WorkRouteId
- [ ] `GetSessionPeriodReport.cs` — date-range grouping query

Each handler receives the `ClaimsPrincipal` to apply role-based projection.

---

### T3 — Endpoints

- [ ] `ReportEndpoints.cs` — 4 GET routes, all `RequireAuthorization()`
- [ ] Sponsors get access to all 4 but receive anonymized response
- [ ] Register in `Program.cs`

---

### T4 — Integration tests

- [ ] `ReportTests.cs`:
  - `GetSummary_ReturnsCorrectCounts`
  - `GetSummary_AsSponsor_Returns200` (sponsors are allowed)
  - `GetClinicReport_AdminSeesCollaboratorNames`
  - `GetSessionPeriod_EmptyRange_ReturnsZeroCounts`

---

## Estimated effort
2–3 hours (pure reads, no domain complexity).
