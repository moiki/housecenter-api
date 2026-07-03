# SDD Proposal — Help Center

## Change name
`help-center`

## Status
`proposed` (2026-07-03)

## Problem

The system now covers the full domain, but nothing in the system explains the system.
Every user learns by asking someone, and the user base makes that expensive:

- **Members** (field collaborators) rotate, work from rural clinics with spotty internet,
  and have nobody to explain why a session needs a clinic *or* a route, or why a
  `Completed` session can't return to `Scheduled`. "Ask on WhatsApp" is not realistic.
- **Doctors** are remote and can't discover what the consultation lifecycle means or why
  their view is scoped.
- **Sponsors** see only aggregate reports — intentional, but reads as "the app is broken"
  without an in-app explanation of the role-based scope.
- **Admins/Owner** run the most error-prone workflows (invitations, role assignment, doctor
  assignment) with zero in-app guidance.

The owner's request: a **persisted, editable, per-module usage guide**, surfaced later as
tooltips. It is DB-backed (not static docs) because content must be editable without a
redeploy, must be **role-filtered** (Sponsors do not read staff workflow guides), and must
anchor to stable **topic keys** the frontend can rely on. This is Clinics-shaped CRUD plus
one role filter.

## Proposed change

Add a `HelpTopic` entity and a `HelpAudience` enum, exposed through a new CQRS module
`Features/Help/` (CQRS by default for new modules). Five root-mounted endpoints, one
additive migration, and baseline seed content. **Backend only** — see Out of scope.

## Entities affected

### New: `HelpTopic : BaseEntity`
```
TopicKey     (string, required, unique)  — stable, dot-namespaced: "sessions.status-lifecycle"
Title        (string, required)          — tooltip/guide heading
Summary      (string, required, ≤300)    — tooltip text, plain
Body         (string, required, text)    — full guide content, markdown
Audience     (HelpAudience)              — role-visibility gate, stored as string
Category     (string, required)          — grouping for the guide index
DisplayOrder (int)                        — order within a category
IsActive / CreatedDate / UpdatedDate      — from BaseEntity
```
Config via `HelpTopicConfiguration` (auto-discovered): enum-as-string, unique index on
`TopicKey`, max lengths. `TopicKey` is **immutable on update** (it is a frontend contract).

### New: `HelpAudience` enum
```
{ All, Staff, MemberOrAbove, AdministratorOrAbove, OwnerOnly }
```
**`HelpAudience` is a new enum inspired by, but independent from, `PolicyNames`.** It is
per-row content-visibility metadata (an inclusive floor), *not* a route policy gate and not
the same constant — changing one does not affect the other. A one-line comment will point at
`PolicyNames`/`RoleNames` so a future 6th role updates both in lockstep.

## Access control

| Endpoint | Read/Write | Policy |
|---|---|---|
| `GET /help/topics` | read (summaries, role-filtered) | any authenticated role |
| `GET /help/topics/{topicKey}` | read (full body; 404 if outside audience) | any authenticated role |
| `POST /help/topics` | write (409 on duplicate/soft-deleted key) | `AdministratorOrAbove` |
| `PUT /help/topics/{id:guid}` | write | `AdministratorOrAbove` |
| `DELETE /help/topics/{id:guid}` | write (soft delete = unpublish) | `AdministratorOrAbove` |

Audience ladder (filtered in the query handler from the JWT role claim, via the established
`ClaimsPrincipal` pass-through): Sponsor→`All`; Doctor→`+Staff`; Member→`+MemberOrAbove`;
Admin→`+AdministratorOrAbove`; Owner→all. Out-of-audience by-key reads return **404** (not
403) so existence isn't leaked.

## Scope

### In scope (this change — plan Phases 1–2)
- `HelpTopic` entity + `HelpAudience` enum + `HelpTopicConfiguration` + `AddHelpTopics` migration
- CQRS handlers (`Features/Help/`, flat layout): List, GetByKey, Create, Update, Delete
- `HelpEndpoints.cs` + one wire line in `Program.cs`; FluentValidation on request DTOs
- Integration tests (`HelpTopicTests.cs`): audience filtering, 404 leak-guard, duplicate-key
  (incl. soft-deleted key → 409), key-format, key-immutability, soft-delete-then-404
- Baseline **Spanish** seed content via `DatabaseSeeder` insert-if-missing (never clobbers admin edits)

### Out of scope
- **Frontend integration (plan Phase 3): `HelpTooltip`, `/help` pages, hooks, anchors.** This
  is a separate, dependent follow-up change against `housecenter-web` — the same split used by
  `sponsor-reports` / `attention-sessions` (backend-only) and `housecenter-web` (its own change).
- Admin authoring UI (content is editable via the API meanwhile)
- Public/anonymous endpoint; view analytics; revision history; images in bodies (see Open questions)

