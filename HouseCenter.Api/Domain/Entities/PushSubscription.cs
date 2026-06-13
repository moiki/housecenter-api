namespace HouseCenter.Api.Domain.Entities;

public enum PushPlatform
{
    Android,
    iOS,
    Web
}

// One FCM registration token per device install, owned by the user currently logged in on
// that device. Tokens rotate and can move between users (logout/login), so registration is
// an upsert keyed by Token.
public class PushSubscription : BaseEntity
{
    public Guid OwnerUserId { get; set; }
    public User Owner { get; set; } = null!;

    public string Token { get; set; } = string.Empty;
    public PushPlatform Platform { get; set; }
}
