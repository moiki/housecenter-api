# SDD Spec — Help Center Tooltips (Frontend)

## Change name
`help-center-tooltips`

## Status
`spec`

## Repos in scope
Code target: `housecenter-web` (separate git repo, own history). SDD artifacts for this
change live in this repo (`HouseCenter/openspec/`). No backend files change in this
change — the `help-center` API contract (R2: audience-filtered list, R3: 404-not-403 on
out-of-audience/missing topics) is consumed as-is; this spec describes only the
frontend's reaction to those guarantees.

---

## Requirements

### R1 — HelpTooltip: anchor & trigger

**R1.1** `HelpTooltip` MUST render a default anchor of a small MUI `IconButton`
wrapping `HelpOutlineOutlined` when no `children` prop is supplied.

**R1.2** When `children` is supplied, `HelpTooltip` MUST wrap the supplied element as
the anchor instead of rendering the default `IconButton`.

**R1.3** The tooltip MUST open on hover of the anchor, on keyboard focus of the
anchor, and on tap (touch devices).

**R1.4** The tooltip MUST remain open when the pointer moves from the anchor into the
tooltip's own content area (interactive tooltip), so the "Leer más" link inside it
stays clickable.

**R1.5** `HelpTooltip` MUST resolve its `topicKey` against the already-fetched
`useHelpTopics()` list; it MUST NOT issue a per-instance network request.

### R2 — HelpTooltip: content & navigation

**R2.1** Tooltip content MUST render, in order: the topic's `Title` (bold), the
topic's `Summary`, and a "Leer más" link.

**R2.2** The "Leer más" link MUST be a React Router `<Link>` to `/help/{topicKey}`
(client-side navigation, not a full page load).

### R3 — Degrade contract

**R3.1** WHEN `topicKey` is not present in the fetched topics list — because it is
unpublished, out of the caller's audience (backend R2/R3), or a typo — `HelpTooltip`
MUST render nothing: no icon, no reserved layout space. It MAY `console.warn` in dev
builds only.

**R3.2** WHILE `useHelpTopics()` is loading or has errored, every `HelpTooltip`
instance MUST render nothing. It MUST NOT render a spinner, an error state, or
otherwise block/delay the host page. Help is an enhancement, not a dependency.

**R3.3** WHEN `/help/:topicKey` resolves to a topic the API does not return (404 —
nonexistent or out-of-audience per backend R3), `HelpTopicPage` MUST render a
friendly "topic not found" state with a link back to `/help`, instead of crashing or
showing a generic error boundary.

### R4 — HelpIndexPage

**R4.1** `HelpIndexPage` MUST render only the topics returned by `GET /help/topics` —
it MUST NOT apply any additional client-side audience filtering (the backend has
already scoped the list per caller role).

**R4.2** Topics MUST be grouped under their `Category`, and within each group MUST be
ordered by ascending `DisplayOrder`.

### R5 — HelpTopicPage

**R5.1** `HelpTopicPage` MUST fetch its topic via `useHelpTopic(topicKey)`, keyed off
the route's `:topicKey` param.

**R5.2** `HelpTopicPage` MUST render the topic's `Title` as a page heading and its
`Body` rendered as Markdown via `markdown-to-jsx` — the raw Markdown source MUST NOT
be shown as plain/unrendered text.

### R6 — Navigation & sidebar visibility

**R6.1** The Help sidebar entry MUST be visible to all five roles (Owner,
Administrator, Member, Doctor, Sponsor) — the sidebar MUST NOT role-gate the Help
item; per-topic audience filtering is the API's responsibility, not the nav's.

**R6.2** The Help sidebar entry MUST be placed in the always-visible core group
(`CORE_PATHS`), consistent with Dashboard / Patients / Clinics / Work Routes.

### R7 — Route behavior

**R7.1** `/help` and `/help/:topicKey` MUST require authentication like every other
in-app route: an unauthenticated visitor MUST be redirected to `/login`.

**R7.2** `/help` and `/help/:topicKey` MUST NOT be wrapped in a role guard — no role
is blocked at the route level. Audience scoping happens per-topic (backend R2/R3, and
R3.1/R3.3 above), never per-route.

### R8 — Inline anchor wiring (Workstream B)

**R8.1** `PageHeader` MUST accept an optional `helpKey?: string` prop; when supplied
it MUST render a `HelpTooltip` for that key beside the title; when omitted,
`PageHeader`'s rendered output MUST be unchanged from today (no tooltip, no layout
shift — none of its existing 13 call sites are required to pass the new prop).

**R8.2** The following topics MUST be anchored on the following controls, and no
others, in this change:

| Topic key | Control |
|---|---|
| `sessions.recording` | Session create form (entry point of the "new session" flow) in `SessionsTab` |
| `sessions.clinic-or-route` | The clinic-or-work-route field inside that same create form |
| `sessions.status-lifecycle` | The session status-change control in `SessionsTab` |
| `treatments.status-lifecycle` | The treatment status-change control in `TreatmentsTab` |

