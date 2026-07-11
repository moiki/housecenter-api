using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using FluentAssertions;
using HouseCenter.Api.Common.Pagination;
using HouseCenter.Api.Data;
using HouseCenter.Api.Domain.Entities;
using HouseCenter.Api.Features.Clinics;
using HouseCenter.Api.Features.WorkRoutes;
using Microsoft.Extensions.DependencyInjection;

namespace HouseCenter.Api.Tests;

[Collection("Api")]
public sealed class WorkRouteCrudTests(ApiFactory factory)
{
    // WorkRoutes stays UNVERSIONED — only Clinics is under /api/v1/.
    private const string Base = "/workroutes";

    private Task<HttpClient> Client() => factory.CreateAuthenticatedClientAsync();

    [Fact]
    public async Task CreateWorkRoute_IndefiniteRecurrence_Returns201WithNullEndDate()
    {
        var client = await Client();
        var clinic = await CreateClinicAsync(client);

        var response = await PostWorkRouteAsync(client, clinic.Id,
            recurrenceDays: ["Tuesday", "Thursday"], isIndefinite: true, endDate: null);

        response.StatusCode.Should().Be(HttpStatusCode.Created);
        var route = await response.Content.ReadFromJsonAsync<WorkRouteResponse>();
        route!.RecurrenceDays.Should().BeEquivalentTo(["Tuesday", "Thursday"]);
        route.IsRecurrenceIndefinite.Should().BeTrue();
        route.RecurrenceEndDate.Should().BeNull();
    }

    [Fact]
    public async Task CreateWorkRoute_FiniteRecurrence_Returns201WithEndOnOrAfterStart()
    {
        var client = await Client();
        var clinic = await CreateClinicAsync(client);
        var start = new DateOnly(2026, 8, 1);
        var end = new DateOnly(2026, 10, 1);

        var response = await PostWorkRouteAsync(client, clinic.Id,
            recurrenceDays: ["Monday"], isIndefinite: false, startDate: start, endDate: end);

        response.StatusCode.Should().Be(HttpStatusCode.Created);
        var route = await response.Content.ReadFromJsonAsync<WorkRouteResponse>();
        route!.IsRecurrenceIndefinite.Should().BeFalse();
        route.RecurrenceStartDate.Should().Be(start);
        route.RecurrenceEndDate.Should().Be(end);
    }

    [Fact]
    public async Task CreateWorkRoute_WithNoWeekdays_Returns400ValidationProblem()
    {
        var client = await Client();
        var clinic = await CreateClinicAsync(client);

        var response = await PostWorkRouteAsync(client, clinic.Id, recurrenceDays: []);

        response.StatusCode.Should().Be(HttpStatusCode.BadRequest);
    }

    [Fact]
    public async Task CreateWorkRoute_FiniteWithoutEndDate_Returns400ValidationProblem()
    {
        var client = await Client();
        var clinic = await CreateClinicAsync(client);

        var response = await PostWorkRouteAsync(client, clinic.Id, isIndefinite: false, endDate: null);

        response.StatusCode.Should().Be(HttpStatusCode.BadRequest);
    }

    [Fact]
    public async Task CreateWorkRoute_EndDateBeforeStartDate_Returns400ValidationProblem()
    {
        var client = await Client();
        var clinic = await CreateClinicAsync(client);

        var response = await PostWorkRouteAsync(client, clinic.Id, isIndefinite: false,
            startDate: new DateOnly(2026, 8, 1), endDate: new DateOnly(2026, 7, 1));

        response.StatusCode.Should().Be(HttpStatusCode.BadRequest);
    }

    [Fact]
    public async Task CreateWorkRoute_IndefiniteWithEndDateSet_Returns400ValidationProblem()
    {
        var client = await Client();
        var clinic = await CreateClinicAsync(client);

        var response = await PostWorkRouteAsync(client, clinic.Id, isIndefinite: true,
            endDate: new DateOnly(2026, 10, 1));

        response.StatusCode.Should().Be(HttpStatusCode.BadRequest);
    }

