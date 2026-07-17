# Spec Delta: Collaborator ↔ User Link

Formalizes requirements for proposal `collaborator-user-link` (Option B — Collaborator
becomes the field profile of a Member User). Covers one new capability and one modified
capability, per the proposal's Capabilities section.

## New Capability: `collaborator-user-link`

### Requirement: Collaborator-User 1:1 link
`Collaborator.UserId` MUST be a nullable FK to `User.Id`, unique at the database level —
at most one `Collaborator` per `User`; multiple `NULL` values MUST be allowed (legacy rows
pending backfill).

#### Scenario: A User cannot be claimed by two Collaborators
- GIVEN `User` U already has `Collaborator` C1 with `UserId = U.Id`
- WHEN a request attempts to set `UserId = U.Id` on a different `Collaborator` C2
- THEN the operation MUST fail with a conflict error

#### Scenario: Unmatched legacy row keeps `UserId = null`
- GIVEN a `Collaborator` predates this change with no email match to any `User`
- WHEN the backfill runs
- THEN `UserId` remains `null` and the row is retained, not deleted or deactivated

### Requirement: Unique email on Collaborator
`Collaborator.Email` MUST be unique at the database level, mirroring `User.Email`.

#### Scenario: Duplicate email rejected
- GIVEN an active `Collaborator` exists with email `x@y.com`
- WHEN `POST /collaborators` or `PUT /collaborators/{id}` submits `x@y.com` for a different collaborator
- THEN the request MUST fail with a conflict error before it can violate the DB constraint

#### Scenario: Pre-migration duplicates block deployment
- GIVEN two or more active `Collaborator` rows share the same email (case-insensitive) before the migration runs
- WHEN the unique-index migration is applied
- THEN it MUST fail fast — duplicates MUST be detected and deduplicated first, as a prerequisite step

### Requirement: Member signup links an existing Collaborator, never fabricates one
When a `User` is created with Role=Member via invitation signup, the system MUST attempt to
link an existing unlinked `Collaborator` by case-insensitive email match. Signup MUST NOT
fabricate a `Collaborator` roster row — `Collaborator.ClinicId` is required and signup
collects no clinic context, so any row it created would be invalid or need guessed data.

#### Scenario: No existing Collaborator matches the invited email
- GIVEN an invitation for Role=Member and no `Collaborator` shares that email (case-insensitive)
- WHEN the invitee completes signup
- THEN the `User` is created without a linked `Collaborator`, no roster row is fabricated,
  and the system MUST log a warning for admin follow-up (link later via `POST /collaborators`)

#### Scenario: An unlinked Collaborator matches the invited email
- GIVEN a `Collaborator` exists with `UserId = null` and the same email as the invitation
- WHEN the invitee completes signup
- THEN that existing `Collaborator` MUST be linked instead of creating a duplicate roster row

### Requirement: `POST /collaborators` resolves an optional User link
Creating a `Collaborator` via the roster-management endpoint MUST accept an optional
`UserId`, resolved in this order: (a) explicit `UserId` → validate the referenced `User`
exists, has Role=Member, and is not already linked; (b) no `UserId` → case-insensitive
email-match to an unlinked Role=Member `User` → link if found; (c) no match → create the
`Collaborator` with `UserId = null` (pending/legacy).

#### Scenario: Target User already has a linked Collaborator
- GIVEN the submitted `UserId` already has a linked `Collaborator`
- WHEN `POST /collaborators` is submitted
- THEN it MUST fail with a conflict error and no row is created

#### Scenario: Target User does not have Role=Member
- GIVEN the submitted `UserId` references a `User` whose roles do not include Member
- WHEN `POST /collaborators` is submitted
- THEN it MUST fail with a validation error

#### Scenario: No UserId and no email match creates a pending row
- GIVEN no `UserId` is submitted and no unlinked Role=Member `User` shares the submitted email
- WHEN `POST /collaborators` is submitted
- THEN the `Collaborator` MUST be created with `UserId = null`, treated as a legacy/pending
  row per the Legacy (unlinked) Collaborator visibility requirement

### Requirement: Legacy (unlinked) Collaborator visibility
A `Collaborator` with `UserId = null` MUST remain visible in roster reads and MUST be
exposed with an explicit "not linked" indicator, so it is neither hidden nor silently
treated as fully onboarded.

#### Scenario: Legacy collaborator flagged in listings
- GIVEN a `Collaborator` has `UserId = null`
- WHEN `GET /collaborators` or `GET /collaborators/{id}` is called
- THEN the response MUST indicate the row is unlinked

#### Scenario: Legacy collaborator has no session/consultation activity
- GIVEN a `Collaborator` has `UserId = null`
- WHEN roster or report data is read
- THEN it MUST show zero `AttentionSession`/`MedicalConsultation` activity for that row —
  no User account exists to have generated any, and this is expected, not an error

#### Scenario: `GET /collaborators/me` resolves via the link
- GIVEN the caller's `User` has a linked `Collaborator` (`Collaborator.UserId = caller.Id`)
- WHEN `GET /collaborators/me` is called
- THEN the linked `Collaborator` MUST be returned; if no link exists, 404 MUST be returned
  (no implicit email-based reconciliation at request time — that is a manual/admin action)

## Modified Capability: `sponsor-reports`

Collaborator identity/counts across all four report endpoints MUST resolve through the
`Collaborator.UserId` link, replacing today's split between direct `db.Collaborators`
reads and `AttentionSession.Collaborator` (a `User`) grouping, so counts reconcile.

### Requirement: Summary report collaborator count
`GET /reports/summary` MUST report a `Collaborators` count reflecting collaborators
resolvable through the User link, consistent with the session-derived collaborator activity
counted elsewhere in the same response.
(Previously: counted directly from `db.Collaborators` with no relation to session data.)

#### Scenario: Summary count matches session-derived collaborator activity
- GIVEN N distinct linked Collaborators generated sessions this month
- WHEN `GET /reports/summary` and `GET /reports/sessions` are both called
- THEN the collaborator population implied by both MUST be derived from the same link-based source

### Requirement: Clinic and work-route collaborator detail
`GET /reports/clinics/{id}` and `GET /reports/work-routes/{id}` MUST resolve
`collaboratorNames`/counts through the User link when producing Administrator/Owner detail
projections; anonymized projections for other roles are unchanged.
(Previously: names/counts sourced directly from `db.Collaborators`, independent of the User link.)

#### Scenario: Admin views clinic detail including a legacy collaborator
- GIVEN a clinic has one linked and one legacy (unlinked) Collaborator
- WHEN Administrator calls `GET /reports/clinics/{id}`
- THEN both MUST be counted in `collaboratorsAssigned`; the legacy row's name MUST still
  appear (roster membership, not session activity, drives this count)

### Requirement: Session period report collaborator breakdown
`GET /reports/sessions` MUST resolve the Admin/Owner-only `SessionsByCollaborator`
breakdown by joining `AttentionSession.Collaborator` (User) through the link back to its
`Collaborator` roster identity, so names/labels match the roster used elsewhere.
(Previously: grouped directly by `AttentionSession.Collaborator`, a `User`, with no
join back to roster identity — same person could be labeled differently than in R1–R3.)

#### Scenario: Session breakdown label matches roster name
- GIVEN a linked Collaborator recorded 3 sessions this period
- WHEN Administrator calls `GET /reports/sessions`
- THEN the per-collaborator label in the breakdown MUST match the collaborator's roster
  name (`Collaborator.FirstName`/`LastName`), not the raw `User` display name
