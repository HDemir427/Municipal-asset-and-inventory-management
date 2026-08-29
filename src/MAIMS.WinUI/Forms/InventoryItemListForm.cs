using MAIMS.Core.DTOs;
using MAIMS.Core.Enums;
using MAIMS.Core.Interfaces;
using MAIMS.WinUI.Theming;

namespace MAIMS.WinUI.Forms;

/// <summary>
/// Inventory item catalog: lists all SKU master records, allows creating,
/// editing, and soft-deleting items, and viewing stock balances per item.
/// </summary>
public class InventoryItemListForm : Form
{
    private readonly IInventoryService _inventoryService;
    private readonly TextBox _txtSearch;
    private readonly Button _btnSearch;
    private readonly Button _btnNew;
    private readonly Button _btnEdit;
    private readonly Button _btnDelete;
    private readonly Button _btnRefresh;
    private readonly Button _btnClose;
    private readonly DataGridView _grid;
    private readonly Label _statusLabel;
    private bool _isLoading;

    public InventoryItemListForm(IInventoryService inventoryService)
    {
        _inventoryService = inventoryService;
        Text = "Inventory — Item Catalog";
        FormBorderStyle = FormBorderStyle.None;
        Dock = DockStyle.Fill;
        BackColor = MaimsTheme.Background;

        var pnlTop = new Panel { Dock = DockStyle.Top, Height = 50, BackColor = MaimsTheme.Surface, Padding = new Padding(8) };
        var lblSearch = new Label { Text = "Search:", Location = new Point(8, 14), AutoSize = true, Font = MaimsTheme.Body };
        _txtSearch = new TextBox { Location = new Point(70, 12), Size = new Size(220, 24), Font = MaimsTheme.Body };

        _btnSearch = MaimsTheme.CreateButton("Search", primary: true);
        _btnNew = MaimsTheme.CreateButton("New Item…");
        _btnEdit = MaimsTheme.CreateButton("Edit…");
        _btnDelete = MaimsTheme.CreateButton("Delete…");
        _btnRefresh = MaimsTheme.CreateButton("Refresh");
        _btnClose = MaimsTheme.CreateButton("Close");
        MaimsTheme.LayoutButtons(300, 10, 8, _btnSearch, _btnNew, _btnEdit, _btnDelete, _btnRefresh, _btnClose);
        _btnClose.Click += (_, _) =>
        {
            if (Parent is TabPage page && page.Parent is TabControl tc)
                tc.TabPages.Remove(page);
            else
                Close();
        };

        pnlTop.Controls.AddRange(new Control[] { lblSearch, _txtSearch, _btnSearch, _btnNew, _btnEdit, _btnDelete, _btnRefresh, _btnClose });

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
        AddCol("Sku", "SKU", 120);
        AddCol("Name", "Name", 200);
        AddCol("Category", "Category", 100);
        AddCol("Uom", "UoM", 60);
        AddCol("ReorderPoint", "Reorder Pt", 90, "N0");
        AddCol("ReorderQty", "Reorder Qty", 90, "N0");
        AddCol("UnitCost", "Unit Cost", 90, "N2");
        AddCol("PreferredSupplier", "Supplier", 150);
        AddCol("HazardousFlag", "Haz?", 50);
        AddCol("Manufacturer", "Manufacturer", 130);
        AddCol("CreatedAt", "Created At", 130);

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

        _btnSearch.Click += async (_, _) => await LoadDataAsync();
        _btnNew.Click += (_, _) => OpenEditDialog(null);
        _btnEdit.Click += (_, _) =>
        {
            if (_grid.CurrentRow?.DataBoundItem is ItemReadDto row)
                OpenEditDialog(row.Id);
            else
                MessageBox.Show("Please select an item to edit.", "No selection",
                    MessageBoxButtons.OK, MessageBoxIcon.Information);
        };
        _btnDelete.Click += async (_, _) => await DeleteItemAsync();
        _btnRefresh.Click += async (_, _) => await LoadDataAsync();
        _txtSearch.KeyDown += async (_, e) => { if (e.KeyCode == Keys.Enter) await LoadDataAsync(); };
        _grid.DoubleClick += (_, _) =>
        {
            if (_grid.CurrentRow?.DataBoundItem is ItemReadDto row)
                OpenEditDialog(row.Id);
        };
        Load += async (_, _) => await LoadDataAsync();
    }

