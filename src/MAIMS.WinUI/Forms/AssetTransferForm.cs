using MAIMS.Core.DTOs;
using MAIMS.Core.Enums;
using MAIMS.Core.Interfaces;
using MAIMS.WinUI.Theming;
using Microsoft.Extensions.DependencyInjection;

namespace MAIMS.WinUI.Forms;

/// <summary>
/// Asset transfer form. Moves an asset between departments / locations /
/// custodians. Requires an approver (department head).
/// </summary>
public class AssetTransferForm : Form
{
    private readonly IAssetService _assetService;
    private readonly IReferenceDataService _refService;
    private readonly IServiceScopeFactory _scopeFactory;
    private readonly ComboBox _cmbAsset;
    private readonly ComboBox _cmbToDepartment;
    private readonly ComboBox _cmbCustodian;
    private readonly TextBox _txtNotes;
    private readonly TextBox _txtApprover;
    private readonly Button _btnTransfer;
    private readonly Button _btnClose;
    private readonly Label _lblStatus;

    public AssetTransferForm(IAssetService assetService, IReferenceDataService refService, IServiceScopeFactory scopeFactory)
    {
        _assetService = assetService;
        _refService = refService;
        _scopeFactory = scopeFactory;
        Text = "Transfer Asset";
        FormBorderStyle = FormBorderStyle.None;
        Dock = DockStyle.Fill;
        BackColor = MaimsTheme.Background;
        Font = MaimsTheme.Body;
        AutoScroll = true;

        var pnlForm = new Panel { Dock = DockStyle.Top, Height = 280, BackColor = MaimsTheme.Surface, Padding = new Padding(16) };

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

        AddLabel(pnlForm, "To Department *:", y);
        _cmbToDepartment = new ComboBox
        {
            Location = new Point(200, y),
            Size = new Size(330, 24),
            DropDownStyle = ComboBoxStyle.DropDownList,
            Font = MaimsTheme.Body
        };
        pnlForm.Controls.Add(_cmbToDepartment);
        y += 32;

        AddLabel(pnlForm, "New Custodian (optional):", y);
        _cmbCustodian = new ComboBox
        {
            Location = new Point(200, y),
            Size = new Size(330, 24),
            DropDownStyle = ComboBoxStyle.DropDownList,
            Font = MaimsTheme.Body
        };
        pnlForm.Controls.Add(_cmbCustodian);
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

        _btnTransfer = MaimsTheme.CreateButton("Transfer Asset", primary: true);
        _btnClose = MaimsTheme.CreateButton("Close");
        MaimsTheme.LayoutButtons(200, y, 8, _btnTransfer, _btnClose);
        _btnClose.Click += (_, _) =>
        {
            if (Parent is TabPage page && page.Parent is TabControl tc)
                tc.TabPages.Remove(page);
            else
                Close();
        };
        _btnTransfer.Click += async (_, _) => await TransferAsync();
        pnlForm.Controls.Add(_btnTransfer);
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
        Load += async (_, _) => await Task.WhenAll(LoadReferenceDataAsync(), PopulateApproverAsync());
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

    private async Task LoadReferenceDataAsync()
    {
        try
        {
            var filter = new AssetSearchFilter(
                SearchText: null, DepartmentId: null, CategoryId: null,
                Status: AssetStatus.InService, MinCondition: null,
                AcquiredFrom: null, AcquiredTo: null, Page: 1, PageSize: 1000);
            var assets = await _assetService.SearchAsync(filter);
            _cmbAsset.DisplayMember = "Display";
            _cmbAsset.ValueMember = "Id";
            _cmbAsset.DataSource = assets.Items
                .Select(a => new { a.Id, Display = $"{a.AssetCode} — {a.Name}" })
                .ToList();

            var depts = await _refService.GetDepartmentsAsync();
            _cmbToDepartment.DisplayMember = "Name";
            _cmbToDepartment.ValueMember = "Id";
            _cmbToDepartment.DataSource = depts.ToList();

            var custodians = await _refService.GetCustodiansAsync();
            var custList = new[] { new { Id = 0L, Name = "(no change)" } }
                .Concat(custodians.Select(u => new { Id = u.Id, Name = $"{u.Username} — {u.Name}" }))
                .ToList();
            _cmbCustodian.DisplayMember = "Name";
            _cmbCustodian.ValueMember = "Id";
            _cmbCustodian.DataSource = custList;
            _cmbCustodian.SelectedIndex = 0;
        }
        catch (Exception ex)
        {
            MessageBox.Show("Failed to load reference data: " + ex.Message, "Error",
                MessageBoxButtons.OK, MessageBoxIcon.Error);
        }
    }

    private async Task TransferAsync()
    {
        if (_cmbAsset.SelectedValue is not long assetId || assetId <= 0)
        {
            MessageBox.Show("Please select an asset to transfer.", "Validation",
                MessageBoxButtons.OK, MessageBoxIcon.Warning);
            return;
        }
        if (_cmbToDepartment.SelectedValue is not long deptId || deptId <= 0)
        {
            MessageBox.Show("Please select a destination department.", "Validation",
                MessageBoxButtons.OK, MessageBoxIcon.Warning);
            return;
        }
        if (!long.TryParse(_txtApprover.Text, out var approverId) || approverId <= 0)
        {
            MessageBox.Show("Please enter a valid Approver User ID.", "Validation",
                MessageBoxButtons.OK, MessageBoxIcon.Warning);
            return;
        }

        long? custodianId = _cmbCustodian.SelectedValue is long c && c > 0 ? c : null;

        try
        {
            _btnTransfer.Enabled = false;
            _lblStatus.Text = "Transferring…";

            var dto = new AssetTransferDto(
                AssetId: assetId,
                ToDepartmentId: deptId,
                ToLocationId: null,
                ToCustodianUserId: custodianId,
                ApprovedByUserId: approverId,
                Notes: string.IsNullOrWhiteSpace(_txtNotes.Text) ? null : _txtNotes.Text);

            var result = await _assetService.TransferAsync(dto);

            _lblStatus.Text = $"✓ Asset '{result.AssetCode}' transferred to {_cmbToDepartment.Text}.";
            _lblStatus.ForeColor = MaimsTheme.OK;

            _txtNotes.Text = "";
            await LoadReferenceDataAsync();
        }
        catch (UnauthorizedAccessException ex)
        {
            MessageBox.Show(ex.Message, "Permission denied",
                MessageBoxButtons.OK, MessageBoxIcon.Warning);
        }
        catch (Exception ex)
        {
            MessageBox.Show("Transfer failed: " + ex.Message, "Error",
                MessageBoxButtons.OK, MessageBoxIcon.Error);
        }
        finally
        {
            _btnTransfer.Enabled = true;
        }
    }
}
