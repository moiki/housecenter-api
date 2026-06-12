# HOH Backend — Migration Plan: TypeScript → .NET 10

## Original project summary

**Domain:** Medical clinic / patient collaboration platform (Nicaragua defaults)
**Stack:** Express + Apollo Server + TypeGraphQL + TypeORM + PostgreSQL

### What the API does

| Module | Operations |
|---|---|
| **Auth** | login, refresh token, signup via invitation, password reset by email |
| **Users** | CRUD, role assignment, theme preference (dark/light) |
| **Roles** | list (Root / Administrator / Local Member / Support Doctor / Sponsor) |
| **Invitations** | create + send email, resend, delete, validate hash |
| **Clinics** | CRUD |
| **WorkRoutes** | CRUD with destination points |
| **Employees** | CRUD + positions |
| **Patients** | CRUD + collaborators |
| **Treatments** | history, details, status (active/completed/paused) |
| **Comments** | patient comments (ROUTE/MEDICAL/SIMPLE) + treatment comments |

### Entity map (TypeScript → C#)

| TypeScript Entity | C# Entity | Notes |
|---|---|---|
| `MyEntity` (abstract) | `BaseEntity` (abstract) | id(uuid), is_active, created_date, updated_date |
| `User` | `User` | hashed_password → HashedPassword |
| `Roles` | `Role` | singular in C# convention |
| `Patient` | `Patient` | gender enum |
| `Clinic` | `Clinic` | |
| `WorkRoute` | `WorkRoute` | destination: json[] → owned entity |
| `Employee` | `Employee` | |
| `Position` | `Position` | |
| `TreatmentHistory` | `Treatment` | simplified name |
| `TreatmentDetail` | `TreatmentDetail` | |
| `TreatmentCommentary` | `TreatmentComment` | |
| `Comentary` | `PatientComment` | "Comentary" typo corrected in C# |
| `Invitation` | `Invitation` | invitation_status enum |

### Known pitfalls in original (carry forward or fix)

- `synchronize: true` in TypeORM → **NOT carried forward** — EF migrations only
- Error handling inconsistent → **fixed** with Result Pattern
- `Comentary`/`Pacient` typos → **corrected** in C# layer, DB column aliases if needed
- No tests → **xUnit added from Phase 2**
- Hard delete (`.remove()`) despite `is_active` → **decision per entity** (see Phase 1.3)
- `inversify` unused → **not migrated** — .NET DI is built-in

---

## Architecture decisions

### Why REST instead of GraphQL

The original uses TypeGraphQL (code-first GraphQL). In .NET, the equivalent would be
Hot Chocolate — a complex library with its own learning curve. Since the primary goal
is learning .NET/C#, REST Minimal API is the right choice:
- Native to ASP.NET Core
- No extra abstraction layer
- Simpler to test
- GraphQL can be added later as a separate layer if needed

### Project structure (Minimal API)

```
Hoh.Api/
├── Program.cs                          ← DI registration + pipeline
├── Hoh.Api.csproj
├── appsettings.json
├── appsettings.Development.json
│
├── Data/
│   ├── HohDbContext.cs                 ← DbContext
│   └── Configurations/                 ← IEntityTypeConfiguration<T> per entity
│       ├── UserConfiguration.cs
│       ├── RoleConfiguration.cs
│       └── ...
│
├── Domain/
│   └── Entities/                       ← EF Core entity classes
│       ├── BaseEntity.cs               ← abstract base
│       ├── User.cs
│       └── ...
│
├── Features/                           ← Vertical slice from Phase 3
│   ├── Auth/
│   │   ├── Endpoints/
│   │   ├── Commands/
│   │   ├── Queries/
│   │   └── Dtos/
│   ├── Clinics/
│   └── ...
│
├── Common/
│   ├── Results/                        ← Result<T>, Error, ErrorType
│   ├── Middleware/                     ← GlobalExceptionMiddleware
│   └── Extensions/                     ← WebApplication extensions
│
├── Options/                            ← IOptions<T> config classes
│   ├── JwtOptions.cs
│   ├── EmailOptions.cs
│   └── AppOptions.cs
│
└── Tests/                              ← xUnit project (or Hoh.Api.Tests/)
```

### Error handling strategy

```csharp
// Every service/handler returns Result<T>
// Never throws for expected business errors
// GlobalExceptionMiddleware handles unexpected exceptions
// All errors map to ProblemDetails (RFC 9457)

Result<User> result = await userService.GetAsync(id, ct);
if (result.IsFailure)
    return Results.Problem(result.Error.ToProblemDetails());
return Results.Ok(result.Value);
```

### Auth strategy

- `POST /auth/login` → `{ accessToken, refreshToken }`
- Access token: short-lived JWT (15min-1h) signed with `Jwt:Secret`
- Refresh token: longer JWT signed with `Jwt:RefreshSecret`
- Invitation token: short-lived JWT (72h) signed with `Jwt:InvitationSecret`
- Reset token: short-lived JWT (1h) signed with `Jwt:ResetSecret`
- Role-based: `[Authorize(Roles = "Root")]` or policy-based

