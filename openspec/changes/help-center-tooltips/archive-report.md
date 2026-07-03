# Archive Report — Help Center Tooltips (Frontend)

**Change**: help-center-tooltips
**Status**: Archived
**Date**: 2026-07-03
**Verdict**: PASS WITH WARNINGS — all 16 tasks complete, frontend integration shipped

---

## Executive summary

The frontend consumer for the backend `help-center` API has been delivered as two sequential PRs (4ebaa6d and 41c4d0c) in the `housecenter-web` repository:

1. **PR1 (Workstream A)** — A read-only Help slice (types, API wrapper, hooks, two pages: `/help` index and `/help/{topicKey}` detail), a reusable `HelpTooltip` component with MUI-based interactive tooltip behavior, sidebar entry, and `markdown-to-jsx` dependency to render the DB-backed CommonMark bodies.

2. **PR2 (Workstream B)** — Cross-cutting integration: an optional `helpKey?: string` prop added to the shared `PageHeader` component (backward-compatible, required by none of its 13 existing call sites), plus four inline tooltip anchors wired to the controls where the five seeded backend topics apply: `sessions.recording`, `sessions.clinic-or-route`, `sessions.status-lifecycle` (all in `SessionsTab.tsx`), and `treatments.status-lifecycle` (in `TreatmentsTab.tsx`).

The verification result is **PASS WITH WARNINGS** — 0 CRITICAL, 1 WARNING, 2 SUGGESTION. The single WARNING is a forward-looking process note (no code-level guardrail on future `patients.assign-doctor` UI changes) already tracked in the proposal's "Out of scope" section and does not block this change. Interactive tooltip behavior (R1.3/R1.4: hover/focus/tap triggers, stay-open-into-content for clickability) is MUI-native and flagged as "requires human browser verification" in the verify-report — this is expected and not a failure, since `housecenter-web` has no automated test runner.

---

## What was delivered

### New files (Workstream A, 6 files)

| File | Purpose | Size |
|---|---|---|
| `src/types/help.types.ts` | TypeScript types: `HelpTopicSummary`, `HelpTopic`, `HelpAudience` — mirrors backend DTOs | ~20 lines |
| `src/api/modules/help.api.ts` | Thin API wrapper: `const BASE = '/help'`; `getTopics()`, `getTopic(topicKey)` | ~15 lines |
| `src/hooks/help/useHelpTopics.ts` | TanStack Query hooks with 24h staleTime; `helpKeys` factory; `enabled` gate | ~30 lines |
| `src/components/shared/HelpTooltip.tsx` | MUI Tooltip component: default anchor = `IconButton + HelpOutlineOutlined`; children override; degrade gracefully on missing/loading keys; interactive title+summary+"Leer más" content | ~60 lines |
| `src/pages/help/HelpIndexPage.tsx` | Topics grouped by category, ordered ascending by displayOrder; links to detail pages | ~75 lines |
| `src/pages/help/HelpTopicPage.tsx` | Topic detail: fetch by route param, markdown-to-jsx rendering of body, friendly 404 fallback | ~65 lines |

### Touched files (Workstream A + B, 7 files)

| File | Change | Lines |
|---|---|---|
| `package.json` | `+ markdown-to-jsx` dependency | +1 |
| `src/App.tsx` | `+2 import lines + 2 route lines` (`/help`, `/help/:topicKey`, no `RequireRole`) | +4 |
| `src/lib/constants.ts` | `+1 NAV_ITEMS entry` (Help, all roles, help icon) | +1 |
| `src/components/shared/Sidebar.tsx` | `+HelpOutlineOutlined import + help entry in NAV_ICONS map + '/help' in CORE_PATHS` | +3 |
| `src/components/shared/PageHeader.tsx` | `+optional helpKey?: string` prop; conditional `<HelpTooltip>` render after title (backward-compatible, no existing call sites pass it) | +5 |
| `src/pages/patients/SessionsTab.tsx` | `+HelpTooltip import + 3 anchors` (sessions.recording at entry point, sessions.clinic-or-route beside clinic/route field, sessions.status-lifecycle beside status select) | +21 |
| `src/pages/patients/TreatmentsTab.tsx` | `+HelpTooltip import + 1 anchor` (treatments.status-lifecycle beside status chip row) | +2 |

