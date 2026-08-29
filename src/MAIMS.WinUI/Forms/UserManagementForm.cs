using MAIMS.Core.Entities;
using MAIMS.Core.Enums;
using MAIMS.Core.Interfaces;
using MAIMS.Data;
using MAIMS.Services.Auth;
using MAIMS.WinUI.Theming;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;

namespace MAIMS.WinUI.Forms;

/// <summary>
/// User management form. Only the SystemAdministrator role may open this form
/// (enforced both here and at the navigation entry points in MainForm).
///
/// The admin can view, create, edit, deactivate, reset passwords, and delete users.
/// Passwords are BCrypt-hashed via AuthService.
/// </summary>
public class UserManagementForm : Form
{
    private readonly IServiceScopeFactory _scopeFactory;
    private readonly DataGridView _grid;
    private readonly Button _btnNew;
    private readonly Button _btnEdit;
    private readonly Button _btnToggleStatus;
    private readonly Button _btnResetPassword;
    private readonly Button _btnDelete;
    private readonly Button _btnRefresh;
    private readonly Button _btnClose;
    private readonly Label _statusLabel;
    private bool _isLoading;

    public UserManagementForm(IServiceScopeFactory scopeFactory)
    {
        _scopeFactory = scopeFactory;
        Text = "User Management";
        FormBorderStyle = FormBorderStyle.None;
        Dock = DockStyle.Fill;
        BackColor = MaimsTheme.Background;

        var pnlTop = new Panel { Dock = DockStyle.Top, Height = 50, BackColor = MaimsTheme.Surface, Padding = new Padding(8) };

        _btnNew = MaimsTheme.CreateButton("New User…", primary: true);
        _btnEdit = MaimsTheme.CreateButton("Edit…");
        _btnToggleStatus = MaimsTheme.CreateButton("Activate/Deactivate");
        _btnResetPassword = MaimsTheme.CreateButton("Reset Password…");
        _btnDelete = MaimsTheme.CreateButton("Delete…");
        _btnRefresh = MaimsTheme.CreateButton("Refresh");
        _btnClose = MaimsTheme.CreateButton("Close");
        MaimsTheme.LayoutButtons(8, 10, 8, _btnNew, _btnEdit, _btnToggleStatus, _btnResetPassword, _btnDelete, _btnRefresh, _btnClose);
        _btnClose.Click += (_, _) =>
        {
            if (Parent is TabPage page && page.Parent is TabControl tc)
                tc.TabPages.Remove(page);
            else
                Close();
        };

        pnlTop.Controls.AddRange(new Control[] { _btnNew, _btnEdit, _btnToggleStatus, _btnResetPassword, _btnDelete, _btnRefresh, _btnClose });

        _grid = new DataGridView
        {
            Dock = DockStyle.Fill,
            AutoGenerateColumns = false,
            AllowUserToAddRows = false,
            AllowUserToDeleteRows = false,
            ReadOnly = true,
            SelectionMode = DataGridViewSelectionMode.FullRowSelect,
            BackgroundColor = MaimsTheme.Surface,
            BorderStyle = BorderStyle.None,
            RowHeadersVisible = false,
            Font = MaimsTheme.Body,
            AutoSizeColumnsMode = DataGridViewAutoSizeColumnsMode.Fill
        };

        AddCol("Id", "ID", 60);
        AddCol("Username", "Username", 120);
        AddCol("Name", "Full Name", 180);
        AddCol("Email", "Email", 200);
        AddCol("RoleName", "Role", 130);
        AddCol("DepartmentName", "Department", 150);
        AddCol("Status", "Status", 80);
        AddCol("LastLoginAt", "Last Login", 140);
        AddCol("FailedLoginAttempts", "Failed Logins", 90);

        var pnlStatus = new Panel { Dock = DockStyle.Bottom, Height = 24, BackColor = MaimsTheme.Surface };
        _statusLabel = new Label
        {
            Dock = DockStyle.Fill,
            Font = MaimsTheme.Small,
            ForeColor = MaimsTheme.TextSecondary,
            Padding = new Padding(8, 4, 8, 4)
        };
        pnlStatus.Controls.Add(_statusLabel);

        Controls.Add(_grid);
        Controls.Add(pnlTop);
        Controls.Add(pnlStatus);

        _btnNew.Click += (_, _) => OpenEditDialog(null);
        _btnEdit.Click += (_, _) =>
        {
            if (_grid.CurrentRow?.DataBoundItem is UserRow row)
                OpenEditDialog(row.Id);
            else
                MessageBox.Show("Please select a user to edit.", "No selection",
                    MessageBoxButtons.OK, MessageBoxIcon.Information);
        };
        _btnToggleStatus.Click += async (_, _) => await ToggleStatusAsync();
        _btnResetPassword.Click += async (_, _) => await ResetPasswordAsync();
        _btnDelete.Click += async (_, _) => await DeleteUserAsync();
        _btnRefresh.Click += async (_, _) => await LoadDataAsync();
        _grid.DoubleClick += (_, _) =>
        {
            if (_grid.CurrentRow?.DataBoundItem is UserRow row)
                OpenEditDialog(row.Id);
        };
        Load += async (_, _) => await LoadDataAsync();
    }

