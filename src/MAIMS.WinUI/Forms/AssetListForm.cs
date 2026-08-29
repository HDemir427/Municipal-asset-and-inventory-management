using MAIMS.Core.DTOs;
using MAIMS.Core.Enums;
using MAIMS.Core.Interfaces;
using MAIMS.WinUI.Theming;
using Microsoft.EntityFrameworkCore;

namespace MAIMS.WinUI.Forms;

/// <summary>
/// Asset registry list view. Filters by search text / status / department;
/// loads data via IAssetService.SearchAsync (paged). Double-click a row to edit.
/// </summary>
public class AssetListForm : Form
{
    private readonly IAssetService _assetService;
    private readonly IReferenceDataService _refService;
    private readonly TextBox _txtSearch;
    private readonly ComboBox _cmbStatus;
    private readonly Button _btnSearch;
    private readonly Button _btnNew;
    private readonly Button _btnEdit;
    private readonly Button _btnDelete;
    private readonly Button _btnClose;
    private readonly DataGridView _grid;
    private readonly BindingSource _binding = new();
    private readonly StatusLabel _statusLabel;
    private bool _isLoading;  // re-entrancy guard: prevents concurrent SearchAsync on same scoped DbContext

    public AssetListForm(IAssetService assetService, IReferenceDataService refService)
    {
        _assetService = assetService;
        _refService = refService;
        Text = "Asset Registry";
        FormBorderStyle = FormBorderStyle.None;
        Dock = DockStyle.Fill;
        BackColor = MaimsTheme.Background;

        var pnlTop = new Panel { Dock = DockStyle.Top, Height = 50, BackColor = MaimsTheme.Surface, Padding = new Padding(8) };
        var lblSearch = new Label { Text = "Search:", Location = new Point(8, 14), AutoSize = true, Font = MaimsTheme.Body };
        _txtSearch = new TextBox { Location = new Point(70, 10), Size = new Size(220, 24), Font = MaimsTheme.Body };
        var lblStatus = new Label { Text = "Status:", Location = new Point(300, 14), AutoSize = true, Font = MaimsTheme.Body };
        _cmbStatus = new ComboBox
        {
            Location = new Point(355, 12),
            Size = new Size(150, 24),
            DropDownStyle = ComboBoxStyle.DropDownList,
            Font = MaimsTheme.Body
        };
        _cmbStatus.Items.AddRange(new object[] { "(all)" });
        foreach (AssetStatus s in Enum.GetValues(typeof(AssetStatus))) _cmbStatus.Items.Add(s);
        _cmbStatus.SelectedIndex = 0;

        _btnSearch = MaimsTheme.CreateButton("Search", primary: true);
        _btnNew = MaimsTheme.CreateButton("New");
        _btnEdit = MaimsTheme.CreateButton("Edit");
        _btnDelete = MaimsTheme.CreateButton("Delete");
        _btnClose = MaimsTheme.CreateButton("Close");
        MaimsTheme.LayoutButtons(515, 9, 8, _btnSearch, _btnNew, _btnEdit, _btnDelete, _btnClose);
        _btnClose.Click += (_, _) =>
        {
            // Find the TabPage we're embedded in and remove it.
            if (Parent is TabPage page && page.Parent is TabControl tc)
                tc.TabPages.Remove(page);
            else
                Close();
        };

        pnlTop.Controls.AddRange(new Control[] { lblSearch, _txtSearch, lblStatus, _cmbStatus, _btnSearch, _btnNew, _btnEdit, _btnDelete, _btnClose });

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
            DataSource = _binding,
            AutoSizeColumnsMode = DataGridViewAutoSizeColumnsMode.Fill
        };

        AddColumn("AssetCode", "Asset Code", 110);
        AddColumn("Name", "Name", 220);
        AddColumn("CategoryName", "Category", 130);
        AddColumn("DepartmentName", "Department", 130);
        AddColumn("Status", "Status", 100);
        AddColumn("ConditionRating", "Condition", 80);
        AddColumn("AcquisitionDate", "Acquired", 100);
        AddColumn("AcquisitionCost", "Cost", 90, "N2");
        AddColumn("CurrentBookValue", "Book Value", 90, "N2");
        AddColumn("CustodianName", "Custodian", 130);

