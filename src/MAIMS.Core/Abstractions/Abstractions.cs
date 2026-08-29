namespace MAIMS.Core.Abstractions;

/// <summary>
/// Marker for entities that carry created/updated audit columns.
/// The EF Core audit interceptor reads/writes these properties during SaveChangesAsync.
/// </summary>
public interface IAuditable
{
    long? CreatedBy { get; set; }
    DateTime CreatedAt { get; set; }
    long? UpdatedBy { get; set; }
    DateTime? UpdatedAt { get; set; }
}

/// <summary>
/// Marker for entities that support soft-delete (logical deletion).
/// The global query filter excludes rows where IsDeleted = true.
/// </summary>
public interface ISoftDeletable
{
    bool IsDeleted { get; set; }
    DateTime? DeletedAt { get; set; }
}

/// <summary>
/// Abstraction over the current session context: who is logged in, from where.
/// Implemented by the WinForms app using a thread-static holder set after login.
/// </summary>
public interface ICurrentSession
{
    long? UserId { get; }
    long? DepartmentId { get; }
    string? DepartmentName { get; }   // resolved at login from Department.Name
    string? RoleName { get; }
    IReadOnlyCollection<string> Permissions { get; }
    string? UserName { get; }
    string? Email { get; }            // resolved at login from User.Email
    DateTime? LastLoginAt { get; }    // resolved at login from User.LastLoginAt
    string? IpAddress { get; }
    string? MachineName { get; }

    /// <summary>True if the current user has the given permission.</summary>
    bool HasPermission(string permission);

    /// <summary>True if the current user can view data outside their own department.</summary>
    bool HasCrossDepartmentAccess();
}

/// <summary>
/// Marker for a no-op session used in tests and background services.
/// </summary>
public sealed class NullCurrentSession : ICurrentSession
{
    public static readonly NullCurrentSession Instance = new();
    public long? UserId => null;
    public long? DepartmentId => null;
    public string? DepartmentName => null;
    public string? RoleName => null;
    public IReadOnlyCollection<string> Permissions => Array.Empty<string>();
    public string? UserName => "system";
    public string? Email => null;
    public DateTime? LastLoginAt => null;
    public string? IpAddress => null;
    public string? MachineName => Environment.MachineName;
    public bool HasPermission(string permission) => true;
    public bool HasCrossDepartmentAccess() => true;
}