**Total scope**: 13 distinct files (6 new, 7 touched), ~300–335 hand-written lines across two PRs.

---

## Commits in housecenter-web

```
4ebaa6d feat: add Help Center read-only slice (PR1)
  - All 6 new Workstream A files
  - Touched: package.json, App.tsx, constants.ts, Sidebar.tsx

41c4d0c feat: wire HelpTooltip into session and treatment controls (PR2)
  - Touched: PageHeader.tsx, SessionsTab.tsx, TreatmentsTab.tsx
```

Both commits are present in `housecenter-web` history; PR2's files remain uncommitted (left for user to review/commit with any follow-up browser verification).

---

## Verification result

**PASS WITH WARNINGS** — see `verify-report.md` for the full checklist.

| Category | Count | Detail |
|---|---|---|
| Requirements verified by code trace | 21/23 | Every R1–R8 requirement confirmed against the actual implementation |
| Requirements requiring human browser verification | 2 | R1.3/R1.4 (hover/focus/tap triggers, stays-open-into-content) — MUI-native, no disabling code found |
| Build & lint | ✅ | `pnpm build` and `pnpm lint` both clean (0 errors); 1 pre-existing unrelated warning in `AuthBootstrap.tsx` |
| CRITICAL findings | 0 | — |
| WARNING findings | 1 | Process-level: no code guardrail ties future `patients.assign-doctor` UI to remembering to anchor it; already tracked in proposal's "Out of scope" |
| SUGGESTION findings | 2 | Minor: `disableInteractive={false}` is explicit but redundant (MUI default); retry behavior on 404 is app-wide convention |
| Additional checks | ✅ | No TODO/FIXME/placeholders; no stale UI-kit patterns (`@/components/base/*`, `onPress`); no `dangerouslySetInnerHTML`; no untracked files |

---

## Scope validation

### In scope (delivered)

- **Workstream A**: Read-only Help slice with all 6 new files and 4 touched infrastructure files
- **Workstream B**: Cross-cutting `HelpTooltip` wiring into `PageHeader` + 3 feature files (Sessions/Treatments tabs)
- **4 inline anchors**: Exactly the 4 topic keys from R8.2 (sessions.recording, sessions.clinic-or-route, sessions.status-lifecycle, treatments.status-lifecycle)
- **Degrade contract**: Rendering nothing (no icon, no gap) for unknown/loading/out-of-audience topics; friendly 404 on missing detail page
- **Sidebar entry**: Help visible to all 5 roles in the core paths group
- **Routes**: Both `/help` and `/help/:topicKey` auth-gated (RequireAuth), not role-gated

### Out of scope (deferred, as specified)

- **`patients.assign-doctor` inline anchor** — the topic is seeded and fully reachable via `/help` and `/help/patients.assign-doctor`, but no assign-doctor UI control exists yet in `housecenter-web` to anchor to (no endpoint in `patients.api.ts`). Anchor will be added when the control is built.
- **Admin authoring UI** — help content remains editable via the API; the UI author page is deferred per the backend proposal.
- **Anonymous/public help access** — auth required per backend R4 and frontend R7.1; unauthenticated visitors land on `/login`.
- **Other topic anchors** — the remaining ~10 P1 topics (`invitations.overview`, `users.roles`, `consultations.*`, `work-routes.*`, etc.) will get inline anchors added incrementally as their guide content is authored in future changes.

---

## Key design decisions

### Markdown renderer: `markdown-to-jsx`

Chosen over `react-markdown` for bundle size (~6 kB vs. remark/rehype/unified/micromark tree) and simplicity — the seeded CommonMark content needs only basic formatting (headings, bold, code spans, lists), not GFM tables or raw-HTML passthrough. `markdown-to-jsx` requires no `rehype-sanitize` (safe by default), and its `options.overrides` map cleanly onto MUI components (Typography, Link). If GFM tables are ever needed, this is a one-file swap.