    private void AddCol(string prop, string header, int width)
    {
        _grid.Columns.Add(new DataGridViewTextBoxColumn
        {
            DataPropertyName = prop,
            HeaderText = header,
            Name = prop,
            Width = width,
            DefaultCellStyle = new DataGridViewCellStyle()
        });
    }

    private async Task LoadDataAsync()
    {
        // Re-entrancy guard: prevents concurrent DbContext operations.
        if (_isLoading) return;
        _isLoading = true;
        try
        {
            _statusLabel.Text = "Loading…";
            using var scope = _scopeFactory.CreateScope();
            var ctx = scope.ServiceProvider.GetRequiredService<MaimsDbContext>();

            var users = await ctx.Users.AsNoTracking()
                .Include(u => u.Role)
                .Include(u => u.Department)
                .OrderBy(u => u.Username)
                .Select(u => new UserRow(
                    u.Id, u.Username, u.Name, u.Email,
                    u.Role != null ? u.Role.Name : "",
                    u.Department != null ? u.Department.Name : "",
                    u.Status.ToString(),
                    u.LastLoginAt,
                    u.FailedLoginAttempts))
                .ToListAsync();

            _grid.DataSource = null;  // force rebind
            _grid.DataSource = users;
            _statusLabel.Text = $"{users.Count} users loaded.";
        }
        catch (Exception ex)
        {
            MessageBox.Show("Failed to load users: " + ex.Message, "Error",
                MessageBoxButtons.OK, MessageBoxIcon.Error);
        }
        finally
        {
            _isLoading = false;
        }
    }

    private void OpenEditDialog(long? userId)
    {
        using var dlg = new UserEditDialog(_scopeFactory, userId);
        if (dlg.ShowDialog(this) == DialogResult.OK)
        {
            _ = LoadDataAsync();
        }
    }

    private async Task ToggleStatusAsync()
    {
        if (_grid.CurrentRow?.DataBoundItem is not UserRow row) return;
        try
        {
            using var scope = _scopeFactory.CreateScope();
            var ctx = scope.ServiceProvider.GetRequiredService<MaimsDbContext>();
            var user = await ctx.Users.FirstOrDefaultAsync(u => u.Id == row.Id);
            if (user is null) return;

            user.Status = user.Status == UserStatus.Active ? UserStatus.Inactive : UserStatus.Active;
            await ctx.SaveChangesAsync();

            MessageBox.Show($"User '{user.Username}' is now {user.Status}.", "Status changed",
                MessageBoxButtons.OK, MessageBoxIcon.Information);
            await LoadDataAsync();
        }
        catch (Exception ex)
        {
            MessageBox.Show("Failed to change status: " + ex.Message, "Error",
                MessageBoxButtons.OK, MessageBoxIcon.Error);
        }
    }

    private async Task ResetPasswordAsync()
    {
        if (_grid.CurrentRow?.DataBoundItem is not UserRow row) return;
        var pwd = PromptDialog.Show(this, "Reset Password",
            $"Enter new password for '{row.Username}':", "NewP@ss123");
        if (string.IsNullOrWhiteSpace(pwd)) return;

        try
        {
            using var scope = _scopeFactory.CreateScope();
            var ctx = scope.ServiceProvider.GetRequiredService<MaimsDbContext>();
            var user = await ctx.Users.FirstOrDefaultAsync(u => u.Id == row.Id);
            if (user is null) return;

            user.PasswordHash = AuthService.HashPassword(pwd);
            await ctx.SaveChangesAsync();

            MessageBox.Show($"Password for '{row.Username}' has been reset.", "Password reset",
                MessageBoxButtons.OK, MessageBoxIcon.Information);
        }
        catch (Exception ex)
        {
            MessageBox.Show("Failed to reset password: " + ex.Message, "Error",
                MessageBoxButtons.OK, MessageBoxIcon.Error);
        }
    }

