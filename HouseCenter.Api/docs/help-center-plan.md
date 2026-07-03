# Implementation Plan — Help Center (in-app user guide + module tooltips)

## Change name
`help-center` (formalized as `openspec/changes/help-center/`)

## Status
Draft plan — input to the `/sdd-new help-center` proposal (2026-07-03). Drafted by the Fable model
from a full inventory of `HouseCenter.Api` endpoints, `CLAUDE.md`'s locked conventions, and the
existing `openspec/changes/*` proposal style.

---

## 1. Problem

HouseCenter now covers the full domain (Phases 1–5 + Domain V2 + notifications + attachments),
but nothing in the system explains the system. Every user learns by asking someone — and the
user base makes that expensive:

- **Field collaborators (Member)** rotate. A new collaborator at a rural clinic with spotty
  internet has nobody sitting next to them to explain why a session needs a clinic *or* a
  work route, or why a `Completed` session can never go back to `Scheduled`. The same
  connectivity argument that justified the notifications change ("check the app constantly
  is not realistic") applies here: "ask the admin on WhatsApp" is not realistic either.
- **Doctors** are remote by definition. They see `/consultations` and a patient list scoped
  to their assignments, and have no way to discover what the consultation lifecycle means
  or why they can't see work routes.
- **Sponsors** see only aggregate `/reports` — which is *intentional*, but reads as "the app
  is broken" unless someone explains the role-based scope to them.
- **Admins/Owner** manage invitation flows, role assignment, and doctor assignment — the
  three most role-gated, error-prone workflows in the system — with zero in-app guidance.

The owner's request: a persisted usage guide with per-module topics, surfaced as tooltips
(summary + "Read more" CTA) on the modules/actions that need them.

### Why DB-backed, not static docs

A markdown file in the repo (or hardcoded frontend strings) fails the actual requirement:

1. **"Persist the guide" means editable without a redeploy.** The NGO's workflows change
   (new clinics, new route policies); the Owner/Admin must fix guide text the same way they
   fix a clinic address — through the API. Everything else in this project that users manage
   lives in Postgres via EF Core; guide content is user-managed content, not code.
2. **Role-filtered visibility requires a query.** Sponsors should not read collaborator
   field-workflow guides (same privacy posture as `sponsor-reports`: "a layer between *I see
   everything* and *I see nothing*"). Static files can't project by role; the Reports module
   already established role-based projection as the pattern for exactly this.
3. **Stable topic keys are an API contract.** The frontend anchors tooltips to keys like
   `sessions.status-lifecycle`. Keys must be validated, unique, and survive content edits —
   that's a unique-indexed column, not a filename convention.

This is the same shape as the existing catalog modules: small bounded dataset, CRUD for
admins, filtered reads for everyone — Clinics with a role filter, essentially.

---

## 2. Domain model

### New entity: `HelpTopic`

Name follows existing entity naming (`Notification`, `FileAttachment`, `Invitation` — plain
domain nouns in `Domain/Entities/`). "Topic" matches the owner's own wording ("specific
topics").

```
HelpTopic : BaseEntity
├── Id (Guid)                    — from BaseEntity
├── TopicKey (string, required)  — stable, dot-namespaced, unique: "sessions.status-lifecycle"
├── Title (string, required)     — "Estados de una sesión" — heading in tooltip and guide page
├── Summary (string, required)   — the tooltip text; max 300 chars, plain text
├── Body (string, required)      — full guide content, markdown; unbounded text column
├── Audience (HelpAudience)      — role-visibility gate, enum stored as string
├── Category (string, required)  — grouping for the guide index page: "Patients", "Sessions"…
├── DisplayOrder (int)           — ordering within a category on the /help index
└── IsActive / CreatedDate / UpdatedDate — from BaseEntity
```

**Configuration:** `HelpTopicConfiguration : IEntityTypeConfiguration<HelpTopic>` — same
per-entity Fluent API pattern as every other entity. Enum as string via
`HasConversion<string>()` (locked convention). Unique index on `TopicKey`. Max lengths:
`TopicKey` 120, `Title` 150, `Summary` 300, `Category` 60; `Body` unbounded (`text`).

### `HelpAudience` enum

Mirrors the existing `PolicyNames` ladder exactly, so visibility rules stay in one mental
model instead of inventing a second permission system:

```
HelpAudience { All, Staff, MemberOrAbove, AdministratorOrAbove, OwnerOnly }
```

| Reader role | Sees topics with Audience ∈ |
|---|---|
| Sponsor | `All` |
| Doctor | `All`, `Staff` |
| Member | `All`, `Staff`, `MemberOrAbove` |
| Administrator | `All`, `Staff`, `MemberOrAbove`, `AdministratorOrAbove` |
| Owner | everything |

So yes — Sponsors do **not** see collaborator-only topics. Same reasoning as sponsor-reports
access control: sponsor-visible content is curated (`All` audience: reports scope, login,
what the platform does), staff workflow detail is `Staff`+. Filtering happens in the query
handler from the JWT role claim, exactly like `GetSessionPeriodReport` projects differently
per role.

### Hierarchy: namespaced keys, not a parent FK

The "Patients > Treatments > Creating a treatment" grouping is expressed by:
- `TopicKey` namespacing (`treatments.status-lifecycle` — first segment is the module), and
- `Category` + `DisplayOrder` for the guide index page.

No self-referencing `ParentTopicId`. A ~25-topic catalog doesn't justify tree traversal
logic, and the flat + namespaced approach matches how the system already handles owned
structure when it's cheap (WorkRoute destinations are owned JSON precisely to avoid
entity overhead for simple shapes). If the catalog ever grows real nesting needs, a parent
key can be added additively.

### Soft-delete implications (two deliberate consequences)

1. **DELETE = unpublish.** Soft-deleting a topic (`IsActive = false`) removes it from all
   reads via the global query filter — the frontend degrade contract (§6) means tooltips
   anchored to that key silently disappear rather than break. This makes unpublishing safe.
2. **Gotcha — unique index vs soft delete:** the unique index on `TopicKey` still covers
   inactive rows (indexes don't respect the global query filter). So re-creating a topic
   with a previously soft-deleted key throws a unique violation. Rule: the create handler
   checks for an existing row **with `IgnoreQueryFilters()`**; if an inactive row holds the
   key, return `Error.Conflict` with a message telling the admin the topic exists but is
   unpublished (reactivation is an open question, §8). This is the documented pattern for
   bypassing the filter (CLAUDE.md: "use `.IgnoreQueryFilters()` to bypass").

---

## 3. Module pattern decision

| Module | Pattern | Reason |
|---|---|---|
| HelpTopics | **CQRS** | New, clean slate — CQRS by default. Pure content CRUD + read-heavy lookups, no cross-service coordination (no email, no token state, no tracked child collections). Same profile as Clinics, the reference CQRS implementation. |

Justified against the existing table's own logic: the Service-pattern modules earn it via
transactional coordination (Auth/Invitations coordinate email sends; WorkRoutes needs
wholesale-replace on owned JSON; Collaborators does clear+add on tracked positions).
HelpTopics has none of that — it is Clinics-shaped CRUD plus one role filter in the list
query. Files follow the newer flat CQRS layout used by Reports/Notifications (one
operation per file) rather than Clinics' `Commands/`/`Queries/` subfolders:

```
Features/Help/
├── HelpTopicDtos.cs
├── HelpTopicValidators.cs
├── HelpEndpoints.cs
├── ListHelpTopics.cs        — query + handler (role-filtered summaries)
├── GetHelpTopic.cs          — query + handler (full body, by key)
├── CreateHelpTopic.cs       — command + handler
├── UpdateHelpTopic.cs       — command + handler
└── DeleteHelpTopic.cs       — command + handler (soft delete)
```

---

## 4. API surface

Mounted at root via `app.MapHelpEndpoints()` like every post-Clinics module (Sessions,
Consultations, Reports, Notifications, Attachments are all root-mounted; only Clinics sits
under `/api/v{version}` as the versioning demonstration). Result Pattern + `ToHttpResult()`
/ `ToProblem()` throughout; FluentValidation via `.WithValidation<T>()`.

```
GET    /help/topics                      → 200 List<HelpTopicSummaryResponse>   [any authenticated role]
GET    /help/topics/{topicKey}           → 200 HelpTopicResponse | 404          [any authenticated role]
POST   /help/topics                      → 201 HelpTopicResponse | 409 dup key  [AdministratorOrAbove]
PUT    /help/topics/{id:guid}            → 200 HelpTopicResponse                [AdministratorOrAbove]
DELETE /help/topics/{id:guid}            → 204 (soft delete = unpublish)        [AdministratorOrAbove]
```

Notes:

- **Reads by key, writes by id.** The frontend only knows topic keys; admin tooling operates
  on the entity like everywhere else (Guid routes). The `{id:guid}` constraint disambiguates
  the two `/help/topics/{...}` GET/PUT shapes for minimal API routing.
- **List returns summaries only** — `TopicKey, Title, Summary, Category, DisplayOrder`, no
  `Body`. The frontend fetches this once per session to power every tooltip on every page;
  keeping it body-free keeps the payload small, which matters on rural connections. Full
  `Body` ships only on the per-topic GET when the user actually opens the guide page.
- **No pagination on the list.** Bounded catalog (~25 topics) — same precedent as
  `GET /roles` (trivial catalog read). Optional `?category=` filter for the guide index.
- **Role filtering on reads:** list handler filters by the audience ladder (§2); the by-key
  GET returns **404** (not 403) for a topic outside the caller's audience — don't leak that
  a staff-only topic exists to a Sponsor. (Contrast: sessions return 403 to Sponsors because
  the *route itself* is forbidden; here the route is open to all roles and visibility is
  per-row, so NotFound is the honest answer.)
- **Writes are AdministratorOrAbove** — guide content is day-to-day management, same tier as
  Clinics/WorkRoutes/Collaborators CRUD. Not OwnerOnly; the Owner shouldn't be a bottleneck
  for fixing a typo in a guide.

### Should reads be public (unauthenticated)?

**No — `RequireAuthorization()` on the whole group.** Reasoned explicitly:

1. Role filtering *requires* an authenticated identity; an anonymous endpoint would have to
   serve `All`-audience content only, creating a second code path for marginal benefit.
2. The guide documents internal NGO workflows and role structure — not secret, but not
   something to index publicly either (same conservative posture the project takes with
   patient data generally).
3. The deep-link UX still works: `housecenter-web` already has `RequireAuth` redirect
   plumbing. An unauthenticated user opening `/help/sessions.recording` from a shared link
   lands on `/login` and continues to the guide after signing in — standard
   redirect-after-login, no API change needed.

If a public "what is HouseCenter" page is ever wanted (e.g. for sponsor recruitment), that's
a separate anonymous endpoint over `Audience = All` topics — listed in Open Questions, not v1.

### DTOs

```csharp
record HelpTopicSummaryResponse(
    string TopicKey, string Title, string Summary, string Category, int DisplayOrder);

record HelpTopicResponse(
    Guid Id, string TopicKey, string Title, string Summary, string Body,
    string Audience, string Category, int DisplayOrder, DateTime UpdatedDate);

record CreateHelpTopicRequest(
    string TopicKey, string Title, string Summary, string Body,
    HelpAudience Audience, string Category, int DisplayOrder);

record UpdateHelpTopicRequest(   // TopicKey immutable after create — it's a frontend contract
    string Title, string Summary, string Body,
    HelpAudience Audience, string Category, int DisplayOrder);
```

`TopicKey` is **immutable on update** — renaming a key silently kills every tooltip anchored
to it. Changing a key = create new + unpublish old, a deliberate two-step.

### Validation rules

| Field | Rule |
|---|---|
| `TopicKey` | NotEmpty, MaxLength 120, matches `^[a-z0-9]+(-[a-z0-9]+)*(\.[a-z0-9]+(-[a-z0-9]+)*)+$` (lowercase, dot-namespaced, at least `module.topic`) |
| `Title` | NotEmpty, MaxLength 150 |
| `Summary` | NotEmpty, MaxLength 300 — it must fit a tooltip |
| `Body` | NotEmpty, MaxLength 20000 |
| `Audience` | IsInEnum |
| `Category` | NotEmpty, MaxLength 60 |
| `DisplayOrder` | ≥ 0 |

### Scenarios (spec-style)

```
SCENARIO: sponsor lists help topics
  GIVEN 20 topics exist: 4 with Audience "All", 16 with "Staff" or narrower
  AND the authenticated user has Sponsor role
  WHEN GET /help/topics
  THEN 200 with exactly the 4 "All" topics; no staff workflow content

SCENARIO: sponsor deep-links a staff-only topic
  GIVEN topic "sessions.recording" exists with Audience "Staff"
  WHEN GET /help/topics/sessions.recording as Sponsor
  THEN 404 (existence not leaked)

SCENARIO: admin creates a topic over a soft-deleted key
  GIVEN topic "reports.role-scope" was soft-deleted (IsActive = false)
  WHEN POST /help/topics with topicKey "reports.role-scope"
  THEN 409 Conflict — "topic exists but is unpublished"

SCENARIO: tooltip anchored to an unpublished topic
  GIVEN the frontend has a HelpTooltip with topicKey "clinics.overview"
  AND that topic is soft-deleted
  WHEN the clinics page renders
  THEN no help icon renders for that anchor; the page is otherwise unaffected
```

---

## 5. Topic taxonomy — which modules/actions get tooltips

The rule used below: a tooltip earns its place when the action is **workflow-critical**
(field data quality depends on doing it right), **role-gated in a non-obvious way** (the
user can see it but not use it, or sees less than they expect), or **carries domain
semantics the UI can't self-explain** (status machines, either/or rules, medical vs
educational typing). Trivial CRUD, universal UX patterns (login, bell icon), and plumbing
endpoints get nothing — a system where everything has a tooltip has no tooltips.

Full inventory pass (module-level rows, action rows where an action deserves its own topic):

| Module / action | Tooltip? | Reasoning | `topic_key` | Audience |
|---|---|---|---|---|
| Auth: login / refresh / me | No | Universal pattern; refresh + me are invisible plumbing | — | — |
| Auth: signup (invitation token) | **Yes** | Invite-only signup confuses new users — "where do I register?" is *the* first-contact question | `auth.signup-invitation` | All |
| Auth: password request/reset | No | Standard forgot-password flow, self-explanatory | — | — |
| Users: CRUD | No | Plain admin table | — | — |
| Users: PUT /{id}/roles | **Yes** | Role ladder is domain-specific — what Member vs Doctor vs Sponsor *means* here; Owner-only action | `users.roles` | AdministratorOrAbove |
| Users: PATCH /me/theme | No | A toggle is a toggle | — | — |
| Invitations (whole flow: create/resend/revoke/validate) | **Yes** | Workflow-critical + role-gated: pick the right role at invite time, token expiry, resend vs revoke semantics | `invitations.overview` | AdministratorOrAbove |
| Roles: GET catalog | No | Internal lookup feeding dropdowns | — | — |
| Clinics: CRUD | Maybe | CRUD is trivial, but "what is a clinic vs a work route" is real domain context for new staff — one module-level topic, no per-action topics | `clinics.overview` | Staff |
| WorkRoutes: CRUD | **Yes** (module) | Same clinic-vs-route context + routes drive session location rules | `work-routes.overview` | Staff |
| WorkRoutes: destination editing | **Yes** | Wholesale-replace semantics (owned JSON — the PUT replaces the *entire* destination list) is genuinely non-obvious and destructive if misunderstood | `work-routes.destinations` | AdministratorOrAbove |
| Collaborators: CRUD | Maybe | The Collaborator (staff record) vs User (login) distinction trips people up — module-level topic | `collaborators.overview` | AdministratorOrAbove |
| Collaborators: positions | Maybe | Clear+add replace semantics, similar to destinations but lower stakes | `collaborators.positions` | AdministratorOrAbove |
| Patients: CRUD / full-summary | Maybe | List/detail are self-evident; one module topic for enrollment context (clinic and/or route assignment) | `patients.overview` | Staff |
| Patients: PrimaryAttentionType | **Yes** | Medical vs EducationalReinforcement typing cascades into treatments, sessions, and reports — get it wrong at enrollment and downstream data is wrong | `patients.attention-types` | Staff |
| Patients: assign collaborator | **Yes** | Determines who records sessions for the child; assignment ≠ automatic visibility | `patients.assign-collaborator` | AdministratorOrAbove |
| Patients: assign doctor | **Yes** | Owner-only + target must hold Doctor role (400 otherwise) + it's what scopes the doctor's entire view of the system | `patients.assign-doctor` | AdministratorOrAbove |
| Patients: comments | No | Free-text notes; covered by `treatments.details-vs-comments` disambiguation | — | — |
| Treatments: create/edit | **Yes** | Typed treatments (inherit patient's attention type by default), plan vs execution distinction | `treatments.overview` | Staff |
| Treatments: PATCH status | **Yes** | Status machine with terminal states — the kind of rule users discover via a 409 unless told first | `treatments.status-lifecycle` | Staff |
| TreatmentDetails vs TreatmentComments | **Yes** | Two nested collections that look identical to a new collaborator; details are clinical progress records, comments are conversation | `treatments.details-vs-comments` | Staff |
| Sessions: create | **Yes** | The core field workflow — the NGO's operational history depends on collaborators recording these correctly | `sessions.recording` | Staff |
| Sessions: clinic-or-route rule | **Yes** | "Either ClinicId or WorkRouteId, not neither" is enforced server-side (400) — explain it before the error does | `sessions.clinic-or-route` | Staff |
| Sessions: PATCH status | **Yes** | `Scheduled → Completed \| Missed`, terminal states final (409), duration/notes only on Completed | `sessions.status-lifecycle` | Staff |
| Sessions: DELETE | No | Admin-only soft delete; standard | — | — |
| Consultations: open + thread | **Yes** | The collaborator↔doctor channel is the least self-evident workflow in the system: when to open one vs writing a treatment comment, who receives it, what to expect | `consultations.overview` | Staff |
| Consultations: PATCH status | **Yes** | Lifecycle semantics (who resolves, what resolved means, notifications fired) | `consultations.status-lifecycle` | Staff |
| Reports: all four endpoints | **Yes** | Role-based projection is *designed* confusion: sponsors see anonymized aggregates, members see own scope, admins see everything — every role needs told why their numbers differ | `reports.role-scope` | **All** (this is the sponsor-facing topic) |
| Reports: dashboard reading | Maybe | What "sessions this month" counts, medical vs educational breakdown | `reports.reading-the-dashboard` | All |
| Notifications: list/read/read-all | No | A notification bell is a universal pattern | — | — |
| Notifications: push subscriptions | No | Transparent to the user (browser permission prompt does the explaining) | — | — |
| Attachments: upload/list | Maybe | Owner-type attachment model ("files live on the patient/treatment they belong to") + size/type limits worth one topic | `attachments.overview` | Staff |
| Seed: POST /seed | **No** | Dev/staging-only test dataset, not an end-user feature — explicitly out | — | — |

### Priority content set

**P1 — ship with the feature (15 topics):** `auth.signup-invitation`, `users.roles`,
`invitations.overview`, `work-routes.overview`, `work-routes.destinations`,
`patients.attention-types`, `patients.assign-collaborator`, `patients.assign-doctor`,
`treatments.overview`, `treatments.status-lifecycle`, `treatments.details-vs-comments`,
`sessions.recording`, `sessions.clinic-or-route`, `sessions.status-lifecycle`,
`consultations.overview`, `consultations.status-lifecycle`, `reports.role-scope`.
(The field-collaborator cluster — sessions, treatments, consultations — is the payoff;
that's where rotating staff and data quality meet.)

**P2 — author as content, no code change (7 topics):** `clinics.overview`,
`patients.overview`, `collaborators.overview`, `collaborators.positions`,
`attachments.overview`, `notifications.overview` (if wanted), `reports.reading-the-dashboard`.

**Skip permanently:** login/password flows, users/clinics trivial CRUD, theme, roles
catalog, notification bell mechanics, push subscriptions, seed.

---

## 6. Frontend integration contract (spec only — implemented in `housecenter-web`)

The consumer is the `housecenter-web` change (React + TS + Vite, TanStack Query v5, React
Router v7, MUI v9 per its implemented deviations). This section is the handoff contract;
no frontend code in this change.

### Data hooks

```ts
// One fetch per session powers ALL tooltips — summaries are body-free by design (§4)
export function useHelpTopics() {
  return useQuery({
    queryKey: ['help', 'topics'],
    queryFn: helpApi.getTopics,           // GET /help/topics
    staleTime: 24 * 60 * 60 * 1000,       // content changes rarely; don't refetch per page
  })
}

// Full body — only fetched when the guide page actually opens
export function useHelpTopic(topicKey: string) {
  return useQuery({
    queryKey: ['help', 'topic', topicKey],
    queryFn: () => helpApi.getTopic(topicKey),   // GET /help/topics/{topicKey}
  })
}
```

### `HelpTooltip` component

```tsx
interface HelpTooltipProps {
  topicKey: string;                 // the only required prop — the whole contract
  placement?: TooltipPlacement;     // default 'top'
  children?: ReactNode;             // custom anchor; default = small info icon button
}
```

Behavior:

- **Anchor:** by default renders a small info icon (`IconButton` + info icon, MUI). If
  `children` is provided, wraps that element instead (e.g. a section label).
- **Trigger:** hover *and* focus (keyboard accessibility), tap on touch devices. Tooltip
  content: **Title** (bold) + **Summary** + **"Read more →"** link. The tooltip must be
  *interactive* (stays open while the pointer moves into it) or the CTA is unclickable —
  MUI Tooltip supports this; it's the one implementation detail worth pinning in the contract.
- **"Read more" CTA:** React Router `<Link to={`/help/${topicKey}`}>` — client-side nav,
  no reload, respects existing `RequireAuth`.
- **Lookup:** resolves `topicKey` against the cached `useHelpTopics()` map — zero extra
  requests per tooltip.

### Degrade contract (must not break the UI)

| Condition | Behavior |
|---|---|
| `topicKey` not in the fetched list (unpublished, out-of-audience, or typo) | Render **nothing** — no icon, no gap-filler. `console.warn` in dev builds only. |
| `GET /help/topics` fails or is still loading | Render nothing; never block, spinner, or error the host page. Help is enhancement, not dependency. |
| Guide page `/help/{topicKey}` → 404 | Friendly "topic not found" state on the HelpPage with a link back to `/help` index. |

Rendering nothing (vs a dead icon) is deliberate: audience filtering means a Sponsor
legitimately lacks most keys — dead icons would look broken; absence looks intentional.

### Routes and anchoring

```
/help                → HelpIndexPage — topics grouped by Category, ordered by DisplayOrder
/help/:topicKey      → HelpTopicPage — Title + rendered markdown Body (needs a md renderer
                       dep, e.g. react-markdown; sanitize or restrict to plain CommonMark)
```

Anchoring recommendation: extend the existing shared `PageHeader.tsx` with an optional
`helpKey?: string` prop — one edit gives every module page a header-level tooltip
(`patients.overview`, `work-routes.overview`, `reports.role-scope`…). Action-level topics
anchor inside the specific control: the session/treatment/consultation **status-change
dialogs** (`*.status-lifecycle`), the **destination editor** (`work-routes.destinations`),
the **assign doctor/collaborator dialogs**, the **invite form** (`invitations.overview`,
`users.roles`). Add a "Help" entry to the sidebar nav (`/help`, `roles: ALL_ROLES`) so the
guide is reachable without hunting for a tooltip.

---

## 7. Phased implementation plan

Fits the existing SDD flow — if formalized, this document splits into
`openspec/changes/help-center/{proposal,spec,tasks}.md` (design.md optional; only
housecenter-web needed one). Each phase is an independent, reviewable PR that builds and
tests green on its own, per the notifications chained-PR precedent.

### Phase 1 — Backend module (`HouseCenter.Api`) — one PR, est. 4–6 h

- [ ] T1 — Domain: `HelpTopic : BaseEntity` + `HelpAudience` enum in `Domain/Entities/`;
      `HelpTopicConfiguration` (enum as string, unique index on `TopicKey`, max lengths);
      `DbSet<HelpTopic>` on `HouseCenterDbContext`; migration `AddHelpTopics`.
- [ ] T2 — CQRS handlers under `Features/Help/` (flat layout, §3): `ListHelpTopics`
      (audience ladder filter from role claim, `AsNoTracking()`, inline `new` projection —
      EF translation gotcha), `GetHelpTopic` (by key, audience-filtered → 404),
      `CreateHelpTopic` (duplicate-key check via `IgnoreQueryFilters()` → `Error.Conflict`),
      `UpdateHelpTopic` (TopicKey immutable), `DeleteHelpTopic` (soft delete).
- [ ] T3 — `HelpEndpoints.cs` (`MapGroup("/help")`, `RequireAuthorization()`, writes
      `PolicyNames.AdministratorOrAbove`, `.WithValidation<T>()`); wire
      `app.MapHelpEndpoints()` in `Program.cs`; validators per §4 table.
- [ ] T4 — Integration tests (`HelpTopicTests.cs`, WebApplicationFactory):
      `CreateTopic_AsAdmin_Returns201` · `CreateTopic_AsMember_Returns403` ·
      `CreateTopic_DuplicateKey_Returns409` · `CreateTopic_DuplicateKeyOfInactive_Returns409` ·
      `CreateTopic_InvalidKeyFormat_Returns400` · `ListTopics_AsSponsor_ReturnsOnlyAllAudience` ·
      `ListTopics_AsMember_ExcludesAdminTopics` · `GetTopic_ByKey_Returns200WithBody` ·
      `GetTopic_OutsideAudience_Returns404` · `UpdateTopic_CannotChangeKey` ·
      `DeleteTopic_ThenGet_Returns404`.
- [ ] Add the HelpTopics row to the CLAUDE.md Module patterns table.

**Acceptance:** `dotnet build` clean, all new + existing tests green.

### Phase 2 — Content seeding + authoring — one PR, est. 3–4 h + writing time

- [ ] T5 — Extend `DatabaseSeeder` with **insert-if-missing** help topics (match on
      `TopicKey`, insert only when absent — never update, so admin edits are never
      overwritten by a deploy). Note: this widens the seeder's charter beyond "roles +
      owner" — deliberate, because baseline guide content is production bootstrap data,
      not test data; `POST /seed` stays untouched.
- [ ] T6 — Author the P1 topic set (§5) as seed content — **in Spanish** (the field users
      are Nicaraguan; see Open Questions on i18n). Content source: the openspec specs
      themselves — e.g. `sessions.status-lifecycle` body is the R3 transition table in
      user language.
- [ ] T7 — Seeder idempotency test: run twice, count stable; edit a seeded topic via API,
      reseed, edit survives.

**Acceptance:** fresh DB boots with the P1 catalog; reseeding never clobbers admin edits.

### Phase 3 — Frontend (`housecenter-web`, separate repo/PR) — est. 4–6 h

- [ ] T8 — `help.api.ts` + `help.types.ts` + `useHelpTopics` / `useHelpTopic` hooks
      (module conventions from the housecenter-web design: api/modules + hooks-per-resource
      + query-key factory).
- [ ] T9 — `HelpTooltip` component per the §6 contract, incl. the degrade rules; extend
      `PageHeader` with `helpKey`.
- [ ] T10 — `/help` + `/help/:topicKey` routes (markdown renderer dep), sidebar entry.
- [ ] T11 — Wire P1 anchors: page headers + the five action-level dialogs (§6).
- [ ] T12 — Admin authoring UI for topics — **optional, may defer**; content is editable
      via the API meanwhile.

**Acceptance:** every P1 anchor shows its tooltip; removing a topic in the DB makes its
icon vanish without console errors; Sponsor login shows only `All`-audience help.

---

## 8. Open questions (for the project owner)

1. **Markdown vs rich text for `Body`?** Plan assumes **markdown** — matches how this team
   already writes everything (openspec docs, CLAUDE.md), renders safely with one frontend
   dep, diffs cleanly. Rich text (HTML) would reopen the legacy Froala mistake the
   housecenter-web proposal explicitly walked away from. Confirm markdown.
2. **Language(s)?** Field users read Spanish; housecenter-web is "English only for now"
   (its own out-of-scope). Plan: author content in Spanish, single-language v1. If real
   i18n is wanted later, add a `Language` column + composite unique key
   `(TopicKey, Language)` — additive migration, no redesign. Decide: Spanish-only now?
3. **Topic view analytics?** ("Which topics do users actually read?") Cheap version: a
   `ViewCount` increment on the by-key GET. Costs a write per read and pollutes a pure
   query — recommend **no** for v1, revisit if content investment needs justifying.
4. **Content versioning / audit?** `UpdatedDate` from `BaseEntity` is the v1 answer. Full
   revision history (who changed what) would be the first audit-trail feature in the
   system — out of scope unless the owner wants it platform-wide.
5. **Reactivating an unpublished topic?** V1 has no un-delete endpoint (consistent with
   Clinics/Patients soft deletes). Is 409-with-explanation enough, or is a
   `PATCH /help/topics/{id}/publish` toggle wanted? (If yes, `IsActive` becomes a
   published-flag semantic for this entity — slightly different from delete elsewhere.)
6. **Public (anonymous) `All`-audience topics** for sponsor recruitment / landing content?
   Out of v1 (§4); flag if wanted so the endpoint split is planned rather than bolted on.
7. **Images/diagrams in guide bodies?** Markdown can embed image URLs; the Attachments
   module could host them (`ownerType: "HelpTopic"`). Defer unless the owner plans visual
   guides — text-first content is also lighter for rural bandwidth.

---

## Why now

Domain V2 + notifications + attachments + housecenter-web means the *system* is done and
real users onboard next — new collaborators, remote doctors, and sponsors who have never
seen it. Help content is cheapest to write while the specs are fresh (Phase 2 literally
translates them), and every week without it converts into WhatsApp support load on the
Owner. It is also small: one Clinics-shaped module, no new architectural patterns.
