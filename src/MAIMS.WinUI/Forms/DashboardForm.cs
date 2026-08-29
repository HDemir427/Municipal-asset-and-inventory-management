using MAIMS.Core.Enums;
using MAIMS.Data;
using MAIMS.WinUI.Theming;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;

namespace MAIMS.WinUI.Forms;

/// <summary>
/// Dashboard — 9 key metrics in a 3×3 grid + three detail panels on the right
/// (Asset Status Breakdown + Asset Condition Distribution + Top 5 Low Stock Items)
/// + recent audit activity.
/// Shows only what a municipal asset manager needs at a glance.
/// </summary>
public class DashboardForm : Form
{
    private readonly IServiceScopeFactory _scopeFactory;
    private readonly Label _lblAssets, _lblInService, _lblNeedsInspection;
    private readonly Label _lblAcquisitionCost, _lblBookValue, _lblInvValue;
    private readonly Label _lblLowStock, _lblDepartments, _lblUsers;
    private readonly DataGridView _gridRecent;
    private readonly Button _btnRefresh, _btnClose;
    private readonly Label _statusLabel;
    private bool _isLoading;

    // Right-side detail panel labels (Asset Status Breakdown)
    private readonly Label _lblStatusPlanned, _lblStatusInService, _lblStatusMaintenance;
    private readonly Label _lblStatusDisposed, _lblStatusWrittenOff;

    // Right-side detail panel labels (Asset Condition Distribution)
    private readonly Label _lblCondCritical, _lblCondPoor, _lblCondFair, _lblCondGood, _lblCondExcellent;

    // Right-side detail panel (Top 5 Low Stock Items)
    private readonly Panel _pnlLowStockList;

    // The right-side container panel — holds the 3 detail panels stacked vertically.
    // Fixed optimal width (NOT stretched) so words don't get truncated and the
    // layout stays balanced next to the 3×3 grid.
    private readonly Panel _pnlRightSide;

    // Optimal fixed width for the right-side panels — wide enough for the longest
    // status name ("Maintenance" = 11 chars at 9pt Segoe UI). 360px gives an
    // extremely generous margin so the last letter is NEVER clipped, even under
    // high-DPI scaling (125%, 150%) or unusual font rendering on Windows.
    private const int RightPanelWidth = 360;

