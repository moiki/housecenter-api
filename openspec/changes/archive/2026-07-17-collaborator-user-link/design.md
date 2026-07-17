# Design: Collaborator ↔ User Link (1:1)

## Technical Approach

`Collaborator` becomes the field profile of a Member `User`, joined by a nullable
`Collaborator.UserId` FK (1:1, partial-unique). The link is set at whichever event
completes the pair: `POST /collaborators` (admin/roster path) or Member signup
(`AuthService.SignupAsync`). Reports resolve the collaborator display identity through
this single join; roster counts and session-activity counts keep distinct semantics but
reconcile by name. Rollout is two additive migrations plus an idempotent reconciliation
pass so real data is never mutated destructively by a schema step.

## Architecture Decisions

### Decision 1 — Provisioning: roster-first link, no fabricated stub at signup

**Choice**: `POST /collaborators` is the authoritative creation path (it has `ClinicId`,
which `Collaborator` requires). Add optional `UserId` to `CreateCollaboratorRequest`,
resolved in this order: (a) explicit `UserId` → validate User exists, is Member,
unlinked; (b) null `UserId` → case-insensitive email-match to an unlinked Member User →
link; (c) no match → create with `UserId = null` (pending). Member `SignupAsync`, after
creating the User, links an existing unlinked `Collaborator` by lowercased email match; if
none exists it leaves the User unlinked and logs a warning for admin follow-up. Signup does
**not** fabricate a Collaborator.

**Alternatives considered**: signup auto-creates a stub (needs a `ClinicId` it does not
have → forces `Collaborator.ClinicId` nullable or a sentinel clinic, both out of scope and
report-polluting); carry `ClinicId` on the invitation (changes `Invitation` entity + invite
flow, heavier than the slice).

**Rationale**: admins own clinic/route assignment (domain actor table), so the roster row
must be created with real clinic context, not guessed. Link-on-signup covers the intended
"invite an already-rostered person" flow; the unlinked-User case is a small, enumerable
anomaly instead of dirty roster data.

### Decision 2 — Backfill: realign seed, email-match real data

**Seed** (`TestDataSeeder`): realign the two field-staff `Collaborator` seeds to mirror the
Member Users in both email and name (`member@`→Carlos López, `member2@`→María Gutiérrez),
set `UserId` directly during seeding, and keep one intentional roster-only legacy row
(`pedro.castillo@`, no User) to exercise the legacy path. Result: zero accidental legacy
rows, one deliberate one.

**Real data** — reconciliation algorithm (idempotent):
1. Normalize: `UPDATE "Collaborators" SET "Email" = lower("Email")`.
2. Match: for each unlinked active `Collaborator`, find an unlinked Member `User` with
   equal lowercased email → set `Collaborator.UserId`. One-to-one; skip if either side is
   already linked (unique index enforces at write).
3. Report gaps: (a) Collaborators with no matching User → stay legacy; (b) Member Users with
   no Collaborator → emit to log/report for admin resolution via `POST /collaborators`.
   No blind prod stubs (no clinic to assign).

**Rationale**: realigning the seed is cleaner than seeding stubs and gives deterministic
fixtures; prod stays non-destructive and human-reconciled.

### Decision 3 — Migration: two additive steps + reconciliation gate

| Step | Action | Safety |
|------|--------|--------|
| Migration A `AddCollaboratorUserLink` | add nullable `UserId` + FK→`Users`(`OnDelete: SetNull`) + **partial-unique** index `UserId WHERE "UserId" IS NOT NULL` | always safe; allows many NULLs |
| Reconciliation (Decision 2) | lowercase emails, email-match backfill, gap report | idempotent, re-runnable |
| Migration B `AddCollaboratorEmailUnique` | plain unique index on `Email` | runs only after reconcile confirms zero dupes; **fails loudly** on remaining dupes — that failure is the safety gate |

Emails are normalized to lowercase on every write (Create/Update/signup/seed/backfill) so a
plain unique index gives case-insensitive uniqueness without `citext` or a functional index.
Column casing is PascalCase (no snake_case convention), so the partial filter is
`"UserId" IS NOT NULL`.

**Rationale**: separating the always-safe additive column from the dedupe-dependent unique
index keeps each migration deterministic and lets destructive dedupe decisions stay
human-reviewed instead of auto-executed inside a schema migration.

### Decision 4 — Reports: one canonical name source via the link

The two count families answer different questions and stay distinct, but **names resolve
through the link only**:
- Roster counts (`GetSummary`/`GetClinic`/`GetWorkRoute` "collaborators") = active
  `Collaborator` rows by clinic/route, **including legacy unlinked rows** (real staff).
- Session-activity names (`GetSessionPeriod` `byCollaborator`, and management name lists)
  group by `AttentionSession.CollaboratorId` (a `User`) and left-join `Collaborator` on
  `c.UserId == s.CollaboratorId`; display name = linked Collaborator name when present, else
  fall back to the User name. Concrete join:

