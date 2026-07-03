# Exploration — Help Center (validation pass on existing plan)

Validates `HouseCenter.Api/docs/help-center-plan.md` against the live codebase before
`/sdd-new help-center` proceeds to proposal. Not a from-scratch exploration.

## Summary

The existing plan is well-grounded: every architectural claim it makes (`BaseEntity` shape,
`IEntityTypeConfiguration<T>` pattern, enum-as-string conversion, unique-index precedent,
policy names, CQRS flat-file layout, root-mounted endpoints, seeder insert-if-missing
pattern) checks out against real code, file-for-file. No "Help"/"Guide"/"FAQ" module exists
today, so there's no redundancy to reconcile. **Confidence: high — proceed to proposal using
the plan as-is**, with two small clarifications and a few sequencing notes captured below.

## Confirmed

- **`BaseEntity` shape** (`HouseCenter.Api/Domain/Entities/BaseEntity.cs`) is exactly
  `Id, IsActive, CreatedDate, UpdatedDate` — `HelpTopic : BaseEntity` fits with zero
  friction, same as every other entity.
- **`IEntityTypeConfiguration<T>` + Fluent API pattern** confirmed via
  `HouseCenter.Api/Data/Configurations/ClinicConfiguration.cs` (simple `Property().IsRequired().HasMaxLength()`)
  and, more precisely, `PushSubscriptionConfiguration.cs`, which is a near-exact template for
  `HelpTopicConfiguration`: string property with max length, `.HasConversion<string>()` on an
  enum property, and `builder.HasIndex(s => s.Token).IsUnique()` — the same shape the plan
  proposes for `TopicKey`. Also confirmed `.HasConversion<string>()` is used pervasively
  (`AttentionSessionConfiguration.cs`, `PatientConfiguration.cs`, `TreatmentConfiguration.cs`,
  etc.) — not a stale claim.
- **Global soft-delete filter** applies automatically to any `BaseEntity` subtype via
  `HouseCenterDbContext.OnModelCreating` (`HouseCenter.Api/Data/HouseCenterDbContext.cs:35-43`) —
  `HelpTopic` gets `IsActive` filtering for free, no extra registration needed beyond adding
  the entity + a `DbSet<HelpTopic>` property. `ApplyConfigurationsFromAssembly` (line 33) also
  means `HelpTopicConfiguration` is auto-discovered — no manual `modelBuilder.ApplyConfiguration()` call needed.
- **Role/policy names are real, not invented.** `HouseCenter.Api/Domain/Constants/PolicyNames.cs`
  defines exactly `OwnerOnly`, `AdministratorOrAbove`, `MemberOrAbove`, `StaffOnly` — all
  registered against `RoleNames` in `Program.cs:133-137`. The plan's write-endpoint policy
  (`AdministratorOrAbove`) matches the existing precedent used identically by
  `ClinicEndpoints.cs` for create/update/delete.
- **No existing Help/Guide/FAQ module.** A glob over `HouseCenter.Api/Features/**` returned
  the full feature list (Auth, Users, Roles, Clinics, WorkRoutes, Collaborators, Patients,
  Sessions, Consultations, Reports, Notifications, Attachments, Invitations, Seeding) — no
  overlap, nothing to reconcile.
- **CQRS flat-file layout matches.** `Features/Reports/GetSummaryReport.cs` and
  `Features/Consultations/{OpenConsultation,ListConsultations,...}.cs` confirm "one
  query/command + handler per file" is the real convention for newer modules (not Clinics'
  `Commands/`/`Queries/` subfolders) — the plan's proposed `Features/Help/` layout matches
  this exactly.
- **Root-mounted, `RequireAuthorization()`-group endpoints.** `NotificationEndpoints.cs` and
  `ConsultationEndpoints.cs` confirm the `MapGroup("/x").WithTags(...).RequireAuthorization()`
  shape, with per-route `.RequireAuthorization(PolicyNames.X)` overrides for writes — exactly
  what the plan's `HelpEndpoints.cs` sketch describes. `Program.cs` only needs one new line
  (`app.MapHelpEndpoints();`) alongside the other unversioned modules — MediatR handler
  discovery, FluentValidation discovery, and EF configuration discovery are all
  convention-over-registration (assembly scanning), so nothing else in `Program.cs` changes.
- **`ClaimsPrincipal principal` pass-through for role-based filtering** is the established
  mechanism for "same route, different rows per role" (`ListConsultationsQuery(paging, status, principal)`,
  `ListNotificationsQuery(paging, unreadOnly, principal)` in `ConsultationEndpoints.cs` /
  `NotificationEndpoints.cs`). The plan describes "filtering from the JWT role claim" in
  prose (§2/§4) but doesn't spell out this exact signature — worth carrying into the proposal
  as the concrete implementation detail for `ListHelpTopicsQuery`/`GetHelpTopicQuery`.
- **Seeder insert-if-missing pattern already exists.** `DatabaseSeeder.SeedRolesAsync`
  (`HouseCenter.Api/Data/DatabaseSeeder.cs:38-51`) is the literal pattern the plan's Phase 2
  T5 proposes to replicate for help topics (diff against existing, add only what's missing,
  never touch existing rows) — not a new idea being introduced into the seeder.
