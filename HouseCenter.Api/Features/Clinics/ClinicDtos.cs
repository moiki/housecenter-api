namespace HouseCenter.Api.Features.Clinics;

public sealed record ClinicResponse(Guid Id, string Name, string Address, bool IsActive);

public sealed record CreateClinicRequest(string Name, string Address);

public sealed record UpdateClinicRequest(string Name, string Address);
