using HouseCenter.Api.Domain.Entities;

namespace HouseCenter.Api.Features.Help;

public sealed record HelpTopicSummaryResponse(
    string TopicKey, string Title, string Summary, string Category, int DisplayOrder);

// Audience is exposed as a plain string (not HelpAudience) to match the codebase convention
// for enum-bearing response DTOs (see ConsultationResponse.Status) — the client-side
// HttpClient.ReadFromJsonAsync default options don't carry the server's JsonStringEnumConverter.
public sealed record HelpTopicResponse(
    Guid Id, string TopicKey, string Title, string Summary, string Body,
    string Audience, string Category, int DisplayOrder, DateTime UpdatedDate);

public sealed record CreateHelpTopicRequest(
    string TopicKey, string Title, string Summary, string Body,
    HelpAudience Audience, string Category, int DisplayOrder);
