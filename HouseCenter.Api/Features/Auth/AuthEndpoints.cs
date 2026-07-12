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

        // Session management (R7) — all self-scoped via ClaimTypes.NameIdentifier, bare
        // .RequireAuthorization() (no extra policy needed, same shape as GET /auth/me).
        // These call IUserSessionService directly (design.md: it's the single place the
        // DbContext is mutated for sessions) rather than routing through IAuthService.
        group.MapPost("/logout", async (LogoutRequest request, ClaimsPrincipal principal,
                IUserSessionService sessions, CancellationToken ct) =>
            {
                var userId = Guid.Parse(principal.FindFirstValue(ClaimTypes.NameIdentifier)!);
                await sessions.RevokeDeviceAsync(userId, request.DeviceId, ct);
                return Results.NoContent();
            })
            .RequireAuthorization()
            .WithValidation<LogoutRequest>()
            .WithName("Logout");

        group.MapGet("/sessions", async (ClaimsPrincipal principal,
                IUserSessionService sessions, CancellationToken ct) =>
            {
                var userId = Guid.Parse(principal.FindFirstValue(ClaimTypes.NameIdentifier)!);
                return Results.Ok(await sessions.GetActiveSessionsAsync(userId, ct));
            })
            .RequireAuthorization()
            .WithName("GetSessions");

        group.MapDelete("/sessions/{id:guid}", async (Guid id, ClaimsPrincipal principal,
                IUserSessionService sessions, CancellationToken ct) =>
            {
                var userId = Guid.Parse(principal.FindFirstValue(ClaimTypes.NameIdentifier)!);
                // NotFound (not Forbidden) for a foreign or nonexistent id — anti-enumeration.
                return (await sessions.RevokeSessionByIdAsync(userId, id, ct)).ToHttpResult();
            })
            .RequireAuthorization()
            .WithName("RevokeSession");

        group.MapPost("/sessions/revoke-all", async (ClaimsPrincipal principal,
                IUserSessionService sessions, CancellationToken ct) =>
            {
                var userId = Guid.Parse(principal.FindFirstValue(ClaimTypes.NameIdentifier)!);
                await sessions.RevokeAllAsync(userId, ct);
                return Results.NoContent();
            })
            .RequireAuthorization()
            .WithName("RevokeAllSessions");

        return app;
    }
}
