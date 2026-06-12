using System.Security.Claims;
using HouseCenter.Api.Common.Pagination;
using HouseCenter.Api.Common.Results;
using HouseCenter.Api.Common.Validation;

namespace HouseCenter.Api.Features.Patients;

public static class PatientEndpoints
{
    public static IEndpointRouteBuilder MapPatientEndpoints(this IEndpointRouteBuilder app)
    {
        MapPatients(app);
        MapTreatments(app);
        MapTreatmentDetails(app);
        MapTreatmentComments(app);
        MapPatientComments(app);
        return app;
    }

    // ─── 4.1 + 4.2 Patients ──────────────────────────────────────────────────
    private static void MapPatients(IEndpointRouteBuilder app)
    {
        var group = app.MapGroup("/patients").WithTags("Patients").RequireAuthorization();

        group.MapGet("/", async ([AsParameters] PageQuery paging, IPatientService service, CancellationToken ct) =>
                (await service.ListAsync(paging, ct)).ToHttpResult())
            .WithName("ListPatients");

        group.MapGet("/{id:guid}", async (Guid id, IPatientService service, CancellationToken ct) =>
                (await service.GetByIdAsync(id, ct)).ToHttpResult())
            .WithName("GetPatient");

        // 4.3 Full summary — parallel: patient + treatments + comments via Task.WhenAll
        group.MapGet("/{id:guid}/full-summary", async (Guid id, IPatientService service, CancellationToken ct) =>
                (await service.GetFullSummaryAsync(id, ct)).ToHttpResult())
            .WithName("GetPatientFullSummary");

        group.MapPost("/", async (CreatePatientRequest request, IPatientService service, CancellationToken ct) =>
            {
                var result = await service.CreateAsync(request, ct);
                return result.Match(
                    created => Results.Created($"/patients/{created.Id}", created),
                    error => error.ToProblem());
            })
            .WithValidation<CreatePatientRequest>()
            .WithName("CreatePatient");

        group.MapPut("/{id:guid}", async (Guid id, UpdatePatientRequest request, IPatientService service, CancellationToken ct) =>
                (await service.UpdateAsync(id, request, ct)).ToHttpResult())
            .WithValidation<UpdatePatientRequest>()
            .WithName("UpdatePatient");

        group.MapDelete("/{id:guid}", async (Guid id, IPatientService service, CancellationToken ct) =>
                (await service.DeactivateAsync(id, ct)).ToHttpResult())
            .WithName("DeactivatePatient");

        // 4.2 Add collaborator to patient
        group.MapPost("/{id:guid}/collaborators/{userId:guid}", async (
                Guid id, Guid userId, IPatientService service, CancellationToken ct) =>
                (await service.AddCollaboratorAsync(id, userId, ct)).ToHttpResult())
            .WithName("AddPatientCollaborator");
    }

    // ─── 4.4 + 4.5 Treatments ────────────────────────────────────────────────
    private static void MapTreatments(IEndpointRouteBuilder app)
    {
        var group = app.MapGroup("/patients/{patientId:guid}/treatments")
            .WithTags("Treatments").RequireAuthorization();

        group.MapGet("/", async (Guid patientId, [AsParameters] PageQuery paging, IPatientService service, CancellationToken ct) =>
                (await service.ListTreatmentsAsync(patientId, paging, ct)).ToHttpResult())
            .WithName("ListTreatments");

        group.MapPost("/", async (Guid patientId, CreateTreatmentRequest request, IPatientService service, CancellationToken ct) =>
            {
                var result = await service.CreateTreatmentAsync(patientId, request, ct);
                return result.Match(
                    created => Results.Created($"/patients/{patientId}/treatments/{created.Id}", created),
                    error => error.ToProblem());
            })
            .WithValidation<CreateTreatmentRequest>()
            .WithName("CreateTreatment");

        group.MapPut("/{treatmentId:guid}", async (Guid patientId, Guid treatmentId, UpdateTreatmentRequest request, IPatientService service, CancellationToken ct) =>
                (await service.UpdateTreatmentAsync(treatmentId, request, ct)).ToHttpResult())
            .WithValidation<UpdateTreatmentRequest>()
            .WithName("UpdateTreatment");

        // 4.5 Status patch — separate endpoint, separate DTO, clear intent
        group.MapPatch("/{treatmentId:guid}/status", async (Guid patientId, Guid treatmentId, UpdateTreatmentStatusRequest request, IPatientService service, CancellationToken ct) =>
                (await service.UpdateTreatmentStatusAsync(treatmentId, request, ct)).ToHttpResult())
            .WithName("UpdateTreatmentStatus");

        group.MapDelete("/{treatmentId:guid}", async (Guid patientId, Guid treatmentId, IPatientService service, CancellationToken ct) =>
                (await service.DeactivateTreatmentAsync(treatmentId, ct)).ToHttpResult())
            .WithName("DeactivateTreatment");
    }

