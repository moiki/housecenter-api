# Phase 2 — Auth Module: Concept Review

> Documento de repaso. Cada sección explica **qué se hizo** y **por qué así** (why/because),
> desde lo idiomático hasta el estilo senior. Leélo con el código abierto al lado.
>
> Código: `HouseCenter.Api/Common/` y `HouseCenter.Api/Features/`
> Tests: `HouseCenter.Api.Tests/` (10 integration tests, Postgres real)

---

## 1. Result Pattern — `Common/Results/`

**Qué:** `Result<T>` encapsula éxito-con-valor o fallo-con-`Error`. Los servicios nunca
lanzan excepciones para errores de negocio esperados (credenciales inválidas, email tomado).

**Why** — las excepciones en .NET son caras (capturan stack trace completo) y semánticamente
incorrectas para flujos esperados. **Because** un login fallido no es "excepcional": pasa miles
de veces por día. Reservar `throw` para lo verdaderamente inesperado hace que el
`GlobalExceptionHandler` sea señal pura: si llegó ahí, es un bug real.

**Analogía Node:** como retornar `{ ok: false, error }` en vez de `throw new HttpException()`
de NestJS — pero con el compilador forzándote a chequear.

### Detalles senior del archivo `Result.cs`

```csharp
[MemberNotNullWhen(true, nameof(Error))]
public bool IsFailure => !IsSuccess;
```
**Why** este atributo — le enseña al *nullability analysis* del compilador que si
`IsFailure == true`, entonces `Error != null`. **Because** sin él, cada acceso a
`result.Error.Code` después de chequear `IsFailure` daría warning CS8602 y te obligaría
a usar `!` por todos lados. El atributo elimina la fricción sin sacrificar null-safety.

```csharp
public static implicit operator Result<T>(T value) => Success(value);
public static implicit operator Result<T>(Error error) => Failure(error);
```
**Why** conversiones implícitas — permiten `return user;` o `return InvalidCredentials;`
directamente en un método que retorna `Result<User>`. **Because** el boilerplate
`return Result<User>.Success(user)` repetido 50 veces entierra la lógica de negocio;
la conversión implícita mantiene el happy path legible.

```csharp
public TOut Match<TOut>(Func<T, TOut> onSuccess, Func<Error, TOut> onFailure)
```
**Why** un `Match` estilo funcional — obliga a manejar ambas ramas en el endpoint.
**Because** `result.Match(Results.Ok, e => e.ToProblem())` no compila si te olvidás
una rama; un `if` sí permite olvidos.

### `Error.cs` — tipos de error como dominio, no como HTTP

`ErrorType` (Validation/Unauthorized/NotFound/Conflict/Failure) es **agnóstico de HTTP**.
El mapeo a status codes vive en un solo lugar: `ResultExtensions.ToProblem()`.

**Why** — los servicios no deben saber de HTTP. **Because** mañana el mismo `AuthService`
puede servirse por gRPC o un worker; si el servicio retornara `401`, estaría acoplado al
transporte. `ErrorType.Unauthorized` → `401` es decisión de la capa HTTP.

---

## 2. ProblemDetails + `IExceptionHandler` — `Common/Middleware/`

**Qué:** errores HTTP con formato RFC 9457 (`application/problem+json`), y un safety net
para excepciones no controladas.

**Why `IExceptionHandler` y no middleware manual** — el plan original decía
"GlobalExceptionMiddleware", pero desde .NET 8 el idiom es implementar `IExceptionHandler`
y registrarlo con `AddExceptionHandler<T>()` + `UseExceptionHandler()`. **Because** la
interfaz se integra con `AddProblemDetails()` (formato automático), permite encadenar
handlers (uno para `ValidationException`, otro genérico), y el framework se encarga del
pipeline plumbing que en middleware manual escribirías a mano.

**Detalle de seguridad:** en Development el response incluye `exception.Message`; en
Production un mensaje genérico. **Because** un stack trace o mensaje interno filtrado a
producción es information disclosure — regala estructura interna a un atacante.

---

## 3. TokenService — `Features/Auth/TokenService.cs`

### 4 tipos de token, 4 secrets, 1 claim discriminador

| Token | Secret | Vida | Uso |
|---|---|---|---|
| Access | `Jwt:Secret` | 60 min | Bearer en cada request |
| Refresh | `Jwt:RefreshSecret` | 7 días | Renovar el par |
| Invitation | `Jwt:InvitationSecret` | 72 h | Link de signup |
| PasswordReset | `Jwt:ResetSecret` | 1 h | Link de reset |

