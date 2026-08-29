using MAIMS.Core.Enums;

namespace MAIMS.Core.Entities;

/// <summary>
/// Organisational unit. Assets, users, warehouses, and stock balances all
/// belong (directly or transitively) to a department. Department isolation
/// is enforced at the service layer via ICurrentSession.DepartmentId.
/// </summary>
public class Department : BaseEntity
{
    public string Name { get; set; } = string.Empty;
    public string Code { get; set; } = string.Empty;
    public long? HeadUserId { get; set; }
    public long? ParentDepartmentId { get; set; }

    public User? Head { get; set; }
    public Department? Parent { get; set; }
    public ICollection<Department> Children { get; set; } = new List<Department>();
    public ICollection<User> Users { get; set; } = new List<User>();
}

/// <summary>
/// Application user. Password is stored as a BCrypt hash (cost 11).
/// A user is associated with exactly one department and one role.
/// </summary>
public class User : BaseEntity
{
    public string Name { get; set; } = string.Empty;
    public string Email { get; set; } = string.Empty;
    public string Username { get; set; } = string.Empty;
    public long RoleId { get; set; }
    public long DepartmentId { get; set; }
    public UserStatus Status { get; set; } = UserStatus.Active;
    public string PasswordHash { get; set; } = string.Empty;
    public DateTime? LastLoginAt { get; set; }
    public int FailedLoginAttempts { get; set; }

    public Role? Role { get; set; }
    public Department? Department { get; set; }
}

/// <summary>
/// Role definition. Permissions are stored as a JSON array of permission keys
/// (see <see cref="MAIMS.Core.Enums.Permissions"/>). The matrix is cached per session.
/// </summary>
public class Role : BaseEntity
{
    public string Name { get; set; } = string.Empty;
    public string Description { get; set; } = string.Empty;
    public string PermissionsJson { get; set; } = "[]";

    public ICollection<User> Users { get; set; } = new List<User>();
}