---

## Phase Tracker

### Phase 1 — Foundation
**Goal:** Working .NET project connected to Postgres, all entities mapped, seeded

- [x] 1.1 Scaffold project with `dotnet new webapi --use-minimal-apis`
- [x] 1.2 Add NuGet packages (Npgsql.EFCore, Serilog, FluentValidation)
- [x] 1.3 Define `BaseEntity` abstract class
- [x] 1.4 Create all 13 domain entities
- [x] 1.5 Create `IEntityTypeConfiguration<T>` for each entity
- [x] 1.6 Create `HouseCenterDbContext` with `ApplyConfigurationsFromAssembly`
- [x] 1.7 Options classes: `DatabaseOptions`, `JwtOptions`, `EmailOptions`, `AppOptions`, `SeedOptions`
- [x] 1.8 Wire up Serilog in `Program.cs`
- [x] 1.9 First migration: `dotnet ef migrations add InitialCreate`
- [x] 1.10 Seeder: 5 roles + Owner user on startup (via `IHostedService`)
- [x] 1.11 Health check endpoint: `GET /health`

**✅ PHASE 1 COMPLETE — 2026-06-09**

**Concepts practiced:** EF Core code-first, Fluent API, Options Pattern, IHostedService

---

### Phase 2 — Auth Module
**Goal:** Full auth flow pariry with original + Result Pattern introduced + first tests

- [x] 2.1 `TokenService`: generate + validate access and refresh tokens
- [x] 2.2 JWT middleware: `AddAuthentication().AddJwtBearer()` (lazy-configured via `IOptions`)
- [x] 2.3 Authorization policies: `OwnerOnly`, `AdministratorOrAbove`
- [x] 2.4 `POST /auth/login`
- [x] 2.5 `POST /auth/refresh`
- [x] 2.6 `GET /auth/me` (authenticated)
- [x] 2.7 Implement `Result<T>` and `Error` types
- [x] 2.8 Global exception handling → `IExceptionHandler` + ProblemDetails (modern .NET 8+ idiom)
- [x] 2.9 `IEmailService` + `SmtpEmailService` (MailKit; logs instead of sending when SMTP unset)
- [x] 2.10 Email templates (invitation + password reset)
- [x] 2.11 Invitation endpoints (create, resend, delete, validate token)
- [x] 2.12 Password reset endpoints (request, reset; validate = token check inside reset)
- [x] 2.13 `POST /auth/signup` (invitation-based, auto-login response)
- [x] 2.14 FluentValidation for all request DTOs (generic `ValidationFilter<T>` endpoint filter)
- [x] 2.15 Integration tests: login flow + signup flow (10 tests, real Postgres `housecenter_test`)

**✅ PHASE 2 COMPLETE — 2026-06-09** · See `PHASE2-AUTH-REVIEW.md` for the concept review doc.

**Concepts practiced:** JWT, middleware pipeline, Result Pattern, FluentValidation, WebApplicationFactory

---

### Phase 3 — Core Domain
**Goal:** Users, Roles, Clinics, WorkRoutes, Employees — with CQRS intro

- [x] 3.1 Generic `IRepository<T>` + `EfRepository<T>` (thin + `Query()` escape hatch; complex reads go straight to DbContext)
- [x] 3.2 Users CRUD endpoints (list/get/update/deactivate + roles assignment [Owner] + theme self-service)
- [x] 3.3 Roles list endpoint (inline — no service layer for a trivial catalog read)
- [x] 3.4 Clinics CRUD endpoints (service + repository; CQRS refactor target for 3.8)
- [x] 3.5 WorkRoutes CRUD endpoints (owned JSON destinations — wholesale replace on update)
- [x] 3.6 Collaborators CRUD endpoints (nested Positions: real entities, clear+add = delete+insert via required FK)
- [x] 3.7 Add MediatR 12.5.0 (pinned: 12.x is Apache 2.0; 13+ is commercial) + assembly scan registration
- [ ] 3.8 Refactor Clinics to CQRS (handlers for all operations)
- [ ] 3.9 `ValidationBehavior` pipeline behavior
- [ ] 3.10 `LoggingBehavior` pipeline behavior
- [ ] 3.11 Integration tests for Clinics + Users

**Concepts practiced:** Repository pattern, CQRS, MediatR, pipeline behaviors

---

### Phase 4 — Patient Domain
**Goal:** Full patient/treatment/comment flow with real concurrency

