# Tasks: Help Center Tooltips (Frontend Integration)

> Code target: `housecenter-web` (separate git repo — see proposal.md header). No files in
> this `HouseCenter` (.NET) repo change. `pnpm build` (type-check) + `pnpm lint` are the only
> commands that verify this change — there is no automated test runner in `housecenter-web`.
> Every phase below therefore also lists explicit manual/browser verification steps.

## Review Workload Forecast

| Field | Value |
|---|---|
| Estimated changed lines (hand-written, both workstreams) | ~300–335 |
| Distinct files touched (new + touched) | 13 — 6 new, 7 touched |
| Workstream A (PR1) size | ~290–310 lines across 10 files (6 new, 4 touched) |
| Workstream B (PR2) size | ~20–26 lines across 3 touched files |
| Feature areas spanned by Workstream B | 2 — shared `PageHeader` component (13 existing consumers app-wide) + patient-profile Sessions/Treatments tabs (2 files, same feature) |
| 400-line budget risk | Low |
| Chained PRs recommended | No |
| Suggested split | 2 PRs — one per workstream (A then B; B has a hard import dependency on A, not a budget-driven chain) |
| Delivery strategy | ask-on-risk |

Decision needed before apply: No
Chained PRs recommended: No
400-line budget risk: Low

**Basis:** sized against the real `housecenter-web` files read for this forecast.
Workstream A new files: `help.types.ts` ~20 lines (3 type declarations, vs. `report.types.ts`'s
25 lines for 2 interfaces), `help.api.ts` ~15 lines (mirrors `reports.api.ts` exactly — 2
methods, `BASE` const, 15 lines), `useHelpTopics.ts` ~28–32 lines (mirrors `useReports.ts`'s 25
lines, plus one extra key-factory entry and a 24h `staleTime` constant), `HelpTooltip.tsx`
~55–70 lines (new branching logic: anchor-vs-children, list lookup, three degrade early-returns,
interactive tooltip content — more branches than `PageHeader.tsx`'s 27 lines but the same order
of magnitude), `HelpIndexPage.tsx` ~70–85 lines (grouping+sorting logic, comparable to
`ReportsPage.tsx`'s 91 lines minus the chart), `HelpTopicPage.tsx` ~55–70 lines (fetch-by-param +
markdown render + 404 fallback). Workstream A touched files: `package.json` +1,
`App.tsx` +4 (2 imports + 2 route lines, sized against its existing route-table rows),
`constants.ts` +1 (`NAV_ITEMS` row, sized against its existing 9 one-line entries),
`Sidebar.tsx` +2–3 (`NAV_ICONS` entry + import + `CORE_PATHS` array append, confirmed at
lines 33/46 in the real file). Workstream B: `PageHeader.tsx` +7–9 (prop, destructure, one
conditional render), `SessionsTab.tsx` +10–13 (1 import + 3 anchor insertions, 1–3 lines each,
confirmed insertion points at `locationMode` `RHFSelect` line ~138 and `statusSchema`
`RHFSelect status` line ~197), `TreatmentsTab.tsx` +3–4 (1 import + 1 anchor insertion at the
`STATUS_ITEMS` Chip group, line ~200–216).