**Why secrets distintos** — un token firmado con el secret A no puede validarse jamás con
el secret B. **Because** si todos compartieran secret, un invitation token (que viaja por
email, el canal más débil) podría usarse como access token. Compartimentar secrets
compartimenta el blast radius.

**Why además el claim `token_kind`** — defense in depth. **Because** si algún día alguien
unifica dos secrets "para simplificar", el check del claim sigue bloqueando el cross-use.
El test `Refresh_WithAccessTokenInsteadOfRefreshToken_Returns401` verifica exactamente esto.

### `JsonWebTokenHandler` vs `JwtSecurityTokenHandler`

**Why el nuevo handler** — `JsonWebTokenHandler` (Microsoft.IdentityModel.JsonWebTokens) es
el reemplazo moderno del viejo `JwtSecurityTokenHandler`. **Because** es más rápido
(menos allocations), thread-safe (una instancia `static` compartida), async-first
(`ValidateTokenAsync`), y NO aplica el legacy claim-type mapping que renombra `sub` a
un URI de Microsoft de 80 caracteres sin que se lo pidas.

### `PasswordRecoveryVersion` — revocación sin estado

El refresh token y el reset token llevan el claim `prv` con la versión actual.
Al cambiar password: `user.PasswordRecoveryVersion++`.

**Why** — JWT es stateless: no podés "borrar" un token ya emitido. **Because** guardar
una blacklist de tokens en DB mata la gracia del stateless; un contador de versión en el
user invalida TODOS los tokens viejos con un solo `int`, sin tabla extra. Es el patrón
"security stamp" de ASP.NET Identity, en miniatura.

---

## 4. JwtBearer middleware — `Program.cs`

### La lección más importante de la fase: config eager vs lazy

**Lo que falló:** la versión inicial hacía
```csharp
var jwt = builder.Configuration.GetSection("Jwt").Get<JwtOptions>()!; // snapshot AHORA
builder.Services.AddAuthentication().AddJwtBearer(o => { /* usa jwt */ });
```
y los tests devolvían `401 — "The signature key was not found"`.

**Why falló** — `Get<JwtOptions>()` materializa un snapshot en el instante en que corre
`Program.cs`. **Because** `WebApplicationFactory` agrega sus overrides de configuración
*después* de que ese snapshot se tomó: la app de test terminaba validando tokens con el
secret de user-secrets mientras el `TokenService` (que usa `IOptions`, resuelto lazy)
firmaba con el secret de test. Dos fuentes de verdad → firma inválida.

**El fix (estilo senior):**
```csharp
builder.Services.AddAuthentication(JwtBearerDefaults.AuthenticationScheme).AddJwtBearer();
builder.Services.AddOptions<JwtBearerOptions>(JwtBearerDefaults.AuthenticationScheme)
    .Configure<IOptions<JwtOptions>>((bearer, jwt) => { /* lee jwt.Value acá */ });
```
**Why** — `Configure<TDep>` difiere la lectura hasta que el sistema de Options resuelve,
con la configuración FINAL. **Because** la regla general es: **nunca materialices
configuración en variables durante el startup si un componente la va a usar en runtime**.
Todo lo que se configura vía `IOptions` permanece consistente, testeable y hot-reloadable.

### Orden del pipeline

`UseAuthentication()` → `UseAuthorization()` → `Map*()`. **Why el orden importa** —
authentication puebla `HttpContext.User`; authorization lo lee. **Because** el pipeline
de ASP.NET es una cadena secuencial (como Express): si autorizás antes de autenticar,
toda request es anónima y todo `RequireAuthorization` devuelve 401.

---

## 5. Authorization Policies — `Program.cs` + `PolicyNames.cs`

```csharp
builder.Services.AddAuthorizationBuilder()
    .AddPolicy(PolicyNames.OwnerOnly, p => p.RequireRole(RoleNames.Owner))
    .AddPolicy(PolicyNames.AdministratorOrAbove, p => p.RequireRole(RoleNames.Owner, RoleNames.Administrator));
```

**Why policies con nombre y no `[Authorize(Roles = "Owner,Administrator")]` inline** —
la policy centraliza la regla. **Because** "qué significa ser admin" va a cambiar
(¿agregar un claim de tenant? ¿un requirement custom?); con policy se cambia en UN lugar.
Strings de roles dispersos en 30 endpoints son el equivalente a magic numbers.

