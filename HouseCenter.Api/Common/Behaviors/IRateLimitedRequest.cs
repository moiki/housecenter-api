namespace HouseCenter.Api.Common.Behaviors;

/// <summary>
/// Opt-in marker for <see cref="RateLimitBehavior{TRequest,TResponse}"/> — only requests
/// implementing this are counted/limited; everything else passes through untouched.
/// </summary>
public interface IRateLimitedRequest
{
    /// <summary>Identity the limit is scoped to — typically the caller's user id. Combined
    /// with the request's own type name as the cache key, so different rate-limited request
    /// types never share one counter.</summary>
    string RateLimitKey { get; }
}
