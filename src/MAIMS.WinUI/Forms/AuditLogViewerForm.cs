using MAIMS.Core.Interfaces;
using MAIMS.WinUI.Theming;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;

namespace MAIMS.WinUI.Forms;

/// <summary>
/// Read-only audit log viewer. Auditors can filter by entity type, action,
/// date range, or changed-by user. Results shown in a DataGridView with
/// before/after JSON preview below. CSV export via IAuditService.ExportAsync.
///
/// Layout uses TableLayoutPanel + Dock so the form resizes correctly inside
/// its parent TabPage regardless of the window's actual size.
/// </summary>
public class AuditLogViewerForm : Form
{
    private readonly IAuditService _auditService;
    private readonly IServiceScopeFactory _scopeFactory;
    private readonly TextBox _txtEntityType;
    private readonly ComboBox _cmbAction;
    private readonly DateTimePicker _dtpFrom;
    private readonly DateTimePicker _dtpTo;
    private readonly CheckBox _chkFrom;
    private readonly CheckBox _chkTo;
    private readonly Button _btnSearch;
    private readonly Button _btnExport;
    private readonly Button _btnPurge;
    private readonly Button _btnClose;
    private readonly DataGridView _grid;
    private readonly TextBox _txtBefore;
    private readonly TextBox _txtAfter;
    private readonly Label _statusLabel;

