using MAIMS.Core.Enums;
using MAIMS.Data;
using MAIMS.WinUI.Theming;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;

namespace MAIMS.WinUI.Forms;

public class StockTransactionHistoryForm : Form
{
    private readonly IServiceScopeFactory _scopeFactory;
    private readonly ComboBox _cmbType = new();
    private readonly ComboBox _cmbItem = new();
    private readonly ComboBox _cmbWarehouse = new();
    private readonly DateTimePicker _dtpFrom = new();
    private readonly DateTimePicker _dtpTo = new();
    private readonly CheckBox _chkFrom = new();
    private readonly CheckBox _chkTo = new();
    private readonly DataGridView _grid = new();
    private readonly Button _btnSearch;
    private readonly Button _btnExportCsv;
    private readonly Button _btnClose;
    private readonly Label _statusLabel = new();

    public StockTransactionHistoryForm(IServiceScopeFactory scopeFactory)
    {
        _scopeFactory = scopeFactory;
        Text = "Stock Transaction History";
        FormBorderStyle = FormBorderStyle.None;
        Dock = DockStyle.Fill;
        BackColor = MaimsTheme.Background;

        // ── Row 1: Filters (Type, Item, Warehouse) ──
        // Labels use AutoSize=false with fixed width + right-aligned text so the
        // colon sits right next to the combo. This prevents DPI-dependent overlap.
        var pnlRow1 = new Panel { Dock = DockStyle.Top, Height = 36, BackColor = MaimsTheme.Surface };

        var lblType = new Label { Text = "Type:", Location = new Point(8, 8), Size = new Size(50, 20), Font = MaimsTheme.Body, TextAlign = ContentAlignment.MiddleRight };
        pnlRow1.Controls.Add(lblType);
        _cmbType.Location = new Point(65, 5);
        _cmbType.Size = new Size(130, 24);
        _cmbType.DropDownStyle = ComboBoxStyle.DropDownList;
        _cmbType.Font = MaimsTheme.Body;
        _cmbType.Items.AddRange(new object[] { "(all)", "Receipt", "Issue", "Transfer", "Adjustment", "WriteOff", "Reservation", "ReservationRelease" });
        _cmbType.SelectedIndex = 0;
        pnlRow1.Controls.Add(_cmbType);

        var lblItem = new Label { Text = "Item:", Location = new Point(210, 8), Size = new Size(45, 20), Font = MaimsTheme.Body, TextAlign = ContentAlignment.MiddleRight };
        pnlRow1.Controls.Add(lblItem);
        _cmbItem.Location = new Point(262, 5);
        _cmbItem.Size = new Size(200, 24);
        _cmbItem.DropDownStyle = ComboBoxStyle.DropDownList;
        _cmbItem.Font = MaimsTheme.Body;
        pnlRow1.Controls.Add(_cmbItem);

        var lblWh = new Label { Text = "Warehouse:", Location = new Point(460, 8), Size = new Size(105, 20), Font = MaimsTheme.Body, TextAlign = ContentAlignment.MiddleRight };
        pnlRow1.Controls.Add(lblWh);
        _cmbWarehouse.Location = new Point(572, 5);
        _cmbWarehouse.Size = new Size(170, 24);
        _cmbWarehouse.DropDownStyle = ComboBoxStyle.DropDownList;
        _cmbWarehouse.Font = MaimsTheme.Body;
        pnlRow1.Controls.Add(_cmbWarehouse);

        // ── Row 2: Date range — SYMMETRIC layout ──
        // Both sides use the same label width (60px) and date picker width (150px).
        var pnlRow2 = new Panel { Dock = DockStyle.Top, Height = 36, BackColor = MaimsTheme.Surface };

        // Left side: From
        _chkFrom.Text = "From:";
        _chkFrom.AutoSize = false;
        _chkFrom.Size = new Size(70, 24);
        _chkFrom.Font = MaimsTheme.Body;
        _chkFrom.Location = new Point(8, 6);
        _chkFrom.Checked = false;
        _chkFrom.TextAlign = ContentAlignment.MiddleLeft;
        pnlRow2.Controls.Add(_chkFrom);

        _dtpFrom.Location = new Point(75, 5);
        _dtpFrom.Size = new Size(150, 24);
        _dtpFrom.Format = DateTimePickerFormat.Short;
        _dtpFrom.Font = MaimsTheme.Body;
        _dtpFrom.Enabled = false;
        pnlRow2.Controls.Add(_dtpFrom);

        // Right side: To — same dimensions, offset by 280px
        _chkTo.Text = "To:";
        _chkTo.AutoSize = false;
        _chkTo.Size = new Size(70, 24);
        _chkTo.Font = MaimsTheme.Body;
        _chkTo.Location = new Point(288, 6);
        _chkTo.Checked = false;
        _chkTo.TextAlign = ContentAlignment.MiddleLeft;
        pnlRow2.Controls.Add(_chkTo);

        _dtpTo.Location = new Point(355, 5);
        _dtpTo.Size = new Size(150, 24);
        _dtpTo.Format = DateTimePickerFormat.Short;
        _dtpTo.Font = MaimsTheme.Body;
        _dtpTo.Enabled = false;
        pnlRow2.Controls.Add(_dtpTo);

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
        _chkTo.CheckedChanged += (s, e) =>
        {
            if (_chkTo.Checked && _chkFrom.Checked && _dtpTo.Value < _dtpFrom.Value)
                _dtpFrom.Value = _dtpTo.Value;
        };

        // ── Row 3: Buttons — left-aligned, never overlap ──
        var pnlRow3 = new Panel { Dock = DockStyle.Top, Height = 42, BackColor = MaimsTheme.Surface };
        _btnSearch = MaimsTheme.CreateButton("Search", primary: true);
        _btnExportCsv = MaimsTheme.CreateButton("Export CSV");
        _btnClose = MaimsTheme.CreateButton("Close");
        MaimsTheme.LayoutButtons(8, 5, 12, _btnSearch, _btnExportCsv, _btnClose);
        _btnClose.Click += (_, _) =>
        {
            if (Parent is TabPage page && page.Parent is TabControl tc)
                tc.TabPages.Remove(page);
            else
                Close();
        };
        pnlRow3.Controls.AddRange(new Control[] { _btnSearch, _btnExportCsv, _btnClose });

        // ── Grid ──
        _grid.Dock = DockStyle.Fill;
        _grid.AutoGenerateColumns = false;
        _grid.AllowUserToAddRows = false;
        _grid.AllowUserToDeleteRows = false;
        _grid.ReadOnly = true;
        _grid.SelectionMode = DataGridViewSelectionMode.FullRowSelect;
        _grid.BackgroundColor = MaimsTheme.Surface;
        _grid.BorderStyle = BorderStyle.None;
        _grid.RowHeadersVisible = false;
        _grid.Font = MaimsTheme.Body;
        _grid.AutoSizeColumnsMode = DataGridViewAutoSizeColumnsMode.Fill;

        AddCol("TransactionDate", "Date", 140);
        AddCol("TransactionType", "Type", 100);
        AddCol("Sku", "SKU", 110);
        AddCol("ItemName", "Item", 200);
        AddCol("WarehouseName", "Warehouse", 150);
        AddCol("Quantity", "Qty", 90, "N3");
        AddCol("ReasonCode", "Reason", 100);
        AddCol("ReferenceDocNo", "Ref Doc", 110);
        AddCol("LotBatch", "Lot/Batch", 100);
        AddCol("Supplier", "Supplier", 150);
        AddCol("Notes", "Notes", 280);

        // ── Status bar ──
        var pnlStatus = new Panel { Dock = DockStyle.Bottom, Height = 24, BackColor = MaimsTheme.Surface };
        _statusLabel.Dock = DockStyle.Fill;
        _statusLabel.Font = MaimsTheme.Small;
        _statusLabel.ForeColor = MaimsTheme.TextSecondary;
        _statusLabel.Padding = new Padding(8, 4, 8, 4);
        pnlStatus.Controls.Add(_statusLabel);

        // Assemble — add bottom-to-top (WinForms docking: last added = topmost)
        Controls.Add(_grid);        // Fill
        Controls.Add(pnlRow3);      // Top (row 3)
        Controls.Add(pnlRow2);      // Top (row 2)
        Controls.Add(pnlRow1);      // Top (row 1)
        Controls.Add(pnlStatus);    // Bottom

        _btnSearch.Click += async (_, _) => await LoadDataAsync();
        _btnExportCsv.Click += async (_, _) => await ExportCsvAsync();
        Load += async (_, _) => await LoadReferenceDataAsync();
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

    private async Task LoadReferenceDataAsync()
    {
        using var scope = _scopeFactory.CreateScope();
        var ctx = scope.ServiceProvider.GetRequiredService<MaimsDbContext>();

        var items = await ctx.Items.AsNoTracking().OrderBy(i => i.Sku).ToListAsync();
        _cmbItem.DisplayMember = "Display";
        _cmbItem.ValueMember = "Id";
        _cmbItem.DataSource = new[] { new { Id = 0L, Display = "(all items)" } }
            .Concat(items.Select(i => new { i.Id, Display = $"{i.Sku} — {i.Name}" })).ToList();

        var warehouses = await ctx.Warehouses.AsNoTracking().OrderBy(w => w.Name).ToListAsync();
        _cmbWarehouse.DisplayMember = "Name";
        _cmbWarehouse.ValueMember = "Id";
        _cmbWarehouse.DataSource = new[] { new { Id = 0L, Name = "(all warehouses)" } }
            .Concat(warehouses.Select(w => new { w.Id, w.Name })).ToList();

        await LoadDataAsync();
    }

    private async Task LoadDataAsync()
    {
        try
        {
            _statusLabel.Text = "Loading…";
            using var scope = _scopeFactory.CreateScope();
            var ctx = scope.ServiceProvider.GetRequiredService<MaimsDbContext>();

            var q = ctx.StockTransactions.AsNoTracking()
                .Include(t => t.Item).Include(t => t.Warehouse).AsQueryable();

            if (_cmbType.SelectedIndex > 0 && Enum.TryParse<StockTransactionType>(_cmbType.SelectedItem?.ToString(), out var txType))
                q = q.Where(t => t.TransactionType == txType);
            if (_cmbItem.SelectedValue is long itemId && itemId > 0)
                q = q.Where(t => t.ItemId == itemId);
            if (_cmbWarehouse.SelectedValue is long whId && whId > 0)
                q = q.Where(t => t.WarehouseId == whId || t.FromWarehouseId == whId || t.ToWarehouseId == whId);
            if (_chkFrom.Checked) q = q.Where(t => t.TransactionDate >= _dtpFrom.Value.Date);
            if (_chkTo.Checked) q = q.Where(t => t.TransactionDate < _dtpTo.Value.Date.AddDays(1));

            var rows = await q.OrderByDescending(t => t.TransactionDate).Take(1000).Select(t => new
            {
                TransactionDate = t.TransactionDate,
                TransactionType = t.TransactionType.ToString(),
                Sku = t.Item != null ? t.Item.Sku : "",
                ItemName = t.Item != null ? t.Item.Name : "",
                WarehouseName = t.Warehouse != null ? t.Warehouse.Name : "",
                Quantity = t.Quantity,
                ReasonCode = t.ReasonCode ?? "",
                ReferenceDocNo = t.ReferenceDocNo ?? "",
                LotBatch = t.LotBatch ?? "",
                Supplier = t.Supplier ?? "",
                Notes = t.Notes ?? ""
            }).ToListAsync();

            _grid.DataSource = rows;
            _statusLabel.Text = $"{rows.Count} transaction(s) found.";
        }
        catch (Exception ex)
        {
            MessageBox.Show("Failed to load: " + ex.Message, "Error", MessageBoxButtons.OK, MessageBoxIcon.Error);
        }
    }

    private async Task ExportCsvAsync()
    {
        try
        {
            using var sfd = new SaveFileDialog { Filter = "CSV files (*.csv)|*.csv|All files (*.*)|*.*", FileName = $"stock_transactions_{DateTime.Now:yyyyMMdd_HHmmss}.csv" };
            if (sfd.ShowDialog() != DialogResult.OK) return;

            using var scope = _scopeFactory.CreateScope();
            var ctx = scope.ServiceProvider.GetRequiredService<MaimsDbContext>();
            var q = ctx.StockTransactions.AsNoTracking().Include(t => t.Item).Include(t => t.Warehouse).AsQueryable();
            if (_cmbType.SelectedIndex > 0 && Enum.TryParse<StockTransactionType>(_cmbType.SelectedItem?.ToString(), out var txType)) q = q.Where(t => t.TransactionType == txType);
            if (_cmbItem.SelectedValue is long itemId && itemId > 0) q = q.Where(t => t.ItemId == itemId);
            if (_cmbWarehouse.SelectedValue is long whId && whId > 0) q = q.Where(t => t.WarehouseId == whId || t.FromWarehouseId == whId || t.ToWarehouseId == whId);
            if (_chkFrom.Checked) q = q.Where(t => t.TransactionDate >= _dtpFrom.Value.Date);
            if (_chkTo.Checked) q = q.Where(t => t.TransactionDate < _dtpTo.Value.Date.AddDays(1));

            var rows = await q.OrderByDescending(t => t.TransactionDate).Take(10000).ToListAsync();
            var sb = new System.Text.StringBuilder();
            sb.AppendLine("Date,Type,SKU,Item,Warehouse,Quantity,ReasonCode,RefDoc,LotBatch,Supplier,Notes");
            foreach (var t in rows)
                sb.AppendLine(string.Join(',', t.TransactionDate.ToString("o"), t.TransactionType.ToString(), t.Item?.Sku ?? "", EscapeCsv(t.Item?.Name ?? ""), EscapeCsv(t.Warehouse?.Name ?? ""), t.Quantity.ToString("F3"), t.ReasonCode ?? "", t.ReferenceDocNo ?? "", t.LotBatch ?? "", EscapeCsv(t.Supplier ?? ""), EscapeCsv(t.Notes ?? "")));
            await File.WriteAllTextAsync(sfd.FileName, sb.ToString());
            MessageBox.Show($"Exported to: {sfd.FileName}", "Export complete", MessageBoxButtons.OK, MessageBoxIcon.Information);
        }
        catch (Exception ex) { MessageBox.Show("Export failed: " + ex.Message, "Error", MessageBoxButtons.OK, MessageBoxIcon.Error); }
    }

    private static string EscapeCsv(string s) => s.Contains(',') || s.Contains('"') || s.Contains('\n') ? "\"" + s.Replace("\"", "\"\"") + "\"" : s;
}
