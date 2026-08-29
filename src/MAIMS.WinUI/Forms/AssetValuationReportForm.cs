using MAIMS.Data;
using MAIMS.WinUI.Theming;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;

namespace MAIMS.WinUI.Forms;

/// <summary>
/// Asset Valuation by Department report. Shows per-department:
/// asset count, total acquisition cost, total current book value, and
/// total depreciation (cost - book value).
/// </summary>
public class AssetValuationReportForm : Form
{
    private readonly IServiceScopeFactory _scopeFactory;
    private readonly DataGridView _grid;
    private readonly Button _btnRefresh;
    private readonly Button _btnExportCsv;
    private readonly Button _btnClose;
    private readonly Label _statusLabel;

    public AssetValuationReportForm(IServiceScopeFactory scopeFactory)
    {
        _scopeFactory = scopeFactory;
        Text = "Asset Valuation by Department";
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

        AddCol("DepartmentCode", "Dept Code", 100);
        AddCol("DepartmentName", "Department", 220);
        AddCol("AssetCount", "Asset Count", 100);
        AddCol("TotalCost", "Total Acquisition Cost", 200, "N2");
        AddCol("TotalBookValue", "Total Book Value", 200, "N2");
        AddCol("Depreciation", "Total Depreciation", 200, "N2");

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

            var rows = await ctx.Assets.AsNoTracking()
                .Where(a => a.Status != MAIMS.Core.Enums.AssetStatus.Disposed
                         && a.Status != MAIMS.Core.Enums.AssetStatus.WrittenOff)
                .Join(ctx.Departments.AsNoTracking(),
                    a => a.DepartmentId, d => d.Id,
                    (a, d) => new { a, d })
                .GroupBy(x => new { x.d.Id, x.d.Code, x.d.Name })
                .Select(g => new
                {
                    DepartmentCode = g.Key.Code,
                    DepartmentName = g.Key.Name,
                    AssetCount = g.Count(),
                    TotalCost = g.Sum(x => (decimal?)(x.a.AcquisitionCost ?? 0)) ?? 0,
                    TotalBookValue = g.Sum(x => (decimal?)(x.a.CurrentBookValue ?? 0)) ?? 0,
                    Depreciation = (g.Sum(x => (decimal?)(x.a.AcquisitionCost ?? 0)) ?? 0)
                                 - (g.Sum(x => (decimal?)(x.a.CurrentBookValue ?? 0)) ?? 0)
                })
                .OrderBy(r => r.DepartmentCode)
                .ToListAsync();

            _grid.DataSource = rows;
            _statusLabel.Text = $"{rows.Count} departments. Total assets: {rows.Sum(r => r.AssetCount)}. Total value: {rows.Sum(r => r.TotalBookValue):N2}.";
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
                FileName = $"asset_valuation_{DateTime.Now:yyyyMMdd_HHmmss}.csv"
            };
            if (sfd.ShowDialog() != DialogResult.OK) return;

            using var scope = _scopeFactory.CreateScope();
            var ctx = scope.ServiceProvider.GetRequiredService<MaimsDbContext>();

            var rows = await ctx.Assets.AsNoTracking()
                .Where(a => a.Status != MAIMS.Core.Enums.AssetStatus.Disposed
                         && a.Status != MAIMS.Core.Enums.AssetStatus.WrittenOff)
                .Join(ctx.Departments.AsNoTracking(),
                    a => a.DepartmentId, d => d.Id,
                    (a, d) => new { a, d })
                .GroupBy(x => new { x.d.Code, x.d.Name })
                .Select(g => new
                {
                    DepartmentCode = g.Key.Code,
                    DepartmentName = g.Key.Name,
                    AssetCount = g.Count(),
                    TotalCost = g.Sum(x => (decimal?)(x.a.AcquisitionCost ?? 0)) ?? 0,
                    TotalBookValue = g.Sum(x => (decimal?)(x.a.CurrentBookValue ?? 0)) ?? 0
                })
                .OrderBy(r => r.DepartmentCode)
                .ToListAsync();

            var sb = new System.Text.StringBuilder();
            sb.AppendLine("DepartmentCode,DepartmentName,AssetCount,TotalAcquisitionCost,TotalBookValue,Depreciation");
            foreach (var r in rows)
                sb.AppendLine($"{r.DepartmentCode},{EscapeCsv(r.DepartmentName)},{r.AssetCount},{r.TotalCost:F2},{r.TotalBookValue:F2},{(r.TotalCost - r.TotalBookValue):F2}");

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
