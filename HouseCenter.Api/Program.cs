using Scalar.AspNetCore;
using System.Text;
using FluentValidation;
using HouseCenter.Api.Common.Behaviors;
using HouseCenter.Api.Common.Email;
using Microsoft.Extensions.Logging;
using HouseCenter.Api.Common.Middleware;
using HouseCenter.Api.Common.Persistence;
using HouseCenter.Api.Common.Push;
using HouseCenter.Api.Data;
using HouseCenter.Api.Domain.Constants;
using HouseCenter.Api.Features.Auth;
using HouseCenter.Api.Features.Clinics;
using HouseCenter.Api.Features.Collaborators;
using HouseCenter.Api.Features.Consultations;
using HouseCenter.Api.Features.Invitations;
using HouseCenter.Api.Features.Notifications;
using HouseCenter.Api.Features.Patients;
using HouseCenter.Api.Features.Patients.Sessions;
using HouseCenter.Api.Features.Reports;
using HouseCenter.Api.Features.Roles;
using HouseCenter.Api.Features.Users;
using HouseCenter.Api.Features.WorkRoutes;
using HouseCenter.Api.Options;
using Microsoft.AspNetCore.Authentication.JwtBearer;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Options;
using Microsoft.IdentityModel.Tokens;
using Serilog;

// Bootstrap logger — captures errors during startup before Serilog is fully configured
Log.Logger = new LoggerConfiguration()
    .WriteTo.Console()
    .CreateBootstrapLogger();

