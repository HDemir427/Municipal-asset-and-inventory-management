using MAIMS.Core.DTOs;
using MAIMS.Core.Interfaces;
using MAIMS.Data;
using MAIMS.WinUI.Theming;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;

namespace MAIMS.WinUI.Forms;

/// <summary>
/// Stock issue (Stock-Out) form. Used by inventory clerks to record stock
/// leaving the warehouse: select item + warehouse, enter quantity + optional
/// asset (issuing to a specific asset) + requester + work order + ref doc.
/// </summary>
public class IssueStockForm : Form
{
    private readonly IInventoryService _inventoryService;
    private readonly IServiceScopeFactory _scopeFactory;
    private readonly ComboBox _cmbItem;
    private readonly ComboBox _cmbWarehouse;
    private readonly ComboBox _cmbToAsset;
    private readonly ComboBox _cmbRequester;
    private readonly TextBox _txtQuantity;
    private readonly TextBox _txtPurpose;
    private readonly TextBox _txtRefDoc;
    private readonly TextBox _txtNotes;
    private readonly Button _btnSave;
    private readonly Button _btnClose;
    private readonly Label _lblStatus;

    public IssueStockForm(IInventoryService inventoryService, IServiceScopeFactory scopeFactory)
    {
        _inventoryService = inventoryService;
        _scopeFactory = scopeFactory;
        Text = "Inventory — Issue Stock (Stock-Out)";
        FormBorderStyle = FormBorderStyle.None;
        Dock = DockStyle.Fill;
        BackColor = MaimsTheme.Background;
        Font = MaimsTheme.Body;
        AutoScroll = true;

        var pnlForm = new Panel { Dock = DockStyle.Top, Height = 380, BackColor = MaimsTheme.Surface, Padding = new Padding(16) };

        int y = 16;
        AddLabel(pnlForm, "Item *:", y);
        _cmbItem = MkCombo(pnlForm, y, 330);
        y += 32;

        AddLabel(pnlForm, "Warehouse *:", y);
        _cmbWarehouse = MkCombo(pnlForm, y, 330);
        y += 32;

        AddLabel(pnlForm, "Quantity *:", y);
        _txtQuantity = MkTxt(pnlForm, y);
        y += 32;

        AddLabel(pnlForm, "To Asset (optional):", y);
        _cmbToAsset = MkCombo(pnlForm, y, 330);
        y += 32;

        AddLabel(pnlForm, "Requester (optional):", y);
        _cmbRequester = MkCombo(pnlForm, y, 330);
        y += 32;

        AddLabel(pnlForm, "Purpose / Work Order:", y);
        _txtPurpose = MkTxt(pnlForm, y);
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

        _btnSave = MaimsTheme.CreateButton("Issue Stock", primary: true);
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
        _cmbWarehouse.DisplayMember = "Name";
        _cmbWarehouse.ValueMember = "Id";
        _cmbWarehouse.DataSource = warehouses.ToList();

        // Assets — for "issue to asset" (e.g., a part installed on a vehicle)
        var assets = await ctx.Assets.AsNoTracking().OrderBy(a => a.AssetCode).ToListAsync();
        _cmbToAsset.DisplayMember = "Display";
        _cmbToAsset.ValueMember = "Id";
        _cmbToAsset.DataSource = new[] { new { Id = 0L, Display = "(none)" } }
            .Concat(assets.Select(a => new { a.Id, Display = $"{a.AssetCode} — {a.Name}" }))
            .ToList();
        _cmbToAsset.SelectedIndex = 0;

        // Requesters — users (limited dropdown)
        var users = await ctx.Users.AsNoTracking().OrderBy(u => u.Name).ToListAsync();
        _cmbRequester.DisplayMember = "Display";
        _cmbRequester.ValueMember = "Id";
        _cmbRequester.DataSource = new[] { new { Id = 0L, Display = "(none)" } }
            .Concat(users.Select(u => new { u.Id, Display = $"{u.Username} — {u.Name}" }))
            .ToList();
        _cmbRequester.SelectedIndex = 0;
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
            _lblStatus.Text = "Issuing…";

            long? toAssetId = _cmbToAsset.SelectedValue is long a && a > 0 ? a : null;
            long? requesterId = _cmbRequester.SelectedValue is long r && r > 0 ? r : null;

            var dto = new StockIssueDto(
                ItemId: itemId,
                WarehouseId: whId,
                Quantity: qty,
                ToAssetId: toAssetId,
                RequesterUserId: requesterId,
                PurposeWorkOrder: string.IsNullOrWhiteSpace(_txtPurpose.Text) ? null : _txtPurpose.Text,
                ReferenceDocNo: string.IsNullOrWhiteSpace(_txtRefDoc.Text) ? null : _txtRefDoc.Text,
                Notes: string.IsNullOrWhiteSpace(_txtNotes.Text) ? null : _txtNotes.Text);

            var result = await _inventoryService.IssueAsync(dto);

            // Reset form for next issue
            _txtQuantity.Text = "";
            _txtPurpose.Text = "";
            _txtRefDoc.Text = "";
            _txtNotes.Text = "";

            _lblStatus.Text = $"✓ Issued {qty} units. Remaining on-hand: {result.QtyOnHand}.";
            _lblStatus.ForeColor = MaimsTheme.OK;
        }
        catch (UnauthorizedAccessException ex)
        {
            MessageBox.Show(ex.Message, "Permission denied", MessageBoxButtons.OK, MessageBoxIcon.Warning);
        }
        catch (InvalidOperationException ex)
        {
            // Insufficient stock
            MessageBox.Show(ex.Message, "Insufficient stock", MessageBoxButtons.OK, MessageBoxIcon.Warning);
        }
        catch (Exception ex)
        {
            MessageBox.Show("Issue failed: " + ex.Message, "Error", MessageBoxButtons.OK, MessageBoxIcon.Error);
        }
        finally
        {
            _btnSave.Enabled = true;
        }
    }
}
