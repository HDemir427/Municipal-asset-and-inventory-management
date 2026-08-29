using MAIMS.Core.DTOs;
using MAIMS.Core.Interfaces;
using MAIMS.Data;
using MAIMS.WinUI.Theming;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;

namespace MAIMS.WinUI.Forms;

/// <summary>
/// Stock transfer form. Moves stock between two warehouses atomically:
/// decreases on-hand at the source, increases on-hand at the destination,
/// records a single StockTransaction of type Transfer for audit.
/// </summary>
public class StockTransferForm : Form
{
    private readonly IInventoryService _inventoryService;
    private readonly IServiceScopeFactory _scopeFactory;
    private readonly ComboBox _cmbItem;
    private readonly ComboBox _cmbFromWarehouse;
    private readonly ComboBox _cmbToWarehouse;
    private readonly TextBox _txtQuantity;
    private readonly TextBox _txtRefDoc;
    private readonly TextBox _txtNotes;
    private readonly Button _btnSave;
    private readonly Button _btnClose;
    private readonly Label _lblStatus;

    public StockTransferForm(IInventoryService inventoryService, IServiceScopeFactory scopeFactory)
    {
        _inventoryService = inventoryService;
        _scopeFactory = scopeFactory;
        Text = "Inventory — Transfer Stock";
        FormBorderStyle = FormBorderStyle.None;
        Dock = DockStyle.Fill;
        BackColor = MaimsTheme.Background;
        Font = MaimsTheme.Body;
        AutoScroll = true;

        var pnlForm = new Panel { Dock = DockStyle.Top, Height = 360, BackColor = MaimsTheme.Surface, Padding = new Padding(16) };

        int y = 16;
        AddLabel(pnlForm, "Item *:", y);
        _cmbItem = MkCombo(pnlForm, y);
        y += 32;

        AddLabel(pnlForm, "From Warehouse *:", y);
        _cmbFromWarehouse = MkCombo(pnlForm, y);
        y += 32;

        AddLabel(pnlForm, "To Warehouse *:", y);
        _cmbToWarehouse = MkCombo(pnlForm, y);
        y += 32;

        AddLabel(pnlForm, "Quantity *:", y);
        _txtQuantity = MkTxt(pnlForm, y);
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

        _btnSave = MaimsTheme.CreateButton("Transfer Stock", primary: true);
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

        _lblStatus = new Label
        {
            Location = new Point(16, y),
            Size = new Size(540, 24),
            Font = MaimsTheme.Small,
            ForeColor = MaimsTheme.TextSecondary
        };
        pnlForm.Controls.Add(_lblStatus);

        Controls.Add(pnlForm);
        Load += async (_, _) => await LoadReferenceDataAsync();
    }

    private void AddLabel(Control parent, string text, int y) =>
        parent.Controls.Add(new Label
        {
            Text = text,
            Location = new Point(16, y + 3),
            Size = new Size(180, 20),
            Font = MaimsTheme.Body
        });

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
        _cmbItem.DisplayMember = "Display";
        _cmbItem.ValueMember = "Id";
        _cmbItem.DataSource = items.Select(i => new { i.Id, Display = $"{i.Sku} — {i.Name}" }).ToList();

        var warehouses = await ctx.Warehouses.AsNoTracking().Where(w => w.IsActive).OrderBy(w => w.Name).ToListAsync();
        _cmbFromWarehouse.DisplayMember = "Name";
        _cmbFromWarehouse.ValueMember = "Id";
        _cmbFromWarehouse.DataSource = warehouses.ToList();

        _cmbToWarehouse.DisplayMember = "Name";
        _cmbToWarehouse.ValueMember = "Id";
        _cmbToWarehouse.DataSource = warehouses.ToList();
        if (_cmbToWarehouse.Items.Count > 1) _cmbToWarehouse.SelectedIndex = 1;
    }

    private async Task SaveAsync()
    {
        if (_cmbItem.SelectedValue is not long itemId || itemId <= 0)
        {
            MessageBox.Show("Please select an item.", "Validation",
                MessageBoxButtons.OK, MessageBoxIcon.Warning);
            return;
        }
        if (_cmbFromWarehouse.SelectedValue is not long fromId || fromId <= 0)
        {
            MessageBox.Show("Please select the source warehouse.", "Validation",
                MessageBoxButtons.OK, MessageBoxIcon.Warning);
            return;
        }
        if (_cmbToWarehouse.SelectedValue is not long toId || toId <= 0)
        {
            MessageBox.Show("Please select the destination warehouse.", "Validation",
                MessageBoxButtons.OK, MessageBoxIcon.Warning);
            return;
        }
        if (fromId == toId)
        {
            MessageBox.Show("Source and destination warehouses must be different.",
                "Validation", MessageBoxButtons.OK, MessageBoxIcon.Warning);
            return;
        }
        if (!decimal.TryParse(_txtQuantity.Text, out var qty) || qty <= 0)
        {
            MessageBox.Show("Quantity must be a positive number.", "Validation",
                MessageBoxButtons.OK, MessageBoxIcon.Warning);
            return;
        }

        try
        {
            _btnSave.Enabled = false;
            _lblStatus.Text = "Transferring…";

            // Get current user ID from session for the approver.
            long approverUserId;
            using (var scope = _scopeFactory.CreateScope())
            {
                var session = scope.ServiceProvider.GetRequiredService<MAIMS.Core.Abstractions.ICurrentSession>();
                approverUserId = session.UserId ?? 1;
            }

            var dto = new StockTransferDto(
                ItemId: itemId,
                FromWarehouseId: fromId,
                ToWarehouseId: toId,
                Quantity: qty,
                ApprovedByUserId: approverUserId,
                ReferenceDocNo: string.IsNullOrWhiteSpace(_txtRefDoc.Text) ? null : _txtRefDoc.Text,
                Notes: string.IsNullOrWhiteSpace(_txtNotes.Text) ? null : _txtNotes.Text);

            await _inventoryService.TransferAsync(dto);

            _txtQuantity.Text = "";
            _txtRefDoc.Text = "";
            _txtNotes.Text = "";

            _lblStatus.Text = $"✓ Transferred {qty} units from source to destination.";
            _lblStatus.ForeColor = MaimsTheme.OK;
        }
        catch (UnauthorizedAccessException ex)
        {
            MessageBox.Show(ex.Message, "Permission denied", MessageBoxButtons.OK, MessageBoxIcon.Warning);
        }
        catch (InvalidOperationException ex)
        {
            // Insufficient stock at source
            MessageBox.Show(ex.Message, "Insufficient stock", MessageBoxButtons.OK, MessageBoxIcon.Warning);
        }
        catch (Exception ex)
        {
            MessageBox.Show("Transfer failed: " + ex.Message, "Error",
                MessageBoxButtons.OK, MessageBoxIcon.Error);
        }
        finally
        {
            _btnSave.Enabled = true;
        }
    }
}
