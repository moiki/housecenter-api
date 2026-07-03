# Verify Report — Help Center

**Change**: help-center
**Mode**: Strict TDD (independently corroborated, not just trusted from apply-progress)
**Date**: 2026-07-03

---

## Verdict

**PASS WITH WARNINGS** — 0 CRITICAL, 5 WARNING, 4 SUGGESTION.

The implementation is behaviorally correct, matches spec.md and proposal.md with no
undocumented drift beyond the 2 explicitly documented deviations, builds clean, and all
113 tests pass. The warnings are all test-coverage completeness gaps (some real,
runtime-proven via coverage data, not guesses) — none indicate broken or missing
production behavior. Safe to proceed to `sdd-archive`; the warnings are worth a small
follow-up test batch but do not block.

---

## Requirements checklist

| Req | Status | Note |
|---|---|---|
| R1 — Entity & validation | ✅ | Fields, unique-including-soft-deleted key, immutability, string-enum conversion, FluentValidation rules all match exactly; only the TopicKey-regex validation path is integration-tested end-to-end, other field-limit rules rely on the already-proven shared `ValidationBehavior` pipeline (SUGGESTION). |
| R2 — Audience-filtered list | ✅ | `HelpAudienceLadder` + in-memory IN-set filter correctly sidesteps the `HasConversion<string>()` alphabetical-`<=` gotcha; summary DTO has no `Body` field (structural guarantee); tested for Sponsor/Member ceilings only, not all 5 tiers on the List endpoint specifically (SUGGESTION). |
| R3 — By-key read, 404 not 403 | ✅ | Single shared `NotFound` instance returned for both "missing" and "out of audience" paths — 100% line coverage, both branches proven by tests. |
| R4 — Auth + write authorization | ⚠️ | Group-level `RequireAuthorization()` + per-write-route `AdministratorOrAbove` policy confirmed in code for all 5 routes. Tested: 401 on List only (not individually on the other 4, though same shared middleware), 403 on Create and Delete, but **no dedicated 403 test for PUT** (WARNING — same policy, but unproven at runtime for this specific route). |
| R5 — Duplicate-key 409 | ⚠️ | Both active-duplicate and soft-deleted-duplicate return 409 as required, code has genuinely distinct Conflict messages ("already exists" vs "exists but is unpublished"). Test `CreateTopic_DuplicateSoftDeletedKey_Returns409` asserts only the status code, **not the message content** the spec explicitly calls for (WARNING). |
| R6 — Soft delete = unpublish | ✅ | `IsActive = false` + reliance on the existing global query filter (no `IgnoreQueryFilters()` override in List/GetByKey) — proven for GetByKey across all 5 roles; List-side disappearance not separately tested (SUGGESTION, same filter mechanism). |
| R7 — Seeder idempotency | ⚠️ | Insert-if-missing via `IgnoreQueryFilters()` diff, never mutates existing rows, fresh entity instances per call — all correct by code and by test. But the one test (`Seeder_RunsTwice_NoDuplicates_EditsSurviveReseed`) only exercises the "already-fully-seeded → no-op, edit survives" branch; the scenario's explicit "3-of-5 present → 2 get backfilled" partial-seed branch is **not exercised at runtime**, only verifiable by static code reading (the set-difference logic) (WARNING). |

---

## Independent verification results

| Check | Result |
|---|---|
| `dotnet build` | ✅ Clean — 0 errors, 4 pre-existing warnings unrelated to help-center (nullable-ref warnings in `ClinicHandlerTests.cs`, an EF-Relational version-conflict MSB3277 warning present before this change) |
| `dotnet test` | ✅ **113/113 passed**, 0 failed, 0 skipped, 23s — matches the cumulative count claimed across all 4 phases in apply-progress (100 → 103 → 109 → 112 → 113), independently re-run, not trusted from the report |
| `dotnet ef migrations has-pending-model-changes` | ✅ "No changes have been made to the model since the last migration." |
| `git log --oneline -6` | ✅ Shows exactly the 4 `feat(help)` commits (PR1–PR4) plus the 2 setup commits (SDD docs, SDD init), matching the chained-PR plan |
| `git status` | ✅ Clean of anything help-center-related. 3 unrelated pre-existing uncommitted files exist (`CLAUDE.md`, `HouseCenter.Api/Data/HouseCenterDbContext.cs`, `HouseCenter.Api.Tests/PatientFlowTests.cs`) — all part of an unrelated Npgsql `DateTime.Kind=Utc` value-converter fix, not help-center scope. Confirmed via diff, not a finding. |
| TODO/FIXME/placeholder grep | ✅ None found in any new help-center file |
| `HelpEndpoints.cs` route count | ✅ Exactly 5 routes, matching the spec's API surface table 1:1 — `GET /topics`, `GET /topics/{topicKey}`, `POST /topics`, `PUT /topics/{id:guid}`, `DELETE /topics/{id:guid}`. No extra/debug route, none missing. |

### Coverage (coverlet, real execution — not estimated)

