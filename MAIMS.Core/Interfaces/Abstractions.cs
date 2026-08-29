namespace MAIMS.Core.Interfaces;

/// <summary>
/// Generates human-readable asset codes (e.g., PW-VEH-00042) from a configurable scheme.
/// Default scheme is DEPT-CATEGORY-SEQ with zero-padded sequence per (department, category).
/// </summary>
public interface IAssetCodeGenerator
{
    /// <summary>Returns the next asset code for the given department + category.</summary>
    Task<string> GenerateAsync(long departmentId, long categoryId, CancellationToken ct = default);
}

/// <summary>
/// Abstraction over the EF Core DbContext so services can be tested without a real database.
/// </summary>
public interface IUnitOfWork : IDisposable
{
    /// <summary>Begins a new transaction. Caller is responsible for Commit/Dispose.</summary>
    Task<IUnitOfWorkTransaction> BeginTransactionAsync(CancellationToken ct = default);

    /// <summary>Saves changes via the EF Core audit interceptor (auto-fills audit + log rows).</summary>
    Task<int> SaveChangesAsync(CancellationToken ct = default);
}

public interface IUnitOfWorkTransaction : IDisposable, IAsyncDisposable
{
    Task CommitAsync(CancellationToken ct = default);
    Task RollbackAsync(CancellationToken ct = default);
}

/// <summary>
/// Generic repository for the most common CRUD operations.
/// Specific repositories (AssetRepository, ItemRepository, etc.) extend this.
/// </summary>
public interface IRepository<T> where T : class
{
    Task<T?> GetByIdAsync(long id, CancellationToken ct = default);
    Task<IReadOnlyList<T>> ListAsync(CancellationToken ct = default);
    Task AddAsync(T entity, CancellationToken ct = default);
    void Update(T entity);
    void Remove(T entity);
}
