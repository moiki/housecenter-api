using System.Security.Claims;
using HouseCenter.Api.Common.Results;
using HouseCenter.Api.Data;
using MediatR;
using Microsoft.EntityFrameworkCore;

namespace HouseCenter.Api.Features.Notifications;

public sealed record GetUnreadCountQuery(ClaimsPrincipal Principal)
    : IRequest<Result<UnreadCountResponse>>;

public sealed class GetUnreadCountHandler(HouseCenterDbContext db)
    : IRequestHandler<GetUnreadCountQuery, Result<UnreadCountResponse>>
{
    public async Task<Result<UnreadCountResponse>> Handle(
        GetUnreadCountQuery request, CancellationToken ct)
    {
        var userId = Guid.Parse(request.Principal.FindFirstValue(ClaimTypes.NameIdentifier)!);

        var count = await db.Notifications
            .AsNoTracking()
            .CountAsync(n => n.RecipientUserId == userId && !n.IsRead, ct);

        return new UnreadCountResponse(count);
    }
}
