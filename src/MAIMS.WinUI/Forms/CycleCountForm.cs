using MAIMS.Core.DTOs;
using MAIMS.Core.Enums;
using MAIMS.Core.Interfaces;
using MAIMS.Data;
using MAIMS.WinUI.Theming;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;

namespace MAIMS.WinUI.Forms;

/// <summary>
/// Cycle count form. Generates a count sheet for a warehouse: lists all
/// items with their book (system) quantity. The user enters the counted
/// (physical) quantity for each item, then submits. Items where counted ≠ book
/// are submitted as StockAdjustment with reason COUNT_ADJUSTMENT.
/// </summary>
public class CycleCountForm : Form
{
    private readonly IServiceScopeFactory _scopeFactory;
    private readonly ComboBox _cmbWarehouse;
    private readonly DataGridView _grid;
    private readonly Button _btnLoad;
    private readonly Button _btnSubmit;
    private readonly Button _btnClose;
    private readonly Label _statusLabel;

    public CycleCountForm(IServiceScopeFactory scopeFactory)
    {
        _scopeFactory = scopeFactory;
        Text = "Cycle Count (Physical Inventory)";
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

        _btnLoad = MaimsTheme.CreateButton("Load Sheet", primary: true);
        _btnSubmit = MaimsTheme.CreateButton("Submit Count");
        _btnClose = MaimsTheme.CreateButton("Close");
        MaimsTheme.LayoutButtons(360, 10, 8, _btnLoad, _btnSubmit, _btnClose);
        _btnClose.Click += (_, _) =>
        {
            if (Parent is TabPage page && page.Parent is TabControl tc)
                tc.TabPages.Remove(page);
            else
                Close();
        };

        pnlTop.Controls.AddRange(new Control[] { lblWh, _cmbWarehouse, _btnLoad, _btnSubmit, _btnClose });

        _grid = new DataGridView
        {
            Dock = DockStyle.Fill,
            AutoGenerateColumns = false,
            AllowUserToAddRows = false,
            AllowUserToDeleteRows = false,
            ReadOnly = false,
            SelectionMode = DataGridViewSelectionMode.FullRowSelect,
            BackgroundColor = MaimsTheme.Surface,
            BorderStyle = BorderStyle.None,
            RowHeadersVisible = false,
            Font = MaimsTheme.Body,
            AutoSizeColumnsMode = DataGridViewAutoSizeColumnsMode.Fill
        };

        AddCol("Sku", "SKU", 110, readOnly: true);
        AddCol("ItemName", "Item", 200, readOnly: true);
        AddCol("BinLocation", "Bin", 90, readOnly: true);
        AddCol("BookQty", "Book Qty", 100, readOnly: true, format: "N3");
        AddCol("CountedQty", "Counted Qty", 110, readOnly: false, format: "N3");
        AddCol("Variance", "Variance", 100, readOnly: true, format: "N3");
        AddCol("Uom", "UoM", 60, readOnly: true);

        // Highlight rows with non-zero variance
        _grid.CellValueChanged += (_, e) =>
        {
            if (e.RowIndex < 0) return;
            RecomputeVariance(e.RowIndex);
        };
        _grid.CurrentCellDirtyStateChanged += (_, _) =>
        {
            if (_grid.IsCurrentCellDirty)
                _grid.CommitEdit(DataGridViewDataErrorContexts.Commit);
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

        _btnLoad.Click += async (_, _) => await LoadSheetAsync();
        _btnSubmit.Click += async (_, _) => await SubmitAsync();
        Load += async (_, _) => await InitAsync();
    }

    private void AddCol(string prop, string header, int width, bool readOnly, string? format = null)
    {
        _grid.Columns.Add(new DataGridViewTextBoxColumn
        {
            DataPropertyName = prop,
            HeaderText = header,
            Name = prop,
            Width = width,
            ReadOnly = readOnly,
            DefaultCellStyle = new DataGridViewCellStyle { Format = format ?? "" }
        });
    }

    private async Task InitAsync()
    {
        using var scope = _scopeFactory.CreateScope();
        var ctx = scope.ServiceProvider.GetRequiredService<MaimsDbContext>();
        var warehouses = await ctx.Warehouses.AsNoTracking().Where(w => w.IsActive).OrderBy(w => w.Name).ToListAsync();
        _cmbWarehouse.DataSource = warehouses.ToList();
        if (warehouses.Count > 0)
        {
            _cmbWarehouse.SelectedIndex = 0;
            await LoadSheetAsync();
        }
    }

    private async Task LoadSheetAsync()
    {
        if (_cmbWarehouse.SelectedValue is not long whId) return;
        try
        {
            _statusLabel.Text = "Loading…";
            using var scope = _scopeFactory.CreateScope();
            var invSvc = scope.ServiceProvider.GetRequiredService<IInventoryService>();
            var balances = await invSvc.GetBalancesAsync(whId);

            var rows = balances.Select(b => new CycleCountRow
            {
                ItemId = b.ItemId,
                Sku = b.Sku,
                ItemName = b.ItemName,
                BinLocation = b.BinLocation ?? "",
                BookQty = b.QtyOnHand,
                CountedQty = b.QtyOnHand,  // default: same as book
                Variance = 0,
                Uom = ""  // UoM not in StockBalanceReadDto; left blank
            }).ToList();

            _grid.DataSource = rows;
            _statusLabel.Text = $"{rows.Count} items loaded. Enter counted quantities, then Submit.";
        }
        catch (Exception ex)
        {
            MessageBox.Show("Failed to load count sheet: " + ex.Message, "Error",
                MessageBoxButtons.OK, MessageBoxIcon.Error);
        }
    }

    private void RecomputeVariance(int rowIndex)
    {
        if (rowIndex < 0 || rowIndex >= _grid.Rows.Count) return;
        var row = _grid.Rows[rowIndex];
        if (row.DataBoundItem is not CycleCountRow cc) return;

        // Read the CountedQty from the grid (user may have just edited it)
        if (decimal.TryParse(row.Cells["CountedQty"].Value?.ToString(), out var counted))
            cc.CountedQty = counted;
        cc.Variance = cc.CountedQty - cc.BookQty;

        // Refresh Variance cell
        row.Cells["Variance"].Value = cc.Variance;

        // Color: red if non-zero variance
        row.Cells["Variance"].Style.ForeColor = cc.Variance != 0 ? MaimsTheme.Critical : MaimsTheme.OK;
        row.Cells["Variance"].Style.Font = new Font(MaimsTheme.Body, cc.Variance != 0 ? FontStyle.Bold : FontStyle.Regular);
    }

    private async Task SubmitAsync()
    {
        if (_grid.DataSource is not List<CycleCountRow> rows || rows.Count == 0)
        {
            MessageBox.Show("No items to submit. Click 'Load Sheet' first.", "No data",
                MessageBoxButtons.OK, MessageBoxIcon.Information);
            return;
        }

        var discrepancies = rows.Where(r => r.Variance != 0).ToList();
        if (discrepancies.Count == 0)
        {
            MessageBox.Show("All counted quantities match book stock. No adjustments needed.",
                "No discrepancies", MessageBoxButtons.OK, MessageBoxIcon.Information);
            return;
        }

        // Re-compute variance from grid (in case user edited but didn't commit)
        foreach (DataGridViewRow row in _grid.Rows)
        {
            if (row.DataBoundItem is CycleCountRow cc)
                RecomputeVariance(row.Index);
        }
        discrepancies = rows.Where(r => r.Variance != 0).ToList();

        var confirm = MessageBox.Show(
            $"Found {discrepancies.Count} discrepancies:\n\n" +
            string.Join("\n", discrepancies.Take(5).Select(d => $"  {d.Sku}: book={d.BookQty:N3}, counted={d.CountedQty:N3}, var={d.Variance:N3}")) +
            (discrepancies.Count > 5 ? $"\n  ...and {discrepancies.Count - 5} more" : "") +
            "\n\nSubmit adjustments with reason COUNT_ADJUSTMENT?",
            "Confirm Cycle Count Adjustments",
            MessageBoxButtons.YesNo,
            MessageBoxIcon.Warning,
            MessageBoxDefaultButton.Button2);
        if (confirm != DialogResult.Yes) return;

        try
        {
            _btnSubmit.Enabled = false;
            _statusLabel.Text = "Submitting adjustments…";

            using var scope = _scopeFactory.CreateScope();
            var invSvc = scope.ServiceProvider.GetRequiredService<IInventoryService>();

            int successCount = 0;
            var errors = new List<string>();

            foreach (var d in discrepancies)
            {
                try
                {
                    var dto = new StockAdjustmentDto(
                        ItemId: d.ItemId,
                        WarehouseId: (long)_cmbWarehouse.SelectedValue!,
                        NewQuantity: d.CountedQty,
                        ReasonCode: StockReasonCodes.CountAdjustment,
                        ReferenceDocNo: $"CYCLE-{DateTime.Now:yyyyMMdd}",
                        Notes: $"Cycle count. Book={d.BookQty:N3}, Counted={d.CountedQty:N3}, Variance={d.Variance:N3}");
                    await invSvc.AdjustAsync(dto);
                    successCount++;
                }
                catch (Exception ex)
                {
                    errors.Add($"{d.Sku}: {ex.Message}");
                }
            }

            var msg = $"Submitted {successCount} of {discrepancies.Count} adjustments.";
            if (errors.Count > 0)
                msg += $"\n\nErrors:\n  - {string.Join("\n  - ", errors.Take(5))}";

            MessageBox.Show(msg, "Cycle Count Complete",
                MessageBoxButtons.OK,
                errors.Count > 0 ? MessageBoxIcon.Warning : MessageBoxIcon.Information);

            _statusLabel.Text = $"✓ {successCount} adjustments submitted.";
            _statusLabel.ForeColor = errors.Count > 0 ? MaimsTheme.Warning : MaimsTheme.OK;

            await LoadSheetAsync();
        }
        catch (Exception ex)
        {
            MessageBox.Show("Submit failed: " + ex.Message, "Error",
                MessageBoxButtons.OK, MessageBoxIcon.Error);
        }
        finally
        {
            _btnSubmit.Enabled = true;
        }
    }

    private sealed class CycleCountRow
    {
        public long ItemId { get; set; }
        public string Sku { get; set; } = "";
        public string ItemName { get; set; } = "";
        public string BinLocation { get; set; } = "";
        public decimal BookQty { get; set; }
        public decimal CountedQty { get; set; }
        public decimal Variance { get; set; }
        public string Uom { get; set; } = "";
    }
}
