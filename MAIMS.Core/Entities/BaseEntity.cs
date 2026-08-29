using MAIMS.Core.Abstractions;

namespace MAIMS.Core.Entities;

/// <summary>
/// Base class for all entity types. Provides common audit columns
/// (created/updated timestamps and users) and soft-delete support.
/// </summary>
public abstract class BaseEntity : IAuditable, ISoftDeletable
{
    public long Id { get; set; }
    public DateTime CreatedAt { get; set; } = DateTime.UtcNow;
    public DateTime? UpdatedAt { get; set; }
    public long? CreatedBy { get; set; }
    public long? UpdatedBy { get; set; }
    public bool IsDeleted { get; set; }
    public DateTime? DeletedAt { get; set; }
}