    private void AddCol(string prop, string header, int width, string? format = null)
    {
        _grid.Columns.Add(new DataGridViewTextBoxColumn
        {
            DataPropertyName = prop,
            HeaderText = header,
            Name = prop,
            Width = width,
            DefaultCellStyle = new DataGridViewCellStyle { Format = format ?? "" }
        });
    }

    private async Task LoadDataAsync()
    {
        // Re-entrancy guard: prevent concurrent DB operations on the same
        // scoped DbContext. Without this, if the user clicks Refresh while a
        // previous Load is still in flight, two async operations would run
        // simultaneously on the same DbContext → "A second operation was
        // started on this context instance" crash.
        if (_isLoading) return;
        _isLoading = true;
        try
        {
            _statusLabel.Text = "Loading…";
            var searchText = string.IsNullOrWhiteSpace(_txtSearch.Text) ? null : _txtSearch.Text.Trim();
            var items = await _inventoryService.SearchItemsAsync(searchText);
            _grid.DataSource = null;  // force DataGridView to rebind
            _grid.DataSource = items.ToList();
            _statusLabel.Text = $"{items.Count} items loaded.";
        }
        catch (UnauthorizedAccessException ex)
        {
            MessageBox.Show(ex.Message, "Permission denied",
                MessageBoxButtons.OK, MessageBoxIcon.Warning);
        }
        catch (Exception ex)
        {
            MessageBox.Show("Failed to load items: " + ex.Message, "Error",
                MessageBoxButtons.OK, MessageBoxIcon.Error);
        }
        finally
        {
            _isLoading = false;
        }
    }

    private void OpenEditDialog(long? itemId)
    {
        using var dlg = new ItemEditDialog(_inventoryService, itemId);
        if (dlg.ShowDialog(this) == DialogResult.OK)
        {
            _ = LoadDataAsync();
        }
    }

    private async Task DeleteItemAsync()
    {
        if (_grid.CurrentRow?.DataBoundItem is not ItemReadDto row)
        {
            MessageBox.Show("Please select an item to delete.", "No selection",
                MessageBoxButtons.OK, MessageBoxIcon.Information);
            return;
        }

        var confirm = MessageBox.Show(
            $"Delete item:\n\n  SKU:  {row.Sku}\n  Name: {row.Name}\n\n" +
            "This is a soft-delete — the item will be hidden from the catalog\n" +
            "but historical transactions are preserved for audit.\n\n" +
            "Items CANNOT be deleted if they have:\n" +
            "  - Non-zero stock balances (issue or write off first)\n" +
            "  - Historical stock transactions (audit trail integrity)\n\n" +
            "If the item has transactions, consider editing it instead\n" +
            "(set ReorderPoint=0 and rename to indicate obsolete).",
            "Confirm Delete",
            MessageBoxButtons.YesNo,
            MessageBoxIcon.Warning,
            MessageBoxDefaultButton.Button2);
        if (confirm != DialogResult.Yes) return;

        try
        {
            await _inventoryService.DeleteItemAsync(row.Id);
            MessageBox.Show($"Item '{row.Sku}' deleted.", "Success",
                MessageBoxButtons.OK, MessageBoxIcon.Information);
            await LoadDataAsync();
        }
        catch (UnauthorizedAccessException ex)
        {
            MessageBox.Show(ex.Message, "Permission denied",
                MessageBoxButtons.OK, MessageBoxIcon.Warning);
        }
        catch (InvalidOperationException ex)
        {
            MessageBox.Show(ex.Message, "Cannot delete",
                MessageBoxButtons.OK, MessageBoxIcon.Warning);
        }
        catch (Exception ex)
        {
            MessageBox.Show("Delete failed: " + ex.Message, "Error",
                MessageBoxButtons.OK, MessageBoxIcon.Error);
        }
    }
}

