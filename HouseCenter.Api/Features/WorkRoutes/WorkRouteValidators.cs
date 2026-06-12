using FluentValidation;

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
    }
}
