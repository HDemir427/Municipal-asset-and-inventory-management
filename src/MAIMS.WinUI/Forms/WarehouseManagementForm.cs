using MAIMS.Core.Entities;
using MAIMS.Core.Interfaces;
using MAIMS.Data;
using MAIMS.WinUI.Theming;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;

namespace MAIMS.WinUI.Forms;

/// <summary>
/// Warehouse (storage location) management form. Without at least one
/// warehouse, the inventory Stock Receive / Issue / Balance modules cannot
/// operate — every stock transaction is tied to a warehouse.
/// </summary>
public class WarehouseManagementForm : Form
{
    private readonly IServiceScopeFactory _scopeFactory;
    private readonly DataGridView _grid;
    private readonly Button _btnNew;
    private readonly Button _btnEdit;
    private readonly Button _btnToggleActive;
    private readonly Button _btnRefresh;
    private readonly Button _btnClose;
    private readonly Label _statusLabel;

    public WarehouseManagementForm(IServiceScopeFactory scopeFactory)
    {
        _scopeFactory = scopeFactory;
        Text = "Warehouses";
        FormBorderStyle = FormBorderStyle.None;
        Dock = DockStyle.Fill;
        BackColor = MaimsTheme.Background;

        var pnlTop = new Panel { Dock = DockStyle.Top, Height = 50, BackColor = MaimsTheme.Surface, Padding = new Padding(8) };

        _btnNew = MaimsTheme.CreateButton("New Warehouse…", primary: true);
        _btnEdit = MaimsTheme.CreateButton("Edit…");
        _btnToggleActive = MaimsTheme.CreateButton("Activate/Deactivate");
        _btnRefresh = MaimsTheme.CreateButton("Refresh");
        _btnClose = MaimsTheme.CreateButton("Close");
        MaimsTheme.LayoutButtons(8, 10, 8, _btnNew, _btnEdit, _btnToggleActive, _btnRefresh, _btnClose);
        _btnClose.Click += (_, _) =>
        {
            if (Parent is TabPage page && page.Parent is TabControl tc)
                tc.TabPages.Remove(page);
            else
                Close();
        };

        pnlTop.Controls.AddRange(new Control[] { _btnNew, _btnEdit, _btnToggleActive, _btnRefresh, _btnClose });

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
        AddCol("Code", "Code", 100);
        AddCol("Name", "Warehouse Name", 220);
        AddCol("DepartmentName", "Department", 180);
        AddCol("LocationName", "Location", 180);
        AddCol("IsActive", "Active?", 80);
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
            if (_grid.CurrentRow?.DataBoundItem is WarehouseRow row)
                OpenEditDialog(row.Id);
        };
        _btnToggleActive.Click += async (_, _) => await ToggleActiveAsync();
        _btnRefresh.Click += async (_, _) => await LoadDataAsync();
        _grid.DoubleClick += (_, _) =>
        {
            if (_grid.CurrentRow?.DataBoundItem is WarehouseRow row)
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

            var ws = await ctx.Warehouses.AsNoTracking()
                .Include(w => w.Department)
                .Include(w => w.Location)
                .OrderBy(w => w.Code)
                .Select(w => new WarehouseRow(
                    w.Id, w.Code, w.Name,
                    w.Department != null ? w.Department.Name : "",
                    w.Location != null ? w.Location.Name : "",
                    w.IsActive ? "Yes" : "No",
                    w.CreatedAt))
                .ToListAsync();

            _grid.DataSource = ws;
            _statusLabel.Text = $"{ws.Count} warehouse(s) loaded.";
        }
        catch (Exception ex)
        {
            MessageBox.Show("Failed to load warehouses: " + ex.Message, "Error",
                MessageBoxButtons.OK, MessageBoxIcon.Error);
        }
    }

    private void OpenEditDialog(long? id)
    {
        using var dlg = new WarehouseEditDialog(_scopeFactory, id);
        if (dlg.ShowDialog(this) == DialogResult.OK)
            _ = LoadDataAsync();
    }

    private async Task ToggleActiveAsync()
    {
        if (_grid.CurrentRow?.DataBoundItem is not WarehouseRow row) return;
        try
        {
            using var scope = _scopeFactory.CreateScope();
            var ctx = scope.ServiceProvider.GetRequiredService<MaimsDbContext>();
            var w = await ctx.Warehouses.FirstOrDefaultAsync(x => x.Id == row.Id);
            if (w is null) return;
            w.IsActive = !w.IsActive;
            await ctx.SaveChangesAsync();
            MessageBox.Show($"Warehouse '{w.Name}' is now {(w.IsActive ? "Active" : "Inactive")}.",
                "Status changed", MessageBoxButtons.OK, MessageBoxIcon.Information);
            await LoadDataAsync();
        }
        catch (Exception ex)
        {
            MessageBox.Show("Failed to change status: " + ex.Message, "Error",
                MessageBoxButtons.OK, MessageBoxIcon.Error);
        }
    }

    private sealed record WarehouseRow(
        long Id, string Code, string Name, string DepartmentName,
        string LocationName, string IsActive, DateTime CreatedAt);
}

/// <summary>
/// Modal dialog for creating / editing a warehouse.
/// </summary>
public class WarehouseEditDialog : Form
{
    private readonly IServiceScopeFactory _scopeFactory;
    private readonly long? _warehouseId;
    private readonly TextBox _txtCode;
    private readonly TextBox _txtName;
    private readonly ComboBox _cmbDepartment;
    private readonly ComboBox _cmbLocation;
    private readonly CheckBox _chkActive;
    private readonly Button _btnSave;
    private readonly Button _btnCancel;

