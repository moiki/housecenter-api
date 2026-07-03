using FluentValidation;

namespace HouseCenter.Api.Features.Help;

public sealed class CreateHelpTopicRequestValidator : AbstractValidator<CreateHelpTopicRequest>
{
    // Dot-namespaced, kebab-case-per-segment key contract: "sessions.status-lifecycle".
    // At least one dot is required (two or more segments) — a bare "sessions" is not a key.
    private const string TopicKeyPattern = @"^[a-z0-9]+(-[a-z0-9]+)*(\.[a-z0-9]+(-[a-z0-9]+)*)+$";

    public CreateHelpTopicRequestValidator()
    {
        RuleFor(x => x.TopicKey)
            .NotEmpty()
            .MaximumLength(120)
            .Matches(TopicKeyPattern)
            .WithMessage("TopicKey must be dot-namespaced, lowercase, kebab-case per segment (e.g. 'sessions.status-lifecycle').");

        RuleFor(x => x.Title).NotEmpty().MaximumLength(150);
        RuleFor(x => x.Summary).NotEmpty().MaximumLength(300);
        RuleFor(x => x.Body).NotEmpty().MaximumLength(20000);
        RuleFor(x => x.Audience).IsInEnum();
        RuleFor(x => x.Category).NotEmpty().MaximumLength(60);
        RuleFor(x => x.DisplayOrder).GreaterThanOrEqualTo(0);
    }
}
