namespace HouseCenter.Api.Domain.Entities;

// Persisted, editable, role-filtered usage guide content — surfaced later as in-app
// tooltips. Frontend integration is a separate, dependent follow-up change against
// housecenter-web; see openspec/changes/help-center/proposal.md.
public class HelpTopic : BaseEntity
{
    // Stable, dot-namespaced key the frontend anchors to (e.g. "sessions.status-lifecycle").
    // Immutable after creation — see CreateHelpTopic/UpdateHelpTopic (Phase 2/3).
    public string TopicKey { get; set; } = string.Empty;
    public string Title { get; set; } = string.Empty;
    public string Summary { get; set; } = string.Empty;
    public string Body { get; set; } = string.Empty;
    public HelpAudience Audience { get; set; }
    public string Category { get; set; } = string.Empty;
    public int DisplayOrder { get; set; }
}