**Fan-out note:** the proposal flagged Workstream B's risk shape as file-count fan-out across
several feature areas, distinct from a line-count problem. That shape is real in kind but
narrow in this change's final scope: R8.2 anchors exactly 4 topics on 3 files (not the ~7–10
files floated during exploration), and R8.3 explicitly excludes the one topic
(`patients.assign-doctor`) that would have added a 4th feature area (no anchor target exists).
The 3 touched files span only 2 areas — the shared `PageHeader` (touched once, but consumed by
13 pages, so its blast radius is wider than its line count suggests) and the patient-profile
Sessions/Treatments tabs (2 files, one feature). Total Workstream B diff is ~20–26 lines. Given
this, further splitting Workstream B into a `PageHeader`-only PR and an anchors-only PR (no
dependency runs between them — the tab anchors don't consume the new `helpKey` prop) is
possible but not warranted: it would add PR-management overhead disproportionate to a ~25-line
change. Recommendation: keep Workstream B as a single PR, sequenced after Workstream A merges.

Overall this change is small and comfortably fits in 2 PRs — unlike the backend
`help-center` change's 4-way chain, no exception or stacked-PR strategy is needed here.

---

## Phase 1: Workstream A — read-only Help slice (new files, additive) — PR1 (~290–310 lines)

- [x] 1.1 `package.json` — add the `markdown-to-jsx` dependency (`pnpm add markdown-to-jsx`). Supports R5.2.
- [x] 1.2 `src/types/help.types.ts` — **New.** `HelpTopicSummary`, `HelpTopic`, `HelpAudience`
      per the spec's "Type shapes" section. Location note: this supersedes the proposal's
      affected-files entry (`src/api/modules/help.types.ts`) — the real repo convention keeps
      domain types in `src/types/{domain}.types.ts`, colocated with neither the api module nor
      the component (confirmed against `report.types.ts`, `patient.types.ts`,
      `session.types.ts`, all of which live in `src/types/`, not `src/api/modules/`). Supports
      R1.5, R2.1, R4.2, R5.2.
- [x] 1.3 `src/api/modules/help.api.ts` — **New.** `const BASE = '/help'`; `getTopics()` →
      `GET /help/topics`; `getTopic(topicKey)` → `GET /help/topics/{topicKey}`. Mirrors
      `reports.api.ts` (thin wrapper over `apiClient`, no versioning prefix). Supports R1.5, R5.1.
- [x] 1.4 `src/hooks/help/useHelpTopics.ts` — **New.** `helpKeys` query-key factory
      (`all`, `list()`, `detail(topicKey)`); `useHelpTopics()` (`staleTime: 24h`, mirrors
      `useReports.ts`'s `staleTime` pattern); `useHelpTopic(topicKey)` (`enabled: !!topicKey`).
      Supports R1.5, R3.2, R5.1.
- [x] 1.5 `src/components/shared/HelpTooltip.tsx` — **New.** `HelpTooltipProps` defined inline
      in this file (not in `help.types.ts` — matches the convention of `PageHeader.tsx`'s local
      `Props` interface). Default anchor = `IconButton` + `HelpOutlineOutlined`; if `children`
      supplied, wrap it instead. Resolve `topicKey` against `useHelpTopics()`'s already-fetched
      list (no per-instance request). Render nothing (no icon, no reserved space) when the key
      is absent from the list, or while the list is loading/errored; `console.warn` in dev only.
      MUI `Tooltip` with `disableInteractive={false}` (interactive, stays open into its content)
      and `title` = Title (bold) + Summary + `<Link to={`/help/${topicKey}`}>`Leer más ›</Link>`.
      Supports R1.1, R1.2, R1.3, R1.4, R1.5, R2.1, R2.2, R3.1, R3.2.
- [x] 1.6 `src/pages/help/HelpIndexPage.tsx` — **New.** Fetch via `useHelpTopics()`; group
      results by `category`; within each group, sort ascending by `displayOrder`; no additional
      client-side audience filtering (the API response is already scoped). Each topic links to
      `/help/{topicKey}`. Supports R4.1, R4.2.
- [x] 1.7 `src/pages/help/HelpTopicPage.tsx` — **New.** Fetch via `useHelpTopic(topicKey)` keyed
      off the route's `:topicKey` param; render `Title` as a page heading and `Body` via
      `markdown-to-jsx` (no raw-Markdown-as-text fallback); on a 404/missing topic, render a
      friendly "tema no encontrado" state with a link back to `/help` instead of an error
      boundary. Supports R5.1, R5.2, R3.3.
- [x] 1.8 `src/App.tsx` — **Touched.** Import `HelpIndexPage`/`HelpTopicPage`; add
      `{ path: '/help', element: <HelpIndexPage /> }` and
      `{ path: '/help/:topicKey', element: <HelpTopicPage /> }` inside the existing all-roles
      group (lines ~52–59) — no `RequireRole` wrapper. Supports R7.1, R7.2.
- [x] 1.9 `src/lib/constants.ts` — **Touched.** Add
      `{ label: 'Help', path: '/help', roles: ALL_ROLES, icon: 'help' }` to `NAV_ITEMS`. Supports R6.1.
- [x] 1.10 `src/components/shared/Sidebar.tsx` — **Touched, 3-part edit in one file.** Import
      `HelpOutlineOutlined` from `@mui/icons-material`; add `help: HelpOutlineOutlined` to the
      `NAV_ICONS` map (near line 33); add `'/help'` to the `CORE_PATHS` array (line 46). Do
      **not** touch `Icon.tsx`'s unrelated legacy `paths` map. Supports R6.1, R6.2.

**PR1 done when:**
`pnpm build` and `pnpm lint` are both clean, AND the following are manually/browser-verified
(no automated test covers any of these — Scenarios A–L):
- `/help` renders topics grouped by category and ordered by `displayOrder` within each group,
  for at least two roles including Sponsor (R4, R6 — Scenarios I, K).
- Visiting `/help/{a seeded topicKey}` renders the topic's `Title` as a heading and its `Body`
  as formatted Markdown (headings/bold/list/code), not raw text (R5.2 — Scenario J).
- Visiting `/help/does-not-exist` shows the friendly not-found state with a working link back
  to `/help`, instead of a crash or generic error boundary (R3.3 — Scenario H).
- An unauthenticated visit to `/help` or `/help/{key}` redirects to `/login`; a signed-in
  Sponsor can open both routes without a role-level redirect (R7.1, R7.2 — Scenario L).
- A `HelpTooltip` (mount it ad hoc on `HelpIndexPage` for this check, or wait until Phase 2's
  live anchors exist) opens on hover, on keyboard `Tab` focus, and on tap; the tooltip stays
  open when the pointer moves from the anchor into its own content, and "Leer más" is
  clickable and navigates client-side (R1.3, R1.4, R2.2 — Scenarios A–E). This is the one
  finicky MUI interaction detail flagged in the proposal's risks — verify it in an actual
  browser, not just visually.
- A `HelpTooltip` with a `topicKey` absent from `useHelpTopics()` renders nothing (no icon, no
  gap) (R3.1 — Scenario F).

---

## Phase 2: Workstream B — inline anchor wiring (touched, pre-existing files) — PR2 (~20–26 lines)

> Depends on Phase 1 merging first: every task below imports `HelpTooltip` from
> `src/components/shared/HelpTooltip.tsx`. This is a sequencing dependency, not a
> budget-driven chain — PR2 is far under the 400-line threshold on its own.

- [x] 2.1 `src/components/shared/PageHeader.tsx` — **Touched.** Add optional `helpKey?: string`
      to the `Props` interface; when supplied, render `<HelpTooltip topicKey={helpKey} />`
      beside the title (inside the left `Box`, after the `Typography` title). When omitted,
      output is byte-identical to today — verified safe against all 13 existing call sites
      (closed `Props` interface, none pass an unknown prop). Supports R8.1.
- [x] 2.2 `src/pages/patients/SessionsTab.tsx` — **Touched, anchor 1/3.** Import `HelpTooltip`.
      Anchor `sessions.recording` at the entry point of the "new session" flow (the "New
      Session" button or the `CreateSessionForm` heading inside the `SlideOver`). Supports R8.2.
- [x] 2.3 `src/pages/patients/SessionsTab.tsx` — **Touched, anchor 2/3 (same file/commit as 2.2).**
      Anchor `sessions.clinic-or-route` beside the `locationMode` `RHFSelect` field inside
      `CreateSessionForm` (confirmed at line ~138). Supports R8.2.
- [x] 2.4 `src/pages/patients/SessionsTab.tsx` — **Touched, anchor 3/3 (same file/commit as 2.2).**
      Anchor `sessions.status-lifecycle` beside the `status` `RHFSelect` field inside
      `StatusPatchForm` (`statusSchema`, confirmed at line ~197). Supports R8.2.
- [x] 2.5 `src/pages/patients/TreatmentsTab.tsx` — **Touched.** Import `HelpTooltip`. Anchor
      `treatments.status-lifecycle` beside the Status label / `STATUS_ITEMS` Chip group inside
      `ExpandedTreatment` (confirmed at lines ~200–216). Supports R8.2.
- [x] 2.6 No-code verification task: grep `PatientProfilePage.tsx` and `patients.api.ts` to
      reconfirm no assign-doctor control or endpoint exists yet, and confirm no
      `HelpTooltip topicKey="patients.assign-doctor"` was added anywhere in this PR. Supports R8.3.

**PR2 done when:**
`pnpm build` and `pnpm lint` are both clean, AND the following are manually/browser-verified
(no automated test covers any of these — Scenarios M, N):
- As a Member on a patient profile: the Sessions tab shows the `sessions.recording` tooltip at
  the new-session entry point, `sessions.clinic-or-route` at the clinic/work-route field, and
  `sessions.status-lifecycle` at the session status control; the Treatments tab shows
  `treatments.status-lifecycle` at the treatment status control (R8.2 — Scenario M).
- Each of the 4 tooltips above opens on hover/focus/tap, stays open into its content, and its
  "Leer más" link is clickable and routes to the correct `/help/{topicKey}` (R1.3, R1.4 —
  Scenarios C, D, E, reconfirmed against real page contexts, not just the Phase-1 harness).
- No `HelpTooltip` for `patients.assign-doctor` exists anywhere in `housecenter-web`; the topic
  remains reachable only via `/help` or a direct visit to `/help/patients.assign-doctor`
  (R8.3 — Scenario N).
- Spot-check 2–3 of the 13 existing `PageHeader` consumers (e.g. `PatientsPage`, `ReportsPage`)
  that do **not** pass `helpKey` and confirm their header rendering and layout are unchanged
  (R8.1).

---

## Out of scope (confirmed by spec, not tracked as tasks)

- Admin authoring UI for help content (create/edit/delete topics).
- Anonymous/public help access.
- Tooltip anchors for any topic key other than the four in R8.2.
- Any change to the `help-center` backend contract.
