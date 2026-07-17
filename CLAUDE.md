# HouseCenter — .NET 10 API

## What this is

**HouseCenter** is a platform that connects collaborators working for an NGO serving
disabled children in rural Nicaragua. Field workers (Member/Collaborator role) attend
patients at fixed clinics or via work routes. Remote doctors (Doctor role) can't visit
but receive progress reports and give medical guidance. Sponsors (Sponsor role) get
curated aggregate visibility to support the NGO's funding.

> Originally "HOH" (House of Hope) — renamed to HouseCenter after the original ONG closed.

This is also a **learning project**: the developer is migrating from Node/TypeScript to
C#/.NET 10, applying concepts progressively. Every decision has a WHY.

---

## Project layout

```
hoh-project-net/
├── ts-hoh-backend/         ← original TypeScript API (reference only, do not modify)
├── house-mui-v1/           ← React/MUI frontend (out of scope)
└── HouseCenter/            ← .NET solution (active work, git root)
    ├── HouseCenter.slnx
    ├── HouseCenter.Api/        ← main API project
    ├── HouseCenter.Api.Tests/  ← integration + unit tests
    ├── CLAUDE.md               ← this file
    ├── MIGRATION_PLAN.md       ← full phase tracker (Phase 1–5 complete)
    ├── PHASE2-AUTH-REVIEW.md   ← auth concepts review document
    └── openspec/               ← SDD artifacts for upcoming changes
        └── changes/
            ├── attention-sessions/
            ├── medical-consultations/
            └── sponsor-reports/
```

---

## Current status

**Phases 1–5 complete (2026-06-12).** Full domain implemented and tested.

| Phase | Content | Status |
|---|---|---|
| 1 | Foundation — scaffold, EF, seeder | ✅ |
| 2 | Auth — JWT, Result Pattern, invitations, tests | ✅ |
| 3 | Core domain — Users, Clinics (CQRS), WorkRoutes, Collaborators, MediatR pipeline | ✅ |
| 4 | Patient domain — Patients, Treatments, Comments, pagination, global soft-delete filter | ✅ |
| 5 | Polish — PerformanceBehavior, CorrelationId, health checks, API versioning, unit tests | ✅ |

**Domain V2 complete (2026-06-12).** All three SDD changes implemented — see `openspec/changes/`.
**Next work:** see "What comes after Domain V2" in `openspec/IMPLEMENTATION_PLAN.md`
(notifications, file uploads, anonymization, frontend integration).

---

## Domain context

### Actors
| Role | Who | What they do |
|---|---|---|
| Owner | NGO director | Full access, user management |
| Administrator | HouseCenter admin | Manages clinics, routes, collaborators |
| Member | Field collaborator | Attends patients, records sessions and progress |
| Doctor | Remote medical professional | Reviews cases assigned to them, gives guidance via consultations |
| Sponsor | Financial supporter | Sees aggregated/anonymized reports, no individual patient data |

### Patient care types
- **Medical** — physical therapy, rehabilitation
- **Educational** — school reinforcement for disabled children

A patient can have both types. Treatments are typed accordingly.

### Core flow
1. Patient enrolled → assigned to clinic and/or work route
2. Field collaborator records **AttentionSessions** (who attended, when, where)
3. Collaborator creates **TreatmentDetails** (progress per session)
4. If guidance is needed → opens **MedicalConsultation** to assigned doctor
5. Doctor responds via consultation thread
6. Sponsors see aggregate **Reports** — no individual clinical data

---

## Architecture decisions (locked)

