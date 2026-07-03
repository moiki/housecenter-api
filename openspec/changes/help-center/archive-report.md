# Archive Report — Help Center

**Change**: help-center  
**Date**: 2026-07-03  
**Status**: ✅ Archived and closed  

---

## What was delivered

### Backend module (4 chained PRs, 6 commits)

1. **PR1 — Foundation + Role-Filtered List** (`711ec69`)
   - `HelpTopic` entity, `HelpAudience` enum, configuration
   - `AddHelpTopics` migration (self-contained, applied)
   - `ListHelpTopics` CQRS handler with audience-ceiling filtering
   - `GET /help/topics` endpoint (summaries only, audience-filtered)
   - 3 integration tests; 103/103 cumulative passed

2. **PR2 — By-Key Read + Create** (`d8a6189`)
   - `GetHelpTopic.cs` — audience-gated, 404 on mismatch (prevents existence leak)
   - `GET /help/topics/{topicKey}` endpoint (full body for authorized audience)
   - `CreateHelpTopic.cs` — duplicate-key detection via `IgnoreQueryFilters()` (409 on active or soft-deleted match)
   - `POST /help/topics` endpoint, `AdministratorOrAbove` policy
   - Validation (`TopicKey` regex, field maxlengths, `Audience` IsInEnum)
   - 6 new tests; 109/109 cumulative passed

3. **PR3 — Update + Delete (Admin Writes)** (`7c3c16a`)
   - `UpdateHelpTopic.cs` — field updates with immutable `TopicKey` (enforced structurally via request DTO)
   - `DeleteHelpTopic.cs` — soft-delete via `IsActive = false`, returns 204
   - `PUT /help/topics/{id:guid}` and `DELETE /help/topics/{id:guid}` endpoints, `AdministratorOrAbove` policy
   - 3 new tests; 112/112 cumulative passed

4. **PR4 — Starter Content Seed** (`fca9c87`)
   - `DatabaseSeeder.SeedHelpTopicsAsync()` — insert-if-missing on `TopicKey`
   - 5 starter Spanish topics: `sessions.recording`, `sessions.clinic-or-route`, `sessions.status-lifecycle`, `patients.assign-doctor`, `treatments.status-lifecycle`
   - Topics grounded in actual code (SessionValidators, UpdateSessionStatus, PatientService, TreatmentService)
   - Seeder test proving partial-seed backfill (3-of-5 present → 2 inserted, edits survive)
   - `openspec/IMPLEMENTATION_PLAN.md` updated
   - 1 new test; 113/113 cumulative passed

### Follow-up (test-coverage closure)

5. **Test Coverage PR** (`test(help): close sdd-verify coverage warnings`) — implemented after verify
   - Added `CreateTopic_AsAdmin_Returns201WithBody` — closes the Create success-path gap (51.6% → 100%)
   - Added `UpdateTopic_NotFound_Returns404`, `DeleteTopic_NotFound_Returns404`
   - Strengthened `CreateTopic_DuplicateSoftDeletedKey_Returns409` to assert message body
   - Added `UpdateTopic_AsMember_Returns403`, `Seeder_PartialSeed_BackfillsOnlyMissingTopics`
   - All 113 tests pass; no production code changes

### Scope (intentionally limited)

- **Backend only** — `HelpTopic` CRUD module via 5 REST endpoints, role-filtered reads, soft-delete unpublish, idempotent seeder
- **Frontend integration deferred** — tooltips, `/help` pages, anchors remain a separate dependent change against `housecenter-web` (matches the precedent of `attention-sessions`/`medical-consultations`/`sponsor-reports`)
- **Out of scope**: admin authoring UI, public/anonymous endpoints, view analytics, revision history, image/diagram support

---

## Verification verdict

**PASS WITH WARNINGS** — 0 CRITICAL, 5 WARNING (all closed), 4 SUGGESTION (deferred by design).

- `dotnet build` clean (0 errors, pre-existing unrelated warnings)
- `dotnet test` 113/113 passed (independently re-run, not trusted from apply-progress)
- `dotnet ef migrations has-pending-model-changes` — none
- All 5 routes present, matching spec API surface table 1:1
- Average coverage ~94% on changed files; Create success path and NotFound branches initially dark, all closed in follow-up PR
- Both documented deviations (`HelpAudience` independent from `PolicyNames`; `HelpTopicResponse.Audience` typed `string`) verified consistent and intentional
- No undocumented drift, no TODO/FIXME/placeholders, no unrelated changes to existing modules

**Full detail**: see `verify-report.md`

---

## Commits

```
c0c13b3 test(help): close sdd-verify coverage warnings
fca9c87 feat(help): seed Spanish starter guide content (PR4)
7c3c16a feat(help): add update and delete admin writes (PR3)
d8a6189 feat(help): add by-key read and create endpoints (PR2)
711ec69 feat(help): add HelpTopic entity and role-filtered topic listing (PR1)
0aa070d docs(help-center): add SDD plan...
d20df5c chore(sdd): initialize SDD context...
```

---

## Files and structure

**New entities and config:**
- `HouseCenter.Api/Domain/Entities/HelpTopic.cs`
- `HouseCenter.Api/Domain/Entities/HelpAudience.cs`
- `HouseCenter.Api/Data/Configurations/HelpTopicConfiguration.cs`
- `HouseCenter.Api/Migrations/…_AddHelpTopics.cs` (self-contained, not hand-reviewed line-by-line)

