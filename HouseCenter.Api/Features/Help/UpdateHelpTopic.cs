using HouseCenter.Api.Common.Results;
using HouseCenter.Api.Data;
using HouseCenter.Api.Domain.Entities;
using MediatR;
using Microsoft.EntityFrameworkCore;

namespace HouseCenter.Api.Features.Help;

public sealed record UpdateHelpTopicCommand(
    Guid Id, string Title, string Summary, string Body,
    HelpAudience Audience, string Category, int DisplayOrder) : IRequest<Result<HelpTopicResponse>>;

public sealed class UpdateHelpTopicHandler(HouseCenterDbContext db)
    : IRequestHandler<UpdateHelpTopicCommand, Result<HelpTopicResponse>>
{
    private static readonly Error NotFound = Error.NotFound("help.topic_not_found", "Help topic not found");

    public async Task<Result<HelpTopicResponse>> Handle(UpdateHelpTopicCommand request, CancellationToken ct)
    {
        // The normal (tracked) query already applies the global IsActive filter, so updating a
        // soft-deleted topic behaves like updating a nonexistent one (404) — R6.
        var topic = await db.HelpTopics.SingleOrDefaultAsync(t => t.Id == request.Id, ct);
        if (topic is null) return NotFound;

        // TopicKey is never touched here — UpdateHelpTopicCommand/Request carry no TopicKey
        // field at all, so the persisted key structurally cannot change (R1).
        topic.Title = request.Title;
        topic.Summary = request.Summary;
        topic.Body = request.Body;
        topic.Audience = request.Audience;
        topic.Category = request.Category;
        topic.DisplayOrder = request.DisplayOrder;

        await db.SaveChangesAsync(ct);

        return new HelpTopicResponse(
            topic.Id, topic.TopicKey, topic.Title, topic.Summary, topic.Body,
            topic.Audience.ToString(), topic.Category, topic.DisplayOrder, topic.UpdatedDate);
    }
}
