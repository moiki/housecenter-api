using System.Security.Claims;
using HouseCenter.Api.Common.Pagination;
using HouseCenter.Api.Common.Results;
using HouseCenter.Api.Data;
using MediatR;
using Microsoft.EntityFrameworkCore;

namespace HouseCenter.Api.Features.Notifications;

public sealed record ListNotificationsQuery(
    PageQuery Paging,
    bool UnreadOnly,
    ClaimsPrincipal Principal) : IRequest<Result<PagedResult<NotificationResponse>>>;

public sealed class ListNotificationsHandler(HouseCenterDbContext db)
    : IRequestHandler<ListNotificationsQuery, Result<PagedResult<NotificationResponse>>>
{
    public async Task<Result<PagedResult<NotificationResponse>>> Handle(
        ListNotificationsQuery request, CancellationToken ct)
    {
        var userId = Guid.Parse(request.Principal.FindFirstValue(ClaimTypes.NameIdentifier)!);

        var query = db.Notifications
            .AsNoTracking()
            .Where(n => n.RecipientUserId == userId);

        if (request.UnreadOnly)
            query = query.Where(n => !n.IsRead);

        var projected = query
            .OrderByDescending(n => n.CreatedDate)
            .Select(n => new NotificationResponse(
                n.Id, n.Type.ToString(), n.Title, n.Body,
                n.ReferenceType, n.ReferenceId, n.IsRead, n.ReadAt, n.CreatedDate));

        return await projected.ToPagedResultAsync(request.Paging, ct);
    }
}
