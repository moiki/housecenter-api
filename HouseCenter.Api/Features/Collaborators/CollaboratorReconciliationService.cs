using HouseCenter.Api.Common.Results;
using HouseCenter.Api.Data;
using HouseCenter.Api.Domain.Constants;
using Microsoft.EntityFrameworkCore;

namespace HouseCenter.Api.Features.Collaborators;

public interface ICollaboratorReconciliationService
{
    Task<Result<ReconciliationResponse>> ReconcileAsync(CancellationToken ct);
}

/// <summary>
/// Idempotent real-data backfill for the Collaborator &lt;-&gt; User link (Phase 3 of
/// collaborator-user-link). Matches unlinked Collaborator roster rows to unlinked Member
/// Users by case-insensitive email, one-to-one, and never fabricates a row on either side:
/// a Collaborator with no matching User stays legacy (design.md Decision 5 — a valid
/// permanent state), a Member User with no matching Collaborator is reported here (and
/// warn-logged) for admin follow-up via POST /collaborators. Re-running is a no-op for
/// pairs already linked — see design.md Decision 2 for the exact algorithm.
/// </summary>
public sealed class CollaboratorReconciliationService(
    HouseCenterDbContext db,
    ILogger<CollaboratorReconciliationService> logger) : ICollaboratorReconciliationService
{
    public async Task<Result<ReconciliationResponse>> ReconcileAsync(CancellationToken ct)
    {
        // Step 1 (Decision 2, step 1): normalize every Collaborator.Email to lowercase —
        // including inactive/legacy rows, mirroring the raw-SQL "UPDATE ... SET Email =
        // lower(Email)" semantics — so the equality match below never misses a case-only
        // difference. Done as a tracked in-memory loop, not ExecuteUpdateAsync: the
        // InMemory EF provider (unit tests) does not support bulk ExecuteUpdate/Delete.
        var allCollaborators = await db.Collaborators.IgnoreQueryFilters().ToListAsync(ct);
        foreach (var collaborator in allCollaborators)
        {
            var normalized = collaborator.Email.Trim().ToLowerInvariant();
            if (collaborator.Email != normalized)
                collaborator.Email = normalized;
        }

        // Snapshot of Users already linked BEFORE this pass, derived from the roster we
        // just normalized (avoids a second correlated query against Collaborators).
        var linkedUserIds = allCollaborators
            .Where(c => c.UserId is not null)
            .Select(c => c.UserId!.Value)
            .ToHashSet();

        var memberUsers = await db.Users
            .Include(u => u.Roles)
            .Where(u => u.Roles.Any(r => r.Name == RoleNames.Member))
            .ToListAsync(ct);

        var unlinkedMemberUsersByEmail = memberUsers
            .Where(u => !linkedUserIds.Contains(u.Id))
            .GroupBy(u => u.Email.Trim().ToLowerInvariant())
            .ToDictionary(g => g.Key, g => g.First());

        // Step 2 (Decision 2, step 2): match each unlinked ACTIVE Collaborator to an
        // unlinked Member User with an equal lowercased email. One-to-one — once a User is
        // claimed in this pass it cannot be claimed again (guards against a pre-existing
        // duplicate-email Collaborator pair; the surviving duplicate is exactly what
        // Migration B's unique index is designed to still reject).
        var matched = 0;
        var claimedUserIds = new HashSet<Guid>();
        foreach (var collaborator in allCollaborators.Where(c => c.IsActive && c.UserId is null))
        {
            if (unlinkedMemberUsersByEmail.TryGetValue(collaborator.Email, out var user) &&
                claimedUserIds.Add(user.Id))
            {
                collaborator.UserId = user.Id;
                matched++;
            }
        }

        await db.SaveChangesAsync(ct);

        // Step 3 (Decision 2, step 3b): Member Users still without a Collaborator after
        // matching are a transient anomaly (Decision 5) — reported + logged, never
        // stubbed (no clinic context to assign, same reasoning as AuthService.SignupAsync).
        var stillUnlinked = memberUsers
            .Where(u => !linkedUserIds.Contains(u.Id) && !claimedUserIds.Contains(u.Id))
            .Select(u => new UnlinkedMemberUserDto(u.Id, u.Email, u.FirstName, u.LastName))
            .ToList();

        foreach (var user in stillUnlinked)
            logger.LogWarning(
                "Reconciliation found Member User {UserId} ({Email}) with no matching Collaborator — an admin must link it later via POST /collaborators",
                user.Id, user.Email);

        logger.LogInformation("Reconciliation matched {Matched} Collaborator(s) to Member Users", matched);

        return new ReconciliationResponse(matched, stillUnlinked);
    }
}
