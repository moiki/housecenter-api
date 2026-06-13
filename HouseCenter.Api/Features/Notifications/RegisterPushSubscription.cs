using System.Security.Claims;
using HouseCenter.Api.Common.Results;
using HouseCenter.Api.Data;
using HouseCenter.Api.Domain.Entities;
using MediatR;
using Microsoft.EntityFrameworkCore;

namespace HouseCenter.Api.Features.Notifications;

public sealed record RegisterPushSubscriptionCommand(
    string Token,
    PushPlatform Platform,
    ClaimsPrincipal Principal) : IRequest<Result>;

public sealed class RegisterPushSubscriptionHandler(HouseCenterDbContext db)
    : IRequestHandler<RegisterPushSubscriptionCommand, Result>
{
    public async Task<Result> Handle(RegisterPushSubscriptionCommand request, CancellationToken ct)
    {
        var userId = Guid.Parse(request.Principal.FindFirstValue(ClaimTypes.NameIdentifier)!);

        // Upsert on the token: the same device may re-register (token refresh) or change
        // owner (logout → another user logs in on that phone).
        var existing = await db.PushSubscriptions
            .SingleOrDefaultAsync(s => s.Token == request.Token, ct);

        if (existing is null)
        {
            db.PushSubscriptions.Add(new PushSubscription
            {
                OwnerUserId = userId,
                Token = request.Token,
                Platform = request.Platform
            });
        }
        else
        {
            existing.OwnerUserId = userId;
            existing.Platform = request.Platform;
        }

        await db.SaveChangesAsync(ct);
        return Result.Success();
    }
}
