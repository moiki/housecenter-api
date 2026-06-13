using System.Security.Claims;
using HouseCenter.Api.Common.Results;
using HouseCenter.Api.Domain.Entities;
using MediatR;

namespace HouseCenter.Api.Features.Attachments;

public static class AttachmentEndpoints
{
    public static IEndpointRouteBuilder MapAttachmentEndpoints(this IEndpointRouteBuilder app)
    {
        var group = app.MapGroup("/attachments").WithTags("Attachments").RequireAuthorization();

        // Multipart upload. ownerType/ownerId come from the query string; the file is the body.
        // DisableAntiforgery: this is a token-authenticated API with no cookie/antiforgery flow.
        group.MapPost("/", async (IFormFile file, AttachmentOwnerType ownerType, Guid ownerId,
                ClaimsPrincipal principal, IMediator mediator, CancellationToken ct) =>
            {
                await using var stream = file.OpenReadStream();
                var result = await mediator.Send(new UploadAttachmentCommand(
                    ownerType, ownerId, stream, file.FileName, file.ContentType, file.Length, principal), ct);
                return result.Match(
                    created => Results.Created(created.DownloadUrl, created),
                    error => error.ToProblem());
            })
            .DisableAntiforgery()
            .WithName("UploadAttachment");

        group.MapGet("/", async (AttachmentOwnerType ownerType, Guid ownerId,
                ClaimsPrincipal principal, IMediator mediator, CancellationToken ct) =>
                (await mediator.Send(new ListAttachmentsQuery(ownerType, ownerId, principal), ct)).ToHttpResult())
            .WithName("ListAttachments");

        group.MapGet("/{id:guid}", async (Guid id, ClaimsPrincipal principal,
                IMediator mediator, CancellationToken ct) =>
            {
                var result = await mediator.Send(new GetAttachmentContentQuery(id, principal), ct);
                return result.Match(
                    content => Results.File(content.Stream, content.ContentType, content.FileName),
                    error => error.ToProblem());
            })
            .WithName("DownloadAttachment");

        group.MapDelete("/{id:guid}", async (Guid id, ClaimsPrincipal principal,
                IMediator mediator, CancellationToken ct) =>
                (await mediator.Send(new DeleteAttachmentCommand(id, principal), ct)).ToHttpResult())
            .WithName("DeleteAttachment");

        return app;
    }
}
