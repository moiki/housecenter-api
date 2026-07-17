using Scalar.AspNetCore;
using System.Text;
using FluentValidation;
using HouseCenter.Api.Common.Behaviors;
using HouseCenter.Api.Common.Email;
using Microsoft.Extensions.Logging;
using HouseCenter.Api.Common.Middleware;
using HouseCenter.Api.Common.Persistence;
using HouseCenter.Api.Common.Push;
using HouseCenter.Api.Common.Storage;
using HouseCenter.Api.Common.Translation;
using HouseCenter.Api.Features.Attachments;
using HouseCenter.Api.Data;
using HouseCenter.Api.Domain.Constants;
using HouseCenter.Api.Features.Auth;
using HouseCenter.Api.Features.Clinics;
using HouseCenter.Api.Features.Collaborators;
using HouseCenter.Api.Features.Consultations;
using HouseCenter.Api.Features.Help;
using HouseCenter.Api.Features.Invitations;
using HouseCenter.Api.Features.Notifications;
using HouseCenter.Api.Features.Patients;
using HouseCenter.Api.Features.Patients.Sessions;
using HouseCenter.Api.Features.Reports;
using HouseCenter.Api.Features.Roles;
using HouseCenter.Api.Features.Seeding;
using HouseCenter.Api.Features.Translation;
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
        // HS256 needs >=256-bit keys — catch short secrets at startup, not at first sign/login
        .Validate(o => Encoding.UTF8.GetByteCount(o.Secret) >= 32, "Jwt:Secret must be at least 32 bytes (256 bits) for HS256")
        .Validate(o => Encoding.UTF8.GetByteCount(o.InvitationSecret) >= 32, "Jwt:InvitationSecret must be at least 32 bytes (256 bits) for HS256")
        .Validate(o => Encoding.UTF8.GetByteCount(o.ResetSecret) >= 32, "Jwt:ResetSecret must be at least 32 bytes (256 bits) for HS256")
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
    builder.Services.Configure<TranslationOptions>(
        builder.Configuration.GetSection(TranslationOptions.SectionName));
    builder.Services.Configure<FileStorageOptions>(
        builder.Configuration.GetSection(FileStorageOptions.SectionName));
    builder.Services.Configure<CorsOptions>(
        builder.Configuration.GetSection(CorsOptions.SectionName));
    builder.Services.Configure<UserSessionOptions>(
        builder.Configuration.GetSection(UserSessionOptions.SectionName));

    // CORS — lets the React web app (dev servers) call the API cross-origin.
    // Explicit origins (not AllowAnyOrigin) so AllowCredentials can be added later
    // without a refactor if a refresh-token cookie is ever introduced.
    var corsOrigins = builder.Configuration
        .GetSection(CorsOptions.SectionName)
        .Get<CorsOptions>()?.AllowedOrigins ?? [];

    builder.Services.AddCors(options =>
    {
        options.AddPolicy(CorsOptions.PolicyName, policy =>
            policy.WithOrigins(corsOrigins)
                  .AllowAnyHeader()
                  .AllowAnyMethod());
    });

    // File storage: local disk by default (zero infra). S3 backend arrives in Phase 2.
    builder.Services.AddSingleton<IFileStorage, LocalDiskFileStorage>();
    builder.Services.AddScoped<IAttachmentAuthorizer, AttachmentAuthorizer>();

    // Push sender: real FCM only when credentials are present, else a logging no-op so the
    // pipeline runs without a Firebase project (same fallback idea as SMTP with no host).
    var pushConfigured = !string.IsNullOrWhiteSpace(
        builder.Configuration.GetSection(PushOptions.SectionName)[nameof(PushOptions.CredentialsJson)]);
    if (pushConfigured)
        builder.Services.AddSingleton<IPushSender, FirebasePushSender>();
    else
        builder.Services.AddSingleton<IPushSender, LoggingPushSender>();

    // Backs RateLimitBehavior's per-user, per-request-type counters.
    builder.Services.AddMemoryCache();

    // Translation client: real Google Cloud Translation API only when credentials are present,
    // else a logging no-op (echoes the input back) so the pipeline runs without a GCP project.
    var translationConfigured = !string.IsNullOrWhiteSpace(
        builder.Configuration.GetSection(TranslationOptions.SectionName)[nameof(TranslationOptions.ProjectId)]);
    if (translationConfigured)
        builder.Services.AddSingleton<ITranslationClient, GoogleTranslationClient>();
    else
        builder.Services.AddSingleton<ITranslationClient, LoggingTranslationClient>();

    // Database — resolved LAZILY via IOptions at pool-creation time (same reasoning as the
    // JwtBearer setup below). Reading builder.Configuration into a local var here would bake
    // in a snapshot taken before WebApplicationFactory's test-host config overrides are
    // applied, silently pointing every test run at the real dev database instead of
    // housecenter_test — which is exactly what was happening.
    builder.Services.AddDbContextPool<HouseCenterDbContext>((serviceProvider, options) =>
        options.UseNpgsql(serviceProvider.GetRequiredService<IOptions<DatabaseOptions>>().Value.ConnectionString));

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

    // Session TTL logic (60d/180d) needs a mockable clock to be testable — FakeTimeProvider
    // drives it in tests. Scoped narrowly to the session path only (design.md decision #6);
    // the DbContext audit hook keeps DateTime.UtcNow, no codebase-wide refactor.
    builder.Services.AddSingleton(TimeProvider.System);

    // Application services
    builder.Services.AddSingleton<ITokenService, TokenService>();
    builder.Services.AddSingleton<IRefreshTokenGenerator, RefreshTokenGenerator>();
    builder.Services.AddSingleton<ITokenHasher, TokenHasher>();
    builder.Services.AddScoped<IUserSessionService, UserSessionService>();
    builder.Services.AddScoped<IAuthService, AuthService>();
    builder.Services.AddScoped<IInvitationService, InvitationService>();
    builder.Services.AddScoped<IUserService, UserService>();
    builder.Services.AddScoped<IWorkRouteService, WorkRouteService>();
    builder.Services.AddScoped<ICollaboratorService, CollaboratorService>();
    builder.Services.AddScoped<ICollaboratorReconciliationService, CollaboratorReconciliationService>();
    builder.Services.AddScoped<IPatientService, PatientService>();
    builder.Services.AddScoped<IEmailService, SmtpEmailService>();
    // Resolved only by POST /seed, which is mapped in Development/Staging exclusively
    builder.Services.AddScoped<TestDataSeeder>();

    // FluentValidation — discovers every AbstractValidator<T> in the assembly
    builder.Services.AddValidatorsFromAssemblyContaining<Program>();

    // MediatR — discovers every IRequestHandler<,> in the assembly (same convention-over-registration idea)
    builder.Services.AddMediatR(cfg =>
    {
        cfg.RegisterServicesFromAssembly(typeof(Program).Assembly);
        cfg.AddOpenBehavior(typeof(PerformanceBehavior<,>));
        cfg.AddOpenBehavior(typeof(LoggingBehavior<,>));
        cfg.AddOpenBehavior(typeof(RateLimitBehavior<,>));
        cfg.AddOpenBehavior(typeof(ValidationBehavior<,>));
    });

    // Unexpected-exception safety net (expected errors flow as Result<T>, never exceptions)
    builder.Services.AddExceptionHandler<GlobalExceptionHandler>();
    builder.Services.AddProblemDetails();

    // Accept enum names ("Male"), not just ordinals, in JSON request bodies — the frontend's
    // TS string-union types send names, and this was the only piece missing to match them.
    builder.Services.ConfigureHttpJsonOptions(options =>
        options.SerializerOptions.Converters.Add(new System.Text.Json.Serialization.JsonStringEnumConverter()));

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
            sp => sp.GetRequiredService<IOptions<DatabaseOptions>>().Value.ConnectionString,
            name: "postgres",
            tags: ["db", "ready"]);
    builder.Services.AddHostedService<DatabaseSeeder>();

    // Session reminders: scoped scanner (testable) + the background trigger that runs it.
    builder.Services.AddScoped<SessionReminderScanner>();
    builder.Services.AddHostedService<SessionReminderService>();

    var app = builder.Build();

    // Dev convenience: `dotnet run -- --reset-help-topics` wipes the starter HelpTopics
    // rows so DatabaseSeeder (a hosted service started further below by app.Run()) reinserts
    // them fresh in this same run — no separate restart needed. DatabaseSeeder normally never
    // touches an existing row, by design, so this is the escape hatch for "I edited the
    // starter content in code and want my local DB to pick it up." Custom topics created via
    // the admin API are untouched: only TopicKeys still present in GetStarterHelpTopicKeys()
    // are ever deleted.
    if (args.Contains("--reset-help-topics"))
    {
        if (!app.Environment.IsDevelopment())
        {
            throw new InvalidOperationException("--reset-help-topics is a Development-only dev convenience flag.");
        }

        using var resetScope = app.Services.CreateScope();
        var db = resetScope.ServiceProvider.GetRequiredService<HouseCenterDbContext>();
        var starterKeys = DatabaseSeeder.GetStarterHelpTopicKeys();
        var deleted = await db.HelpTopics
            .IgnoreQueryFilters()
            .Where(t => starterKeys.Contains(t.TopicKey))
            .ExecuteDeleteAsync();
        app.Logger.LogWarning(
            "--reset-help-topics: deleted {Count} starter help topics; they will be reseeded fresh on this startup.",
            deleted);
    }

    app.UseExceptionHandler();

    if (app.Environment.IsDevelopment())
    {
        app.MapOpenApi();
        app.MapScalarApiReference();
    }

    app.UseHttpsRedirection();
    app.UseMiddleware<CorrelationIdMiddleware>();
    app.UseSerilogRequestLogging();

    // Before auth: a preflight OPTIONS carries no Authorization header, so CORS must
    // resolve it (and short-circuit with 204) before the auth middleware can 401 it.
    app.UseCors(CorsOptions.PolicyName);

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
    app.MapAttachmentEndpoints();
    app.MapHelpEndpoints();
    app.MapTranslationEndpoints();

    // Test data on demand — the route only exists in Development/Staging (404 in
    // Production) and never runs at startup; DatabaseSeeder keeps seeding roles + owner.
    if (app.Environment.IsDevelopment() || app.Environment.IsStaging())
        app.MapSeedEndpoints();

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
