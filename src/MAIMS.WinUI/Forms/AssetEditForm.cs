using MAIMS.Core.DTOs;
using MAIMS.Core.Entities;
using MAIMS.Core.Enums;
using MAIMS.Core.Interfaces;
using MAIMS.WinUI.Theming;

namespace MAIMS.WinUI.Forms;

/// <summary>
/// Asset create / edit form. Uses MVP-passive-view: form owns state,
/// service does the work. Validation errors surface via ErrorProvider.
/// Category and Department dropdowns are loaded from the DB via IReferenceDataService.
/// </summary>
public class AssetEditForm : Form
{
    private readonly IAssetService _assetService;
    private readonly IReferenceDataService _refService;
    private readonly long? _assetId;
    private readonly ErrorProvider _error = new();

    private TextBox _txtName = null!;
    private TextBox _txtDescription = null!;
    private ComboBox _cmbCategory = null!;
    private ComboBox _cmbDepartment = null!;
    private ComboBox _cmbStatus = null!;
    private ComboBox _cmbCondition = null!;
    private TextBox _txtSerial = null!;
    private TextBox _txtFunding = null!;
    private TextBox _txtCost = null!;
    private DateTimePicker _dtpAcquired = null!;
    private CheckBox _chkAcquired = null!;
    private Button _btnSave = null!;
    private Button _btnCancel = null!;
    private Button _btnClose = null!;
    private Label _lblCode = null!;

    public AssetEditForm(IAssetService assetService, IReferenceDataService refService, long? assetId)
    {
        _assetService = assetService;
        _refService = refService;
        _assetId = assetId;

        Text = assetId is null ? "New Asset" : $"Edit Asset #{assetId}";
        FormBorderStyle = FormBorderStyle.FixedDialog;
        MaximizeBox = false;
        MinimizeBox = false;
        StartPosition = FormStartPosition.CenterParent;
        ClientSize = new Size(620, 600);
        BackColor = MaimsTheme.Background;
        Font = MaimsTheme.Body;

        BuildFields();
        Load += async (_, _) => await LoadReferenceDataAsync();

        if (_assetId is not null)
        {
            Load += async (_, _) => await LoadAssetAsync();
        }
    }

    private void BuildFields()
    {
        int y = 16;
        AddLabel("Asset Code (auto):", y); _lblCode = AddReadOnly("", y); y += 28;
        AddLabel("Name *:", y); _txtName = AddTextBox(y); y += 28;
        AddLabel("Description:", y); _txtDescription = AddTextBox(y, multiline: true); y += 50;
        AddLabel("Category *:", y); _cmbCategory = AddComboBox(y); y += 28;
        AddLabel("Department *:", y); _cmbDepartment = AddComboBox(y); y += 28;
        AddLabel("Status:", y); _cmbStatus = AddComboBox(y); y += 28;
        AddLabel("Condition:", y); _cmbCondition = AddComboBox(y); y += 28;
        AddLabel("Serial Number:", y); _txtSerial = AddTextBox(y); y += 28;
        AddLabel("Funding Source:", y); _txtFunding = AddTextBox(y); y += 28;
        AddLabel("Acquisition Cost:", y); _txtCost = AddTextBox(y); y += 28;
        AddLabel("Acquisition Date:", y);
        _chkAcquired = new CheckBox
        {
            Text = "Has date",
            Location = new Point(200, y),
            AutoSize = true,
            Checked = false,
            Font = MaimsTheme.Body
        };
        _dtpAcquired = new DateTimePicker
        {
            Location = new Point(330, y),
            Size = new Size(230, 24),
            Format = DateTimePickerFormat.Short,
            Enabled = false,
            Font = MaimsTheme.Body
        };
        _chkAcquired.CheckedChanged += (s, e) => _dtpAcquired.Enabled = _chkAcquired.Checked;
        Controls.Add(_chkAcquired);
        Controls.Add(_dtpAcquired);
        y += 40;

        // Buttons — in a FlowLayoutPanel docked to bottom so they stay anchored
        // regardless of form height/width. AutoScroll handles tall content above.
        var pnlButtons = new FlowLayoutPanel
        {
            Dock = DockStyle.Bottom,
            Height = 46,
            BackColor = MaimsTheme.Surface,
            FlowDirection = FlowDirection.LeftToRight,
            WrapContents = false,
            Padding = new Padding(16, 6, 16, 6)
        };
        _btnSave = MaimsTheme.CreateButton("Save", primary: true);
        _btnCancel = MaimsTheme.CreateButton("Cancel");
        _btnClose = MaimsTheme.CreateButton("Close");
        // Add right margin for horizontal spacing between buttons.
        // The LAST button (Close) gets zero right margin (no trailing gap needed).
        // Top margin is 0 for all (set by CreateButton) so Y baselines align.
        _btnSave.Margin = new Padding(0, 0, 8, 0);
        _btnCancel.Margin = new Padding(0, 0, 8, 0);
        _btnClose.Margin = new Padding(0, 0, 0, 0);
        _btnCancel.DialogResult = DialogResult.Cancel;

        _btnSave.Click += async (_, _) => await SaveAsync();
        _btnClose.Click += (_, _) =>
        {
            if (Parent is TabPage page && page.Parent is TabControl tc)
                tc.TabPages.Remove(page);
            else
                Close();
        };
        pnlButtons.Controls.AddRange(new Control[] { _btnSave, _btnCancel, _btnClose });

        AcceptButton = _btnSave;
        CancelButton = _btnCancel;

        // Add the button panel to the form (Dock=Bottom means it sticks to the bottom).
        Controls.Add(pnlButtons);
    }

