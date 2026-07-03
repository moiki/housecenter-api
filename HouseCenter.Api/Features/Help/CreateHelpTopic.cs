using HouseCenter.Api.Common.Results;
using HouseCenter.Api.Data;
using HouseCenter.Api.Domain.Entities;
using MediatR;
using Microsoft.EntityFrameworkCore;

namespace HouseCenter.Api.Features.Help;

public sealed record CreateHelpTopicCommand(
    string TopicKey, string Title, string Summary, string Body,
    HelpAudience Audience, string Category, int DisplayOrder) : IRequest<Result<HelpTopicResponse>>;

public sealed class CreateHelpTopicHandler(HouseCenterDbContext db)
    : IRequestHandler<CreateHelpTopicCommand, Result<HelpTopicResponse>>
{
    public async Task<Result<HelpTopicResponse>> Handle(CreateHelpTopicCommand request, CancellationToken ct)
    {
        // TopicKey must be unique across EVERY row, including soft-deleted ones (R1/R5) —
        // IgnoreQueryFilters() bypasses the global IsActive filter so a re-used key is caught
        // here with a friendly 409 instead of hitting the unique index and raising a raw DB
        // error. The message distinguishes "already exists" (active) from "exists but
        // unpublished" (soft-deleted) so an admin knows whether to pick a new key or restore.
        var existing = await db.HelpTopics
            .IgnoreQueryFilters()
            .Where(t => t.TopicKey == request.TopicKey)
            .Select(t => new { t.IsActive })
            .SingleOrDefaultAsync(ct);

        if (existing is not null)
        {
            return existing.IsActive
                ? Error.Conflict("help.topic_key_taken", "A help topic with this key already exists")
                : Error.Conflict("help.topic_key_unpublished", "A help topic with this key exists but is unpublished");
        }

        var topic = new HelpTopic
        {
            TopicKey = request.TopicKey,
            Title = request.Title,
            Summary = request.Summary,
            Body = request.Body,
            Audience = request.Audience,
            Category = request.Category,
            DisplayOrder = request.DisplayOrder
        };

        db.HelpTopics.Add(topic);
        await db.SaveChangesAsync(ct);

        return new HelpTopicResponse(
            topic.Id, topic.TopicKey, topic.Title, topic.Summary, topic.Body,
            topic.Audience.ToString(), topic.Category, topic.DisplayOrder, topic.UpdatedDate);
    }
}
