using FirebaseAdmin;
using FirebaseAdmin.Messaging;
using Google.Apis.Auth.OAuth2;
using HouseCenter.Api.Options;
using Microsoft.Extensions.Options;

namespace HouseCenter.Api.Common.Push;

// FCM channel. Brokers Android, iOS (via APNs) and web from one API. Registered as a
// singleton only when Push:CredentialsJson is present; otherwise LoggingPushSender is used.
public sealed class FirebasePushSender : IPushSender
{
    private readonly FirebaseMessaging _messaging;
    private readonly ILogger<FirebasePushSender> _logger;

    public FirebasePushSender(IOptions<PushOptions> options, ILogger<FirebasePushSender> logger)
    {
        _logger = logger;
        // Named app so we never collide with a default FirebaseApp another component might create.
        var app = FirebaseApp.Create(new FirebaseAdmin.AppOptions
        {
            Credential = GoogleCredential.FromJson(options.Value.CredentialsJson)
        }, "housecenter-push");
        _messaging = FirebaseMessaging.GetMessaging(app);
    }

    public async Task<IReadOnlyCollection<string>> SendAsync(
        IReadOnlyCollection<string> tokens, string title, string body,
        IReadOnlyDictionary<string, string>? data, CancellationToken ct = default)
    {
        if (tokens.Count == 0) return [];

        var tokenList = tokens.ToList();
        var message = new MulticastMessage
        {
            Tokens = tokenList,
            Notification = new FirebaseAdmin.Messaging.Notification { Title = title, Body = body },
            Data = data?.ToDictionary(kv => kv.Key, kv => kv.Value)
        };

        var response = await _messaging.SendEachForMulticastAsync(message, ct);

        // A token is dead when FCM says Unregistered (uninstalled / expired) or the token
        // argument was invalid. Those get pruned by the caller. Other failures are transient.
        var invalid = new List<string>();
        for (var i = 0; i < response.Responses.Count; i++)
        {
            var r = response.Responses[i];
            if (r.IsSuccess) continue;
            if (r.Exception is FirebaseMessagingException fme &&
                fme.MessagingErrorCode is MessagingErrorCode.Unregistered or MessagingErrorCode.InvalidArgument)
            {
                invalid.Add(tokenList[i]);
            }
            else
            {
                _logger.LogWarning(r.Exception, "Transient push failure for a token");
            }
        }
        return invalid;
    }
}
