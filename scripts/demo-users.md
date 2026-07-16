# Usuarios de demo — HouseCenter (pre-defensa de monografía)

Credenciales para iniciar sesión y mostrar la interacción de cada rol, generadas por
`POST /seed` (`TestDataSeeder.cs`) y enriquecidas con historial real por
`scripts/demo-seed.mjs`. Todas comparten la misma contraseña.

**No incluye al usuario Owner/root** — sus credenciales viven en tus User Secrets
(`Seed:OwnerEmail` / `Seed:OwnerPassword`), no en el código. El Owner es el único rol con
control total: gestión de usuarios y roles, asignación de médicos a pacientes.

| Rol | Nombre | Email | Password |
|---|---|---|---|
| Administrator | Ana Martínez | `admin@housecenter.dev` | `Demo@123!` |
| Member (Fisioterapeuta) | Carlos López | `member@housecenter.dev` | `Demo@123!` |
| Member (Educadora) | María Gutiérrez | `member2@housecenter.dev` | `Demo@123!` |
| Doctor (Médico Rehabilitador) | Roberto Fernández | `doctor@housecenter.dev` | `Demo@123!` |
| Sponsor | Susan Miller | `sponsor@housecenter.dev` | `Demo@123!` |

> Contraseña configurable vía `Seed:TestUserPassword` en User Secrets — si la cambiaste,
> usá esa en vez de `Demo@123!`.

---

## Qué mostrar desde cada cuenta

### Administrator — `admin@housecenter.dev`
- **Users / Invitations / Collaborators / Clinics / Work Routes**: gestión completa (todo
  menos asignar roles a un usuario, que es exclusivo del Owner).
- **Invitations**: ve la invitación pendiente a `nueva.colaboradora@housecenter.dev`
  (rol Member, estado `Pending`).
- **Reports**: única vista, junto con Owner, que incluye el ranking "Sessions by
  collaborator".

### Member — Carlos López (`member@housecenter.dev`)
- Paciente **Sofía Hernández**: pestaña *Sessions* con 9 semanas de historial real
  (mezcla de `Completed`/`Missed`/`Scheduled`) — el mejor ejemplo del ciclo de vida
  completo de una sesión.
- Abrió la consulta **"Seguimiento de Sofía Hernández"** con el Dr. Fernández — ya
  **Resolved**, con 3 mensajes en el hilo (primer mensaje, respuesta del médico,
  seguimiento propio).
- **Reports**: solo ve su propio conteo de sesiones, sin el ranking por colaborador
  (ese bloque es exclusivo de Admin/Owner).

### Member — María Gutiérrez (`member2@housecenter.dev`)
- Pacientes **Valentina Cruz** y **Camila Ortiz**: ambas con `WorkRoute` asignado
  (Ruta San Ramón / Ruta El Tuma) — buen ejemplo de "Ruta del día" en mobile.
- Tratamiento de Camila Ortiz está en estado **Paused** (el resto quedan `Active`) —
  variedad de estados en la pestaña Treatments.
- Abrió la consulta de **Emmanuel García** con el Dr. Fernández — quedó en
  **Under Review** (el médico ya respondió una vez, no se resolvió).

### Doctor — Roberto Fernández (`doctor@housecenter.dev`)
- Ve 3 pacientes asignados (**Sofía, Diego, Emmanuel**) sin haberlos visitado en
  persona — el flujo de seguimiento remoto.
- Bandeja de **Consultations**: 1 resuelta (Sofía), 2 en `Under Review`
  (Diego, Emmanuel) — respondió en las 3.
- **Reports**: solo cuenta sesiones de sus pacientes asignados.

### Sponsor — Susan Miller (`sponsor@housecenter.dev`)
- Único rol sin acceso a **Consultations** ni a datos clínicos individuales.
- **Reports**: ve el agregado de sesiones sin desglose por colaborador (mismo
  bloque que ve un Member) — el ejemplo más claro del rol de "visibilidad curada"
  para financiamiento, sin exponer información de pacientes.

---

## Detalle honesto (no lo escondas si te preguntan)

- La consulta de **Diego Ramírez** fue abierta con la cuenta de **Owner**, no con la de
  un Member — Diego no tiene ningún colaborador asignado en el seed base
  (`TestDataSeeder.cs`), y solo un Owner/Admin puede abrir una consulta a nombre de un
  paciente sin colaborador propio. Es una particularidad real de los datos de prueba,
  no un bug de la demo.
- La invitación a `nueva.colaboradora@housecenter.dev` está **Pending** — nadie inició
  sesión con ese email todavía porque nadie aceptó la invitación (no es una cuenta
  utilizable).
