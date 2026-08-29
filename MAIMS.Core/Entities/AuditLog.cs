namespace MAIMS.Core.Entities;

/// <summary>
/// Append-only audit log entry. Every CREATE/UPDATE/DELETE on a tracked entity
/// writes one row here. MySQL privilege level enforces INSERT-only — the app user
/// has NO UPDATE/DELETE privilege on this table.
/// </summary>
public class AuditLog
{
    public long Id { get; set; }
    public string EntityType { get; set; } = string.Empty;
    public long EntityId { get; set; }
    public string Action { get; set; } = string.Empty;     // CREATE/UPDATE/DELETE/LOGIN/...
    public long? ChangedBy { get; set; }
    public DateTime ChangedAt { get; set; } = DateTime.UtcNow;
    public string? BeforeJson { get; set; }                 // snapshot of entity before change
    public string? AfterJson { get; set; }                  // snapshot of entity after change
    public string? IpAddress { get; set; }
    public string? MachineName { get; set; }
    public string? CorrelationId { get; set; }              // groups multiple logs from one operation
}
