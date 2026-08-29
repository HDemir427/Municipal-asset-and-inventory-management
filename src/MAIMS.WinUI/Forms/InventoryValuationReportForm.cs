using MAIMS.Data;
using MAIMS.WinUI.Theming;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;

namespace MAIMS.WinUI.Forms;

/// <summary>
/// Inventory Valuation report. Shows per-item:
/// SKU, name, unit cost, total on-hand across all warehouses,
/// and total value (unit_cost × total_on_hand).
/// </summary>
public class InventoryValuationReportForm : Form
{
    private readonly IServiceScopeFactory _scopeFactory;
    private readonly DataGridView _grid;
    private readonly Button _btnRefresh;
    private readonly Button _btnExportCsv;
    private readonly Button _btnClose;
    private readonly Label _statusLabel;

    public InventoryValuationReportForm(IServiceScopeFactory scopeFactory)
    {
        _scopeFactory = scopeFactory;
        Text = "Inventory Valuation Report";
        FormBorderStyle = FormBorderStyle.None;
        Dock = DockStyle.Fill;
        BackColor = MaimsTheme.Background;

        var pnlTop = new Panel { Dock = DockStyle.Top, Height = 50, BackColor = MaimsTheme.Surface, Padding = new Padding(8) };

        _btnRefresh = MaimsTheme.CreateButton("Refresh", primary: true);
        _btnExportCsv = MaimsTheme.CreateButton("Export CSV");
        _btnClose = MaimsTheme.CreateButton("Close");
        MaimsTheme.LayoutButtons(8, 10, 8, _btnRefresh, _btnExportCsv, _btnClose);
        _btnClose.Click += (_, _) =>
        {
            if (Parent is TabPage page && page.Parent is TabControl tc)
                tc.TabPages.Remove(page);
            else
                Close();
        };

        pnlTop.Controls.AddRange(new Control[] { _btnRefresh, _btnExportCsv, _btnClose });

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

        AddCol("Sku", "SKU", 120);
        AddCol("Name", "Item Name", 220);
        AddCol("Uom", "UoM", 60);
        AddCol("UnitCost", "Unit Cost", 110, "N2");
        AddCol("TotalOnHand", "Total On Hand", 130, "N3");
        AddCol("TotalValue", "Total Value", 140, "N2");

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
        _btnExportCsv.Click += async (_, _) => await ExportCsvAsync();
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
        try
        {
            _statusLabel.Text = "Loading…";
            using var scope = _scopeFactory.CreateScope();
            var ctx = scope.ServiceProvider.GetRequiredService<MaimsDbContext>();

            var rows = await ctx.Items.AsNoTracking()
                .GroupJoin(ctx.StockBalances.AsNoTracking(),
                    i => i.Id, sb => sb.ItemId,
                    (i, balances) => new { i, balances })
                .SelectMany(x => x.balances.DefaultIfEmpty(),
                    (x, sb) => new { x.i, sb })
                .GroupBy(x => new { x.i.Id, x.i.Sku, x.i.Name, x.i.Uom, x.i.UnitCost })
                .Select(g => new
                {
                    Sku = g.Key.Sku,
                    Name = g.Key.Name,
                    Uom = g.Key.Uom.ToString(),
                    UnitCost = (decimal?)(g.Key.UnitCost ?? 0),
                    TotalOnHand = g.Sum(x => (decimal?)(x.sb != null ? x.sb.QtyOnHand : 0)) ?? 0
                })
                .OrderBy(r => r.Sku)
                .ToListAsync();

            var view = rows
                .Select(r => new
                {
                    r.Sku,
                    r.Name,
                    r.Uom,
                    UnitCost = r.UnitCost ?? 0,
                    r.TotalOnHand,
                    TotalValue = (r.UnitCost ?? 0) * r.TotalOnHand
                })
                .ToList();

            _grid.DataSource = view;
            var totalValue = view.Sum(r => r.TotalValue);
            _statusLabel.Text = $"{view.Count} items. Total inventory value: {totalValue:N2}.";
        }
        catch (Exception ex)
        {
            MessageBox.Show("Failed to load report: " + ex.Message, "Error",
                MessageBoxButtons.OK, MessageBoxIcon.Error);
        }
    }

    private async Task ExportCsvAsync()
    {
        try
        {
            using var sfd = new SaveFileDialog
            {
                Filter = "CSV files (*.csv)|*.csv|All files (*.*)|*.*",
                FileName = $"inventory_valuation_{DateTime.Now:yyyyMMdd_HHmmss}.csv"
            };
            if (sfd.ShowDialog() != DialogResult.OK) return;

            using var scope = _scopeFactory.CreateScope();
            var ctx = scope.ServiceProvider.GetRequiredService<MaimsDbContext>();

            var rows = await ctx.Items.AsNoTracking()
                .GroupJoin(ctx.StockBalances.AsNoTracking(),
                    i => i.Id, sb => sb.ItemId,
                    (i, balances) => new { i, balances })
                .SelectMany(x => x.balances.DefaultIfEmpty(),
                    (x, sb) => new { x.i, sb })
                .GroupBy(x => new { x.i.Sku, x.i.Name, x.i.Uom, x.i.UnitCost })
                .Select(g => new
                {
                    Sku = g.Key.Sku,
                    Name = g.Key.Name,
                    Uom = g.Key.Uom.ToString(),
                    UnitCost = (decimal?)(g.Key.UnitCost ?? 0),
                    TotalOnHand = g.Sum(x => (decimal?)(x.sb != null ? x.sb.QtyOnHand : 0)) ?? 0
                })
                .OrderBy(r => r.Sku)
                .ToListAsync();

            var sb = new System.Text.StringBuilder();
            sb.AppendLine("SKU,Name,UoM,UnitCost,TotalOnHand,TotalValue");
            foreach (var r in rows)
            {
                var uc = r.UnitCost ?? 0;
                var value = uc * r.TotalOnHand;
                sb.AppendLine($"{r.Sku},{EscapeCsv(r.Name)},{r.Uom},{uc:F2},{r.TotalOnHand:F3},{value:F2}");
            }

            await File.WriteAllTextAsync(sfd.FileName, sb.ToString());
            MessageBox.Show($"Exported to: {sfd.FileName}", "Export complete",
                MessageBoxButtons.OK, MessageBoxIcon.Information);
        }
        catch (Exception ex)
        {
            MessageBox.Show("Export failed: " + ex.Message, "Error",
                MessageBoxButtons.OK, MessageBoxIcon.Error);
        }
    }

    private static string EscapeCsv(string s) =>
        s.Contains(',') || s.Contains('"') || s.Contains('\n')
            ? "\"" + s.Replace("\"", "\"\"") + "\""
            : s;
}