| File | Line % | Branch note | Rating |
|---|---|---|---|
| `Features/Help/HelpAudienceLadder.cs` | 100% | — | ✅ Excellent |
| `Features/Help/ListHelpTopics.cs` | 100% | — | ✅ Excellent |
| `Features/Help/GetHelpTopic.cs` | 100% | — | ✅ Excellent |
| `Features/Help/HelpTopicValidators.cs` | 100% | — | ✅ Excellent |
| `Features/Help/HelpTopicDtos.cs` | 100% | — | ✅ Excellent |
| `Domain/Entities/HelpTopic.cs` | 100% | — | ✅ Excellent |
| `Data/Configurations/HelpTopicConfiguration.cs` | 100% | — | ✅ Excellent |
| `Features/Help/HelpEndpoints.cs` | 97.0% | L32 uncovered — the `Results.Created(...)` success lambda | ⚠️ Acceptable |
| `Data/DatabaseSeeder.cs` | 98.8% (help-related portion 100%) | L28-30 uncovered belongs to the pre-existing `ApplyMigrationsAtStartup` branch, unrelated to help-center | ✅ Excellent |
| `Features/Help/UpdateHelpTopic.cs` | 94.7% | L23 `if (topic is null)` branch-coverage 50% (1/2) — the NotFound/404 branch is **never exercised** | ⚠️ Acceptable |
| `Features/Help/DeleteHelpTopic.cs` | 90.0% | L18 `if (topic is null)` branch-coverage 50% (1/2) — the NotFound/404 branch is **never exercised** | ⚠️ Acceptable |
| **`Features/Help/CreateHelpTopic.cs`** | **51.6%** | **L36-52 entirely uncovered — the whole success path (entity construction, `Add`, `SaveChangesAsync`, response building) has never been exercised by any test** | ⚠️ **Low — see Findings** |

**Average changed-file coverage**: ~94% overall, but the Create-success-path gap below is the most material finding in this review.

---

## Findings

### CRITICAL
None.

### WARNING

1. **`Features/Help/CreateHelpTopic.cs:36-52` and `Features/Help/HelpEndpoints.cs:32`** — The successful-create path of `POST /help/topics` (constructing the entity, saving, and returning `201 HelpTopicResponse`) is **never exercised by any test**. All 4 `CreateTopic_*` tests in `HelpTopicTests.cs` hit either the 403 (role gate), 409×2 (duplicate checks), or 400 (validation) branches — none send a valid, non-duplicate payload as an Administrator and assert `201`/response shape. Confirmed by coverlet: 51.6% line coverage on the handler, with the entire success block dark. Code review shows the logic is simple and correct, so this is a coverage/TDD-completeness gap, not a known defect — but per strict-TDD philosophy this endpoint's primary happy path shipped without a RED/GREEN cycle proving it.
   - **Closed** — see `CreateTopic_AsAdmin_Returns201WithBody` (`HouseCenter.Api.Tests/HelpTopicTests.cs`). Asserts 201, non-null `Location` header, and that the response body's `TopicKey`/`Title`/`Summary`/`Body`/`Audience`/`Category`/`DisplayOrder` all match the sent request. Re-ran coverlet: `CreateHelpTopic.cs` now shows 100% line-rate / 100% branch-rate (up from 51.6%). No production code changed — the existing implementation was already correct.
2. **`Features/Help/UpdateHelpTopic.cs:22-23`** and **`Features/Help/DeleteHelpTopic.cs:17-18`** — The `if (topic is null) return NotFound;` branch in both handlers shows 50% (1/2) branch coverage — i.e. updating/deleting a nonexistent or already-soft-deleted `id` has never been tested for either endpoint. tasks.md's Phase 3 test list (3.6) does not include such a case either.
   - **Closed** — see `UpdateTopic_NotFound_Returns404` and `DeleteTopic_NotFound_Returns404` (`HouseCenter.Api.Tests/HelpTopicTests.cs`), both PUT/DELETE-ing a `Guid.NewGuid()` id as Administrator and asserting 404. Re-ran coverlet: both `UpdateHelpTopic.cs` and `DeleteHelpTopic.cs` now show 100% line-rate / 100% branch-rate (up from 94.7%/90.0%). No production code changed.
3. **`HouseCenter.Api.Tests/HelpTopicTests.cs`** (R5) — `CreateTopic_DuplicateSoftDeletedKey_Returns409` asserts only `HttpStatusCode.Conflict`, not the response body/message. R5 explicitly requires the 409 to come "with a message indicating the topic exists but is unpublished" — the code (`CreateHelpTopic.cs:33`) does return a distinct, correct message (`help.topic_key_unpublished`), but no test proves that at runtime; it's currently indistinguishable at the test level from the plain duplicate-active-key 409.
   - **Closed** — the existing `CreateTopic_DuplicateSoftDeletedKey_Returns409` test was strengthened (not replaced) to also read the response body and assert it contains the exact message text `"A help topic with this key exists but is unpublished"` (verified against `CreateHelpTopic.cs:33` before writing the assertion, not guessed).
