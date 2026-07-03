# Tasks: Help Center

## Review Workload Forecast

| Field | Value |
|---|---|
| Estimated changed lines (hand-written; excludes generated migration Designer.cs) | ~1,050–1,150 across full in-scope work |
| 400-line budget risk | High |
| Chained PRs recommended | Yes |
| Suggested split | 4 PRs — one per phase below |
| Delivery strategy | ask-on-risk |
| Chain strategy | stacked-to-main (confirmed by user) |

Decision needed before apply: Yes
Chained PRs recommended: Yes
Chain strategy: stacked-to-main (confirmed by user)
400-line budget risk: High

**Basis:** entity+enum+config+migration+5 CQRS handlers+DTOs+validators+endpoints alone
≈540 lines (sized against `Features/Reports/` and `Features/Patients/Sessions/`, ~45–60
lines/handler file). 11 integration tests ≈280 lines (`AttentionSessionTests.cs` 261
lines/10 tests, `ReportTests.cs` 272 lines/10 tests → ~27 lines/test). 3–5 Spanish seed
topics ≈150–200 lines. Sum lands ~2.5x over the proposal's "near 400" framing for a single
PR1. The generated `AddHelpTopics.Designer.cs` (~950–1,000 lines — precedent:
`AddAttentionSessions.Designer.cs` = 973 lines, full model snapshot) is excluded — it is
never hand-reviewed line-by-line, consistent with every prior migration in this repo.
**Swing factors:** trimming to 3 seed topics or 8 tests saves ~60–140 lines total — not
enough alone to fit one PR, hence the 4-way split below (each phase = one PR, each
comfortably under budget on its own).

## Phase 1: Foundation + Role-Filtered List — PR1 (~375 lines)
- [x] 1.1 `Domain/Entities/HelpTopic.cs` — `HelpTopic : BaseEntity` (TopicKey, Title, Summary, Body, Audience, Category, DisplayOrder) (R1)
- [x] 1.2 `Domain/Entities/HelpAudience.cs` — enum `{ All, Staff, MemberOrAbove, AdministratorOrAbove, OwnerOnly }`, comment linking to `PolicyNames`/`RoleNames`
- [x] 1.3 `Data/Configurations/HelpTopicConfiguration.cs` — Audience as string, unique index on TopicKey, field max lengths (R1)
- [x] 1.4 Add `DbSet<HelpTopic>` to `HouseCenterDbContext.cs`; add migration `AddHelpTopics`; run `dotnet ef database update`
- [x] 1.5 `Features/Help/HelpTopicDtos.cs` — `HelpTopicSummaryResponse`
- [x] 1.6 `Features/Help/ListHelpTopics.cs` — query+handler: audience ladder from `ClaimsPrincipal` role claim, `AsNoTracking()`, inline `new` projection (R2)
- [x] 1.7 `Features/Help/HelpEndpoints.cs` — `MapGroup("/help")`, `RequireAuthorization()`, `GET /help/topics`; wire `app.MapHelpEndpoints()` in `Program.cs`
- [x] 1.8 Add HelpTopics row (CQRS) to CLAUDE.md Module patterns table
- [x] 1.9 `HelpTopicTests.cs`: `ListTopics_Unauthenticated_Returns401`, `ListTopics_AsSponsor_ReturnsOnlyAllAudience`, `ListTopics_AsMember_ExcludesAdministratorOrAboveTopics`

**PR1 done when:** `dotnet build` clean, migration applies, 3 tests green.

## Phase 2: By-Key Read + Create — PR2 (~310 lines)
- [x] 2.1 Extend `HelpTopicDtos.cs` — `HelpTopicResponse` (full, incl. Body), `CreateHelpTopicRequest`
- [x] 2.2 `Features/Help/GetHelpTopic.cs` — query+handler: by TopicKey, audience-gated → 404 on mismatch or missing (R3)
- [x] 2.3 Add `GET /help/topics/{topicKey}` route to `HelpEndpoints.cs`
- [x] 2.4 `Features/Help/HelpTopicValidators.cs` — `CreateHelpTopicRequestValidator` (key regex, max lengths, IsInEnum — R1)
- [x] 2.5 `Features/Help/CreateHelpTopic.cs` — command+handler: dup-key check via `IgnoreQueryFilters()` → 409 on active or soft-deleted match (R5)
- [x] 2.6 Add `POST /help/topics` route, `PolicyNames.AdministratorOrAbove`, `.WithValidation<CreateHelpTopicRequest>()` (R4)
- [x] 2.7 Tests: `GetTopic_OutsideAudience_Returns404`, `GetTopic_WithinAudience_Returns200WithBody`, `CreateTopic_AsMember_Returns403`, `CreateTopic_DuplicateActiveKey_Returns409`, `CreateTopic_DuplicateSoftDeletedKey_Returns409`, `CreateTopic_InvalidKeyFormat_Returns400`

**PR2 done when:** `dotnet build` clean, 6 new + 3 prior tests green.

## Phase 3: Update + Delete (Admin Writes) — PR3 (~210 lines)
- [x] 3.1 Extend `HelpTopicDtos.cs` — `UpdateHelpTopicRequest` (no TopicKey field)
- [x] 3.2 Add `UpdateHelpTopicRequestValidator` to `HelpTopicValidators.cs`
- [x] 3.3 `Features/Help/UpdateHelpTopic.cs` — command+handler: ignores any TopicKey in payload, persisted key unchanged (R1)
- [x] 3.4 `Features/Help/DeleteHelpTopic.cs` — command+handler: soft delete (`IsActive = false`) → 204 (R6)
- [x] 3.5 Add `PUT /help/topics/{id:guid}` and `DELETE /help/topics/{id:guid}` routes, `PolicyNames.AdministratorOrAbove`
- [x] 3.6 Tests: `UpdateTopic_ChangesFields_PersistsCorrectly` (renamed from
      `UpdateTopic_TopicKeyInPayloadIgnored_PersistedKeyUnchanged` — the request DTO has no
      TopicKey field at all, so immutability is structurally guaranteed, not runtime-checked;
      the test instead asserts a normal field update persists correctly, including that
      TopicKey is unchanged), `DeleteTopic_AsMember_Returns403`,
      `DeleteTopic_ThenGet_Returns404ForEveryRole`

**PR3 done when:** `dotnet build` clean, 3 new + 9 prior tests green.

## Phase 4: Starter Content Seed — PR4 (~250 lines)
- [x] 4.1 `Data/DatabaseSeeder.cs` — insert-if-missing `HelpTopic` block, matched on TopicKey, never updates existing rows (R7)
- [x] 4.2 Author 3–5 Spanish starter topics: `sessions.recording`, `sessions.clinic-or-route`, `sessions.status-lifecycle`, `patients.assign-doctor`, `treatments.status-lifecycle`
- [x] 4.3 `openspec/IMPLEMENTATION_PLAN.md` — +1 line noting help-center backend shipped
- [x] 4.4 Test: `Seeder_RunsTwice_NoDuplicates_EditsSurviveReseed` (R7)

**PR4 done when:** fresh DB boots with the starter catalog; reseeding never overwrites an admin edit.