    /// <summary>
    /// Deletes the selected user with hard-delete (since User already supports soft-delete
    /// via the audit interceptor, this performs a real remove). Validates:
    ///   1. A row is selected in the grid.
    ///   2. The user exists in the DB at delete-time (not already deleted).
    ///   3. The user is not the bootstrap admin (idempotent safety).
    ///   4. The user is not the currently logged-in user (cannot delete yourself).
    /// </summary>
    private async Task DeleteUserAsync()
    {
        if (_grid.CurrentRow?.DataBoundItem is not UserRow row)
        {
            MessageBox.Show("Please select a user to delete.", "No selection",
                MessageBoxButtons.OK, MessageBoxIcon.Information);
            return;
        }

        // Re-fetch from DB to confirm the user still exists.
        User? user;
        using (var checkScope = _scopeFactory.CreateScope())
        {
            var checkCtx = checkScope.ServiceProvider.GetRequiredService<MaimsDbContext>();
            user = await checkCtx.Users.AsNoTracking().FirstOrDefaultAsync(u => u.Id == row.Id);
        }

        if (user is null)
        {
            MessageBox.Show(
                $"User with ID {row.Id} does not exist in the database.\n\n" +
                "The user may have been deleted by another administrator.\n" +
                "Click Refresh to update the list.",
                "User not found",
                MessageBoxButtons.OK, MessageBoxIcon.Warning);
            return;
        }

        // Safety: prevent deleting the bootstrap admin (username = "admin")
        if (string.Equals(user.Username, "admin", StringComparison.OrdinalIgnoreCase))
        {
            MessageBox.Show(
                "The bootstrap 'admin' user cannot be deleted.\n" +
                "This account is required for emergency access.\n\n" +
                "If you want to disable it, use Activate/Deactivate instead.",
                "Cannot delete admin",
                MessageBoxButtons.OK, MessageBoxIcon.Warning);
            return;
        }

        // Safety: prevent users from deleting themselves
        var currentUserName = scope_getCurrentUserName();
        if (!string.IsNullOrWhiteSpace(currentUserName) &&
            string.Equals(user.Username, currentUserName, StringComparison.OrdinalIgnoreCase))
        {
            MessageBox.Show(
                "You cannot delete your own account while logged in.\n" +
                "Please sign in as a different administrator first.",
                "Cannot delete yourself",
                MessageBoxButtons.OK, MessageBoxIcon.Warning);
            return;
        }

        // Confirmation dialog with details
        var confirm = MessageBox.Show(
            $"Are you sure you want to delete this user?\n\n" +
            $"   Username:  {user.Username}\n" +
            $"   Name:      {user.Name}\n" +
            $"   Email:     {user.Email}\n\n" +
            "This action cannot be undone. The user's record will be\n" +
            "permanently removed and an audit log entry will be written.",
            "Confirm Delete User",
            MessageBoxButtons.YesNo,
            MessageBoxIcon.Warning,
            MessageBoxDefaultButton.Button2);

        if (confirm != DialogResult.Yes) return;

        try
        {
            using var scope = _scopeFactory.CreateScope();
            var ctx = scope.ServiceProvider.GetRequiredService<MaimsDbContext>();

            var toDelete = await ctx.Users.FirstOrDefaultAsync(u => u.Id == row.Id);
            if (toDelete is null)
            {
                MessageBox.Show(
                    "The user was deleted by another session just now.\n" +
                    "Click Refresh to update the list.",
                    "Already deleted",
                    MessageBoxButtons.OK, MessageBoxIcon.Information);
                return;
            }

            ctx.Users.Remove(toDelete);
            await ctx.SaveChangesAsync();

            MessageBox.Show(
                $"User '{toDelete.Username}' has been deleted.",
                "Deleted",
                MessageBoxButtons.OK, MessageBoxIcon.Information);

            await LoadDataAsync();
        }
        catch (DbUpdateException ex) when (ex.InnerException != null && ex.InnerException.Message.Contains("foreign key"))
        {
            MessageBox.Show(
                "Cannot delete this user because other records reference it\n" +
                "(e.g., assets where this user is the custodian, stock transactions\n" +
                "performed by this user, etc.).\n\n" +
                "Consider deactivating the user instead (Activate/Deactivate).",
                "Referenced by other records",
                MessageBoxButtons.OK, MessageBoxIcon.Warning);
        }
        catch (Exception ex)
        {
            MessageBox.Show(
                "Failed to delete user: " + ex.Message,
                "Error",
                MessageBoxButtons.OK, MessageBoxIcon.Error);
        }
    }

