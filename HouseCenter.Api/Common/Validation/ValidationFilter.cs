using FluentValidation;

namespace HouseCenter.Api.Common.Validation;

/// <summary>
/// Endpoint filter that validates the first argument of type T using its registered
/// IValidator&lt;T&gt;. Returns 400 ValidationProblem before the handler ever runs.
/// </summary>
public sealed class ValidationFilter<T> : IEndpointFilter where T : class
{
    public async ValueTask<object?> InvokeAsync(
        EndpointFilterInvocationContext context,
        EndpointFilterDelegate next)
    {
        var argument = context.Arguments.OfType<T>().FirstOrDefault();
        if (argument is null)
            return await next(context);

        var validator = context.HttpContext.RequestServices.GetService<IValidator<T>>();
        if (validator is null)
            return await next(context);

        var validation = await validator.ValidateAsync(argument, context.HttpContext.RequestAborted);
        if (!validation.IsValid)
            // Fully qualified: the HouseCenter.Api.Common.Results NAMESPACE shadows the Results class here
            return Microsoft.AspNetCore.Http.Results.ValidationProblem(validation.ToDictionary());

        return await next(context);
    }
}

public static class ValidationFilterExtensions
{
    /// <summary>Fluent shortcut: .WithValidation&lt;LoginRequest&gt;() on any endpoint.</summary>
    public static RouteHandlerBuilder WithValidation<T>(this RouteHandlerBuilder builder) where T : class =>
        builder.AddEndpointFilter<ValidationFilter<T>>();
}
