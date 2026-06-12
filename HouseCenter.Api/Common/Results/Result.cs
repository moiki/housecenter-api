using System.Diagnostics.CodeAnalysis;

namespace HouseCenter.Api.Common.Results;

public class Result
{
    protected Result(bool isSuccess, Error? error)
    {
        // Invariant: success carries no error; failure always carries one
        if (isSuccess && error is not null) throw new InvalidOperationException("Success result cannot carry an error");
        if (!isSuccess && error is null) throw new InvalidOperationException("Failure result must carry an error");

        IsSuccess = isSuccess;
        Error = error;
    }

    public bool IsSuccess { get; }

    [MemberNotNullWhen(true, nameof(Error))]
    public bool IsFailure => !IsSuccess;

    public Error? Error { get; }

    public static Result Success() => new(true, null);
    public static Result Failure(Error error) => new(false, error);

    public static implicit operator Result(Error error) => Failure(error);

    public static Result<T> Success<T>(T value) => Result<T>.Success(value);
    public static Result<T> Failure<T>(Error error) => Result<T>.Failure(error);
}

public sealed class Result<T> : Result
{
    private readonly T? _value;

    private Result(T value) : base(true, null) => _value = value;
    private Result(Error error) : base(false, error) => _value = default;

    public T Value => IsSuccess
        ? _value!
        : throw new InvalidOperationException("Cannot access Value of a failed result — check IsSuccess first");

    public static Result<T> Success(T value) => new(value);
    public static new Result<T> Failure(Error error) => new(error);

    // Allows `return user;` and `return SomeError;` directly from service methods
    public static implicit operator Result<T>(T value) => Success(value);
    public static implicit operator Result<T>(Error error) => Failure(error);

    public TOut Match<TOut>(Func<T, TOut> onSuccess, Func<Error, TOut> onFailure) =>
        IsSuccess ? onSuccess(_value!) : onFailure(Error!);
}