**Why `AdministratorOrAbove` incluye Owner** — jerarquía explícita. **Because** sin eso,
el Owner (máximo poder) no podría crear invitaciones — el clásico bug de "el super-admin
no puede hacer X porque el check pide exactamente el rol Y".

---

## 6. Vertical Slice — `Features/Auth/`, `Features/Invitations/`

**Qué:** cada feature agrupa endpoints + servicio + DTOs + validators en SU carpeta,
en vez de `Controllers/`, `Services/`, `Dtos/` horizontales.

**Why** — cohesión por cambio. **Because** cuando toques "invitations" vas a tocar su
endpoint, su DTO y su validator JUNTOS; tenerlos en tres carpetas distintas optimiza
para el lector equivocado. Vertical slice optimiza para "todo lo que cambia junto, vive junto".

**Endpoints como extension methods + `MapGroup`:**
```csharp
var group = app.MapGroup("/auth").WithTags("Auth");
```
**Why** — `MapGroup` aplica prefijo, tags de OpenAPI y filtros a todo el grupo.
**Because** repetir `/auth/` en 6 rutas y `.WithTags("Auth")` en cada una es ruido;
y un `RequireAuthorization()` a nivel grupo es imposible de olvidar en el endpoint nuevo.

---

## 7. FluentValidation en Minimal APIs — `Common/Validation/ValidationFilter.cs`

**Qué:** un `IEndpointFilter` genérico que corre el validator ANTES del handler y
devuelve `400 ValidationProblem` con los errores por campo.

**Why un filter y no validar dentro del handler** — separación de concerns + pipeline.
**Because** Minimal APIs no tiene el auto-validation de MVC; el filter lo repone de forma
opt-in y declarativa: `.WithValidation<LoginRequest>()` se lee como contrato del endpoint.

**Why `AddValidatorsFromAssemblyContaining<Program>()`** — descubrimiento por reflexión
al startup. **Because** registrar 5 validators a mano hoy son 5 líneas; en Phase 4 serán 20,
y el día que te olvides uno el endpoint queda silenciosamente sin validar. Convención > memoria.

**Gotcha real que nos pasó:** dentro del namespace `HouseCenter.Api.Common.Validation`,
el identificador `Results` resolvía al **namespace** `HouseCenter.Api.Common.Results`
(sibling), no a la clase `Microsoft.AspNetCore.Http.Results` → error CS0234.
Fix: fully-qualify. **Lección:** nombrar un namespace igual que una clase del framework
que usás seguido tiene costo; considerá `Common.ResultTypes` si molesta mucho.

---

## 8. Seguridad aplicada (los detalles que separan junior de senior)

| Decisión | Why / Because |
|---|---|
| Login: un solo error `auth.invalid_credentials` para email inexistente Y password mal | **Because** errores distintos permiten *user enumeration*: un atacante descubre qué emails existen probando |
| `POST /password/request` → siempre `202 Accepted` | **Because** mismo motivo — la respuesta no puede revelar si la cuenta existe; el email solo se envía si existe |
| Signup toma el email DEL invitation token, nunca del body | **Because** si el cliente mandara el email, podría registrarse con un email distinto al invitado usando un token ajeno |
| Reset token de un solo uso (`prv` + increment) | **Because** un reset link reenviado/filtrado no debe funcionar dos veces |
| BCrypt para passwords | **Because** es deliberadamente lento (work factor) — GPU-cracking de un dump de DB se vuelve impracticable; SHA256 sería un bug de seguridad |
| `EmailConfirmed = true` en signup por invitación | **Because** el flujo ya probó posesión del email: el token llegó POR email |

---

## 9. Options con validación al startup

```csharp
builder.Services.AddOptions<JwtOptions>()
    .Bind(builder.Configuration.GetSection(JwtOptions.SectionName))
    .Validate(o => !string.IsNullOrWhiteSpace(o.Secret), "Jwt:Secret is required")
    .Validate(o => o.Secret != o.RefreshSecret, "Jwt:Secret and Jwt:RefreshSecret must differ")
    .ValidateOnStart();
```

**Why `ValidateOnStart`** — fail fast. **Because** sin esto, un `Jwt:Secret` vacío en
producción se descubre en el PRIMER login fallido a las 3 AM, no en el deploy. La app
debe negarse a arrancar mal configurada.

