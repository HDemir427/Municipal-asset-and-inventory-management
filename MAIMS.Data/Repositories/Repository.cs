using MAIMS.Core.Interfaces;
using Microsoft.EntityFrameworkCore;

namespace MAIMS.Data.Repositories;

/// <summary>
/// Generic EF Core repository. Specific repositories (AssetRepository, etc.) can extend
/// this or compose it. Read queries use AsNoTracking for performance.
/// </summary>
public class Repository<T> : IRepository<T> where T : class
{
    protected readonly DbContext Context;
    protected readonly DbSet<T> Set;

    public Repository(DbContext context)
    {
        Context = context;
        Set = context.Set<T>();
    }

    public virtual Task<T?> GetByIdAsync(long id, CancellationToken ct = default)
        => Set.FindAsync(new object[] { id }, ct).AsTask();

    public virtual async Task<IReadOnlyList<T>> ListAsync(CancellationToken ct = default)
        => await Set.AsNoTracking().ToListAsync(ct);

    public virtual Task AddAsync(T entity, CancellationToken ct = default)
        => Set.AddAsync(entity, ct).AsTask();

    public virtual void Update(T entity) => Set.Update(entity);

    public virtual void Remove(T entity) => Set.Remove(entity);
}
