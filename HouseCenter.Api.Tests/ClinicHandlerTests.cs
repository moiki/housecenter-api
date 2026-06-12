using FluentAssertions;
using HouseCenter.Api.Common.Persistence;
using HouseCenter.Api.Common.Results;
using HouseCenter.Api.Data;
using HouseCenter.Api.Domain.Entities;
using HouseCenter.Api.Features.Clinics.Commands;
using Microsoft.EntityFrameworkCore;

namespace HouseCenter.Api.Tests;

// Unit tests for CQRS handlers — in-memory EF provider, no HTTP, no WebApplicationFactory.
// Faster than integration tests and fully isolated: each test gets its own DbContext.
public sealed class ClinicHandlerTests : IDisposable
{
    private readonly HouseCenterDbContext _db;
    private readonly EfRepository<Clinic> _repo;

    public ClinicHandlerTests()
    {
        var options = new DbContextOptionsBuilder<HouseCenterDbContext>()
            .UseInMemoryDatabase(Guid.NewGuid().ToString()) // unique DB per test
            .Options;

        _db = new HouseCenterDbContext(options);
        _repo = new EfRepository<Clinic>(_db);
    }

    public void Dispose() => _db.Dispose();

    // ─── CreateClinicHandler ──────────────────────────────────────────────────

    [Fact]
    public async Task CreateClinic_WhenNameAvailable_ReturnsSuccess()
    {
        var handler = new CreateClinicHandler(_repo);

        var result = await handler.Handle(
            new CreateClinicCommand("Clínica Test", "Dirección Test"),
            CancellationToken.None);

        result.IsSuccess.Should().BeTrue();
        result.Value.Name.Should().Be("Clínica Test");
        _db.Set<Clinic>().Should().HaveCount(1);
    }

    [Fact]
    public async Task CreateClinic_WhenNameTaken_ReturnsConflict()
    {
        _db.Set<Clinic>().Add(new Clinic { Name = "Clínica Existente", Address = "Dir", IsActive = true });
        await _db.SaveChangesAsync();

        var handler = new CreateClinicHandler(_repo);
        var result = await handler.Handle(
            new CreateClinicCommand("Clínica Existente", "Cualquier dirección"),
            CancellationToken.None);

        result.IsFailure.Should().BeTrue();
        result.Error.Type.Should().Be(ErrorType.Conflict);
        _db.Set<Clinic>().Should().HaveCount(1); // no new entity added
    }

    // ─── UpdateClinicHandler ──────────────────────────────────────────────────

    [Fact]
    public async Task UpdateClinic_WhenExists_UpdatesFields()
    {
        var clinic = new Clinic { Name = "Viejo", Address = "Vieja Dir", IsActive = true };
        _db.Set<Clinic>().Add(clinic);
        await _db.SaveChangesAsync();

        var handler = new UpdateClinicHandler(_repo);
        var result = await handler.Handle(
            new UpdateClinicCommand(clinic.Id, "Nuevo", "Nueva Dir"),
            CancellationToken.None);

        result.IsSuccess.Should().BeTrue();
        result.Value.Name.Should().Be("Nuevo");
        result.Value.Address.Should().Be("Nueva Dir");
    }

    [Fact]
    public async Task UpdateClinic_WhenNotFound_ReturnsNotFound()
    {
        var handler = new UpdateClinicHandler(_repo);

        var result = await handler.Handle(
            new UpdateClinicCommand(Guid.NewGuid(), "Nuevo", "Dir"),
            CancellationToken.None);

        result.IsFailure.Should().BeTrue();
        result.Error.Type.Should().Be(ErrorType.NotFound);
    }

    // ─── DeactivateClinicHandler ──────────────────────────────────────────────

    [Fact]
    public async Task DeactivateClinic_WhenExists_SetsIsActiveFalse()
    {
        var clinic = new Clinic { Name = "Activa", Address = "Dir", IsActive = true };
        _db.Set<Clinic>().Add(clinic);
        await _db.SaveChangesAsync();

        var handler = new DeactivateClinicHandler(_repo);
        var result = await handler.Handle(
            new DeactivateClinicCommand(clinic.Id),
            CancellationToken.None);

        result.IsSuccess.Should().BeTrue();
        var stored = await _db.Set<Clinic>().IgnoreQueryFilters().FirstAsync(c => c.Id == clinic.Id);
        stored.IsActive.Should().BeFalse();
    }

    [Fact]
    public async Task DeactivateClinic_WhenNotFound_ReturnsNotFound()
    {
        var handler = new DeactivateClinicHandler(_repo);

        var result = await handler.Handle(
            new DeactivateClinicCommand(Guid.NewGuid()),
            CancellationToken.None);

        result.IsFailure.Should().BeTrue();
        result.Error.Type.Should().Be(ErrorType.NotFound);
    }
}
