using FluentValidation;

namespace HouseCenter.Api.Features.Collaborators;

public sealed class CreateCollaboratorRequestValidator : AbstractValidator<CreateCollaboratorRequest>
{
    public CreateCollaboratorRequestValidator()
    {
        RuleFor(x => x.FirstName).NotEmpty().MaximumLength(100);
        RuleFor(x => x.LastName).NotEmpty().MaximumLength(100);
        RuleFor(x => x.Email).NotEmpty().EmailAddress().MaximumLength(300);
        RuleFor(x => x.PhoneNumber).NotEmpty().MaximumLength(50);
        RuleFor(x => x.Address).NotEmpty().MaximumLength(500);
        RuleFor(x => x.Country).MaximumLength(100);
        RuleFor(x => x.State).MaximumLength(100);
        RuleFor(x => x.City).MaximumLength(100);
        RuleFor(x => x.ProfilePicture).MaximumLength(500);
        RuleFor(x => x.ClinicId).NotEmpty();
        RuleForEach(x => x.Positions).NotEmpty().MaximumLength(100);
    }
}

public sealed class UpdateCollaboratorRequestValidator : AbstractValidator<UpdateCollaboratorRequest>
{
    public UpdateCollaboratorRequestValidator()
    {
        RuleFor(x => x.FirstName).NotEmpty().MaximumLength(100);
        RuleFor(x => x.LastName).NotEmpty().MaximumLength(100);
        RuleFor(x => x.Email).NotEmpty().EmailAddress().MaximumLength(300);
        RuleFor(x => x.PhoneNumber).NotEmpty().MaximumLength(50);
        RuleFor(x => x.Address).NotEmpty().MaximumLength(500);
        RuleFor(x => x.Country).MaximumLength(100);
        RuleFor(x => x.State).MaximumLength(100);
        RuleFor(x => x.City).MaximumLength(100);
        RuleFor(x => x.ProfilePicture).MaximumLength(500);
        RuleFor(x => x.ClinicId).NotEmpty();
        RuleForEach(x => x.Positions).NotEmpty().MaximumLength(100);
    }
}
