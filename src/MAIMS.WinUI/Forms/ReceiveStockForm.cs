using MAIMS.Core.DTOs;
using MAIMS.Core.Interfaces;
using MAIMS.Data;
using MAIMS.WinUI.Theming;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;

namespace MAIMS.WinUI.Forms;

/// <summary>
/// Stock receipt (Stock-In) form. Used by inventory clerks to record
/// incoming stock from a supplier: select item + warehouse, enter quantity
/// + lot + expiry + supplier + reference doc, then submit.
/// </summary>
public class ReceiveStockForm : Form
{
    private readonly IInventoryService _inventoryService;
    private readonly IServiceScopeFactory _scopeFactory;
    private readonly ComboBox _cmbItem;
    private readonly ComboBox _cmbWarehouse;
    private readonly TextBox _txtQuantity;
    private readonly TextBox _txtLotBatch;
    private readonly TextBox _txtBinLocation;  // NEW: bin/shelf location in the warehouse
    private readonly DateTimePicker _dtpExpiry;
    private readonly CheckBox _chkExpiry;
    private readonly TextBox _txtSupplier;
    private readonly TextBox _txtRefDoc;
    private readonly TextBox _txtNotes;
    private readonly Button _btnSave;
    private readonly Button _btnClose;
    private readonly Label _lblStatus;

    public ReceiveStockForm(IInventoryService inventoryService, IServiceScopeFactory scopeFactory)
    {
        _inventoryService = inventoryService;
        _scopeFactory = scopeFactory;
        Text = "Inventory — Receive Stock (Stock-In)";
        FormBorderStyle = FormBorderStyle.None;
        Dock = DockStyle.Fill;
        BackColor = MaimsTheme.Background;
        Font = MaimsTheme.Body;
        AutoScroll = true;

        var pnlForm = new Panel { Dock = DockStyle.Top, Height = 432, BackColor = MaimsTheme.Surface, Padding = new Padding(16) };

        int y = 16;
        AddLabel(pnlForm, "Item *:", y);
        _cmbItem = MkCombo(pnlForm, y);
        _cmbItem.Location = new Point(200, y);
        y += 32;

        AddLabel(pnlForm, "Warehouse *:", y);
        _cmbWarehouse = MkCombo(pnlForm, y);
        _cmbWarehouse.Location = new Point(200, y);
        y += 32;

        AddLabel(pnlForm, "Quantity *:", y);
        _txtQuantity = MkTxt(pnlForm, y);
        y += 32;

        AddLabel(pnlForm, "Bin Location:", y);
        _txtBinLocation = MkTxt(pnlForm, y);
        y += 32;

        AddLabel(pnlForm, "Lot / Batch:", y);
        _txtLotBatch = MkTxt(pnlForm, y);
        y += 32;

        AddLabel(pnlForm, "Expiry Date:", y);
        _chkExpiry = new CheckBox
        {
            Text = "Has expiry",
            Location = new Point(200, y),
            AutoSize = true,
            Font = MaimsTheme.Body
        };
        _dtpExpiry = new DateTimePicker
        {
            Location = new Point(330, y),
            Size = new Size(200, 24),
            Format = DateTimePickerFormat.Short,
            Font = MaimsTheme.Body,
            Enabled = false
        };
        _chkExpiry.CheckedChanged += (s, e) => _dtpExpiry.Enabled = _chkExpiry.Checked;
        pnlForm.Controls.Add(_chkExpiry);
        pnlForm.Controls.Add(_dtpExpiry);
        y += 32;

        AddLabel(pnlForm, "Supplier:", y);
        _txtSupplier = MkTxt(pnlForm, y);
        y += 32;

        AddLabel(pnlForm, "Reference Doc No.:", y);
        _txtRefDoc = MkTxt(pnlForm, y);
        y += 32;

        AddLabel(pnlForm, "Notes:", y);
        _txtNotes = new TextBox
        {
            Location = new Point(200, y),
            Size = new Size(330, 60),
            Multiline = true,
            Font = MaimsTheme.Body
        };
        pnlForm.Controls.Add(_txtNotes);
        y += 70;

        _btnSave = MaimsTheme.CreateButton("Receive Stock", primary: true);
        _btnClose = MaimsTheme.CreateButton("Close");
        MaimsTheme.LayoutButtons(200, y, 8, _btnSave, _btnClose);
        _btnClose.Click += (_, _) =>
        {
            if (Parent is TabPage page && page.Parent is TabControl tc)
                tc.TabPages.Remove(page);
            else
                Close();
        };
        _btnSave.Click += async (_, _) => await SaveAsync();
        pnlForm.Controls.Add(_btnSave);
        pnlForm.Controls.Add(_btnClose);
        y += 40;

        // Status label — placed INSIDE the form panel (not the form itself),
        // with AutoSize=true so it grows vertically if the message wraps to
        // multiple lines. The panel height (380px) leaves room for it below
        // the buttons (y reaches ~350 after the buttons).
        _lblStatus = new Label
        {
            Location = new Point(16, y),
            Size = new Size(540, 40),  // taller so multi-line messages are visible
            AutoSize = false,
            Font = MaimsTheme.Small,
            ForeColor = MaimsTheme.TextSecondary
        };
        pnlForm.Controls.Add(_lblStatus);

        Controls.Add(pnlForm);

        Load += async (_, _) => await LoadReferenceDataAsync();
    }

