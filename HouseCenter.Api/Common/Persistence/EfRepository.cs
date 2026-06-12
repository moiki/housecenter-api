using HouseCenter.Api.Data;
using HouseCenter.Api.Domain.Entities;
using Microsoft.EntityFrameworkCore;

namespace HouseCenter.Api.Common.Persistence;

public sealed class EfRepository<T>(HouseCenterDbContext db) : IRepository<T> where T : BaseEntity
{
    private readonly DbSet<T> _set = db.Set<T>();

    public async Task<T?> GetByIdAsync(Guid id, CancellationToken ct = default) =>
        await _set.SingleOrDefaultAsync(e => e.Id == id, ct);

    public async Task<List<T>> ListAsync(CancellationToken ct = default) =>
        await _set.AsNoTracking().ToListAsync(ct);

    public async Task<bool> ExistsAsync(Guid id, CancellationToken ct = default) =>
        await _set.AnyAsync(e => e.Id == id, ct);

    public void Add(T entity) => _set.Add(entity);

    public void Remove(T entity) => _set.Remove(entity);

    public IQueryable<T> Query() => _set;

    public Task<int> SaveChangesAsync(CancellationToken ct = default) => db.SaveChangesAsync(ct);
}
