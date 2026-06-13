using FluentValidation;

namespace HouseCenter.Api.Features.Patients;

public sealed class CreatePatientRequestValidator : AbstractValidator<CreatePatientRequest>
{
    public CreatePatientRequestValidator()
    {
        RuleFor(x => x.FirstName).NotEmpty().MaximumLength(100);
        RuleFor(x => x.LastName).NotEmpty().MaximumLength(100);
        RuleFor(x => x.BirthDate).LessThan(DateTime.UtcNow);
        RuleFor(x => x.Address).NotEmpty().MaximumLength(500);
        RuleFor(x => x.Country).MaximumLength(100);
        RuleFor(x => x.State).MaximumLength(100);
        RuleFor(x => x.City).MaximumLength(100);
        RuleFor(x => x.PrimaryAttentionType).IsInEnum();
    }
}

public sealed class UpdatePatientRequestValidator : AbstractValidator<UpdatePatientRequest>
{
    public UpdatePatientRequestValidator()
    {
        RuleFor(x => x.FirstName).NotEmpty().MaximumLength(100);
        RuleFor(x => x.LastName).NotEmpty().MaximumLength(100);
        RuleFor(x => x.BirthDate).LessThan(DateTime.UtcNow);
        RuleFor(x => x.Address).NotEmpty().MaximumLength(500);
        RuleFor(x => x.Country).MaximumLength(100);
        RuleFor(x => x.State).MaximumLength(100);
        RuleFor(x => x.City).MaximumLength(100);
        RuleFor(x => x.PrimaryAttentionType).IsInEnum();
    }
}

public sealed class CreateTreatmentRequestValidator : AbstractValidator<CreateTreatmentRequest>
{
    public CreateTreatmentRequestValidator()
    {
        RuleFor(x => x.Name).NotEmpty().MaximumLength(200);
        RuleFor(x => x.Description).NotEmpty().MaximumLength(2000);
        RuleFor(x => x.StartDate).NotEmpty();
        RuleFor(x => x.EndDate).GreaterThan(x => x.StartDate)
            .WithMessage("EndDate must be after StartDate");
        RuleFor(x => x.Type).IsInEnum().When(x => x.Type.HasValue);
    }
}

public sealed class UpdateTreatmentRequestValidator : AbstractValidator<UpdateTreatmentRequest>
{
    public UpdateTreatmentRequestValidator()
    {
        RuleFor(x => x.Name).NotEmpty().MaximumLength(200);
        RuleFor(x => x.Description).NotEmpty().MaximumLength(2000);
        RuleFor(x => x.StartDate).NotEmpty();
        RuleFor(x => x.EndDate).GreaterThan(x => x.StartDate)
            .WithMessage("EndDate must be after StartDate");
        RuleFor(x => x.Type).IsInEnum().When(x => x.Type.HasValue);
    }
}

public sealed class CreateTreatmentDetailRequestValidator : AbstractValidator<CreateTreatmentDetailRequest>
{
    public CreateTreatmentDetailRequestValidator()
    {
        RuleFor(x => x.Name).NotEmpty().MaximumLength(200);
        RuleFor(x => x.Description).NotEmpty().MaximumLength(2000);
        RuleFor(x => x.TreatmentDate).NotEmpty();
    }
}

public sealed class UpdateTreatmentDetailRequestValidator : AbstractValidator<UpdateTreatmentDetailRequest>
{
    public UpdateTreatmentDetailRequestValidator()
    {
        RuleFor(x => x.Name).NotEmpty().MaximumLength(200);
        RuleFor(x => x.Description).NotEmpty().MaximumLength(2000);
        RuleFor(x => x.TreatmentDate).NotEmpty();
    }
}

public sealed class CreateTreatmentCommentRequestValidator : AbstractValidator<CreateTreatmentCommentRequest>
{
    public CreateTreatmentCommentRequestValidator()
    {
        RuleFor(x => x.Body).NotEmpty().MaximumLength(2000);
    }
}

public sealed class UpdateTreatmentCommentRequestValidator : AbstractValidator<UpdateTreatmentCommentRequest>
{
    public UpdateTreatmentCommentRequestValidator()
    {
        RuleFor(x => x.Body).NotEmpty().MaximumLength(2000);
    }
}

public sealed class CreatePatientCommentRequestValidator : AbstractValidator<CreatePatientCommentRequest>
{
    public CreatePatientCommentRequestValidator()
    {
        RuleFor(x => x.Body).NotEmpty().MaximumLength(2000);
    }
}

public sealed class UpdatePatientCommentRequestValidator : AbstractValidator<UpdatePatientCommentRequest>
{
    public UpdatePatientCommentRequestValidator()
    {
        RuleFor(x => x.Body).NotEmpty().MaximumLength(2000);
    }
}
