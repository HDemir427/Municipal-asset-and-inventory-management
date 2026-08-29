using MAIMS.Core.Entities;
using MAIMS.Core.Enums;
using MAIMS.Data;
using MAIMS.WinUI.Theming;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;

namespace MAIMS.WinUI.Forms;

/// <summary>
/// Asset inspection form. Records a condition inspection for an asset:
/// new condition rating (1-5), inspector notes, and optional cost.
/// Writes an AssetLifecycleEvent of type Inspection.
/// </summary>
public class AssetInspectionForm : Form
{
    private readonly IServiceScopeFactory _scopeFactory;
    private readonly ComboBox _cmbAsset;
    private readonly ComboBox _cmbCondition;
    private readonly TextBox _txtInspector;
    private readonly TextBox _txtCost;
    private readonly TextBox _txtNotes;
    private readonly Button _btnSave;
    private readonly Button _btnClose;
    private readonly Label _lblStatus;

    public AssetInspectionForm(IServiceScopeFactory scopeFactory)
    {
        _scopeFactory = scopeFactory;
        Text = "Asset Inspection";
        FormBorderStyle = FormBorderStyle.None;
        Dock = DockStyle.Fill;
        BackColor = MaimsTheme.Background;
        Font = MaimsTheme.Body;
        AutoScroll = true;

        var pnlForm = new Panel { Dock = DockStyle.Top, Height = 320, BackColor = MaimsTheme.Surface, Padding = new Padding(16) };

        int y = 16;
        AddLabel(pnlForm, "Asset *:", y);
        _cmbAsset = new ComboBox
        {
            Location = new Point(200, y),
            Size = new Size(330, 24),
            DropDownStyle = ComboBoxStyle.DropDownList,
            Font = MaimsTheme.Body
        };
        pnlForm.Controls.Add(_cmbAsset);
        y += 32;

        AddLabel(pnlForm, "New Condition *:", y);
        _cmbCondition = new ComboBox
        {
            Location = new Point(200, y),
            Size = new Size(330, 24),
            DropDownStyle = ComboBoxStyle.DropDownList,
            Font = MaimsTheme.Body
        };
        _cmbCondition.Items.AddRange(new object[]
        {
            "1 — Critical",
            "2 — Poor",
            "3 — Fair",
            "4 — Good",
            "5 — Excellent"
        });
        _cmbCondition.SelectedIndex = 2;  // Fair
        pnlForm.Controls.Add(_cmbCondition);
        y += 32;

        AddLabel(pnlForm, "Inspector (User ID):", y);
        _txtInspector = new TextBox
        {
            Location = new Point(200, y),
            Size = new Size(150, 24),
            Font = MaimsTheme.Body,
            Text = ""  // populated from session on Load
        };
        pnlForm.Controls.Add(_txtInspector);
        y += 32;

        AddLabel(pnlForm, "Inspection Cost (optional):", y);
        _txtCost = new TextBox
        {
            Location = new Point(200, y),
            Size = new Size(150, 24),
            Font = MaimsTheme.Body
        };
        pnlForm.Controls.Add(_txtCost);
        y += 32;

        AddLabel(pnlForm, "Notes:", y);
        _txtNotes = new TextBox
        {
            Location = new Point(200, y),
            Size = new Size(330, 80),
            Multiline = true,
            Font = MaimsTheme.Body
        };
        pnlForm.Controls.Add(_txtNotes);
        y += 90;

        _btnSave = MaimsTheme.CreateButton("Save", primary: true);
        _btnClose = MaimsTheme.CreateButton("Close");
        MaimsTheme.LayoutButtons(200, y, 8, _btnSave, _btnClose);
        _btnClose.Click += (_, _) =>
        {
            if (Parent is TabPage page && page.Parent is TabControl tc)
                tc.TabPages.Remove(page);
            else
                Close();
        };
        _btnSave.Click += async (_, _) => await SaveAsync();
        pnlForm.Controls.Add(_btnSave);
        pnlForm.Controls.Add(_btnClose);
        y += 40;

        _lblStatus = new Label
        {
            Location = new Point(16, y),
            Size = new Size(540, 24),
            Font = MaimsTheme.Small,
            ForeColor = MaimsTheme.TextSecondary
        };
        pnlForm.Controls.Add(_lblStatus);

        Controls.Add(pnlForm);
        Load += async (_, _) => await Task.WhenAll(LoadAssetsAsync(), PopulateInspectorAsync());
    }

    private void AddLabel(Control parent, string text, int y) =>
        parent.Controls.Add(new Label
        {
            Text = text,
            Location = new Point(16, y + 3),
            Size = new Size(180, 20),
            Font = MaimsTheme.Body
        });

