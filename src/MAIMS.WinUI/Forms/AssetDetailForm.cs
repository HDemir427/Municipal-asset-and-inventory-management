using MAIMS.Core.Enums;
using MAIMS.Data;
using MAIMS.WinUI.Theming;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;

namespace MAIMS.WinUI.Forms;

/// <summary>
/// Asset detail form. Shows full lifecycle history (Acquisition, StatusChange,
/// Transfer, Maintenance, Inspection, Disposal) for a single asset, plus
/// a list of attachments (purchase receipts, photos, manuals).
///
/// Read-only — to modify the asset, use AssetEditForm.
/// </summary>
public class AssetDetailForm : Form
{
    private readonly IServiceScopeFactory _scopeFactory;
    private readonly long _assetId;
    private readonly Label _lblHeader;
    private readonly DataGridView _gridLifecycle;
    private readonly DataGridView _gridAttachments;
    private readonly Button _btnRefresh;
    private readonly Button _btnClose;
    private readonly Label _statusLabel;
    private bool _isLoading;  // re-entrancy guard: Load + VisibleChanged can fire concurrently

    public AssetDetailForm(IServiceScopeFactory scopeFactory, long assetId)
    {
        _scopeFactory = scopeFactory;
        _assetId = assetId;
        Text = $"Asset Detail — #{assetId}";
        FormBorderStyle = FormBorderStyle.None;
        Dock = DockStyle.Fill;
        BackColor = MaimsTheme.Background;

        // Top header
        _lblHeader = new Label
        {
            Dock = DockStyle.Top,
            Height = 50,
            BackColor = MaimsTheme.Primary,
            ForeColor = Color.White,
            Font = MaimsTheme.Heading,
            TextAlign = ContentAlignment.MiddleLeft,
            Padding = new Padding(16, 0, 16, 0),
            Text = $"Loading asset #{assetId}…"
        };

        // Lifecycle grid
        var lblLifecycle = new Label
        {
            Dock = DockStyle.Top,
            Height = 24,
            Text = "  Lifecycle History",
            Font = MaimsTheme.Body,
            ForeColor = MaimsTheme.TextSecondary,
            BackColor = MaimsTheme.Surface
        };
        _gridLifecycle = new DataGridView
        {
            Dock = DockStyle.Top,
            Height = 200,
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
        AddCol(_gridLifecycle, "EventDate", "Date", 140);
        AddCol(_gridLifecycle, "EventType", "Type", 130);
        AddCol(_gridLifecycle, "FromStatus", "From", 110);
        AddCol(_gridLifecycle, "ToStatus", "To", 110);
        AddCol(_gridLifecycle, "PerformedBy", "Performed By", 110);
        AddCol(_gridLifecycle, "Cost", "Cost", 100, "N2");
        AddCol(_gridLifecycle, "Notes", "Notes", 300);

        // Attachments grid
        var lblAttachments = new Label
        {
            Dock = DockStyle.Top,
            Height = 24,
            Text = "  Attachments (purchase receipts, photos, manuals)",
            Font = MaimsTheme.Body,
            ForeColor = MaimsTheme.TextSecondary,
            BackColor = MaimsTheme.Surface
        };
        _gridAttachments = new DataGridView
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
        AddCol(_gridAttachments, "FileName", "File Name", 220);
        AddCol(_gridAttachments, "FileType", "Type", 110);
        AddCol(_gridAttachments, "FileSize", "Size (bytes)", 130, "N0");
        AddCol(_gridAttachments, "Description", "Description", 250);
        AddCol(_gridAttachments, "UploadedAt", "Uploaded At", 140);

        // Bottom toolbar — buttons in a FlowLayoutPanel so they auto-position
        // and NEVER overflow regardless of form width.
        var pnlBottom = new FlowLayoutPanel
        {
            Dock = DockStyle.Bottom,
            Height = 46,
            BackColor = MaimsTheme.Surface,
            FlowDirection = FlowDirection.LeftToRight,
            WrapContents = false,
            Padding = new Padding(8, 6, 8, 6)
        };
        _btnRefresh = MaimsTheme.CreateButton("Refresh", primary: true);
        _btnClose = MaimsTheme.CreateButton("Close");
        // Right margin on Refresh for spacing; Close (last) gets zero.
        // Top margin is 0 for both (set by CreateButton) so Y baselines align.
        _btnRefresh.Margin = new Padding(0, 0, 8, 0);
        _btnClose.Margin = new Padding(0, 0, 0, 0);
        _btnClose.Click += (_, _) =>
        {
            if (Parent is TabPage page && page.Parent is TabControl tc)
                tc.TabPages.Remove(page);
            else
                Close();
        };
        pnlBottom.Controls.Add(_btnRefresh);
        pnlBottom.Controls.Add(_btnClose);

        var pnlStatus = new Panel { Dock = DockStyle.Bottom, Height = 24, BackColor = MaimsTheme.Surface };
        _statusLabel = new Label
        {
            Dock = DockStyle.Fill,
            Font = MaimsTheme.Small,
            ForeColor = MaimsTheme.TextSecondary,
            Padding = new Padding(8, 4, 8, 4)
        };
        pnlStatus.Controls.Add(_statusLabel);

        // Layout — WinForms docks in REVERSE add order (last added = closest to edge).
        // We want: header at top, lifecycle grid below it, attachments grid below that,
        // status bar at very bottom, button panel just above status bar.
        // So add Fill first, then Top items, then Bottom items LAST.
        Controls.Add(_gridAttachments);     // Fill (added first → fills remaining space)
        Controls.Add(lblAttachments);        // Top
        Controls.Add(_gridLifecycle);        // Top
        Controls.Add(lblLifecycle);          // Top
        Controls.Add(_lblHeader);            // Top
        Controls.Add(pnlStatus);             // Bottom (added last → closest to bottom edge)
        Controls.Add(pnlBottom);             // Bottom (added second-to-last → just above status)

        _btnRefresh.Click += async (_, _) => await LoadDataAsync();
        Load += async (_, _) => await LoadDataAsync();

        // Auto-refresh when the user returns to this tab. The form is hosted inside
        // a TabPage; VisibleChanged fires whenever the tab is activated or deactivated.
        // This ensures newly-created lifecycle events (transfers, inspections, disposals)
        // performed in OTHER tabs show up immediately when the user comes back here,
        // without forcing them to click Refresh manually.
        // The _isLoading guard prevents a double-load on first show (Load + VisibleChanged
        // both fire when the form first appears).
        VisibleChanged += async (_, _) =>
        {
            if (Visible && !IsDisposed)
                await LoadDataAsync();
        };
    }

    private static void AddCol(DataGridView grid, string prop, string header, int width, string? format = null)
    {
        grid.Columns.Add(new DataGridViewTextBoxColumn
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
        // Re-entrancy guard: Load and VisibleChanged both fire on first show,
        // and Refresh can be clicked while a Load is in flight. Each LoadDataAsync
        // creates its own scope, but the DbContext pool may reuse a DbContext
        // before its previous operation's connection is fully released.
        if (_isLoading) return;
        _isLoading = true;
        try
        {
            _statusLabel.Text = "Loading…";
            using var scope = _scopeFactory.CreateScope();
            var ctx = scope.ServiceProvider.GetRequiredService<MaimsDbContext>();

            // Asset header
            var asset = await ctx.Assets.AsNoTracking()
                .Include(a => a.Category)
                .Include(a => a.Department)
                .Include(a => a.Custodian)
                .FirstOrDefaultAsync(a => a.Id == _assetId);

            if (asset is null)
            {
                _lblHeader.Text = $"Asset #{_assetId} not found.";
                _statusLabel.Text = "Asset not found.";
                return;
            }

            _lblHeader.Text = $"  {asset.AssetCode} — {asset.Name}  ({asset.Status}, {asset.ConditionRating})";

            // Lifecycle events
            // NOTE: DataGridView caches column metadata on the first DataSource assignment
            // for anonymous types. Subsequent assignments to a NEW list of the same anonymous
            // type do NOT refresh the rows — the grid keeps showing the original snapshot.
            // Fix: set DataSource = null first, then assign the new list. This forces the
            // grid to re-bind and re-render all rows on every Refresh.
            var events = await ctx.AssetLifecycleEvents.AsNoTracking()
                .Where(e => e.AssetId == _assetId)
                .OrderByDescending(e => e.EventDate)
                .Select(e => new
                {
                    EventDate = e.EventDate,
                    EventType = e.EventType.ToString(),
                    FromStatus = e.FromStatus != null ? e.FromStatus.ToString() : "",
                    ToStatus = e.ToStatus != null ? e.ToStatus.ToString() : "",
                    PerformedBy = e.PerformedBy != null ? e.PerformedBy.ToString() : "(system)",
                    Cost = e.Cost,
                    Notes = e.Notes ?? ""
                })
                .ToListAsync();
            _gridLifecycle.DataSource = null;
            _gridLifecycle.DataSource = events;

            // Attachments
            var attachments = await ctx.AssetAttachments.AsNoTracking()
                .Where(at => at.AssetId == _assetId)
                .OrderByDescending(at => at.CreatedAt)
                .Select(at => new
                {
                    FileName = at.OriginalFileName ?? at.FilePath,
                    FileType = at.FileType,
                    FileSize = at.FileSizeBytes,
                    Description = at.Description ?? "",
                    UploadedAt = at.CreatedAt
                })
                .ToListAsync();
            _gridAttachments.DataSource = null;
            _gridAttachments.DataSource = attachments;

            _statusLabel.Text = $"{events.Count} lifecycle events, {attachments.Count} attachments.";
        }
        catch (Exception ex)
        {
            MessageBox.Show("Failed to load asset detail: " + ex.Message, "Error",
                MessageBoxButtons.OK, MessageBoxIcon.Error);
            _statusLabel.Text = "Load failed.";
        }
        finally
        {
            _isLoading = false;
        }
    }
}
