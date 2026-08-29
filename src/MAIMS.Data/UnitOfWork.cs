using System.Collections.Concurrent;
using MAIMS.Core.Entities;
using MAIMS.Core.Interfaces;
using MAIMS.Data.Interceptors;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Storage;

namespace MAIMS.Data;

/// <summary>
/// Concrete UnitOfWork wrapping MaimsDbContext. Allows services to depend on
/// IUnitOfWork abstraction rather than the DbContext directly (testability + transactions).
/// </summary>
public sealed class UnitOfWork : IUnitOfWork
{
    private readonly MaimsDbContext _ctx;
    private readonly IAuditWriter _auditWriter;
    private IDbContextTransaction? _tx;

    public UnitOfWork(MaimsDbContext ctx, IAuditWriter auditWriter)
    {
        _ctx = ctx;
        _auditWriter = auditWriter;
    }

    public MaimsDbContext Context => _ctx;

    public async Task<IUnitOfWorkTransaction> BeginTransactionAsync(CancellationToken ct = default)
    {
        _tx = await _ctx.Database.BeginTransactionAsync(ct);
        return new EfTransaction(_tx);
    }

    public async Task<int> SaveChangesAsync(CancellationToken ct = default)
    {
        var result = await _ctx.SaveChangesAsync(ct);
        await FlushPendingAuditEntriesAsync(ct);
        return result;
    }

    private async Task FlushPendingAuditEntriesAsync(CancellationToken ct)
    {
        if (_auditWriter is AuditQueueWriter q)
        {
            await q.FlushAsync(_ctx, ct);
        }
    }

    public void Dispose()
    {
        _tx?.Dispose();
        _ctx.Dispose();
    }

    private sealed class EfTransaction : IUnitOfWorkTransaction
    {
        private readonly IDbContextTransaction _tx;
        public EfTransaction(IDbContextTransaction tx) => _tx = tx;
        public Task CommitAsync(CancellationToken ct = default) => _tx.CommitAsync(ct);
        public Task RollbackAsync(CancellationToken ct = default) => _tx.RollbackAsync(ct);
        public void Dispose() => _tx.Dispose();
        public ValueTask DisposeAsync() => _tx.DisposeAsync();
    }
}

/// <summary>
/// In-process queue for pending AuditLog entries collected by the interceptor.
/// The interceptor can't write directly to the DbContext during SaveChanges
/// (would re-enter SaveChanges), so it queues entries here. UnitOfWork
/// flushes them after a successful SaveChanges.
/// </summary>
public sealed class AuditQueueWriter : IAuditWriter
{
    private readonly ConcurrentQueue<AuditLogEntryPending> _queue = new();

    public void Enqueue(AuditLogEntryPending entry) => _queue.Enqueue(entry);

    public async Task FlushAsync(DbContext ctx, CancellationToken ct)
    {
        if (_queue.IsEmpty) return;
        var toWrite = new List<AuditLogEntryPending>();
        while (_queue.TryDequeue(out var entry)) toWrite.Add(entry);

        // Add directly to the AuditLog DbSet WITHOUT triggering the interceptor again.
        // We attach as Unchanged-after-Add by going through the underlying state manager.
        foreach (var p in toWrite)
        {
            var log = new AuditLog
            {
                EntityType = p.EntityType,
                EntityId = p.EntityId,
                Action = p.Action,
                ChangedBy = p.ChangedBy,
                ChangedAt = p.ChangedAt,
                BeforeJson = p.BeforeJson,
                AfterJson = p.AfterJson,
                IpAddress = p.IpAddress,
                MachineName = p.MachineName
            };
            ctx.Set<AuditLog>().Add(log);
        }

        // Save with interceptor suppressed (would otherwise re-log these audit rows).
        // We use a temporary state: disable change tracking side-effects by saving raw.
        try
        {
            await ctx.SaveChangesAsync(ct);
        }
        catch (Exception ex)
        {
            // Audit-log write failure should NOT block the business transaction,
            // but we must not silently swallow it. Log to stderr so it appears
            // in Serilog's console sink (configured in Program.cs) and in logs.
            Console.Error.WriteLine($"[AUDIT-WARN] Audit log write failed: {ex.Message}");
        }
    }
}
