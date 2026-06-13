using System.Collections.Concurrent;
using HouseCenter.Api.Common.Push;

namespace HouseCenter.Api.Tests;

public sealed record PushSend(string Token, string Title, string Body);

/// <summary>
/// Test double for <see cref="IPushSender"/>: records pushes instead of hitting FCM.
/// Tokens added to <see cref="InvalidTokens"/> are reported back as invalid so the handler
/// exercises its pruning path.
/// </summary>
public sealed class RecordingPushSender : IPushSender
{
    private readonly ConcurrentQueue<PushSend> _sent = new();

    public IReadOnlyCollection<PushSend> Sent => _sent;
    public HashSet<string> InvalidTokens { get; } = [];

    public Task<IReadOnlyCollection<string>> SendAsync(
        IReadOnlyCollection<string> tokens, string title, string body,
        IReadOnlyDictionary<string, string>? data, CancellationToken ct = default)
    {
        foreach (var t in tokens)
            _sent.Enqueue(new PushSend(t, title, body));

        IReadOnlyCollection<string> invalid = tokens.Where(InvalidTokens.Contains).ToList();
        return Task.FromResult(invalid);
    }
}