/// <summary>
/// Modal dialog for creating OR editing an inventory item (SKU master record).
/// When itemId is null → create mode (SKU field editable).
/// When itemId is not null → edit mode (SKU field read-only, fields pre-populated).
/// </summary>
public class ItemEditDialog : Form
{
    private readonly IInventoryService _inventoryService;
    private readonly long? _itemId;
    private readonly TextBox _txtSku;
    private readonly TextBox _txtName;
    private readonly TextBox _txtDescription;
    private readonly TextBox _txtCategory;
    private readonly ComboBox _cmbUom;
    private readonly TextBox _txtReorderPoint;
    private readonly TextBox _txtReorderQty;
    private readonly TextBox _txtUnitCost;
    private readonly TextBox _txtSupplier;
    private readonly TextBox _txtLeadTime;
    private readonly CheckBox _chkHazardous;
    private readonly TextBox _txtStorage;
    private readonly TextBox _txtManufacturer;
    private readonly TextBox _txtPartNumber;
    private readonly Button _btnSave;
    private readonly Button _btnCancel;

    public ItemEditDialog(IInventoryService inventoryService, long? itemId = null)
    {
        _inventoryService = inventoryService;
        _itemId = itemId;

        Text = itemId is null ? "New Inventory Item" : $"Edit Item #{itemId}";
        FormBorderStyle = FormBorderStyle.FixedDialog;
        MaximizeBox = false;
        MinimizeBox = false;
        StartPosition = FormStartPosition.CenterParent;
        ClientSize = new Size(560, 620);
        BackColor = MaimsTheme.Background;
        Font = MaimsTheme.Body;
        AutoScroll = true;

        int y = 16;
        AddLabel("SKU *:", y);
        _txtSku = new TextBox
        {
            Location = new Point(200, y),
            Size = new Size(330, 24),
            Font = MaimsTheme.Body,
            ReadOnly = itemId is not null  // SKU is immutable in edit mode
        };
        if (itemId is not null)
            _txtSku.BackColor = Color.FromArgb(240, 240, 240);  // visual cue: read-only
        Controls.Add(_txtSku);
        y += 32;

        AddLabel("Name *:", y); _txtName = MkTxt(y); y += 32;
        AddLabel("Description:", y); _txtDescription = MkTxt(y, 100); y += 110;
        AddLabel("Category *:", y); _txtCategory = MkTxt(y); y += 32;
        AddLabel("Unit of Measure:", y);
        _cmbUom = new ComboBox
        {
            Location = new Point(200, y),
            Size = new Size(330, 24),
            DropDownStyle = ComboBoxStyle.DropDownList,
            Font = MaimsTheme.Body
        };
        _cmbUom.Items.AddRange(Enum.GetValues(typeof(UnitOfMeasure)).Cast<object>().ToArray());
        _cmbUom.SelectedIndex = 0;
        Controls.Add(_cmbUom);
        y += 32;

        AddLabel("Reorder Point:", y); _txtReorderPoint = MkTxt(y); y += 32;
        AddLabel("Reorder Quantity:", y); _txtReorderQty = MkTxt(y); y += 32;
        AddLabel("Unit Cost:", y); _txtUnitCost = MkTxt(y); y += 32;
        AddLabel("Preferred Supplier:", y); _txtSupplier = MkTxt(y); y += 32;
        AddLabel("Lead Time (days):", y); _txtLeadTime = MkTxt(y); y += 32;
        AddLabel("Storage Reqs:", y); _txtStorage = MkTxt(y); y += 32;
        AddLabel("Manufacturer:", y); _txtManufacturer = MkTxt(y); y += 32;
        AddLabel("Mfr Part No.:", y); _txtPartNumber = MkTxt(y); y += 32;
        AddLabel("Hazardous?", y);
        _chkHazardous = new CheckBox
        {
            Text = "Yes, this item is hazardous",
            Location = new Point(200, y),
            AutoSize = true,
            Font = MaimsTheme.Body
        };
        Controls.Add(_chkHazardous);
        y += 36;

        _btnSave = MaimsTheme.CreateButton("Save", primary: true);
        _btnCancel = MaimsTheme.CreateButton("Cancel");
        MaimsTheme.LayoutButtons(310, y, 8, _btnSave, _btnCancel);
        _btnCancel.DialogResult = DialogResult.Cancel;
        _btnSave.Click += async (_, _) => await SaveAsync();
        Controls.Add(_btnSave);
        Controls.Add(_btnCancel);

        AcceptButton = _btnSave;
        CancelButton = _btnCancel;

        // In edit mode, load the existing item's data asynchronously.
        if (_itemId is not null)
            Load += async (_, _) => await LoadItemAsync();
    }

