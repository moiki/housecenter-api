# SDD Tasks — Notifications

Delivered as chained PRs (see proposal). This file details **Phase 1**; later phases get
their tasks expanded when their PR starts.

---

## Phase 1 — Foundation + in-app notifications

### T1 — Domain: Notification entity + enum + config + migration
- [x] Add `NotificationType` enum (`ConsultationMessage`, `ConsultationOpened`, `ConsultationResolved`)
- [x] Create `Notification : BaseEntity` (RecipientUserId, Type, Title, Body, ReferenceType?, ReferenceId?, IsRead, ReadAt?)
- [x] Create `NotificationConfiguration` — enum as string, Restrict on RecipientUserId FK, index on (RecipientUserId, IsRead)
- [x] Add `DbSet<Notification>` to `HouseCenterDbContext`
- [x] Migration `AddNotifications` + `dotnet ef database update`

**Acceptance:** migration applies, build clean.

### T2 — Domain events
- [x] Create `Features/Notifications/Events/ConsultationEvents.cs`:
  `ConsultationMessagePostedEvent`, `ConsultationOpenedEvent`, `ConsultationResolvedEvent` (all `: INotification`)

### T3 — Publish events from consultation handlers
- [x] Inject `IPublisher` into `OpenConsultationHandler`, `PostConsultationMessageHandler`,
  `UpdateConsultationStatusHandler`
- [x] Publish the matching event **after** `SaveChangesAsync` succeeds
- [x] Capture the data the event needs before returning (ids, title)

**Acceptance:** existing consultation tests still green.

### T4 — In-app channel handlers
- [x] Create `Features/Notifications/Handlers/InAppNotificationHandlers.cs` with an
  `INotificationHandler<T>` for each event
- [x] Resolve recipient(s) per R2; never notify the author
- [x] Persist `Notification` rows via scoped `DbContext`
- [x] Wrap channel work in try/catch + log (R3 best-effort)

### T5 — Read API
- [x] `NotificationDtos.cs` (`NotificationResponse`, `UnreadCountResponse`)
- [x] CQRS queries/commands: `ListNotifications`, `GetUnreadCount`, `MarkNotificationRead`, `MarkAllRead`
- [x] `NotificationEndpoints.cs` (4 routes, `RequireAuthorization()`, recipient-scoped)
- [x] Register `MapNotificationEndpoints()` in `Program.cs`

**Acceptance:** build clean.

### T6 — Integration tests
- [x] `NotificationTests.cs`:
  - `PostMessage_NotifiesOtherParticipant_NotAuthor`
  - `OpenConsultation_NotifiesAssignedDoctor`
  - `ResolveConsultation_NotifiesOpener`
  - `ListNotifications_UnreadOnly_ReturnsUnread`
  - `MarkRead_DecrementsUnreadCount`
  - `MarkRead_OnAnotherUsersNotification_Returns404`

**Acceptance:** all new tests green, full suite still green.

---

## Phase 2 — Email channel ✅

- [x] Add `NotificationCreatedEvent : INotification` (fan-out point so recipient resolution
  is not duplicated per channel)
- [x] In-app handlers publish `NotificationCreatedEvent` per persisted notification
- [x] `EmailTemplates.Notification(title, body)`
- [x] `EmailNotificationHandler : INotificationHandler<NotificationCreatedEvent>` — looks up
  recipient email, sends templated email, best-effort (try/catch + log)
- [x] Test infra: `RecordingEmailService` double registered in `ApiFactory` (with
  `ThrowOnSend` toggle), exposed as `factory.Emails`
- [x] Tests: `OpenConsultation_EmailsAssignedDoctor`,
  `EmailFailure_DoesNotBreakAction_AndInAppStillCreated`

**Acceptance:** full suite green (76/76).

## Phase 3 — Session reminders (tasks expanded when PR starts)
- `SessionMissedEvent` from `UpdateSessionStatus`; `SessionUpcomingEvent` from an `IHostedService` scanner with once-only dedup; tests.

## Phase 4 — Push (tasks expanded when PR starts)
- Resolve push provider; `PushSubscription` entity + registration endpoints; push handler; tests.

---

## Estimated effort
Phase 1: 4–6 hours. Full feature: ~3–4 phased PRs.