    [Fact]
    public async Task CreateWorkRoute_WithInvalidWeekdayName_Returns400ValidationProblem()
    {
        var client = await Client();
        var clinic = await CreateClinicAsync(client);

        var response = await PostWorkRouteAsync(client, clinic.Id, recurrenceDays: ["Funday"]);

        response.StatusCode.Should().Be(HttpStatusCode.BadRequest);
    }

    [Fact]
    public async Task UpdateWorkRoute_IndefiniteSwitchedToFinite_Returns200()
    {
        var client = await Client();
        var clinic = await CreateClinicAsync(client);
        var created = await CreateWorkRouteAsync(client, clinic.Id, isIndefinite: true, endDate: null);
        var start = created.RecurrenceStartDate;
        var end = start.AddMonths(2);

        var response = await client.PutAsJsonAsync($"{Base}/{created.Id}",
            BuildUpdateRequest(created, isIndefinite: false, startDate: start, endDate: end));

        response.StatusCode.Should().Be(HttpStatusCode.OK);
        var updated = await response.Content.ReadFromJsonAsync<WorkRouteResponse>();
        updated!.IsRecurrenceIndefinite.Should().BeFalse();
        updated.RecurrenceEndDate.Should().Be(end);
    }

    [Fact]
    public async Task UpdateWorkRoute_FiniteSwitchedToIndefinite_EndDateBecomesNull()
    {
        var client = await Client();
        var clinic = await CreateClinicAsync(client);
        var start = new DateOnly(2026, 8, 1);
        var created = await CreateWorkRouteAsync(client, clinic.Id,
            isIndefinite: false, startDate: start, endDate: start.AddMonths(2));

        var response = await client.PutAsJsonAsync($"{Base}/{created.Id}",
            BuildUpdateRequest(created, isIndefinite: true, startDate: start, endDate: null));

        response.StatusCode.Should().Be(HttpStatusCode.OK);
        var updated = await response.Content.ReadFromJsonAsync<WorkRouteResponse>();
        updated!.IsRecurrenceIndefinite.Should().BeTrue();
        updated.RecurrenceEndDate.Should().BeNull();
    }

    [Fact]
    public async Task UpdateWorkRoute_EmptyWeekdays_Returns200()
    {
        // D9: UPDATE does not require >=1 weekday (unlike CREATE) — legacy scheduleless
        // routes stay editable without being forced to schedule.
        var client = await Client();
        var clinic = await CreateClinicAsync(client);
        var created = await CreateWorkRouteAsync(client, clinic.Id, recurrenceDays: ["Monday"]);

        var response = await client.PutAsJsonAsync($"{Base}/{created.Id}",
            BuildUpdateRequest(created,
                startDate: created.RecurrenceStartDate,
                endDate: created.RecurrenceEndDate,
                isIndefinite: created.IsRecurrenceIndefinite,
                recurrenceDays: []));

        response.StatusCode.Should().Be(HttpStatusCode.OK);
        var updated = await response.Content.ReadFromJsonAsync<WorkRouteResponse>();
        updated!.RecurrenceDays.Should().BeEmpty();
    }

    [Fact]
    public async Task WorkRouteResponse_SerializesRecurrenceDaysAsPascalCaseArrayAndDatesAsIsoStrings()
    {
        var client = await Client();
        var clinic = await CreateClinicAsync(client);
        var created = await CreateWorkRouteAsync(client, clinic.Id,
            recurrenceDays: ["Tuesday", "Thursday"], isIndefinite: false,
            startDate: new DateOnly(2026, 8, 1), endDate: new DateOnly(2026, 10, 1));

        var response = await client.GetAsync($"{Base}/{created.Id}");
        var json = await response.Content.ReadAsStringAsync();
        using var doc = JsonDocument.Parse(json);
        var root = doc.RootElement;

        root.GetProperty("recurrenceDays").EnumerateArray()
            .Select(e => e.GetString())
            .Should().Equal("Tuesday", "Thursday");
        root.GetProperty("recurrenceStartDate").GetString().Should().Be("2026-08-01");
        root.GetProperty("recurrenceEndDate").GetString().Should().Be("2026-10-01");
    }