    // ─── 4.6 Treatment details ────────────────────────────────────────────────
    private static void MapTreatmentDetails(IEndpointRouteBuilder app)
    {
        var group = app.MapGroup("/treatments/{treatmentId:guid}/details")
            .WithTags("TreatmentDetails").RequireAuthorization();

        group.MapPost("/", async (Guid treatmentId, CreateTreatmentDetailRequest request, IPatientService service, CancellationToken ct) =>
            {
                var result = await service.CreateTreatmentDetailAsync(treatmentId, request, ct);
                return result.Match(
                    created => Results.Created($"/treatments/{treatmentId}/details/{created.Id}", created),
                    error => error.ToProblem());
            })
            .WithValidation<CreateTreatmentDetailRequest>()
            .WithName("CreateTreatmentDetail");

        group.MapPut("/{detailId:guid}", async (Guid treatmentId, Guid detailId, UpdateTreatmentDetailRequest request, IPatientService service, CancellationToken ct) =>
                (await service.UpdateTreatmentDetailAsync(detailId, request, ct)).ToHttpResult())
            .WithValidation<UpdateTreatmentDetailRequest>()
            .WithName("UpdateTreatmentDetail");

        group.MapDelete("/{detailId:guid}", async (Guid treatmentId, Guid detailId, IPatientService service, CancellationToken ct) =>
                (await service.DeactivateTreatmentDetailAsync(detailId, ct)).ToHttpResult())
            .WithName("DeactivateTreatmentDetail");
    }

    // ─── 4.7 Treatment comments ───────────────────────────────────────────────
    private static void MapTreatmentComments(IEndpointRouteBuilder app)
    {
        var group = app.MapGroup("/treatments/{treatmentId:guid}/comments")
            .WithTags("TreatmentComments").RequireAuthorization();

        group.MapPost("/", async (Guid treatmentId, CreateTreatmentCommentRequest request,
                IPatientService service, ClaimsPrincipal principal, CancellationToken ct) =>
            {
                var result = await service.CreateTreatmentCommentAsync(treatmentId, request, principal, ct);
                return result.Match(
                    created => Results.Created($"/treatments/{treatmentId}/comments/{created.Id}", created),
                    error => error.ToProblem());
            })
            .WithValidation<CreateTreatmentCommentRequest>()
            .WithName("CreateTreatmentComment");

        group.MapPut("/{commentId:guid}", async (Guid treatmentId, Guid commentId, UpdateTreatmentCommentRequest request, IPatientService service, CancellationToken ct) =>
                (await service.UpdateTreatmentCommentAsync(commentId, request, ct)).ToHttpResult())
            .WithValidation<UpdateTreatmentCommentRequest>()
            .WithName("UpdateTreatmentComment");

        group.MapDelete("/{commentId:guid}", async (Guid treatmentId, Guid commentId, IPatientService service, CancellationToken ct) =>
                (await service.DeactivateTreatmentCommentAsync(commentId, ct)).ToHttpResult())
            .WithName("DeactivateTreatmentComment");
    }

    // ─── 4.8 Patient comments ─────────────────────────────────────────────────
    private static void MapPatientComments(IEndpointRouteBuilder app)
    {
        var group = app.MapGroup("/patients/{patientId:guid}/comments")
            .WithTags("PatientComments").RequireAuthorization();

        group.MapPost("/", async (Guid patientId, CreatePatientCommentRequest request,
                IPatientService service, ClaimsPrincipal principal, CancellationToken ct) =>
            {
                var result = await service.CreatePatientCommentAsync(patientId, request, principal, ct);
                return result.Match(
                    created => Results.Created($"/patients/{patientId}/comments/{created.Id}", created),
                    error => error.ToProblem());
            })
            .WithValidation<CreatePatientCommentRequest>()
            .WithName("CreatePatientComment");

        group.MapPut("/{commentId:guid}", async (Guid patientId, Guid commentId, UpdatePatientCommentRequest request, IPatientService service, CancellationToken ct) =>
                (await service.UpdatePatientCommentAsync(commentId, request, ct)).ToHttpResult())
            .WithValidation<UpdatePatientCommentRequest>()
            .WithName("UpdatePatientComment");

        group.MapDelete("/{commentId:guid}", async (Guid patientId, Guid commentId, IPatientService service, CancellationToken ct) =>
                (await service.DeactivatePatientCommentAsync(commentId, ct)).ToHttpResult())
            .WithName("DeactivatePatientComment");
    }
}