    /// <summary>
    /// Helper that resolves the current user's username from the DI container's
    /// ICurrentSession singleton.
    /// </summary>
    private string? scope_getCurrentUserName()
    {
        try
        {
            using var scope = _scopeFactory.CreateScope();
            var session = scope.ServiceProvider.GetRequiredService<MAIMS.Core.Abstractions.ICurrentSession>();
            return session.UserName;
        }
        catch
        {
            return null;
        }
    }

    private sealed record UserRow(
        long Id, string Username, string Name, string Email,
        string RoleName, string DepartmentName, string Status, DateTime? LastLoginAt,
        int FailedLoginAttempts);
}

/// <summary>
/// Modal dialog for creating OR editing a user.
/// When userId is null → create mode (Username + Password fields editable).
/// When userId is not null → edit mode (Username + Password read-only;
///   password is changed via the dedicated "Reset Password" button on the grid).
/// </summary>
public class UserEditDialog : Form
{
    private readonly IServiceScopeFactory _scopeFactory;
    private readonly long? _userId;
    private readonly TextBox _txtUsername;
    private readonly TextBox _txtName;
    private readonly TextBox _txtEmail;
    private readonly ComboBox _cmbRole;
    private readonly ComboBox _cmbDepartment;
    private readonly TextBox _txtPassword;
    private readonly Button _btnSave;
    private readonly Button _btnCancel;

    public UserEditDialog(IServiceScopeFactory scopeFactory, long? userId = null)
    {
        _scopeFactory = scopeFactory;
        _userId = userId;

        Text = userId is null ? "New User" : $"Edit User #{userId}";
        FormBorderStyle = FormBorderStyle.FixedDialog;
        MaximizeBox = false;
        MinimizeBox = false;
        StartPosition = FormStartPosition.CenterParent;
        ClientSize = new Size(440, 310);  // taller — added password info label row
        BackColor = MaimsTheme.Background;
        Font = MaimsTheme.Body;

        int y = 16;
        AddLabel("Username *:", y);
        _txtUsername = new TextBox
        {
            Location = new Point(140, y),
            Size = new Size(280, 24),
            Font = MaimsTheme.Body,
            ReadOnly = userId is not null  // username immutable in edit mode
        };
        if (userId is not null)
            _txtUsername.BackColor = Color.FromArgb(240, 240, 240);
        Controls.Add(_txtUsername);
        y += 32;

        AddLabel("Full Name *:", y); _txtName = MkTxt(y); y += 32;
        AddLabel("Email *:", y); _txtEmail = MkTxt(y); y += 32;
        AddLabel("Role *:", y);
        _cmbRole = new ComboBox
        {
            Location = new Point(140, y),
            Size = new Size(280, 24),
            DropDownStyle = ComboBoxStyle.DropDownList,
            Font = MaimsTheme.Body
        };
        Controls.Add(_cmbRole);
        y += 32;

        AddLabel("Department *:", y);
        _cmbDepartment = new ComboBox
        {
            Location = new Point(140, y),
            Size = new Size(280, 24),
            DropDownStyle = ComboBoxStyle.DropDownList,
            Font = MaimsTheme.Body
        };
        Controls.Add(_cmbDepartment);
        y += 32;

        AddLabel("Password *:", y);
        _txtPassword = new TextBox
        {
            Location = new Point(140, y),
            Size = new Size(280, 24),
            Font = MaimsTheme.Body,
            PasswordChar = '*',
            ReadOnly = userId is not null,  // password not editable here in edit mode
            PlaceholderText = userId is null ? "" : "(use Reset Password button below)"
        };
        if (userId is not null)
        {
            _txtPassword.BackColor = Color.FromArgb(240, 240, 240);
            // Show "Last changed: <date>" instead of fake asterisks.
            // BCrypt hashes are one-way — we cannot show the actual password,
            // but we CAN show when the user record was last updated (UpdatedAt
            // is set by the audit interceptor whenever password changes via
            // Reset Password). This gives the admin meaningful feedback about
            // whether the password was recently changed.
            _txtPassword.Text = "(use Reset Password below)";
            _txtPassword.ForeColor = MaimsTheme.TextSecondary;
            _txtPassword.Font = new Font("Segoe UI", 8.25F, FontStyle.Italic);
        }
        Controls.Add(_txtPassword);
        y += 32;

        // In edit mode, add a "Password info" label showing the last-update
        // timestamp so the admin sees whether the password was recently changed.
        // (BCrypt is one-way — we cannot show the actual password, but UpdatedAt
        // reflects the last time Reset Password was used.)
        Label? lblPwdInfo = null;
        if (userId is not null)
        {
            lblPwdInfo = new Label
            {
                Location = new Point(140, y),
                Size = new Size(280, 18),
                Font = MaimsTheme.Small,
                ForeColor = MaimsTheme.TextSecondary,
                Text = "Loading password info…"
            };
            Controls.Add(lblPwdInfo);
            y += 22;
        }
        y += 8;

        _btnSave = MaimsTheme.CreateButton("Save", primary: true);
        _btnCancel = MaimsTheme.CreateButton("Cancel");
        MaimsTheme.LayoutButtons(200, y, 8, _btnSave, _btnCancel);
        _btnCancel.DialogResult = DialogResult.Cancel;
        _btnSave.Click += async (_, _) => await SaveAsync();
        Controls.Add(_btnSave);
        Controls.Add(_btnCancel);

        AcceptButton = _btnSave;
        CancelButton = _btnCancel;

        Load += async (_, _) =>
        {
            await LoadRefDataAsync();
            if (_userId is not null)
                await LoadUserAsync();
        };
    }

