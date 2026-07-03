# SDD Proposal — Help Center Tooltips (frontend integration)

## Change name
`help-center-tooltips`

## Status
`implemented` (2026-07-03) — 2 PRs (housecenter-web commits 4ebaa6d, 41c4d0c), verified PASS WITH WARNINGS (see verify-report.md), 16/16 tasks complete. Interactive tooltip behavior (R1.3/R1.4) still needs a human browser confirmation — not blocking, see verify-report.md's "Requires human browser verification" section.

> **Two repositories.** SDD artifacts for this change live here in
> `HouseCenter/openspec/changes/help-center-tooltips/` (same precedent as the already-implemented
> `housecenter-web` change, whose docs also live in this repo). The **code** described below lands
> in the sibling repository at
> `/Users/moisesreyes/Documents/growthoptix/personal/hoh-project-net/housecenter-web/` — a separate
> git root, its own history, its own `CLAUDE.md` and skills.

## Problem

The backend `help-center` change shipped a complete, DB-backed help catalog: five endpoints, a
`HelpTopic` entity, audience-filtered reads, and Spanish seed content for the five highest-friction
topics. That work explicitly deferred the consumer to a follow-up change (its "Out of scope":
*"Frontend integration (plan Phase 3): `HelpTooltip`, `/help` pages, hooks, anchors."*). Today that
content is **reachable only by an authenticated `curl`** — no user of `housecenter-web` can see any
of it. The support-load argument that justified the backend (rotating rural collaborators, remote
doctors, first-time sponsors, no one to ask on WhatsApp) is only paid off once the guide surfaces
inside the app, on the screens where the confusion actually happens.

This change builds that consumer: an in-app help browser (`/help`) plus a reusable `HelpTooltip`
anchored on the concrete controls that map to the topics **already seeded**, degrading invisibly
wherever a topic is missing, unpublished, or out of the caller's audience.

## Proposed change

Add a read-only Help slice to `housecenter-web` (types → api → hooks → pages → route → nav) and a
cross-cutting `HelpTooltip` component wired into the existing controls that correspond to the
seeded topics. No backend change — the API contract from `help-center` is consumed as-is
(`GET /help/topics`, `GET /help/topics/{topicKey}`, unversioned, `RequireAuthorization`).

Ground truth for every UI decision below is the **real `housecenter-web` code** (`PatientsPage.tsx`,
`ClinicsPage.tsx`, `PageHeader.tsx`, `Sidebar.tsx`, `SessionsTab.tsx`, `App.tsx`, `constants.ts`),
**not** that repo's own `CLAUDE.md` or its three skills (`add-feature-slice`, `add-api-call`,
`review-conventions`), which are stale on the UI-kit dimension: they describe an abandoned Untitled
UI + React Aria Components stack (`@/components/base/*`, `onPress`) that was migrated to **MUI v9**
(documented as a deviation in the `housecenter-web` proposal's Status line, never backported into
its docs). Every real page uses plain `@mui/material` with `onClick`. Everything *else* in those
skills — the four-layer slice pattern, `@/` alias, named exports, TanStack Query key-factory,
RBAC route guards, `pnpm build` + `pnpm lint` as the only verification (no test runner) — remains
accurate and is followed here.

## Structure — two workstreams (this shapes how `sdd-tasks` will chain PRs)

This change deliberately does **not** fit the `add-feature-slice` mold (there is no create / update /
delete half — the admin authoring UI is deferred per the backend proposal). It splits into two
workstreams with **different review-risk shapes**:

### Workstream A — conventional read-only slice (new files, self-contained)
The normal four-layer slice minus mutations: `help.types.ts` → `help.api.ts` →
`useHelpTopics`/`useHelpTopic` hooks → `HelpIndexPage`/`HelpTopicPage` → `/help` + `/help/:topicKey`
routes → sidebar entry, plus the new shared `HelpTooltip` component and the one markdown dependency.
Additive, mostly greenfield, easy to review as a single PR.