    private void AddLabel(string text, int y)
    {
        Controls.Add(new Label { Text = text, Location = new Point(20, y + 3), Size = new Size(170, 20), Font = MaimsTheme.Body });
    }

    private TextBox MkTxt(int y, int h = 24)
    {
        var tb = new TextBox
        {
            Location = new Point(200, y),
            Size = new Size(330, h),
            Multiline = h > 24,
            Font = MaimsTheme.Body
        };
        Controls.Add(tb);
        return tb;
    }

    private async Task LoadItemAsync()
    {
        try
        {
            var item = await _inventoryService.GetItemAsync(_itemId!.Value);
            _txtSku.Text = item.Sku;
            _txtName.Text = item.Name;
            _txtDescription.Text = item.Description ?? "";
            _txtCategory.Text = item.Category;
            _cmbUom.SelectedItem = item.Uom;
            _txtReorderPoint.Text = item.ReorderPoint.ToString();
            _txtReorderQty.Text = item.ReorderQty.ToString();
            _txtUnitCost.Text = item.UnitCost?.ToString() ?? "";
            _txtSupplier.Text = item.PreferredSupplier ?? "";
            _txtLeadTime.Text = item.LeadTimeDays?.ToString() ?? "";
            _chkHazardous.Checked = item.HazardousFlag;
            _txtStorage.Text = item.StorageRequirements ?? "";
            _txtManufacturer.Text = item.Manufacturer ?? "";
            _txtPartNumber.Text = item.ManufacturerPartNumber ?? "";
        }
        catch (Exception ex)
        {
            MessageBox.Show("Failed to load item: " + ex.Message, "Error",
                MessageBoxButtons.OK, MessageBoxIcon.Error);
        }
    }

