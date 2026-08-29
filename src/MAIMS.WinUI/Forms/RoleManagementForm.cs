using MAIMS.Core.Entities;
using MAIMS.Core.Enums;
using MAIMS.Data;
using MAIMS.WinUI.Theming;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using System.Text.Json;

namespace MAIMS.WinUI.Forms;

/// <summary>
/// Role management form. Admin can view, create, edit roles and toggle
/// individual permissions via checkboxes. Permissions are stored as JSON
/// arrays in the role.permissions_json column.
/// </summary>
public class RoleManagementForm : Form
{
    private readonly IServiceScopeFactory _scopeFactory;
    private readonly DataGridView _grid;
    private readonly Button _btnNew;
    private readonly Button _btnEdit;
    private readonly Button _btnRefresh;
    private readonly Button _btnClose;
    private readonly Label _statusLabel;

    public RoleManagementForm(IServiceScopeFactory scopeFactory)
    {
        _scopeFactory = scopeFactory;
        Text = "Roles & Permissions";
        FormBorderStyle = FormBorderStyle.None;
        Dock = DockStyle.Fill;
        BackColor = MaimsTheme.Background;

        var pnlTop = new Panel { Dock = DockStyle.Top, Height = 50, BackColor = MaimsTheme.Surface, Padding = new Padding(8) };

        _btnNew = MaimsTheme.CreateButton("New Role…", primary: true);
        _btnEdit = MaimsTheme.CreateButton("Edit Permissions…");
        _btnRefresh = MaimsTheme.CreateButton("Refresh");
        _btnClose = MaimsTheme.CreateButton("Close");
        MaimsTheme.LayoutButtons(8, 10, 8, _btnNew, _btnEdit, _btnRefresh, _btnClose);
        _btnClose.Click += (_, _) =>
        {
            if (Parent is TabPage page && page.Parent is TabControl tc)
                tc.TabPages.Remove(page);
            else
                Close();
        };

        pnlTop.Controls.AddRange(new Control[] { _btnNew, _btnEdit, _btnRefresh, _btnClose });

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
        AddCol("Name", "Role Name", 180);
        AddCol("Description", "Description", 280);
        AddCol("PermissionCount", "# Perms", 90);
        AddCol("IsSystem", "System?", 80);
        AddCol("CreatedAt", "Created At", 140);

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
            if (_grid.CurrentRow?.DataBoundItem is RoleRow row)
                OpenEditDialog(row.Id);
        };
        _btnRefresh.Click += async (_, _) => await LoadDataAsync();
        _grid.DoubleClick += (_, _) =>
        {
            if (_grid.CurrentRow?.DataBoundItem is RoleRow row)
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
        try
        {
            _statusLabel.Text = "Loading…";
            using var scope = _scopeFactory.CreateScope();
            var ctx = scope.ServiceProvider.GetRequiredService<MaimsDbContext>();

            var roles = await ctx.Roles.AsNoTracking()
                .OrderBy(r => r.Name)
                .Select(r => new RoleRow(
                    r.Id, r.Name, r.Description,
                    CountPermissions(r.PermissionsJson),
                    Permissions.SystemRoles.Contains(r.Name) ? "Yes" : "No",
                    r.CreatedAt))
                .ToListAsync();

            _grid.DataSource = roles;
            _statusLabel.Text = $"{roles.Count} roles loaded.";
        }
        catch (Exception ex)
        {
            MessageBox.Show("Failed to load roles: " + ex.Message, "Error",
                MessageBoxButtons.OK, MessageBoxIcon.Error);
        }
    }

    /// <summary>
    /// MySQL can't translate JsonSerializer.Deserialize, so we can't compute the
    /// count in SQL. We use a hack: parse the JSON client-side via a separate
    /// query (or compute it client-side from a raw string fetch).
    /// </summary>
    private static int CountPermissions(string json)
    {
        try
        {
            var list = JsonSerializer.Deserialize<List<string>>(json);
            return list?.Count ?? 0;
        }
        catch
        {
            return 0;
        }
    }

