using System.Collections.Concurrent;
using HouseCenter.Api.Common.Email;

namespace HouseCenter.Api.Tests;

public sealed record SentEmail(string To, string Subject, string Body);

/// <summary>
/// Test double for <see cref="IEmailService"/>: records sends instead of hitting SMTP.
/// When <see cref="ThrowOnSend"/> is set, throws to exercise best-effort handling.
/// </summary>
public sealed class RecordingEmailService : IEmailService
{
    private readonly ConcurrentQueue<SentEmail> _sent = new();

    public IReadOnlyCollection<SentEmail> Sent => _sent;
    public bool ThrowOnSend { get; set; }

    public Task SendAsync(string to, string subject, string htmlBody, CancellationToken ct = default)
    {
        if (ThrowOnSend)
            throw new InvalidOperationException("Simulated SMTP failure");

        _sent.Enqueue(new SentEmail(to, subject, htmlBody));
        return Task.CompletedTask;
    }
}