    private async Task PopulateInspectorAsync()
    {
        try
        {
            using var scope = _scopeFactory.CreateScope();
            var session = scope.ServiceProvider.GetRequiredService<MAIMS.Core.Abstractions.ICurrentSession>();
            if (session.UserId.HasValue)
                _txtInspector.Text = session.UserId.Value.ToString();
        }
        catch { }
    }

    private async Task LoadAssetsAsync()
    {
        try
        {
            using var scope = _scopeFactory.CreateScope();
            var ctx = scope.ServiceProvider.GetRequiredService<MaimsDbContext>();

            // Load only assets that are In Service or Under Maintenance
            var assets = await ctx.Assets.AsNoTracking()
                .Where(a => a.Status == AssetStatus.InService || a.Status == AssetStatus.UnderMaintenance)
                .OrderBy(a => a.AssetCode)
                .Select(a => new { a.Id, Display = $"{a.AssetCode} — {a.Name}" })
                .ToListAsync();

            _cmbAsset.DisplayMember = "Display";
            _cmbAsset.ValueMember = "Id";
            _cmbAsset.DataSource = assets;

            if (assets.Count == 0)
                _lblStatus.Text = "No assets available for inspection (only In Service / Under Maintenance).";
        }
        catch (Exception ex)
        {
            MessageBox.Show("Failed to load assets: " + ex.Message, "Error",
                MessageBoxButtons.OK, MessageBoxIcon.Error);
        }
    }

    private async Task SaveAsync()
    {
        if (_cmbAsset.SelectedValue is not long assetId || assetId <= 0)
        {
            MessageBox.Show("Please select an asset.", "Validation",
                MessageBoxButtons.OK, MessageBoxIcon.Warning);
            return;
        }

        // Parse condition from "N — Label" format
        var conditionText = _cmbCondition.SelectedItem?.ToString() ?? "";
        if (!int.TryParse(conditionText.Split('—')[0].Trim(), out var conditionValue) ||
            conditionValue < 1 || conditionValue > 5)
        {
            MessageBox.Show("Invalid condition rating.", "Validation",
                MessageBoxButtons.OK, MessageBoxIcon.Warning);
            return;
        }

        long? inspectorId = long.TryParse(_txtInspector.Text, out var insp) && insp > 0 ? insp : null;
        decimal? cost = decimal.TryParse(_txtCost.Text, out var c) ? c : null;
        if (cost.HasValue && cost < 0)
        {
            MessageBox.Show("Inspection cost cannot be negative.", "Validation",
                MessageBoxButtons.OK, MessageBoxIcon.Warning);
            return;
        }

        try
        {
            _btnSave.Enabled = false;
            _lblStatus.Text = "Saving inspection…";

            using var scope = _scopeFactory.CreateScope();
            var ctx = scope.ServiceProvider.GetRequiredService<MaimsDbContext>();

            var asset = await ctx.Assets.FirstOrDefaultAsync(a => a.Id == assetId);
            if (asset is null)
            {
                MessageBox.Show("Asset not found.", "Error",
                    MessageBoxButtons.OK, MessageBoxIcon.Error);
                return;
            }

            var oldCondition = asset.ConditionRating;
            var newCondition = (ConditionRating)conditionValue;

            // Update asset condition
            asset.ConditionRating = newCondition;

            // Write lifecycle event
            ctx.AssetLifecycleEvents.Add(new AssetLifecycleEvent
            {
                AssetId = assetId,
                EventType = AssetEventType.Inspection,
                EventDate = DateTime.UtcNow,
                PerformedBy = inspectorId,
                FromStatus = asset.Status,
                ToStatus = asset.Status,
                Cost = cost,
                Notes = $"Inspection: condition {oldCondition} → {newCondition}. " +
                        (string.IsNullOrWhiteSpace(_txtNotes.Text) ? "" : _txtNotes.Text)
            });

            await ctx.SaveChangesAsync();

            _lblStatus.Text = $"✓ Inspection recorded. Condition: {oldCondition} → {newCondition}.";
            _lblStatus.ForeColor = MaimsTheme.OK;

            // Reset for next inspection
            _txtCost.Text = "";
            _txtNotes.Text = "";
            await LoadAssetsAsync();
        }
        catch (Exception ex)
        {
            MessageBox.Show("Save failed: " + ex.Message, "Error",
                MessageBoxButtons.OK, MessageBoxIcon.Error);
        }
        finally
        {
            _btnSave.Enabled = true;
        }
    }
}