### Workstream B — cross-cutting anchor wiring (small edits, wide fan-out)
`HelpTooltip` consumed from `PageHeader` (one additive prop) plus surgical 1–3 line insertions into
**pre-existing, unrelated** feature files. **Review Workload note for `sdd-tasks`:** the changed-line
count is small, but the *file-count spread crosses several feature areas* — this is a **fan-out risk**,
a different shape than the usual "too many lines in one file" forecast. For this change the seeded-topic
anchor surface is deliberately narrow (see Scope), but `sdd-tasks` should still forecast Workstream B
by files-touched-across-areas, not by line count, and keep A and B as separate PRs so the greenfield
slice can merge and be verified (`pnpm build && pnpm lint`) independently of the edits into live pages.

## Decision — markdown renderer: **`markdown-to-jsx`**

The seeded `HelpTopic.Body` values are genuine CommonMark (`##` headings, `` `code` `` spans,
`**bold**`, ordered and unordered lists — verified in `DatabaseSeeder.cs`), so a renderer is
required, not optional. Choose **`markdown-to-jsx`** over `react-markdown`:

> It is a single ~6 kB dependency with no transitive runtime tree, versus `react-markdown`'s
> remark/rehype/unified/micromark ESM toolchain — a meaningful bundle difference for an app whose
> users are on rural Nicaraguan connections. The seeded content needs only basic CommonMark (no GFM
> tables, footnotes, or raw-HTML passthrough), so `react-markdown`'s plugin ecosystem is unused
> weight. `markdown-to-jsx` renders **no raw HTML by default** (safe without an added
> `rehype-sanitize` step), and its `options.overrides` map cleanly onto MUI components
> (`Typography`, `Link`), matching the app's MUI-v9 convention on the one guide page that renders a
> body. If GFM tables are ever authored into a body, revisiting this is a one-file swap.

## Decision — sidebar placement: **`CORE_PATHS`**

Confirmed against `Sidebar.tsx` and `constants.ts`. The Help entry uses `roles: ALL_ROLES`
(Sponsors need the guide too), which matches Dashboard / Patients / Clinics / Work Routes — all
`ALL_ROLES` items bucketed into `CORE_PATHS` and rendered in the top group. A new group would only
be justified if Help were role-narrowed or belonged with Management; it is neither. Placing `/help`
in `CORE_PATHS` puts it in the always-visible core list, consistent with every other everyone-can-see
module.

**Wiring is a confirmed 3-file touch, not a 1-line push** — items absent from a bucket array
render nothing regardless of `NAV_ITEMS`/role filtering:
1. `src/lib/constants.ts` — add the `NAV_ITEMS` entry `{ label: 'Help', path: '/help', roles: ALL_ROLES, icon: 'help' }`.
2. `src/components/shared/Sidebar.tsx` — add `'/help'` to the `CORE_PATHS` array (line 46).
3. `src/components/shared/Sidebar.tsx` — add `help: HelpOutlineOutlined` to the `NAV_ICONS` map (line 33) and its `@mui/icons-material` import. This is a separate lookup from the near-dead legacy `Icon.tsx` `paths` map (1 consumer) — do **not** touch `Icon.tsx`.

## `HelpTooltip` behavior (consumed contract from the backend plan §6)

- **Anchor:** default = a small `IconButton` + `HelpOutlineOutlined` (MUI), matching how icons are
  wired everywhere else. If `children` is passed, wrap that element instead (e.g. a section label).
- **Content:** MUI `Tooltip` with a JSX `title` = **Title** (bold) + **Summary** + a **"Leer más →"**
  React Router `<Link to={`/help/${topicKey}`}>`. The tooltip must be *interactive* (stays open when
  the pointer moves into it) or the CTA is unclickable — the one implementation detail worth pinning.
- **Trigger:** hover **and** focus (keyboard), tap on touch.
- **Lookup:** resolves `topicKey` against the once-per-session cached `useHelpTopics()` map — zero
  extra requests per tooltip.
- **Self-contained:** it is pure MUI (`Tooltip` + `IconButton` + icon), so it drops into either an
  MUI subtree (`SessionsTab`, `TreatmentsTab`) or a Tailwind subtree (`PatientProfilePage`) without a
  wrapper — MUI components need only the app-level `ThemeProvider`, already provided by `AppLayout`.

## Degrade contract (must never break a host page — restated from plan §6)