    public DashboardForm(IServiceScopeFactory scopeFactory)
    {
        _scopeFactory = scopeFactory;
        Text = "Dashboard";
        FormBorderStyle = FormBorderStyle.None;
        Dock = DockStyle.Fill;
        BackColor = MaimsTheme.Background;

        // Top toolbar
        var pnlTop = new Panel { Dock = DockStyle.Top, Height = 50, BackColor = MaimsTheme.Surface, Padding = new Padding(8) };
        var lblTitle = new Label
        {
            Text = "MAIMS Dashboard — Overview",
            Location = new Point(8, 14),
            AutoSize = true,
            Font = MaimsTheme.Heading,
            ForeColor = MaimsTheme.Primary
        };
        _btnRefresh = MaimsTheme.CreateButton("Refresh", primary: true);
        _btnClose = MaimsTheme.CreateButton("Close");
        MaimsTheme.LayoutButtons(700, 10, 8, _btnRefresh, _btnClose);
        _btnClose.Click += (_, _) =>
        {
            if (Parent is TabPage page && page.Parent is TabControl tc)
                tc.TabPages.Remove(page);
            else
                Close();
        };
        pnlTop.Controls.AddRange(new Control[] { lblTitle, _btnRefresh, _btnClose });

        // ── Metrics container: 3×3 grid (left) + 3 detail panels (right) ──
        // Layout: the 3×3 grid occupies x=16..708 (3 cols × 220 + 2 gaps × 16).
        // The right-side container starts at x=720 with a FIXED width of 260px.
        // It is NOT stretched — this prevents word truncation and excessive whitespace.
        var pnlMetrics = new Panel { Dock = DockStyle.Top, Height = 240, BackColor = MaimsTheme.Background, Padding = new Padding(16) };

        // ── LEFT: 3×3 metric grid ──
        // Row 1: Asset counts
        _lblAssets = CreateMetricCard(pnlMetrics, "Total Assets", "—", 0, 0, MaimsTheme.Primary);
        _lblInService = CreateMetricCard(pnlMetrics, "In Service", "—", 236, 0, MaimsTheme.OK);
        _lblNeedsInspection = CreateMetricCard(pnlMetrics, "Needing Inspection", "—", 472, 0, MaimsTheme.Warning);

        // Row 2: Financial values
        _lblAcquisitionCost = CreateMetricCard(pnlMetrics, "Total Acquisition Cost", "—", 0, 80, MaimsTheme.Primary);
        _lblBookValue = CreateMetricCard(pnlMetrics, "Current Book Value", "—", 236, 80, MaimsTheme.OK);
        _lblInvValue = CreateMetricCard(pnlMetrics, "Inventory Value", "—", 472, 80, MaimsTheme.Accent);

        // Row 3: Operational metrics
        _lblLowStock = CreateMetricCard(pnlMetrics, "Low Stock Alerts", "—", 0, 160, MaimsTheme.Warning);
        _lblDepartments = CreateMetricCard(pnlMetrics, "Departments", "—", 236, 160, MaimsTheme.Accent);
        _lblUsers = CreateMetricCard(pnlMetrics, "Active Users", "—", 472, 160, MaimsTheme.Primary);

        // ── RIGHT: 3 detail panels in a 2×2 grid layout ──
        //
        // Layout:
        //   ┌──────────────────┬──────────────────┐
        //   │ Asset Status     │                  │
        //   │ Breakdown        │  Top 5 Low Stock │
        //   │ (360 × 116)      │  Items           │
        //   ├──────────────────┤  (360 × 240)     │
        //   │ Asset Condition  │                  │
        //   │ Distribution     │                  │
        //   │ (360 × 116)      │                  │
        //   └──────────────────┴──────────────────┘
        //
        //   Left column: 2 panels stacked vertically (each 360×116, 8px gap)
        //   Right column: 1 panel (360×240 = sol sütun toplam yükseklik)
        //   Gap between columns: 8px (same as between the two left panels)
        //   All widths equal: 360px
        //
        // The third panel (Low Stock) is to the RIGHT of the other two,
        // not at the bottom. Its height equals the combined height of the
        // two left panels (116+8+116 = 240px).
        _pnlRightSide = new Panel
        {
            Location = new Point(720, 0),
            Size = new Size(RightPanelWidth * 2 + 8, 240),  // 2 cols × 360 + 8px gap
            BackColor = MaimsTheme.Background
        };
        pnlMetrics.Controls.Add(_pnlRightSide);

        // Layout constants — left column has 2 panels stacked vertically,
        // each 116px tall, with 8px gap between them.
        // Total left column height = 116 + 8 + 116 = 240px = right panel height.
        const int leftPanelHeight = 116;
        const int panelGap = 8;
        const int rightPanelX = RightPanelWidth + panelGap;  // x = 368
        const int rightPanelHeight = leftPanelHeight * 2 + panelGap;  // 240

        // ── Panel 1 (left-top): Asset Status Breakdown — 360×116 ──
        var pnlStatusBreakdown = new Panel
        {
            Location = new Point(0, 0),
            Size = new Size(RightPanelWidth, leftPanelHeight),
            BackColor = MaimsTheme.Surface,
            BorderStyle = BorderStyle.FixedSingle
        };
        var lblStatusHeader = new Label
        {
            Text = "Asset Status Breakdown",
            Location = new Point(8, 4),
            Size = new Size(RightPanelWidth - 16, 18),
            Font = MaimsTheme.Small,
            ForeColor = MaimsTheme.TextSecondary
        };
        pnlStatusBreakdown.Controls.Add(lblStatusHeader);
        // 2 columns × 3 rows. 116px panel → header 24px + 92px for 3 rows = ~30px/row.
        // Rows at y=28, 58, 88 (very comfortable spacing).
        // col1X=8, col2X=173 — 5px gap between col1 count (ends at 168) and col2 dot.
        // name label is 120px wide — fits "Maintenance" (11 chars at 9pt) with wide margin.
        const int col1X = 8;
        const int col2X = 173;
        const int row1Y = 28;
        const int row2Y = 58;
        const int row3Y = 88;
        _lblStatusPlanned = CreateStatusCell(pnlStatusBreakdown, "Planned", col1X, row1Y, Color.FromArgb(120, 120, 120));
        _lblStatusInService = CreateStatusCell(pnlStatusBreakdown, "In Service", col2X, row1Y, MaimsTheme.OK);
        _lblStatusMaintenance = CreateStatusCell(pnlStatusBreakdown, "Maintenance", col1X, row2Y, MaimsTheme.Warning);
        _lblStatusDisposed = CreateStatusCell(pnlStatusBreakdown, "Disposed", col2X, row2Y, MaimsTheme.Accent);
        _lblStatusWrittenOff = CreateStatusCell(pnlStatusBreakdown, "Written Off", col1X, row3Y, MaimsTheme.Critical);
        _pnlRightSide.Controls.Add(pnlStatusBreakdown);

        // ── Panel 2 (left-bottom): Asset Condition Distribution — 360×116 ──
        // Same size as panel 1. Stacked below panel 1 with 8px gap.
        var pnlCondition = new Panel
        {
            Location = new Point(0, leftPanelHeight + panelGap),  // y = 124
            Size = new Size(RightPanelWidth, leftPanelHeight),
            BackColor = MaimsTheme.Surface,
            BorderStyle = BorderStyle.FixedSingle
        };
        var lblCondHeader = new Label
        {
            Text = "Asset Condition Distribution",
            Location = new Point(8, 4),
            Size = new Size(RightPanelWidth - 16, 18),
            Font = MaimsTheme.Small,
            ForeColor = MaimsTheme.TextSecondary
        };
        pnlCondition.Controls.Add(lblCondHeader);
        _lblCondCritical = CreateStatusCell(pnlCondition, "Critical", col1X, row1Y, MaimsTheme.Critical);
        _lblCondPoor = CreateStatusCell(pnlCondition, "Poor", col2X, row1Y, MaimsTheme.Warning);
        _lblCondFair = CreateStatusCell(pnlCondition, "Fair", col1X, row2Y, MaimsTheme.TextSecondary);
        _lblCondGood = CreateStatusCell(pnlCondition, "Good", col2X, row2Y, MaimsTheme.OK);
        _lblCondExcellent = CreateStatusCell(pnlCondition, "Excellent", col1X, row3Y, Color.FromArgb(46, 125, 50));
        _pnlRightSide.Controls.Add(pnlCondition);

        // ── Panel 3 (right): Top 5 Low Stock Items — 360×240 ──
        // Positioned to the RIGHT of panels 1 and 2 (not below them).
        // Width = 360px (same as panels 1 and 2).
        // Height = 240px = combined height of the two left panels (116+8+116).
        var pnlLowStock = new Panel
        {
            Location = new Point(rightPanelX, 0),  // x = 368
            Size = new Size(RightPanelWidth, rightPanelHeight),  // 360 × 240
            BackColor = MaimsTheme.Surface,
            BorderStyle = BorderStyle.FixedSingle
        };
        var lblLowStockHeader = new Label
        {
            Text = "Top 5 Low Stock Items",
            Location = new Point(8, 4),
            Size = new Size(RightPanelWidth - 16, 18),
            Font = MaimsTheme.Small,
            ForeColor = MaimsTheme.TextSecondary
        };
        _pnlLowStockList = new Panel
        {
            Location = new Point(8, 28),
            Size = new Size(RightPanelWidth - 16, rightPanelHeight - 32),  // 208px tall content
            BackColor = MaimsTheme.Surface
        };
        pnlLowStock.Controls.Add(lblLowStockHeader);
        pnlLowStock.Controls.Add(_pnlLowStockList);
        _pnlRightSide.Controls.Add(pnlLowStock);

        // The right-side container is FIXED width — it does NOT stretch.
        // If the form is very narrow, hide it to avoid overlap with the 3×3 grid.
        // Container needs 2 × 360px + 8px gap = 728px.
        pnlMetrics.Resize += (_, _) =>
        {
            var rightX = 720;
            var neededWidth = RightPanelWidth * 2 + panelGap;  // 728px
            _pnlRightSide.Visible = pnlMetrics.Width > rightX + neededWidth + 16;
        };

        // Recent activity grid
        var lblRecent = new Label
        {
            Dock = DockStyle.Top,
            Height = 24,
            Text = "  Recent Audit Log Activity (last 10)",
            Font = MaimsTheme.Body,
            ForeColor = MaimsTheme.TextSecondary,
            BackColor = MaimsTheme.Surface
        };

        _gridRecent = new DataGridView
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
        AddCol("ChangedAt", "Date", 140);
        AddCol("EntityType", "Entity", 100);
        AddCol("EntityId", "ID", 60);
        AddCol("Action", "Action", 80);
        AddCol("MachineName", "Machine", 120);
        AddCol("BeforeJson", "Before", 200);
        AddCol("AfterJson", "After", 200);

        var pnlStatus = new Panel { Dock = DockStyle.Bottom, Height = 24, BackColor = MaimsTheme.Surface };
        _statusLabel = new Label
        {
            Dock = DockStyle.Fill,
            Font = MaimsTheme.Small,
            ForeColor = MaimsTheme.TextSecondary,
            Padding = new Padding(8, 4, 8, 4)
        };
        pnlStatus.Controls.Add(_statusLabel);

        // Assemble — Dock order: last added = closest to edge
        Controls.Add(_gridRecent);       // Fill
        Controls.Add(lblRecent);          // Top
        Controls.Add(pnlMetrics);         // Top
        Controls.Add(pnlTop);             // Top
        Controls.Add(pnlStatus);          // Bottom

        _btnRefresh.Click += async (_, _) => await LoadDataAsync();
        Load += async (_, _) => await LoadDataAsync();
    }