**CQRS handlers and endpoints:**
- `HouseCenter.Api/Features/Help/ListHelpTopics.cs`
- `HouseCenter.Api/Features/Help/GetHelpTopic.cs`
- `HouseCenter.Api/Features/Help/CreateHelpTopic.cs`
- `HouseCenter.Api/Features/Help/UpdateHelpTopic.cs`
- `HouseCenter.Api/Features/Help/DeleteHelpTopic.cs`
- `HouseCenter.Api/Features/Help/HelpTopicDtos.cs`
- `HouseCenter.Api/Features/Help/HelpTopicValidators.cs`
- `HouseCenter.Api/Features/Help/HelpAudienceLadder.cs` (shared helper)
- `HouseCenter.Api/Features/Help/HelpEndpoints.cs`

**Tests:**
- `HouseCenter.Api.Tests/HelpTopicTests.cs` (11 scenarios)
- `HouseCenter.Api.Tests/DatabaseSeederTests.cs` (extended with seeder-specific coverage)

**Configuration:**
- `HouseCenter.Api/Program.cs` — `+app.MapHelpEndpoints()`
- `HouseCenter.Api/Data/HouseCenterDbContext.cs` — `+DbSet<HelpTopic>`
- `HouseCenter.Api/Data/DatabaseSeeder.cs` — `+SeedHelpTopicsAsync()`
- `CLAUDE.md` — Module patterns table (HelpTopics row added)

**Planning:**
- `openspec/IMPLEMENTATION_PLAN.md` — "Delivered since then" section updated

---

## Audience ladder (role-based filtering)

| Role | Sees | Policy |
|---|---|---|
| Sponsor | `All` audience only | Read-only |
| Doctor | `All` + `Staff` audience | Read-only |
| Member | `All` + `Staff` + `MemberOrAbove` audience | Read-only |
| Administrator | All except `OwnerOnly` | Read-only + Write (CREATE, UPDATE, DELETE) |
| Owner | All (100%) | Read-only + Write (CREATE, UPDATE, DELETE) |

---

## How to use

### List topics (any authenticated role)
```http
GET /api/v1/help/topics
```
Response: `[{ TopicKey, Title, Summary, Category, DisplayOrder }, ...]` — role-filtered, no Body.

### Get full topic by key (any authenticated role, 404 if outside audience)
```http
GET /api/v1/help/topics/{topicKey}
```
Response: Full `{ Id, TopicKey, Title, Summary, Body, Audience, Category, DisplayOrder, UpdatedDate }` if within audience; 404 otherwise.

### Create topic (AdministratorOrAbove)
```http
POST /api/v1/help/topics
Content-Type: application/json

{
  "topicKey": "sessions.recording",
  "title": "How to Record a Session",
  "summary": "Short summary for tooltips",
  "body": "# Full markdown body…",
  "audience": "Staff",
  "category": "Sessions",
  "displayOrder": 0
}
```
Response: `201 Created` with Location header and full topic body.

### Update topic (AdministratorOrAbove, `TopicKey` immutable)
```http
PUT /api/v1/help/topics/{id:guid}
Content-Type: application/json

{
  "title": "Updated Title",
  "summary": "Updated summary",
  "body": "Updated markdown…",
  "audience": "Staff",
  "category": "Sessions",
  "displayOrder": 0
}
```
Response: `200 OK` with updated topic (TopicKey unchanged).

### Delete topic (AdministratorOrAbove, soft delete = unpublish)
```http
DELETE /api/v1/help/topics/{id:guid}
```
Response: `204 No Content`. Topic disappears from both `/help/topics` and by-key reads for all roles.

---

## Next: Frontend integration

The 5 starter topics are now live in the database. The frontend can:
1. Call `GET /api/v1/help/topics` to fetch the full guide index (role-filtered)
2. Call `GET /api/v1/help/topics/{topicKey}` to fetch a guide by key
3. Render the markdown `Body` in tooltips, a `/help` page, or inline guidance
4. Admins can use `POST /PUT /DELETE` to author/update/unpublish content via the API

This frontend integration is a separate change against `housecenter-web`, not part of this backend-only delivery.

---

## Rollback (if needed)

This is a **low-risk** isolated module with no dependencies in-repo:
1. `dotnet ef migrations remove` (undo the `AddHelpTopics` migration)
2. Delete `Features/Help/`, `Domain/Entities/HelpTopic.cs`, `Domain/Entities/HelpAudience.cs`, `Data/Configurations/HelpTopicConfiguration.cs`
3. Remove `DbSet<HelpTopic>` from `HouseCenterDbContext.cs`
4. Remove `app.MapHelpEndpoints()` from `Program.cs`
5. Remove seeder block from `DatabaseSeeder.cs`
6. Remove the `HelpTopics` row from CLAUDE.md module table

No data migration, no schema change to existing entities, no blast radius beyond the new module.

---

## Reference documentation

- **Full proposal** — `proposal.md` (problem, scope, decisions, Q&A)
- **API spec** — `spec.md` (requirements, scenarios, DTOs, validation rules)
- **Design & tasks** — `tasks.md` (4-phase work breakdown with workload forecast)
- **Implementation log** — `state.yaml` (per-phase notes, cumulative test counts, coverage)
- **Verification** — `verify-report.md` (requirements checklist, coverage analysis, findings and closures)