**R8.3** This change MUST NOT add an inline `HelpTooltip` anchor for
`patients.assign-doctor` anywhere in `housecenter-web`. The topic's content MUST
remain reachable only via the `/help` index and a direct visit to
`/help/patients.assign-doctor` — no assign-doctor control exists yet to anchor it to.

---

## Type shapes (TypeScript)

```ts
interface HelpTopicSummary {
  topicKey: string; title: string; summary: string;
  category: string; displayOrder: number
}
interface HelpTopic extends HelpTopicSummary {
  body: string; audience: HelpAudience; updatedDate: string
}
type HelpAudience = 'All' | 'Staff' | 'MemberOrAbove' | 'AdministratorOrAbove'

interface HelpTooltipProps {
  topicKey: string
  children?: React.ReactNode
}
```

---

## Scenarios

(Manually/browser-verified — `housecenter-web` has no automated test runner;
verification is `pnpm build` + `pnpm lint` plus the checks below.)

### Scenario A — Default anchor render
Given: `HelpTooltip` is mounted with only a `topicKey` prop, no `children`
Then: a small `IconButton` with `HelpOutlineOutlined` renders as the anchor

### Scenario B — Anchor override via children
Given: `HelpTooltip` is mounted with `children` (e.g., a section label)
Then: the supplied children render as the anchor; no default `IconButton` appears

### Scenario C — Hover, focus, and tap triggers
Given: a mounted `HelpTooltip` with a valid, in-audience `topicKey`
When: the anchor is hovered, or receives keyboard focus (Tab), or is tapped on a touch device
Then: the tooltip opens showing Title, Summary, and "Leer más"

### Scenario D — Tooltip stays open into content, CTA clickable
Given: the tooltip from Scenario C is open
When: the pointer moves from the anchor into the tooltip's content area
Then: the tooltip remains open and the "Leer más" link is clickable

### Scenario E — "Leer más" navigation
Given: an open tooltip for `topicKey = "sessions.recording"`
When: the "Leer más" link is clicked
Then: client-side navigation goes to `/help/sessions.recording` (no full page reload)

### Scenario F — Degrade: unknown/out-of-audience/typo key
Given: `HelpTooltip topicKey="patients.nonexistent"` where the key is absent from the
caller's `useHelpTopics()` result (unpublished, out-of-audience per backend R2/R3, or a typo)
Then: nothing renders at that anchor position — no icon, no gap in the layout

### Scenario G — Degrade: topics list loading or erroring
Given: `useHelpTopics()` is still loading, or its request has failed
Then: every `HelpTooltip` on the page renders nothing; the host page renders and is
usable as if no `HelpTooltip` were present

### Scenario H — 404 topic page fallback
Given: a signed-in user visits `/help/does-not-exist` (or a topic key outside their audience)
When: the underlying `GET /help/topics/{topicKey}` call returns 404
Then: `HelpTopicPage` shows a friendly "topic not found" message with a link back to `/help`

### Scenario I — Help index grouping and ordering
Given: `GET /help/topics` returns topics across multiple `Category` values with varying `DisplayOrder`
When: `HelpIndexPage` renders
Then: topics are grouped by `Category`, and within each group appear in ascending `DisplayOrder`

### Scenario J — Topic page markdown rendering
Given: a topic whose `Body` contains CommonMark (headings, bold, a code span, a list)
When: `HelpTopicPage` renders that topic
Then: the `Title` shows as a heading and the `Body` renders as formatted elements
(via `markdown-to-jsx`), not as raw Markdown text

### Scenario K — Sidebar visibility across all roles
Given: a signed-in user with any of the five roles (Owner, Administrator, Member, Doctor, Sponsor)
When: the sidebar renders
Then: a "Help" entry is visible in the core group for every one of the five roles

### Scenario L — Route auth-gated, not role-gated
Given: an unauthenticated visitor
When: they navigate to `/help` or `/help/sessions.recording`
Then: they are redirected to `/login`

Given: a signed-in Sponsor (the most role-restricted persona for most other modules)
When: they navigate to `/help` or `/help/sessions.recording`
Then: the route renders (no `RequireRole` redirect); any per-topic gating manifests
only as Scenario F/H, never as a route-level block

### Scenario M — Inline anchors wired to the correct controls
Given: a Member on a patient profile's Sessions tab and Treatments tab
Then: the "new session" entry point shows a `sessions.recording` tooltip, the
clinic-or-route field in that form shows a `sessions.clinic-or-route` tooltip, the
session status-change control shows a `sessions.status-lifecycle` tooltip, and the
treatment status-change control (Treatments tab) shows a `treatments.status-lifecycle` tooltip

### Scenario N — No inline anchor for patients.assign-doctor
Given: `PatientProfilePage` and its tabs as they exist today (no assign-doctor control)
Then: no `HelpTooltip` for `patients.assign-doctor` exists anywhere inline; the
content is reachable only by navigating directly to `/help/patients.assign-doctor` or
finding it in the `/help` index

---

## Out of scope (not specified by this spec)

- Admin authoring UI for help content (create/edit/delete) — deferred per the backend proposal
- Anonymous/public help access — auth required per R7.1
- Tooltip anchors for any topic key other than the four listed in R8.2
- Any change to the `help-center` backend contract (consumed as-is)
