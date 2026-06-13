namespace HouseCenter.Api.Common.Push;

public interface IPushSender
{
    /// <summary>
    /// Sends a push to the given device tokens. Returns the subset of tokens the provider
    /// reported as permanently invalid (unregistered / malformed) so the caller can prune them.
    /// </summary>
    Task<IReadOnlyCollection<string>> SendAsync(
        IReadOnlyCollection<string> tokens,
        string title,
        string body,
        IReadOnlyDictionary<string, string>? data,
        CancellationToken ct = default);
}
