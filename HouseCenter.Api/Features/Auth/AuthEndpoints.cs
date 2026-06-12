using System.Security.Claims;
using HouseCenter.Api.Common.Results;
using HouseCenter.Api.Common.Validation;

namespace HouseCenter.Api.Features.Auth;

public static class AuthEndpoints
{
    public static IEndpointRouteBuilder MapAuthEndpoints(this IEndpointRouteBuilder app)
    {
        var group = app.MapGroup("/auth").WithTags("Auth");

        group.MapPost("/login", async (LoginRequest request, IAuthService auth, CancellationToken ct) =>
                (await auth.LoginAsync(request, ct)).ToHttpResult())
            .WithValidation<LoginRequest>()
            .WithName("Login");

        group.MapPost("/refresh", async (RefreshRequest request, IAuthService auth, CancellationToken ct) =>
                (await auth.RefreshAsync(request, ct)).ToHttpResult())
            .WithValidation<RefreshRequest>()
            .WithName("RefreshToken");

        group.MapGet("/me", async (ClaimsPrincipal principal, IAuthService auth, CancellationToken ct) =>
            {
                var userId = Guid.Parse(principal.FindFirstValue(ClaimTypes.NameIdentifier)!);
                return (await auth.MeAsync(userId, ct)).ToHttpResult();
            })
            .RequireAuthorization()
            .WithName("Me");

        group.MapPost("/signup", async (SignupRequest request, IAuthService auth, CancellationToken ct) =>
                (await auth.SignupAsync(request, ct)).ToHttpResult())
            .WithValidation<SignupRequest>()
            .WithName("Signup");

        group.MapPost("/password/request", async (PasswordResetRequestDto request, IAuthService auth, CancellationToken ct) =>
            {
                await auth.RequestPasswordResetAsync(request, ct);
                // 202 always — response must not reveal whether the email exists
                return Results.Accepted();
            })
            .WithValidation<PasswordResetRequestDto>()
            .WithName("RequestPasswordReset");

        group.MapPost("/password/reset", async (PasswordResetDto request, IAuthService auth, CancellationToken ct) =>
                (await auth.ResetPasswordAsync(request, ct)).ToHttpResult())
            .WithValidation<PasswordResetDto>()
            .WithName("ResetPassword");

        return app;
    }
}
