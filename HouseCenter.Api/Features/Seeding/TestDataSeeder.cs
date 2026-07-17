using HouseCenter.Api.Data;
using HouseCenter.Api.Domain.Constants;
using HouseCenter.Api.Domain.Entities;
using HouseCenter.Api.Options;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Options;

namespace HouseCenter.Api.Features.Seeding;

public sealed record SeedGroupResult(int Created, int Skipped);

public sealed record SeedResponse(
    SeedGroupResult Users,
    SeedGroupResult Clinics,
    SeedGroupResult Collaborators,
    SeedGroupResult Patients);

/// <summary>
/// Inserts a fixed, known set of test data: one user per non-owner role, clinics,
/// collaborators (with positions) and patients wired to those clinics and users.
/// Idempotent: records are keyed by natural identifiers (email / name) and existing
/// ones are skipped. Existence checks bypass the global IsActive filter so a
/// soft-deleted seed record is never re-inserted as a duplicate.
/// Runs only when POST /seed is called (Development/Staging) — never at startup;
/// startup seeding (roles + owner) stays in <see cref="DatabaseSeeder"/>.
/// </summary>
public sealed class TestDataSeeder(
    HouseCenterDbContext db,
    IOptions<SeedOptions> seedOptions,
    ILogger<TestDataSeeder> logger)
{
    public async Task<SeedResponse> SeedAsync(CancellationToken ct)
    {
        // Order matters: patients reference users (collaborator/doctor assignment) and
        // clinics; collaborators reference clinics.
        var users = await SeedUsersAsync(ct);
        var clinics = await SeedClinicsAsync(ct);
        var collaborators = await SeedCollaboratorsAsync(ct);
        var patients = await SeedPatientsAsync(ct);

        logger.LogInformation(
            "Test data seed: users +{Users}, clinics +{Clinics}, collaborators +{Collaborators}, patients +{Patients}",
            users.Created, clinics.Created, collaborators.Created, patients.Created);

        return new SeedResponse(users, clinics, collaborators, patients);
    }

    // ---------- Users (one per non-owner role, all with the same known test password) ----------

    private sealed record UserSeed(string Email, string First, string Last, string Role, string? Profession);

    private static readonly UserSeed[] TestUsers =
    [
        new("admin@housecenter.dev", "Ana", "Martínez", RoleNames.Administrator, null),
        new("member@housecenter.dev", "Carlos", "López", RoleNames.Member, "Fisioterapeuta"),
        new("member2@housecenter.dev", "María", "Gutiérrez", RoleNames.Member, "Educadora"),
        new("doctor@housecenter.dev", "Roberto", "Fernández", RoleNames.Doctor, "Médico Rehabilitador"),
        new("sponsor@housecenter.dev", "Susan", "Miller", RoleNames.Sponsor, null)
    ];

    private async Task<SeedGroupResult> SeedUsersAsync(CancellationToken ct)
    {
        var emails = TestUsers.Select(u => u.Email).ToArray();
        var existing = await db.Users.IgnoreQueryFilters()
            .Where(u => emails.Contains(u.Email))
            .Select(u => u.Email)
            .ToListAsync(ct);

        var missing = TestUsers.Where(u => !existing.Contains(u.Email)).ToList();
        if (missing.Count == 0) return new SeedGroupResult(0, TestUsers.Length);

        var roles = await db.Roles.ToDictionaryAsync(r => r.Name, ct);
        var hashedPassword = BCrypt.Net.BCrypt.HashPassword(seedOptions.Value.TestUserPassword);

        db.Users.AddRange(missing.Select(u => new User
        {
            FirstName = u.First,
            LastName = u.Last,
            Email = u.Email,
            HashedPassword = hashedPassword,
            Address = "Managua, Nicaragua",
            Country = "nicaragua",
            City = "Managua",
            Profession = u.Profession,
            EmailConfirmed = true,
            Roles = [roles[u.Role]]
        }));
        await db.SaveChangesAsync(ct);

        return new SeedGroupResult(missing.Count, TestUsers.Length - missing.Count);
    }

    // ---------- Clinics ----------

    private sealed record ClinicSeed(string Name, string Address);

    private static readonly ClinicSeed[] TestClinics =
    [
        new("Clínica Central Managua", "Barrio Martha Quezada, Managua"),
        new("Clínica San Ramón", "Calle Central, San Ramón, Matagalpa"),
        new("Clínica El Tuma", "Comunidad El Tuma, Matagalpa")
    ];

    private async Task<SeedGroupResult> SeedClinicsAsync(CancellationToken ct)
    {
        var names = TestClinics.Select(c => c.Name).ToArray();
        var existing = await db.Clinics.IgnoreQueryFilters()
            .Where(c => names.Contains(c.Name))
            .Select(c => c.Name)
            .ToListAsync(ct);

        var missing = TestClinics.Where(c => !existing.Contains(c.Name)).ToList();
        if (missing.Count == 0) return new SeedGroupResult(0, TestClinics.Length);

        db.Clinics.AddRange(missing.Select(c => new Clinic { Name = c.Name, Address = c.Address }));
        await db.SaveChangesAsync(ct);

        return new SeedGroupResult(missing.Count, TestClinics.Length - missing.Count);
    }

    // ---------- Collaborators (field staff records, distinct from User accounts) ----------

    // UserEmail links this roster row directly to its Member User seed at insert time (null
    // == deliberate legacy/roster-only row, no login). Realigned per
    // openspec/changes/collaborator-user-link/design.md Decision 2: member@/member2@ mirror
    // their Member User seeds in BOTH email and name, so reconciliation never needs to touch
    // them — zero accidental legacy rows. pedro.castillo@ is the one intentional legacy row,
    // exercising that path (design.md Decision 5: a permanently valid state, not an anomaly).
    private sealed record CollaboratorSeed(
        string Email, string First, string Last, string Phone, string Clinic, string[] Positions,
        string? UserEmail = null);

    private static readonly CollaboratorSeed[] TestCollaborators =
    [
        new("member@housecenter.dev", "Carlos", "López", "+505 8888 0001",
            "Clínica Central Managua", ["Fisioterapeuta"], "member@housecenter.dev"),
        new("member2@housecenter.dev", "María", "Gutiérrez", "+505 8888 0002",
            "Clínica San Ramón", ["Educadora", "Coordinadora de Ruta"], "member2@housecenter.dev"),
        new("pedro.castillo@housecenter.dev", "Pedro", "Castillo", "+505 8888 0003",
            "Clínica El Tuma", ["Fisioterapeuta"])
    ];

    private async Task<SeedGroupResult> SeedCollaboratorsAsync(CancellationToken ct)
    {
        var emails = TestCollaborators.Select(c => c.Email).ToArray();
        var existing = await db.Collaborators.IgnoreQueryFilters()
            .Where(c => emails.Contains(c.Email))
            .Select(c => c.Email)
            .ToListAsync(ct);

        var missing = TestCollaborators.Where(c => !existing.Contains(c.Email)).ToList();
        if (missing.Count == 0) return new SeedGroupResult(0, TestCollaborators.Length);

        // Seeded clinics are guaranteed to exist — SeedClinicsAsync ran first
        var clinicIds = await db.Clinics.IgnoreQueryFilters()
            .Where(c => TestClinics.Select(tc => tc.Name).Contains(c.Name))
            .ToDictionaryAsync(c => c.Name, c => c.Id, ct);

        // Seeded users are guaranteed to exist — SeedUsersAsync ran first. Only rows with a
        // UserEmail need a lookup; pedro.castillo@ (UserEmail == null) stays legacy.
        var userEmails = missing.Where(c => c.UserEmail is not null).Select(c => c.UserEmail!).ToArray();
        var userIdsByEmail = await db.Users.IgnoreQueryFilters()
            .Where(u => userEmails.Contains(u.Email))
            .ToDictionaryAsync(u => u.Email, u => u.Id, ct);

        db.Collaborators.AddRange(missing.Select(c => new Collaborator
        {
            FirstName = c.First,
            LastName = c.Last,
            Email = c.Email,
            PhoneNumber = c.Phone,
            Address = "Nicaragua",
            ClinicId = clinicIds[c.Clinic],
            UserId = c.UserEmail is not null ? userIdsByEmail[c.UserEmail] : null,
            Positions = c.Positions.Select(p => new Position { Name = p }).ToList()
        }));
        await db.SaveChangesAsync(ct);

        return new SeedGroupResult(missing.Count, TestCollaborators.Length - missing.Count);
    }

    // ---------- Patients (children, wired to seeded clinics / users) ----------

    private sealed record PatientSeed(
        string First, string Last, Gender Gender, DateTime BirthDate, AttentionType Type,
        string Clinic, string City, string State, string? CollaboratorEmail, string? DoctorEmail);

    private static DateTime Utc(int year, int month, int day) =>
        new(year, month, day, 0, 0, 0, DateTimeKind.Utc);

    private static readonly PatientSeed[] TestPatients =
    [
        new("Sofía", "Hernández", Gender.Female, Utc(2015, 3, 12), AttentionType.Medical,
            "Clínica Central Managua", "Managua", "Managua",
            "member@housecenter.dev", "doctor@housecenter.dev"),
        new("Diego", "Ramírez", Gender.Male, Utc(2017, 8, 25), AttentionType.Medical,
            "Clínica Central Managua", "Managua", "Managua",
            null, "doctor@housecenter.dev"),
        new("Valentina", "Cruz", Gender.Female, Utc(2013, 11, 2), AttentionType.EducationalReinforcement,
            "Clínica San Ramón", "San Ramón", "Matagalpa",
            "member2@housecenter.dev", null),
        new("Mateo", "Flores", Gender.Male, Utc(2016, 1, 19), AttentionType.Medical,
            "Clínica San Ramón", "San Ramón", "Matagalpa",
            "member@housecenter.dev", null),
        new("Camila", "Ortiz", Gender.Female, Utc(2018, 6, 7), AttentionType.EducationalReinforcement,
            "Clínica El Tuma", "El Tuma", "Matagalpa",
            null, null),
        new("Emmanuel", "García", Gender.Male, Utc(2014, 9, 30), AttentionType.Medical,
            "Clínica El Tuma", "El Tuma", "Matagalpa",
            "member2@housecenter.dev", "doctor@housecenter.dev")
    ];

    private async Task<SeedGroupResult> SeedPatientsAsync(CancellationToken ct)
    {
        var firstNames = TestPatients.Select(p => p.First).ToArray();
        var existing = await db.Patients.IgnoreQueryFilters()
            .Where(p => firstNames.Contains(p.FirstName))
            .Select(p => p.FirstName + "|" + p.LastName)
            .ToListAsync(ct);

        var missing = TestPatients.Where(p => !existing.Contains(p.First + "|" + p.Last)).ToList();
        if (missing.Count == 0) return new SeedGroupResult(0, TestPatients.Length);

        var clinicIds = await db.Clinics.IgnoreQueryFilters()
            .Where(c => TestClinics.Select(tc => tc.Name).Contains(c.Name))
            .ToDictionaryAsync(c => c.Name, c => c.Id, ct);

        var userEmails = TestUsers.Select(u => u.Email).ToArray();
        var usersByEmail = await db.Users.IgnoreQueryFilters()
            .Where(u => userEmails.Contains(u.Email))
            .ToDictionaryAsync(u => u.Email, ct);

        db.Patients.AddRange(missing.Select(p => new Patient
        {
            FirstName = p.First,
            LastName = p.Last,
            Gender = p.Gender,
            BirthDate = p.BirthDate,
            PrimaryAttentionType = p.Type,
            Address = $"{p.City}, {p.State}",
            City = p.City,
            State = p.State,
            Description = "Paciente de prueba (seed)",
            ClinicId = clinicIds[p.Clinic],
            Collaborators = p.CollaboratorEmail is null ? [] : [usersByEmail[p.CollaboratorEmail]],
            AssignedDoctors = p.DoctorEmail is null ? [] : [usersByEmail[p.DoctorEmail]]
        }));
        await db.SaveChangesAsync(ct);

        return new SeedGroupResult(missing.Count, TestPatients.Length - missing.Count);
    }
}
