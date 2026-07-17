using FluentValidation;

namespace HouseCenter.Api.Features.Translation;

public sealed class TranslateTextRequestValidator : AbstractValidator<TranslateTextRequest>
{
    public TranslateTextRequestValidator()
    {
        // 5000 matches the existing cap on consultation message Body (PostMessageRequestValidator) —
        // translation input is always an already-posted message/comment, never longer than that.
        RuleFor(x => x.Text).NotEmpty().MaximumLength(5000);
        RuleFor(x => x.TargetLanguage).IsInEnum();
        RuleFor(x => x.SourceLanguage).IsInEnum().When(x => x.SourceLanguage is not null);
    }
}
