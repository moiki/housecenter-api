using HouseCenter.Api.Domain.Constants;
using HouseCenter.Api.Domain.Entities;
using HouseCenter.Api.Options;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Options;

namespace HouseCenter.Api.Data;

public class DatabaseSeeder(
    IServiceProvider serviceProvider,
    IOptions<SeedOptions> seedOptions,
    IOptions<DatabaseOptions> databaseOptions,
    ILogger<DatabaseSeeder> logger) : IHostedService
{
    public async Task StartAsync(CancellationToken cancellationToken)
    {
        // DbContext is Scoped; a hosted service is Singleton — create a scope manually
        using var scope = serviceProvider.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<HouseCenterDbContext>();

        if (databaseOptions.Value.RecreateOnStartup)
        {
            logger.LogWarning("RecreateOnStartup is ON — dropping database (tests only!)");
            await db.Database.EnsureDeletedAsync(cancellationToken);
            await db.Database.MigrateAsync(cancellationToken);
        }
        else if (databaseOptions.Value.ApplyMigrationsAtStartup)
        {
            await db.Database.MigrateAsync(cancellationToken);
        }

        await SeedRolesAsync(db, cancellationToken);
        await SeedOwnerUserAsync(db, cancellationToken);
        await SeedHelpTopicsAsync(db, cancellationToken);
    }

    public Task StopAsync(CancellationToken cancellationToken) => Task.CompletedTask;

    private async Task SeedRolesAsync(HouseCenterDbContext db, CancellationToken ct)
    {
        var existing = await db.Roles
            .Select(r => r.Name)
            .ToListAsync(ct);

        var missing = RoleNames.All.Except(existing).ToList();
        if (missing.Count == 0) return;

        db.Roles.AddRange(missing.Select(name => new Role { Name = name }));
        await db.SaveChangesAsync(ct);

        logger.LogInformation("Seeded roles: {Roles}", string.Join(", ", missing));
    }

    private async Task SeedOwnerUserAsync(HouseCenterDbContext db, CancellationToken ct)
    {
        var seed = seedOptions.Value;

        if (string.IsNullOrWhiteSpace(seed.OwnerEmail) || string.IsNullOrWhiteSpace(seed.OwnerPassword))
        {
            logger.LogWarning("Seed:OwnerEmail / Seed:OwnerPassword not configured — skipping owner seed");
            return;
        }

        var exists = await db.Users.AnyAsync(u => u.Email == seed.OwnerEmail, ct);
        if (exists) return;

        var ownerRole = await db.Roles.SingleAsync(r => r.Name == RoleNames.Owner, ct);

        db.Users.Add(new User
        {
            FirstName = seed.OwnerFirstName,
            LastName = seed.OwnerLastName,
            Email = seed.OwnerEmail,
            HashedPassword = BCrypt.Net.BCrypt.HashPassword(seed.OwnerPassword),
            Address = "N/A",
            EmailConfirmed = true,
            Roles = [ownerRole]
        });
        await db.SaveChangesAsync(ct);

        logger.LogInformation("Seeded owner user {Email}", seed.OwnerEmail);
    }

    // help-center R7 — production bootstrap content (guide text every environment needs),
    // NOT the POST /seed fake test dataset (see Features/Seeding/TestDataSeeder.cs).
    // Insert-if-missing only: never updates an existing row, so admin edits made via the
    // API (PUT /help/topics/{id}) survive every reseed. IgnoreQueryFilters() matches the
    // CreateHelpTopic duplicate-key check — a soft-deleted topic with the same TopicKey
    // must not be re-inserted either.
    private async Task SeedHelpTopicsAsync(HouseCenterDbContext db, CancellationToken ct)
    {
        var existingKeys = await db.HelpTopics
            .IgnoreQueryFilters()
            .Select(t => t.TopicKey)
            .ToListAsync(ct);

        var missing = GetStarterHelpTopics()
            .Where(t => !existingKeys.Contains(t.TopicKey))
            .ToList();
        if (missing.Count == 0) return;

        db.HelpTopics.AddRange(missing);
        await db.SaveChangesAsync(ct);

        logger.LogInformation("Seeded help topics: {Topics}",
            string.Join(", ", missing.Select(t => t.TopicKey)));
    }

    // A fresh list of entity instances per call — avoids sharing mutable object state
    // (and Id/Guid identity) across seeder runs.
    private static List<HelpTopic> GetStarterHelpTopics() =>
    [
        new HelpTopic
        {
            TopicKey = "sessions.recording",
            Title = "Cómo registrar una sesión de atención",
            Summary = "Registra una sesión con el paciente, el colaborador, el tipo de " +
                      "atención y la fecha. Debes indicar una clínica o una ruta de " +
                      "trabajo (o ambas). La sesión siempre comienza con estado \"Programada\".",
            Body = """
                   ## Registrar una sesión de atención

                   Una sesión (`AttentionSession`) es el registro real de que un colaborador
                   atendió a un paciente: quién, cuándo y dónde.

                   **Quién puede crearla:** un colaborador con rol Miembro (Member) o un
                   Administrador.

                   **Campos obligatorios:**
                   - `PatientId` — el paciente atendido (va en la URL).
                   - `CollaboratorId` — el colaborador que realizó la atención (debe tener
                     rol Miembro).
                   - `AttentionType` — tipo de atención: médica o refuerzo educativo.
                   - `SessionDate` — fecha de la sesión.

                   **Ubicación de la sesión:** debes indicar al menos una clínica
                   (`ClinicId`) o una ruta de trabajo (`WorkRouteId`). Ver la guía "Clínica
                   o ruta: ¿cuál elijo?" para más detalle.

                   **Estado inicial:** toda sesión nueva se crea con estado "Programada"
                   (`Scheduled`). `DurationMinutes` y `Notes` se completan más adelante, al
                   actualizar el estado de la sesión.
                   """,
            Audience = HelpAudience.Staff,
            Category = "Sesiones de atención",
            DisplayOrder = 1
        },
        new HelpTopic
        {
            TopicKey = "sessions.clinic-or-route",
            Title = "Clínica o ruta: ¿cuál elijo al crear una sesión?",
            Summary = "Toda sesión necesita un lugar: una clínica, una ruta de trabajo, o " +
                      "ambas. Lo único que no está permitido es dejar los dos campos vacíos.",
            Body = """
                   ## Clínica o ruta de trabajo

                   Al crear una sesión (`POST /patients/{id}/sessions`) el sistema necesita
                   saber dónde ocurrió la atención.

                   **La regla es "al menos uno", no "exactamente uno":**
                   - Solo `ClinicId` — la atención fue en una clínica fija.
                   - Solo `WorkRouteId` — la atención fue durante una ruta de trabajo
                     (visita domiciliaria).
                   - Ambos a la vez — por ejemplo, si la ruta de trabajo incluye una parada
                     que es también una clínica registrada.
                   - **Nunca los dos vacíos** — si no se indica ninguno, la API rechaza la
                     sesión con un error de validación (400) antes de guardarla.

                   Si tienes dudas sobre cuál usar, piensa en la pregunta: "¿el paciente se
                   movió a una instalación fija, o el colaborador se movió hacia el
                   paciente?"
                   """,
            Audience = HelpAudience.Staff,
            Category = "Sesiones de atención",
            DisplayOrder = 2
        },
        new HelpTopic
        {
            TopicKey = "sessions.status-lifecycle",
            Title = "Ciclo de estado de una sesión",
            Summary = "Una sesión nace \"Programada\" y solo puede pasar una vez a " +
                      "\"Completada\" o \"No asistió\". El cambio es definitivo: no puede " +
                      "revertirse ni repetirse.",
            Body = """
                   ## Estados de una sesión (`SessionStatus`)

                   Una sesión de atención tiene tres estados posibles: `Scheduled`
                   (Programada), `Completed` (Completada) y `Missed` (No asistió).

                   **El flujo es de un solo sentido:**
                   1. Toda sesión nace en `Scheduled`.
                   2. Desde `Scheduled` solo hay dos caminos: pasar a `Completed` o pasar a
                      `Missed`.
                   3. Una vez que la sesión sale de `Scheduled`, el cambio es definitivo —
                      no puede volver a `Scheduled` ni cambiar entre `Completed` y `Missed`.
                      Intentarlo devuelve un error de conflicto (409).

                   **Quién puede cambiar el estado:** solo el colaborador dueño de la
                   sesión, o un Administrador.

                   **Campos asociados al cambio de estado:**
                   - `DurationMinutes` solo tiene sentido (y solo se guarda) cuando el nuevo
                     estado es `Completed`.
                   - `Notes` se puede actualizar en cualquiera de las dos transiciones.

                   Marcar una sesión como `Missed` dispara un aviso interno hacia el
                   colaborador — no requiere ninguna acción adicional de tu parte.
                   """,
            Audience = HelpAudience.Staff,
            Category = "Sesiones de atención",
            DisplayOrder = 3
        },
        new HelpTopic
        {
            TopicKey = "patients.assign-doctor",
            Title = "Cómo asignar un médico a un paciente",
            Summary = "Solo el Owner puede asignar o quitar médicos remotos de un " +
                      "paciente. El usuario debe tener el rol Doctor; de lo contrario la " +
                      "asignación se rechaza.",
            Body = """
                   ## Asignar un médico remoto a un paciente

                   Los médicos (`Doctor`) no visitan al paciente en persona: reciben
                   reportes de progreso y dan guía médica a distancia a través de las
                   consultas médicas del paciente. Para que un médico tenga visibilidad
                   sobre un paciente, primero debe quedar asignado a él.

                   **Quién puede asignar/quitar:** únicamente el Owner, mediante:
                   - `POST /patients/{id}/doctors/{userId}` — asignar
                   - `DELETE /patients/{id}/doctors/{userId}` — quitar

                   **Requisito del usuario:** el `userId` indicado debe tener el rol Doctor.
                   Si el usuario no tiene ese rol, la API responde con un error de
                   validación (400) y no se crea la asignación.

                   **Casos ya cubiertos por la API:**
                   - Asignar un médico que ya está asignado a ese paciente devuelve un
                     conflicto (409); no se duplica la asignación.
                   - Quitar un médico que no está asignado devuelve "no encontrado" (404).

                   Ambas operaciones, al tener éxito, devuelven 204 sin contenido.
                   """,
            Audience = HelpAudience.Staff,
            Category = "Pacientes",
            DisplayOrder = 1
        },
        new HelpTopic
        {
            TopicKey = "treatments.status-lifecycle",
            Title = "Estados de un tratamiento",
            Summary = "Un tratamiento tiene tres estados: Activo, Completado y Pausado. A " +
                      "diferencia de las sesiones, el estado se puede cambiar libremente " +
                      "entre los tres en cualquier momento.",
            Body = """
                   ## Estados de un tratamiento (`TreatmentStatus`)

                   Un tratamiento (`Treatment`) puede estar en uno de tres estados: `Active`
                   (Activo), `Completed` (Completado) o `Paused` (Pausado).

                   **A diferencia de las sesiones de atención, aquí no hay un flujo de un
                   solo sentido:** el estado se puede cambiar libremente entre cualquiera de
                   los tres valores, en cualquier orden, mediante
                   `PATCH /patients/{patientId}/treatments/{treatmentId}/status`. No existe
                   una restricción de "transición válida" para tratamientos.

                   **Cuándo usar cada estado:**
                   - `Active` — el tratamiento está en curso; es el estado por defecto al
                     crearlo.
                   - `Paused` — el tratamiento se interrumpió temporalmente (por ejemplo, el
                     paciente viajó o hubo una pausa médica), pero se espera retomarlo.
                   - `Completed` — se cumplieron los objetivos del tratamiento.

                   **Por qué mantenerlo actualizado:** los reportes agregados
                   (`/reports/summary`, `/reports/clinics/{id}`) cuentan únicamente los
                   tratamientos en estado `Active` para mostrar la carga de trabajo real de
                   cada clínica o del sistema en general.
                   """,
            Audience = HelpAudience.Staff,
            Category = "Tratamientos",
            DisplayOrder = 1
        }
    ];
}
