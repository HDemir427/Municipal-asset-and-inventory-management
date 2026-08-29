using MAIMS.Core.Enums;
using MAIMS.Data;
using MAIMS.WinUI.Theming;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;

namespace MAIMS.WinUI.Forms;

/// <summary>
/// Shows recent asset lifecycle events of type Transfer. Used by department
/// heads to track asset movements between departments / locations / custodians.
///
/// Transfers are executed immediately (no pending/approval workflow) — this
/// form displays the transfer history so managers can review past movements.
/// Despite the form name, the grid shows ALL recent transfers, not just
/// pending ones (the approval workflow is a future enhancement).
/// </summary>
public class PendingTransfersForm : Form
{
    private readonly IServiceScopeFactory _scopeFactory;
    private readonly DataGridView _grid;
    private readonly Button _btnRefresh;
    private readonly Button _btnClose;
    private readonly Label _statusLabel;

    public PendingTransfersForm(IServiceScopeFactory scopeFactory)
    {
        _scopeFactory = scopeFactory;
        Text = "Assets — Recent Transfers";
        FormBorderStyle = FormBorderStyle.None;
        Dock = DockStyle.Fill;
        BackColor = MaimsTheme.Background;

        var pnlTop = new Panel { Dock = DockStyle.Top, Height = 50, BackColor = MaimsTheme.Surface, Padding = new Padding(8) };
        var lblTitle = new Label
        {
            Text = "Recent asset transfer events (newest first):",
            Location = new Point(8, 14),
            AutoSize = true,
            Font = MaimsTheme.Body,
            ForeColor = MaimsTheme.TextSecondary
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

        AddCol("EventDate", "Date", 140);
        AddCol("AssetCode", "Asset Code", 130);
        AddCol("AssetName", "Asset Name", 220);
        AddCol("FromStatus", "From", 100);
        AddCol("ToStatus", "To", 100);
        AddCol("PerformedBy", "Performed By", 150);
        AddCol("Notes", "Notes", 380);

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

    private void AddCol(string prop, string header, int width)
    {
        _grid.Columns.Add(new DataGridViewTextBoxColumn
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
        try
        {
            _statusLabel.Text = "Loading…";
            using var scope = _scopeFactory.CreateScope();
            var ctx = scope.ServiceProvider.GetRequiredService<MaimsDbContext>();

            var events = await ctx.AssetLifecycleEvents.AsNoTracking()
                .Where(e => e.EventType == AssetEventType.Transfer)
                .Include(e => e.Asset)
                .OrderByDescending(e => e.EventDate)
                .Take(200)
                .Select(e => new
                {
                    EventDate = e.EventDate,
                    AssetCode = e.Asset != null ? e.Asset.AssetCode : "",
                    AssetName = e.Asset != null ? e.Asset.Name : "",
                    FromStatus = e.FromStatus != null ? e.FromStatus.ToString() : "",
                    ToStatus = e.ToStatus != null ? e.ToStatus.ToString() : "",
                    PerformedBy = e.PerformedBy != null ? e.PerformedBy.ToString() : "(system)",
                    Notes = e.Notes ?? ""
                })
                .ToListAsync();

            _grid.DataSource = events;
            _statusLabel.Text = $"{events.Count} transfer event(s) found.";
        }
        catch (Exception ex)
        {
            MessageBox.Show("Failed to load transfers: " + ex.Message, "Error",
                MessageBoxButtons.OK, MessageBoxIcon.Error);
        }
    }
}
