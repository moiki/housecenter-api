using FluentValidation;

namespace HouseCenter.Api.Features.Invitations;

public sealed class CreateInvitationRequestValidator : AbstractValidator<CreateInvitationRequest>
{
    public CreateInvitationRequestValidator()
    {
        RuleFor(x => x.Email).NotEmpty().EmailAddress().MaximumLength(300);
        RuleFor(x => x.RoleId).NotEmpty();
    }
}
