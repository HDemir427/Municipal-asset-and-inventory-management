using System.Collections.Concurrent;
using System.Text.Json;
using MAIMS.Core.Abstractions;
using MAIMS.Core.Entities;
using MAIMS.Core.Enums;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.ChangeTracking;
using Microsoft.EntityFrameworkCore.Diagnostics;

namespace MAIMS.Data.Interceptors;

/// <summary>
/// EF Core interceptor that automatically:
///   1. Fills CreatedAt/CreatedBy/UpdatedAt/UpdatedBy on every auditable entity.
///   2. Sets IsDeleted/DeletedAt on soft-delete (when state == Deleted).
///   3. Writes an immutable AuditLog row for every CUD operation on tracked entities.
///
/// Audit log entries are written in SavedChangesAsync (AFTER SaveChanges)
/// so that auto-increment IDs are already populated by the database. The list
/// of changed entries is captured in SavingChangesAsync (BEFORE SaveChanges)
/// into a per-call list, then re-walked in SavedChangesAsync to read the
/// now-populated IDs.
///
/// This avoids the previous bug where IDs were read as 0 (or, after JSON
/// serialization, appeared as negative numbers) because auto-increment IDs
/// are only assigned by the DB at INSERT time.
/// </summary>
public sealed class AuditSaveChangesInterceptor : SaveChangesInterceptor
{
    private static readonly JsonSerializerOptions JsonOpts = new()
    {
        WriteIndented = false,
        ReferenceHandler = System.Text.Json.Serialization.ReferenceHandler.IgnoreCycles
    };

    // Per-save snapshot of changed entries (EntityEntry + state + before/after JSON).
    // AsyncLocal<> flows correctly across async/await continuations (unlike ThreadStatic
    // which can lose data when the continuation runs on a different thread).
    private static readonly AsyncLocal<List<PendingEntry>?> t_pending = new();

    private readonly ICurrentSession _session;
    private readonly Lazy<IAuditWriter> _auditWriter;

    public AuditSaveChangesInterceptor(ICurrentSession session, Lazy<IAuditWriter> auditWriter)
    {
        _session = session;
        _auditWriter = auditWriter;
    }

    public override InterceptionResult<int> SavingChanges(DbContextEventData eventData, InterceptionResult<int> result)
    {
        if (eventData.Context is not null) ApplyAuditFieldsAndSnapshot(eventData.Context);
        return base.SavingChanges(eventData, result);
    }

    public override ValueTask<InterceptionResult<int>> SavingChangesAsync(
        DbContextEventData eventData,
        InterceptionResult<int> result,
        CancellationToken ct = default)
    {
        if (eventData.Context is not null) ApplyAuditFieldsAndSnapshot(eventData.Context);
        return base.SavingChangesAsync(eventData, result, ct);
    }

    public override int SavedChanges(SaveChangesCompletedEventData eventData, int result)
    {
        if (eventData.Context is not null) FlushAuditEntries();
        return result;
    }

    public override ValueTask<int> SavedChangesAsync(
        SaveChangesCompletedEventData eventData,
        int result,
        CancellationToken ct = default)
    {
        if (eventData.Context is not null) FlushAuditEntries();
        return new ValueTask<int>(result);
    }