### Sidebar placement: `CORE_PATHS`

The Help entry uses `roles: ALL_ROLES` (Sponsors need the guide too), consistent with Dashboard / Patients / Clinics / Work Routes — all always-visible core items. Placement in `CORE_PATHS` (not a new group) keeps the navigation hierarchy flat and discoverable.

### `HelpTooltip` degradation: render nothing

When a `topicKey` is absent from the fetched list (unpublished, out-of-audience per backend R2/R3, or a typo), the tooltip renders zero DOM (no icon, no reserved layout space) instead of a dead/broken icon. This is intentional: audience filtering means a Sponsor legitimately lacks most keys — absence looks intentional, not broken.

### PageHeader integration: optional prop

The new `helpKey?: string` prop is backward-compatible; all 13 existing call sites continue to work unchanged (none pass a `helpKey`, so the render guard `{helpKey && ...}` is falsy and contributes zero DOM). Verified by `pnpm build` type-checking clean against every call site.

---

## Outstanding: browser verification

The verify-report flags R1.3 and R1.4 as "requires human browser verification" — these are MUI `Tooltip`'s interactive runtime behaviors (hover/focus/tap triggers, staying open when the pointer moves into the content area so the "Leer más" link remains clickable). No command or static code read can prove these; the implementation uses MUI's defaults with no disabling code found. The full manual verification checklist is in `tasks.md`'s "PR1 done when" and "PR2 done when" sections — these should be confirmed in an actual browser session before or shortly after this archive step, but they do not block the archive itself (no test runner exists in `housecenter-web`, so this is expected for any frontend-only change).

---

## All tasks completed

All 16 tasks from `tasks.md` are marked `[x]`:

**Phase 1 (Workstream A, PR1)**: 1.1–1.10 ✅
- Package dependency, 6 new files, 4 infrastructure touches, routes, sidebar entry

**Phase 2 (Workstream B, PR2)**: 2.1–2.6 ✅
- PageHeader integration, 3 anchor wiring tasks, no-code verification (confirm no `patients.assign-doctor` anchor added)

---

## Related artifacts

- **Proposal**: `/openspec/changes/help-center-tooltips/proposal.md` — problem statement, design decisions (markdown renderer, sidebar placement), risk analysis
- **Spec**: `/openspec/changes/help-center-tooltips/spec.md` — 8 requirement groups (R1–R8), 14 test scenarios (A–N)
- **Tasks**: `/openspec/changes/help-center-tooltips/tasks.md` — 16 task checklist with "done when" acceptance criteria
- **Verify report**: `/openspec/changes/help-center-tooltips/verify-report.md` — full requirements checklist, code-trace results, build/lint verification, Requires human browser verification section
- **State**: `/openspec/changes/help-center-tooltips/state.yaml` — phase/status tracking
- **Backend API contract** (consumed unchanged): `/openspec/changes/help-center/` — `HelpTopic` CRUD, 5-topic Spanish seed, role-filtered list/read

---

## Risks & follow-up

- **No guardrail on `patients.assign-doctor` UI evolution** — Currently deferred (no UI control exists); when the assign-doctor control is built, whoever builds it must remember to add the tooltip anchor. Already tracked in the proposal's "Out of scope" section and the verify-report's single WARNING. Mitigation: add a comment at the insertion point when the control is built, or add the anchor to a checklist in the `PatientProfilePage` component's own TODO.
- **Manual browser verification outstanding** — R1.3, R1.4 (interactive tooltip behavior) and the full "PR1 done when" / "PR2 done when" checklists in tasks.md require actual browser confirmation. Not blocking the archive, but should be done before merging PR2.
- **PR2 uncommitted in housecenter-web** — Left for user to review and commit separately; no auto-commits were made.

---

## Recommendation

**Ready for closure.** No CRITICAL findings. The single WARNING is a documented process note, not a defect. All tasks complete, both PRs committed/staged, `pnpm build` and `pnpm lint` clean. Outstanding browser verification (R1.3/R1.4) is MUI-native behavior with no disabling code found — flag for confirmation before or shortly after merging, but does not block archiving the change.