4. **`HouseCenter.Api.Tests/DatabaseSeederTests.cs`** (R7) — The single seeder test only proves the "all 5 already present → no-op, edit survives" branch. The spec's own scenario text ("the database already has 3 of the 5 starter topics ... THEN ... the 2 missing topics are inserted") describes a partial-seed backfill case that is never exercised at runtime — only verifiable by reading the simple set-difference logic in `SeedHelpTopicsAsync`.
   - **Closed** — see `Seeder_PartialSeed_BackfillsOnlyMissingTopics` (`HouseCenter.Api.Tests/DatabaseSeederTests.cs`). Pre-seeds 3 of the 5 starter `TopicKey`s with distinct sentinel content before running the seeder once, then asserts the other 2 (`sessions.status-lifecycle`, `treatments.status-lifecycle`) are backfilled while the 3 pre-existing rows keep their original (untouched) `Summary`. Matches the existing test's InMemory-DB/hoisted-`Guid.NewGuid()` setup pattern exactly.
5. **R4** — No dedicated 403 test exists for `PUT /help/topics/{id:guid}` as a non-admin caller (only Create and Delete have one). The route uses the identical `RequireAuthorization(PolicyNames.AdministratorOrAbove)` primitive, so risk is low, but it is untested at this specific route.
   - **Closed** — see `UpdateTopic_AsMember_Returns403` (`HouseCenter.Api.Tests/HelpTopicTests.cs`), seeding a topic then PUT-ing as a Member-role client and asserting 403.

### SUGGESTION

1. Only the `TopicKey` regex validation rule is integration-tested (`CreateTopic_InvalidKeyFormat_Returns400`). The other field-limit rules (Title/Summary/Body/Category max-lengths, `DisplayOrder >= 0`, `Audience` IsInEnum) are implemented correctly in `HelpTopicValidators.cs` but rely on the shared, already-proven `ValidationBehavior` pipeline rather than a dedicated integration test per rule.
2. `ListTopics_*` tests cover only the Sponsor and Member audience ceilings; Doctor/Administrator/Owner ceilings are untested directly on `GET /help/topics` (though the ladder logic itself is a single shared helper already exercised by `GetHelpTopic`'s all-5-roles loop for the by-key endpoint).
3. There is no dedicated test proving a soft-deleted topic disappears from `GET /help/topics` (the list) — only from `GET /help/topics/{topicKey}` (`DeleteTopic_ThenGet_Returns404ForEveryRole`). Same global query-filter mechanism, so risk is low.
4. Only `ListTopics_Unauthenticated_Returns401` proves the 401 gate; the other 4 routes share the same `RequireAuthorization()` group but are not individually re-proven unauthenticated.

---

## Documented deviations reviewed

Both deviations flagged in proposal.md were checked against the actual code and found consistent and intentional — no additional undocumented drift found beyond these two:

1. **`HelpAudience` independent from `PolicyNames`** — Confirmed: `HelpAudience` (`Domain/Entities/HelpAudience.cs`) is a distinct enum type from `PolicyNames`/`RoleNames` (`Domain/Constants/`). They coincidentally share 3 string values (`AdministratorOrAbove`, `MemberOrAbove`, `OwnerOnly`) but are never the same type, never converted into each other, and the code carries an explicit comment ("If a 6th role is ever added, update RoleNames, the AuthorizationBuilder policies in Program.cs, AND this ladder together — there is no shared source of truth between them.") warning future maintainers. Consistent.
2. **`HelpTopicResponse.Audience` typed `string`, not `HelpAudience`** — Confirmed both in spec.md's own DTO definition (already reflects `string Audience`) and in code (`HelpTopicDtos.cs:8-10`), with an explicit comment citing the precedent. Verified the precedent claim is accurate: `ConsultationResponse.Status` (`Features/Consultations/ConsultationDtos.cs:20`) is indeed typed `string`. Consistent, not fabricated.

No other undocumented deviations found — DTO shapes, route signatures, and validation rules all match spec.md field-for-field.

---

## Scope check

Confirmed clean against proposal.md's "Backend only" boundary:
- No frontend code, `HelpTooltip`, `/help` pages, hooks, or anchors present anywhere in the diff.
- No admin authoring UI.
- No public/anonymous endpoint — all 5 routes require authentication.
- No view analytics, no revision history, no image/attachment handling in `HelpTopic`.
- The 3 unrelated uncommitted files in `git status` (Npgsql UTC `DateTime` fix) are pre-existing, out-of-scope work not touched by or related to this change — correctly out of scope for this review per the task instructions.

---

## Recommendation

**Ready for `sdd-archive`.** No CRITICAL findings; all WARNINGs are test-coverage completeness gaps in a codebase with otherwise thorough behavioral testing (113/113 passing, 94%+ average coverage on changed files), not evidence of broken behavior. Suggested (non-blocking) follow-up before or shortly after archiving: add `CreateTopic_AsAdmin_Returns201WithBody` (closes the largest gap — the Create success path), `UpdateTopic_NotFound_Returns404`, `DeleteTopic_NotFound_Returns404`, and assert the 409 message body in `CreateTopic_DuplicateSoftDeletedKey_Returns409`.
