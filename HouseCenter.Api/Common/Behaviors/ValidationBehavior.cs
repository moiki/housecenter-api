using FluentValidation;
using HouseCenter.Api.Common.Results;
using MediatR;

namespace HouseCenter.Api.Common.Behaviors;

// Runs before every IRequestHandler<TRequest, TResponse> where TResponse is Result<T> or Result.
// If no IValidator<TRequest> is registered, the behavior is a no-op — it just calls next().
public sealed class ValidationBehavior<TRequest, TResponse>(IEnumerable<IValidator<TRequest>> validators)
    : IPipelineBehavior<TRequest, TResponse>
    where TRequest : IRequest<TResponse>
    where TResponse : Result
{
    public async Task<TResponse> Handle(
        TRequest request,
        RequestHandlerDelegate<TResponse> next,
        CancellationToken ct)
    {
        if (!validators.Any())
            return await next(ct);

        var context = new ValidationContext<TRequest>(request);

        var failures = validators
            .Select(v => v.Validate(context))
            .SelectMany(r => r.Errors)
            .Where(f => f is not null)
            .ToList();

        if (failures.Count == 0)
            return await next(ct);

        var error = Error.Validation(
            "validation.failed",
            string.Join("; ", failures.Select(f => f.ErrorMessage)));

        // Result and Result<T> both support implicit conversion from Error.
        // The cast is safe because TResponse is constrained to Result.
        return (TResponse)(Result)error;
    }
}
