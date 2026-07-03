using HouseCenter.Api.Common.Results;
using HouseCenter.Api.Data;
using MediatR;
using Microsoft.EntityFrameworkCore;

namespace HouseCenter.Api.Features.Help;

public sealed record DeleteHelpTopicCommand(Guid Id) : IRequest<Result>;

public sealed class DeleteHelpTopicHandler(HouseCenterDbContext db)
    : IRequestHandler<DeleteHelpTopicCommand, Result>
{
    private static readonly Error NotFound = Error.NotFound("help.topic_not_found", "Help topic not found");

    public async Task<Result> Handle(DeleteHelpTopicCommand request, CancellationToken ct)
    {
        var topic = await db.HelpTopics.SingleOrDefaultAsync(t => t.Id == request.Id, ct);
        if (topic is null) return NotFound;

        // Soft delete = unpublish (R6): flips IsActive off so the global query filter hides
        // this topic from every subsequent GET /help/topics and GET /help/topics/{topicKey}
        // read, for every role, with no separate un-delete endpoint in this phase.
        topic.IsActive = false;
        await db.SaveChangesAsync(ct);

        return Result.Success();
    }
}
