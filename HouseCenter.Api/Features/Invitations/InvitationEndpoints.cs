using HouseCenter.Api.Common.Pagination;
using HouseCenter.Api.Common.Results;
using HouseCenter.Api.Common.Validation;
using HouseCenter.Api.Domain.Constants;

namespace HouseCenter.Api.Features.Invitations;

public static class InvitationEndpoints
{
    public static IEndpointRouteBuilder MapInvitationEndpoints(this IEndpointRouteBuilder app)
    {
        var group = app.MapGroup("/invitations").WithTags("Invitations");

        group.MapGet("/", async ([AsParameters] PageQuery paging, IInvitationService invitations, CancellationToken ct) =>
                (await invitations.ListAsync(paging, ct)).ToHttpResult())
            .RequireAuthorization(PolicyNames.AdministratorOrAbove)
            .WithName("ListInvitations");

        group.MapPost("/", async (CreateInvitationRequest request, IInvitationService invitations, CancellationToken ct) =>
            {
                var result = await invitations.CreateAsync(request, ct);
                return result.Match(
                    created => Results.Created($"/invitations/{created.Id}", created),
                    error => error.ToProblem());
            })
            .RequireAuthorization(PolicyNames.AdministratorOrAbove)
            .WithValidation<CreateInvitationRequest>()
            .WithName("CreateInvitation");

        group.MapPost("/{id:guid}/resend", async (Guid id, IInvitationService invitations, CancellationToken ct) =>
                (await invitations.ResendAsync(id, ct)).ToHttpResult())
            .RequireAuthorization(PolicyNames.AdministratorOrAbove)
            .WithName("ResendInvitation");

        group.MapDelete("/{id:guid}", async (Guid id, IInvitationService invitations, CancellationToken ct) =>
                (await invitations.DeleteAsync(id, ct)).ToHttpResult())
            .RequireAuthorization(PolicyNames.AdministratorOrAbove)
            .WithName("DeleteInvitation");

        // Anonymous on purpose: the recipient is not a user yet
        group.MapGet("/validate", async (string token, IInvitationService invitations, CancellationToken ct) =>
                (await invitations.ValidateAsync(token, ct)).ToHttpResult())
            .WithName("ValidateInvitation");

        return app;
    }
}
