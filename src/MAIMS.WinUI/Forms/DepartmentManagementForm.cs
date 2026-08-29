using MAIMS.Core.Entities;
using MAIMS.Core.Enums;
using MAIMS.Data;
using MAIMS.WinUI.Theming;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;

namespace MAIMS.WinUI.Forms;

/// <summary>
/// Department management form. Admin can view, create, edit code/name of
/// departments. Soft-delete is supported via the audit interceptor.
/// </summary>
public class DepartmentManagementForm : Form
{
    private readonly IServiceScopeFactory _scopeFactory;
    private readonly DataGridView _grid;
    private readonly Button _btnNew;
    private readonly Button _btnEdit;
    private readonly Button _btnRefresh;
    private readonly Button _btnClose;
    private readonly Label _statusLabel;

    public DepartmentManagementForm(IServiceScopeFactory scopeFactory)
    {
        _scopeFactory = scopeFactory;
        Text = "Department Management";
        FormBorderStyle = FormBorderStyle.None;
        Dock = DockStyle.Fill;
        BackColor = MaimsTheme.Background;

        var pnlTop = new Panel { Dock = DockStyle.Top, Height = 50, BackColor = MaimsTheme.Surface, Padding = new Padding(8) };

        _btnNew = MaimsTheme.CreateButton("New Department…", primary: true);
        _btnEdit = MaimsTheme.CreateButton("Edit…");
        _btnRefresh = MaimsTheme.CreateButton("Refresh");
        _btnClose = MaimsTheme.CreateButton("Close");
        MaimsTheme.LayoutButtons(8, 10, 8, _btnNew, _btnEdit, _btnRefresh, _btnClose);
        _btnClose.Click += (_, _) =>
        {
            if (Parent is TabPage page && page.Parent is TabControl tc)
                tc.TabPages.Remove(page);
            else
                Close();
        };

        pnlTop.Controls.AddRange(new Control[] { _btnNew, _btnEdit, _btnRefresh, _btnClose });

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

        AddCol("Id", "ID", 60);
        AddCol("Code", "Code", 100);
        AddCol("Name", "Department Name", 280);
        AddCol("ParentName", "Parent", 200);
        AddCol("HeadName", "Head", 180);
        AddCol("CreatedAt", "Created At", 140);

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

        _btnNew.Click += (_, _) => OpenEditDialog(null);
        _btnEdit.Click += (_, _) =>
        {
            if (_grid.CurrentRow?.DataBoundItem is DepartmentRow row)
                OpenEditDialog(row.Id);
        };
        _btnRefresh.Click += async (_, _) => await LoadDataAsync();
        _grid.DoubleClick += (_, _) =>
        {
            if (_grid.CurrentRow?.DataBoundItem is DepartmentRow row)
                OpenEditDialog(row.Id);
        };
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

            var depts = await ctx.Departments.AsNoTracking()
                .Include(d => d.Parent)
                .Include(d => d.Head)  // load the department head (User navigation)
                .OrderBy(d => d.Code)
                .Select(d => new DepartmentRow(
                    d.Id, d.Code, d.Name,
                    d.Parent != null ? d.Parent.Name : "",
                    d.Head != null ? d.Head.Name : "(not assigned)",  // show head's full Name
                    d.CreatedAt))
                .ToListAsync();

            _grid.DataSource = depts;
            _statusLabel.Text = $"{depts.Count} departments loaded.";
        }
        catch (Exception ex)
        {
            MessageBox.Show("Failed to load departments: " + ex.Message, "Error",
                MessageBoxButtons.OK, MessageBoxIcon.Error);
        }
    }

    private void OpenEditDialog(long? id)
    {
        using var dlg = new DepartmentEditDialog(_scopeFactory, id);
        if (dlg.ShowDialog(this) == DialogResult.OK)
            _ = LoadDataAsync();
    }

    private sealed record DepartmentRow(
        long Id, string Code, string Name,
        string ParentName, string HeadName, DateTime CreatedAt);
}

/// <summary>
/// Modal dialog for creating / editing a department.
/// </summary>
public class DepartmentEditDialog : Form
{
    private readonly IServiceScopeFactory _scopeFactory;
    private readonly long? _departmentId;
    private readonly TextBox _txtCode;
    private readonly TextBox _txtName;
    private readonly ComboBox _cmbParent;
    private readonly ComboBox _cmbHead;  // department head (User)
    private readonly Button _btnSave;
    private readonly Button _btnCancel;

    public DepartmentEditDialog(IServiceScopeFactory scopeFactory, long? departmentId)
    {
        _scopeFactory = scopeFactory;
        _departmentId = departmentId;

        Text = departmentId is null ? "New Department" : $"Edit Department #{departmentId}";
        FormBorderStyle = FormBorderStyle.FixedDialog;
        MaximizeBox = false;
        MinimizeBox = false;
        StartPosition = FormStartPosition.CenterParent;
        ClientSize = new Size(440, 240);  // taller — added Head row
        BackColor = MaimsTheme.Background;
        Font = MaimsTheme.Body;

        int y = 16;
        AddLabel("Code *:", y); _txtCode = MkTxt(y); y += 32;
        AddLabel("Name *:", y); _txtName = MkTxt(y); y += 32;
        AddLabel("Parent:", y);
        _cmbParent = new ComboBox
        {
            Location = new Point(140, y),
            Size = new Size(280, 24),
            DropDownStyle = ComboBoxStyle.DropDownList,
            Font = MaimsTheme.Body
        };
        _cmbParent.Items.Add("(no parent)");
        _cmbParent.SelectedIndex = 0;
        Controls.Add(_cmbParent);
        y += 32;

        // Head — the User who leads this department (optional).
        // Only Active users are listed. The head does NOT need to be in this
        // department — the field is informational (e.g., a director may
        // formally lead a dept while their own user record is in IT).
        AddLabel("Head:", y);
        _cmbHead = new ComboBox
        {
            Location = new Point(140, y),
            Size = new Size(280, 24),
            DropDownStyle = ComboBoxStyle.DropDownList,
            Font = MaimsTheme.Body
        };
        Controls.Add(_cmbHead);
        y += 36;

        _btnSave = MaimsTheme.CreateButton("Save", primary: true);
        _btnCancel = MaimsTheme.CreateButton("Cancel");
        MaimsTheme.LayoutButtons(200, y, 8, _btnSave, _btnCancel);
        _btnCancel.DialogResult = DialogResult.Cancel;
        _btnSave.Click += async (_, _) => await SaveAsync();
        Controls.Add(_btnSave);
        Controls.Add(_btnCancel);

        AcceptButton = _btnSave;
        CancelButton = _btnCancel;

        Load += async (_, _) => await LoadDataAsync();
    }

