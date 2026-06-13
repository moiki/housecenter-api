using FluentValidation;

namespace HouseCenter.Api.Features.Patients.Sessions;

public sealed class CreateAttentionSessionRequestValidator : AbstractValidator<CreateAttentionSessionRequest>
{
    public CreateAttentionSessionRequestValidator()
    {
        RuleFor(x => x.CollaboratorId).NotEmpty();
        RuleFor(x => x.AttentionType).IsInEnum();

        // Cross-field rule: validated on the whole request because neither field
        // alone can answer "is a location present?"
        RuleFor(x => x)
            .Must(x => x.ClinicId.HasValue || x.WorkRouteId.HasValue)
            .WithMessage("Either ClinicId or WorkRouteId must be provided")
            .OverridePropertyName("Location");

        // Near-term pre-scheduling is allowed; far-future entries are likely typos
        RuleFor(x => x.SessionDate)
            .Must(date => date <= DateTime.UtcNow.AddDays(7))
            .WithMessage("SessionDate cannot be more than 7 days in the future");

        RuleFor(x => x.DurationMinutes)
            .InclusiveBetween(5, 480)
            .When(x => x.DurationMinutes.HasValue);

        RuleFor(x => x.Notes).MaximumLength(2000);
    }
}

public sealed class UpdateSessionStatusRequestValidator : AbstractValidator<UpdateSessionStatusRequest>
{
    public UpdateSessionStatusRequestValidator()
    {
        RuleFor(x => x.Status).IsInEnum();

        RuleFor(x => x.DurationMinutes)
            .InclusiveBetween(5, 480)
            .When(x => x.DurationMinutes.HasValue);

        RuleFor(x => x.Notes).MaximumLength(2000);
    }
}
