using MAIMS.Core.Entities;
using MAIMS.Data;
using MAIMS.WinUI.Theming;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;

namespace MAIMS.WinUI.Forms;

/// <summary>
/// Location management form. Locations are physical places where assets
/// reside or warehouses are placed. Hierarchical: Site → Building →
/// Floor → Room → Outdoor.
/// </summary>
public class LocationManagementForm : Form
{
    private readonly IServiceScopeFactory _scopeFactory;
    private readonly DataGridView _grid;
    private readonly Button _btnNew;
    private readonly Button _btnEdit;
    private readonly Button _btnRefresh;
    private readonly Button _btnClose;
    private readonly Label _statusLabel;

    public LocationManagementForm(IServiceScopeFactory scopeFactory)
    {
        _scopeFactory = scopeFactory;
        Text = "Locations";
        FormBorderStyle = FormBorderStyle.None;
        Dock = DockStyle.Fill;
        BackColor = MaimsTheme.Background;

        var pnlTop = new Panel { Dock = DockStyle.Top, Height = 50, BackColor = MaimsTheme.Surface, Padding = new Padding(8) };

        _btnNew = MaimsTheme.CreateButton("New Location…", primary: true);
        _btnEdit = MaimsTheme.CreateButton("Edit…");
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
        AddCol("Name", "Location Name", 220);
        AddCol("LocationType", "Type", 100);
        AddCol("ParentName", "Parent", 200);
        AddCol("Address", "Address", 280);
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
            if (_grid.CurrentRow?.DataBoundItem is LocationRow row)
                OpenEditDialog(row.Id);
        };
        _btnRefresh.Click += async (_, _) => await LoadDataAsync();
        _grid.DoubleClick += (_, _) =>
        {
            if (_grid.CurrentRow?.DataBoundItem is LocationRow row)
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

            var locs = await ctx.Locations.AsNoTracking()
                .Include(l => l.Parent)
                .OrderBy(l => l.Name)
                .Select(l => new LocationRow(
                    l.Id, l.Name, l.LocationType,
                    l.Parent != null ? l.Parent.Name : "",
                    l.Address ?? "",
                    l.CreatedAt))
                .ToListAsync();

            _grid.DataSource = locs;
            _statusLabel.Text = $"{locs.Count} location(s) loaded.";
        }
        catch (Exception ex)
        {
            MessageBox.Show("Failed to load locations: " + ex.Message, "Error",
                MessageBoxButtons.OK, MessageBoxIcon.Error);
        }
    }

    private void OpenEditDialog(long? id)
    {
        using var dlg = new LocationEditDialog(_scopeFactory, id);
        if (dlg.ShowDialog(this) == DialogResult.OK)
            _ = LoadDataAsync();
    }

    private sealed record LocationRow(
        long Id, string Name, string LocationType, string ParentName,
        string Address, DateTime CreatedAt);
}

/// <summary>
/// Modal dialog for creating / editing a location.
/// </summary>
public class LocationEditDialog : Form
{
    private readonly IServiceScopeFactory _scopeFactory;
    private readonly long? _locationId;
    private readonly TextBox _txtName;
    private readonly ComboBox _txtType;  // editable ComboBox — Name kept as _txtType for compat
    private readonly ComboBox _cmbParent;
    private readonly TextBox _txtAddress;
    private readonly TextBox _txtGpsLat;
    private readonly TextBox _txtGpsLng;
    private readonly Button _btnSave;
    private readonly Button _btnCancel;

