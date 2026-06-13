using System.Security.Claims;
using HouseCenter.Api.Common.Results;
using HouseCenter.Api.Data;
using MediatR;
using Microsoft.EntityFrameworkCore;

namespace HouseCenter.Api.Features.Notifications;

public sealed record RemovePushSubscriptionCommand(string Token, ClaimsPrincipal Principal)
    : IRequest<Result>;

public sealed class RemovePushSubscriptionHandler(HouseCenterDbContext db)
    : IRequestHandler<RemovePushSubscriptionCommand, Result>
{
    public async Task<Result> Handle(RemovePushSubscriptionCommand request, CancellationToken ct)
    {
        var userId = Guid.Parse(request.Principal.FindFirstValue(ClaimTypes.NameIdentifier)!);

        // Scoped to the caller's own token — idempotent (deleting a non-existent token is a no-op).
        await db.PushSubscriptions
            .Where(s => s.Token == request.Token && s.OwnerUserId == userId)
            .ExecuteDeleteAsync(ct);

        return Result.Success();
    }
}
