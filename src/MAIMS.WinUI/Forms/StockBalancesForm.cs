using MAIMS.Core.DTOs;
using MAIMS.Core.Interfaces;
using MAIMS.Data;
using MAIMS.WinUI.Theming;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;

namespace MAIMS.WinUI.Forms;

/// <summary>
/// Shows stock balances per warehouse. User selects a warehouse from the
/// dropdown and the grid shows all items with their on-hand / reserved /
/// on-order quantities, plus a "Below Reorder?" flag.
/// </summary>
public class StockBalancesForm : Form
{
    private readonly IInventoryService _inventoryService;
    private readonly IServiceScopeFactory _scopeFactory;
    private readonly ComboBox _cmbWarehouse;
    private readonly Button _btnRefresh;
    private readonly Button _btnClose;
    private readonly DataGridView _grid;
    private readonly Label _statusLabel;
    private bool _isLoading;       // re-entrancy guard for LoadDataAsync
    private bool _initializing;    // suppresses SelectedIndexChanged during InitAsync

    public StockBalancesForm(IInventoryService inventoryService, IServiceScopeFactory scopeFactory)
    {
        _inventoryService = inventoryService;
        _scopeFactory = scopeFactory;
        Text = "Inventory — Stock Balances";
        FormBorderStyle = FormBorderStyle.None;
        Dock = DockStyle.Fill;
        BackColor = MaimsTheme.Background;

        var pnlTop = new Panel { Dock = DockStyle.Top, Height = 50, BackColor = MaimsTheme.Surface, Padding = new Padding(8) };
        var lblWh = new Label { Text = "Warehouse:", Location = new Point(8, 14), AutoSize = true, Font = MaimsTheme.Body };
        _cmbWarehouse = new ComboBox
        {
            Location = new Point(100, 12),
            Size = new Size(250, 24),
            DropDownStyle = ComboBoxStyle.DropDownList,
            Font = MaimsTheme.Body
        };
        _cmbWarehouse.DisplayMember = "Name";
        _cmbWarehouse.ValueMember = "Id";

        _btnRefresh = MaimsTheme.CreateButton("Refresh", primary: true);
        _btnClose = MaimsTheme.CreateButton("Close");
        MaimsTheme.LayoutButtons(360, 9, 8, _btnRefresh, _btnClose);
        _btnClose.Click += (_, _) =>
        {
            if (Parent is TabPage page && page.Parent is TabControl tc)
                tc.TabPages.Remove(page);
            else
                Close();
        };

        pnlTop.Controls.AddRange(new Control[] { lblWh, _cmbWarehouse, _btnRefresh, _btnClose });

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

        AddCol("ItemName", "Item", 220);
        AddCol("Sku", "SKU", 110);
        AddCol("WarehouseName", "Warehouse", 150);
        AddCol("BinLocation", "Bin", 90);
        AddCol("QtyOnHand", "On Hand", 100, "N3");
        // QtyReserved and QtyOnOrder columns removed — these fields are never
        // populated by any service method (reservation/PO systems not yet
        // implemented), so they always showed 0.000 which was misleading.
        AddCol("ReorderPoint", "Reorder Pt", 100, "N3");
        AddCol("BelowReorderPoint", "Below?", 70);

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

        _btnRefresh.Click += async (_, _) => await LoadDataAsync();
        // SelectedIndexChanged fires when DataSource is assigned AND when
        // SelectedIndex changes. During InitAsync we set _initializing=true
        // to suppress the handler — otherwise it would start LoadDataAsync
        // concurrently with InitAsync's own LoadDataAsync call, causing
        // "A second operation was started on this context instance".
        _cmbWarehouse.SelectedIndexChanged += async (_, _) =>
        {
            if (_initializing) return;
            await LoadDataAsync();
        };
        Load += async (_, _) => await InitAsync();
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

    private async Task InitAsync()
    {
        _initializing = true;
        try
        {
            // Load warehouses from DB
            using var scope = _scopeFactory.CreateScope();
            var ctx = scope.ServiceProvider.GetRequiredService<MAIMS.Data.MaimsDbContext>();
            var warehouses = await ctx.Warehouses.AsNoTracking().Where(w => w.IsActive).OrderBy(w => w.Name).ToListAsync();
            _cmbWarehouse.DataSource = warehouses;

            if (warehouses.Count > 0)
            {
                _cmbWarehouse.SelectedIndex = 0;
                // Manually trigger the first load (SelectedIndexChanged was suppressed).
                await LoadDataAsync();
            }
            else
            {
                _statusLabel.Text = "No warehouses found. Use the Warehouse module to create one.";
            }
        }
        catch (Exception ex)
        {
            MessageBox.Show("Failed to load warehouses: " + ex.Message, "Error",
                MessageBoxButtons.OK, MessageBoxIcon.Error);
        }
        finally
        {
            _initializing = false;
        }
    }

    private async Task LoadDataAsync()
    {
        // Re-entrancy guard: prevent concurrent DB operations on the same
        // scoped DbContext (_inventoryService holds a scoped IUnitOfWork →
        // scoped MaimsDbContext). Two concurrent calls would crash with
        // "A second operation was started on this context instance".
        if (_isLoading) return;
        _isLoading = true;
        try
        {
            if (_cmbWarehouse.SelectedValue is not long whId) return;
            _statusLabel.Text = "Loading…";
            var balances = await _inventoryService.GetBalancesAsync(whId);
            var rows = balances.Select(b => new
            {
                ItemName = b.ItemName,
                Sku = b.Sku,
                WarehouseName = b.WarehouseName,
                BinLocation = b.BinLocation ?? "",
                QtyOnHand = b.QtyOnHand,
                QtyReserved = b.QtyReserved,
                QtyOnOrder = b.QtyOnOrder,
                ReorderPoint = b.ReorderPoint,
                BelowReorderPoint = b.BelowReorderPoint ? "YES" : ""
            }).ToList();
            _grid.DataSource = null;  // force DataGridView to rebind
            _grid.DataSource = rows;
            _statusLabel.Text = $"{rows.Count} items in warehouse.";
        }
        catch (UnauthorizedAccessException ex)
        {
            MessageBox.Show(ex.Message, "Permission denied", MessageBoxButtons.OK, MessageBoxIcon.Warning);
        }
        catch (Exception ex)
        {
            MessageBox.Show("Failed to load balances: " + ex.Message, "Error", MessageBoxButtons.OK, MessageBoxIcon.Error);
        }
        finally
        {
            _isLoading = false;
        }
    }
}
