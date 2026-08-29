using MAIMS.Core.DTOs;
using MAIMS.Core.Enums;
using MAIMS.Core.Interfaces;
using MAIMS.Data;
using MAIMS.WinUI.Theming;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;

namespace MAIMS.WinUI.Forms;

/// <summary>
/// Stock adjustment form. Used to record a counted quantity (cycle count)
/// and let the system compute the delta vs. book stock. Mandatory reason
/// code per spec §FR-I03.
/// </summary>
public class StockAdjustmentForm : Form
{
    private readonly IInventoryService _inventoryService;
    private readonly IServiceScopeFactory _scopeFactory;
    private readonly ComboBox _cmbItem;
    private readonly ComboBox _cmbWarehouse;
    private readonly TextBox _txtCurrentQty;
    private readonly TextBox _txtNewQty;
    private readonly ComboBox _cmbReason;
    private readonly TextBox _txtRefDoc;
    private readonly TextBox _txtNotes;
    private readonly Button _btnSave;
    private readonly Button _btnClose;
    private readonly Label _lblStatus;
    private bool _initializing;    // suppresses SelectedIndexChanged during LoadReferenceDataAsync
    private bool _isLoadingQty;    // re-entrancy guard for UpdateCurrentQtyAsync

    public StockAdjustmentForm(IInventoryService inventoryService, IServiceScopeFactory scopeFactory)
    {
        _inventoryService = inventoryService;
        _scopeFactory = scopeFactory;
        Text = "Inventory — Stock Adjustment";
        FormBorderStyle = FormBorderStyle.None;
        Dock = DockStyle.Fill;
        BackColor = MaimsTheme.Background;
        Font = MaimsTheme.Body;
        AutoScroll = true;

        var pnlForm = new Panel { Dock = DockStyle.Top, Height = 400, BackColor = MaimsTheme.Surface, Padding = new Padding(16) };

        int y = 16;
        AddLabel(pnlForm, "Item *:", y);
        _cmbItem = MkCombo(pnlForm, y, 330);
        _cmbItem.SelectedIndexChanged += async (_, _) =>
        {
            if (_initializing) return;
            await UpdateCurrentQtyAsync();
        };
        y += 32;

        AddLabel(pnlForm, "Warehouse *:", y);
        _cmbWarehouse = MkCombo(pnlForm, y, 330);
        _cmbWarehouse.SelectedIndexChanged += async (_, _) =>
        {
            if (_initializing) return;
            await UpdateCurrentQtyAsync();
        };
        y += 32;

        AddLabel(pnlForm, "Current Qty (book):", y);
        _txtCurrentQty = new TextBox
        {
            Location = new Point(200, y),
            Size = new Size(150, 24),
            ReadOnly = true,
            BackColor = Color.FromArgb(245, 245, 245),
            Font = MaimsTheme.Body
        };
        pnlForm.Controls.Add(_txtCurrentQty);
        y += 32;

        AddLabel(pnlForm, "Counted Qty *:", y);
        _txtNewQty = MkTxt(pnlForm, y);
        y += 32;

        AddLabel(pnlForm, "Reason Code *:", y);
        _cmbReason = new ComboBox
        {
            Location = new Point(200, y),
            Size = new Size(330, 24),
            DropDownStyle = ComboBoxStyle.DropDownList,
            Font = MaimsTheme.Body
        };
        _cmbReason.Items.AddRange(new object[]
        {
            StockReasonCodes.Damage,
            StockReasonCodes.Loss,
            StockReasonCodes.CountCorrection,
            StockReasonCodes.Expired,
            StockReasonCodes.CountAdjustment,
            StockReasonCodes.Obsolete
        });
        _cmbReason.SelectedIndex = 0;
        pnlForm.Controls.Add(_cmbReason);
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

        _btnSave = MaimsTheme.CreateButton("Apply Adjustment", primary: true);
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

    private ComboBox MkCombo(Control parent, int y, int w)
    {
        var cmb = new ComboBox
        {
            Location = new Point(200, y),
            Size = new Size(w, 24),
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
            Size = new Size(150, 24),
            Font = MaimsTheme.Body
        };
        parent.Controls.Add(tb);
        return tb;
    }

    private async Task LoadReferenceDataAsync()
    {
        _initializing = true;
        try
        {
            using var scope = _scopeFactory.CreateScope();
            var ctx = scope.ServiceProvider.GetRequiredService<MaimsDbContext>();

            var items = await ctx.Items.AsNoTracking().OrderBy(i => i.Sku).ToListAsync();
            _cmbItem.DisplayMember = "Display";
            _cmbItem.ValueMember = "Id";
            _cmbItem.DataSource = items.Select(i => new { i.Id, Display = $"{i.Sku} — {i.Name}" }).ToList();

            var warehouses = await ctx.Warehouses.AsNoTracking().Where(w => w.IsActive).OrderBy(w => w.Name).ToListAsync();
            _cmbWarehouse.DisplayMember = "Name";
            _cmbWarehouse.ValueMember = "Id";
            _cmbWarehouse.DataSource = warehouses.ToList();

            // Now that both combos are populated, manually trigger the first qty load.
            // SelectedIndexChanged was suppressed during DataSource assignment.
            await UpdateCurrentQtyAsync();
        }
        catch (Exception ex)
        {
            MessageBox.Show("Failed to load reference data: " + ex.Message, "Error",
                MessageBoxButtons.OK, MessageBoxIcon.Error);
        }
        finally
        {
            _initializing = false;
        }
    }

    private async Task UpdateCurrentQtyAsync()
    {
        // Re-entrancy guard: both _cmbItem and _cmbWarehouse fire SelectedIndexChanged
        // when their DataSource is assigned. Without _initializing + _isLoadingQty,
        // two concurrent GetBalancesAsync calls would hit the same scoped DbContext.
        if (_isLoadingQty) return;
        _isLoadingQty = true;
        try
        {
            if (_cmbItem.SelectedValue is not long itemId || itemId <= 0) return;
            if (_cmbWarehouse.SelectedValue is not long whId || whId <= 0) return;

            try
            {
                var balances = await _inventoryService.GetBalancesAsync(whId);
                var bal = balances.FirstOrDefault(b => b.ItemId == itemId);
                _txtCurrentQty.Text = bal?.QtyOnHand.ToString("N3") ?? "0.000";
            }
            catch
            {
                _txtCurrentQty.Text = "—";
            }
        }
        finally
        {
            _isLoadingQty = false;
        }
    }

    private async Task SaveAsync()
    {
        if (_cmbItem.SelectedValue is not long itemId || itemId <= 0)
        {
            MessageBox.Show("Please select an item.", "Validation",
                MessageBoxButtons.OK, MessageBoxIcon.Warning);
            return;
        }
        if (_cmbWarehouse.SelectedValue is not long whId || whId <= 0)
        {
            MessageBox.Show("Please select a warehouse.", "Validation",
                MessageBoxButtons.OK, MessageBoxIcon.Warning);
            return;
        }
        if (!decimal.TryParse(_txtNewQty.Text, out var newQty) || newQty < 0)
        {
            MessageBox.Show("Counted quantity must be a non-negative number.", "Validation",
                MessageBoxButtons.OK, MessageBoxIcon.Warning);
            return;
        }

        var reason = _cmbReason.SelectedItem?.ToString() ?? "";
        if (string.IsNullOrWhiteSpace(reason))
        {
            MessageBox.Show("Reason code is required.", "Validation",
                MessageBoxButtons.OK, MessageBoxIcon.Warning);
            return;
        }

        try
        {
            _btnSave.Enabled = false;
            _lblStatus.Text = "Applying adjustment…";

            var dto = new StockAdjustmentDto(
                ItemId: itemId,
                WarehouseId: whId,
                NewQuantity: newQty,
                ReasonCode: reason,
                ReferenceDocNo: string.IsNullOrWhiteSpace(_txtRefDoc.Text) ? null : _txtRefDoc.Text,
                Notes: string.IsNullOrWhiteSpace(_txtNotes.Text) ? null : _txtNotes.Text);

            var result = await _inventoryService.AdjustAsync(dto);

            _txtNewQty.Text = "";
            _txtRefDoc.Text = "";
            _txtNotes.Text = "";
            await UpdateCurrentQtyAsync();

            _lblStatus.Text = $"✓ Adjusted. New on-hand: {result.QtyOnHand}.";
            _lblStatus.ForeColor = MaimsTheme.OK;
        }
        catch (UnauthorizedAccessException ex)
        {
            MessageBox.Show(ex.Message, "Permission denied", MessageBoxButtons.OK, MessageBoxIcon.Warning);
        }
        catch (Exception ex)
        {
            MessageBox.Show("Adjustment failed: " + ex.Message, "Error",
                MessageBoxButtons.OK, MessageBoxIcon.Error);
        }
        finally
        {
            _btnSave.Enabled = true;
        }
    }
}