    private void AddLabel(string text, int y)
    {
        var lbl = new Label
        {
            Text = text,
            Location = new Point(20, y + 3),
            Size = new Size(170, 20),
            Font = MaimsTheme.Body
        };
        Controls.Add(lbl);
    }

    private TextBox AddTextBox(int y, bool multiline = false)
    {
        var tb = new TextBox
        {
            Location = new Point(200, y),
            Size = multiline ? new Size(360, 50) : new Size(360, 24),
            Multiline = multiline,
            Font = MaimsTheme.Body
        };
        Controls.Add(tb);
        return tb;
    }

    private Label AddReadOnly(string text, int y)
    {
        var lbl = new Label
        {
            Text = text,
            Location = new Point(200, y + 3),
            Size = new Size(360, 20),
            ForeColor = MaimsTheme.TextSecondary,
            Font = MaimsTheme.Body
        };
        Controls.Add(lbl);
        return lbl;
    }

    private ComboBox AddComboBox(int y)
    {
        var cmb = new ComboBox
        {
            Location = new Point(200, y),
            Size = new Size(360, 24),
            DropDownStyle = ComboBoxStyle.DropDownList,
            Font = MaimsTheme.Body
        };
        Controls.Add(cmb);
        return cmb;
    }

    private async Task LoadReferenceDataAsync()
    {
        try
        {
            // Status + Condition (enums)
            _cmbStatus.Items.AddRange(Enum.GetValues(typeof(AssetStatus)).Cast<object>().ToArray());
            _cmbCondition.Items.AddRange(Enum.GetValues(typeof(ConditionRating)).Cast<object>().ToArray());
            _cmbStatus.SelectedIndex = (int)AssetStatus.Planned;
            _cmbCondition.SelectedIndex = (int)ConditionRating.Fair - 1;

            // Categories from DB
            var categories = await _refService.GetCategoriesAsync();
            _cmbCategory.DisplayMember = nameof(AssetCategory.Name);
            _cmbCategory.ValueMember = nameof(AssetCategory.Id);
            _cmbCategory.DataSource = categories.ToList();

            // Departments from DB
            var departments = await _refService.GetDepartmentsAsync();
            _cmbDepartment.DisplayMember = nameof(Department.Name);
            _cmbDepartment.ValueMember = nameof(Department.Id);
            _cmbDepartment.DataSource = departments.ToList();
        }
        catch (Exception ex)
        {
            MessageBox.Show("Could not load reference data: " + ex.Message, "Error",
                MessageBoxButtons.OK, MessageBoxIcon.Error);
        }
    }