**Why validar que los secrets difieran** — es exactamente el invariante de seguridad
de la sección 3, ahora ejecutable. La arquitectura que no se valida sola, se erosiona.

---

## 10. Integration tests — `HouseCenter.Api.Tests/`

### Postgres real, no in-memory

**Why no EF InMemory/SQLite** — el provider in-memory no soporta `OwnsMany().ToJson()`
(nuestra columna JSON de `DestinationPoint`), ni valida SQL real, ni constraints.
**Because** un test que pasa contra un fake y falla contra Postgres es peor que no tener
test: da confianza falsa. Se usa `housecenter_test`, recreada desde las migraciones en
cada arranque del host de test (`Database:RecreateOnStartup=true`) — así además las
migraciones se testean solas.

### `WebApplicationFactory<Program>` + `public partial class Program;`

**Why esa línea rara al final de `Program.cs`** — con top-level statements el compilador
genera una clase `Program` *internal*. **Because** `WebApplicationFactory<Program>`
necesita verla desde el assembly de tests; `public partial class Program;` la expone
sin tocar nada más. Es el idiom oficial.

### El truco de configuración de la factory

```csharp
var current = config.Build();                       // lee user secrets YA registrados
var testCs = new NpgsqlConnectionStringBuilder(current["Database:ConnectionString"])
    { Database = "housecenter_test" }.ConnectionString;
config.AddInMemoryCollection(...);                  // override: gana la última fuente
```
**Why** — reusar la connection string del dev (host/user/password) cambiando solo el
nombre de la DB. **Because** hardcodear credenciales de Postgres en el test rompería en
la máquina de cualquier otro dev; heredar de user-secrets hace el test portable.

### Qué se testea y por qué ESOS tests

- **Happy paths** (login, refresh, me, signup completo) — el contrato principal.
- **Cross-use de tokens** (access como refresh → 401) — verifica el diseño de secrets/kind.
- **Reuso de invitation consumida → 409** — verifica la transición de estado.
- **Sin auth → 401, body inválido → 400** — verifica que los filtros y policies están realmente conectados (un `.RequireAuthorization()` olvidado no falla en compilación).

**Why integration y no unit tests para auth** — el riesgo de auth está en el WIRING
(middleware, policies, claims, config), no en la lógica aislada. **Because** un unit test
de `AuthService` con mocks jamás habría encontrado el bug real de esta fase — el snapshot
eager de configuración — porque el bug vivía en `Program.cs`, no en el servicio.

---

## Mapa de archivos de la fase

```
HouseCenter.Api/
├── Common/
│   ├── Results/        Error.cs · Result.cs · ResultExtensions.cs
│   ├── Middleware/     GlobalExceptionHandler.cs
│   ├── Validation/     ValidationFilter.cs
│   └── Email/          IEmailService.cs · SmtpEmailService.cs · EmailTemplates.cs
├── Features/
│   ├── Auth/           TokenService.cs · AuthService.cs · AuthEndpoints.cs
│   │                   AuthDtos.cs · AuthValidators.cs
│   └── Invitations/    InvitationService.cs · InvitationEndpoints.cs
│                       InvitationDtos.cs · InvitationValidators.cs
├── Domain/Constants/   PolicyNames.cs (+ RoleNames.cs de Phase 1)
└── Program.cs          authn/authz wiring + lazy JwtBearerOptions

HouseCenter.Api.Tests/
├── ApiFactory.cs       WebApplicationFactory + housecenter_test DB
├── LoginFlowTests.cs   7 tests
└── SignupFlowTests.cs  3 tests (incluye flujo completo de 7 pasos)
```

## Conceptos para repasar en frío (self-quiz)

1. ¿Por qué `Result<T>` y no excepciones para "email already taken"?
2. ¿Qué dos mecanismos impiden usar un invitation token como access token?
3. ¿Por qué el middleware JwtBearer se configura vía `AddOptions<JwtBearerOptions>().Configure<IOptions<JwtOptions>>` y no leyendo `builder.Configuration` directo?
4. ¿Cómo se revocan refresh tokens sin guardar estado? ¿Qué claim participa?
5. ¿Por qué `POST /password/request` devuelve 202 aunque el email no exista?
6. ¿Qué hace `[MemberNotNullWhen(true, nameof(Error))]` y qué warning evita?
7. ¿Por qué los integration tests corren contra Postgres real y no EF InMemory?
8. ¿Para qué existe `public partial class Program;` al final de `Program.cs`?
