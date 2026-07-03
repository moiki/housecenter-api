using System.Linq.Expressions;
using HouseCenter.Api.Domain.Entities;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Storage.ValueConversion;

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
    public DbSet<AttentionSession> AttentionSessions => Set<AttentionSession>();
    public DbSet<MedicalConsultation> MedicalConsultations => Set<MedicalConsultation>();
    public DbSet<ConsultationMessage> ConsultationMessages => Set<ConsultationMessage>();
    public DbSet<Notification> Notifications => Set<Notification>();
    public DbSet<PushSubscription> PushSubscriptions => Set<PushSubscription>();
    public DbSet<FileAttachment> FileAttachments => Set<FileAttachment>();
    public DbSet<HelpTopic> HelpTopics => Set<HelpTopic>();

    protected override void OnModelCreating(ModelBuilder modelBuilder)
    {
        base.OnModelCreating(modelBuilder);

        modelBuilder.ApplyConfigurationsFromAssembly(typeof(HouseCenterDbContext).Assembly);

        foreach (var entityType in modelBuilder.Model.GetEntityTypes())
        {
            // Global soft-delete filter: every query on a BaseEntity subtype automatically
            // excludes inactive records. Use .IgnoreQueryFilters() to bypass when needed.
            if (typeof(BaseEntity).IsAssignableFrom(entityType.ClrType))
            {
                modelBuilder.Entity(entityType.ClrType)
                    .HasQueryFilter(BuildIsActiveFilter(entityType.ClrType));
            }

            // Npgsql requires DateTime.Kind == Utc for "timestamp with time zone" columns.
            // Client JSON payloads without a "Z"/offset deserialize as Kind=Unspecified, which
            // throws at SaveChanges. Tag every DateTime property as Utc globally instead of
            // relying on each request handler to do it — new entities get this for free.
            foreach (var property in entityType.GetProperties())
            {
                if (property.GetValueConverter() is not null) continue;

                if (property.ClrType == typeof(DateTime))
                    property.SetValueConverter(UtcDateTimeConverter);
                else if (property.ClrType == typeof(DateTime?))
                    property.SetValueConverter(UtcNullableDateTimeConverter);
            }
        }
    }

    private static readonly ValueConverter<DateTime, DateTime> UtcDateTimeConverter = new(
        v => v.Kind == DateTimeKind.Local ? v.ToUniversalTime() : DateTime.SpecifyKind(v, DateTimeKind.Utc),
        v => DateTime.SpecifyKind(v, DateTimeKind.Utc));

    private static readonly ValueConverter<DateTime?, DateTime?> UtcNullableDateTimeConverter = new(
        v => v.HasValue ? (v.Value.Kind == DateTimeKind.Local ? v.Value.ToUniversalTime() : DateTime.SpecifyKind(v.Value, DateTimeKind.Utc)) : v,
        v => v.HasValue ? DateTime.SpecifyKind(v.Value, DateTimeKind.Utc) : v);

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
