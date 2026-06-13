# SDD Proposal — Notifications

## Change name
`notifications`

## Status
`proposed`

## Problem

Domain V2 gave collaborators and doctors a structured channel (medical consultations)
and an operational record (attention sessions), but nothing tells a user that something
needs their attention:

- A collaborator posts a message on a consultation → the assigned doctor is never told.
- A doctor is assigned a new case → they only find out by browsing `/consultations`.
- A consultation is resolved → the opener doesn't know the answer arrived.
- A scheduled session is coming up (or was missed) → no reminder reaches the field worker.

The platform serves field workers in rural Nicaragua with intermittent connectivity, so
"check the app constantly" is not realistic. Users need to be reached.

## Proposed change

Introduce a **Notifications** capability with three delivery channels driven by a single
set of domain events:

1. **In-app** — a persisted `Notification` entity the user can list and mark as read.
2. **Email** — via the existing `IEmailService`, best-effort (a failed send never breaks
   the originating action).
3. **Push** — out-of-band delivery to a registered device/subscription (provider TBD —
   see Open Questions).

Notifications are emitted as **MediatR domain events** (`INotification`) published by the
command handlers after a successful `SaveChangesAsync`. Each channel is an independent
`INotificationHandler`, so adding or removing a channel never touches the business logic
that raised the event. This is a new pattern for the codebase (no domain events exist yet).

### Trigger events
| Event | Recipient | Source handler |
|---|---|---|
| `ConsultationMessagePostedEvent` | the participant who is **not** the author | `PostConsultationMessage` |
| `ConsultationOpenedEvent` | the assigned doctor | `OpenConsultation` |
| `ConsultationResolvedEvent` | the user who opened the consultation | `UpdateConsultationStatus` |
| `SessionMissedEvent` | the session's collaborator | `UpdateSessionStatus` (→ Missed) |
| `SessionUpcomingEvent` | the session's collaborator | scheduled scan (IHostedService) |

## Entities affected

### New: `Notification`
```
Id (Guid)
RecipientUserId (FK → User, required)
Type (NotificationType enum, stored as string)
Title (string)
Body (string)
ReferenceType (string?, e.g. "Consultation" / "AttentionSession")
ReferenceId (Guid?, the consultation/session it points to)
IsRead (bool, default false)
ReadAt (DateTime?, nullable)
BaseEntity fields (IsActive / CreatedDate / UpdatedDate)
```

### New (Phase 4 — push): `PushSubscription`
Device/browser subscription tokens per user. Shape depends on the chosen provider.

### Unchanged business entities
Consultations and sessions are not modified. Handlers gain an injected `IPublisher` and a
single `Publish(...)` call; that is the entire footprint on existing code.

## Delivery plan (chained PRs)

The full feature is far over the 400-line review budget, so it ships as ordered slices.
Each phase is an independent, reviewable PR that builds and tests green on its own.

| Phase | PR scope | Depends on |
|---|---|---|
| **P1 — Foundation + in-app** | Domain-event infra, `Notification` entity + migration, in-app `INotificationHandler`s, the 3 consultation events wired, read API (`GET /notifications`, mark-read), tests | — |
| **P2 — Email channel** | `INotificationHandler` that emails recipients via `IEmailService`, `EmailTemplates` additions, best-effort error handling, tests | P1 |
| **P3 — Session reminders** | `SessionMissedEvent` (request-time) + `SessionUpcomingEvent` via an `IHostedService` scanner, tests | P1 |
| **P4 — Push** | Provider decision, `PushSubscription` entity + registration endpoints, push `INotificationHandler`, tests | P1, P2 |

## Scope (this change, all phases)

- New `Notification` domain + EF config + migration + read API.
- Domain-event types (`INotification`) and `IPublisher` wiring in 4 command handlers.
- In-app, email, and push channel handlers.
- Session reminder scheduler.
- Integration tests per phase.

## Out of scope

- Notification preferences / per-user channel opt-out (future).
- Digest / batching (one notification per event for now).
- Localization of notification copy (English + existing Spanish domain terms).
- Real-time delivery to an open client (SignalR) — unless chosen as the push transport.

## Open questions

- **Push provider** (blocks P4 only): Web Push (VAPID, browser — fits the `house-mui-v1`
  React frontend), Firebase Cloud Messaging (needs a Firebase project), or SignalR
  (real-time to connected clients, not true push to closed apps). Requires external setup
  (keys / project), so it is deferred to P4 and decided then.

## Why now

It is the first post-Domain-V2 roadmap item and the natural completion of the consultation
flow: the channel exists, but it is silent until someone is notified.
