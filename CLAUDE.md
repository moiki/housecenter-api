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
| Auth | ASP.NET Core JWT — 4 secrets (access/refresh/invitation/reset), `token_kind` claim |
| JwtBearer config | **Lazy** via `AddOptions<JwtBearerOptions>().Configure<IOptions<JwtOptions>>()` — NEVER eager snapshot (breaks WebApplicationFactory) |
| Validation | FluentValidation + `ValidationBehavior` in MediatR pipeline |
| Logging | Serilog structured — bootstrap logger + request logging + CorrelationId |
| MediatR pipeline order | `PerformanceBehavior → LoggingBehavior → ValidationBehavior → Handler` |
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
- **MediatR cast in ValidationBehavior**: `(TResponse)(Result)error` — double cast needed because
  compiler can't verify `Result → TResponse` at compile time, even with `where TResponse : Result`.
- **DbContext is not thread-safe**: `Task.WhenAll` over multiple hot queries on one scoped
  context races and throws "a second operation was started on this context instance".
  Await queries sequentially; true parallelism needs one context per query (`IDbContextFactory`).

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
Jwt:Secret / RefreshSecret / InvitationSecret / ResetSecret
Email:Host / Port / UserName / Password / FromAddress
App:FrontendUrl
Seed:OwnerEmail / Seed:OwnerPassword
```

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