    private void AddLabel(Control parent, string text, int y)
    {
        parent.Controls.Add(new Label
        {
            Text = text,
            Location = new Point(16, y + 3),
            Size = new Size(180, 20),
            Font = MaimsTheme.Body
        });
    }

    private ComboBox MkCombo(Control parent, int y)
    {
        var cmb = new ComboBox
        {
            Location = new Point(200, y),
            Size = new Size(330, 24),
            DropDownStyle = ComboBoxStyle.DropDownList,
            Font = MaimsTheme.Body
        };
        parent.Controls.Add(cmb);
        return cmb;
    }

    private TextBox MkTxt(Control parent, int y)
    {
        var tb = new TextBox
        {
            Location = new Point(200, y),
            Size = new Size(330, 24),
            Font = MaimsTheme.Body
        };
        parent.Controls.Add(tb);
        return tb;
    }

    private async Task LoadReferenceDataAsync()
    {
        using var scope = _scopeFactory.CreateScope();
        var ctx = scope.ServiceProvider.GetRequiredService<MaimsDbContext>();

        var items = await ctx.Items.AsNoTracking().OrderBy(i => i.Sku).ToListAsync();
        _cmbItem.DisplayMember = "Sku";
        _cmbItem.ValueMember = "Id";
        // Show "SKU — Name" via formatting
        _cmbItem.DataSource = items.Select(i => new { i.Id, Display = $"{i.Sku} — {i.Name}" }).ToList();
        _cmbItem.DisplayMember = "Display";
        _cmbItem.ValueMember = "Id";

        var warehouses = await ctx.Warehouses.AsNoTracking().Where(w => w.IsActive).OrderBy(w => w.Name).ToListAsync();
        _cmbWarehouse.DisplayMember = "Name";
        _cmbWarehouse.ValueMember = "Id";
        _cmbWarehouse.DataSource = warehouses.ToList();
    }

    private async Task SaveAsync()
    {
        if (_cmbItem.SelectedValue is not long itemId || itemId <= 0)
        {
            MessageBox.Show("Please select an item.", "Validation", MessageBoxButtons.OK, MessageBoxIcon.Warning);
            return;
        }
        if (_cmbWarehouse.SelectedValue is not long whId || whId <= 0)
        {
            MessageBox.Show("Please select a warehouse.", "Validation", MessageBoxButtons.OK, MessageBoxIcon.Warning);
            return;
        }
        if (!decimal.TryParse(_txtQuantity.Text, out var qty) || qty <= 0)
        {
            MessageBox.Show("Quantity must be a positive number.", "Validation", MessageBoxButtons.OK, MessageBoxIcon.Warning);
            return;
        }

        try
        {
            _btnSave.Enabled = false;
            _lblStatus.Text = "Receiving…";

            var dto = new StockReceiptDto(
                ItemId: itemId,
                WarehouseId: whId,
                Quantity: qty,
                LotBatch: string.IsNullOrWhiteSpace(_txtLotBatch.Text) ? null : _txtLotBatch.Text,
                ExpiryDate: _chkExpiry.Checked ? (DateTime?)_dtpExpiry.Value.Date : null,
                Supplier: string.IsNullOrWhiteSpace(_txtSupplier.Text) ? null : _txtSupplier.Text,
                ReferenceDocNo: string.IsNullOrWhiteSpace(_txtRefDoc.Text) ? null : _txtRefDoc.Text,
                Notes: string.IsNullOrWhiteSpace(_txtNotes.Text) ? null : _txtNotes.Text,
                BinLocation: string.IsNullOrWhiteSpace(_txtBinLocation.Text) ? null : _txtBinLocation.Text);

            var result = await _inventoryService.ReceiveAsync(dto);

            // Reset form for next receipt
            _txtQuantity.Text = "";
            _txtBinLocation.Text = "";
            _txtLotBatch.Text = "";
            _chkExpiry.Checked = false;
            _txtSupplier.Text = "";
            _txtRefDoc.Text = "";
            _txtNotes.Text = "";

            _lblStatus.Text = $"✓ Received {qty} units. New on-hand: {result.QtyOnHand}.";
            _lblStatus.ForeColor = MaimsTheme.OK;
        }
        catch (UnauthorizedAccessException ex)
        {
            MessageBox.Show(ex.Message, "Permission denied", MessageBoxButtons.OK, MessageBoxIcon.Warning);
        }
        catch (Exception ex)
        {
            MessageBox.Show("Receive failed: " + ex.Message, "Error", MessageBoxButtons.OK, MessageBoxIcon.Error);
        }
        finally
        {
            _btnSave.Enabled = true;
        }
    }
}