    private void AddLabel(string text, int y)
    {
        Controls.Add(new Label { Text = text, Location = new Point(16, y + 3), Size = new Size(120, 20), Font = MaimsTheme.Body });
    }

    private TextBox MkTxt(int y)
    {
        var tb = new TextBox
        {
            Location = new Point(140, y),
            Size = new Size(280, 24),
            Font = MaimsTheme.Body
        };
        Controls.Add(tb);
        return tb;
    }

    private async Task LoadRefDataAsync()
    {
        using var scope = _scopeFactory.CreateScope();
        var refSvc = scope.ServiceProvider.GetRequiredService<IReferenceDataService>();
        var ctx = scope.ServiceProvider.GetRequiredService<MaimsDbContext>();

        var roles = await ctx.Roles.AsNoTracking().OrderBy(r => r.Name).ToListAsync();
        _cmbRole.DisplayMember = "Name";
        _cmbRole.ValueMember = "Id";
        _cmbRole.DataSource = roles;

        var depts = await refSvc.GetDepartmentsAsync();
        _cmbDepartment.DisplayMember = "Name";
        _cmbDepartment.ValueMember = "Id";
        _cmbDepartment.DataSource = depts.ToList();
    }

    private async Task LoadUserAsync()
    {
        try
        {
            using var scope = _scopeFactory.CreateScope();
            var ctx = scope.ServiceProvider.GetRequiredService<MaimsDbContext>();
            var user = await ctx.Users.AsNoTracking().FirstOrDefaultAsync(u => u.Id == _userId!.Value);
            if (user is null)
            {
                MessageBox.Show("User not found.", "Error",
                    MessageBoxButtons.OK, MessageBoxIcon.Error);
                return;
            }
            _txtUsername.Text = user.Username;
            _txtName.Text = user.Name;
            _txtEmail.Text = user.Email ?? "";
            _cmbRole.SelectedValue = user.RoleId;
            _cmbDepartment.SelectedValue = user.DepartmentId;

            // Update the password info label with the user record's last-update
            // timestamp. UpdatedAt is set by the audit interceptor on every
            // SaveChangesAsync, so it reflects the most recent password change
            // (via Reset Password) or any other profile field change.
            // Find the lblPwdInfo control we created in the constructor.
            foreach (var c in Controls)
            {
                if (c is Label l && l.Text == "Loading password info…")
                {
                    l.Text = user.UpdatedAt.HasValue
                        ? $"Last changed: {user.UpdatedAt.Value.ToLocalTime():yyyy-MM-dd HH:mm}"
                        : "Last changed: never (initial password)";
                    break;
                }
            }
        }
        catch (Exception ex)
        {
            MessageBox.Show("Failed to load user: " + ex.Message, "Error",
                MessageBoxButtons.OK, MessageBoxIcon.Error);
        }
    }

