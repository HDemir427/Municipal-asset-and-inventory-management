using MAIMS.Core.DTOs;
using MAIMS.Core.Enums;
using MAIMS.Core.Interfaces;
using MAIMS.WinUI.Theming;
using Microsoft.Extensions.DependencyInjection;

namespace MAIMS.WinUI.Forms;

/// <summary>
/// Asset disposal form. Marks an In-Service asset as Disposed, records the
/// disposal method (Sale / Donation / Scrap / Trade-In / Loss), proceeds,
/// and an approver (separation of duties: disposer ≠ approver).
/// </summary>
public class AssetDisposalForm : Form
{
    private readonly IAssetService _assetService;
    private readonly IReferenceDataService _refService;
    private readonly IServiceScopeFactory _scopeFactory;
    private readonly ComboBox _cmbAsset;
    private readonly ComboBox _cmbMethod;
    private readonly TextBox _txtProceeds;
    private readonly DateTimePicker _dtpDisposalDate;
    private readonly TextBox _txtApprover;
    private readonly TextBox _txtNotes;
    private readonly Button _btnDispose;
    private readonly Button _btnClose;
    private readonly Label _lblStatus;

    public AssetDisposalForm(IAssetService assetService, IReferenceDataService refService, IServiceScopeFactory scopeFactory)
    {
        _assetService = assetService;
        _refService = refService;
        _scopeFactory = scopeFactory;
        Text = "Dispose Asset";
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

        AddLabel(pnlForm, "Disposal Method *:", y);
        _cmbMethod = new ComboBox
        {
            Location = new Point(200, y),
            Size = new Size(330, 24),
            DropDownStyle = ComboBoxStyle.DropDownList,
            Font = MaimsTheme.Body
        };
        _cmbMethod.Items.AddRange(Enum.GetValues(typeof(DisposalMethod)).Cast<object>().ToArray());
        _cmbMethod.SelectedIndex = 0;
        pnlForm.Controls.Add(_cmbMethod);
        y += 32;

        AddLabel(pnlForm, "Disposal Date *:", y);
        _dtpDisposalDate = new DateTimePicker
        {
            Location = new Point(200, y),
            Size = new Size(200, 24),
            Format = DateTimePickerFormat.Short,
            Font = MaimsTheme.Body,
            Value = DateTime.Today
        };
        pnlForm.Controls.Add(_dtpDisposalDate);
        y += 32;

        AddLabel(pnlForm, "Proceeds (optional):", y);
        _txtProceeds = new TextBox
        {
            Location = new Point(200, y),
            Size = new Size(150, 24),
            Font = MaimsTheme.Body
        };
        pnlForm.Controls.Add(_txtProceeds);
        y += 32;

        AddLabel(pnlForm, "Approved By User ID *:", y);
        _txtApprover = new TextBox
        {
            Location = new Point(200, y),
            Size = new Size(150, 24),
            Font = MaimsTheme.Body,
            Text = ""  // populated from session on Load
        };
        pnlForm.Controls.Add(_txtApprover);
        y += 32;

        AddLabel(pnlForm, "Notes:", y);
        _txtNotes = new TextBox
        {
            Location = new Point(200, y),
            Size = new Size(330, 60),
            Multiline = true,
            Font = MaimsTheme.Body
        };
        pnlForm.Controls.Add(_txtNotes);
        y += 70;

        _btnDispose = MaimsTheme.CreateButton("Dispose Asset", primary: true);
        _btnClose = MaimsTheme.CreateButton("Close");
        MaimsTheme.LayoutButtons(200, y, 8, _btnDispose, _btnClose);
        _btnClose.Click += (_, _) =>
        {
            if (Parent is TabPage page && page.Parent is TabControl tc)
                tc.TabPages.Remove(page);
            else
                Close();
        };
        _btnDispose.Click += async (_, _) => await DisposeAsync();
        pnlForm.Controls.Add(_btnDispose);
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
        Load += async (_, _) => await Task.WhenAll(LoadAssetsAsync(), PopulateApproverAsync());
    }

    private void AddLabel(Control parent, string text, int y) =>
        parent.Controls.Add(new Label
        {
            Text = text,
            Location = new Point(16, y + 3),
            Size = new Size(180, 20),
            Font = MaimsTheme.Body
        });

    private async Task PopulateApproverAsync()
    {
        try
        {
            using var scope = _scopeFactory.CreateScope();
            var session = scope.ServiceProvider.GetRequiredService<MAIMS.Core.Abstractions.ICurrentSession>();
            if (session.UserId.HasValue)
                _txtApprover.Text = session.UserId.Value.ToString();
        }
        catch { }
    }