- **Test file naming/location.** `HouseCenter.Api.Tests/{Notification,Attachment,Report}Tests.cs`
  are flat files at the test project root — `HelpTopicTests.cs` (plan's T4) matches.
- **openspec proposal style.** `openspec/changes/sponsor-reports/proposal.md` and
  `attention-sessions/proposal.md` confirm the real proposal shape: `Change name / Status /
  Problem / Proposed change / Scope / Access control (or Entities affected) / Out of scope /
  Why this is P{n}`. The plan's own structure (Problem → Domain model → Module pattern → API
  surface → Taxonomy → Frontend contract → Phases → Open Questions) is richer than a normal
  proposal.md — expected, since it's pre-proposal research, not the proposal itself. The
  `sdd-propose` phase should compress it into the standard shape rather than copy it wholesale.

## Discrepancies / corrections

None found in substance. One naming nuance worth flagging so the proposal phase states it
explicitly rather than leaving it implicit:

- The plan's `HelpAudience` enum uses `Staff` as a value, while the real policy constant is
  `PolicyNames.StaffOnly` ("every role except Sponsor"). This is fine as a *new, independent*
  enum — `HelpAudience` is content-visibility metadata, not literally reused
  `PolicyNames` — but the plan's own framing ("mirrors the existing `PolicyNames` ladder
  exactly") slightly overstates it: names are similar, not identical, and the ladder direction
  is subtly different from ASP.NET role checks (audience is an *inclusive floor* per topic row,
  not a policy gate on the route). Recommend the proposal say explicitly: "`HelpAudience` is a
  new enum inspired by, but independent from, `PolicyNames`" — avoids a reader assuming they're
  interchangeable or that changing one affects the other.

Everything else — entity shape, config pattern, policy names, module absence, file layout,
endpoint mounting, seeder pattern, test naming — matches current code with no changes needed.

## Additional considerations for the proposal

- **Scope-down option for v1.** The plan's own P1 set (15 topics) is already a reasonable cut
  from the full 22-topic taxonomy, but even 15 topics is real authoring time (Spanish content
  writing, not just code). Consider whether the *first* PR should ship the module + only the
  3-5 topics covering the highest-friction workflows (`sessions.clinic-or-route`,
  `sessions.status-lifecycle`, `patients.assign-doctor`) with the rest seeded incrementally via
  the API post-launch — decouples "module works" (code review, fast) from "content is complete"
  (editorial, slow, not code-reviewable in the same sense). This changes the Review Workload
  Forecast for `sdd-tasks`: Phase 1 + partial-Phase-2 combined might still fit one PR if seed
  content is trimmed further.
- **Migration sequencing.** `HouseCenter.Api/Migrations/` shows one migration per change
  (`AddAttentionSessions`, `AddMedicalConsultations`, `AddNotifications`, ...) — confirms
  `AddHelpTopics` as a single, self-contained migration is the right unit, but it should land
  in the *same* PR as T1 (entity+config), not deferred, since `dotnet ef database update` is
  part of the acceptance loop other changes followed.
- **`HelpAudience` vs `PolicyNames` drift risk (future).** If a sixth role is ever added,
  `RoleNames.All`, the `AuthorizationBuilder` policies, and `HelpAudience`'s ladder table would
  all need updating in lockstep with no shared source of truth. Not a blocker for v1 (worth a
  one-line comment in `HelpTopicConfiguration`/`HelpAudience` pointing back at `PolicyNames`
  and `RoleNames` so a future role addition doesn't silently miss the audience ladder).
- **`openspec/IMPLEMENTATION_PLAN.md` is stale relative to this change** — its "still not yet
  planned" list doesn't mention Help Center, since this plan originated outside that roadmap.
  Not a blocker, but `sdd-archive` (or the proposal) should add a line there once this ships,
  to keep that doc current as it has been for every other change (attention-sessions,
  medical-consultations, sponsor-reports, notifications, file-uploads, housecenter-web).
- **Frontend contract (§6 of the plan) is out of this repo's scope** (`housecenter-web` is a
  separate change/repo per `CLAUDE.md`) — the proposal for `help-center` should scope itself
  to the `HouseCenter.Api` backend module only (Phases 1-2 of the plan) and treat Phase 3 as a
  *dependent, follow-up* change, consistent with how `sponsor-reports`/`attention-sessions`
  stayed backend-only and `housecenter-web` was its own separate SDD change.

## Recommendation

**Proceed to `sdd-propose`** using `HouseCenter.Api/docs/help-center-plan.md` as the base
input. No architectural rework needed — the plan's entity design, module pattern choice, API
surface, and phased structure all hold up against the real codebase. The proposal phase should:

1. Compress the plan into the standard `proposal.md` shape (Problem / Proposed change / Scope /
   Access control / Out of scope / Why this priority) — matching `sponsor-reports`/`attention-sessions`.
2. Scope `help-center` (this change) to Phase 1 + Phase 2 (backend module + seed content) only;
   treat frontend integration (Phase 3) as future, dependent work in `housecenter-web`.
3. Carry forward the plan's own 7 Open Questions unresolved — they're genuine product decisions
   (markdown vs rich text, i18n, analytics, versioning, unpublish/republish, public endpoint,
   images) that the proposal should either answer or explicitly defer.
4. State the `HelpAudience`-is-independent-from-`PolicyNames` clarification explicitly.
5. Weigh the P1-topic-count scope-down option above against the Review Workload Guard once
   `sdd-tasks` estimates line counts for Phase 2's seed content.
