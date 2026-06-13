# SDD Spec — Notifications

Scope of this spec: **Phase 1 (foundation + in-app)**. Later phases (email, session
reminders, push) reuse the same domain events; their requirements are outlined at the end.

## Requirements

### R1 — Domain events are published after commit
- Command handlers publish a MediatR `INotification` only **after** `SaveChangesAsync`
  succeeds, so no notification is raised for an action that was rolled back.
- The publishing handler does not depend on any channel — it calls `IPublisher.Publish`
  and is done.
- Three events ship in Phase 1: `ConsultationMessagePostedEvent`,
  `ConsultationOpenedEvent`, `ConsultationResolvedEvent`.

### R2 — In-app notification is persisted per recipient
- An `INotificationHandler` for each event resolves the recipient(s) and writes one
  `Notification` row each.
- Recipient rules:
  - message posted → the participant who is **not** the author (opener or assigned doctor)
  - consultation opened → the assigned doctor
  - consultation resolved → the user who opened the consultation
- A user never receives a notification for their own action.
- `Type`, `Title`, `Body`, `ReferenceType="Consultation"`, `ReferenceId=consultationId`
  are populated; `IsRead=false`.

### R3 — Channel delivery is best-effort
- A handler that throws (e.g. a future email/push failure) must not fail the originating
  command. Channel handlers catch and log their own errors.
- Phase 1's in-app handler persists via its scoped `DbContext`; a failure is logged, the
  command result is unaffected.

### R4 — List my notifications
- `GET /notifications` returns the authenticated user's notifications, newest first,
  paginated (`PagedResult<NotificationResponse>`).
- Supports `unreadOnly=true` filter.
- A user only ever sees notifications where `RecipientUserId` is their own id.

### R5 — Mark as read
- `PATCH /notifications/{id}/read` sets `IsRead=true`, `ReadAt=UtcNow`; only the recipient
  may mark their own; others get 404 (not 403 — existence is not revealed).
- `POST /notifications/read-all` marks all of the caller's unread notifications read.
- `GET /notifications/unread-count` returns `{ count }` for badge display.

### R6 — Access control
- All `/notifications` routes require authentication; every role (including Sponsor) may
  read **their own** notifications. Scoping is by `RecipientUserId`, not by role.

## Scenarios

```
SCENARIO: doctor is notified of a new message
  GIVEN a consultation opened by collaborator María with doctor Rodríguez assigned
  WHEN María posts a message
  THEN a Notification row exists for Rodríguez with ReferenceId = the consultation id
  AND María has no notification for her own message

SCENARIO: doctor is notified when assigned a new case
  WHEN a consultation is opened against doctor Rodríguez
  THEN Rodríguez has one unread notification of type ConsultationOpened

SCENARIO: opener is notified on resolution
  GIVEN doctor Rodríguez resolves María's consultation
  THEN María has one unread notification of type ConsultationResolved

SCENARIO: list and mark read
  GIVEN Rodríguez has 2 unread notifications
  WHEN GET /notifications?unreadOnly=true
  THEN 2 items are returned
  WHEN PATCH /notifications/{first.id}/read
  THEN GET /notifications/unread-count returns { count: 1 }

SCENARIO: cannot read another user's notifications
  GIVEN a notification belongs to Rodríguez
  WHEN María calls PATCH /notifications/{id}/read
  THEN 404 is returned
```

## API surface (Phase 1)

```
GET   /notifications?page=&pageSize=&unreadOnly=   → 200 PagedResult<NotificationResponse>
GET   /notifications/unread-count                  → 200 { count }
PATCH /notifications/{id}/read                      → 200 NotificationResponse | 404
POST  /notifications/read-all                       → 204
```

## DTOs

```csharp
record NotificationResponse(
    Guid Id, string Type, string Title, string Body,
    string? ReferenceType, Guid? ReferenceId,
    bool IsRead, DateTime? ReadAt, DateTime CreatedDate);

record UnreadCountResponse(int Count);
```

## Domain events (Phase 1)

```csharp
record ConsultationMessagePostedEvent(
    Guid ConsultationId, string Title, Guid AuthorId,
    Guid OpenedByUserId, Guid AssignedDoctorId) : INotification;

record ConsultationOpenedEvent(
    Guid ConsultationId, string Title, Guid AssignedDoctorId) : INotification;

record ConsultationResolvedEvent(
    Guid ConsultationId, string Title, Guid OpenedByUserId) : INotification;
```

## NotificationType enum

`ConsultationMessage`, `ConsultationOpened`, `ConsultationResolved`
(Phase 3 adds `SessionUpcoming`, `SessionMissed`).

## Later phases (outline only — full specs added when each PR starts)

- **P2 Email**: an `INotificationHandler` per event sends an email to the recipient via
  `IEmailService` using new `EmailTemplates`; best-effort (R3).
- **P3 Session reminders**: `SessionMissedEvent` published from `UpdateSessionStatus` when
  status → Missed; `SessionUpcomingEvent` from an `IHostedService` that scans Scheduled
  sessions whose `SessionDate` falls in the next N hours (dedup so each session reminds once).
- **P4 Push**: `PushSubscription` entity + registration endpoints; a push
  `INotificationHandler`; provider per the proposal's open question.
