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
            Summary = "Desde la pestaña Sessions del perfil del paciente, completá el " +
                      "formulario New Session: colaborador, tipo de atención, fecha y una " +
                      "clínica o ruta de trabajo. La sesión se crea con estado Scheduled.",
            Body = """
                   ## Registrar una sesión de atención

                   En el perfil de un paciente, abrí la pestaña **Sessions** y hacé clic en
                   el botón **New Session** (arriba a la derecha). Se abre un panel lateral
                   con el formulario.

                   **Campos del formulario, en orden:**
                   - **Collaborator** — el colaborador que realizó la atención.
                   - **Attention Type** — `Medical` o `Educational Reinforcement`.
                   - **Session Date** — fecha y hora de la sesión.
                   - **Location Type** — elegí `Clinic` o `Work Route`; según lo que
                     elijas aparece un desplegable adicional para seleccionar cuál. Ver la
                     guía "Clínica o ruta: ¿cuál elijo?" si tenés dudas.
                   - **Duration (minutes)** y **Notes** — opcionales; se completan más
                     adelante, al actualizar el estado de la sesión.

                   Al guardar con **Create Session**, la sesión aparece en la lista con el
                   estado **Scheduled**.
                   """,
            Audience = HelpAudience.Staff,
            Category = "Sesiones de atención",
            DisplayOrder = 1
        },
        new HelpTopic
        {
            TopicKey = "sessions.clinic-or-route",
            Title = "Clínica o ruta: ¿cuál elijo al crear una sesión?",
            Summary = "En el formulario New Session, el campo Location Type te deja " +
                      "elegir entre Clinic y Work Route; según cuál elijas, completá el " +
                      "desplegable que aparece debajo.",
            Body = """
                   ## Clínica o ruta de trabajo

                   En el formulario **New Session**, el campo **Location Type** te deja
                   elegir entre **Clinic** y **Work Route**. Al elegir una opción aparece un
                   segundo desplegable para indicar cuál clínica o cuál ruta fue.

                   **Cómo decidir:**
                   - **Clinic** — la atención ocurrió en una sede fija.
                   - **Work Route** — la atención ocurrió durante una visita domiciliaria,
                     como parte de una ruta de trabajo.

                   Si al guardar ves el mensaje "Clinic is required" o "Work route is
                   required", es porque elegiste el tipo de ubicación pero todavía no
                   seleccionaste cuál en el segundo desplegable.
                   """,
            Audience = HelpAudience.Staff,
            Category = "Sesiones de atención",
            DisplayOrder = 2
        },
        new HelpTopic
        {
            TopicKey = "sessions.status-lifecycle",
            Title = "Ciclo de estado de una sesión",
            Summary = "Una sesión se crea con estado Scheduled. Para pasarla a Completed " +
                      "o Missed, expandí la sesión en la lista y usá el desplegable " +
                      "Status — el cambio es definitivo.",
            Body = """
                   ## Cambiar el estado de una sesión

                   En la pestaña **Sessions**, hacé clic sobre una sesión de la lista para
                   expandirla (la flecha gira hacia abajo). Se despliega un formulario con:

                   - **Status** — desplegable con tres opciones: `Scheduled`, `Completed`,
                     `Missed`.
                   - **Duration (min)** — solo tiene sentido completarlo cuando pasás la
                     sesión a `Completed`.
                   - **Notes** — se puede escribir en cualquier momento.

                   Hacé clic en **Save** para confirmar el cambio.

                   **El cambio es de un solo sentido:**
                   1. Toda sesión nace en `Scheduled`.
                   2. Desde ahí solo podés pasar a `Completed` o a `Missed` — una sola vez.
                   3. Una vez que sale de `Scheduled`, no hay forma de volver a esa vista ni
                      de pasar de `Completed` a `Missed` (o viceversa) desde la pantalla.
                      Revisá bien antes de guardar.

                   Si marcás una sesión como `Missed`, el colaborador asignado recibe un
                   aviso en la campana de notificaciones — no tenés que hacer nada más.
                   """,
            Audience = HelpAudience.Staff,
            Category = "Sesiones de atención",
            DisplayOrder = 3
        },
        new HelpTopic
        {
            TopicKey = "patients.assign-doctor",
            Title = "Cómo asignar un médico a un paciente",
            Summary = "Desde la pestaña Overview del perfil del paciente, en la sección " +
                      "Assigned Doctors, un Owner puede elegir un médico del desplegable " +
                      "y hacer clic en Assign.",
            Body = """
                   ## Asignar un médico remoto a un paciente

                   Los médicos (rol **Doctor**) no visitan al paciente en persona: siguen su
                   evolución y dan guía médica a distancia a través de las **Consultas** del
                   paciente. Para que un médico pueda ver a un paciente, primero hay que
                   asignarlo.

                   **Dónde hacerlo:** abrí el perfil del paciente, pestaña **Overview**,
                   sección **Assigned Doctors**.

                   **Quién puede hacerlo:** solo un usuario con rol **Owner** ve el
                   desplegable y el botón para asignar o quitar médicos. El resto de los
                   roles puede ver la lista de médicos ya asignados, pero no modificarla.

                   **Cómo asignar:** elegí un médico en el desplegable (solo lista usuarios
                   con rol Doctor que todavía no están asignados a este paciente) y hacé
                   clic en **Assign**. El médico aparece como una etiqueta junto a los
                   demás.

                   **Cómo quitar:** hacé clic en la ✕ de la etiqueta del médico que querés
                   quitar.

                   Si el desplegable muestra "No doctors available", es porque no hay ningún
                   usuario con rol Doctor disponible para asignar (ya sea porque no existen
                   o porque ya están todos asignados a este paciente).
                   """,
            Audience = HelpAudience.Staff,
            Category = "Pacientes",
            DisplayOrder = 1
        },
        new HelpTopic
        {
            TopicKey = "patients.comments",
            Title = "Cómo dejar una nota sobre un paciente (pestaña Comments)",
            Summary = "Desde la pestaña Comments del perfil del paciente, cualquier " +
                      "usuario puede escribir una observación con texto enriquecido y un " +
                      "tipo (Simple, Medical o Route).",
            Body = """
                   ## Comentarios del paciente

                   La pestaña **Comments** del perfil del paciente es un registro
                   cronológico de observaciones sobre el paciente (no sobre un tratamiento
                   en particular).

                   **Para agregar uno:** hacé clic en **Add comment**, elegí el **Type**
                   (`Simple`, `Medical` o `Route`) y escribí en el editor de texto
                   enriquecido. Guardá con **Post comment**.

                   Cada comentario se muestra con una etiqueta de tipo, una etiqueta de
                   estado (`Pending`, `Accepted` o `Rejected` — este estado no lo elegís
                   vos, lo define un proceso de revisión aparte) y la fecha. La tarjeta no
                   muestra quién escribió el comentario.

                   **Eliminar:** cualquier usuario que pueda ver el perfil puede borrar
                   cualquier comentario con el ícono de basura (pide confirmación). No hay
                   forma de editar un comentario ya publicado — si hay un error, hay que
                   borrarlo y escribir uno nuevo.
                   """,
            Audience = HelpAudience.All,
            Category = "Pacientes",
            DisplayOrder = 2
        },
        new HelpTopic
        {
            TopicKey = "patients.attachments",
            Title = "Cómo subir y descargar archivos de un paciente (pestaña Attachments)",
            Summary = "Desde la pestaña Attachments, el botón Upload file permite subir " +
                      "una imagen (JPEG, PNG, WEBP) o un PDF. Cada archivo se puede " +
                      "descargar o eliminar desde la lista.",
            Body = """
                   ## Adjuntos del paciente

                   La pestaña **Attachments** del perfil del paciente guarda archivos
                   relacionados con el paciente (estudios, fotos, documentos).

                   **Subir un archivo:** hacé clic en **Upload file** (arriba a la derecha),
                   que abre el selector de archivos del sistema. Solo se puede subir un
                   archivo a la vez, y solo se aceptan imágenes JPEG, PNG o WEBP, y
                   documentos PDF — cualquier otro tipo se rechaza. Mientras se sube,
                   aparece una barra de progreso con el porcentaje.

                   **Ver y gestionar lo ya subido:** cada archivo se lista con una
                   miniatura (o un ícono genérico si es un PDF), el nombre, el tamaño,
                   quién lo subió y cuándo. Tiene dos acciones: **descargar** (ícono de
                   flecha hacia abajo) y **eliminar** (ícono de basura, pide confirmación).
                   No hay una vista previa ampliada — para ver el archivo completo hay que
                   descargarlo.
                   """,
            Audience = HelpAudience.All,
            Category = "Pacientes",
            DisplayOrder = 3
        },
        new HelpTopic
        {
            TopicKey = "treatments.status-lifecycle",
            Title = "Estados de un tratamiento",
            Summary = "Un tratamiento tiene tres estados — Active, Completed y Paused — " +
                      "que se cambian con un clic desde la pestaña Treatments, sin " +
                      "restricciones de orden.",
            Body = """
                   ## Cambiar el estado de un tratamiento

                   En la pestaña **Treatments** del perfil del paciente, hacé clic en un
                   tratamiento para expandirlo. Ahí vas a ver una fila de tres chips bajo
                   **Status**: `Active`, `Completed`, `Paused`. Hacé clic en cualquiera de
                   los tres para cambiar el estado — a diferencia de las sesiones, acá no
                   hay un orden obligatorio: podés moverte libremente entre los tres estados
                   en cualquier momento.

                   **Cuándo usar cada uno:**
                   - **Active** — el tratamiento está en curso; es el estado con el que se
                     crea.
                   - **Paused** — se interrumpió temporalmente (el paciente viajó, hubo una
                     pausa médica), pero se espera retomarlo.
                   - **Completed** — se cumplieron los objetivos del tratamiento.

                   **Por qué mantenerlo actualizado:** la pantalla de **Reports** solo
                   cuenta los tratamientos en estado `Active` para mostrar la carga de
                   trabajo real de la organización — un tratamiento que quedó como `Active`
                   por error infla esos números.
                   """,
            Audience = HelpAudience.Staff,
            Category = "Tratamientos",
            DisplayOrder = 1
        },
        new HelpTopic
        {
            TopicKey = "treatments.details-and-comments",
            Title = "Agregar detalles y comentarios a un tratamiento",
            Summary = "Cada tratamiento tiene dos secciones internas — Details y " +
                      "Comments — donde el equipo registra avances y observaciones con " +
                      "texto enriquecido.",
            Body = """
                   ## Detalles y comentarios de un tratamiento

                   Al expandir un tratamiento en la pestaña **Treatments**, además del
                   control de estado vas a encontrar dos secciones:

                   **Details** — hitos o avances puntuales del tratamiento. Hacé clic en
                   **Add detail** para abrir el formulario: **Name**, **Date** y una
                   **Description** con editor de texto enriquecido (negrita, listas, etc.).
                   Guardá con **Save detail**. Cada detalle tiene un ícono de basura para
                   eliminarlo (se pide confirmación).

                   **Comments** — observaciones de seguimiento, con un tipo asociado. Hacé
                   clic en **Add comment**, elegí el **Type** (`Simple`, `Medical` o
                   `Route`) y escribí el comentario en el editor. Guardá con **Post
                   comment**. También se puede eliminar con confirmación.

                   Ni los detalles ni los comentarios se pueden editar una vez guardados —
                   solo eliminar y volver a crear si hace falta corregir algo.
                   """,
            Audience = HelpAudience.Staff,
            Category = "Tratamientos",
            DisplayOrder = 2
        },
        new HelpTopic
        {
            TopicKey = "clinics.manage",
            Title = "Cómo crear y editar una clínica",
            Summary = "Desde Clinics, el botón New Clinic abre un formulario con Name y " +
                      "Address. Para editar, hacé clic en el ícono de lápiz de la fila; " +
                      "para desactivar, en el ícono de basura.",
            Body = """
                   ## Crear y editar clínicas

                   En la pantalla **Clinics** vas a ver la lista de todas las clínicas
                   registradas, con columnas **Name** y **Address**.

                   **Crear una clínica:** hacé clic en **New Clinic** (arriba a la
                   derecha). Se abre un panel lateral con dos campos: **Name** y
                   **Address** (ambos obligatorios). Guardá con **Create clinic**.

                   **Editar una clínica:** hacé clic en el ícono de lápiz de la fila. Se
                   abre la página de detalle con el mismo formulario ya completado; guardá
                   los cambios con **Save changes**. Al guardar, la página se queda donde
                   estás y muestra un mensaje verde de confirmación — no te devuelve a la
                   lista automáticamente.

                   **Desactivar una clínica:** hacé clic en el ícono de basura (rojo) de la
                   fila. Se pide confirmación antes de desactivarla. Una vez desactivada, la
                   clínica desaparece de la lista — hoy no hay ninguna pantalla para
                   reactivarla ni para ver clínicas desactivadas, así que confirmá bien
                   antes de desactivar.
                   """,
            Audience = HelpAudience.All,
            Category = "Clínicas",
            DisplayOrder = 1
        },
        new HelpTopic
        {
            TopicKey = "workroutes.manage",
            Title = "Cómo crear y editar una ruta de trabajo",
            Summary = "Desde Work Routes, New Route abre un formulario con nombre, " +
                      "descripción, clínica y una lista de destinos (paradas). La clínica " +
                      "no se puede cambiar después de crear la ruta.",
            Body = """
                   ## Crear y editar rutas de trabajo

                   La pantalla **Work Routes** lista las rutas existentes con su clínica
                   asociada y la cantidad de destinos.

                   **Crear una ruta:** hacé clic en **New Route**. El formulario pide, en
                   orden:
                   - **Route name** y **Description**.
                   - **Clinic** — la clínica a la que pertenece esta ruta. **Esta clínica
                     no se puede cambiar más adelante**, así que confirmá que sea la
                     correcta antes de guardar.
                   - **Featured image URL** (opcional).
                   - **Destinations** — al menos una parada, cada una con **Name**,
                     **Description** y un link opcional de Google Maps. El botón **Add
                     destination** agrega otra parada.

                   Guardá con **Create work route**.

                   **Editar una ruta:** en la página de detalle podés modificar el nombre,
                   la descripción, la imagen y las paradas (ahí se llaman "Stop #N"). El
                   campo **Clinic** no aparece en esta pantalla — como se explicó arriba,
                   no es editable. Guardá con **Save changes**; vas a ver un "Saved!" junto
                   al botón para confirmar que se guardó, sin salir de la pantalla.

                   **Desactivar una ruta:** solo se puede desde la lista (ícono de basura),
                   no desde el detalle. Al igual que con las clínicas, no hay forma de
                   reactivarla ni de verla de nuevo una vez desactivada.
                   """,
            Audience = HelpAudience.All,
            Category = "Rutas de trabajo",
            DisplayOrder = 1
        },
        new HelpTopic
        {
            TopicKey = "consultations.overview",
            Title = "Cómo usar el módulo de Consultas",
            Summary = "Consultations es un chat entre el equipo y el médico asignado " +
                      "sobre un caso. Se crea con New Consultation y el estado se cambia " +
                      "desde un desplegable en la parte superior del hilo.",
            Body = """
                   ## Consultas médicas

                   **Consultations** es el canal donde el equipo (Miembros, Administradores,
                   Owners) consulta a un **Doctor** sobre un paciente, a distancia. No está
                   disponible para el rol Sponsor.

                   **Iniciar una consulta:** hacé clic en **New Consultation**. El
                   formulario pide **Patient**, **Assigned Doctor** (solo aparecen usuarios
                   con rol Doctor), un **Title** y un **First Message** con texto
                   enriquecido — este primer mensaje va a ser el primer mensaje del chat.
                   Confirmá con **Open Consultation**.

                   **Responder:** al abrir una consulta de la lista, vas a ver el hilo como
                   un chat: tus mensajes aparecen a la derecha, los de los demás a la
                   izquierda. Escribí tu respuesta en el cuadro de abajo y enviala con el
                   botón de avión de papel.

                   **Cambiar el estado:** junto al título hay un desplegable con tres
                   opciones:
                   - **Open** — recién abierta.
                   - **Under Review** — alguien la está analizando (es solo informativo, no
                     cambia nada en la pantalla).
                   - **Resolved** — se considera cerrada. **Al pasar a Resolved, el cuadro
                     para escribir desaparece** y se reemplaza por un aviso; para seguir
                     escribiendo hay que volver a cambiar el estado a Open o Under Review.

                   No hay forma de adjuntar archivos a una consulta ni de ver el perfil del
                   paciente desde el hilo — el título es la única referencia, así que
                   elegilo para que quede claro de qué caso se trata. Un Doctor se entera de
                   una consulta nueva o de una respuesta a través de la campana de
                   notificaciones, que lleva directo al hilo.
                   """,
            Audience = HelpAudience.Staff,
            Category = "Consultas",
            DisplayOrder = 1
        },
        new HelpTopic
        {
            TopicKey = "reports.overview",
            Title = "Cómo leer la pantalla de Reports",
            Summary = "Reports muestra un gráfico de sesiones por semana (Medical vs " +
                      "Educational) y, para Administradores y Owners, un ranking de " +
                      "sesiones por colaborador.",
            Body = """
                   ## Reportes de sesiones

                   La pantalla **Reports** tiene dos bloques:

                   **Sessions over time** — un gráfico de barras, una por semana, dividida
                   en dos colores: sesiones `Medical` y `Educational`. Pasá el mouse sobre
                   un segmento para ver el número exacto.

                   **Sessions by collaborator** — un ranking de qué colaborador registró más
                   sesiones en el período. **Esta sección solo la ven los roles
                   Administrator y Owner** — el resto de los roles ve únicamente el gráfico
                   de arriba, sin ningún aviso de que falta esa sección.

                   **Elegir el período:** cuatro botones arriba del gráfico — **Last 4
                   weeks**, **Last 8 weeks** (el que viene seleccionado por defecto),
                   **Last 3 months**, **Last 6 months**. No hay selector de fecha manual ni
                   filtro por clínica.

                   **Ojo con esto:** el gráfico suma todas las sesiones del período sin
                   importar si están `Scheduled`, `Completed` o `Missed` — una barra alta no
                   significa necesariamente que esa cantidad de atenciones se haya
                   completado. Los datos se guardan en caché unos minutos, así que si
                   acabás de cargar una sesión puede tardar un poco en reflejarse acá.
                   """,
            Audience = HelpAudience.All,
            Category = "Reportes",
            DisplayOrder = 1
        },
        new HelpTopic
        {
            TopicKey = "collaborators.manage",
            Title = "Cómo administrar Collaborators",
            Summary = "Solo Owner y Administrator ven esta pantalla. New Collaborator " +
                      "abre un formulario con datos personales, clínica, ruta de trabajo " +
                      "(opcional) y una lista de puestos (Positions).",
            Body = """
                   ## Administrar colaboradores

                   **Collaborators** es el directorio de personal (enfermeros, médicos de
                   campo, técnicos) — distinto de **Users** (las cuentas con inicio de
                   sesión). Solo visible para **Owner** y **Administrator**.

                   **Crear un colaborador:** hacé clic en **New Collaborator**. El
                   formulario pide, en orden: **First name**, **Last name**, **Email**,
                   **Phone**, **Address**, **Country / State / City**, **Clinic**, **Work
                   route** (opcional) y **Positions** — una lista de puestos en texto libre
                   (podés agregar varios con **Add position**; necesitás al menos uno).
                   Guardá con **Create collaborator**.

                   **Editar:** ícono de lápiz en la fila, mismo formulario, guardá con
                   **Save changes**.

                   **Desactivar:** ícono de basura (rojo), pide confirmación. Como en
                   Clínicas y Rutas, no hay forma de reactivar un colaborador ni de verlo
                   una vez desactivado — desaparece de la lista.

                   **Tip:** como **Positions** es texto libre (no una lista fija), ponete
                   de acuerdo con el equipo sobre cómo nombrar los puestos (por ejemplo,
                   "Nurse" vs "RN") para que no queden inconsistencias.
                   """,
            Audience = HelpAudience.AdministratorOrAbove,
            Category = "Colaboradores",
            DisplayOrder = 1
        },
        new HelpTopic
        {
            TopicKey = "users.manage",
            Title = "Qué podés hacer en la pantalla de Users",
            Summary = "Users muestra todas las cuentas del sistema con sus roles y " +
                      "estado. No se crean ni editan cuentas desde acá — solo se pueden " +
                      "desactivar.",
            Body = """
                   ## La pantalla de Users

                   **Users** (solo para Owner y Administrator) muestra todas las cuentas
                   registradas: nombre, roles (con una etiqueta de color por rol) y estado
                   (`Active` o `Inactive`).

                   **No hay botón para crear un usuario acá.** Las cuentas nuevas se generan
                   cuando alguien acepta una invitación enviada desde **Invitations** (ver
                   esa guía). Tampoco hay forma de editar el nombre, el email o los roles de
                   un usuario ya creado desde esta pantalla.

                   **La única acción disponible es Deactivate** (ícono de basura, solo en
                   usuarios activos): pide confirmación y le quita el acceso al sistema a
                   esa persona. No hay botón para reactivarlo después.

                   **Si asignaste el rol equivocado al invitar a alguien:** hoy no hay forma
                   de cambiar el rol de un usuario ya creado desde la interfaz. La única
                   opción es desactivar esa cuenta y enviar una invitación nueva con el rol
                   correcto.
                   """,
            Audience = HelpAudience.AdministratorOrAbove,
            Category = "Usuarios",
            DisplayOrder = 1
        },
        new HelpTopic
        {
            TopicKey = "invitations.manage",
            Title = "Cómo invitar a un nuevo usuario",
            Summary = "Desde Invitations, Invite user pide un email y un rol. La persona " +
                      "invitada recibe un link para crear su cuenta; al aceptar, aparece " +
                      "en Users con el rol elegido.",
            Body = """
                   ## Invitar a un nuevo usuario

                   **Invitations** (solo para Owner y Administrator) es donde se generan
                   las invitaciones para que alguien se una a HouseCenter con una cuenta
                   propia.

                   **Enviar una invitación:** hacé clic en **Invite user**. El formulario
                   pide **Email** y **Role** (el rol que va a tener la cuenta una vez
                   creada — no se puede cambiar después desde la interfaz). Confirmá con
                   **Send invitation**.

                   **Qué ve la persona invitada:** recibe un link para crear su cuenta, con
                   un formulario que pide nombre, apellido y contraseña. Al completarlo, la
                   cuenta se crea con el rol que elegiste al invitar, y la persona aparece
                   automáticamente en **Users** como `Active`.

                   **Estados de una invitación:** `Pending` (todavía no la aceptaron —
                   podés usar el ícono de avión de papel para **reenviarla**), `Accepted`
                   (ya se convirtió en una cuenta) y `Expired` (venció el link; ya no se
                   puede reenviar, hay que **eliminarla** y enviar una invitación nueva).

                   **Eliminar** una invitación (ícono de basura) es permanente y no se
                   puede deshacer.
                   """,
            Audience = HelpAudience.AdministratorOrAbove,
            Category = "Invitaciones",
            DisplayOrder = 1
        },
        new HelpTopic
        {
            TopicKey = "settings.overview",
            Title = "Qué podés cambiar en Settings",
            Summary = "En Settings solo se puede ver tu información de perfil (de solo " +
                      "lectura) y activar el modo oscuro. Para corregir tus datos, tenés " +
                      "que pedírselo a un administrador.",
            Body = """
                   ## La pantalla de Settings

                   Se accede desde el menú de tu usuario (arriba a la derecha) →
                   **Settings**. La ven todos los roles por igual.

                   **Profile** — muestra tu nombre, tus roles, email, teléfono, dirección y
                   profesión. **Es de solo lectura**: si necesitás corregir algún dato,
                   tenés que pedírselo a un administrador; no hay ningún campo editable en
                   esta sección.

                   **Appearance** — un único control, el interruptor **Dark mode**, para
                   cambiar entre tema claro y oscuro. Se aplica al instante y se recuerda la
                   próxima vez que inicies sesión. El mismo interruptor también está
                   disponible como un ícono de sol/luna en la barra superior, para
                   cambiarlo sin entrar a Settings.
                   """,
            Audience = HelpAudience.All,
            Category = "Configuración",
            DisplayOrder = 1
        },
        new HelpTopic
        {
            TopicKey = "notifications.overview",
            Title = "Cómo usar la campana de notificaciones",
            Summary = "La campana (arriba a la derecha) avisa sobre mensajes de " +
                      "consultas, sesiones próximas o perdidas, y consultas abiertas o " +
                      "resueltas.",
            Body = """
                   ## La campana de notificaciones

                   El ícono de campana, en la barra superior, está visible en todas las
                   pantallas. Un punto rojo aparece cuando tenés notificaciones sin leer
                   (no muestra un número, solo indica que hay algo nuevo).

                   **Qué tipos de aviso podés recibir:**
                   - Un nuevo mensaje en una consulta en la que estás involucrado.
                   - Que se abrió una consulta nueva asignada a vos.
                   - Que una consulta en la que participabas se marcó como resuelta.
                   - Que se acerca una sesión de atención programada.
                   - Que una sesión programada se marcó como `Missed` (no se realizó).

                   **Al hacer clic en una notificación:** se marca como leída
                   automáticamente. Si es sobre una consulta, además te lleva directo a esa
                   consulta. Las notificaciones de sesiones no llevan a ningún lado (las
                   sesiones no tienen una pantalla propia — viven dentro del perfil del
                   paciente), solo se marcan como leídas.

                   **Mark all read** marca todas como leídas de una vez. No hay forma de
                   eliminar o descartar una notificación individual — quedan en la lista,
                   solo que dejan de resaltarse.
                   """,
            Audience = HelpAudience.All,
            Category = "Notificaciones",
            DisplayOrder = 1
        }
    ];

    // Lets a dev-only startup flag (see Program.cs's --reset-help-topics) wipe exactly the
    // starter rows so the next seed run reinserts them with whatever content is currently
    // in GetStarterHelpTopics() — without hardcoding the key list a second time anywhere.
    internal static IReadOnlyCollection<string> GetStarterHelpTopicKeys() =>
        GetStarterHelpTopics().Select(t => t.TopicKey).ToList();
}
