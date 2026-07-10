namespace HouseCenter.Api.Domain.Entities;

/// <summary>
/// Days of the week a WorkRoute recurs on. Flags so a route can carry any combination
/// (e.g. "Tuesday, Thursday") as a single scalar column via EF's string conversion —
/// see WorkRouteConfiguration.
/// </summary>
[Flags]
public enum Weekday
{
    None = 0,
    Monday = 1,
    Tuesday = 2,
    Wednesday = 4,
    Thursday = 8,
    Friday = 16,
    Saturday = 32,
    Sunday = 64
}
