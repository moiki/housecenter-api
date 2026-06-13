using FluentValidation;

namespace HouseCenter.Api.Features.Consultations;

public sealed class CreateConsultationRequestValidator : AbstractValidator<CreateConsultationRequest>
{
    public CreateConsultationRequestValidator()
    {
        RuleFor(x => x.PatientId).NotEmpty();
        RuleFor(x => x.AssignedDoctorId).NotEmpty();
        RuleFor(x => x.Title).NotEmpty().MaximumLength(300);
        RuleFor(x => x.FirstMessage).NotEmpty().MaximumLength(5000);
        RuleFor(x => x.AttachmentUrl)
            .MaximumLength(500)
            .Must(url => Uri.TryCreate(url, UriKind.Absolute, out _))
            .When(x => !string.IsNullOrWhiteSpace(x.AttachmentUrl))
            .WithMessage("AttachmentUrl must be a valid absolute URL");
    }
}

public sealed class PostMessageRequestValidator : AbstractValidator<PostMessageRequest>
{
    public PostMessageRequestValidator()
    {
        RuleFor(x => x.Body).NotEmpty().MaximumLength(5000);
        RuleFor(x => x.AttachmentUrl)
            .MaximumLength(500)
            .Must(url => Uri.TryCreate(url, UriKind.Absolute, out _))
            .When(x => !string.IsNullOrWhiteSpace(x.AttachmentUrl))
            .WithMessage("AttachmentUrl must be a valid absolute URL");
    }
}

public sealed class UpdateConsultationStatusRequestValidator : AbstractValidator<UpdateConsultationStatusRequest>
{
    public UpdateConsultationStatusRequestValidator()
    {
        RuleFor(x => x.Status).IsInEnum();
    }
}
