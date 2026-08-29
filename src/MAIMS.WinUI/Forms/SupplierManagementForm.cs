using MAIMS.Data;
using MAIMS.WinUI.Theming;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;

namespace MAIMS.WinUI.Forms;

/// <summary>
/// Supplier management form. Since there's no dedicated Supplier table
/// in the current schema (suppliers are stored as text on Item.PreferredSupplier
/// and StockTransaction.Supplier), this form shows a read-only list of all
/// distinct supplier names found in the database, with the count of items
/// and transactions associated with each.
/// </summary>
public class SupplierManagementForm : Form
{
    private readonly IServiceScopeFactory _scopeFactory;
    private readonly DataGridView _grid;
    private readonly Button _btnRefresh;
    private readonly Button _btnClose;
    private readonly Label _statusLabel;

    public SupplierManagementForm(IServiceScopeFactory scopeFactory)
    {
        _scopeFactory = scopeFactory;
        Text = "Suppliers";
        FormBorderStyle = FormBorderStyle.None;
        Dock = DockStyle.Fill;
        BackColor = MaimsTheme.Background;

        var pnlTop = new Panel { Dock = DockStyle.Top, Height = 50, BackColor = MaimsTheme.Surface, Padding = new Padding(8) };

        var lblInfo = new Label
        {
            Text = "Suppliers are derived from Item.PreferredSupplier and StockTransaction.Supplier fields.",
            Location = new Point(8, 14),
            AutoSize = true,
            Font = MaimsTheme.Small,
            ForeColor = MaimsTheme.TextSecondary
        };

        _btnRefresh = MaimsTheme.CreateButton("Refresh", primary: true);
        _btnClose = MaimsTheme.CreateButton("Close");
        MaimsTheme.LayoutButtons(600, 10, 8, _btnRefresh, _btnClose);
        _btnClose.Click += (_, _) =>
        {
            if (Parent is TabPage page && page.Parent is TabControl tc)
                tc.TabPages.Remove(page);
            else
                Close();
        };

        pnlTop.Controls.AddRange(new Control[] { lblInfo, _btnRefresh, _btnClose });

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

        AddCol("SupplierName", "Supplier Name", 300);
        AddCol("ItemCount", "Items Using This Supplier", 200);
        AddCol("TransactionCount", "Stock Receipts from Supplier", 220);
        AddCol("LastTransactionDate", "Last Receipt Date", 160);

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

            // Get distinct supplier names from items
            var itemSuppliers = await ctx.Items.AsNoTracking()
                .Where(i => i.PreferredSupplier != null && i.PreferredSupplier != "")
                .GroupBy(i => i.PreferredSupplier)
                .Select(g => new { SupplierName = g.Key, ItemCount = g.Count() })
                .ToListAsync();

            // Get distinct supplier names from stock transactions (Receipts only)
            var txSuppliers = await ctx.StockTransactions.AsNoTracking()
                .Where(t => t.Supplier != null && t.Supplier != "")
                .GroupBy(t => t.Supplier)
                .Select(g => new
                {
                    SupplierName = g.Key,
                    TransactionCount = g.Count(),
                    LastTransactionDate = g.Max(t => (DateTime?)t.TransactionDate)
                })
                .ToListAsync();

            // Merge the two lists
            var allSuppliers = itemSuppliers
                .Select(s => s.SupplierName!)
                .Union(txSuppliers.Select(s => s.SupplierName!))
                .OrderBy(s => s)
                .ToList();

            var rows = allSuppliers.Select(name => new
            {
                SupplierName = name,
                ItemCount = itemSuppliers.FirstOrDefault(s => s.SupplierName == name)?.ItemCount ?? 0,
                TransactionCount = txSuppliers.FirstOrDefault(s => s.SupplierName == name)?.TransactionCount ?? 0,
                LastTransactionDate = txSuppliers.FirstOrDefault(s => s.SupplierName == name)?.LastTransactionDate
            }).ToList();

            _grid.DataSource = rows;
            _statusLabel.Text = $"{rows.Count} supplier(s) found.";
        }
        catch (Exception ex)
        {
            MessageBox.Show("Failed to load suppliers: " + ex.Message, "Error",
                MessageBoxButtons.OK, MessageBoxIcon.Error);
        }
    }
}
