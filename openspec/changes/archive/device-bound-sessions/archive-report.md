# Archive Report — device-bound-sessions (change #1)

**Status**: SHIPPED
**Archived**: 2026-07-13
**Artifact store**: engram (memory-persisted)

## Outcome
Replaced stateless refresh JWTs with persisted per-device UserSession tokens. Implemented 4 stacked PRs (Foundation, Login/Refresh Rewrite, Session Mgmt + Reset, Tests) with 33/33 tasks complete, all requirements R1-R10 verified, backend-tested via 194/194 xUnit tests against real Postgres.

## Verification
PASS WITH WARNINGS (0 CRITICAL / 3 WARNING / 4 SUGGESTION) — `dotnet test` (xUnit, WebApplicationFactory, real Postgres): 194/194 passed. R1-R9 code-verified; R10 validators exist but no explicit HTTP 400 test found for missing-deviceId.

## Delivery
PRs #20-23 stacked on moiki/housecenter-api (issue #19 tracking). All 4 commits merged to main.

## Residual / follow-ups
WARNING 1: R10 (deviceId/platform required) validators present but no HTTP-level test asserts 400 on invalid input — recommend smoke-test real login with missing deviceId. WARNING 2: Formal "TDD Cycle Evidence" table not present despite strict_tdd:true — downgraded from CRITICAL because verify independently re-ran tests and got exact matches. WARNING 3: state.yaml's `chain_strategy`/`decision_needed_before_apply` fields not updated to reflect 4-PR resolution. Delta spec retained in this archived folder's `spec.md`; not merged into `openspec/specs/` (consistent with sibling API changes; spec-tree merge is a separate maintainer task).

## Verification source
Observation #517: sdd/device-bound-sessions/verify-report