    public AuditLogViewerForm(IAuditService auditService, IServiceScopeFactory scopeFactory)
    {
        _auditService = auditService;
        _scopeFactory = scopeFactory;
        Text = "Audit Log Viewer";
        FormBorderStyle = FormBorderStyle.None;
        Dock = DockStyle.Fill;
        BackColor = MaimsTheme.Background;

        // ─── Root: 3-row table (top filters | grid | detail panel) ───
        var root = new TableLayoutPanel
        {
            Dock = DockStyle.Fill,
            ColumnCount = 1,
            RowCount = 4,
            BackColor = MaimsTheme.Background,
            Padding = new Padding(0)
        };
        // Rows: 0 = filters (auto), 1 = grid (percent 50%), 2 = detail (percent 50%), 3 = status bar
        root.RowStyles.Add(new RowStyle(SizeType.Absolute, 80));   // filters
        root.RowStyles.Add(new RowStyle(SizeType.Percent, 50));     // grid
        root.RowStyles.Add(new RowStyle(SizeType.Percent, 50));     // detail
        root.RowStyles.Add(new RowStyle(SizeType.Absolute, 28));    // status (was 24 — increased so "58 log entries loaded" fully visible)
        root.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100));

        // ─── Top filter row (2-column table: filters on left, buttons on right) ───
        var pnlTop = new TableLayoutPanel
        {
            Dock = DockStyle.Fill,
            ColumnCount = 2,
            RowCount = 1,
            BackColor = MaimsTheme.Surface,
            Padding = new Padding(8, 8, 8, 8)
        };
        pnlTop.ColumnStyles.Add(new ColumnStyle(SizeType.Absolute, 860));  // filters area (widened so To: doesn't wrap)
        pnlTop.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100));   // buttons area (fills rest)

        var pnlFilters = new FlowLayoutPanel
        {
            Dock = DockStyle.Fill,
            FlowDirection = FlowDirection.LeftToRight,
            WrapContents = true,
            AutoScroll = true,
            Padding = new Padding(0)
        };

        var lblEntity = new Label { Text = "Entity:", Margin = new Padding(0, 6, 4, 0), AutoSize = true, Font = MaimsTheme.Body };
        _txtEntityType = new TextBox { Size = new Size(120, 24), Font = MaimsTheme.Body, Margin = new Padding(0, 4, 12, 0) };

        var lblAction = new Label { Text = "Action:", Margin = new Padding(0, 6, 4, 0), AutoSize = true, Font = MaimsTheme.Body };
        _cmbAction = new ComboBox
        {
            Size = new Size(110, 24),
            DropDownStyle = ComboBoxStyle.DropDownList,
            Font = MaimsTheme.Body,
            Margin = new Padding(0, 4, 12, 0)
        };
        _cmbAction.Items.AddRange(new object[] { "(all)", "CREATE", "UPDATE", "DELETE", "LOGIN", "LOGOUT" });
        _cmbAction.SelectedIndex = 0;

        _chkFrom = new CheckBox { Text = "From:", AutoSize = true, Font = MaimsTheme.Body, Checked = false, Margin = new Padding(0, 4, 4, 0) };
        _dtpFrom = new DateTimePicker { Size = new Size(120, 24), Format = DateTimePickerFormat.Short, Font = MaimsTheme.Body, Enabled = false, Margin = new Padding(0, 4, 12, 0) };

        _chkTo = new CheckBox { Text = "To:", AutoSize = true, Font = MaimsTheme.Body, Checked = false, Margin = new Padding(0, 4, 4, 0) };
        _dtpTo = new DateTimePicker { Size = new Size(120, 24), Format = DateTimePickerFormat.Short, Font = MaimsTheme.Body, Enabled = false, Margin = new Padding(0, 4, 12, 0) };

        _chkFrom.CheckedChanged += (s, e) => _dtpFrom.Enabled = _chkFrom.Checked;
        _chkTo.CheckedChanged += (s, e) => _dtpTo.Enabled = _chkTo.Checked;

        // Date validation: From cannot be after To.
        _dtpFrom.ValueChanged += (s, e) =>
        {
            if (_chkFrom.Checked && _chkTo.Checked && _dtpFrom.Value > _dtpTo.Value)
                _dtpTo.Value = _dtpFrom.Value;
        };
        _dtpTo.ValueChanged += (s, e) =>
        {
            if (_chkFrom.Checked && _chkTo.Checked && _dtpTo.Value < _dtpFrom.Value)
                _dtpFrom.Value = _dtpTo.Value;
        };

        pnlFilters.Controls.AddRange(new Control[] { lblEntity, _txtEntityType, lblAction, _cmbAction, _chkFrom, _dtpFrom, _chkTo, _dtpTo });

        // Buttons on the right (right-aligned via anchor)
        var pnlButtons = new FlowLayoutPanel
        {
            Dock = DockStyle.Fill,
            FlowDirection = FlowDirection.LeftToRight,
            WrapContents = false,
            AutoScroll = true
        };

        _btnSearch = MaimsTheme.CreateButton("Search", primary: true);
        _btnExport = MaimsTheme.CreateButton("Export CSV");
        _btnPurge = MaimsTheme.CreateButton("Purge Invalid…");
        _btnClose = MaimsTheme.CreateButton("Close");
        MaimsTheme.LayoutButtons(700, 10, 8, _btnSearch, _btnExport, _btnPurge, _btnClose);
        _btnPurge.Click += async (_, _) => await PurgeInvalidEntriesAsync();
        _btnClose.Click += (_, _) =>
        {
            if (Parent is TabPage page && page.Parent is TabControl tc)
                tc.TabPages.Remove(page);
            else
                Close();
        };

        pnlButtons.Controls.AddRange(new Control[] { _btnSearch, _btnExport, _btnPurge, _btnClose });

        pnlTop.Controls.Add(pnlFilters, 0, 0);
        pnlTop.Controls.Add(pnlButtons, 1, 0);

        // ─── Grid ───
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
            AutoSizeColumnsMode = DataGridViewAutoSizeColumnsMode.Fill,
            Anchor = AnchorStyles.Top | AnchorStyles.Bottom | AnchorStyles.Left | AnchorStyles.Right
        };

        AddCol("Id", "ID", 60);
        AddCol("EntityType", "Entity Type", 120);
        AddCol("EntityId", "Entity ID", 80);
        AddCol("Action", "Action", 80);
        AddCol("ChangedBy", "Changed By", 100);
        AddCol("ChangedAt", "Changed At", 140);
        // IP Address column removed — the WinForms host never populates
        // ICurrentSession.IpAddress (always null), so the column was always
        // empty. The entity/DTO still carry the field for future remote-host
        // scenarios, but the UI no longer shows it.
        AddCol("MachineName", "Machine Name", 120);

        _grid.SelectionChanged += (_, _) => ShowSelectedDetails();

        // ─── Detail panel: 2-column table (Before | After), each with Dock=Fill textbox ───
        var pnlDetail = new TableLayoutPanel
        {
            Dock = DockStyle.Fill,
            ColumnCount = 2,
            RowCount = 2,
            BackColor = MaimsTheme.Surface,
            Padding = new Padding(8, 4, 8, 4)
        };
        pnlDetail.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 50));
        pnlDetail.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 50));
        pnlDetail.RowStyles.Add(new RowStyle(SizeType.Absolute, 22));   // labels
        pnlDetail.RowStyles.Add(new RowStyle(SizeType.Percent, 100));    // textboxes

        var lblBefore = new Label { Text = "Before (JSON):", Dock = DockStyle.Fill, Font = MaimsTheme.Body, ForeColor = MaimsTheme.TextSecondary };
        var lblAfter = new Label { Text = "After (JSON):", Dock = DockStyle.Fill, Font = MaimsTheme.Body, ForeColor = MaimsTheme.TextSecondary };

        _txtBefore = new TextBox
        {
            Dock = DockStyle.Fill,
            Multiline = true,
            ScrollBars = ScrollBars.Vertical,
            ReadOnly = true,
            Font = new Font("Consolas", 9F),
            BackColor = Color.White,
            BorderStyle = BorderStyle.FixedSingle
        };
        _txtAfter = new TextBox
        {
            Dock = DockStyle.Fill,
            Multiline = true,
            ScrollBars = ScrollBars.Vertical,
            ReadOnly = true,
            Font = new Font("Consolas", 9F),
            BackColor = Color.White,
            BorderStyle = BorderStyle.FixedSingle
        };

        pnlDetail.Controls.Add(lblBefore, 0, 0);
        pnlDetail.Controls.Add(lblAfter, 1, 0);
        pnlDetail.Controls.Add(_txtBefore, 0, 1);
        pnlDetail.Controls.Add(_txtAfter, 1, 1);

        // ─── Status bar ───
        // Fixed height (28px) instead of Dock=Fill — the root TableLayoutPanel
        // already reserves 24px for this row (RowStyle Absolute 24). With Dock=Fill
        // + a 24px row, the label's bottom edge was being clipped by the grid above
        // when the text was long (e.g. "58 log entries loaded" wrapped to 2 lines).
        // 28px gives 4px breathing room so the full status text is always visible.
        var pnlStatus = new Panel
        {
            Dock = DockStyle.Fill,
            Height = 28,
            BackColor = MaimsTheme.PrimaryDark
        };
        _statusLabel = new Label
        {
            Dock = DockStyle.Fill,
            Font = MaimsTheme.Small,
            ForeColor = Color.White,
            Padding = new Padding(8, 4, 8, 4),
            TextAlign = ContentAlignment.MiddleLeft,
            AutoEllipsis = false  // don't truncate with "..." — show full text
        };
        pnlStatus.Controls.Add(_statusLabel);

        // Assemble root
        root.Controls.Add(pnlTop, 0, 0);
        root.Controls.Add(_grid, 0, 1);
        root.Controls.Add(pnlDetail, 0, 2);
        root.Controls.Add(pnlStatus, 0, 3);

        Controls.Add(root);

        _btnSearch.Click += async (_, _) => await LoadDataAsync();
        _btnExport.Click += async (_, _) => await ExportCsvAsync();
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
            _btnSearch.Enabled = false;
            _statusLabel.Text = "Loading…";

            var action = _cmbAction.SelectedIndex > 0 ? _cmbAction.SelectedItem?.ToString() : null;
            var from = _chkFrom.Checked ? (DateTime?)_dtpFrom.Value.Date : null;
            var to = _chkTo.Checked ? (DateTime?)_dtpTo.Value.Date.AddDays(1).AddSeconds(-1) : null;
            var entityType = string.IsNullOrWhiteSpace(_txtEntityType.Text) ? null : _txtEntityType.Text.Trim();

            var filter = new AuditSearchFilter(
                EntityType: entityType,
                EntityId: null,
                ChangedByUserId: null,
                Action: action,
                From: from,
                To: to,
                Page: 1,
                PageSize: 500);

            var rows = await _auditService.SearchAsync(filter);
            _grid.DataSource = rows.ToList();
            _statusLabel.Text = $"{rows.Count} audit log entries loaded.";
            _txtBefore.Text = "";
            _txtAfter.Text = "";
        }
        catch (UnauthorizedAccessException ex)
        {
            MessageBox.Show(ex.Message, "Permission denied",
                MessageBoxButtons.OK, MessageBoxIcon.Warning);
        }
        catch (Exception ex)
        {
            MessageBox.Show("Failed to load audit log: " + ex.Message, "Error",
                MessageBoxButtons.OK, MessageBoxIcon.Error);
        }
        finally
        {
            _btnSearch.Enabled = true;
        }
    }

    private void ShowSelectedDetails()
    {
        if (_grid.CurrentRow?.DataBoundItem is not AuditLogEntry entry)
        {
            _txtBefore.Text = "";
            _txtAfter.Text = "";
            return;
        }
        _txtBefore.Text = PrettyJson(entry.BeforeJson);
        _txtAfter.Text = PrettyJson(entry.AfterJson);
    }

    private static string PrettyJson(string? json)
    {
        if (string.IsNullOrWhiteSpace(json)) return "(empty)";
        try
        {
            var doc = System.Text.Json.JsonDocument.Parse(json);
            return System.Text.Json.JsonSerializer.Serialize(doc, new System.Text.Json.JsonSerializerOptions { WriteIndented = true });
        }
        catch
        {
            return json;
        }
    }

    private async Task ExportCsvAsync()
    {
        try
        {
            var action = _cmbAction.SelectedIndex > 0 ? _cmbAction.SelectedItem?.ToString() : null;
            var from = _chkFrom.Checked ? (DateTime?)_dtpFrom.Value.Date : null;
            var to = _chkTo.Checked ? (DateTime?)_dtpTo.Value.Date.AddDays(1).AddSeconds(-1) : null;
            var entityType = string.IsNullOrWhiteSpace(_txtEntityType.Text) ? null : _txtEntityType.Text.Trim();

            var filter = new AuditSearchFilter(
                EntityType: entityType, EntityId: null, ChangedByUserId: null,
                Action: action, From: from, To: to, Page: 1, PageSize: 10000);

            var bytes = await _auditService.ExportAsync(filter, "csv");

            using var sfd = new SaveFileDialog
            {
                Filter = "CSV files (*.csv)|*.csv|All files (*.*)|*.*",
                FileName = $"audit_log_{DateTime.Now:yyyyMMdd_HHmmss}.csv"
            };
            if (sfd.ShowDialog() == DialogResult.OK)
            {
                await File.WriteAllBytesAsync(sfd.FileName, bytes);
                MessageBox.Show($"Exported to: {sfd.FileName}", "Export complete",
                    MessageBoxButtons.OK, MessageBoxIcon.Information);
            }
        }
        catch (UnauthorizedAccessException ex)
        {
            MessageBox.Show(ex.Message, "Permission denied",
                MessageBoxButtons.OK, MessageBoxIcon.Warning);
        }
        catch (Exception ex)
        {
            MessageBox.Show("Export failed: " + ex.Message, "Error",
                MessageBoxButtons.OK, MessageBoxIcon.Error);
        }
    }

    /// <summary>
    /// Purges audit_log entries with invalid entity_id (≤ 0) or all entries.
    /// Requires the user to enter the MySQL root password because the BEFORE
    /// DELETE trigger on audit_log must be temporarily dropped.
    /// </summary>
    private async Task PurgeInvalidEntriesAsync()
    {
        // Step 1: Show what we're about to do
        var info = MessageBox.Show(
            "This will permanently DELETE audit log entries with invalid Entity ID (≤ 0).\n\n" +
            "These entries were created by a previous bug and have no corresponding\n" +
            "real entity in the database.\n\n" +
            "The purge requires MySQL root privileges because the BEFORE DELETE\n" +
            "trigger on audit_log must be temporarily dropped and recreated.\n\n" +
            "Continue?",
            "Purge Invalid Audit Log Entries",
            MessageBoxButtons.YesNo,
            MessageBoxIcon.Warning,
            MessageBoxDefaultButton.Button2);
        if (info != DialogResult.Yes) return;

        // Step 2: Ask for root password
        var rootPwd = PromptDialog.Show(this, "MySQL Root Password",
            "Enter MySQL root password:", "");
        if (string.IsNullOrWhiteSpace(rootPwd))
        {
            MessageBox.Show("Purge cancelled — root password is required.",
                "Cancelled", MessageBoxButtons.OK, MessageBoxIcon.Information);
            return;
        }

        // Step 3: Build root connection string from the app's connection string
        // but replace User/Password with root credentials.
        string rootConnStr;
        try
        {
            using var scope = _scopeFactory.CreateScope();
            var config = scope.ServiceProvider.GetRequiredService<Microsoft.Extensions.Configuration.IConfiguration>();
            var appConnStr = config.GetConnectionString("MaimsDb")
                ?? throw new InvalidOperationException("ConnectionStrings:MaimsDb not found.");

            // Use MySqlConnectionStringBuilder to safely build the root connection
            // string — prevents injection if the root password contains ; or =.
            var appBuilder = new MySqlConnector.MySqlConnectionStringBuilder(appConnStr);
            var rootBuilder = new MySqlConnector.MySqlConnectionStringBuilder
            {
                Server = appBuilder.Server,
                Port = appBuilder.Port,
                Database = "maims",
                UserID = "root",
                Password = rootPwd,
                AllowPublicKeyRetrieval = true,
                CharacterSet = "utf8mb4"
            };
            rootConnStr = rootBuilder.ConnectionString;
        }
        catch (Exception ex)
        {
            MessageBox.Show("Failed to build root connection string: " + ex.Message,
                "Error", MessageBoxButtons.OK, MessageBoxIcon.Error);
            return;
        }

        // Step 4: Final confirmation
        var confirm = MessageBox.Show(
            "FINAL CONFIRMATION\n\n" +
            "You are about to permanently delete audit log entries\n" +
            "with invalid Entity ID (≤ 0).\n\n" +
            "The audit_log table's BEFORE DELETE trigger will be:\n" +
            "  1. DROPPED temporarily\n" +
            "  2. DELETE executed\n" +
            "  3. RECREATED (immutability restored)\n\n" +
            "Proceed?",
            "Final Confirmation",
            MessageBoxButtons.YesNo,
            MessageBoxIcon.Warning,
            MessageBoxDefaultButton.Button2);
        if (confirm != DialogResult.Yes) return;

        // Step 5: Execute purge
        try
        {
            _btnPurge.Enabled = false;
            _statusLabel.Text = "Purging invalid entries…";

            var deleted = await _auditService.PurgeInvalidEntriesAsync(rootConnStr, purgeAll: false);

            MessageBox.Show(
                $"Purge complete.\n\nDeleted {deleted} invalid audit log entr(ies).\n\n" +
                "The BEFORE DELETE trigger has been recreated.\n" +
                "Audit log immutability is restored.",
                "Purge Complete",
                MessageBoxButtons.OK,
                MessageBoxIcon.Information);

            _statusLabel.Text = $"✓ Purged {deleted} invalid entr(ies).";
            _statusLabel.ForeColor = MaimsTheme.OK;

            // Reload data
            await LoadDataAsync();
        }
        catch (Exception ex)
        {
            MessageBox.Show(
                "Purge failed: " + ex.Message + "\n\n" +
                "If the BEFORE DELETE trigger was dropped but not recreated,\n" +
                "run db/30_cleanup_audit_log.sql manually to restore it.",
                "Error",
                MessageBoxButtons.OK,
                MessageBoxIcon.Error);
            _statusLabel.Text = "Purge failed.";
            _statusLabel.ForeColor = MaimsTheme.Critical;
        }
        finally
        {
            _btnPurge.Enabled = true;
        }
    }
}
