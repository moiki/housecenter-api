#!/usr/bin/env node
// Generates realistic historical demo data for the pre-defense presentation, on top of
// whatever POST /seed already inserts (5 users, 3 clinics, 3 collaborators, 6 patients).
//
// WHY a script against the real API instead of a DB seeder: notifications (the bell, on
// both web and mobile) and a few other side effects are raised as MediatR events inside the
// request handlers (UpdateSessionStatus, OpenConsultation, PostConsultationMessage,
// UpdateConsultationStatus) — not database triggers. Writing rows directly to Postgres would
// skip them, so this script drives the same HTTP endpoints a real user's browser/app would.
//
// Usage:
//   cd HouseCenter
//   dotnet run --project HouseCenter.Api   # leave running in another terminal (Development)
//   DEMO_OWNER_EMAIL=owner@example.com DEMO_OWNER_PASSWORD=... node scripts/demo-seed.mjs
//
// Env vars (all optional except the two owner ones, which match your User Secrets):
//   DEMO_API_URL          default http://localhost:5080
//   DEMO_OWNER_EMAIL      required — Seed:OwnerEmail from User Secrets
//   DEMO_OWNER_PASSWORD   required — Seed:OwnerPassword from User Secrets
//   DEMO_TEST_PASSWORD    default Demo@123! — must match Seed:TestUserPassword if you changed it
//
// Re-running: WorkRoutes and the Invitation are looked up by name/email first (safe to
// re-run). AttentionSessions, Treatments, TreatmentDetails, PatientComments and
// MedicalConsultations are NOT deduplicated — re-running adds a second batch on top. For a
// clean slate before the real rehearsal, drop and recreate the database (or set
// Database:RecreateOnStartup once) and run this script exactly once.

const API_URL = process.env.DEMO_API_URL ?? 'http://localhost:5080'
const OWNER_EMAIL = process.env.DEMO_OWNER_EMAIL
const OWNER_PASSWORD = process.env.DEMO_OWNER_PASSWORD
const TEST_PASSWORD = process.env.DEMO_TEST_PASSWORD ?? 'Demo@123!'

if (!OWNER_EMAIL || !OWNER_PASSWORD) {
  console.error('DEMO_OWNER_EMAIL and DEMO_OWNER_PASSWORD are required (see Seed:OwnerEmail / Seed:OwnerPassword in User Secrets).')
  process.exit(1)
}

const SEED_EMAILS = {
  admin: 'admin@housecenter.dev',
  member: 'member@housecenter.dev',
  member2: 'member2@housecenter.dev',
  doctor: 'doctor@housecenter.dev',
  sponsor: 'sponsor@housecenter.dev',
}

// --- tiny HTTP helper -------------------------------------------------------

async function call(method, path, { token, body } = {}) {
  const res = await fetch(`${API_URL}${path}`, {
    method,
    headers: {
      'Content-Type': 'application/json',
      ...(token ? { Authorization: `Bearer ${token}` } : {}),
    },
    body: body !== undefined ? JSON.stringify(body) : undefined,
  })
  const text = await res.text()
  const data = text ? JSON.parse(text) : undefined
  if (!res.ok) {
    throw new Error(`${method} ${path} -> ${res.status}\n${JSON.stringify(data, null, 2)}`)
  }
  return data
}

async function login(email, password) {
  const { accessToken } = await call('POST', '/auth/login', {
    body: {
      email,
      password,
      deviceId: 'demo-seed-script',
      deviceName: 'Demo Seed Script',
      platform: 'Web',
    },
  })
  return accessToken
}

async function listAll(path, token) {
  const { items } = await call('GET', `${path}${path.includes('?') ? '&' : '?'}page=1&pageSize=100`, { token })
  return items
}

// --- date helpers (relative to "now" when the script runs) -----------------

const DAY_MS = 24 * 60 * 60 * 1000
const daysAgo = (n) => new Date(Date.now() - n * DAY_MS).toISOString()
const daysFromNow = (n) => new Date(Date.now() + n * DAY_MS).toISOString()

// --- main --------------------------------------------------------------------