    private static Label CreateMetricCard(Control parent, string title, string value, int x, int y, Color accentColor)
    {
        var card = new Panel
        {
            Location = new Point(x, y),
            Size = new Size(220, 68),
            BackColor = MaimsTheme.Surface,
            BorderStyle = BorderStyle.FixedSingle
        };
        var strip = new Panel
        {
            Location = new Point(0, 0),
            Size = new Size(4, 68),
            BackColor = accentColor
        };
        var lblTitle = new Label
        {
            Text = title,
            Location = new Point(12, 6),
            Size = new Size(200, 18),
            Font = MaimsTheme.Small,
            ForeColor = MaimsTheme.TextSecondary
        };
        var lblValue = new Label
        {
            Text = value,
            Location = new Point(12, 26),
            Size = new Size(200, 36),
            Font = new Font("Segoe UI", 16F, FontStyle.Bold),
            ForeColor = accentColor,
            TextAlign = ContentAlignment.MiddleLeft
        };
        card.Controls.AddRange(new Control[] { strip, lblTitle, lblValue });
        parent.Controls.Add(card);
        return lblValue;
    }

    /// <summary>
    /// Creates a status cell: colored dot + status name + count.
    /// Used for Asset Status Breakdown and Asset Condition Distribution panels.
    /// 
    /// Layout (per cell):
    ///   [dot 8×8] [name label 120px] [count label 28px]
    /// 
    /// The name label is 120px wide — fits the longest status name
    /// ("Maintenance" = 11 chars at 9pt Segoe UI) with a wide safety margin,
    /// so words are NEVER truncated regardless of DPI scaling or font rendering.
    /// The count label is right-aligned so numbers visually align across cells.
    /// 
    /// Returns the count label so LoadDataAsync can update it.
    /// </summary>
    private static Label CreateStatusCell(Control parent, string statusName, int x, int y, Color dotColor)
    {
        var dot = new Panel
        {
            Location = new Point(x, y + 4),
            Size = new Size(8, 8),
            BackColor = dotColor
        };
        var lblName = new Label
        {
            Text = statusName,
            Location = new Point(x + 12, y),
            Size = new Size(120, 18),
            Font = MaimsTheme.Body,  // 9pt — same as metric card titles, readable
            ForeColor = MaimsTheme.TextPrimary
        };
        var lblCount = new Label
        {
            Text = "—",
            Location = new Point(x + 135, y),
            Size = new Size(28, 18),
            Font = new Font("Segoe UI", 9F, FontStyle.Bold),
            ForeColor = dotColor,
            TextAlign = ContentAlignment.MiddleRight
        };
        parent.Controls.AddRange(new Control[] { dot, lblName, lblCount });
        return lblCount;
    }

