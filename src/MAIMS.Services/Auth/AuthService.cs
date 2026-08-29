using MAIMS.Core.Abstractions;
using MAIMS.Core.Entities;
using MAIMS.Core.Enums;
using MAIMS.Core.Interfaces;
using MAIMS.Data;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;

namespace MAIMS.Services.Auth;

/// <summary>
/// BCrypt-based authentication service. Stores a 11-cost hash per user.
/// Implements the ICurrentSession contract so the rest of the app can read
/// the logged-in user's identity / permissions without re-querying the DB.
///
/// Lifetime: SINGLETON. Because singleton cannot directly resolve scoped services
/// (MaimsDbContext is scoped via DbContextPool), we inject IServiceScopeFactory
/// and create a fresh scope each time we need a DbContext.
/// </summary>
public class AuthService : IAuthService, ICurrentSession
{
    private readonly IServiceScopeFactory _scopeFactory;
    private readonly TimeProvider _clock;

    // Per-session cached identity. Set after LoginAsync, cleared on LogoutAsync.
    private long? _userId;
    private long? _departmentId;
    private string? _departmentName;
    private string? _roleName;
    private string? _userName;
    private string? _email;
    private DateTime? _lastLoginAt;
    private HashSet<string> _permissions = new(StringComparer.Ordinal);

    /// <summary>
    /// Construct AuthService with an IServiceScopeFactory.
    /// Each operation creates a fresh DI scope to resolve scoped services (DbContext).
    /// </summary>
    public AuthService(IServiceScopeFactory scopeFactory, TimeProvider? clock = null)
    {
        _scopeFactory = scopeFactory;
        _clock = clock ?? TimeProvider.System;
    }

    public long? CurrentUserId => _userId;
    public long? CurrentDepartmentId => _departmentId;
    public string? CurrentDepartmentName => _departmentName;
    public string? CurrentRoleName => _roleName;
    public string? CurrentUserName => _userName;
    public string? CurrentEmail => _email;
    public DateTime? CurrentLastLoginAt => _lastLoginAt;

    // ICurrentSession — same backing fields, slightly different names per interface contract.
    long? ICurrentSession.UserId => _userId;
    long? ICurrentSession.DepartmentId => _departmentId;
    string? ICurrentSession.DepartmentName => _departmentName;
    string? ICurrentSession.RoleName => _roleName;
    string? ICurrentSession.UserName => _userName;
    string? ICurrentSession.Email => _email;
    DateTime? ICurrentSession.LastLoginAt => _lastLoginAt;

    // ICurrentSession
    public IReadOnlyCollection<string> Permissions => _permissions;
    public string? IpAddress => null; // populated by the WinForms host if needed
    public string? MachineName => Environment.MachineName;
    public bool HasPermission(string permission) => _permissions.Contains(permission);
    public bool HasCrossDepartmentAccess() => _permissions.Contains(Core.Enums.Permissions.CrossDepartmentView);

    public async Task<AuthResult> LoginAsync(string username, string password, CancellationToken ct = default)
    {
        if (string.IsNullOrWhiteSpace(username) || string.IsNullOrWhiteSpace(password))
            return new AuthResult(false, "Username and password are required.", null, null);

        using var scope = _scopeFactory.CreateScope();
        var ctx = scope.ServiceProvider.GetRequiredService<MaimsDbContext>();

        var user = await ctx.Set<User>()
            .AsNoTracking()
            .Include(u => u.Role)
            .Include(u => u.Department)
            .FirstOrDefaultAsync(u => u.Username == username, ct);

        if (user is null)
            return new AuthResult(false, "Invalid username or password.", null, null);

        if (user.Status != Core.Enums.UserStatus.Active)
            return new AuthResult(false, "This account is inactive. Contact your administrator.", null, null);

        if (!BCrypt.Net.BCrypt.Verify(password, user.PasswordHash))
        {
            // Increment FailedLoginAttempts — the UserManagementForm grid shows
            // this value so admins can spot accounts under brute-force attack.
            //
            // We use a tracked entity + SaveChangesAsync instead of ExecuteUpdateAsync
            // because EF Core 8's ExecuteUpdateAsync + SetProperty lambda has a
            // compiler quirk that produces CS0103 ("The name 'u' does not exist
            // in the current context") on some toolchain versions. The tracked
            // approach is slightly slower but compiles cleanly everywhere.
            var userToUpdate = await ctx.Set<User>().FirstOrDefaultAsync(u => u.Id == user.Id, ct);
            if (userToUpdate is not null)
            {
                userToUpdate.FailedLoginAttempts++;
                await ctx.SaveChangesAsync(ct);
            }
            return new AuthResult(false, "Invalid username or password.", null, null);
        }

        // Capture the previous login timestamp BEFORE we overwrite it, so the
        // Account Details dialog can show "Last login: <previous time>".
        var previousLoginAt = user.LastLoginAt;

        // Update LastLoginAt + reset FailedLoginAttempts to 0 on successful login.
        // Tracked entity approach (see comment above re: ExecuteUpdateAsync).
        var userToMark = await ctx.Set<User>().FirstOrDefaultAsync(u => u.Id == user.Id, ct);
        if (userToMark is not null)
        {
            userToMark.LastLoginAt = _clock.GetUtcNow().DateTime;
            userToMark.FailedLoginAttempts = 0;
            await ctx.SaveChangesAsync(ct);
        }

        // Hydrate session state.
        _userId = user.Id;
        _departmentId = user.DepartmentId;
        _departmentName = user.Department?.Name;
        _roleName = user.Role?.Name;
        _userName = user.Username;
        _email = user.Email;
        _lastLoginAt = previousLoginAt;

        _permissions = ParsePermissions(user.Role?.PermissionsJson).ToHashSet(StringComparer.Ordinal);

        return new AuthResult(true, null, user.Username, user.Role?.Name);
    }

    public Task LogoutAsync()
    {
        _userId = null;
        _departmentId = null;
        _departmentName = null;
        _roleName = null;
        _userName = null;
        _email = null;
        _lastLoginAt = null;
        _permissions = new HashSet<string>(StringComparer.Ordinal);
        return Task.CompletedTask;
    }

    private static IEnumerable<string> ParsePermissions(string? json)
    {
        if (string.IsNullOrWhiteSpace(json)) return Array.Empty<string>();
        try
        {
            return System.Text.Json.JsonSerializer.Deserialize<List<string>>(json) ?? new List<string>();
        }
        catch
        {
            return Array.Empty<string>();
        }
    }

    /// <summary>
    /// Hashes a password using BCrypt at cost 11. Used by the seeder and the user management UI.
    /// </summary>
    public static string HashPassword(string password) => BCrypt.Net.BCrypt.HashPassword(password, workFactor: 11);
}
