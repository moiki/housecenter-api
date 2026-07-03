# SDD Spec — Help Center

## Requirements

### R1 — HelpTopic entity & validation
- `HelpTopic` MUST have `TopicKey`, `Title`, `Summary`, `Body`, `Audience` (`HelpAudience`),
  `Category`, `DisplayOrder`, plus `BaseEntity` fields (`Id`, `IsActive`, `CreatedDate`, `UpdatedDate`).
- `TopicKey` MUST be unique across ALL rows, including soft-deleted (`IsActive = false`) ones
  — checked in the create handler via `IgnoreQueryFilters()`, not the DB constraint alone.
- `TopicKey` MUST be immutable after creation: an update payload that includes a different
  `TopicKey` value MUST NOT change the persisted `TopicKey`.
- `Audience` MUST be stored as a string (`HasConversion<string>()`), per project convention.
- Field limits per the Validation rules table below MUST be enforced by FluentValidation.

### R2 — List reads are audience-filtered summaries
- `GET /help/topics` MUST return only topics whose `Audience` is at-or-below the caller's
  tier on the ladder: Sponsor→`All`; Doctor→`+Staff`; Member→`+MemberOrAbove`;
  Administrator→`+AdministratorOrAbove`; Owner→all.
- The response MUST contain summary fields only (`TopicKey, Title, Summary, Category,
  DisplayOrder`) — `Body` MUST NOT be included.
- The list MUST NOT be paginated; an optional `?category=` filter MAY be supported.

### R3 — By-key reads are audience-gated, 404 on mismatch
- `GET /help/topics/{topicKey}` MUST return the full topic (including `Body`) when
  `Audience` is within the caller's tier.
- `GET /help/topics/{topicKey}` MUST return 404 — not 403 — both when the topic does not
  exist and when it exists but is outside the caller's audience, so existence is never leaked.

### R4 — Authentication and write authorization
- All five endpoints MUST require an authenticated caller; there is no anonymous access.
- `POST /help/topics`, `PUT /help/topics/{id}`, `DELETE /help/topics/{id}` MUST require
  `AdministratorOrAbove`; callers below that tier MUST receive 403.

### R5 — Duplicate-key conflict on create
- `POST /help/topics` MUST return 409 when `TopicKey` already exists on an active row.
- `POST /help/topics` MUST also return 409 — with a message indicating the topic exists but
  is unpublished — when `TopicKey` matches a soft-deleted row. It MUST NOT return 500 or
  silently succeed.

### R6 — Soft delete = unpublish
- `DELETE /help/topics/{id}` MUST set `IsActive = false` and return 204.
- Once soft-deleted, the topic MUST disappear from both `GET /help/topics` and
  `GET /help/topics/{topicKey}` for every role, via the existing global query filter.

### R7 — Seeder idempotency
- `DatabaseSeeder` MUST insert a `HelpTopic` only when no row with that `TopicKey` already
  exists (insert-if-missing).
- The seeder MUST NOT update or overwrite an existing row's fields on any run, so admin edits
  made via the API survive reseeding.

## Scenarios

```
SCENARIO: sponsor lists help topics
  GIVEN 20 topics exist: 4 with Audience "All", 16 with "Staff" or narrower
  AND authenticated user has Sponsor role
  WHEN GET /help/topics
  THEN 200 returns exactly the 4 "All" summaries; no Body field present

SCENARIO: sponsor deep-links a staff-only topic
  GIVEN topic "sessions.recording" has Audience "Staff"
  AND authenticated user has Sponsor role
  WHEN GET /help/topics/sessions.recording
  THEN 404 is returned (existence not leaked)

SCENARIO: unauthenticated request is rejected
  GIVEN no auth token is provided
  WHEN GET /help/topics
  THEN 401 is returned

SCENARIO: member attempts to create a topic
  GIVEN authenticated user has Member role
  WHEN POST /help/topics with a valid payload
  THEN 403 is returned; no row is created

SCENARIO: duplicate active key rejected
  GIVEN topic "patients.attention-types" exists and is active
  AND authenticated user is Administrator
  WHEN POST /help/topics with topicKey "patients.attention-types"
  THEN 409 is returned; no new row is created

SCENARIO: create over a soft-deleted key
  GIVEN topic "reports.role-scope" was soft-deleted (IsActive = false)
  AND authenticated user is Administrator
  WHEN POST /help/topics with topicKey "reports.role-scope"
  THEN 409 is returned, explaining the topic exists but is unpublished

SCENARIO: admin renames a topic key via update
  GIVEN topic "clinics.overview" exists
  AND authenticated user is Administrator
  WHEN PUT /help/topics/{id} with topicKey set to a different value
  THEN the update succeeds but the persisted TopicKey is unchanged

SCENARIO: admin soft-deletes a topic
  GIVEN topic "clinics.overview" exists and is active
  AND authenticated user is Administrator
  WHEN DELETE /help/topics/{id}
  THEN 204 is returned
  AND GET /help/topics/clinics.overview subsequently returns 404 for every role

SCENARIO: seeder reseeds without overwriting an admin edit
  GIVEN topic "sessions.recording" was seeded then edited via PUT
  AND the database already has 3 of the 5 starter topics
  WHEN the DatabaseSeeder runs again
  THEN the edited fields remain unchanged, the 2 missing topics are inserted,
       and no duplicate row is created for any existing TopicKey
```

## API surface

```
GET    /help/topics                → 200 List<HelpTopicSummaryResponse>  [any authenticated role]
GET    /help/topics/{topicKey}     → 200 HelpTopicResponse | 404        [any authenticated role]
POST   /help/topics                → 201 HelpTopicResponse | 409        [AdministratorOrAbove]
PUT    /help/topics/{id:guid}      → 200 HelpTopicResponse              [AdministratorOrAbove]
DELETE /help/topics/{id:guid}      → 204                                [AdministratorOrAbove]
```

## DTOs

```csharp
record HelpTopicSummaryResponse(
    string TopicKey, string Title, string Summary, string Category, int DisplayOrder);

record HelpTopicResponse(
    Guid Id, string TopicKey, string Title, string Summary, string Body,
    string Audience, string Category, int DisplayOrder, DateTime UpdatedDate);

record CreateHelpTopicRequest(
    string TopicKey, string Title, string Summary, string Body,
    HelpAudience Audience, string Category, int DisplayOrder);

record UpdateHelpTopicRequest(   // TopicKey is ignored/immutable — see R1
    string Title, string Summary, string Body,
    HelpAudience Audience, string Category, int DisplayOrder);
```

## Validation rules

| Field | Rule |
|---|---|
| `TopicKey` | NotEmpty, MaxLength 120, matches `^[a-z0-9]+(-[a-z0-9]+)*(\.[a-z0-9]+(-[a-z0-9]+)*)+$` |
| `Title` | NotEmpty, MaxLength 150 |
| `Summary` | NotEmpty, MaxLength 300 |
| `Body` | NotEmpty, MaxLength 20000 |
| `Audience` | IsInEnum |
| `Category` | NotEmpty, MaxLength 60 |
| `DisplayOrder` | ≥ 0 |