    public WarehouseEditDialog(IServiceScopeFactory scopeFactory, long? warehouseId)
    {
        _scopeFactory = scopeFactory;
        _warehouseId = warehouseId;

        Text = warehouseId is null ? "New Warehouse" : $"Edit Warehouse #{warehouseId}";
        FormBorderStyle = FormBorderStyle.FixedDialog;
        MaximizeBox = false;
        MinimizeBox = false;
        StartPosition = FormStartPosition.CenterParent;
        ClientSize = new Size(440, 240);
        BackColor = MaimsTheme.Background;
        Font = MaimsTheme.Body;

        int y = 16;
        AddLabel("Code *:", y); _txtCode = MkTxt(y); y += 32;
        AddLabel("Name *:", y); _txtName = MkTxt(y); y += 32;
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

        AddLabel("Location:", y);
        _cmbLocation = new ComboBox
        {
            Location = new Point(140, y),
            Size = new Size(280, 24),
            DropDownStyle = ComboBoxStyle.DropDownList,
            Font = MaimsTheme.Body
        };
        Controls.Add(_cmbLocation);
        y += 32;

        AddLabel("Active:", y);
        _chkActive = new CheckBox
        {
            Text = "Yes, this warehouse is active",
            Location = new Point(140, y),
            AutoSize = true,
            Checked = true,
            Font = MaimsTheme.Body
        };
        Controls.Add(_chkActive);
        y += 36;

        _btnSave = MaimsTheme.CreateButton("Save", primary: true);
        _btnCancel = MaimsTheme.CreateButton("Cancel");
        MaimsTheme.LayoutButtons(200, y, 8, _btnSave, _btnCancel);
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
            Size = new Size(280, 24),
            Font = MaimsTheme.Body
        };
        Controls.Add(tb);  // ← MUST be added to Controls, otherwise invisible
        return tb;
    }

    private async Task LoadDataAsync()
    {
        using var scope = _scopeFactory.CreateScope();
        var ctx = scope.ServiceProvider.GetRequiredService<MaimsDbContext>();
        var refSvc = scope.ServiceProvider.GetRequiredService<IReferenceDataService>();

        var depts = await refSvc.GetDepartmentsAsync();
        _cmbDepartment.DisplayMember = "Name";
        _cmbDepartment.ValueMember = "Id";
        _cmbDepartment.DataSource = depts.ToList();

        var locs = await refSvc.GetLocationsAsync();
        var locList = new List<Location> { new() { Id = 0, Name = "(no location)" } }
            .Concat(locs).ToList();
        _cmbLocation.DisplayMember = "Name";
        _cmbLocation.ValueMember = "Id";
        _cmbLocation.DataSource = locList;
        _cmbLocation.SelectedIndex = 0;

        if (_warehouseId is long id)
        {
            var w = await ctx.Warehouses.AsNoTracking().FirstOrDefaultAsync(x => x.Id == id);
            if (w != null)
            {
                _txtCode.Text = w.Code;
                _txtName.Text = w.Name;
                _chkActive.Checked = w.IsActive;
                if (w.DepartmentId > 0) _cmbDepartment.SelectedValue = w.DepartmentId;
                if (w.LocationId is long locId) _cmbLocation.SelectedValue = locId;
            }
        }
    }

    private async Task SaveAsync()
    {
        if (string.IsNullOrWhiteSpace(_txtCode.Text) ||
            string.IsNullOrWhiteSpace(_txtName.Text))
        {
            MessageBox.Show("Code and Name are required.", "Validation",
                MessageBoxButtons.OK, MessageBoxIcon.Warning);
            return;
        }
        if (_cmbDepartment.SelectedValue is not long deptId || deptId <= 0)
        {
            MessageBox.Show("Department is required.", "Validation",
                MessageBoxButtons.OK, MessageBoxIcon.Warning);
            return;
        }

        try
        {
            using var scope = _scopeFactory.CreateScope();
            var ctx = scope.ServiceProvider.GetRequiredService<MaimsDbContext>();

            var code = _txtCode.Text.Trim();
            if (await ctx.Warehouses.AnyAsync(w => w.Code == code && w.Id != (_warehouseId ?? 0)))
            {
                MessageBox.Show($"Warehouse code '{code}' already exists.", "Duplicate",
                    MessageBoxButtons.OK, MessageBoxIcon.Warning);
                return;
            }

            long? locId = _cmbLocation.SelectedValue is long l && l > 0 ? l : null;

            if (_warehouseId is null)
            {
                ctx.Warehouses.Add(new Warehouse
                {
                    Code = code,
                    Name = _txtName.Text.Trim(),
                    DepartmentId = deptId,
                    LocationId = locId,
                    IsActive = _chkActive.Checked
                });
            }
            else
            {
                var w = await ctx.Warehouses.FirstOrDefaultAsync(x => x.Id == _warehouseId.Value);
                if (w is null) return;
                w.Code = code;
                w.Name = _txtName.Text.Trim();
                w.DepartmentId = deptId;
                w.LocationId = locId;
                w.IsActive = _chkActive.Checked;
            }

            await ctx.SaveChangesAsync();
            MessageBox.Show("Warehouse saved.", "Success",
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