| Concern | Decision |
|---|---|
| API style | REST — Minimal API (.NET 10) |
| ORM | EF Core 10 + Npgsql + Fluent API (`IEntityTypeConfiguration<T>` per entity) |
| DbContext | `HouseCenterDbContext`, `AddDbContextPool`, audit dates in `SaveChangesAsync` |
| Soft delete | Global `IsActive` query filter in `OnModelCreating` — use `.IgnoreQueryFilters()` to bypass |
| Enums in DB | Stored as strings (`HasConversion<string>()`) |
| Error handling | Result Pattern (`Result<T>`, `Error`, `ErrorType`) + ProblemDetails RFC 9457 |
| Auth | ASP.NET Core JWT — 3 secrets (access/invitation/reset), `token_kind` claim. Refresh is an opaque, hashed, DB-persisted `UserSession` token — not a JWT (see `openspec/changes/device-bound-sessions/`) |
| JwtBearer config | **Lazy** via `AddOptions<JwtBearerOptions>().Configure<IOptions<JwtOptions>>()` — NEVER eager snapshot (breaks WebApplicationFactory) |
| Validation | FluentValidation via `ValidationFilter<T>` endpoint filter (`.WithValidation<T>()`) — validates the HTTP request DTO before MediatR runs. See gotchas for why the MediatR `ValidationBehavior` itself is currently dead code. |
| Rate limiting | Opt-in per-request via `IRateLimitedRequest` + `RateLimitBehavior` in MediatR pipeline — in-memory fixed-window counter (20/min), 429 on exceed |
| Logging | Serilog structured — bootstrap logger + request logging + CorrelationId |
| MediatR pipeline order | `PerformanceBehavior → LoggingBehavior → RateLimitBehavior → ValidationBehavior → Handler` |
| MediatR version | **12.5.0** pinned — 12.x is Apache 2.0; 13+ is commercial |
| DI | Built-in + open generic: `AddScoped(typeof(IRepository<>), typeof(EfRepository<>))` |
| Architecture | Vertical slices (`Features/`) — service pattern OR CQRS per module (see below) |
| Pattern per module | Clinics = CQRS (handlers). Others = Service. New modules: CQRS by default. |
| Testing | xUnit + FluentAssertions + WebApplicationFactory (integration) + InMemory EF (unit) |
| Password hashing | BCrypt.Net-Next |
| Seeding | Idempotent `IHostedService` (`DatabaseSeeder`) |
| API versioning | `Asp.Versioning.Http` — URL segment: `/api/v{n}/resource` |
| Health checks | `/health` (liveness) + `/health/ready` (readiness — Postgres probe) |

---

## Module patterns

| Module | Pattern | Reason |
|---|---|---|
| Auth | Service | Transactional coordination across email + token + DB |
| Invitations | Service | Same — coordinates email send within the operation |
| Users | Service | Role assignment has inter-operation state dependency |
| Roles | Inline (no service) | Trivial catalog read |
| Clinics | **CQRS** | Pure CRUD — reference implementation of the pattern |
| WorkRoutes | Service | Owned JSON destinations need wholesale replace semantics |
| Collaborators | Service | Positions (real entities) clear+add via tracked collection |
| Patients | Service | Large domain — multiple sub-resources in one service |
| AttentionSessions | **CQRS** | New, clean slate — CQRS by default |
| MedicalConsultations | **CQRS** | New, clean slate |
| Reports | **CQRS** | Read-only query handlers, role-based projection |
| HelpTopics | **CQRS** | New, clean slate |

---

## Key gotchas

- **EF projection in `Select`**: must be inline `new ResponseType(...)` — passing a method reference
  throws "could not be translated" at runtime. EF translates expression trees, not delegates.
- **JwtBearer eager config**: reading `JwtOptions` at startup (not via `IOptions`) creates a snapshot
  that ignores WebApplicationFactory overrides → "signature key not found" in tests.
- **`|| !entity.IsActive` guards**: redundant after global query filter was added in Phase 4.
  `GetByIdAsync` returns `null` for inactive records — the `!IsActive` branch is dead code.
- **EF async methods in unit tests**: `AnyAsync`, `ToListAsync` require an EF query provider.
  `List<T>.AsQueryable()` doesn't work — use `UseInMemoryDatabase` instead.
- **`UseInMemoryDatabase` doesn't enforce unique indexes**: `HasIndex(...).IsUnique()` (filtered or not)
  is never checked at `SaveChangesAsync` under the InMemory provider — inserting two rows with the same
  key succeeds silently. A unit test asserting duplicate-rejection is testing nothing; verify real
  constraint behavior (409 on conflict) with an integration test against actual Postgres instead.