    private void OpenEditDialog(long? id)
    {
        using var dlg = new RoleEditDialog(_scopeFactory, id);
        if (dlg.ShowDialog(this) == DialogResult.OK)
            _ = LoadDataAsync();
    }

    private sealed record RoleRow(
        long Id, string Name, string Description,
        int PermissionCount, string IsSystem, DateTime CreatedAt);
}

/// <summary>
/// Modal dialog for creating / editing a role and toggling its permissions.
/// Permissions are presented as checkboxes grouped by category.
/// </summary>
public class RoleEditDialog : Form
{
    private readonly IServiceScopeFactory _scopeFactory;
    private readonly long? _roleId;
    private readonly TextBox _txtName;
    private readonly TextBox _txtDescription;
    private readonly FlowLayoutPanel _permPanel;
    private readonly Dictionary<string, CheckBox> _permChecks = new();
    private readonly Button _btnSave;
    private readonly Button _btnCancel;

    public RoleEditDialog(IServiceScopeFactory scopeFactory, long? roleId)
    {
        _scopeFactory = scopeFactory;
        _roleId = roleId;

        Text = roleId is null ? "New Role" : $"Edit Role #{roleId}";
        FormBorderStyle = FormBorderStyle.FixedDialog;
        MaximizeBox = false;
        MinimizeBox = false;
        StartPosition = FormStartPosition.CenterParent;
        ClientSize = new Size(620, 580);
        BackColor = MaimsTheme.Background;
        Font = MaimsTheme.Body;
        AutoScroll = true;

        int y = 16;
        AddLabel("Name *:", y); _txtName = MkTxt(y); y += 32;
        AddLabel("Description:", y); _txtDescription = MkTxt(y); y += 36;

        var lblPerms = new Label
        {
            Text = "Permissions:",
            Location = new Point(16, y),
            Size = new Size(280, 20),
            Font = MaimsTheme.Body
        };
        Controls.Add(lblPerms);
        y += 24;

        // FlowLayoutPanel of checkboxes for every known permission.
        _permPanel = new FlowLayoutPanel
        {
            Location = new Point(16, y),
            Size = new Size(580, 380),
            AutoScroll = true,
            FlowDirection = FlowDirection.TopDown,
            WrapContents = false,
            BackColor = MaimsTheme.Surface,
            BorderStyle = BorderStyle.FixedSingle
        };

        // All 31 permission keys, grouped by category for readability.
        var allPerms = new[]
        {
            // Asset management (10)
            Permissions.AssetView,
            Permissions.AssetCreate,
            Permissions.AssetEdit,
            Permissions.AssetDelete,
            Permissions.AssetTransfer,
            Permissions.AssetDispose,
            Permissions.AssetInspect,
            Permissions.AssetAttachments,
            Permissions.AssetMaintenance,
            Permissions.AssetQrCode,

            // Inventory management (7)
            Permissions.InventoryView,
            Permissions.InventoryReceive,
            Permissions.InventoryIssue,
            Permissions.InventoryAdjust,
            Permissions.InventoryWriteOff,
            Permissions.InventoryCycleCount,
            Permissions.InventoryReorder,

            // Cross-department (1)
            Permissions.CrossDepartmentView,

            // Administration (7)
            Permissions.DashboardView,
            Permissions.UserManage,
            Permissions.RoleManage,
            Permissions.DeptManage,
            Permissions.WarehouseManage,
            Permissions.LocationManage,
            Permissions.SupplierView,

            // Audit (3)
            Permissions.AuditView,
            Permissions.AuditExport,
            Permissions.AuditPurge,

            // Reporting (3)
            Permissions.ReportOperational,
            Permissions.ReportFinancial,
            Permissions.ReportCompliance
        };

        foreach (var p in allPerms)
        {
            var cb = new CheckBox
            {
                Text = p,
                AutoSize = true,
                Margin = new Padding(8, 4, 8, 4),
                Font = MaimsTheme.Body
            };
            _permChecks[p] = cb;
            _permPanel.Controls.Add(cb);
        }

        Controls.Add(_permPanel);
        y += 390;

        _btnSave = MaimsTheme.CreateButton("Save", primary: true);
        _btnCancel = MaimsTheme.CreateButton("Cancel");
        MaimsTheme.LayoutButtons(330, y, 8, _btnSave, _btnCancel);
        _btnCancel.DialogResult = DialogResult.Cancel;
        _btnSave.Click += async (_, _) => await SaveAsync();
        Controls.Add(_btnSave);
        Controls.Add(_btnCancel);

        AcceptButton = _btnSave;
        CancelButton = _btnCancel;

        Load += async (_, _) => await LoadDataAsync();
    }

