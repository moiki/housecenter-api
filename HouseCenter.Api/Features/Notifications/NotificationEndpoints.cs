using System.Security.Claims;
using HouseCenter.Api.Common.Pagination;
using HouseCenter.Api.Common.Results;
using HouseCenter.Api.Common.Validation;
using MediatR;

namespace HouseCenter.Api.Features.Notifications;

public static class NotificationEndpoints
{
    public static IEndpointRouteBuilder MapNotificationEndpoints(this IEndpointRouteBuilder app)
    {
        // Every authenticated role reads their OWN notifications; scoping is by recipient,
        // not by role, so no policy beyond authentication is needed.
        var group = app.MapGroup("/notifications").WithTags("Notifications").RequireAuthorization();

        group.MapGet("/", async ([AsParameters] PageQuery paging, bool? unreadOnly,
                ClaimsPrincipal principal, IMediator mediator, CancellationToken ct) =>
                (await mediator.Send(new ListNotificationsQuery(
                    paging, unreadOnly ?? false, principal), ct)).ToHttpResult())
            .WithName("ListNotifications");

        group.MapGet("/unread-count", async (ClaimsPrincipal principal,
                IMediator mediator, CancellationToken ct) =>
                (await mediator.Send(new GetUnreadCountQuery(principal), ct)).ToHttpResult())
            .WithName("GetUnreadNotificationCount");

        group.MapPatch("/{id:guid}/read", async (Guid id, ClaimsPrincipal principal,
                IMediator mediator, CancellationToken ct) =>
                (await mediator.Send(new MarkNotificationReadCommand(id, principal), ct)).ToHttpResult())
            .WithName("MarkNotificationRead");

        group.MapPost("/read-all", async (ClaimsPrincipal principal,
                IMediator mediator, CancellationToken ct) =>
                (await mediator.Send(new MarkAllReadCommand(principal), ct)).ToHttpResult())
            .WithName("MarkAllNotificationsRead");

        // Push device-token registration (one per device install, owned by the caller)
        group.MapPost("/push-subscriptions", async (RegisterPushSubscriptionRequest request,
                ClaimsPrincipal principal, IMediator mediator, CancellationToken ct) =>
                (await mediator.Send(new RegisterPushSubscriptionCommand(
                    request.Token, request.Platform, principal), ct)).ToHttpResult())
            .WithValidation<RegisterPushSubscriptionRequest>()
            .WithName("RegisterPushSubscription");

        group.MapDelete("/push-subscriptions/{token}", async (string token,
                ClaimsPrincipal principal, IMediator mediator, CancellationToken ct) =>
                (await mediator.Send(new RemovePushSubscriptionCommand(token, principal), ct)).ToHttpResult())
            .WithName("RemovePushSubscription");

        return app;
    }
}