- [ ] 4.1 Patients CRUD endpoints
- [ ] 4.2 `POST /patients/{id}/collaborators/{userId}`
- [ ] 4.3 `GET /patients/{id}/full-summary` (parallel: patient + treatment + comments)
- [ ] 4.4 TreatmentHistory CRUD
- [ ] 4.5 `PATCH /treatments/{id}/status`
- [ ] 4.6 TreatmentDetails (nested CRUD)
- [ ] 4.7 TreatmentComments CRUD
- [ ] 4.8 PatientComments CRUD (ROUTE/MEDICAL/SIMPLE)
- [ ] 4.9 `PagedResult<T>` — pagination for all list endpoints
- [ ] 4.10 EF Core global query filter for `IsActive` (soft delete)
- [ ] 4.11 Integration tests for patient flow

**Concepts practiced:** Task.WhenAll in real endpoint, soft-delete with EF query filters, pagination

---

### Phase 5 — Polish & Advanced
**Goal:** Full CQRS everywhere, observability, tests complete

- [ ] 5.1 Migrate all remaining modules to CQRS handlers
- [ ] 5.2 `PerformanceBehavior` — log if handler > 200ms
- [ ] 5.3 Correlation ID middleware
- [ ] 5.4 `AsNoTracking()` audit — all read queries verified
- [ ] 5.5 Detailed health checks (DB + email connectivity)
- [ ] 5.6 API versioning with `Asp.Versioning.Http`
- [ ] 5.7 Unit tests for MediatR handlers
- [ ] 5.8 Full test pass — all critical flows covered

**Concepts practiced:** Structured logging, distributed tracing basics, API versioning, benchmarking reads

---

## Concepts by phase (learning map)

| Phase | Key concepts applied |
|---|---|
| 1 | EF Core code-first, Fluent API, Options Pattern, IHostedService |
| 2 | JWT, ASP.NET Core middleware, Result Pattern, FluentValidation, integration tests |
| 3 | Repository pattern, CQRS, MediatR, pipeline behaviors, async/await real usage |
| 4 | Task.WhenAll in production, soft delete, pagination, complex EF relations |
| 5 | Structured logging, API versioning, AsNoTracking audit, unit tests |

---

## Session notes

> Add notes here as work progresses. Each session should end with
> a brief note: what was completed, what was left, any decision made.

### Session log

| Date | Phase | Completed | Notes |
|---|---|---|---|
| — | — | Planning | Plan documented, ready to scaffold |
| 2026-06-09 | 2 (complete) | Tasks 2.1–2.15 | Full auth module: Result Pattern (`Common/Results`), `TokenService` (JsonWebTokenHandler, 4 token kinds with per-kind secret + `token_kind` claim), JwtBearer **lazy-configured via `IOptions`** (eager config snapshot broke under WebApplicationFactory — "signature key not found"), policies `OwnerOnly`/`AdministratorOrAbove`, `IExceptionHandler`+ProblemDetails, MailKit email (dev mode logs), invitations + signup + password reset (anti-enumeration 202, `PasswordRecoveryVersion` revokes refresh+reset tokens), `ValidationFilter<T>` for FluentValidation in Minimal APIs, 10 integration tests green vs real Postgres (`housecenter_test`, recreated via `Database:RecreateOnStartup`). Gotchas: namespace `Common.Results` shadows `Results` class; EF version pinning needed in test csproj. |
| 2026-06-09 | 1 (complete) | Tasks 1.1–1.11 | Renames vs plan: project = **HouseCenter** (HOH dropped — ONG closed), solution `HouseCenter/HouseCenter.slnx` + project `HouseCenter.Api`. Entity `Employee` → **Collaborator**. Roles renamed: **Owner / Administrator / Member / Doctor / Sponsor** (Root→Owner, Local Member→Member, Support Doctor→Doctor). DbContext = `HouseCenterDbContext` with audit dates in `SaveChangesAsync`. Enums stored as strings (`HasConversion<string>`). `DestinationPoint` = owned entity as JSON column. Seeder = idempotent `IHostedService`, owner credentials via user secrets (`Seed:OwnerEmail/OwnerPassword`), BCrypt.Net-Next for hashing. `AddDbContextPool` used. Config via user secrets (no .env). Migration `InitialCreate` applied to local Postgres `housecenter`. |
| 2026-06-12 | 3–5 (complete) | Tasks 3.1–5.8 | Phase 3: IRepository<T> + EfRepository, Users/Clinics/WorkRoutes/Collaborators CRUD, MediatR 12.5.0, Clinics refactored to CQRS (reference pattern), ValidationBehavior + LoggingBehavior. Phase 4: Patient domain full — AttentionSession placeholder pending, Treatments, Comments, Task.WhenAll full-summary, PagedResult<T>, global IsActive query filter. Phase 5: PerformanceBehavior, CorrelationId, health checks deep (Postgres probe), API versioning (Clinics on /api/v1), unit tests with InMemory EF. 39/39 tests green. |
| 2026-06-12 | Domain V2 | Planning | Domain analysis complete. Three SDD changes planned: `attention-sessions` (P1), `medical-consultations` (P2), `sponsor-reports` (P3). See `openspec/IMPLEMENTATION_PLAN.md`. CLAUDE.md updated with full domain context and architecture decisions. |
