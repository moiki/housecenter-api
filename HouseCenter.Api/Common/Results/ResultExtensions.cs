namespace HouseCenter.Api.Common.Results;

public static class ResultExtensions
{
    /// <summary>Maps a domain Error to an RFC 9457 ProblemDetails HTTP response.</summary>
    public static IResult ToProblem(this Error error)
    {
        var statusCode = error.Type switch
        {
            ErrorType.Validation => StatusCodes.Status400BadRequest,
            ErrorType.Unauthorized => StatusCodes.Status401Unauthorized,
            ErrorType.Forbidden => StatusCodes.Status403Forbidden,
            ErrorType.NotFound => StatusCodes.Status404NotFound,
            ErrorType.Conflict => StatusCodes.Status409Conflict,
            _ => StatusCodes.Status500InternalServerError
        };

        return Microsoft.AspNetCore.Http.Results.Problem(
            title: error.Code,
            detail: error.Message,
            statusCode: statusCode);
    }

    public static IResult ToHttpResult<T>(this Result<T> result) =>
        result.Match(Microsoft.AspNetCore.Http.Results.Ok, error => error.ToProblem());

    public static IResult ToHttpResult(this Result result) =>
        result.IsFailure ? result.Error.ToProblem() : Microsoft.AspNetCore.Http.Results.NoContent();
}