async function main() {
  console.log(`Target API: ${API_URL}`)

  console.log('\n[1/8] Logging in as Owner and running POST /seed ...')
  const ownerToken = await login(OWNER_EMAIL, OWNER_PASSWORD)
  const seedResult = await call('POST', '/seed', { token: ownerToken })
  console.log('  seed result:', JSON.stringify(seedResult))

  console.log('\n[2/8] Logging in as each seed user ...')
  const tokens = {}
  for (const [key, email] of Object.entries(SEED_EMAILS)) {
    tokens[key] = await login(email, TEST_PASSWORD)
    console.log(`  ${key} (${email}) OK`)
  }

  console.log('\n[3/8] Loading reference data (users, patients, clinics, roles) ...')
  const users = await listAll('/users', ownerToken)
  const patients = await listAll('/patients', ownerToken)
  const clinics = await listAll('/api/v1/clinics', ownerToken)
  const roles = await call('GET', '/roles', { token: ownerToken })

  const userIdByEmail = Object.fromEntries(users.map((u) => [u.email, u.id]))
  const patientByName = Object.fromEntries(patients.map((p) => [`${p.firstName} ${p.lastName}`, p]))
  const clinicIdByName = Object.fromEntries(clinics.map((c) => [c.name, c.id]))
  const roleIdByName = Object.fromEntries(roles.map((r) => [r.name, r.id]))

  const memberId = userIdByEmail[SEED_EMAILS.member]
  const member2Id = userIdByEmail[SEED_EMAILS.member2]
  const doctorId = userIdByEmail[SEED_EMAILS.doctor]

  // Patient roster with the demo-relevant wiring (mirrors TestDataSeeder.cs exactly).
  // visitTime is real data now (Patient.RouteVisitTime) — it drives stop ordering on the
  // route's calendar/map view directly, no more inferring it from an assumed workday.
  const roster = [
    { name: 'Sofía Hernández', collaboratorId: memberId, hasDoctor: true, workRoute: null, visitTime: null },
    { name: 'Diego Ramírez', collaboratorId: memberId, hasDoctor: true, workRoute: null, visitTime: null },
    { name: 'Valentina Cruz', collaboratorId: member2Id, hasDoctor: false, workRoute: 'Ruta San Ramón', visitTime: '09:00:00' },
    { name: 'Mateo Flores', collaboratorId: memberId, hasDoctor: false, workRoute: 'Ruta San Ramón', visitTime: '11:30:00' },
    { name: 'Camila Ortiz', collaboratorId: member2Id, hasDoctor: false, workRoute: 'Ruta El Tuma', visitTime: '09:30:00' },
    { name: 'Emmanuel García', collaboratorId: member2Id, hasDoctor: true, workRoute: 'Ruta El Tuma', visitTime: '13:00:00' },
  ].map((r) => ({ ...r, patient: patientByName[r.name] }))

  for (const r of roster) {
    if (!r.patient) throw new Error(`Seed patient "${r.name}" not found — did POST /seed run against a fresh DB?`)
  }

  console.log('\n[4/8] Creating work routes (idempotent by name) ...')
  const existingRoutes = await listAll('/workroutes', ownerToken)
  const workRouteIdByName = Object.fromEntries(existingRoutes.map((w) => [w.routeName, w.id]))

  const ALL_WEEKDAYS = ['Monday', 'Tuesday', 'Wednesday', 'Thursday', 'Friday', 'Saturday']
  // Stops are no longer entered on the route itself (no more free-text destinations) —
  // they're derived from whichever patients have this route set via workRouteId, so
  // creating a route here is now just name + clinic + recurrence.
  const routeDefs = [
    {
      routeName: 'Ruta San Ramón',
      description: 'Visitas domiciliarias semanales en la zona de San Ramón, Matagalpa.',
      clinicName: 'Clínica San Ramón',
    },
    {
      routeName: 'Ruta El Tuma',
      description: 'Visitas domiciliarias en la comunidad El Tuma, Matagalpa.',
      clinicName: 'Clínica El Tuma',
    },
  ]

  for (const def of routeDefs) {
    if (workRouteIdByName[def.routeName]) {
      console.log(`  "${def.routeName}" already exists — skipping`)
      continue
    }
    const created = await call('POST', '/workroutes', {
      token: ownerToken,
      body: {
        routeName: def.routeName,
        description: def.description,
        featuredImage: null,
        clinicId: clinicIdByName[def.clinicName],
        // Every weekday but Sunday, so "Ruta del día" always has something to show
        // regardless of which day the demo actually happens on.
        recurrenceDays: ALL_WEEKDAYS,
        recurrenceStartDate: new Date(Date.now() - 90 * DAY_MS).toISOString().slice(0, 10),
        recurrenceEndDate: null,
        isRecurrenceIndefinite: true,
      },
    })
    workRouteIdByName[def.routeName] = created.id
    console.log(`  created "${def.routeName}"`)
  }

  console.log('\n[5/8] Wiring the 4 route-based patients to their work route + visit time ...')
  for (const r of roster.filter((r) => r.workRoute)) {
    const full = await call('GET', `/patients/${r.patient.id}`, { token: ownerToken })
    await call('PUT', `/patients/${r.patient.id}`, {
      token: ownerToken,
      body: {
        firstName: full.firstName,
        lastName: full.lastName,
        profile: full.profile,
        birthDate: full.birthDate,
        gender: full.gender,
        country: full.country,
        state: full.state,
        city: full.city,
        address: full.address,
        description: full.description,
        primaryAttentionType: full.primaryAttentionType,
        clinicId: full.clinicId,
        workRouteId: workRouteIdByName[r.workRoute],
        routeVisitTime: r.visitTime,
      },
    })
    console.log(`  ${r.name} -> ${r.workRoute} @ ${r.visitTime.slice(0, 5)}`)
  }

  console.log('\n[6/8] Creating attention sessions (9 weeks back + 1 upcoming, per patient) ...')
  const NOTES_BY_TYPE = {
    Medical: ['Buena evolución motora, continúa con los ejercicios asignados.', 'Sesión de rehabilitación física, se ajusta rutina de estiramiento.', 'Control de progreso, familia reporta mejoría en autonomía.'],
    EducationalReinforcement: ['Avance en lectoescritura, se refuerzan vocales.', 'Sesión de refuerzo escolar, buena participación.', 'Se trabajó con material lúdico para motricidad fina.'],
  }
  let sessionsCreated = 0, sessionsCompleted = 0, sessionsMissed = 0

  for (const r of roster) {
    const attentionType = r.patient.primaryAttentionType
    const notesPool = NOTES_BY_TYPE[attentionType] ?? NOTES_BY_TYPE.Medical
    const location = r.workRoute
      ? { workRouteId: workRouteIdByName[r.workRoute], clinicId: null }
      : { workRouteId: null, clinicId: r.patient.clinicId }

    // 9 weekly sessions in the past, oldest first
    for (let week = 9; week >= 1; week--) {
      const session = await call('POST', `/patients/${r.patient.id}/sessions`, {
        token: tokens.member, // recorded by field staff, regardless of who's assigned
        body: {
          collaboratorId: r.collaboratorId,
          ...location,
          attentionType,
          sessionDate: daysAgo(week * 7),
          durationMinutes: 45,
          notes: notesPool[week % notesPool.length],
        },
      })
      sessionsCreated++

      // ~70% Completed, ~20% Missed, ~10% left Scheduled (status lifecycle variety)
      const roll = (week * 7 + r.name.length) % 10
      const status = roll < 7 ? 'Completed' : roll < 9 ? 'Missed' : null
      if (status) {
        // Ownership rule: only the session's own collaborator or an admin/owner can
        // change its status (sessions.not_owner) — some sessions here were created
        // with collaboratorId = member2Id, so use ownerToken to cover every case.
        await call('PATCH', `/patients/${r.patient.id}/sessions/${session.id}/status`, {
          token: ownerToken,
          body: { status, durationMinutes: status === 'Completed' ? 45 : null, notes: null },
        })
        status === 'Completed' ? sessionsCompleted++ : sessionsMissed++
      }
    }

    // One upcoming session, left Scheduled — shows the "next visit" state
    await call('POST', `/patients/${r.patient.id}/sessions`, {
      token: tokens.member,
      body: {
        collaboratorId: r.collaboratorId,
        ...location,
        attentionType,
        sessionDate: daysFromNow(2),
        durationMinutes: 45,
        notes: null,
      },
    })
    sessionsCreated++
  }
  console.log(`  ${sessionsCreated} sessions created (${sessionsCompleted} completed, ${sessionsMissed} missed -> triggers notifications)`)

  console.log('\n[7/8] Creating treatments, details, comments and consultations ...')
  const TREATMENT_NAME = {
    Medical: 'Terapia física — plan de rehabilitación',
    EducationalReinforcement: 'Refuerzo escolar — plan de nivelación',
  }

  for (const r of roster) {
    const attentionType = r.patient.primaryAttentionType
    const treatment = await call('POST', `/patients/${r.patient.id}/treatments`, {
      token: tokens.member,
      body: {
        name: TREATMENT_NAME[attentionType] ?? TREATMENT_NAME.Medical,
        description: 'Plan de atención generado para la demo de monografía.',
        profile: null,
        startDate: daysAgo(70),
        endDate: daysFromNow(90),
        type: attentionType,
      },
    })
    r.treatmentId = treatment.id

    await call('POST', `/treatments/${treatment.id}/details`, {
      token: tokens.member,
      body: { name: 'Evaluación inicial', description: 'Línea base registrada al inicio del tratamiento.', profile: null, treatmentDate: daysAgo(63) },
    })
    await call('POST', `/treatments/${treatment.id}/details`, {
      token: tokens.member,
      body: { name: 'Control de avance', description: 'Progreso visible respecto a la evaluación inicial.', profile: null, treatmentDate: daysAgo(21) },
    })
    await call('POST', `/treatments/${treatment.id}/comments`, {
      token: tokens.member,
      body: { body: 'Familia colabora activamente con los ejercicios asignados en casa.', type: 'Simple' },
    })

    // Pause one treatment for status-variety on the Treatments tab
    if (r.name === 'Camila Ortiz') {
      await call('PATCH', `/patients/${r.patient.id}/treatments/${treatment.id}/status`, {
        token: tokens.member,
        body: { status: 'Paused' },
      })
    }

    await call('POST', `/patients/${r.patient.id}/comments`, {
      token: tokens.member,
      body: { body: 'Paciente muestra buena disposición durante las visitas.', type: 'Simple' },
    })
  }

  // Consultations, on the patients that already have an assigned doctor. Opening one
  // requires being an actual Patient.Collaborators member (spec R5) — a DIFFERENT
  // relationship than the `collaboratorId` used for AttentionSessions above, and one
  // TestDataSeeder only set for Sofía (member) and Emmanuel (member2); Diego has no
  // assigned collaborator at all, so only the admin/owner bypass can open his case.
  const OPENER_TOKEN_BY_PATIENT = {
    'Sofía Hernández': 'member',
    'Emmanuel García': 'member2',
  }
  const consultationPatients = roster.filter((r) => r.hasDoctor)
  const consultations = []
  for (const r of consultationPatients) {
    const openerKey = OPENER_TOKEN_BY_PATIENT[r.name]
    const openerToken = openerKey ? tokens[openerKey] : ownerToken
    const consultation = await call('POST', '/consultations', {
      token: openerToken,
      body: {
        patientId: r.patient.id,
        treatmentId: r.treatmentId,
        assignedDoctorId: doctorId,
        title: `Seguimiento de ${r.name}`,
        firstMessage: '¿Podría revisar el progreso reciente y confirmar si ajustamos la frecuencia de sesiones?',
        attachmentUrl: null,
      },
    })
    consultations.push({ ...consultation, patientName: r.name, openerToken })
  }

  // Doctor replies on each (-> notifies whoever opened it; also flips Open -> UnderReview)
  for (const c of consultations) {
    await call('POST', `/consultations/${c.id}/messages`, {
      token: tokens.doctor,
      body: { body: 'Progreso adecuado. Mantengamos la frecuencia actual dos semanas más.', attachmentUrl: null },
    })
  }
  // Opener follow-up on the first consultation, then resolve it; the rest stay open/under review
  await call('POST', `/consultations/${consultations[0].id}/messages`, {
    token: consultations[0].openerToken,
    body: { body: 'Entendido, gracias por la confirmación.', attachmentUrl: null },
  })
  await call('PATCH', `/consultations/${consultations[0].id}/status`, {
    token: tokens.doctor,
    body: { status: 'Resolved' },
  })
  console.log(`  ${consultations.length} consultations created (1 resolved, ${consultations.length - 1} left open/under review)`)

  console.log('\n[8/8] Creating a pending invitation ...')
  const existingInvitations = await listAll('/invitations', ownerToken)
  const invitationEmail = 'nueva.colaboradora@housecenter.dev'
  if (existingInvitations.some((i) => i.email === invitationEmail)) {
    console.log('  invitation already exists — skipping')
  } else {
    await call('POST', '/invitations', {
      token: ownerToken,
      body: { email: invitationEmail, roleId: roleIdByName.Member },
    })
    console.log(`  invited ${invitationEmail} as Member (Pending)`)
  }

  console.log('\nDone. Demo data ready — login as any *@housecenter.dev user with password', TEST_PASSWORD)
}

main().catch((err) => {
  console.error('\nFAILED:', err.message)
  process.exit(1)
})
