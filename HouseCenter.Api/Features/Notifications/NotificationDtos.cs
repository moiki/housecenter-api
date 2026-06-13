namespace HouseCenter.Api.Features.Notifications;

public sealed record NotificationResponse(
    Guid Id,
    string Type,
    string Title,
    string Body,
    string? ReferenceType,
    Guid? ReferenceId,
    bool IsRead,
    DateTime? ReadAt,
    DateTime CreatedDate);

public sealed record UnreadCountResponse(int Count);
