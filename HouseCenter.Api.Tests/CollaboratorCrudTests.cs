using System.Net;
using System.Net.Http.Json;
using FluentAssertions;
using HouseCenter.Api.Common.Pagination;
using HouseCenter.Api.Features.Clinics;
using HouseCenter.Api.Features.Collaborators;

namespace HouseCenter.Api.Tests;

[Collection("Api")]
public sealed class CollaboratorCrudTests(ApiFactory factory)
{
    // Collaborators stays UNVERSIONED — only Clinics is under /api/v1/.
    private const string Base = "/collaborators";

    private Task<HttpClient> Client() => factory.CreateAuthenticatedClientAsync();

    [Fact]
    public async Task CreateCollaborator_WithValidData_Returns201AndPersistedCollaborator()
    {
        var client = await Client();
        var clinic = await CreateClinicAsync(client);

        var response = await PostCollaboratorAsync(client, clinic.Id, "Ana", "Gómez");

        response.StatusCode.Should().Be(HttpStatusCode.Created);
        var collaborator = await response.Content.ReadFromJsonAsync<CollaboratorResponse>();
        collaborator!.FirstName.Should().Be("Ana");
        collaborator.ClinicId.Should().Be(clinic.Id);
        collaborator.IsActive.Should().BeTrue();
    }

    [Fact]
    public async Task CreateCollaborator_WithUnknownClinic_Returns404()
    {
        var client = await Client();

        var response = await PostCollaboratorAsync(client, Guid.NewGuid(), "Luis", "Ramírez");

        response.StatusCode.Should().Be(HttpStatusCode.NotFound);
    }

    [Fact]
    public async Task GetCollaborator_AfterCreate_ReturnsIt()
    {
        var client = await Client();
        var clinic = await CreateClinicAsync(client);
        var created = await CreateCollaboratorAsync(client, clinic.Id, "Sofía", "Ríos");

        var response = await client.GetAsync($"{Base}/{created.Id}");

        response.StatusCode.Should().Be(HttpStatusCode.OK);
        var collaborator = await response.Content.ReadFromJsonAsync<CollaboratorResponse>();
        collaborator!.Id.Should().Be(created.Id);
        collaborator.LastName.Should().Be("Ríos");
    }

    [Fact]
    public async Task GetCollaborator_WithUnknownId_Returns404()
    {
        var client = await Client();
        var response = await client.GetAsync($"{Base}/{Guid.NewGuid()}");
        response.StatusCode.Should().Be(HttpStatusCode.NotFound);
    }

    [Fact]
    public async Task DeactivateCollaborator_Returns204AndIsNoLongerFound()
    {
        var client = await Client();
        var clinic = await CreateClinicAsync(client);
        var created = await CreateCollaboratorAsync(client, clinic.Id, "Temporal", "Test");

        var deleteResponse = await client.DeleteAsync($"{Base}/{created.Id}");
        deleteResponse.StatusCode.Should().Be(HttpStatusCode.NoContent);

        var getResponse = await client.GetAsync($"{Base}/{created.Id}");
        getResponse.StatusCode.Should().Be(HttpStatusCode.NotFound);
    }

    [Fact]
    public async Task ListCollaborators_WithoutToken_Returns401()
    {
        var response = await factory.CreateClient().GetAsync(Base);
        response.StatusCode.Should().Be(HttpStatusCode.Unauthorized);
    }

    [Fact]
    public async Task ListCollaborators_DefaultPaging_ReturnsEnvelopeWithPageOneAndSizeTwenty()
    {
        var client = await Client();
        var clinic = await CreateClinicAsync(client);
        await CreateCollaboratorAsync(client, clinic.Id, "Paginado", "Uno");

        var response = await client.GetAsync(Base);

        response.StatusCode.Should().Be(HttpStatusCode.OK);
        var paged = await response.Content.ReadFromJsonAsync<PagedResult<CollaboratorResponse>>();
        paged!.Items.Should().NotBeEmpty();
        paged.Page.Should().Be(1);
        paged.PageSize.Should().Be(20);
        paged.TotalCount.Should().BeGreaterThanOrEqualTo(1);
    }

    [Fact]
    public async Task ListCollaborators_Page2_ReturnsNextSlice()
    {
        var client = await Client();
        var clinic = await CreateClinicAsync(client);
        for (var i = 0; i < 3; i++)
            await CreateCollaboratorAsync(client, clinic.Id, $"Slice{i}", $"Test{Guid.NewGuid():N}"[..10]);

        var page1 = await (await client.GetAsync($"{Base}?page=1&pageSize=1"))
            .Content.ReadFromJsonAsync<PagedResult<CollaboratorResponse>>();
        var page2 = await (await client.GetAsync($"{Base}?page=2&pageSize=1"))
            .Content.ReadFromJsonAsync<PagedResult<CollaboratorResponse>>();

        page1!.Items.Should().HaveCount(1);
        page2!.Items.Should().HaveCount(1);
        page2.Page.Should().Be(2);
        page1.Items[0].Id.Should().NotBe(page2.Items[0].Id);
    }

    [Fact]
    public async Task ListCollaborators_PageSizeAbove100_ClampsTo100()
    {
        var client = await Client();

        var response = await client.GetAsync($"{Base}?page=1&pageSize=500");

        response.StatusCode.Should().Be(HttpStatusCode.OK);
        var paged = await response.Content.ReadFromJsonAsync<PagedResult<CollaboratorResponse>>();
        paged!.PageSize.Should().Be(100);
    }

    private static async Task<ClinicResponse> CreateClinicAsync(HttpClient client)
    {
        var response = await client.PostAsJsonAsync("/api/v1/clinics",
            new CreateClinicRequest($"Clínica {Guid.NewGuid():N}"[..25], "Calle Central"));
        response.EnsureSuccessStatusCode();
        return (await response.Content.ReadFromJsonAsync<ClinicResponse>())!;
    }

    private static Task<HttpResponseMessage> PostCollaboratorAsync(
        HttpClient client, Guid clinicId, string firstName, string lastName) =>
        client.PostAsJsonAsync(Base, new CreateCollaboratorRequest(
            firstName, lastName, $"colab-{Guid.NewGuid():N}@test.local", "8888-8888",
            "Dirección colaborador", "Nicaragua", null, "Managua", null,
            clinicId, null, ["Fisioterapeuta"]));

    private static async Task<CollaboratorResponse> CreateCollaboratorAsync(
        HttpClient client, Guid clinicId, string firstName, string lastName)
    {
        var response = await PostCollaboratorAsync(client, clinicId, firstName, lastName);
        response.EnsureSuccessStatusCode();
        return (await response.Content.ReadFromJsonAsync<CollaboratorResponse>())!;
    }
}