| Condition | Behavior |
|---|---|
| `topicKey` not in the fetched list (unpublished, out-of-audience per R2/R3, or typo) | Render **nothing** — no icon, no gap. `console.warn` in dev builds only. |
| `GET /help/topics` failing or still loading | Render nothing; never block, spinner, or error the host page. Help is enhancement, not dependency. |
| `/help/:topicKey` resolves to a 404 (R3: 404-not-403, existence never leaked) | The `HelpTopicPage` shows a friendly "tema no encontrado" state with a link back to `/help` — handled inside the page, because `App.tsx`'s catch-all redirects unknown routes to `/`, so a real topic key must always route to the page and let it degrade. |

Rendering *nothing* (vs a dead icon) is deliberate: audience filtering means a Sponsor legitimately
lacks most keys — dead icons would look broken, absence looks intentional.

## Scope

### In scope

**Workstream A (new):**
- `help.types.ts` + `help.api.ts` (`const BASE = '/help'`, unversioned, using `src/api/client.ts`) —
  `getTopics()` → `GET /help/topics`, `getTopic(key)` → `GET /help/topics/{topicKey}`.
- `useHelpTopics` (24h `staleTime`) + `useHelpTopic(key)` hooks with a `helpKeys` query-key factory,
  mirroring `hooks/patients/usePatients.ts`.
- `HelpIndexPage` (topics grouped by `Category`, ordered by `DisplayOrder`) and `HelpTopicPage`
  (`Title` + markdown `Body` via `markdown-to-jsx`, plus the 404 degrade state).
- New shared `HelpTooltip` component (MUI, per the contract above).
- `/help` and `/help/:topicKey` routes in `App.tsx`, in the all-roles group (lines 52–59), no
  `RequireRole` wrapper.
- Sidebar nav entry (the 3-file touch above).
- One new dependency: `markdown-to-jsx`.

**Workstream B (touched, pre-existing):**
- `PageHeader.tsx` — add an optional `helpKey?: string` prop that renders a `HelpTooltip` beside the
  title. Confirmed safe: closed `Props` interface, 13 consumers all destructure only
  `title`/`description`/`action`; an optional prop breaks none. This makes header-level tooltips a
  one-liner for every future page topic.
- `SessionsTab.tsx` — anchor the three seeded `sessions.*` topics on their real controls: the
  create-session form (`sessions.recording`), the clinic/route field in that form
  (`sessions.clinic-or-route`), and the status-change dialog (`sessions.status-lifecycle` — the
  `statusSchema` / `RHFSelect status` at lines 52–57, 197, confirmed present).
- `TreatmentsTab.tsx` — anchor `treatments.status-lifecycle` on the treatment status-change control.

### Out of scope

- **Inline anchor for `patients.assign-doctor`.** The topic is seeded (`DatabaseSeeder.cs`), but
  **no assign-doctor UI exists in `housecenter-web` today** — `PatientProfilePage.tsx` has only
  overview/treatments/sessions/comments/attachments tabs, and `patients.api.ts` exposes no doctor
  assignment call. Its content ships and is fully reachable via the `/help` index and
  `/help/patients.assign-doctor`; the *inline* tooltip anchor is deferred until the assign-doctor
  control is built (tracked below and in Risks). The degrade contract already covers this gracefully.
- **Admin authoring UI** for help content (create/edit/delete topics) — deferred per the backend
  proposal; content is editable via the API meanwhile. This is why Workstream A is read-only.
- **Anonymous / public help access** — reads require auth (backend R4); deep links land on `/login`
  and continue after sign-in via existing `RequireAuth`.
- **Full taxonomy coverage.** Only the **5 topics with seeded content today** get anchors in this
  change. The remaining ~10 P1 topics (`invitations.overview`, `users.roles`,
  `consultations.*`, `work-routes.*`, `patients.attention-types`, `reports.role-scope`, etc.) get
  their tooltip anchors added incrementally **as their content is authored** — this change is not
  blocked on writing guide bodies that do not exist yet. Each future anchor is a small additive edit
  (a `helpKey` prop or one `HelpTooltip`), the same Workstream-B shape.

## Priority set (the seeded 5 this change targets)

`sessions.recording`, `sessions.clinic-or-route`, `sessions.status-lifecycle`,
`treatments.status-lifecycle` — anchored inline; and `patients.assign-doctor` — content-only until
its UI exists. (All five verified as seeded Spanish content in `HouseCenter.Api/Data/DatabaseSeeder.cs`.)

## Affected files