try
{
    var builder = WebApplication.CreateBuilder(args);

    // Serilog — replaces the default .NET logging provider
    builder.Host.UseSerilog((context, services, config) =>
        config.ReadFrom.Configuration(context.Configuration)
              .ReadFrom.Services(services)
              .Enrich.FromLogContext()
              .WriteTo.Console());

    // Options — bound, validated at startup where misconfiguration must fail fast
    builder.Services.Configure<DatabaseOptions>(
        builder.Configuration.GetSection(DatabaseOptions.SectionName));
    builder.Services.AddOptions<JwtOptions>()
        .Bind(builder.Configuration.GetSection(JwtOptions.SectionName))
        .Validate(o => !string.IsNullOrWhiteSpace(o.Secret), "Jwt:Secret is required")
        .Validate(o => !string.IsNullOrWhiteSpace(o.RefreshSecret), "Jwt:RefreshSecret is required")
        .Validate(o => o.Secret != o.RefreshSecret, "Jwt:Secret and Jwt:RefreshSecret must differ")
        .ValidateOnStart();
    builder.Services.Configure<EmailOptions>(
        builder.Configuration.GetSection(EmailOptions.SectionName));
    builder.Services.Configure<AppOptions>(
        builder.Configuration.GetSection(AppOptions.SectionName));
    builder.Services.Configure<SeedOptions>(
        builder.Configuration.GetSection(SeedOptions.SectionName));
    builder.Services.Configure<SessionReminderOptions>(
        builder.Configuration.GetSection(SessionReminderOptions.SectionName));
    builder.Services.Configure<PushOptions>(
        builder.Configuration.GetSection(PushOptions.SectionName));

    // Push sender: real FCM only when credentials are present, else a logging no-op so the
    // pipeline runs without a Firebase project (same fallback idea as SMTP with no host).
    var pushConfigured = !string.IsNullOrWhiteSpace(
        builder.Configuration.GetSection(PushOptions.SectionName)[nameof(PushOptions.CredentialsJson)]);
    if (pushConfigured)
        builder.Services.AddSingleton<IPushSender, FirebasePushSender>();
    else
        builder.Services.AddSingleton<IPushSender, LoggingPushSender>();

    // Database
    var connectionString = builder.Configuration
        .GetSection(DatabaseOptions.SectionName)
        .GetValue<string>(nameof(DatabaseOptions.ConnectionString));

    builder.Services.AddDbContextPool<HouseCenterDbContext>(options =>
        options.UseNpgsql(connectionString));

    // Authentication — validates the access token on every request carrying a Bearer header.
    // Configured LAZILY through DI (not a startup config snapshot) so it always sees the
    // final configuration — including test-host overrides and reloaded sources.
    builder.Services.AddAuthentication(JwtBearerDefaults.AuthenticationScheme).AddJwtBearer();
    builder.Services.AddOptions<JwtBearerOptions>(JwtBearerDefaults.AuthenticationScheme)
        .Configure<IOptions<JwtOptions>>((bearer, jwtOptions) =>
        {
            var jwt = jwtOptions.Value;
            bearer.TokenValidationParameters = new TokenValidationParameters
            {
                ValidIssuer = jwt.Issuer,
                ValidAudience = jwt.Audience,
                IssuerSigningKey = new SymmetricSecurityKey(Encoding.UTF8.GetBytes(jwt.Secret)),
                ClockSkew = TimeSpan.FromSeconds(30)
            };
        });

    // Authorization — named policies instead of magic strings on endpoints
    builder.Services.AddAuthorizationBuilder()
        .AddPolicy(PolicyNames.OwnerOnly, p => p.RequireRole(RoleNames.Owner))
        .AddPolicy(PolicyNames.AdministratorOrAbove, p => p.RequireRole(RoleNames.Owner, RoleNames.Administrator))
        .AddPolicy(PolicyNames.MemberOrAbove, p => p.RequireRole(RoleNames.Owner, RoleNames.Administrator, RoleNames.Member))
        .AddPolicy(PolicyNames.StaffOnly, p => p.RequireRole(RoleNames.Owner, RoleNames.Administrator, RoleNames.Member, RoleNames.Doctor));

    // Generic repository — open generic registration: one line covers IRepository<T> for every entity
    builder.Services.AddScoped(typeof(IRepository<>), typeof(EfRepository<>));

    // Application services
    builder.Services.AddSingleton<ITokenService, TokenService>();
    builder.Services.AddScoped<IAuthService, AuthService>();
    builder.Services.AddScoped<IInvitationService, InvitationService>();
    builder.Services.AddScoped<IUserService, UserService>();
    builder.Services.AddScoped<IWorkRouteService, WorkRouteService>();
    builder.Services.AddScoped<ICollaboratorService, CollaboratorService>();
    builder.Services.AddScoped<IPatientService, PatientService>();
    builder.Services.AddScoped<IEmailService, SmtpEmailService>();

    // FluentValidation — discovers every AbstractValidator<T> in the assembly
    builder.Services.AddValidatorsFromAssemblyContaining<Program>();

    // MediatR — discovers every IRequestHandler<,> in the assembly (same convention-over-registration idea)
    builder.Services.AddMediatR(cfg =>
    {
        cfg.RegisterServicesFromAssembly(typeof(Program).Assembly);
        cfg.AddOpenBehavior(typeof(PerformanceBehavior<,>));
        cfg.AddOpenBehavior(typeof(LoggingBehavior<,>));
        cfg.AddOpenBehavior(typeof(ValidationBehavior<,>));
    });

    // Unexpected-exception safety net (expected errors flow as Result<T>, never exceptions)
    builder.Services.AddExceptionHandler<GlobalExceptionHandler>();
    builder.Services.AddProblemDetails();

    // API versioning — URL segment strategy: /api/v1/clinics, /api/v2/clinics, etc.
    builder.Services.AddApiVersioning(options =>
    {
        options.DefaultApiVersion = new Asp.Versioning.ApiVersion(1);
        options.AssumeDefaultVersionWhenUnspecified = true;
        options.ReportApiVersions = true; // adds api-supported-versions header
    });

    builder.Services.AddOpenApi();
    builder.Services.AddHealthChecks()
        .AddNpgSql(
            connectionString ?? string.Empty,
            name: "postgres",
            tags: ["db", "ready"]);
    builder.Services.AddHostedService<DatabaseSeeder>();

    // Session reminders: scoped scanner (testable) + the background trigger that runs it.
    builder.Services.AddScoped<SessionReminderScanner>();
    builder.Services.AddHostedService<SessionReminderService>();

    var app = builder.Build();

    app.UseExceptionHandler();

    if (app.Environment.IsDevelopment())
    {
        app.MapOpenApi();
        app.MapScalarApiReference();
    }

    app.UseHttpsRedirection();
    app.UseMiddleware<CorrelationIdMiddleware>();
    app.UseSerilogRequestLogging();

    app.UseAuthentication();
    app.UseAuthorization();

    app.MapHealthChecks("/health");
    app.MapHealthChecks("/health/ready", new Microsoft.AspNetCore.Diagnostics.HealthChecks.HealthCheckOptions
    {
        Predicate = check => check.Tags.Contains("ready"),
        ResponseWriter = async (ctx, report) =>
        {
            ctx.Response.ContentType = "application/json";
            var result = System.Text.Json.JsonSerializer.Serialize(new
            {
                status = report.Status.ToString(),
                checks = report.Entries.Select(e => new
                {
                    name = e.Key,
                    status = e.Value.Status.ToString(),
                    description = e.Value.Description,
                    duration = e.Value.Duration.TotalMilliseconds
                })
            });
            await ctx.Response.WriteAsync(result);
        }
    });
    // Versioned API group — Clinics demonstrates the versioning pattern.
    // Other modules migrate to /api/v{n} in Phase 5.1.
    var versionSet = app.NewApiVersionSet()
        .HasApiVersion(new Asp.Versioning.ApiVersion(1))
        .ReportApiVersions()
        .Build();

    var v1 = app.MapGroup("/api/v{version:apiVersion}")
        .WithApiVersionSet(versionSet);

    v1.MapClinicEndpoints();

    // Unversioned endpoints (will migrate incrementally)
    app.MapAuthEndpoints();
    app.MapInvitationEndpoints();
    app.MapUserEndpoints();
    app.MapRoleEndpoints();
    app.MapWorkRouteEndpoints();
    app.MapCollaboratorEndpoints();
    app.MapPatientEndpoints();
    app.MapSessionEndpoints();
    app.MapConsultationEndpoints();
    app.MapReportEndpoints();
    app.MapNotificationEndpoints();

    app.Run();
}
catch (Exception ex) when (ex is not HostAbortedException)
{
    // HostAbortedException is thrown by design when `dotnet ef` inspects the app model
    Log.Fatal(ex, "Application failed to start");
}
finally
{
    Log.CloseAndFlush();
}

// Exposes the implicit Program class to WebApplicationFactory<Program> in integration tests
public partial class Program;