    [Fact]
    public async Task GetWorkRoute_WithUnknownId_Returns404()
    {
        var client = await Client();
        var response = await client.GetAsync($"{Base}/{Guid.NewGuid()}");
        response.StatusCode.Should().Be(HttpStatusCode.NotFound);
    }

    [Fact]
    public async Task ListWorkRoutes_WithoutToken_Returns401()
    {
        var response = await factory.CreateClient().GetAsync(Base);
        response.StatusCode.Should().Be(HttpStatusCode.Unauthorized);
    }

    [Fact]
    public async Task ListWorkRoutes_DefaultPaging_ReturnsEnvelopeWithPageOneAndSizeTwenty()
    {
        var client = await Client();
        var clinic = await CreateClinicAsync(client);
        await CreateWorkRouteAsync(client, clinic.Id);

        var response = await client.GetAsync(Base);

        response.StatusCode.Should().Be(HttpStatusCode.OK);
        var paged = await response.Content.ReadFromJsonAsync<PagedResult<WorkRouteResponse>>();
        paged!.Items.Should().NotBeEmpty();
        paged.Page.Should().Be(1);
        paged.PageSize.Should().Be(20);
        paged.TotalCount.Should().BeGreaterThanOrEqualTo(1);
    }

    [Fact]
    public async Task ListWorkRoutes_Page2_ReturnsNextSlice()
    {
        var client = await Client();
        var clinic = await CreateClinicAsync(client);
        for (var i = 0; i < 3; i++)
            await CreateWorkRouteAsync(client, clinic.Id);

        var page1 = await (await client.GetAsync($"{Base}?page=1&pageSize=1"))
            .Content.ReadFromJsonAsync<PagedResult<WorkRouteResponse>>();
        var page2 = await (await client.GetAsync($"{Base}?page=2&pageSize=1"))
            .Content.ReadFromJsonAsync<PagedResult<WorkRouteResponse>>();

        page1!.Items.Should().HaveCount(1);
        page2!.Items.Should().HaveCount(1);
        page2.Page.Should().Be(2);
        page1.Items[0].Id.Should().NotBe(page2.Items[0].Id);
    }

    [Fact]
    public async Task ListWorkRoutes_PageSizeAbove100_ClampsTo100()
    {
        var client = await Client();

        var response = await client.GetAsync($"{Base}?page=1&pageSize=500");

        response.StatusCode.Should().Be(HttpStatusCode.OK);
        var paged = await response.Content.ReadFromJsonAsync<PagedResult<WorkRouteResponse>>();
        paged!.PageSize.Should().Be(100);
    }

    [Fact]
    public async Task ListWorkRoutes_AfterPaginate_WeekdayEnumStillMapsToStringArrayInMemory()
    {
        // Regression guard for the WorkRoutes pagination nuance: the entity IQueryable is
        // paginated FIRST (Skip/Take over WorkRoute), THEN the weekday Flags enum is expanded
        // to a string[] in memory — if that ordering were reversed (or done inside .Select),
        // this would throw at runtime ("could not be translated") instead of silently mismapping.
        var client = await Client();
        var clinic = await CreateClinicAsync(client);
        await CreateWorkRouteAsync(client, clinic.Id, recurrenceDays: ["Tuesday", "Thursday"]);

        var response = await client.GetAsync($"{Base}?page=1&pageSize=20");

        response.StatusCode.Should().Be(HttpStatusCode.OK);
        var paged = await response.Content.ReadFromJsonAsync<PagedResult<WorkRouteResponse>>();
        paged!.Items.Should().Contain(w =>
            w.RecurrenceDays.SequenceEqual(new[] { "Tuesday", "Thursday" }));
    }

