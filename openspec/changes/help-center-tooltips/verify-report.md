# Verify Report — Help Center Tooltips (Frontend)

**Change**: help-center-tooltips
**Mode**: Standard (no test runner exists in `housecenter-web` — no Strict TDD applicable)
**Date**: 2026-07-03
**Code repo verified**: `/Users/moisesreyes/Documents/growthoptix/personal/hoh-project-net/housecenter-web/` (separate git root from these SDD artifacts)

---

## Verdict

**PASS WITH WARNINGS** — 0 CRITICAL, 1 WARNING, 2 SUGGESTION.

There is no automated test suite in `housecenter-web` (confirmed true of the entire repo,
not a gap specific to this change), so this verification is based on: (a) `pnpm build` and
`pnpm lint`, both clean, and (b) a full code trace of every requirement R1–R8 against the
real implementation, function by function. Every requirement that is verifiable by reading
code (anchor logic, degrade branches, routing, grouping/sorting, markdown rendering, sidebar
wiring, scope boundaries) is genuinely implemented as specified — not just present as a
correctly-named file. Two requirements (R1.3, R1.4) describe interactive browser behavior
supplied natively by MUI's `Tooltip` component and cannot be proven by static reading or by
any command; these are called out separately below as "requires human browser verification,"
not as failures. The orchestrator's mid-flight `PageHeader.tsx` inline-layout fix is
confirmed correctly in place, and a systematic check of the other three anchor insertion
points found no instance of the same "block-stacking sibling" bug — all four are inside
`display: flex` parent containers.

---

## Requirements checklist

