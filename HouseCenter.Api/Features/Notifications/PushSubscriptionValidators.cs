using FluentValidation;

namespace HouseCenter.Api.Features.Notifications;

public sealed class RegisterPushSubscriptionRequestValidator
    : AbstractValidator<RegisterPushSubscriptionRequest>
{
    public RegisterPushSubscriptionRequestValidator()
    {
        RuleFor(x => x.Token).NotEmpty().MaximumLength(512);
        RuleFor(x => x.Platform).IsInEnum();
    }
}
