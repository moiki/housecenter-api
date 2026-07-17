using FluentAssertions;
using HouseCenter.Api.Data;
using HouseCenter.Api.Domain.Entities;
using HouseCenter.Api.Features.Clinics;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using System.Net.Http.Json;

namespace HouseCenter.Api.Tests;

// Integration test for Migration B (AddCollaboratorEmailUnique, Phase 3). Runs against the
// REAL Postgres test DB (WebApplicationFactory) because the InMemory EF provider does NOT
// enforce unique-index constraints at SaveChangesAsync — verified empirically in PR1's
// CollaboratorUserLinkTests. This is the actual "fails loudly on duplicates" safety gate
// design.md Decision 3 describes: a duplicate active email must be physically rejected by
// the database, not just by the application-level check in CollaboratorService.CreateAsync.
[Collection("Api")]
public sealed class CollaboratorEmailUniqueIndexTests(ApiFactory factory)
{
    [Fact]
    public async Task DuplicateEmail_InsertedDirectlyBypassingServiceChecks_ViolatesDbUniqueIndex()
    {
        var admin = await factory.CreateAuthenticatedClientAsync();
        var clinic = await CreateClinicAsync(admin);
        var email = $"dupindex-{Guid.NewGuid():N}@test.local";

        await using var scope = factory.Services.CreateAsyncScope();
        var db = scope.ServiceProvider.GetRequiredService<HouseCenterDbContext>();

        db.Collaborators.Add(new Collaborator
        {
            FirstName = "Uno",
            LastName = "Duplicado",
            Email = email,
            PhoneNumber = "8888-0000",
            Address = "Dirección",
            ClinicId = clinic.Id
        });
        await db.SaveChangesAsync();

        // Second insert bypasses CollaboratorService.CreateAsync's application-level dup
        // check entirely (direct DbContext write) — only the DB-level unique index from
        // Migration B can still stop it. That failure IS the intended safety gate.
        db.Collaborators.Add(new Collaborator
        {
            FirstName = "Dos",
            LastName = "Duplicado",
            Email = email,
            PhoneNumber = "8888-0001",
            Address = "Dirección",
            ClinicId = clinic.Id
        });

        var act = async () => await db.SaveChangesAsync();

        await act.Should().ThrowAsync<DbUpdateException>();
    }

    private static async Task<ClinicResponse> CreateClinicAsync(HttpClient client)
    {
        var response = await client.PostAsJsonAsync("/api/v1/clinics",
            new CreateClinicRequest($"Clínica {Guid.NewGuid():N}"[..25], "Calle Central"));
        response.EnsureSuccessStatusCode();
        return (await response.Content.ReadFromJsonAsync<ClinicResponse>())!;
    }
}