## Priority content set (P1 — the field-collaborator cluster is the payoff)

`sessions.recording`, `sessions.clinic-or-route`, `sessions.status-lifecycle`,
`treatments.overview`, `treatments.status-lifecycle`, `treatments.details-vs-comments`,
`consultations.overview`, `consultations.status-lifecycle`, `patients.attention-types`,
`patients.assign-collaborator`, `patients.assign-doctor`, `work-routes.overview`,
`work-routes.destinations`, `users.roles`, `invitations.overview`, `auth.signup-invitation`,
`reports.role-scope`. (Full 22-topic taxonomy with per-row reasoning lives in
`HouseCenter.Api/docs/help-center-plan.md` §5 — the permanent reference.)

**Delivery decision (adopts the exploration's scope-down):** the first PR ships the module +
migration + tests + a **starter seed of the 3–5 highest-friction topics** (`sessions.*`,
`patients.assign-doctor`, `treatments.status-lifecycle`). The remaining P1 topics are authored
incrementally afterward via the seeder/API as **editorial** work. Rationale: it decouples
"module works" (fast, code-reviewable) from "content complete" (slow, not code-review-shaped)
and keeps the first PR near the 400-line review budget. `sdd-tasks` should forecast against
this split, not against all 17 topics landing in one PR.

## Open questions (positions taken)

| # | Question | Position |
|---|---|---|
| 1 | Markdown vs rich text for `Body` | **Resolved: markdown** — matches team norms, one safe frontend renderer, no legacy-HTML repeat. |
| 2 | Language(s) | **Resolved: Spanish-only v1** — field users are Nicaraguan. i18n later is additive: `Language` column + composite unique `(TopicKey, Language)`. |
| 3 | Topic view analytics | **Resolved: no for v1** — a write-per-read pollutes a pure query for marginal value. |
| 4 | Content versioning / audit | **Resolved: no for v1** — `UpdatedDate` is the answer; full audit trail would be a platform-wide feature. |
| 5 | Reactivate an unpublished topic | **Deferred — decision needed from owner.** v1 default: no un-delete endpoint (matches Clinics/Patients soft delete); duplicate-key create returns 409-with-explanation. A `publish` toggle would give `IsActive` published-flag semantics — confirm if wanted. |
| 6 | Public (anonymous) `All`-audience endpoint | **Deferred (out of v1).** Flag if sponsor-recruitment/landing content is wanted, so the endpoint split is planned, not bolted on. |
| 7 | Images/diagrams in bodies | **Deferred.** Text-first is lighter for rural bandwidth; markdown can embed image URLs (or Attachments with `ownerType: "HelpTopic"`) additively later. |

## Affected modules / packages

Mainly additive; **zero changes to existing modules' logic.**

| Area | Impact |
|---|---|
| `Domain/Entities/HelpTopic.cs`, `HelpAudience.cs` | New |
| `Data/Configurations/HelpTopicConfiguration.cs` | New |
| `Features/Help/*` (DTOs, validators, endpoints, 5 handlers) | New |
| `Migrations/…_AddHelpTopics.cs` | New (self-contained, lands with the entity PR) |
| `HouseCenter.Api.Tests/HelpTopicTests.cs` | New |
| `Data/HouseCenterDbContext.cs` | +1 line (`DbSet<HelpTopic>`) |
| `Program.cs` | +1 line (`app.MapHelpEndpoints()`) |
| `Data/DatabaseSeeder.cs` | +1 insert-if-missing block (widens charter to bootstrap content; `POST /seed` untouched) |
| `CLAUDE.md` module table, `openspec/IMPLEMENTATION_PLAN.md` | +1 row / +1 line (docs) |

No new NuGet packages (markdown rendering is a frontend concern, out of scope here).

## Rollback plan

**Low-risk** — this is a new, isolated module with an additive migration touching no existing
table or column. To revert: `dotnet ef migrations remove` (or a down migration dropping the
`HelpTopics` table), delete `Features/Help/`, remove the `DbSet<HelpTopic>` line, remove the
`app.MapHelpEndpoints()` line, and drop the seeder block. No data migration, no schema change
to existing entities, no consumer in this repo (the frontend is a separate future change), so
rollback has no blast radius beyond the new module.

## Why this priority

Independent, additive, and unblocking — it depends on nothing and nothing depends on it, so it
can be scheduled flexibly. High leverage now: the system is feature-complete and real users
(rotating collaborators, remote doctors, first-time sponsors) onboard next, and content is
cheapest to write while the specs are fresh (Phase 2 largely translates them). Every week
without it converts into WhatsApp support load on the Owner. Small blast radius (one
Clinics-shaped module, no new architectural patterns) keeps the cost low.