    private void AddLabel(string text, int y) =>
        Controls.Add(new Label { Text = text, Location = new Point(16, y + 3), Size = new Size(120, 20), Font = MaimsTheme.Body });

    private TextBox MkTxt(int y)
    {
        var tb = new TextBox
        {
            Location = new Point(140, y),
            Size = new Size(280, 24),
            Font = MaimsTheme.Body
        };
        Controls.Add(tb);  // ← MUST be added to Controls, otherwise invisible
        return tb;
    }

    private async Task LoadDataAsync()
    {
        using var scope = _scopeFactory.CreateScope();
        var ctx = scope.ServiceProvider.GetRequiredService<MaimsDbContext>();

        // Load all departments for parent dropdown (exclude self if editing).
        var depts = await ctx.Departments.AsNoTracking()
            .Where(d => _departmentId == null || d.Id != _departmentId.Value)
            .OrderBy(d => d.Name)
            .ToListAsync();

        _cmbParent.DisplayMember = "Name";
        _cmbParent.ValueMember = "Id";
        _cmbParent.DataSource = new List<Department> { new() { Id = 0, Name = "(no parent)" } }
            .Concat(depts)
            .ToList();

        // Load all Active users for the Head dropdown. Show "Full Name (username)"
        // so the admin can distinguish users with similar names.
        var users = await ctx.Users.AsNoTracking()
            .Where(u => u.Status == UserStatus.Active)
            .OrderBy(u => u.Name)
            .Select(u => new { u.Id, Display = $"{u.Name}  ({u.Username})" })
            .ToListAsync();

        // Build a unified list with a "(not assigned)" sentinel entry at top.
        // Both the sentinel and user entries share the same anonymous shape
        // { Id: long, Display: string }, so the ComboBox binds cleanly.
        var headList = new[]
        {
            new { Id = 0L, Display = "(not assigned)" }
        }.Concat(users.Select(u => new { Id = (long)u.Id, Display = (string)u.Display }))
         .ToList();

        _cmbHead.DisplayMember = "Display";
        _cmbHead.ValueMember = "Id";
        _cmbHead.DataSource = headList;
        _cmbHead.SelectedIndex = 0;

        if (_departmentId is long id)
        {
            var dept = await ctx.Departments.AsNoTracking().FirstOrDefaultAsync(d => d.Id == id);
            if (dept != null)
            {
                _txtCode.Text = dept.Code;
                _txtName.Text = dept.Name;
                if (dept.ParentDepartmentId is long parentId)
                    _cmbParent.SelectedValue = parentId;
                if (dept.HeadUserId is long headId)
                    _cmbHead.SelectedValue = headId;
            }
        }
    }

    private async Task SaveAsync()
    {
        if (string.IsNullOrWhiteSpace(_txtCode.Text) ||
            string.IsNullOrWhiteSpace(_txtName.Text))
        {
            MessageBox.Show("Code and Name are required.", "Validation",
                MessageBoxButtons.OK, MessageBoxIcon.Warning);
            return;
        }

        try
        {
            using var scope = _scopeFactory.CreateScope();
            var ctx = scope.ServiceProvider.GetRequiredService<MaimsDbContext>();

            var code = _txtCode.Text.Trim();
            if (await ctx.Departments.AnyAsync(d => d.Code == code && d.Id != (_departmentId ?? 0)))
            {
                MessageBox.Show($"Department code '{code}' already exists.", "Duplicate",
                    MessageBoxButtons.OK, MessageBoxIcon.Warning);
                return;
            }

            long? parentId = _cmbParent.SelectedValue is long p && p > 0 ? p : null;
            long? headId = _cmbHead.SelectedValue is long h && h > 0 ? h : null;

            if (_departmentId is null)
            {
                ctx.Departments.Add(new Department
                {
                    Code = code,
                    Name = _txtName.Text.Trim(),
                    ParentDepartmentId = parentId,
                    HeadUserId = headId
                });
            }
            else
            {
                var dept = await ctx.Departments.FirstOrDefaultAsync(d => d.Id == _departmentId.Value);
                if (dept is null) return;
                dept.Code = code;
                dept.Name = _txtName.Text.Trim();
                dept.ParentDepartmentId = parentId;
                dept.HeadUserId = headId;
            }

            await ctx.SaveChangesAsync();
            MessageBox.Show("Department saved.", "Success",
                MessageBoxButtons.OK, MessageBoxIcon.Information);
            DialogResult = DialogResult.OK;
            Close();
        }
        catch (Exception ex)
        {
            MessageBox.Show("Save failed: " + ex.Message, "Error",
                MessageBoxButtons.OK, MessageBoxIcon.Error);
        }
    }
}