| Req | Status | Note |
|---|---|---|
| R1.1 — Default IconButton anchor | ✅ | `HelpTooltip.tsx:54-58` renders `IconButton` + `HelpOutlineOutlined` when `children` is absent (ternary else-branch). |
| R1.2 — Children override anchor | ✅ | `HelpTooltip.tsx:50-53` wraps `children` in an inline-flex `Box` instead, mutually exclusive with the default branch. Not currently exercised by any of the 4 real anchors (all use the default), but the capability is implemented and correct. |
| R1.3 — Hover/focus/tap triggers | ⚠️ Not code-verifiable | MUI `Tooltip` provides these natively; no trigger-disabling props (`disableHoverListener`, `disableFocusListener`, `disableTouchListener`) are set anywhere. **Requires human browser verification** (see section below), not a code defect. |
| R1.4 — Stays open into content | ⚠️ Not code-verifiable | `disableInteractive={false}` is set explicitly (`HelpTooltip.tsx:30` — this is MUI's own default value, so the prop is a no-op, but it correctly signals intent and does not disable interactivity). **Requires human browser verification.** |
| R1.5 — No per-instance fetch, resolves against cached list | ✅ | `HelpTooltip` calls only `useHelpTopics()` (shared TanStack Query key `helpKeys.list()`); grepped `HelpTooltip.tsx` and `useHelpTopics.ts` for any other network call — none exists. All 4 real anchors + the `PageHeader` mechanism share one cached query; TanStack Query dedupes concurrent mounts against the same key, so N mounted tooltips issue 1 request, not N. |
| R2.1 — Content order: Title (bold), Summary, "Leer más" | ✅ | `HelpTooltip.tsx:33-46` — `Typography fontWeight:700` (title) → `Typography` (summary) → `Link` ("Leer más ›"), in that literal JSX order. |
| R2.2 — Client-side nav Link | ✅ | `component={RouterLink}` (react-router-dom's `Link`) to `/help/${topicKey}` — not a raw `<a href>`. |
| R3.1 — Degrade: unknown/out-of-audience/typo key → render nothing | ✅ | `HelpTooltip.tsx:19-26` — `topics?.find(...)` miss returns `null` before any DOM (icon or wrapper) is emitted; `console.warn` gated by `import.meta.env.DEV`. |
| R3.2 — Degrade: loading/error → render nothing | ✅ | `HelpTooltip.tsx:17` — `if (isLoading \|\| isError) return null`, checked before the list lookup; no spinner/error UI at any anchor. |
| R3.3 — 404 on HelpTopicPage → friendly fallback | ✅ | `HelpTopicPage.tsx:24-38` — `isError \|\| !topic` renders a "Tema no encontrado" `Paper` with a `Button` linking back to `/help`, not a crash/error boundary. Traced the failure path through `apiClient`'s response interceptor (`client.ts:63-69`): any non-2xx (including 404) throws a mapped `ApiError`, which TanStack Query surfaces as `isError: true` — confirmed this is not silently swallowed anywhere upstream. |
| R4.1 — No extra client-side audience filtering | ✅ | `HelpIndexPage.tsx:22-23` passes `data ?? []` straight into `groupByCategory` with no role/audience predicate — trusts the API response as-is. |
| R4.2 — Group by Category, sort by DisplayOrder ascending | ✅ | `groupByCategory` (`HelpIndexPage.tsx:8-19`) buckets by `topic.category` then `group.sort((a,b) => a.displayOrder - b.displayOrder)` per bucket. |
| R5.1 — Fetch by route param via useHelpTopic | ✅ | `HelpTopicPage.tsx:8-9` — `useParams<{topicKey}>()` feeds `useHelpTopic(topicKey)` directly. |
| R5.2 — Title as heading, Body via markdown-to-jsx | ✅ | `HelpTopicPage.tsx:42` renders `<PageHeader title={topic.title} />` (page heading); `body` is passed as `<Markdown>{topic.body}</Markdown>` (`markdown-to-jsx`) with MUI `Typography` overrides for h1/h2/h3/p — not `dangerouslySetInnerHTML`, not raw text. |
| R6.1 — Sidebar entry visible to all 5 roles | ✅ | `constants.ts:32` — `{ label: 'Help', path: '/help', roles: ALL_ROLES, icon: 'help' }`; `ALL_ROLES = ['Owner','Administrator','Member','Doctor','Sponsor']` (`constants.ts:11`). No role-based exclusion. |
| R6.2 — Placed in CORE_PATHS | ✅ | `Sidebar.tsx:48` — `CORE_PATHS = ['/', '/patients', '/clinics', '/work-routes', '/consultations', '/help']`, rendered in the always-visible top group alongside Dashboard/Patients/Clinics/Work Routes. |
| R7.1 — Routes require auth | ✅ | `App.tsx:46-89` — both `/help` and `/help/:topicKey` (lines 62-63) sit inside the `<RequireAuth />` wrapper's tree (the "Protected app shell" block), not in the public `AuthLayout` group. Unauthenticated visitors are redirected per `RequireAuth`'s existing behavior (unchanged, reused as-is). |
| R7.2 — Routes not role-gated | ✅ | Both routes are direct children of `AppLayout`'s route array at the same nesting level as `/clinics`, `/patients`, `/reports` — outside both `RequireRole roles={STAFF_ONLY}` and `RequireRole roles={ADMIN_ABOVE}` sub-trees. No role guard wraps them. |
| R8.1 — PageHeader helpKey optional, backward-compatible | ✅ | `PageHeader.tsx:9,12,20` — `helpKey?: string`, destructured, rendered only `{helpKey && <HelpTooltip .../>}`. `pnpm build` type-checks clean against all 15 real call sites (13 pre-existing + `HelpIndexPage`/`HelpTopicPage`), none of which pass `helpKey` — confirms the prop is genuinely optional and the omit-path is unaffected. |
| R8.2 — Exact 4 topic keys anchored on the exact 4 controls | ✅ | Grepped `HelpTooltip topicKey=` across `src/`: `sessions.recording` (SessionsTab.tsx:249, next to "New Session"), `sessions.clinic-or-route` (SessionsTab.tsx:147, beside `locationMode` `RHFSelect`), `sessions.status-lifecycle` (SessionsTab.tsx:209, beside `status` `RHFSelect` in `StatusPatchForm`), `treatments.status-lifecycle` (TreatmentsTab.tsx:205, beside the `STATUS_ITEMS` Chip row in `ExpandedTreatment`). No 5th anchor exists (the `PageHeader` mechanism is generic pass-through, not a 5th hardcoded key). |
| R8.3 — No `patients.assign-doctor` anchor | ✅ | `grep -rn "assign-doctor" src/` → 0 matches. `grep -in "assign" PatientProfilePage.tsx patients.api.ts` → 0 matches. No assign-doctor control or endpoint exists; the topic remains reachable only via `/help` and direct navigation. |

**Compliance summary**: 21/23 sub-requirements confirmed by direct code trace; 2 (R1.3, R1.4) are inherently interactive-runtime behavior outside any code-reading or command-based proof and are separately flagged below, not counted as failures.

---

## Independent verification results

| Check | Result |
|---|---|
| `pnpm build` (`tsc -b && vite build`) | ✅ Clean — 0 TypeScript errors, 1446 modules transformed, build succeeded in 450ms. One pre-existing, unrelated warning about a >500kB chunk (bundle-size housekeeping, not introduced by this change and not in scope). |
| `pnpm lint` (`eslint .`) | ✅ 0 errors. 1 warning in `AuthBootstrap.tsx` (`react-hooks/exhaustive-deps`) — confirmed pre-existing and unrelated (same warning noted in apply-progress before this change touched anything), not in any file this change modified. |
| `git log --oneline` (housecenter-web) | ✅ `4ebaa6d feat: add Help Center read-only slice (PR1)` present, most recent commit, containing exactly the 6 new Workstream A files + `App.tsx`/`constants.ts`/`Sidebar.tsx`/`package.json`/`pnpm-lock.yaml` touches — no extra/missing files. |
| `git status` (housecenter-web) | ✅ PR2's 3 files uncommitted as expected: `PageHeader.tsx`, `SessionsTab.tsx`, `TreatmentsTab.tsx` (modified, not staged). 3 additional pre-existing unrelated uncommitted files (`.claude/skills/add-feature-slice/SKILL.md`, `.claude/skills/review-conventions/SKILL.md`, `CLAUDE.md` — the MUI-v9 doc-backport mentioned in prior session notes), out of this change's scope, left untouched. Nothing in this change's scope is missing or lost. |
| `git diff --stat` (PR2 files) | ✅ `PageHeader.tsx +5/-1`, `SessionsTab.tsx +21/-5` (includes the mid-flight flex-row fix), `TreatmentsTab.tsx +2/-0` — matches tasks.md's forecast (~20–26 hand-written lines) and the apply-progress notes. |
| `markdown-to-jsx` dependency | ✅ Present in `package.json` (`"markdown-to-jsx": "^9.8.2"`), committed in `4ebaa6d`. |
| TODO/FIXME/placeholder-comment grep | ✅ None found in any new or touched file (the only "placeholder" hits are legitimate HTML `placeholder=` form-field attributes, unrelated to leftover work markers). |
| Stale UI-kit pattern grep (`@/components/base/*`, `onPress`) | ✅ None found in any new or touched file — all components use `@mui/material` / `@mui/icons-material` with `onClick`. |
| `dangerouslySetInnerHTML` grep (`src/pages/help/`) | ✅ None — `HelpTopicPage.tsx` renders `Body` exclusively through `markdown-to-jsx`'s `<Markdown>` component. |

---

## The `PageHeader.tsx` inline-layout fix — verified in place

Confirmed at `PageHeader.tsx:16`: the title `Typography` and the conditional `HelpTooltip` now
share an inner `Box` with `sx={{ display: 'flex', alignItems: 'center', gap: 0.5 }}` (line 16),
nested inside the existing outer `Box`. Before the fix these two elements would have
block-stacked (default `Box` = `display: block`, each child a full-width block). This is
exactly the fix described in the assigning context, confirmed correct and present.

### Systematic check of the other 3 anchor insertion points for the same bug class

| Anchor | File:line | Immediate parent container | Flex? |
|---|---|---|---|
| `sessions.recording` | SessionsTab.tsx:249 | `Box sx={{ display: 'flex', alignItems: 'center', gap: 0.5 }}` (line 248, wraps tooltip + "New Session" `Button`) | ✅ Yes |
| `sessions.clinic-or-route` | SessionsTab.tsx:147 | `Box sx={{ display: 'flex', alignItems: 'center', gap: 0.5 }}` (line 139, wraps the `locationMode` `RHFSelect` + tooltip) | ✅ Yes |
| `sessions.status-lifecycle` | SessionsTab.tsx:209 | `Box sx={{ display: 'flex', alignItems: 'center', gap: 0.5 }}` (line 207, wraps the `status` `RHFSelect` + tooltip, itself inside a CSS-grid column) | ✅ Yes |
| `treatments.status-lifecycle` | TreatmentsTab.tsx:205 | `Box sx={{ display: 'flex', alignItems: 'center', gap: 1.5, flexWrap: 'wrap' }}` (line 201, wraps the "Status" label + tooltip + Chip `Stack`) | ✅ Yes |

No other instance of the "block-stacking sibling" bug class was found — all four inline anchors
sit inside an explicit `display: flex` (or `inline-flex`, for the `children`-override branch
inside `HelpTooltip.tsx` itself) parent, and none rely on an ambient flex context from further
up the tree that could silently break.

---

## Findings

### CRITICAL
None.

### WARNING

1. **Coverage of `patients.assign-doctor` UI evolution is entirely process-dependent, not code-enforced.** R8.3 is satisfied today (no anchor exists, confirmed by grep), but nothing in the codebase — no lint rule, no test, no comment marker at a specific insertion point — will flag a future PR that adds an assign-doctor control without also remembering to anchor (or deliberately not anchor) `patients.assign-doctor`. This is a process/documentation risk rather than a defect in the current diff; the proposal already tracks it under "Out of scope" for a future change. No action needed for this change to pass, but worth carrying forward as a note for whoever builds the assign-doctor control.

### SUGGESTION

1. `HelpTooltip.tsx:30` sets `disableInteractive={false}` explicitly even though this is MUI `Tooltip`'s own default value — functionally a no-op, but harmless and arguably good self-documentation of the R1.4 intent. No change needed.
2. The app's global `QueryClient` default (`retry: 1`, `staleTime: 30_000` in `lib/queryClient.ts`) means a genuine 404 on `/help/topics/{topicKey}` is retried once before `HelpTopicPage` settles into `isError: true` and shows the friendly fallback — a small, user-invisible delay, not a correctness issue, and this is a pre-existing app-wide convention, not something introduced by this change.

---

## Requires human browser verification (not verifiable by this phase)

These describe genuine interactive/runtime behavior that MUI's `Tooltip` supplies internally.
No command or static code read can prove them; the code was inspected only to confirm nothing
disables or overrides MUI's defaults (confirmed above — no `disableHoverListener`,
`disableFocusListener`, `disableTouchListener`, or non-interactive override exists anywhere).

- **R1.3 / Scenario C** — the tooltip actually opens on mouse hover, on keyboard `Tab` focus,
  and on tap on a touch device, for all 4 real anchors and the ad-hoc default-anchor case.
- **R1.4 / Scenario D** — the tooltip stays open when the pointer moves from the anchor into
  the tooltip's own content area, and the "Leer más" link remains clickable in that state
  (this is the one implementation detail the proposal itself flagged as "finicky").
- **Scenario E** (partially) — that clicking "Leer más" from a live, open tooltip performs a
  real client-side navigation with no full-page reload (the `Link`/`RouterLink` wiring is
  confirmed by code; the absence of a full reload is a runtime/network-tab observation).
- **Scenario K/L** (visual/redirect confirmation) — that the sidebar Help entry is actually
  visible on-screen for all 5 roles and that an unauthenticated visit truly redirects to
  `/login` in a live session (the routing/role-array logic is confirmed by code; this is the
  live-session confirmation layered on top).
- The full "PR1 done when" / "PR2 done when" manual checklists in `tasks.md`, which explicitly
  require an actual browser per the repo's own testing constraints.

---

## Scope check

- **Backend**: `git status`/`git diff --stat` on the `HouseCenter` (.NET) repo shows zero code
  changes for this SDD change — only the new `openspec/changes/help-center-tooltips/` artifact
  directory was added. The `help-center` API contract is consumed unchanged, confirmed by
  `help.api.ts`'s two thin GET wrappers with no new backend endpoints assumed.
- **Anchor surface**: exactly 4 `HelpTooltip topicKey=` literals exist in the codebase
  (`sessions.recording`, `sessions.clinic-or-route`, `sessions.status-lifecycle`,
  `treatments.status-lifecycle`) plus the generic `PageHeader` `helpKey` pass-through — no 5th
  hardcoded topic, no anchor for any topic outside R8.2's table, no `patients.assign-doctor`
  anchor anywhere (R8.3).
- **No admin authoring UI, no anonymous access** — both explicitly out of scope per the
  proposal, and neither appears in the diff.
- Nothing beyond the two workstreams described in proposal.md/tasks.md crept into either repo.

---

## Recommendation

**Ready for `sdd-archive`.** No CRITICAL findings. The single WARNING is a forward-looking
process note (not a defect in this diff) already tracked in the proposal's "Out of scope"
section for a future change. All 16 tasks in tasks.md are `[x]` and match the actual code
state; `pnpm build` and `pnpm lint` are both clean; every requirement provable by code has
been traced and confirmed correct; the two requirements that are genuinely un-provable by
this phase (R1.3, R1.4) are MUI-native behaviors with no overriding code found, and are
called out above for a human to confirm in a live browser before or shortly after archiving
— this does not block the archive step itself.
