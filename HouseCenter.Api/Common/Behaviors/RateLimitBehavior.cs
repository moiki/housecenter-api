using HouseCenter.Api.Common.Results;
using MediatR;
using Microsoft.Extensions.Caching.Memory;

namespace HouseCenter.Api.Common.Behaviors;

// Runs before every IRequestHandler<TRequest, TResponse> where TResponse is Result<T> or Result.
// A no-op for any request that isn't IRateLimitedRequest. In-memory fixed-window counter per
// (request type, RateLimitKey) — fine for a single-instance API; would need a distributed store
// (Redis) behind a load balancer, not needed at this scale.
public sealed class RateLimitBehavior<TRequest, TResponse>(
    IMemoryCache cache,
    ILogger<RateLimitBehavior<TRequest, TResponse>> logger)
    : IPipelineBehavior<TRequest, TResponse>
    where TRequest : IRequest<TResponse>
    where TResponse : Result
{
    private const int MaxRequestsPerWindow = 20;
    private static readonly TimeSpan Window = TimeSpan.FromMinutes(1);

    // (TResponse)(Result)error (the double-cast used elsewhere, e.g. ValidationBehavior) only
    // works when TResponse is exactly Result — for a closed Result<T>, (Result)error builds a
    // plain base Result instance via Result.Failure(error), and downcasting THAT to Result<T>
    // throws InvalidCastException at runtime (verified: it 500'd here for
    // Result<TranslateTextResponse>). Result<T> defines its OWN `implicit operator
    // Result<T>(Error)`, so invoking TResponse's own op_Implicit via reflection (cached per
    // closed TResponse type) always builds the right concrete type, whether it's Result or a
    // specific Result<T>.
    private static readonly Lazy<Func<Error, TResponse>> ToFailure = new(() =>
    {
        var method = typeof(TResponse).GetMethod("op_Implicit", [typeof(Error)])
            ?? throw new InvalidOperationException($"{typeof(TResponse)} has no implicit conversion from Error");
        return (Func<Error, TResponse>)method.CreateDelegate(typeof(Func<Error, TResponse>));
    });

    public async Task<TResponse> Handle(
        TRequest request,
        RequestHandlerDelegate<TResponse> next,
        CancellationToken ct)
    {
        if (request is not IRateLimitedRequest rateLimited)
            return await next(ct);

        var cacheKey = $"rate-limit:{typeof(TRequest).Name}:{rateLimited.RateLimitKey}";

        var counter = cache.GetOrCreate(cacheKey, entry =>
        {
            entry.AbsoluteExpirationRelativeToNow = Window;
            return new RequestCounter();
        })!;

        if (Interlocked.Increment(ref counter.Count) > MaxRequestsPerWindow)
        {
            logger.LogWarning(
                "Rate limit exceeded for {Request} by {RateLimitKey}",
                typeof(TRequest).Name, rateLimited.RateLimitKey);

            var error = Error.TooManyRequests(
                "rate_limit.exceeded",
                "Too many requests. Please wait a moment and try again.");

            return ToFailure.Value(error);
        }

        return await next(ct);
    }

    private sealed class RequestCounter
    {
        public int Count;
    }
}