```csharp
from s in sessions
join c in db.Collaborators on s.CollaboratorId equals c.UserId into gj
from c in gj.DefaultIfEmpty()
group s by new {
    s.CollaboratorId,
    Name = c != null ? c.FirstName + " " + c.LastName
                      : s.Collaborator.FirstName + " " + s.Collaborator.LastName
} into g
select new { g.Key.Name, Count = g.Count() }
```

`CollaboratorService.GetMeAsync` stops matching by email (the documented "first match"
workaround) and matches `c.UserId == <principal NameIdentifier>` — the unique link removes
the ambiguity.

**Rationale**: unification means one identity source for names, not collapsing two
legitimately different metrics; documented delta: roster count ≥ collaborators-with-sessions.

### Decision 5 — Legacy rows: asymmetric rule

| State | Rule |
|-------|------|
| `Collaborator` with `UserId = null` | **Valid permanently** — roster-only staff (admin/support who never log in, or historical). Counted in roster, excluded from anything needing login. |
| Member `User` with no `Collaborator` | **Transient anomaly** — must be resolved; enumerated by reconciliation, warned at signup, admin links via `POST /collaborators`. |

## Data Flow

```
Admin ─POST /collaborators─→ CollaboratorService ─(resolve UserId: explicit│email│null)─→ Collaborator
Invitee ─signup─→ AuthService ─create User(Member)─→ link existing Collaborator by email │ warn if none
AttentionSession.CollaboratorId (User) ──link (Collaborator.UserId)──→ Collaborator (name source)
Reports ──roster count(Collaborators)  +  session names(join via link)──→ reconciled response
```

## File Changes

| File | Action | Description |
|------|--------|-------------|
| `Domain/Entities/Collaborator.cs` | Modify | Add `Guid? UserId` + `User? User` nav |
| `Domain/Entities/User.cs` | Modify | Add inverse `Collaborator? Collaborator` nav |
| `Data/Configurations/EmployeeConfiguration.cs` | Modify | `HasOne(c=>c.User).WithOne(u=>u.Collaborator).HasForeignKey<Collaborator>(c=>c.UserId).OnDelete(SetNull)`; partial-unique `UserId` index (Email unique index deferred to Phase 3/Migration B). |
| `Migrations/*_AddCollaboratorUserLink` | Create | Nullable FK + partial-unique index |
| `Migrations/*_AddCollaboratorEmailUnique` | Create | Unique `Email` index (post-reconcile) |
| `Features/Collaborators/CollaboratorDtos.cs` | Modify | `CreateCollaboratorRequest.UserId` (optional); `CollaboratorResponse.UserId` |
| `Features/Collaborators/CollaboratorService.cs` | Modify | Link/resolve on create; lowercase email; `GetMeAsync` by `UserId`; email-uniqueness error |
| `Features/Collaborators/CollaboratorValidators.cs` | Modify | Optional `UserId` rule |
| `Features/Auth/AuthService.cs` | Modify | Member signup links Collaborator by email or warns |
| `Features/Seeding/TestDataSeeder.cs` | Modify | Realign 2 collaborators to Member Users + set `UserId`; keep 1 legacy |
| `Features/Reports/GetSessionPeriodReport.cs` / `GetClinicReport.cs` / `GetWorkRouteReport.cs` | Modify | Name via link join; documented count semantics |

## Interfaces / Contracts

```csharp
// Collaborator.cs
public Guid? UserId { get; set; }
public User? User { get; set; }

// EmployeeConfiguration.cs
builder.HasIndex(c => c.UserId).IsUnique().HasFilter("\"UserId\" IS NOT NULL");
builder.HasIndex(c => c.Email).IsUnique();

// CreateCollaboratorRequest: append optional Guid? UserId
// CollaboratorResponse: append Guid? UserId  (null == legacy/roster-only)
```

New error: `collaborators.email_taken` (Conflict) and `collaborators.user_already_linked`
(Conflict) replace the "first match" degradation.

## Testing Strategy

| Layer | What | Approach |
|-------|------|----------|
| Unit | Reconciliation match/gap logic; email-normalize | InMemory EF fixtures |
| Integration | `POST /collaborators` with/without `UserId`; duplicate email → 409; signup links roster row; signup with no roster → unlinked + warn | WebApplicationFactory |
| Integration | Summary vs session-period counts reconcile; legacy Collaborator absent from session names; unlinked-User session falls back to User name | Seeded dataset (2 linked + 1 legacy) |
| Migration | Partial-unique allows many NULLs; email unique index fails on dupes pre-reconcile | Test DB apply |

## Migration / Rollout

1. Apply Migration A (safe, additive). 2. Run reconciliation (seed realign is automatic on
`POST /seed`; prod via the reconciliation pass). 3. Confirm zero duplicate emails. 4. Apply
Migration B. Rollback: down-migrations drop the `Email` unique index then the `UserId`
column/index; `User`/`Collaborator` rows untouched → no data loss. Revert
enrollment/reports commits to restore prior behavior.

## Open Questions

- [ ] Should the "Member User without Collaborator" gap surface via an admin endpoint/report now, or only structured logs (deferred)?
- [ ] Reconciliation trigger for prod: one-off maintenance endpoint vs SQL script — pick during tasks.
