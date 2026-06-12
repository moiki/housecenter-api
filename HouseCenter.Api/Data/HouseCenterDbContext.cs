using System.Linq.Expressions;
using HouseCenter.Api.Domain.Entities;
using Microsoft.EntityFrameworkCore;

namespace HouseCenter.Api.Data;

public class HouseCenterDbContext(DbContextOptions<HouseCenterDbContext> options) : DbContext(options)
{
    public DbSet<User> Users => Set<User>();
    public DbSet<Role> Roles => Set<Role>();
    public DbSet<Invitation> Invitations => Set<Invitation>();
    public DbSet<Clinic> Clinics => Set<Clinic>();
    public DbSet<WorkRoute> WorkRoutes => Set<WorkRoute>();
    public DbSet<Collaborator> Collaborators => Set<Collaborator>();
    public DbSet<Position> Positions => Set<Position>();
    public DbSet<Patient> Patients => Set<Patient>();
    public DbSet<PatientComment> PatientComments => Set<PatientComment>();
    public DbSet<Treatment> Treatments => Set<Treatment>();
    public DbSet<TreatmentDetail> TreatmentDetails => Set<TreatmentDetail>();
    public DbSet<TreatmentComment> TreatmentComments => Set<TreatmentComment>();

    protected override void OnModelCreating(ModelBuilder modelBuilder)
    {
        base.OnModelCreating(modelBuilder);

        modelBuilder.ApplyConfigurationsFromAssembly(typeof(HouseCenterDbContext).Assembly);

        // Global soft-delete filter: every query on a BaseEntity subtype automatically
        // excludes inactive records. Use .IgnoreQueryFilters() to bypass when needed.
        foreach (var entityType in modelBuilder.Model.GetEntityTypes())
        {
            if (typeof(BaseEntity).IsAssignableFrom(entityType.ClrType))
            {
                modelBuilder.Entity(entityType.ClrType)
                    .HasQueryFilter(BuildIsActiveFilter(entityType.ClrType));
            }
        }
    }

    public override Task<int> SaveChangesAsync(CancellationToken cancellationToken = default)
    {
        SetAuditDates();
        return base.SaveChangesAsync(cancellationToken);
    }

    private void SetAuditDates()
    {
        var entries = ChangeTracker.Entries<BaseEntity>()
            .Where(e => e.State is EntityState.Added or EntityState.Modified);

        foreach (var entry in entries)
        {
            entry.Entity.UpdatedDate = DateTime.UtcNow;

            if (entry.State == EntityState.Added)
                entry.Entity.CreatedDate = DateTime.UtcNow;
        }
    }

    private static LambdaExpression BuildIsActiveFilter(Type type)
    {
        var param = Expression.Parameter(type, "e");
        var prop = Expression.Property(param, nameof(BaseEntity.IsActive));
        var body = Expression.Equal(prop, Expression.Constant(true));
        return Expression.Lambda(body, param);
    }
}
