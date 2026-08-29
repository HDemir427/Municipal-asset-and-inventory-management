using MAIMS.Data;
using MAIMS.WinUI.Theming;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;

namespace MAIMS.WinUI.Forms;

/// <summary>
/// Asset Depreciation report. Shows per-asset depreciation calculation:
///   - Acquisition cost
///   - Current book value (stored)
///   - Accumulated depreciation (cost - book value)
///   - Years in service (from acquisition date to now)
///   - Annual depreciation (accumulated / years)
///   - Depreciation % (accumulated / cost * 100)
///
/// Only shows assets that are NOT Disposed / Written Off (those are fully
/// depreciated by definition).
/// </summary>
public class AssetDepreciationReportForm : Form
{
    private readonly IServiceScopeFactory _scopeFactory;
    private readonly DataGridView _grid;
    private readonly Button _btnRefresh;
    private readonly Button _btnExportCsv;
    private readonly Button _btnClose;
    private readonly Label _statusLabel;

    public AssetDepreciationReportForm(IServiceScopeFactory scopeFactory)
    {
        _scopeFactory = scopeFactory;
        Text = "Asset Depreciation Report";
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

        AddCol("AssetCode", "Asset Code", 120);
        AddCol("AssetName", "Name", 180);
        AddCol("CategoryName", "Category", 130);
        AddCol("DepartmentName", "Department", 130);
        AddCol("AcquisitionCost", "Acquisition Cost", 140, "N2");
        AddCol("CurrentBookValue", "Current Book Value", 140, "N2");
        AddCol("AccumulatedDepreciation", "Accumulated Depreciation", 160, "N2");
        AddCol("YearsInService", "Years in Service", 120, "F1");
        AddCol("AnnualDepreciation", "Annual Depreciation", 140, "N2");
        AddCol("DepreciationPct", "Depreciation %", 120, "F1");
        AddCol("UsefulLifeYears", "Useful Life (yrs)", 110);
        AddCol("RemainingLife", "Remaining Life (yrs)", 120);

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

            // Load assets with category + department, excluding disposed/written-off
            var assets = await ctx.Assets.AsNoTracking()
                .Include(a => a.Category)
                .Include(a => a.Department)
                .Where(a => a.Status != MAIMS.Core.Enums.AssetStatus.Disposed
                         && a.Status != MAIMS.Core.Enums.AssetStatus.WrittenOff)
                .Where(a => a.AcquisitionCost.HasValue && a.AcquisitionCost > 0)
                .OrderBy(a => a.AssetCode)
                .ToListAsync();

            var now = DateTime.UtcNow;
            var rows = assets.Select(a =>
            {
                var cost = a.AcquisitionCost ?? 0;
                var bookValue = a.CurrentBookValue ?? 0;
                var accumulated = cost - bookValue;
                var years = a.AcquisitionDate.HasValue
                    ? Math.Max((now - a.AcquisitionDate.Value).TotalDays / 365.25, 0.01)
                    : 0;
                var annual = years > 0 ? accumulated / (decimal)years : 0;
                var pct = cost > 0 ? accumulated / cost * 100 : 0;
                var usefulLife = a.Category?.UsefulLifeYears;
                var remaining = usefulLife.HasValue ? Math.Max(usefulLife.Value - (int)years, 0) : (int?)null;

                return new
                {
                    a.AssetCode,
                    AssetName = a.Name,
                    CategoryName = a.Category?.Name ?? "",
                    DepartmentName = a.Department?.Name ?? "",
                    AcquisitionCost = cost,
                    CurrentBookValue = bookValue,
                    AccumulatedDepreciation = accumulated,
                    YearsInService = years,
                    AnnualDepreciation = annual,
                    DepreciationPct = pct,
                    UsefulLifeYears = usefulLife?.ToString() ?? "—",
                    RemainingLife = remaining?.ToString() ?? "—"
                };
            }).ToList();

            _grid.DataSource = rows;
            var totalCost = rows.Sum(r => r.AcquisitionCost);
            var totalBook = rows.Sum(r => r.CurrentBookValue);
            var totalDepreciation = rows.Sum(r => r.AccumulatedDepreciation);
            _statusLabel.Text = $"{rows.Count} assets. Total cost: {totalCost:N2}, Total book value: {totalBook:N2}, Total depreciation: {totalDepreciation:N2}.";
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
                FileName = $"asset_depreciation_{DateTime.Now:yyyyMMdd_HHmmss}.csv"
            };
            if (sfd.ShowDialog() != DialogResult.OK) return;

            using var scope = _scopeFactory.CreateScope();
            var ctx = scope.ServiceProvider.GetRequiredService<MaimsDbContext>();

            var assets = await ctx.Assets.AsNoTracking()
                .Include(a => a.Category)
                .Include(a => a.Department)
                .Where(a => a.Status != MAIMS.Core.Enums.AssetStatus.Disposed
                         && a.Status != MAIMS.Core.Enums.AssetStatus.WrittenOff)
                .Where(a => a.AcquisitionCost.HasValue && a.AcquisitionCost > 0)
                .OrderBy(a => a.AssetCode)
                .ToListAsync();

            var now = DateTime.UtcNow;
            var sb = new System.Text.StringBuilder();
            sb.AppendLine("AssetCode,Name,Category,Department,AcquisitionCost,CurrentBookValue,AccumulatedDepreciation,YearsInService,AnnualDepreciation,DepreciationPct,UsefulLifeYears,RemainingLifeYears");

            foreach (var a in assets)
            {
                var cost = a.AcquisitionCost ?? 0;
                var bookValue = a.CurrentBookValue ?? 0;
                var accumulated = cost - bookValue;
                var years = a.AcquisitionDate.HasValue
                    ? Math.Max((now - a.AcquisitionDate.Value).TotalDays / 365.25, 0.01)
                    : 0;
                var annual = years > 0 ? accumulated / (decimal)years : 0;
                var pct = cost > 0 ? accumulated / cost * 100 : 0;

                sb.AppendLine(string.Join(',',
                    a.AssetCode,
                    EscapeCsv(a.Name),
                    EscapeCsv(a.Category?.Name ?? ""),
                    EscapeCsv(a.Department?.Name ?? ""),
                    cost.ToString("F2"),
                    bookValue.ToString("F2"),
                    accumulated.ToString("F2"),
                    years.ToString("F1"),
                    annual.ToString("F2"),
                    pct.ToString("F1"),
                    a.Category?.UsefulLifeYears?.ToString() ?? "",
                    a.Category?.UsefulLifeYears.HasValue == true
                        ? Math.Max(a.Category.UsefulLifeYears.Value - (int)years, 0).ToString()
                        : ""));
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