    private void AddLabel(string text, int y) =>
        Controls.Add(new Label { Text = text, Location = new Point(16, y + 3), Size = new Size(120, 20), Font = MaimsTheme.Body });

    private TextBox MkTxt(int y)
    {
        var tb = new TextBox
        {
            Location = new Point(140, y),
            Size = new Size(450, 24),
            Font = MaimsTheme.Body
        };
        Controls.Add(tb);
        return tb;
    }

    private async Task LoadDataAsync()
    {
        if (_roleId is null) return;

        using var scope = _scopeFactory.CreateScope();
        var ctx = scope.ServiceProvider.GetRequiredService<MaimsDbContext>();
        var role = await ctx.Roles.AsNoTracking().FirstOrDefaultAsync(r => r.Id == _roleId.Value);
        if (role is null) return;

        _txtName.Text = role.Name;
        _txtDescription.Text = role.Description ?? "";

        var perms = ParsePermissions(role.PermissionsJson);
        foreach (var (key, cb) in _permChecks)
            cb.Checked = perms.Contains(key);
    }

    private static HashSet<string> ParsePermissions(string json)
    {
        try
        {
            var list = JsonSerializer.Deserialize<List<string>>(json);
            return list?.ToHashSet(StringComparer.Ordinal) ?? new HashSet<string>(StringComparer.Ordinal);
        }
        catch
        {
            return new HashSet<string>(StringComparer.Ordinal);
        }
    }

    private async Task SaveAsync()
    {
        if (string.IsNullOrWhiteSpace(_txtName.Text))
        {
            MessageBox.Show("Name is required.", "Validation",
                MessageBoxButtons.OK, MessageBoxIcon.Warning);
            return;
        }

        try
        {
            using var scope = _scopeFactory.CreateScope();
            var ctx = scope.ServiceProvider.GetRequiredService<MaimsDbContext>();

            var name = _txtName.Text.Trim();
            if (await ctx.Roles.AnyAsync(r => r.Name == name && r.Id != (_roleId ?? 0)))
            {
                MessageBox.Show($"Role '{name}' already exists.", "Duplicate",
                    MessageBoxButtons.OK, MessageBoxIcon.Warning);
                return;
            }

            var perms = _permChecks.Where(kv => kv.Value.Checked).Select(kv => kv.Key).ToList();
            var permsJson = JsonSerializer.Serialize(perms);

            if (_roleId is null)
            {
                ctx.Roles.Add(new Role
                {
                    Name = name,
                    Description = string.IsNullOrWhiteSpace(_txtDescription.Text) ? null : _txtDescription.Text,
                    PermissionsJson = permsJson
                });
            }
            else
            {
                var role = await ctx.Roles.FirstOrDefaultAsync(r => r.Id == _roleId.Value);
                if (role is null) return;
                role.Name = name;
                role.Description = string.IsNullOrWhiteSpace(_txtDescription.Text) ? null : _txtDescription.Text;
                role.PermissionsJson = permsJson;
            }

            await ctx.SaveChangesAsync();
            MessageBox.Show("Role saved.", "Success",
                MessageBoxButtons.OK, MessageBoxIcon.Information);
            DialogResult = DialogResult.OK;
            Close();
        }
        catch (Exception ex)
        {
            MessageBox.Show("Save failed: " + ex.Message, "Error",
                MessageBoxButtons.OK, MessageBoxIcon.Error);
        }
    }
}