Distinguishes **new** files (Workstream A) from **touched** pre-existing files (Workstream B) so
`sdd-tasks` can forecast the fan-out risk. All paths are in the `housecenter-web` repo unless noted.

| File | Workstream | New / Touched | Change |
|---|---|---|---|
| `src/api/modules/help.api.ts` | A | New | `BASE='/help'`; `getTopics`, `getTopic(key)` over `client.ts` |
| `src/api/modules/help.types.ts` *(per existing type-colocation convention)* | A | New | `HelpTopicSummary`, `HelpTopic`, `HelpAudience` types mirroring backend DTOs |
| `src/hooks/help/useHelpTopics.ts` | A | New | `useHelpTopics` + `useHelpTopic` + `helpKeys` factory |
| `src/components/shared/HelpTooltip.tsx` | A | New | MUI tooltip with Title+Summary+"Leer más" link; degrade rules |
| `src/pages/help/HelpIndexPage.tsx` | A | New | Topics grouped by category |
| `src/pages/help/HelpTopicPage.tsx` | A | New | Markdown body render + 404 friendly state |
| `package.json` | A | Touched | `+ markdown-to-jsx` dependency |
| `src/App.tsx` | A | Touched | `+2` route lines (`/help`, `/help/:topicKey`) in all-roles group |
| `src/lib/constants.ts` | A | Touched | `+1` `NAV_ITEMS` entry |
| `src/components/shared/Sidebar.tsx` | A | Touched | `+ '/help'` in `CORE_PATHS`; `+ help` in `NAV_ICONS` + import |
| `src/components/shared/PageHeader.tsx` | B | Touched | `+ helpKey?: string` optional prop → renders `HelpTooltip` |
| `src/pages/patients/SessionsTab.tsx` | B | Touched | anchor `sessions.recording`, `sessions.clinic-or-route`, `sessions.status-lifecycle` |
| `src/pages/patients/TreatmentsTab.tsx` | B | Touched | anchor `treatments.status-lifecycle` |

No files in the `HouseCenter` (.NET) repo change — the backend contract is consumed unchanged.

## Rollback plan

**Low risk — additive only.** New files (Workstream A) can be deleted outright; touched files
(Workstream B) carry only small additive insertions (one optional `PageHeader` prop, a handful of
`HelpTooltip` mounts, three sidebar edits, two route lines, one dependency). To revert: `git revert`
the PR(s) in `housecenter-web`. There is no schema change, no migration, no API change, and no change
to any existing module's data flow. Because of the degrade contract, even a *partially* applied state
(component present but the topics request failing) renders nothing rather than breaking a host page,
so there is no blast radius beyond the Help feature itself.

## Risks & open questions

- **`patients.assign-doctor` has no anchor target.** Its inline tooltip cannot ship in this change
  (no assign-doctor UI in `housecenter-web`). Mitigated: content is reachable via `/help`; anchor is
  added when the assign-doctor control is built. `sdd-spec` should not write a scenario asserting an
  inline assign-doctor tooltip.
- **Stale-skill regression risk.** If `sdd-apply`'s auto-injected "Project Standards" for
  `housecenter-web` are sourced from that repo's skill registry, they will point at the abandoned
  `@/components/base/*` + `onPress` stack and produce non-compiling code. The orchestrator must inject
  this proposal's MUI-v9 correction inline, or re-resolve the cached rules, before apply.
- **Fan-out review shape (Workstream B).** Small line counts across multiple feature files; forecast
  by files-touched, not lines. Keep A and B as separate PRs. `delivery_strategy` is `ask-on-risk`.
- **Interactive-tooltip + touch behavior** is the one finicky MUI detail (CTA must remain clickable);
  flag for `sdd-design`/`sdd-apply` to verify in the browser, since there is no test runner
  (`pnpm build` + `pnpm lint` only).
- **`help.types.ts` exact location** (colocated vs a `types/` dir) is a convention detail for
  `sdd-spec`/`sdd-tasks` to pin against the real repo; it does not affect this proposal's shape.

## Why now

The backend is live and its seed content is invisible until this consumer ships — every day without
it is WhatsApp support load on the Owner, the exact cost the `help-center` change was meant to remove.
The change is small and additive (one read-only slice + one shared component wired into a narrow,
already-built anchor surface), depends on nothing new, and nothing depends on it — so it can land
flexibly and be reverted cleanly.