        _btnSearch.Click += async (_, _) => await LoadDataAsync();
        _btnNew.Click += (_, _) => OpenEdit(null);
        _btnEdit.Click += (_, _) =>
        {
            if (_binding.Current is AssetReadDto a) OpenEdit(a.Id);
        };
        _btnDelete.Click += async (_, _) => await DeleteSelectedAsync();
        _grid.DoubleClick += (_, _) =>
        {
            if (_binding.Current is AssetReadDto a) OpenEdit(a.Id);
        };
        _txtSearch.KeyDown += async (_, e) => { if (e.KeyCode == Keys.Enter) await LoadDataAsync(); };

        var pnlStatus = new Panel { Dock = DockStyle.Bottom, Height = 24, BackColor = MaimsTheme.Surface };
        _statusLabel = new StatusLabel { Dock = DockStyle.Fill, Font = MaimsTheme.Small, ForeColor = MaimsTheme.TextSecondary, Padding = new Padding(8, 4, 8, 4) };
        pnlStatus.Controls.Add(_statusLabel);

        Controls.Add(_grid);
        Controls.Add(pnlTop);
        Controls.Add(pnlStatus);

        Load += async (_, _) => await LoadDataAsync();
    }

    private void AddColumn(string prop, string header, int width, string? format = null)
    {
        var col = new DataGridViewTextBoxColumn
        {
            DataPropertyName = prop,
            HeaderText = header,
            Name = prop,
            Width = width,
            DefaultCellStyle = new DataGridViewCellStyle { Format = format ?? "" }
        };
        _grid.Columns.Add(col);
    }

    private async Task LoadDataAsync()
    {
        // Re-entrancy guard: the form uses an injected scoped IAssetService.
        // The fire-and-forget `_ = LoadDataAsync()` in OpenEdit can overlap
        // with a user-triggered Refresh or Search — without this guard, two
        // concurrent SearchAsync calls would hit the same DbContext.
        if (_isLoading) return;
        _isLoading = true;
        try
        {
            _btnSearch.Enabled = false;
            _statusLabel.Text = "Loading…";

            AssetStatus? statusFilter = null;
            if (_cmbStatus.SelectedIndex > 0 && _cmbStatus.SelectedItem is AssetStatus s) statusFilter = s;

            var filter = new AssetSearchFilter(
                SearchText: string.IsNullOrWhiteSpace(_txtSearch.Text) ? null : _txtSearch.Text.Trim(),
                DepartmentId: null,
                CategoryId: null,
                Status: statusFilter,
                MinCondition: null,
                AcquiredFrom: null,
                AcquiredTo: null,
                Page: 1,
                PageSize: 200);

            var result = await _assetService.SearchAsync(filter);
            _binding.DataSource = result.Items.ToList();
            _statusLabel.Text = $"{result.Items.Count} of {result.TotalCount} assets shown.";
        }
        catch (UnauthorizedAccessException ex)
        {
            MessageBox.Show(ex.Message, "Permission denied", MessageBoxButtons.OK, MessageBoxIcon.Warning);
        }
        catch (Exception ex)
        {
            MessageBox.Show("Failed to load assets: " + ex.Message, "Error", MessageBoxButtons.OK, MessageBoxIcon.Error);
        }
        finally
        {
            _btnSearch.Enabled = true;
            _isLoading = false;
        }
    }

    private void OpenEdit(long? id)
    {
        using var editForm = new AssetEditForm(_assetService, _refService, id);
        if (editForm.ShowDialog(this) == DialogResult.OK)
        {
            _ = LoadDataAsync();
        }
    }

    private async Task DeleteSelectedAsync()
    {
        if (_binding.Current is not AssetReadDto a) return;
        var confirm = MessageBox.Show(
            $"Delete asset '{a.AssetCode}' ({a.Name})?\n\n" +
            "This is a soft delete — the record will be marked as deleted but\n" +
            "remains in the database and will be audit-logged.",
            "Confirm Delete",
            MessageBoxButtons.YesNo,
            MessageBoxIcon.Question,
            MessageBoxDefaultButton.Button2);  // default to No (safer)
        if (confirm != DialogResult.Yes) return;
        try
        {
            await _assetService.DeleteAsync(a.Id);
            MessageBox.Show("Asset deleted (soft-delete).", "Deleted",
                MessageBoxButtons.OK, MessageBoxIcon.Information);
            await LoadDataAsync();
        }
        catch (UnauthorizedAccessException ex)
        {
            MessageBox.Show(ex.Message, "Permission denied",
                MessageBoxButtons.OK, MessageBoxIcon.Warning);
        }
        catch (Exception ex)
        {
            MessageBox.Show(ex.Message, "Error",
                MessageBoxButtons.OK, MessageBoxIcon.Error);
        }
    }

    private sealed class StatusLabel : Label { }
}