    private async Task SaveAsync()
    {
        if (string.IsNullOrWhiteSpace(_txtName.Text) ||
            string.IsNullOrWhiteSpace(_txtCategory.Text))
        {
            MessageBox.Show("Name and Category are required.", "Validation",
                MessageBoxButtons.OK, MessageBoxIcon.Warning);
            return;
        }

        // SKU is required only in create mode (immutable in edit mode).
        if (_itemId is null && string.IsNullOrWhiteSpace(_txtSku.Text))
        {
            MessageBox.Show("SKU is required.", "Validation",
                MessageBoxButtons.OK, MessageBoxIcon.Warning);
            return;
        }

        try
        {
            var reorderPoint = decimal.TryParse(_txtReorderPoint.Text, out var rp) ? rp : 0;
            var reorderQty = decimal.TryParse(_txtReorderQty.Text, out var rq) ? rq : 0;
            decimal? unitCost = decimal.TryParse(_txtUnitCost.Text, out var uc) ? (decimal?)uc : null;

            if (reorderPoint < 0 || reorderQty < 0 || (unitCost.HasValue && unitCost < 0))
            {
                MessageBox.Show("Reorder Point, Reorder Quantity, and Unit Cost cannot be negative.",
                    "Validation", MessageBoxButtons.OK, MessageBoxIcon.Warning);
                return;
            }

            if (_itemId is null)
            {
                // Create mode
                var dto = new ItemCreateDto(
                    Sku: _txtSku.Text.Trim(),
                    Name: _txtName.Text.Trim(),
                    Description: string.IsNullOrWhiteSpace(_txtDescription.Text) ? null : _txtDescription.Text,
                    Category: _txtCategory.Text.Trim(),
                    Uom: (UnitOfMeasure)_cmbUom.SelectedItem!,
                    ReorderPoint: reorderPoint,
                    ReorderQty: reorderQty,
                    UnitCost: unitCost,
                    PreferredSupplier: string.IsNullOrWhiteSpace(_txtSupplier.Text) ? null : _txtSupplier.Text,
                    LeadTimeDays: int.TryParse(_txtLeadTime.Text, out var lt) ? lt : null,
                    HazardousFlag: _chkHazardous.Checked,
                    StorageRequirements: string.IsNullOrWhiteSpace(_txtStorage.Text) ? null : _txtStorage.Text,
                    Manufacturer: string.IsNullOrWhiteSpace(_txtManufacturer.Text) ? null : _txtManufacturer.Text,
                    ManufacturerPartNumber: string.IsNullOrWhiteSpace(_txtPartNumber.Text) ? null : _txtPartNumber.Text);

                await _inventoryService.CreateItemAsync(dto);
                MessageBox.Show($"Item '{dto.Sku}' created.", "Success",
                    MessageBoxButtons.OK, MessageBoxIcon.Information);
            }
            else
            {
                // Edit mode
                var dto = new ItemUpdateDto(
                    Id: _itemId.Value,
                    Name: _txtName.Text.Trim(),
                    Description: string.IsNullOrWhiteSpace(_txtDescription.Text) ? null : _txtDescription.Text,
                    Category: _txtCategory.Text.Trim(),
                    Uom: (UnitOfMeasure)_cmbUom.SelectedItem!,
                    ReorderPoint: reorderPoint,
                    ReorderQty: reorderQty,
                    UnitCost: unitCost,
                    PreferredSupplier: string.IsNullOrWhiteSpace(_txtSupplier.Text) ? null : _txtSupplier.Text,
                    LeadTimeDays: int.TryParse(_txtLeadTime.Text, out var lt) ? lt : null,
                    HazardousFlag: _chkHazardous.Checked,
                    StorageRequirements: string.IsNullOrWhiteSpace(_txtStorage.Text) ? null : _txtStorage.Text,
                    Manufacturer: string.IsNullOrWhiteSpace(_txtManufacturer.Text) ? null : _txtManufacturer.Text,
                    ManufacturerPartNumber: string.IsNullOrWhiteSpace(_txtPartNumber.Text) ? null : _txtPartNumber.Text);

                await _inventoryService.UpdateItemAsync(dto);
                MessageBox.Show($"Item '{_txtSku.Text}' updated.", "Success",
                    MessageBoxButtons.OK, MessageBoxIcon.Information);
            }

            DialogResult = DialogResult.OK;
            Close();
        }
        catch (UnauthorizedAccessException ex)
        {
            MessageBox.Show(ex.Message, "Permission denied",
                MessageBoxButtons.OK, MessageBoxIcon.Warning);
        }
        catch (Exception ex)
        {
            MessageBox.Show("Failed to save item: " + ex.Message, "Error",
                MessageBoxButtons.OK, MessageBoxIcon.Error);
        }
    }
}
