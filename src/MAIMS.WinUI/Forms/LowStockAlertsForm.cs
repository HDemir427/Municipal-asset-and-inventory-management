using MAIMS.Core.Interfaces;
using MAIMS.WinUI.Theming;

namespace MAIMS.WinUI.Forms;

/// <summary>
/// Lists every item whose on-hand quantity has dropped to or below its
/// reorder point — across ALL warehouses. Used by purchasing to know which
/// SKUs to reorder.
/// </summary>
public class LowStockAlertsForm : Form
{
    private readonly IInventoryService _inventoryService;
    private readonly Button _btnRefresh;
    private readonly Button _btnClose;
    private readonly DataGridView _grid;
    private readonly Label _statusLabel;
    private bool _isLoading;  // re-entrancy guard

    public LowStockAlertsForm(IInventoryService inventoryService)
    {
        _inventoryService = inventoryService;
        Text = "Inventory — Low Stock Alerts";
        FormBorderStyle = FormBorderStyle.None;
        Dock = DockStyle.Fill;
        BackColor = MaimsTheme.Background;

        var pnlTop = new Panel { Dock = DockStyle.Top, Height = 50, BackColor = MaimsTheme.Surface, Padding = new Padding(8) };
        var lblTitle = new Label
        {
            Text = "Items at or below their reorder point:",
            Location = new Point(8, 14),
            AutoSize = true,
            Font = MaimsTheme.Body,
            ForeColor = MaimsTheme.Warning
        };

        _btnRefresh = MaimsTheme.CreateButton("Refresh", primary: true);
        _btnClose = MaimsTheme.CreateButton("Close");
        MaimsTheme.LayoutButtons(400, 9, 8, _btnRefresh, _btnClose);
        _btnClose.Click += (_, _) =>
        {
            if (Parent is TabPage page && page.Parent is TabControl tc)
                tc.TabPages.Remove(page);
            else
                Close();
        };

        pnlTop.Controls.AddRange(new Control[] { lblTitle, _btnRefresh, _btnClose });

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

        AddCol("Sku", "SKU", 110);
        AddCol("ItemName", "Item", 220);
        AddCol("WarehouseName", "Warehouse", 150);
        AddCol("BinLocation", "Bin", 90);
        AddCol("QtyOnHand", "On Hand", 100, "N3");
        AddCol("ReorderPoint", "Reorder Pt", 100, "N3");
        AddCol("QtyReserved", "Reserved", 100, "N3");

        // Highlight low-stock rows in warning color
        _grid.CellFormatting += (_, e) =>
        {
            if (e.RowIndex < 0) return;
            if (_grid.Columns[e.ColumnIndex].Name == "QtyOnHand")
            {
                e.CellStyle!.ForeColor = MaimsTheme.Critical;
                e.CellStyle.Font = new Font(MaimsTheme.Body, FontStyle.Bold);
            }
        };

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
        // Re-entrancy guard: prevents concurrent GetLowStockAsync calls on the
        // same scoped DbContext if Refresh is clicked while a Load is in flight.
        if (_isLoading) return;
        _isLoading = true;
        try
        {
            _statusLabel.Text = "Loading…";
            var rows = await _inventoryService.GetLowStockAsync();
            var view = rows.Select(r => new
            {
                r.Sku,
                r.ItemName,
                r.WarehouseName,
                BinLocation = r.BinLocation ?? "",
                r.QtyOnHand,
                r.ReorderPoint,
                r.QtyReserved
            }).ToList();
            _grid.DataSource = null;  // force DataGridView to rebind
            _grid.DataSource = view;
            _statusLabel.Text = $"{view.Count} item(s) below reorder point.";
        }
        catch (UnauthorizedAccessException ex)
        {
            MessageBox.Show(ex.Message, "Permission denied", MessageBoxButtons.OK, MessageBoxIcon.Warning);
        }
        catch (Exception ex)
        {
            MessageBox.Show("Failed to load low-stock items: " + ex.Message, "Error", MessageBoxButtons.OK, MessageBoxIcon.Error);
        }
        finally
        {
            _isLoading = false;
        }
    }
}
