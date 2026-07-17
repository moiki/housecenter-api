using FluentAssertions;
using HouseCenter.Api.Common.Behaviors;
using HouseCenter.Api.Common.Results;
using MediatR;
using Microsoft.Extensions.Caching.Memory;
using Microsoft.Extensions.Logging.Abstractions;

namespace HouseCenter.Api.Tests;

// Unit tests for RateLimitBehavior in isolation — no HTTP, no WebApplicationFactory. Exercised
// end-to-end (429 over HTTP) by TranslationTests, but that only proves ONE consumer works; this
// proves the behavior itself, including the reflection-based Error -> TResponse conversion that
// CLAUDE.md's gotcha warns ValidationBehavior's naive double-cast gets wrong for closed Result<T>.
public sealed class RateLimitBehaviorTests
{
    private sealed record LimitedRequest(string Key) : IRequest<Result<string>>, IRateLimitedRequest
    {
        public string RateLimitKey => Key;
    }

    private sealed record UnlimitedRequest : IRequest<Result<string>>;

    private static RateLimitBehavior<TRequest, Result<string>> CreateBehavior<TRequest>(IMemoryCache cache)
        where TRequest : IRequest<Result<string>> =>
        new(cache, NullLogger<RateLimitBehavior<TRequest, Result<string>>>.Instance);

    [Fact]
    public async Task Handle_RequestNotRateLimited_AlwaysPassesThrough()
    {
        using var cache = new MemoryCache(new MemoryCacheOptions());
        var behavior = CreateBehavior<UnlimitedRequest>(cache);
        var nextCalls = 0;

        for (var i = 0; i < 25; i++)
        {
            var result = await behavior.Handle(new UnlimitedRequest(), _ =>
            {
                nextCalls++;
                return Task.FromResult<Result<string>>("ok");
            }, CancellationToken.None);

            result.IsSuccess.Should().BeTrue();
        }

        nextCalls.Should().Be(25, "a request that isn't IRateLimitedRequest is never counted");
    }

    [Fact]
    public async Task Handle_UpToLimitPerWindow_CallsNext()
    {
        using var cache = new MemoryCache(new MemoryCacheOptions());
        var behavior = CreateBehavior<LimitedRequest>(cache);
        var nextCalls = 0;

        for (var i = 0; i < 20; i++)
        {
            var result = await behavior.Handle(new LimitedRequest("user-1"), _ =>
            {
                nextCalls++;
                return Task.FromResult<Result<string>>("ok");
            }, CancellationToken.None);

            result.IsSuccess.Should().BeTrue();
        }

        nextCalls.Should().Be(20);
    }

    [Fact]
    public async Task Handle_OverLimitPerWindow_ReturnsTooManyRequestsWithoutCallingNext()
    {
        using var cache = new MemoryCache(new MemoryCacheOptions());
        var behavior = CreateBehavior<LimitedRequest>(cache);
        var nextCalls = 0;

        Result<string>? last = null;
        for (var i = 0; i < 21; i++)
        {
            last = await behavior.Handle(new LimitedRequest("user-2"), _ =>
            {
                nextCalls++;
                return Task.FromResult<Result<string>>("ok");
            }, CancellationToken.None);
        }

        nextCalls.Should().Be(20, "the 21st call must be rejected before reaching next()");
        last!.IsFailure.Should().BeTrue();
        last.Error!.Type.Should().Be(ErrorType.TooManyRequests);
    }

    [Fact]
    public async Task Handle_DifferentRateLimitKeys_HaveIndependentCounters()
    {
        using var cache = new MemoryCache(new MemoryCacheOptions());
        var behavior = CreateBehavior<LimitedRequest>(cache);

        for (var i = 0; i < 20; i++)
            await behavior.Handle(new LimitedRequest("user-a"), _ => Task.FromResult<Result<string>>("ok"), CancellationToken.None);

        // user-b's own window is untouched by user-a's 20 calls.
        var result = await behavior.Handle(
            new LimitedRequest("user-b"), _ => Task.FromResult<Result<string>>("ok"), CancellationToken.None);

        result.IsSuccess.Should().BeTrue();
    }

    [Fact]
    public async Task Handle_OverLimit_ReturnsARealClosedResultT_NotABareResult()
    {
        // Regression guard for the exact bug CLAUDE.md's gotcha documents: (TResponse)(Result)error
        // throws InvalidCastException for a closed Result<T> because (Result)error always builds
        // a plain base Result via Result's own operator. RateLimitBehavior avoids this via
        // reflection over TResponse's own op_Implicit(Error) — this test proves that path returns
        // a genuine Result<string> (readable .Value semantics on success, no cast exception here).
        using var cache = new MemoryCache(new MemoryCacheOptions());
        var behavior = CreateBehavior<LimitedRequest>(cache);

        for (var i = 0; i < 20; i++)
            await behavior.Handle(new LimitedRequest("user-3"), _ => Task.FromResult<Result<string>>("ok"), CancellationToken.None);

        // If this ever regresses to (TResponse)(Result)error, this line throws InvalidCastException
        // and the test fails with that exception rather than the assertion below.
        var result = await behavior.Handle(
            new LimitedRequest("user-3"), _ => Task.FromResult<Result<string>>("ok"), CancellationToken.None);

        result.Error!.Code.Should().Be("rate_limit.exceeded");
    }
}
