using HouseCenter.Api.Domain.Entities;

namespace HouseCenter.Api.Common.Persistence;

/// <summary>
/// Thin generic repository over EF Core. Covers the mechanical CRUD surface;
/// complex read queries (projections, aggregations) go straight to the DbContext —
/// wrapping those would only hide capability behind a poorer API.
/// </summary>
public interface IRepository<T> where T : BaseEntity
{
    Task<T?> GetByIdAsync(Guid id, CancellationToken ct = default);
    Task<List<T>> ListAsync(CancellationToken ct = default);
    Task<bool> ExistsAsync(Guid id, CancellationToken ct = default);

    void Add(T entity);
    void Remove(T entity);

    /// <summary>Escape hatch for composing queries (Include, Where, projection) without widening this interface.</summary>
    IQueryable<T> Query();

    /// <summary>Unit of Work commit — delegates to DbContext.SaveChangesAsync (audit dates included).</summary>
    Task<int> SaveChangesAsync(CancellationToken ct = default);
}
