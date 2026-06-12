using System.Net;
using System.Net.Http.Json;
using FluentAssertions;
using HouseCenter.Api.Features.Clinics;

namespace HouseCenter.Api.Tests;

[Collection("Api")]
public sealed class ClinicCrudTests(ApiFactory factory)
{
    // Clinics moved to versioned route in Phase 5.6
    private const string Base = "/api/v1/clinics";

    private Task<HttpClient> Client() => factory.CreateAuthenticatedClientAsync();

    [Fact]
    public async Task CreateClinic_WithValidData_Returns201AndPersistedClinic()
    {
        var client = await Client();

        var response = await client.PostAsJsonAsync(Base,
            new CreateClinicRequest("Clínica Norte", "Avenida Norte 123"));

        response.StatusCode.Should().Be(HttpStatusCode.Created);
        response.Headers.Location.Should().NotBeNull();
        var clinic = await response.Content.ReadFromJsonAsync<ClinicResponse>();
        clinic!.Name.Should().Be("Clínica Norte");
        clinic.IsActive.Should().BeTrue();
    }

    [Fact]
    public async Task CreateClinic_WithDuplicateName_Returns409Conflict()
    {
        var client = await Client();
        var request = new CreateClinicRequest("Clínica Única", "Dirección 1");

        await client.PostAsJsonAsync(Base, request);
        var duplicate = await client.PostAsJsonAsync(Base, request);

        duplicate.StatusCode.Should().Be(HttpStatusCode.Conflict);
    }

    [Fact]
    public async Task CreateClinic_WithEmptyName_Returns400ValidationProblem()
    {
        var client = await Client();

        var response = await client.PostAsJsonAsync(Base,
            new CreateClinicRequest("", "Dirección válida"));

        response.StatusCode.Should().Be(HttpStatusCode.BadRequest);
    }

    [Fact]
    public async Task GetClinic_AfterCreate_ReturnsIt()
    {
        var client = await Client();
        var created = await CreateClinicAsync(client, "Clínica Sur");

        var response = await client.GetAsync($"{Base}/{created.Id}");

        response.StatusCode.Should().Be(HttpStatusCode.OK);
        var clinic = await response.Content.ReadFromJsonAsync<ClinicResponse>();
        clinic!.Id.Should().Be(created.Id);
        clinic.Name.Should().Be("Clínica Sur");
    }

    [Fact]
    public async Task GetClinic_WithUnknownId_Returns404()
    {
        var client = await Client();
        var response = await client.GetAsync($"{Base}/{Guid.NewGuid()}");
        response.StatusCode.Should().Be(HttpStatusCode.NotFound);
    }

    [Fact]
    public async Task UpdateClinic_ChangesNameAndAddress()
    {
        var client = await Client();
        var created = await CreateClinicAsync(client, "Clínica Original");

        var response = await client.PutAsJsonAsync($"{Base}/{created.Id}",
            new UpdateClinicRequest("Clínica Actualizada", "Nueva Dirección 456"));

        response.StatusCode.Should().Be(HttpStatusCode.OK);
        var updated = await response.Content.ReadFromJsonAsync<ClinicResponse>();
        updated!.Name.Should().Be("Clínica Actualizada");
        updated.Address.Should().Be("Nueva Dirección 456");
    }

    [Fact]
    public async Task DeactivateClinic_Returns204AndIsNoLongerFound()
    {
        var client = await Client();
        var created = await CreateClinicAsync(client, "Clínica Temporal");

        var deleteResponse = await client.DeleteAsync($"{Base}/{created.Id}");
        deleteResponse.StatusCode.Should().Be(HttpStatusCode.NoContent);

        var getResponse = await client.GetAsync($"{Base}/{created.Id}");
        getResponse.StatusCode.Should().Be(HttpStatusCode.NotFound);
    }

    [Fact]
    public async Task ListClinics_WithoutToken_Returns401()
    {
        var response = await factory.CreateClient().GetAsync(Base);
        response.StatusCode.Should().Be(HttpStatusCode.Unauthorized);
    }

    private static async Task<ClinicResponse> CreateClinicAsync(HttpClient client, string name)
    {
        var response = await client.PostAsJsonAsync(Base,
            new CreateClinicRequest(name, $"Dirección de {name}"));
        response.EnsureSuccessStatusCode();
        return (await response.Content.ReadFromJsonAsync<ClinicResponse>())!;
    }
}