- **`ValidationBehavior`'s double-cast is dead code, and unsafe if it ever runs**: request validation
  actually happens via `ValidationFilter<T>` (`.WithValidation<T>()`), an endpoint filter that returns
  400 before MediatR is even invoked — no `IValidator<TCommand>` is ever registered, so
  `ValidationBehavior.validators.Any()` is always `false` and its `return (TResponse)(Result)error;`
  never executes. That line is also **broken** if it ever did: `(Result)error` always builds a plain
  base `Result` via `Result`'s own `implicit operator`, and downcasting that to a closed `Result<T>`
  throws `InvalidCastException` — `Result<T>` has its own separate `implicit operator Result<T>(Error)`
  that the compiler can't reach through a generic type parameter. Any NEW MediatR pipeline behavior
  that needs to short-circuit with an `Error` for `TResponse : Result` must use `RateLimitBehavior`'s
  approach instead: reflect for `TResponse`'s own `op_Implicit(Error)` and invoke it (cached per closed
  type), which correctly resolves to `Result<T>`'s operator when `TResponse` is generic.
- **DbContext is not thread-safe**: `Task.WhenAll` over multiple hot queries on one scoped
  context races and throws "a second operation was started on this context instance".
  Await queries sequentially; true parallelism needs one context per query (`IDbContextFactory`).
- **Npgsql requires `DateTime.Kind=Utc`**: client JSON without a `Z`/offset (e.g.
  `"2024-01-15T00:00:00"`) deserializes as `Kind=Unspecified`, which throws
  `Cannot write DateTime with Kind=Unspecified to PostgreSQL type 'timestamp with time zone'`
  at `SaveChangesAsync`. Fixed globally via a `ValueConverter` in
  `HouseCenterDbContext.OnModelCreating` that tags every `DateTime`/`DateTime?` property as
  UTC — new entities get this for free, no per-property `SpecifyKind` needed.

---

## Naming changes vs original TypeScript

| Original | HouseCenter |
|---|---|
| `Employee` entity | `Collaborator` |
| Root / Local Member / Support Doctor | **Owner / Member / Doctor** |
| `Comentary` / `TreatmentCommentary` | `PatientComment` / `TreatmentComment` |
| `TreatmentHistory` | `Treatment` |
| `Destination` (json column) | `DestinationPoint` — owned entity via `OwnsMany().ToJson()` |

---

## Commands

```bash
cd HouseCenter
dotnet build
dotnet run --project HouseCenter.Api
dotnet watch --project HouseCenter.Api
dotnet test
dotnet ef migrations add <Name> --project HouseCenter.Api
dotnet ef database update --project HouseCenter.Api
```

---

## Configuration (no .env — .NET User Secrets)

Rider → right-click project → Tools → *Open project user secrets*

```
Database:ConnectionString
Jwt:Secret / InvitationSecret / ResetSecret
Email:Host / Port / UserName / Password / FromAddress
App:FrontendUrl
Seed:OwnerEmail / Seed:OwnerPassword
Seed:TestUserPassword  # optional — password for POST /seed test users (default Demo@123!)
Push:CredentialsJson   # Firebase service-account JSON; blank = push logs instead of sending (FCM off)
```

**Test data:** `POST /seed` (Development/Staging only, AdministratorOrAbove) inserts an
idempotent fixed dataset — 5 users (one per non-owner role, `*@housecenter.dev`),
3 clinics, 3 collaborators, 6 patients. Never runs at startup; `DatabaseSeeder` only
seeds roles + owner.

Integration tests override these via `ApiFactory.ConfigureWebHost` (in-memory collection).
Test DB: `housecenter_test` (recreated on each test run).

---

## How to start a new session

1. Read the **Current status** section above
2. Check `openspec/changes/` for the next pending SDD change
3. Check `MIGRATION_PLAN.md` for any remaining Phase 5 items
4. Use `/dotnet-teacher` for implementation guidance

---

## SDD changes (Domain V2)

All three changes implemented (2026-06-12). See `openspec/changes/` for full specs.

| Change | Priority | Status | Summary |
|---|---|---|---|
| `attention-sessions` | P1 | ✅ | Record actual care events (who attended, when, where) |
| `medical-consultations` | P2 | ✅ | Structured thread between collaborator and assigned doctor |
| `sponsor-reports` | P3 | ✅ | Aggregate endpoints with role-based projection |
