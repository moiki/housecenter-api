# SDD Tasks — Notifications

Delivered as chained PRs (see proposal). This file details **Phase 1**; later phases get
their tasks expanded when their PR starts.

---

## Phase 1 — Foundation + in-app notifications

### T1 — Domain: Notification entity + enum + config + migration
- [ ] Add `NotificationType` enum (`ConsultationMessage`, `ConsultationOpened`, `ConsultationResolved`)
- [ ] Create `Notification : BaseEntity` (RecipientUserId, Type, Title, Body, ReferenceType?, ReferenceId?, IsRead, ReadAt?)
- [ ] Create `NotificationConfiguration` — enum as string, Restrict on RecipientUserId FK, index on (RecipientUserId, IsRead)
- [ ] Add `DbSet<Notification>` to `HouseCenterDbContext`
- [ ] Migration `AddNotifications` + `dotnet ef database update`

**Acceptance:** migration applies, build clean.

### T2 — Domain events
- [ ] Create `Features/Notifications/Events/ConsultationEvents.cs`:
  `ConsultationMessagePostedEvent`, `ConsultationOpenedEvent`, `ConsultationResolvedEvent` (all `: INotification`)

### T3 — Publish events from consultation handlers
- [ ] Inject `IPublisher` into `OpenConsultationHandler`, `PostConsultationMessageHandler`,
  `UpdateConsultationStatusHandler`
- [ ] Publish the matching event **after** `SaveChangesAsync` succeeds
- [ ] Capture the data the event needs before returning (ids, title)

**Acceptance:** existing consultation tests still green.

### T4 — In-app channel handlers
- [ ] Create `Features/Notifications/Handlers/InAppNotificationHandlers.cs` with an
  `INotificationHandler<T>` for each event
- [ ] Resolve recipient(s) per R2; never notify the author
- [ ] Persist `Notification` rows via scoped `DbContext`
- [ ] Wrap channel work in try/catch + log (R3 best-effort)

### T5 — Read API
- [ ] `NotificationDtos.cs` (`NotificationResponse`, `UnreadCountResponse`)
- [ ] CQRS queries/commands: `ListNotifications`, `GetUnreadCount`, `MarkNotificationRead`, `MarkAllRead`
- [ ] `NotificationEndpoints.cs` (4 routes, `RequireAuthorization()`, recipient-scoped)
- [ ] Register `MapNotificationEndpoints()` in `Program.cs`

**Acceptance:** build clean.

### T6 — Integration tests
- [ ] `NotificationTests.cs`:
  - `PostMessage_NotifiesOtherParticipant_NotAuthor`
  - `OpenConsultation_NotifiesAssignedDoctor`
  - `ResolveConsultation_NotifiesOpener`
  - `ListNotifications_UnreadOnly_ReturnsUnread`
  - `MarkRead_DecrementsUnreadCount`
  - `MarkRead_OnAnotherUsersNotification_Returns404`

**Acceptance:** all new tests green, full suite still green.

---

## Phase 2 — Email channel (tasks expanded when PR starts)
- Email `INotificationHandler` per event via `IEmailService`; `EmailTemplates` additions; best-effort; tests.

## Phase 3 — Session reminders (tasks expanded when PR starts)
- `SessionMissedEvent` from `UpdateSessionStatus`; `SessionUpcomingEvent` from an `IHostedService` scanner with once-only dedup; tests.

## Phase 4 — Push (tasks expanded when PR starts)
- Resolve push provider; `PushSubscription` entity + registration endpoints; push handler; tests.

---

## Estimated effort
Phase 1: 4–6 hours. Full feature: ~3–4 phased PRs.