    public LocationEditDialog(IServiceScopeFactory scopeFactory, long? locationId)
    {
        _scopeFactory = scopeFactory;
        _locationId = locationId;

        Text = locationId is null ? "New Location" : $"Edit Location #{locationId}";
        FormBorderStyle = FormBorderStyle.FixedDialog;
        MaximizeBox = false;
        MinimizeBox = false;
        StartPosition = FormStartPosition.CenterParent;
        ClientSize = new Size(440, 280);
        BackColor = MaimsTheme.Background;
        Font = MaimsTheme.Body;

        int y = 16;
        AddLabel("Name *:", y); _txtName = MkTxt(y); y += 32;
        AddLabel("Type *:", y);
        // Type is a free-text field, but we provide a ComboBox with common
        // location types as suggestions (Site/Building/Floor/Room/Outdoor/
        // Yard/Other). DropDown style allows the user to either pick a preset
        // or type a custom value — keeps validation lenient while guiding the user.
        _txtType = new ComboBox
        {
            Location = new Point(140, y),
            Size = new Size(280, 24),
            DropDownStyle = ComboBoxStyle.DropDown,  // editable
            Font = MaimsTheme.Body,
            Text = "Site"
        };
        _txtType.Items.AddRange(new object[]
        {
            "Site",
            "Building",
            "Floor",
            "Room",
            "Outdoor",
            "Yard",
            "Depot",
            "Other"
        });
        Controls.Add(_txtType);
        y += 32;

        AddLabel("Parent:", y);
        _cmbParent = new ComboBox
        {
            Location = new Point(140, y),
            Size = new Size(280, 24),
            DropDownStyle = ComboBoxStyle.DropDownList,
            Font = MaimsTheme.Body
        };
        Controls.Add(_cmbParent);
        y += 32;

        AddLabel("Address:", y); _txtAddress = MkTxt(y); y += 32;
        AddLabel("GPS Lat:", y); _txtGpsLat = MkTxt(y); y += 32;
        AddLabel("GPS Lng:", y); _txtGpsLng = MkTxt(y); y += 36;

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

        var parents = await ctx.Locations.AsNoTracking()
            .Where(l => _locationId == null || l.Id != _locationId.Value)
            .OrderBy(l => l.Name)
            .ToListAsync();
        _cmbParent.DisplayMember = "Name";
        _cmbParent.ValueMember = "Id";
        _cmbParent.DataSource = new List<Location> { new() { Id = 0, Name = "(no parent)" } }
            .Concat(parents).ToList();
        _cmbParent.SelectedIndex = 0;

        if (_locationId is long id)
        {
            var loc = await ctx.Locations.AsNoTracking().FirstOrDefaultAsync(l => l.Id == id);
            if (loc != null)
            {
                _txtName.Text = loc.Name;
                _txtType.Text = loc.LocationType;
                _txtAddress.Text = loc.Address ?? "";
                _txtGpsLat.Text = loc.GpsLat?.ToString() ?? "";
                _txtGpsLng.Text = loc.GpsLng?.ToString() ?? "";
                if (loc.ParentId is long pId) _cmbParent.SelectedValue = pId;
            }
        }
    }

    private async Task SaveAsync()
    {
        if (string.IsNullOrWhiteSpace(_txtName.Text) ||
            string.IsNullOrWhiteSpace(_txtType.Text))
        {
            MessageBox.Show("Name and Type are required.", "Validation",
                MessageBoxButtons.OK, MessageBoxIcon.Warning);
            return;
        }

        try
        {
            using var scope = _scopeFactory.CreateScope();
            var ctx = scope.ServiceProvider.GetRequiredService<MaimsDbContext>();

            decimal? lat = decimal.TryParse(_txtGpsLat.Text, out var la) ? la : null;
            decimal? lng = decimal.TryParse(_txtGpsLng.Text, out var ln) ? ln : null;
            long? parentId = _cmbParent.SelectedValue is long p && p > 0 ? p : null;

            if (_locationId is null)
            {
                ctx.Locations.Add(new Location
                {
                    Name = _txtName.Text.Trim(),
                    LocationType = _txtType.Text.Trim(),
                    ParentId = parentId,
                    Address = string.IsNullOrWhiteSpace(_txtAddress.Text) ? null : _txtAddress.Text,
                    GpsLat = lat,
                    GpsLng = lng
                });
            }
            else
            {
                var loc = await ctx.Locations.FirstOrDefaultAsync(l => l.Id == _locationId.Value);
                if (loc is null) return;
                loc.Name = _txtName.Text.Trim();
                loc.LocationType = _txtType.Text.Trim();
                loc.ParentId = parentId;
                loc.Address = string.IsNullOrWhiteSpace(_txtAddress.Text) ? null : _txtAddress.Text;
                loc.GpsLat = lat;
                loc.GpsLng = lng;
            }

            await ctx.SaveChangesAsync();
            MessageBox.Show("Location saved.", "Success",
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
