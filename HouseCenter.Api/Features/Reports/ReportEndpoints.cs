using System.Security.Claims;
using HouseCenter.Api.Common.Results;
using MediatR;

namespace HouseCenter.Api.Features.Reports;

public static class ReportEndpoints
{
    public static IEndpointRouteBuilder MapReportEndpoints(this IEndpointRouteBuilder app)
    {
        // Any authenticated role — including Sponsor. Per-role projection
        // (anonymization, scoping) happens inside the handlers.
        var group = app.MapGroup("/reports").WithTags("Reports").RequireAuthorization();

        group.MapGet("/summary", async (IMediator mediator, CancellationToken ct) =>
                (await mediator.Send(new GetSummaryReportQuery(), ct)).ToHttpResult())
            .WithName("GetSummaryReport");

        group.MapGet("/clinics/{id:guid}", async (Guid id, ClaimsPrincipal principal,
                IMediator mediator, CancellationToken ct) =>
                (await mediator.Send(new GetClinicReportQuery(id, principal), ct)).ToHttpResult())
            .WithName("GetClinicReport");

        group.MapGet("/work-routes/{id:guid}", async (Guid id, ClaimsPrincipal principal,
                IMediator mediator, CancellationToken ct) =>
                (await mediator.Send(new GetWorkRouteReportQuery(id, principal), ct)).ToHttpResult())
            .WithName("GetWorkRouteReport");

        group.MapGet("/sessions", async (DateOnly @from, DateOnly to, ClaimsPrincipal principal,
                IMediator mediator, CancellationToken ct) =>
                (await mediator.Send(new GetSessionPeriodReportQuery(@from, to, principal), ct)).ToHttpResult())
            .WithName("GetSessionPeriodReport");

        return app;
    }
}
