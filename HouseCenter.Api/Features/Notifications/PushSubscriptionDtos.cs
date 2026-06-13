using HouseCenter.Api.Domain.Entities;

namespace HouseCenter.Api.Features.Notifications;

public sealed record RegisterPushSubscriptionRequest(string Token, PushPlatform Platform);
