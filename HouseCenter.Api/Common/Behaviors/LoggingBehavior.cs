using MediatR;
using Microsoft.Extensions.Logging;

namespace HouseCenter.Api.Common.Behaviors;

public sealed class LoggingBehavior<TRequest, TResponse>(ILogger<LoggingBehavior<TRequest, TResponse>> logger)
    : IPipelineBehavior<TRequest, TResponse>
    where TRequest : IRequest<TResponse>
{
    public async Task<TResponse> Handle(
        TRequest request,
        RequestHandlerDelegate<TResponse> next,
        CancellationToken ct)
    {
        var name = typeof(TRequest).Name;

        logger.LogInformation("Handling {Request}", name);

        var response = await next(ct);

        logger.LogInformation("Handled {Request}", name);

        return response;
    }
}
