using System.Security.Claims;
using HouseCenter.Api.Common.Results;
using HouseCenter.Api.Data;
using MediatR;
using Microsoft.EntityFrameworkCore;

namespace HouseCenter.Api.Features.Notifications;

public sealed record MarkAllReadCommand(ClaimsPrincipal Principal) : IRequest<Result>;

public sealed class MarkAllReadHandler(HouseCenterDbContext db)
    : IRequestHandler<MarkAllReadCommand, Result>
{
    public async Task<Result> Handle(MarkAllReadCommand request, CancellationToken ct)
    {
        var userId = Guid.Parse(request.Principal.FindFirstValue(ClaimTypes.NameIdentifier)!);
        var now = DateTime.UtcNow;

        // Set-based update: one UPDATE, no entity round trip.
        await db.Notifications
            .Where(n => n.RecipientUserId == userId && !n.IsRead)
            .ExecuteUpdateAsync(s => s
                .SetProperty(n => n.IsRead, true)
                .SetProperty(n => n.ReadAt, now), ct);

        return Result.Success();
    }
}