    /// <summary>
    /// Fills audit fields (CreatedAt/CreatedBy/UpdatedAt/UpdatedBy/IsDeleted) AND
    /// snapshots the list of changed entries + their original state into the
    /// thread-local list. Snapshots are taken BEFORE SaveChanges so the OriginalValues
    /// are still the pre-update values; IDs are read in FlushAuditEntries AFTER
    /// SaveChanges so they reflect the DB-assigned auto-increment values.
    /// </summary>
    private void ApplyAuditFieldsAndSnapshot(DbContext ctx)
    {
        t_pending.Value ??= new List<PendingEntry>();
        t_pending.Value.Clear();

        var now = DateTime.UtcNow;
        var userId = _session.UserId;

        foreach (EntityEntry entry in ctx.ChangeTracker.Entries())
        {
            if (entry.Entity is AuditLog) continue;

            var state = entry.State;
            if (state is not (EntityState.Added or EntityState.Modified or EntityState.Deleted))
                continue;

            // Fill audit fields (this mutates the entity in-memory; EF will persist these).
            if (entry.Entity is IAuditable auditable)
            {
                switch (state)
                {
                    case EntityState.Added:
                        auditable.CreatedAt = now;
                        auditable.CreatedBy = userId;
                        break;
                    case EntityState.Modified:
                        auditable.UpdatedAt = now;
                        auditable.UpdatedBy = userId;
                        break;
                }
            }

            // Soft-delete: convert hard Delete → Modified + IsDeleted=true
            // (do this BEFORE snapshotting so the audit reflects the soft-delete state).
            if (entry.Entity is ISoftDeletable soft && state == EntityState.Deleted)
            {
                entry.State = EntityState.Modified;
                soft.IsDeleted = true;
                soft.DeletedAt = now;
                state = EntityState.Modified;  // for snapshot purposes, treat as Modified (which sets IsDeleted)
            }

            // Snapshot OriginalValues for BEFORE-JSON (used by UPDATE/DELETE).
            // CurrentValues are read AFTER SaveChanges in FlushAuditEntries (so ID is populated).
            string? beforeJson = null;
            if (state == EntityState.Modified || state == EntityState.Deleted)
            {
                var beforeProps = entry.OriginalValues.Properties
                    .ToDictionary(p => p.Name, p => entry.OriginalValues[p]);
                beforeJson = JsonSerializer.Serialize(beforeProps, JsonOpts);
            }

            // Determine audit action from original EF state (before soft-delete conversion).
            var action = state switch
            {
                EntityState.Added => AuditActions.Create,
                EntityState.Modified => AuditActions.Update,
                EntityState.Deleted => AuditActions.Delete,
                _ => null
            };
            if (action is null) continue;

            t_pending.Value.Add(new PendingEntry(entry, action, beforeJson));
        }
    }

    /// <summary>
    /// Called AFTER SaveChanges — at this point auto-increment IDs are populated
    /// by the database. We re-walk the snapshotted entries, read their CurrentValues
    /// (which now reflect the real ID), and enqueue AuditLog entries.
    /// </summary>
    private void FlushAuditEntries()
    {
        if (t_pending.Value is null || t_pending.Value.Count == 0) return;

        // Skip if no user session (e.g., during seeding).
        if (_session is NullCurrentSession)
        {
            t_pending.Value.Clear();
            return;
        }

        var userId = _session.UserId;
        var ip = _session.IpAddress;
        var machine = _session.MachineName ?? Environment.MachineName;
        var now = DateTime.UtcNow;

        foreach (var p in t_pending.Value)
        {
            var entry = p.Entry;
            // After SaveChanges, entity state has settled. Read the ID now.
            var entityId = ExtractId(entry);
            if (entityId <= 0) continue;  // still no ID (e.g., not yet committed) — skip

            string? afterJson = null;
            if (p.Action != AuditActions.Delete)
            {
                var afterProps = entry.CurrentValues.Properties
                    .ToDictionary(prop => prop.Name, prop => entry.CurrentValues[prop]);
                afterJson = JsonSerializer.Serialize(afterProps, JsonOpts);
            }

            var entityType = entry.Entity.GetType().Name;
            _auditWriter.Value.Enqueue(new AuditLogEntryPending(
                entityType, entityId, p.Action, userId, now,
                p.BeforeJson, afterJson, ip, machine));
        }

        t_pending.Value.Clear();
    }

    private static long ExtractId(EntityEntry entry)
    {
        var idProp = entry.Metadata.FindPrimaryKey()?.Properties.FirstOrDefault();
        if (idProp is null) return 0;
        try
        {
            var value = entry.CurrentValues[idProp];
            return value switch
            {
                long l => l,
                int i => i,
                ulong ul => (long)ul,
                uint ui => ui,
                _ => value is null ? 0 : Convert.ToInt64(value, System.Globalization.CultureInfo.InvariantCulture)
            };
        }
        catch
        {
            return 0;
        }
    }

    /// <summary>Internal record holding the entry snapshot taken in SavingChanges.</summary>
    private sealed record PendingEntry(EntityEntry Entry, string Action, string? BeforeJson);
}

/// <summary>Deferred audit writer used by the interceptor. Implementation lives in Data layer.</summary>
public interface IAuditWriter
{
    void Enqueue(AuditLogEntryPending entry);
}

public sealed record AuditLogEntryPending(
    string EntityType,
    long EntityId,
    string Action,
    long? ChangedBy,
    DateTime ChangedAt,
    string? BeforeJson,
    string? AfterJson,
    string? IpAddress,
    string? MachineName);
