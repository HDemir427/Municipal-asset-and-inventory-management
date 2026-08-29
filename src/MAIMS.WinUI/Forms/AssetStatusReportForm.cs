using MAIMS.Core.Enums;
using MAIMS.Data;
using MAIMS.WinUI.Theming;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;

namespace MAIMS.WinUI.Forms;

/// <summary>
/// Asset Status Distribution report. Shows the count of assets in each
/// status (Planned, Acquired, In Service, Under Maintenance, In Storage,
/// Disposed, Written Off) plus total acquisition cost per status.
/// </summary>
public class AssetStatusReportForm : Form
{
    private readonly IServiceScopeFactory _scopeFactory;
    private readonly DataGridView _grid;
    private readonly Button _btnRefresh;
    private readonly Button _btnExportCsv;
    private readonly Button _btnClose;
    private readonly Label _statusLabel;

    public AssetStatusReportForm(IServiceScopeFactory scopeFactory)
    {
        _scopeFactory = scopeFactory;
        Text = "Asset Status Distribution Report";
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

        AddCol("Status", "Status", 180);
        AddCol("Count", "Asset Count", 120);
        AddCol("TotalCost", "Total Acquisition Cost", 200, "N2");
        AddCol("TotalBookValue", "Total Book Value", 200, "N2");
        AddCol("Percentage", "% of Total", 120);

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

            var groups = await ctx.Assets.AsNoTracking()
                .GroupBy(a => a.Status)
                .Select(g => new
                {
                    Status = g.Key.ToString(),
                    Count = g.Count(),
                    TotalCost = g.Sum(a => (decimal?)(a.AcquisitionCost ?? 0)) ?? 0,
                    TotalBookValue = g.Sum(a => (decimal?)(a.CurrentBookValue ?? 0)) ?? 0
                })
                .ToListAsync();

            var totalCount = groups.Sum(g => g.Count);
            var rows = groups
                .OrderBy(g => g.Status)
                .Select(g => new
                {
                    g.Status,
                    g.Count,
                    g.TotalCost,
                    g.TotalBookValue,
                    Percentage = totalCount == 0 ? "0%" : $"{g.Count * 100.0 / totalCount:F1}%"
                })
                .ToList();

            _grid.DataSource = rows;
            _statusLabel.Text = $"{totalCount} assets across {rows.Count} statuses.";
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
                FileName = $"asset_status_report_{DateTime.Now:yyyyMMdd_HHmmss}.csv"
            };
            if (sfd.ShowDialog() != DialogResult.OK) return;

            using var scope = _scopeFactory.CreateScope();
            var ctx = scope.ServiceProvider.GetRequiredService<MaimsDbContext>();

            var groups = await ctx.Assets.AsNoTracking()
                .GroupBy(a => a.Status)
                .Select(g => new
                {
                    Status = g.Key.ToString(),
                    Count = g.Count(),
                    TotalCost = g.Sum(a => (decimal?)(a.AcquisitionCost ?? 0)) ?? 0,
                    TotalBookValue = g.Sum(a => (decimal?)(a.CurrentBookValue ?? 0)) ?? 0
                })
                .ToListAsync();

            var sb = new System.Text.StringBuilder();
            sb.AppendLine("Status,AssetCount,TotalAcquisitionCost,TotalBookValue");
            foreach (var g in groups.OrderBy(g => g.Status))
                sb.AppendLine($"{g.Status},{g.Count},{g.TotalCost:F2},{g.TotalBookValue:F2}");

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
}