    private void AddCol(string prop, string header, int width)
    {
        _gridRecent.Columns.Add(new DataGridViewTextBoxColumn
        {
            DataPropertyName = prop,
            HeaderText = header,
            Name = prop,
            Width = width,
            DefaultCellStyle = new DataGridViewCellStyle()
        });
    }

    private async Task LoadDataAsync()
    {
        // Re-entrancy guard: prevent concurrent DB operations.
        if (_isLoading) return;
        _isLoading = true;
        try
        {
            _statusLabel.Text = "Loading dashboard…";
            using var scope = _scopeFactory.CreateScope();
            var ctx = scope.ServiceProvider.GetRequiredService<MaimsDbContext>();

            // Row 1: Asset counts
            var totalAssets = await ctx.Assets.AsNoTracking().CountAsync();
            _lblAssets.Text = totalAssets.ToString("N0");

            var inService = await ctx.Assets.AsNoTracking()
                .CountAsync(a => a.Status == AssetStatus.InService);
            _lblInService.Text = inService.ToString("N0");

            var needsInspection = await ctx.Assets.AsNoTracking()
                .CountAsync(a => a.ConditionRating == ConditionRating.Critical
                              || a.ConditionRating == ConditionRating.Poor);
            _lblNeedsInspection.Text = needsInspection.ToString("N0");
            _lblNeedsInspection.ForeColor = needsInspection > 0 ? MaimsTheme.Critical : MaimsTheme.Warning;

            // Row 2: Financial values
            var acqCost = await ctx.Assets.AsNoTracking()
                .Where(a => a.Status != AssetStatus.Disposed && a.Status != AssetStatus.WrittenOff)
                .SumAsync(a => (decimal?)(a.AcquisitionCost ?? 0)) ?? 0;
            _lblAcquisitionCost.Text = acqCost.ToString("N2");

            var bookValue = await ctx.Assets.AsNoTracking()
                .Where(a => a.Status != AssetStatus.Disposed && a.Status != AssetStatus.WrittenOff)
                .SumAsync(a => (decimal?)(a.CurrentBookValue ?? 0)) ?? 0;
            _lblBookValue.Text = bookValue.ToString("N2");

            var invValue = await ctx.Items.AsNoTracking()
                .GroupJoin(ctx.StockBalances.AsNoTracking(),
                    i => i.Id, sb => sb.ItemId,
                    (i, balances) => new { i, balances })
                .SelectMany(x => x.balances.DefaultIfEmpty(),
                    (x, sb) => new { x.i, sb })
                .SumAsync(x => (decimal?)(x.i.UnitCost ?? 0) * (x.sb != null ? x.sb.QtyOnHand : 0)) ?? 0;
            _lblInvValue.Text = invValue.ToString("N2");

            // Row 3: Operational metrics
            var lowStock = await ctx.StockBalances.AsNoTracking()
                .Join(ctx.Items.AsNoTracking(),
                    sb => sb.ItemId, i => i.Id,
                    (sb, i) => new { sb, i })
                .CountAsync(x => x.sb.QtyOnHand <= x.i.ReorderPoint);
            _lblLowStock.Text = lowStock.ToString("N0");
            _lblLowStock.ForeColor = lowStock > 0 ? MaimsTheme.Critical : MaimsTheme.Warning;

            var depts = await ctx.Departments.AsNoTracking().CountAsync();
            _lblDepartments.Text = depts.ToString("N0");

            var activeUsers = await ctx.Users.AsNoTracking()
                .CountAsync(u => u.Status == UserStatus.Active);
            _lblUsers.Text = activeUsers.ToString("N0");

            // ── Right panel 1: Asset Status Breakdown ──
            var statusCounts = await ctx.Assets.AsNoTracking()
                .GroupBy(a => a.Status)
                .Select(g => new { Status = g.Key, Count = g.Count() })
                .ToListAsync();
            var statusMap = statusCounts.ToDictionary(x => x.Status, x => x.Count);
            _lblStatusPlanned.Text = (statusMap.TryGetValue(AssetStatus.Planned, out var p) ? p : 0).ToString("N0");
            _lblStatusInService.Text = (statusMap.TryGetValue(AssetStatus.InService, out var ins) ? ins : 0).ToString("N0");
            _lblStatusMaintenance.Text = (statusMap.TryGetValue(AssetStatus.UnderMaintenance, out var m) ? m : 0).ToString("N0");
            _lblStatusDisposed.Text = (statusMap.TryGetValue(AssetStatus.Disposed, out var d) ? d : 0).ToString("N0");
            _lblStatusWrittenOff.Text = (statusMap.TryGetValue(AssetStatus.WrittenOff, out var w) ? w : 0).ToString("N0");

            // ── Right panel 2: Asset Condition Distribution ──
            var condCounts = await ctx.Assets.AsNoTracking()
                .GroupBy(a => a.ConditionRating)
                .Select(g => new { Cond = g.Key, Count = g.Count() })
                .ToListAsync();
            var condMap = condCounts.ToDictionary(x => x.Cond, x => x.Count);
            _lblCondCritical.Text = (condMap.TryGetValue(ConditionRating.Critical, out var c) ? c : 0).ToString("N0");
            _lblCondPoor.Text = (condMap.TryGetValue(ConditionRating.Poor, out var po) ? po : 0).ToString("N0");
            _lblCondFair.Text = (condMap.TryGetValue(ConditionRating.Fair, out var f) ? f : 0).ToString("N0");
            _lblCondGood.Text = (condMap.TryGetValue(ConditionRating.Good, out var g2) ? g2 : 0).ToString("N0");
            _lblCondExcellent.Text = (condMap.TryGetValue(ConditionRating.Excellent, out var ex) ? ex : 0).ToString("N0");

            // ── Right panel 3: Top 5 Low Stock Items ──
            var topLowStock = await ctx.StockBalances.AsNoTracking()
                .Join(ctx.Items.AsNoTracking(),
                    sb => sb.ItemId, i => i.Id,
                    (sb, i) => new { sb, i })
                .Where(x => x.sb.QtyOnHand <= x.i.ReorderPoint && x.i.ReorderPoint > 0)
                .OrderBy(x => x.sb.QtyOnHand - x.i.ReorderPoint)
                .Take(5)
                .Select(x => new { x.i.Sku, x.i.Name, OnHand = x.sb.QtyOnHand, ReorderPt = x.i.ReorderPoint })
                .ToListAsync();

            _pnlLowStockList.Controls.Clear();
            if (topLowStock.Count == 0)
            {
                var empty = new Label
                {
                    Text = "✓ All items above reorder point",
                    Location = new Point(0, 0),
                    Size = new Size(_pnlLowStockList.Width, _pnlLowStockList.Height),
                    Font = MaimsTheme.Body,  // 9pt — readable
                    ForeColor = MaimsTheme.OK,
                    TextAlign = ContentAlignment.MiddleLeft
                };
                _pnlLowStockList.Controls.Add(empty);
            }
            else
            {
                // Panel content area is now 208px tall (right panel 240 - header 32).
                // 5 items fit comfortably at 20px row height — readable, not cramped.
                const int rowHeight = 20;
                int ly = 0;
                foreach (var item in topLowStock)
                {
                    var lbl = new Label
                    {
                        Text = $"• {item.Sku} — {TruncateItemName(item.Name)} ({item.OnHand:N0}/{item.ReorderPt:N0})",
                        Location = new Point(0, ly),
                        Size = new Size(_pnlLowStockList.Width, rowHeight),
                        Font = MaimsTheme.Body,  // 9pt — same as status cell names
                        ForeColor = MaimsTheme.Critical,
                        TextAlign = ContentAlignment.MiddleLeft
                    };
                    _pnlLowStockList.Controls.Add(lbl);
                    ly += rowHeight;
                    if (ly >= _pnlLowStockList.Height) break;  // safety: don't overflow
                }
            }

            // Recent audit log (last 10)
            var recent = await ctx.AuditLogs.AsNoTracking()
                .OrderByDescending(a => a.ChangedAt)
                .Take(10)
                .Select(a => new
                {
                    a.ChangedAt,
                    a.EntityType,
                    a.EntityId,
                    a.Action,
                    a.MachineName,
                    BeforeJson = TruncateJson(a.BeforeJson),
                    AfterJson = TruncateJson(a.AfterJson)
                })
                .ToListAsync();
            _gridRecent.DataSource = null;
            _gridRecent.DataSource = recent;

            var totalValue = bookValue + invValue;
            _statusLabel.Text = $"Refreshed at {DateTime.Now:HH:mm:ss} — " +
                $"{totalAssets} assets ({inService} in service, {needsInspection} need inspection), " +
                $"total value {totalValue:N2}.";
        }
        catch (Exception ex)
        {
            _statusLabel.Text = "Failed to load: " + ex.Message;
            _statusLabel.ForeColor = MaimsTheme.Critical;
        }
        finally
        {
            _isLoading = false;
        }
    }

    private static string TruncateItemName(string name)
    {
        if (string.IsNullOrEmpty(name)) return "";
        return name.Length > 20 ? name.Substring(0, 20) + "…" : name;
    }

    private static string TruncateJson(string? json)
    {
        if (string.IsNullOrWhiteSpace(json)) return "";
        return json.Length > 100 ? json.Substring(0, 100) + "…" : json;
    }
}
