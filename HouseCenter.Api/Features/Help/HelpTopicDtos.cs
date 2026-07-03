namespace HouseCenter.Api.Features.Help;

public sealed record HelpTopicSummaryResponse(
    string TopicKey, string Title, string Summary, string Category, int DisplayOrder);