    [Fact]
    public async Task GetWorkRoute_LegacyBackfilledRow_HasEmptyWeekdaysAndIsIndefinite()
    {
        // Simulates a pre-migration row after the AddWorkRouteRecurrence backfill: seeded
        // directly through the DbContext (bypassing the API/validators, which now hard-require
        // >=1 weekday on create) so RecurrenceDays/IsRecurrenceIndefinite/RecurrenceEndDate keep
        // their entity defaults (None / true / null) — exactly what the migration's column
        // defaults + raw-SQL backfill produce for existing rows.
        var client = await Client();
        var clinic = await CreateClinicAsync(client);
        var expectedStartDate = DateOnly.FromDateTime(DateTime.UtcNow);

        using var scope = factory.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<HouseCenterDbContext>();
        var legacyRoute = new WorkRoute
        {
            RouteName = "Ruta heredada",
            Description = "Ruta previa a la migración de recurrencia",
            ClinicId = clinic.Id,
            RecurrenceStartDate = expectedStartDate
        };
        db.WorkRoutes.Add(legacyRoute);
        await db.SaveChangesAsync();

        var response = await client.GetAsync($"{Base}/{legacyRoute.Id}");

        response.StatusCode.Should().Be(HttpStatusCode.OK);
        var route = await response.Content.ReadFromJsonAsync<WorkRouteResponse>();
        route!.RecurrenceDays.Should().BeEmpty();
        route.IsRecurrenceIndefinite.Should().BeTrue();
        route.RecurrenceEndDate.Should().BeNull();
        route.RecurrenceStartDate.Should().Be(expectedStartDate);
    }

    private static async Task<ClinicResponse> CreateClinicAsync(HttpClient client)
    {
        var response = await client.PostAsJsonAsync("/api/v1/clinics",
            new CreateClinicRequest($"Clínica {Guid.NewGuid():N}"[..25], "Calle Central"));
        response.EnsureSuccessStatusCode();
        return (await response.Content.ReadFromJsonAsync<ClinicResponse>())!;
    }

    private static Task<HttpResponseMessage> PostWorkRouteAsync(
        HttpClient client,
        Guid clinicId,
        IReadOnlyList<string>? recurrenceDays = null,
        DateOnly? startDate = null,
        DateOnly? endDate = null,
        bool isIndefinite = true)
    {
        var request = new CreateWorkRouteRequest(
            $"Ruta {Guid.NewGuid():N}"[..20],
            "Ruta de prueba",
            null,
            clinicId,
            [],
            recurrenceDays ?? ["Tuesday"],
            startDate ?? DateOnly.FromDateTime(DateTime.UtcNow),
            endDate,
            isIndefinite);

        return client.PostAsJsonAsync(Base, request);
    }

    private static async Task<WorkRouteResponse> CreateWorkRouteAsync(
        HttpClient client,
        Guid clinicId,
        IReadOnlyList<string>? recurrenceDays = null,
        DateOnly? startDate = null,
        DateOnly? endDate = null,
        bool isIndefinite = true)
    {
        var response = await PostWorkRouteAsync(client, clinicId, recurrenceDays, startDate, endDate, isIndefinite);
        response.EnsureSuccessStatusCode();
        return (await response.Content.ReadFromJsonAsync<WorkRouteResponse>())!;
    }

    // startDate/endDate/isIndefinite are required (not defaulted via `??`) because `endDate`
    // is a DateOnly? whose valid target state IS null — a `??` fallback couldn't distinguish
    // "explicitly clear it" from "not provided", so every caller states its recurrence window
    // in full.
    private static UpdateWorkRouteRequest BuildUpdateRequest(
        WorkRouteResponse current,
        DateOnly startDate,
        DateOnly? endDate,
        bool isIndefinite,
        IReadOnlyList<string>? recurrenceDays = null) => new(
        current.RouteName,
        current.Description,
        current.FeaturedImage,
        [],
        recurrenceDays ?? current.RecurrenceDays,
        startDate,
        endDate,
        isIndefinite);
}
