using MAIMS.Core.Abstractions;
using MAIMS.Core.Entities;
using MAIMS.Core.Enums;
using MAIMS.Core.Interfaces;
using MAIMS.Data;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;

namespace MAIMS.Services.Audit;

/// <summary>
/// Read side of the audit log. Writes are performed by the EF interceptor via IAuditWriter,
/// so this service exposes only LogAsync (for explicit, non-CUD audit events like LOGIN)
/// and the search/export methods used by auditors.
///
/// Lifetime: SCOPED (registered as Scoped). Uses IServiceScopeFactory because some callers
/// (like AuditService from a background thread) may need to create their own scope.
/// </summary>
public class AuditService : IAuditService
{
    private readonly IServiceScopeFactory _scopeFactory;
    private readonly ICurrentSession _session;

    public AuditService(IServiceScopeFactory scopeFactory, ICurrentSession session)
    {
        _scopeFactory = scopeFactory;
        _session = session;
    }

    public async Task LogAsync(string entityType, long entityId, string action,
        string? beforeJson, string? afterJson, CancellationToken ct = default)
    {
        using var scope = _scopeFactory.CreateScope();
        var ctx = scope.ServiceProvider.GetRequiredService<MaimsDbContext>();

        ctx.Set<AuditLog>().Add(new AuditLog
        {
            EntityType = entityType,
            EntityId = entityId,
            Action = action,
            ChangedBy = _session.UserId,
            ChangedAt = DateTime.UtcNow,
            BeforeJson = beforeJson,
            AfterJson = afterJson,
            // IpAddress intentionally omitted — the WinForms host does not populate
            // ICurrentSession.IpAddress (it's always null). The AuditLog entity still
            // has the column for future remote-host scenarios, but we don't write null
            // explicitly here; the default value (null) applies.
            MachineName = _session.MachineName
        });
        await ctx.SaveChangesAsync(ct);
    }

    public async Task<IReadOnlyList<AuditLogEntry>> SearchAsync(AuditSearchFilter filter, CancellationToken ct = default)
    {
        using var scope = _scopeFactory.CreateScope();
        var ctx = scope.ServiceProvider.GetRequiredService<MaimsDbContext>();

        IQueryable<AuditLog> q = ctx.Set<AuditLog>().AsNoTracking();

        if (!string.IsNullOrEmpty(filter.EntityType))
            q = q.Where(a => a.EntityType == filter.EntityType);
        if (filter.EntityId is long id) q = q.Where(a => a.EntityId == id);
        if (filter.ChangedByUserId is long u) q = q.Where(a => a.ChangedBy == u);
        if (!string.IsNullOrEmpty(filter.Action)) q = q.Where(a => a.Action == filter.Action);
        if (filter.From is DateTime f) q = q.Where(a => a.ChangedAt >= f);
        if (filter.To is DateTime t) q = q.Where(a => a.ChangedAt <= t);

        var page = Math.Max(1, filter.Page);
        var size = Math.Clamp(filter.PageSize, 1, 500);

        var rows = await q.OrderByDescending(a => a.ChangedAt)
            .Skip((page - 1) * size)
            .Take(size)
            .Select(a => new AuditLogEntry(
                a.Id, a.EntityType, a.EntityId, a.Action, a.ChangedBy,
                null, a.ChangedAt, a.BeforeJson, a.AfterJson, a.MachineName))
            .ToListAsync(ct);

        return rows;
    }

    public async Task<byte[]> ExportAsync(AuditSearchFilter filter, string format, CancellationToken ct = default)
    {
        if (!_session.HasPermission(Permissions.AuditExport))
            throw new UnauthorizedAccessException("Missing permission: audit.export");

        var rows = await SearchAsync(filter with { Page = 1, PageSize = 10000 }, ct);
        return format.ToLowerInvariant() switch
        {
            "csv" => ExportCsv(rows),
            _ => throw new NotSupportedException($"Export format '{format}' is not supported. Use 'csv'.")
        };
    }

    /// <summary>
    /// Purges audit_log entries with invalid entity_id (≤ 0) or all entries.
    /// Requires a ROOT connection string because the BEFORE DELETE trigger
    /// must be temporarily dropped. Steps:
    ///   1. Connect as root
    ///   2. DROP TRIGGER trg_audit_log_block_delete
    ///   3. DELETE FROM audit_log WHERE entity_id &lt;= 0 (or all)
    ///   4. CREATE TRIGGER trg_audit_log_block_delete (recreates immutability)
    ///   5. Returns the number of rows deleted
    /// </summary>
    public async Task<int> PurgeInvalidEntriesAsync(string rootConnectionString, bool purgeAll = false, CancellationToken ct = default)
    {
        if (!_session.HasPermission(Permissions.AuditPurge))
            throw new UnauthorizedAccessException("Missing permission: audit.purge");

        if (string.IsNullOrWhiteSpace(rootConnectionString))
            throw new ArgumentException("Root connection string is required.", nameof(rootConnectionString));

        await using var conn = new MySqlConnector.MySqlConnection(rootConnectionString);
        await conn.OpenAsync(ct);

        // Step 1: Drop the BEFORE DELETE trigger so we can delete from audit_log
        await using (var cmd1 = conn.CreateCommand())
        {
            cmd1.CommandText = "DROP TRIGGER IF EXISTS trg_audit_log_block_delete";
            await cmd1.ExecuteNonQueryAsync(ct);
        }

        int deleted;
        try
        {
            // Step 2: Delete invalid entries
            await using var cmd2 = conn.CreateCommand();
            cmd2.CommandText = purgeAll
                ? "DELETE FROM audit_log"
                : "DELETE FROM audit_log WHERE entity_id <= 0 OR entity_id IS NULL";
            deleted = await cmd2.ExecuteNonQueryAsync(ct);
        }
        finally
        {
            // Step 3: ALWAYS recreate the trigger — even if DELETE threw.
            // This is critical: without the trigger, audit_log immutability
            // is permanently broken until someone manually runs the SQL script.
            try
            {
                await using var cmd3 = conn.CreateCommand();
                cmd3.CommandText = """
                    CREATE TRIGGER trg_audit_log_block_delete
                    BEFORE DELETE ON audit_log
                    FOR EACH ROW
                    BEGIN
                        SIGNAL SQLSTATE '45000'
                            SET MESSAGE_TEXT = 'audit_log is append-only. DELETE is not permitted.';
                    END
                    """;
                await cmd3.ExecuteNonQueryAsync(ct);
            }
            catch
            {
                // If trigger recreation fails, we can't do much more here.
                // The caller (UI) will inform the user to run db/30_cleanup_audit_log.sql.
            }
        }

        return deleted;
    }

    private static byte[] ExportCsv(IEnumerable<AuditLogEntry> rows)
    {
        using var sw = new StringWriter();
        sw.WriteLine("Id,EntityType,EntityId,Action,ChangedBy,ChangedAt,MachineName");
        foreach (var r in rows)
        {
            sw.WriteLine(string.Join(',',
                r.Id, CsvEscape(r.EntityType), r.EntityId, CsvEscape(r.Action),
                r.ChangedBy?.ToString() ?? "", r.ChangedAt.ToString("o"),
                CsvEscape(r.MachineName ?? "")));
        }
        return System.Text.Encoding.UTF8.GetBytes(sw.ToString());
    }

    private static string CsvEscape(string s)
    {
        if (s.Contains(',') || s.Contains('"') || s.Contains('\n'))
            return "\"" + s.Replace("\"", "\"\"") + "\"";
        return s;
    }
}