    private async Task LoadAssetsAsync()
    {
        try
        {
            // Load only In-Service or Under-Maintenance assets (candidates for disposal).
            var filter = new AssetSearchFilter(
                SearchText: null, DepartmentId: null, CategoryId: null,
                Status: AssetStatus.InService, MinCondition: null,
                AcquiredFrom: null, AcquiredTo: null, Page: 1, PageSize: 1000);
            var assets = await _assetService.SearchAsync(filter);

            // Also include UnderMaintenance status.
            var filter2 = new AssetSearchFilter(
                SearchText: null, DepartmentId: null, CategoryId: null,
                Status: AssetStatus.UnderMaintenance, MinCondition: null,
                AcquiredFrom: null, AcquiredTo: null, Page: 1, PageSize: 1000);
            var assets2 = await _assetService.SearchAsync(filter2);

            var all = assets.Items.Concat(assets2.Items).ToList();
            _cmbAsset.DisplayMember = "Display";
            _cmbAsset.ValueMember = "Id";
            _cmbAsset.DataSource = all.Select(a => new { a.Id, Display = $"{a.AssetCode} — {a.Name}" }).ToList();

            if (all.Count == 0)
                _lblStatus.Text = "No assets available for disposal (only In Service / Under Maintenance can be disposed).";
        }
        catch (Exception ex)
        {
            MessageBox.Show("Failed to load assets: " + ex.Message, "Error",
                MessageBoxButtons.OK, MessageBoxIcon.Error);
        }
    }

    private async Task DisposeAsync()
    {
        if (_cmbAsset.SelectedValue is not long assetId || assetId <= 0)
        {
            MessageBox.Show("Please select an asset to dispose.", "Validation",
                MessageBoxButtons.OK, MessageBoxIcon.Warning);
            return;
        }
        if (!long.TryParse(_txtApprover.Text, out var approverId) || approverId <= 0)
        {
            MessageBox.Show("Please enter a valid Approver User ID.", "Validation",
                MessageBoxButtons.OK, MessageBoxIcon.Warning);
            return;
        }
        decimal? proceeds = decimal.TryParse(_txtProceeds.Text, out var p) ? p : null;
        if (proceeds.HasValue && proceeds < 0)
        {
            MessageBox.Show("Proceeds cannot be negative.", "Validation",
                MessageBoxButtons.OK, MessageBoxIcon.Warning);
            return;
        }
        var method = (DisposalMethod)_cmbMethod.SelectedItem!;

        // Confirmation
        var confirm = MessageBox.Show(
            $"Dispose asset:\n\n" +
            $"   Asset:     {_cmbAsset.Text}\n" +
            $"   Method:    {method}\n" +
            $"   Date:      {_dtpDisposalDate.Value:yyyy-MM-dd}\n" +
            $"   Proceeds:  {(proceeds?.ToString("C") ?? "—")}\n" +
            $"   Approver:  User #{approverId}\n\n" +
            "This action cannot be undone. An audit log entry will be written.",
            "Confirm Disposal",
            MessageBoxButtons.YesNo,
            MessageBoxIcon.Warning,
            MessageBoxDefaultButton.Button2);
        if (confirm != DialogResult.Yes) return;

        try
        {
            _btnDispose.Enabled = false;
            _lblStatus.Text = "Disposing…";

            var dto = new AssetDisposalDto(
                AssetId: assetId,
                Method: method,
                DisposalDate: _dtpDisposalDate.Value.Date,
                Proceeds: proceeds,
                ApprovedByUserId: approverId,
                Notes: string.IsNullOrWhiteSpace(_txtNotes.Text) ? null : _txtNotes.Text);

            var result = await _assetService.DisposeAsync(dto);

            _lblStatus.Text = $"✓ Asset '{result.AssetCode}' disposed via {method}.";
            _lblStatus.ForeColor = MaimsTheme.OK;

            // Reset for next disposal
            _txtProceeds.Text = "";
            _txtNotes.Text = "";
            await LoadAssetsAsync();
        }
        catch (UnauthorizedAccessException ex)
        {
            MessageBox.Show(ex.Message, "Permission denied",
                MessageBoxButtons.OK, MessageBoxIcon.Warning);
        }
        catch (InvalidOperationException ex)
        {
            // Separation of duties violation
            MessageBox.Show(ex.Message, "Cannot dispose",
                MessageBoxButtons.OK, MessageBoxIcon.Warning);
        }
        catch (Exception ex)
        {
            MessageBox.Show("Disposal failed: " + ex.Message, "Error",
                MessageBoxButtons.OK, MessageBoxIcon.Error);
        }
        finally
        {
            _btnDispose.Enabled = true;
        }
    }
}