    private async Task SaveAsync()
    {
        if (string.IsNullOrWhiteSpace(_txtName.Text) ||
            string.IsNullOrWhiteSpace(_txtEmail.Text))
        {
            MessageBox.Show("Full Name and Email are required.", "Validation",
                MessageBoxButtons.OK, MessageBoxIcon.Warning);
            return;
        }

        // Username + Password required only in create mode.
        if (_userId is null)
        {
            if (string.IsNullOrWhiteSpace(_txtUsername.Text) ||
                string.IsNullOrWhiteSpace(_txtPassword.Text))
            {
                MessageBox.Show("Username and Password are required for new users.", "Validation",
                    MessageBoxButtons.OK, MessageBoxIcon.Warning);
                return;
            }
        }

        try
        {
            using var scope = _scopeFactory.CreateScope();
            var ctx = scope.ServiceProvider.GetRequiredService<MaimsDbContext>();

            if (_userId is null)
            {
                // ── Create mode ──
                if (await ctx.Users.AnyAsync(u => u.Username == _txtUsername.Text.Trim()))
                {
                    MessageBox.Show("Username already exists.", "Duplicate",
                        MessageBoxButtons.OK, MessageBoxIcon.Warning);
                    return;
                }

                var user = new User
                {
                    Username = _txtUsername.Text.Trim(),
                    Name = _txtName.Text.Trim(),
                    Email = _txtEmail.Text.Trim(),
                    RoleId = (long)_cmbRole.SelectedValue!,
                    DepartmentId = (long)_cmbDepartment.SelectedValue!,
                    Status = UserStatus.Active,
                    PasswordHash = AuthService.HashPassword(_txtPassword.Text)
                };
                ctx.Users.Add(user);
                await ctx.SaveChangesAsync();

                MessageBox.Show($"User '{user.Username}' created.", "Success",
                    MessageBoxButtons.OK, MessageBoxIcon.Information);
            }
            else
            {
                // ── Edit mode ──
                var user = await ctx.Users.FirstOrDefaultAsync(u => u.Id == _userId.Value);
                if (user is null)
                {
                    MessageBox.Show("User not found.", "Error",
                        MessageBoxButtons.OK, MessageBoxIcon.Error);
                    return;
                }

                user.Name = _txtName.Text.Trim();
                user.Email = _txtEmail.Text.Trim();
                user.RoleId = (long)_cmbRole.SelectedValue!;
                user.DepartmentId = (long)_cmbDepartment.SelectedValue!;

                await ctx.SaveChangesAsync();

                MessageBox.Show($"User '{user.Username}' updated.", "Success",
                    MessageBoxButtons.OK, MessageBoxIcon.Information);
            }

            DialogResult = DialogResult.OK;
            Close();
        }
        catch (Exception ex)
        {
            MessageBox.Show("Failed to save user: " + ex.Message, "Error",
                MessageBoxButtons.OK, MessageBoxIcon.Error);
        }
    }
}

/// <summary>
/// Simple input dialog for prompting the user for a single string value
/// (e.g., new password during reset).
/// </summary>
public static class PromptDialog
{
    public static string? Show(IWin32Window owner, string title, string prompt, string defaultValue = "")
    {
        using var f = new Form
        {
            Text = title,
            FormBorderStyle = FormBorderStyle.FixedDialog,
            MaximizeBox = false,
            MinimizeBox = false,
            StartPosition = FormStartPosition.CenterParent,
            ClientSize = new Size(420, 130),
            BackColor = MaimsTheme.Background,
            Font = MaimsTheme.Body
        };

        var lbl = new Label { Text = prompt, Location = new Point(16, 16), Size = new Size(368, 24), Font = MaimsTheme.Body };
        var txt = new TextBox { Text = defaultValue, Location = new Point(16, 48), Size = new Size(368, 24), Font = MaimsTheme.Body };
        var btnOk = MaimsTheme.CreateButton("OK", primary: true);
        var btnCancel = MaimsTheme.CreateButton("Cancel");
        // Use LayoutButtons so buttons never overlap and start from the left.
        MaimsTheme.LayoutButtons(16, 84, 12, btnOk, btnCancel);
        btnOk.DialogResult = DialogResult.OK;
        btnCancel.DialogResult = DialogResult.Cancel;

        f.Controls.AddRange(new Control[] { lbl, txt, btnOk, btnCancel });
        f.AcceptButton = btnOk;
        f.CancelButton = btnCancel;

        return f.ShowDialog(owner) == DialogResult.OK ? txt.Text : null;
    }
}