    private async Task LoadAssetAsync()
    {
        try
        {
            var a = await _assetService.GetByIdAsync(_assetId!.Value);
            _lblCode.Text = a.AssetCode;
            _txtName.Text = a.Name;
            _txtDescription.Text = a.Description ?? "";
            _txtSerial.Text = a.SerialNumber ?? "";
            _txtFunding.Text = a.FundingSource ?? "";
            _txtCost.Text = a.AcquisitionCost?.ToString("N2") ?? "";
            _cmbStatus.SelectedItem = a.Status;
            _cmbCondition.SelectedItem = a.ConditionRating;
            if (a.CategoryId > 0) _cmbCategory.SelectedValue = a.CategoryId;
            if (a.DepartmentId > 0) _cmbDepartment.SelectedValue = a.DepartmentId;
            if (a.AcquisitionDate.HasValue)
            {
                _chkAcquired.Checked = true;
                _dtpAcquired.Value = a.AcquisitionDate.Value.Date;
            }
        }
        catch (Exception ex)
        {
            MessageBox.Show("Could not load asset: " + ex.Message, "Error",
                MessageBoxButtons.OK, MessageBoxIcon.Error);
        }
    }

    private async Task SaveAsync()
    {
        _error.Clear();

        // Validation
        if (string.IsNullOrWhiteSpace(_txtName.Text))
        {
            _error.SetError(_txtName, "Name is required.");
            _txtName.Focus();
            return;
        }
        if (_cmbCategory.SelectedValue is not long categoryId || categoryId <= 0)
        {
            _error.SetError(_cmbCategory, "Category is required.");
            _cmbCategory.Focus();
            return;
        }
        if (_cmbDepartment.SelectedValue is not long departmentId || departmentId <= 0)
        {
            _error.SetError(_cmbDepartment, "Department is required.");
            _cmbDepartment.Focus();
            return;
        }

        decimal? cost = decimal.TryParse(_txtCost.Text, out var c) ? c : null;
        DateTime? acquired = _chkAcquired.Checked ? _dtpAcquired.Value.Date : null;
        var status = (AssetStatus)_cmbStatus.SelectedItem!;
        var condition = (ConditionRating)_cmbCondition.SelectedItem!;

        try
        {
            if (_assetId is null)
            {
                var dto = new AssetCreateDto(
                    _txtName.Text.Trim(),
                    string.IsNullOrWhiteSpace(_txtDescription.Text) ? null : _txtDescription.Text,
                    categoryId,
                    departmentId,
                    null, null, status, acquired, cost,
                    string.IsNullOrWhiteSpace(_txtFunding.Text) ? null : _txtFunding.Text,
                    condition, null,
                    string.IsNullOrWhiteSpace(_txtSerial.Text) ? null : _txtSerial.Text);
                await _assetService.CreateAsync(dto);
            }
            else
            {
                var dto = new AssetUpdateDto(
                    _assetId.Value,
                    _txtName.Text.Trim(),
                    string.IsNullOrWhiteSpace(_txtDescription.Text) ? null : _txtDescription.Text,
                    categoryId, departmentId, null, null, status, acquired, cost,
                    string.IsNullOrWhiteSpace(_txtFunding.Text) ? null : _txtFunding.Text,
                    condition, null,
                    string.IsNullOrWhiteSpace(_txtSerial.Text) ? null : _txtSerial.Text);
                await _assetService.UpdateAsync(dto);
            }

            MessageBox.Show("Asset saved successfully.", "Success",
                MessageBoxButtons.OK, MessageBoxIcon.Information);

            // If we're embedded in a TabPage, close the tab. Otherwise Close() the modal dialog.
            if (Parent is TabPage page && page.Parent is TabControl tc)
            {
                tc.TabPages.Remove(page);
            }
            else
            {
                DialogResult = DialogResult.OK;
                Close();
            }
        }
        catch (UnauthorizedAccessException ex)
        {
            MessageBox.Show(ex.Message, "Permission denied",
                MessageBoxButtons.OK, MessageBoxIcon.Warning);
        }
        catch (Exception ex)
        {
            MessageBox.Show("Save failed: " + ex.Message, "Error",
                MessageBoxButtons.OK, MessageBoxIcon.Error);
        }
    }
}
