using System.Security.Claims;
using HouseCenter.Api.Common.Pagination;
using HouseCenter.Api.Common.Persistence;
using HouseCenter.Api.Common.Results;
using HouseCenter.Api.Data;
using HouseCenter.Api.Domain.Entities;
using Microsoft.EntityFrameworkCore;

namespace HouseCenter.Api.Features.Patients;

public interface IPatientService
{
    Task<Result<PagedResult<PatientResponse>>> ListAsync(PageQuery paging, CancellationToken ct);
    Task<Result<PatientResponse>> GetByIdAsync(Guid id, CancellationToken ct);
    Task<Result<PatientResponse>> CreateAsync(CreatePatientRequest request, CancellationToken ct);
    Task<Result<PatientResponse>> UpdateAsync(Guid id, UpdatePatientRequest request, CancellationToken ct);
    Task<Result> DeactivateAsync(Guid id, CancellationToken ct);
    Task<Result> AddCollaboratorAsync(Guid patientId, Guid userId, CancellationToken ct);
    Task<Result<PatientFullSummaryResponse>> GetFullSummaryAsync(Guid id, CancellationToken ct);

    // Treatments
    Task<Result<PagedResult<TreatmentResponse>>> ListTreatmentsAsync(Guid patientId, PageQuery paging, CancellationToken ct);
    Task<Result<TreatmentResponse>> CreateTreatmentAsync(Guid patientId, CreateTreatmentRequest request, CancellationToken ct);
    Task<Result<TreatmentResponse>> UpdateTreatmentAsync(Guid treatmentId, UpdateTreatmentRequest request, CancellationToken ct);
    Task<Result<TreatmentResponse>> UpdateTreatmentStatusAsync(Guid treatmentId, UpdateTreatmentStatusRequest request, CancellationToken ct);
    Task<Result> DeactivateTreatmentAsync(Guid treatmentId, CancellationToken ct);

    // Treatment details
    Task<Result<TreatmentDetailResponse>> CreateTreatmentDetailAsync(Guid treatmentId, CreateTreatmentDetailRequest request, CancellationToken ct);
    Task<Result<TreatmentDetailResponse>> UpdateTreatmentDetailAsync(Guid detailId, UpdateTreatmentDetailRequest request, CancellationToken ct);
    Task<Result> DeactivateTreatmentDetailAsync(Guid detailId, CancellationToken ct);

    // Treatment comments
    Task<Result<TreatmentCommentResponse>> CreateTreatmentCommentAsync(Guid treatmentId, CreateTreatmentCommentRequest request, ClaimsPrincipal user, CancellationToken ct);
    Task<Result<TreatmentCommentResponse>> UpdateTreatmentCommentAsync(Guid commentId, UpdateTreatmentCommentRequest request, CancellationToken ct);
    Task<Result> DeactivateTreatmentCommentAsync(Guid commentId, CancellationToken ct);

    // Patient comments
    Task<Result<PatientCommentResponse>> CreatePatientCommentAsync(Guid patientId, CreatePatientCommentRequest request, ClaimsPrincipal user, CancellationToken ct);
    Task<Result<PatientCommentResponse>> UpdatePatientCommentAsync(Guid commentId, UpdatePatientCommentRequest request, CancellationToken ct);
    Task<Result> DeactivatePatientCommentAsync(Guid commentId, CancellationToken ct);
}

