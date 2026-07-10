using FluentValidation;
using HouseCenter.Api.Domain.Entities;

namespace HouseCenter.Api.Features.WorkRoutes;

public sealed class DestinationPointDtoValidator : AbstractValidator<DestinationPointDto>
{
    public DestinationPointDtoValidator()
    {
        RuleFor(x => x.Name).NotEmpty().MaximumLength(200);
        RuleFor(x => x.Description).NotEmpty();
        RuleFor(x => x.Picture).MaximumLength(500);
        RuleFor(x => x.GoogleMapUrl).MaximumLength(500);
    }
}

public sealed class CreateWorkRouteRequestValidator : AbstractValidator<CreateWorkRouteRequest>
{
    public CreateWorkRouteRequestValidator()
    {
        RuleFor(x => x.RouteName).NotEmpty().MaximumLength(200);
        RuleFor(x => x.Description).NotEmpty();
        RuleFor(x => x.FeaturedImage).MaximumLength(500);
        RuleFor(x => x.ClinicId).NotEmpty();
        // Validates each item in the collection with the nested validator
        RuleForEach(x => x.Destinations).SetValidator(new DestinationPointDtoValidator());

        // Create hard-requires >=1 weekday; Update allows an empty set (legacy scheduleless
        // routes stay editable — see UpdateWorkRouteRequestValidator).
        RuleFor(x => x.RecurrenceDays).NotEmpty().WithMessage("At least one recurrence weekday is required");
        RuleForEach(x => x.RecurrenceDays)
            .Must(name => Enum.TryParse<Weekday>(name, out var day) && day != Weekday.None)
            .WithMessage("'{PropertyValue}' is not a valid weekday name");

        RuleFor(x => x.RecurrenceStartDate).NotEqual(default(DateOnly));

        RuleFor(x => x.RecurrenceEndDate)
            .NotNull().WithMessage("RecurrenceEndDate is required for finite recurrence")
            .GreaterThanOrEqualTo(x => x.RecurrenceStartDate)
                .WithMessage("RecurrenceEndDate must be on or after RecurrenceStartDate")
            .When(x => !x.IsRecurrenceIndefinite);

        RuleFor(x => x.RecurrenceEndDate)
            .Null().WithMessage("RecurrenceEndDate must be null for indefinite recurrence")
            .When(x => x.IsRecurrenceIndefinite);
    }
}

public sealed class UpdateWorkRouteRequestValidator : AbstractValidator<UpdateWorkRouteRequest>
{
    public UpdateWorkRouteRequestValidator()
    {
        RuleFor(x => x.RouteName).NotEmpty().MaximumLength(200);
        RuleFor(x => x.Description).NotEmpty();
        RuleFor(x => x.FeaturedImage).MaximumLength(500);
        RuleForEach(x => x.Destinations).SetValidator(new DestinationPointDtoValidator());

        // Update omits the >=1 weekday rule (D9): allows recurrenceDays = [] so legacy
        // scheduleless routes stay editable without being forced to schedule.
        RuleForEach(x => x.RecurrenceDays)
            .Must(name => Enum.TryParse<Weekday>(name, out var day) && day != Weekday.None)
            .WithMessage("'{PropertyValue}' is not a valid weekday name");

        RuleFor(x => x.RecurrenceStartDate).NotEqual(default(DateOnly));

        RuleFor(x => x.RecurrenceEndDate)
            .NotNull().WithMessage("RecurrenceEndDate is required for finite recurrence")
            .GreaterThanOrEqualTo(x => x.RecurrenceStartDate)
                .WithMessage("RecurrenceEndDate must be on or after RecurrenceStartDate")
            .When(x => !x.IsRecurrenceIndefinite);

        RuleFor(x => x.RecurrenceEndDate)
            .Null().WithMessage("RecurrenceEndDate must be null for indefinite recurrence")
            .When(x => x.IsRecurrenceIndefinite);
    }
}