public sealed class PatientService(
    IRepository<Patient> patients,
    IRepository<Treatment> treatments,
    IRepository<TreatmentDetail> details,
    IRepository<TreatmentComment> treatmentComments,
    IRepository<PatientComment> patientComments,
    HouseCenterDbContext db) : IPatientService
{
    private static readonly Error PatientNotFound = Error.NotFound("patients.not_found", "Patient not found");
    private static readonly Error TreatmentNotFound = Error.NotFound("treatments.not_found", "Treatment not found");
    private static readonly Error DetailNotFound = Error.NotFound("treatment_details.not_found", "Treatment detail not found");
    private static readonly Error CommentNotFound = Error.NotFound("comments.not_found", "Comment not found");

    // ─── Patients ────────────────────────────────────────────────────────────

    public async Task<Result<PagedResult<PatientResponse>>> ListAsync(PageQuery paging, CancellationToken ct)
    {
        var query = db.Patients
            .AsNoTracking()
            .OrderBy(p => p.FirstName).ThenBy(p => p.LastName)
            .Select(p => new PatientResponse(
                p.Id, p.FirstName, p.LastName, p.Profile, p.BirthDate,
                p.Gender.ToString(), p.Country, p.State, p.City,
                p.Address, p.Description, p.ClinicId, p.WorkRouteId, p.IsActive));

        return await query.ToPagedResultAsync(paging, ct);
    }

    public async Task<Result<PatientResponse>> GetByIdAsync(Guid id, CancellationToken ct)
    {
        var patient = await db.Patients
            .AsNoTracking()
            .Where(p => p.Id == id)
            .Select(p => new PatientResponse(
                p.Id, p.FirstName, p.LastName, p.Profile, p.BirthDate,
                p.Gender.ToString(), p.Country, p.State, p.City,
                p.Address, p.Description, p.ClinicId, p.WorkRouteId, p.IsActive))
            .SingleOrDefaultAsync(ct);

        return patient is not null ? patient : PatientNotFound;
    }

    public async Task<Result<PatientResponse>> CreateAsync(CreatePatientRequest request, CancellationToken ct)
    {
        var patient = new Patient
        {
            FirstName = request.FirstName,
            LastName = request.LastName,
            Profile = request.Profile,
            BirthDate = request.BirthDate,
            Gender = request.Gender,
            Country = request.Country,
            State = request.State,
            City = request.City,
            Address = request.Address,
            Description = request.Description,
            ClinicId = request.ClinicId,
            WorkRouteId = request.WorkRouteId
        };

        patients.Add(patient);
        await patients.SaveChangesAsync(ct);

        return ToPatientResponse(patient);
    }

    public async Task<Result<PatientResponse>> UpdateAsync(Guid id, UpdatePatientRequest request, CancellationToken ct)
    {
        var patient = await patients.GetByIdAsync(id, ct);
        if (patient is null) return PatientNotFound;

        patient.FirstName = request.FirstName;
        patient.LastName = request.LastName;
        patient.Profile = request.Profile;
        patient.BirthDate = request.BirthDate;
        patient.Gender = request.Gender;
        patient.Country = request.Country;
        patient.State = request.State;
        patient.City = request.City;
        patient.Address = request.Address;
        patient.Description = request.Description;
        patient.ClinicId = request.ClinicId;
        patient.WorkRouteId = request.WorkRouteId;

        await patients.SaveChangesAsync(ct);
        return ToPatientResponse(patient);
    }

    public async Task<Result> DeactivateAsync(Guid id, CancellationToken ct)
    {
        var patient = await patients.GetByIdAsync(id, ct);
        if (patient is null) return PatientNotFound;

        patient.IsActive = false;
        await patients.SaveChangesAsync(ct);
        return Result.Success();
    }

    public async Task<Result> AddCollaboratorAsync(Guid patientId, Guid userId, CancellationToken ct)
    {
        var patient = await patients.Query()
            .Include(p => p.Collaborators)
            .SingleOrDefaultAsync(p => p.Id == patientId, ct);
        if (patient is null) return PatientNotFound;

        var user = await db.Users.FindAsync([userId], ct);
        if (user is null) return Error.NotFound("users.not_found", "User not found");

        if (patient.Collaborators.Any(c => c.Id == userId))
            return Error.Conflict("patients.collaborator_exists", "User is already a collaborator of this patient");

        patient.Collaborators.Add(user);
        await patients.SaveChangesAsync(ct);
        return Result.Success();
    }

    // Task.WhenAll — launches 2 independent DB queries concurrently, awaits both.
    // The patient query is the "gate": if not found, we short-circuit before awaiting the rest.
    public async Task<Result<PatientFullSummaryResponse>> GetFullSummaryAsync(Guid id, CancellationToken ct)
    {
        var patientTask = db.Patients
            .AsNoTracking()
            .Where(p => p.Id == id)
            .Select(p => new PatientResponse(
                p.Id, p.FirstName, p.LastName, p.Profile, p.BirthDate,
                p.Gender.ToString(), p.Country, p.State, p.City,
                p.Address, p.Description, p.ClinicId, p.WorkRouteId, p.IsActive))
            .SingleOrDefaultAsync(ct);

        var treatmentsTask = db.Treatments
            .AsNoTracking()
            .Where(t => t.PatientId == id)
            .OrderByDescending(t => t.StartDate)
            .Select(t => new TreatmentSummaryDto(
                t.Id, t.Name, t.Description, t.Status.ToString(), t.StartDate, t.EndDate))
            .ToListAsync(ct);

        var commentsTask = db.PatientComments
            .AsNoTracking()
            .Where(c => c.PatientId == id)
            .OrderByDescending(c => c.CreatedDate)
            .Select(c => new PatientCommentDto(
                c.Id, c.Body, c.Type.ToString(), c.Status.ToString(), c.UserId, c.CreatedDate))
            .ToListAsync(ct);

        await Task.WhenAll(patientTask, treatmentsTask, commentsTask);

        var patient = await patientTask;
        if (patient is null) return PatientNotFound;

        return new PatientFullSummaryResponse(patient, await treatmentsTask, await commentsTask);
    }

    // ─── Treatments ──────────────────────────────────────────────────────────

    public async Task<Result<PagedResult<TreatmentResponse>>> ListTreatmentsAsync(
        Guid patientId, PageQuery paging, CancellationToken ct)
    {
        if (!await db.Patients.AnyAsync(p => p.Id == patientId, ct))
            return PatientNotFound;

        var query = db.Treatments
            .AsNoTracking()
            .Where(t => t.PatientId == patientId)
            .OrderByDescending(t => t.StartDate)
            .Select(t => new TreatmentResponse(
                t.Id, t.PatientId, t.Name, t.Description, t.Profile,
                t.Status.ToString(), t.StartDate, t.EndDate, t.IsActive));

        return await query.ToPagedResultAsync(paging, ct);
    }

    public async Task<Result<TreatmentResponse>> CreateTreatmentAsync(
        Guid patientId, CreateTreatmentRequest request, CancellationToken ct)
    {
        if (!await db.Patients.AnyAsync(p => p.Id == patientId, ct))
            return PatientNotFound;

        var treatment = new Treatment
        {
            PatientId = patientId,
            Name = request.Name,
            Description = request.Description,
            Profile = request.Profile,
            StartDate = request.StartDate,
            EndDate = request.EndDate
        };

        treatments.Add(treatment);
        await treatments.SaveChangesAsync(ct);
        return ToTreatmentResponse(treatment);
    }

    public async Task<Result<TreatmentResponse>> UpdateTreatmentAsync(
        Guid treatmentId, UpdateTreatmentRequest request, CancellationToken ct)
    {
        var treatment = await treatments.GetByIdAsync(treatmentId, ct);
        if (treatment is null) return TreatmentNotFound;

        treatment.Name = request.Name;
        treatment.Description = request.Description;
        treatment.Profile = request.Profile;
        treatment.StartDate = request.StartDate;
        treatment.EndDate = request.EndDate;

        await treatments.SaveChangesAsync(ct);
        return ToTreatmentResponse(treatment);
    }

    public async Task<Result<TreatmentResponse>> UpdateTreatmentStatusAsync(
        Guid treatmentId, UpdateTreatmentStatusRequest request, CancellationToken ct)
    {
        var treatment = await treatments.GetByIdAsync(treatmentId, ct);
        if (treatment is null) return TreatmentNotFound;

        treatment.Status = request.Status;
        await treatments.SaveChangesAsync(ct);
        return ToTreatmentResponse(treatment);
    }

    public async Task<Result> DeactivateTreatmentAsync(Guid treatmentId, CancellationToken ct)
    {
        var treatment = await treatments.GetByIdAsync(treatmentId, ct);
        if (treatment is null) return TreatmentNotFound;

        treatment.IsActive = false;
        await treatments.SaveChangesAsync(ct);
        return Result.Success();
    }

    // ─── Treatment details ───────────────────────────────────────────────────

    public async Task<Result<TreatmentDetailResponse>> CreateTreatmentDetailAsync(
        Guid treatmentId, CreateTreatmentDetailRequest request, CancellationToken ct)
    {
        if (!await db.Treatments.AnyAsync(t => t.Id == treatmentId, ct))
            return TreatmentNotFound;

        var detail = new TreatmentDetail
        {
            TreatmentId = treatmentId,
            Name = request.Name,
            Description = request.Description,
            Profile = request.Profile,
            TreatmentDate = request.TreatmentDate
        };

        details.Add(detail);
        await details.SaveChangesAsync(ct);
        return ToDetailResponse(detail);
    }

    public async Task<Result<TreatmentDetailResponse>> UpdateTreatmentDetailAsync(
        Guid detailId, UpdateTreatmentDetailRequest request, CancellationToken ct)
    {
        var detail = await details.GetByIdAsync(detailId, ct);
        if (detail is null) return DetailNotFound;

        detail.Name = request.Name;
        detail.Description = request.Description;
        detail.Profile = request.Profile;
        detail.TreatmentDate = request.TreatmentDate;

        await details.SaveChangesAsync(ct);
        return ToDetailResponse(detail);
    }

    public async Task<Result> DeactivateTreatmentDetailAsync(Guid detailId, CancellationToken ct)
    {
        var detail = await details.GetByIdAsync(detailId, ct);
        if (detail is null) return DetailNotFound;

        detail.IsActive = false;
        await details.SaveChangesAsync(ct);
        return Result.Success();
    }

    // ─── Treatment comments ──────────────────────────────────────────────────

    public async Task<Result<TreatmentCommentResponse>> CreateTreatmentCommentAsync(
        Guid treatmentId, CreateTreatmentCommentRequest request, ClaimsPrincipal principal, CancellationToken ct)
    {
        if (!await db.Treatments.AnyAsync(t => t.Id == treatmentId, ct))
            return TreatmentNotFound;

        var userId = Guid.Parse(principal.FindFirstValue(ClaimTypes.NameIdentifier)!);
        var comment = new TreatmentComment
        {
            TreatmentId = treatmentId,
            UserId = userId,
            Body = request.Body,
            Type = request.Type
        };

        treatmentComments.Add(comment);
        await treatmentComments.SaveChangesAsync(ct);
        return ToTreatmentCommentResponse(comment);
    }

    public async Task<Result<TreatmentCommentResponse>> UpdateTreatmentCommentAsync(
        Guid commentId, UpdateTreatmentCommentRequest request, CancellationToken ct)
    {
        var comment = await treatmentComments.GetByIdAsync(commentId, ct);
        if (comment is null) return CommentNotFound;

        comment.Body = request.Body;
        comment.Type = request.Type;
        comment.Status = request.Status;

        await treatmentComments.SaveChangesAsync(ct);
        return ToTreatmentCommentResponse(comment);
    }

    public async Task<Result> DeactivateTreatmentCommentAsync(Guid commentId, CancellationToken ct)
    {
        var comment = await treatmentComments.GetByIdAsync(commentId, ct);
        if (comment is null) return CommentNotFound;

        comment.IsActive = false;
        await treatmentComments.SaveChangesAsync(ct);
        return Result.Success();
    }

    // ─── Patient comments ────────────────────────────────────────────────────

    public async Task<Result<PatientCommentResponse>> CreatePatientCommentAsync(
        Guid patientId, CreatePatientCommentRequest request, ClaimsPrincipal principal, CancellationToken ct)
    {
        if (!await db.Patients.AnyAsync(p => p.Id == patientId, ct))
            return PatientNotFound;

        var userId = Guid.Parse(principal.FindFirstValue(ClaimTypes.NameIdentifier)!);
        var comment = new PatientComment
        {
            PatientId = patientId,
            UserId = userId,
            Body = request.Body,
            Type = request.Type
        };

        patientComments.Add(comment);
        await patientComments.SaveChangesAsync(ct);
        return ToPatientCommentResponse(comment);
    }

    public async Task<Result<PatientCommentResponse>> UpdatePatientCommentAsync(
        Guid commentId, UpdatePatientCommentRequest request, CancellationToken ct)
    {
        var comment = await patientComments.GetByIdAsync(commentId, ct);
        if (comment is null) return CommentNotFound;

        comment.Body = request.Body;
        comment.Type = request.Type;
        comment.Status = request.Status;

        await patientComments.SaveChangesAsync(ct);
        return ToPatientCommentResponse(comment);
    }

    public async Task<Result> DeactivatePatientCommentAsync(Guid commentId, CancellationToken ct)
    {
        var comment = await patientComments.GetByIdAsync(commentId, ct);
        if (comment is null) return CommentNotFound;

        comment.IsActive = false;
        await patientComments.SaveChangesAsync(ct);
        return Result.Success();
    }

    // ─── Mapping helpers ─────────────────────────────────────────────────────

    private static PatientResponse ToPatientResponse(Patient p) => new(
        p.Id, p.FirstName, p.LastName, p.Profile, p.BirthDate,
        p.Gender.ToString(), p.Country, p.State, p.City,
        p.Address, p.Description, p.ClinicId, p.WorkRouteId, p.IsActive);

    private static TreatmentResponse ToTreatmentResponse(Treatment t) => new(
        t.Id, t.PatientId, t.Name, t.Description, t.Profile,
        t.Status.ToString(), t.StartDate, t.EndDate, t.IsActive);

    private static TreatmentDetailResponse ToDetailResponse(TreatmentDetail d) => new(
        d.Id, d.TreatmentId, d.Name, d.Description, d.Profile, d.TreatmentDate, d.IsActive);

    private static TreatmentCommentResponse ToTreatmentCommentResponse(TreatmentComment c) => new(
        c.Id, c.TreatmentId, c.Body, c.Type.ToString(), c.Status.ToString(), c.UserId, c.IsActive);

    private static PatientCommentResponse ToPatientCommentResponse(PatientComment c) => new(
        c.Id, c.PatientId, c.Body, c.Type.ToString(), c.Status.ToString(), c.UserId, c.IsActive);
}
