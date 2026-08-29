using MAIMS.Core.Interfaces;
using MAIMS.WinUI.Forms;
using MAIMS.WinUI.Theming;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using System.Drawing.Drawing2D;

namespace MAIMS.WinUI;

/// <summary>
/// Application shell: top bar (title + search + profile), left TreeView navigation,
/// central TabControl workspace, bottom StatusStrip.
/// </summary>
public class MainForm : Form
{
    private readonly IServiceProvider _services;
    private readonly IServiceScopeFactory _scopeFactory;
    private readonly IAuthService _auth;
    private readonly Panel _topBar;
    private readonly StatusStrip _status;
    private readonly TreeView _nav;
    private readonly TabControl _workspace;
    private readonly ToolStripStatusLabel _lblUser;
    private readonly ToolStripStatusLabel _lblRole;
    private readonly ToolStripStatusLabel _lblConn;

    public bool IsSignOut { get; private set; }

    public MainForm(IServiceProvider services)
    {
        _services = services;
        _scopeFactory = services.GetRequiredService<IServiceScopeFactory>();
        _auth = services.GetRequiredService<IAuthService>();

        Text = "MAIMS — Municipal Asset & Inventory Management System";
        WindowState = FormWindowState.Maximized;
        MinimumSize = new Size(1024, 700);
        Font = MaimsTheme.Body;
        BackColor = MaimsTheme.Background;

        _topBar = BuildTopBar();
        _status = BuildStatus(out _lblUser, out _lblRole, out _lblConn);
        _nav = BuildNavigation();
        _workspace = new TabControl
        {
            Dock = DockStyle.Fill,
            Appearance = TabAppearance.Normal,
            Font = MaimsTheme.Body
        };

        var split = new SplitContainer
        {
            Dock = DockStyle.Fill,
            Orientation = Orientation.Vertical
        };
        split.Panel1.Controls.Add(_nav);
        split.Panel2.Controls.Add(_workspace);

        Controls.Add(split);
        Controls.Add(_topBar);
        Controls.Add(_status);

        Load += (s, e) =>
        {
            split.Panel1MinSize = 180;
            split.Panel2MinSize = 200;
            split.SplitterDistance = Math.Min(240, Math.Max(180, (int)(ClientSize.Width * 0.22)));
            split.Panel2MinSize = 400;
        };

        RefreshStatus();
        WireNavigation();
    }

    // ── Top bar: title (left) + search box with popup suggestions (center) + profile button (right) ──
    private Panel BuildTopBar()
    {
        var bar = new Panel
        {
            Dock = DockStyle.Top,
            Height = 80,  // 72 → 80: a bit more breathing room for the icon + button
            BackColor = MaimsTheme.Primary
        };

        // Left: Title — AutoSize so it never wraps
        var lblTitle = new Label
        {
            Text = "  Municipal Asset & Inventory Management System",
            Font = new Font("Segoe UI", 12F, FontStyle.Bold),
            ForeColor = Color.White,
            AutoSize = true,
            TextAlign = ContentAlignment.MiddleLeft
        };
        bar.Controls.Add(lblTitle);

        // ── Right: Profile button (user icon + username + chevron, all in one) ──
        // Draw an NxN white user silhouette at runtime (no external image asset required).
        // Smaller icon (32px) so the button doesn't look oversized horizontally.
        var userIcon = CreateWhiteUserIcon(32);
        bar.Disposed += (s, e) => userIcon.Dispose();

        var btnProfile = new Button
        {
            Image = userIcon,
            ImageAlign = ContentAlignment.MiddleLeft,
            Text = "  " + (_auth.CurrentUserName ?? "User") + "  \u25BE",
            TextImageRelation = TextImageRelation.ImageBeforeText,
            TextAlign = ContentAlignment.MiddleRight,
            Font = new Font("Segoe UI", 10F, FontStyle.Bold),
            ForeColor = Color.White,
            BackColor = MaimsTheme.PrimaryDark,
            FlatStyle = FlatStyle.Flat,
            AutoSize = false,
            Width = 280,
            Height = 52,  // 48 → 52: matches the new 80px bar (52 + 14 padding top/bottom = 80)
            Padding = new Padding(14, 4, 18, 4),
            Cursor = Cursors.Hand,
            UseVisualStyleBackColor = false
        };
        btnProfile.FlatAppearance.BorderSize = 1;
        btnProfile.FlatAppearance.BorderColor = Color.FromArgb(80, 110, 170);
        // Subtle hover feedback so the button is clearly interactive.
        btnProfile.MouseEnter += (s, e) => btnProfile.BackColor = Color.FromArgb(45, 70, 130);
        btnProfile.MouseLeave += (s, e) => btnProfile.BackColor = MaimsTheme.PrimaryDark;
        bar.Controls.Add(btnProfile);

        // Profile dropdown — unified, no separators
        var menu = new ContextMenuStrip { Renderer = new MaimsToolStripRenderer() };
        menu.Items.Add("Account Details", null, (s, e) => ShowAccountDetails());
        menu.Items.Add("Change Password…", null, (s, e) => OpenChangePassword());
        menu.Items.Add("Sign Out", null, (s, e) => { IsSignOut = true; Close(); });
        menu.Items.Add("Exit", null, (s, e) => { IsSignOut = false; Application.Exit(); });
        btnProfile.Click += (s, e) => menu.Show(btnProfile, new Point(0, btnProfile.Height + 2));

        // ── Center: Search box ──
        var txtSearch = new TextBox
        {
            Font = new Font("Segoe UI", 11F),
            Width = 500,
            BackColor = Color.White,
            ForeColor = MaimsTheme.TextPrimary,
            BorderStyle = BorderStyle.FixedSingle
        };
        bar.Controls.Add(txtSearch);

        // Hint label
        var lblHint = new Label
        {
            Text = "Search modules…",
            ForeColor = Color.Gray,
            Font = new Font("Segoe UI", 10F),
            AutoSize = true,
            Cursor = Cursors.IBeam,
            BackColor = Color.White
        };
        bar.Controls.Add(lblHint);

        // All searchable modules
        var searchMap = new (string Display, Action Op)[]
        {
            ("Dashboard", () => OpenDashboard()),
            ("Asset Registry", () => OpenAssetList()),
            ("New Asset", () => OpenAssetEdit(null)),
            ("Transfer Asset", () => OpenAssetTransfer()),
            ("Dispose Asset", () => OpenAssetDisposal()),
            ("Asset Inspection", () => OpenAssetInspection()),
            ("Asset Attachments", () => OpenAssetAttachments()),
            ("Asset Detail View", () => OpenAssetDetailView()),
            ("Recent Transfers", () => OpenTransferHistory()),
            ("Item Catalog", () => OpenInventoryItems()),
            ("Receive Stock", () => OpenReceiveStock()),
            ("Issue Stock", () => OpenIssueStock()),
            ("Stock Adjustment", () => OpenStockAdjustment()),
            ("Transfer Stock", () => OpenStockTransfer()),
            ("Cycle Count", () => OpenCycleCount()),
            ("Stock Balances", () => OpenStockBalances()),
            ("Low Stock Alerts", () => OpenLowStockAlerts()),
            ("Transaction History", () => OpenStockTransactionHistory()),
            ("Warehouses", () => OpenWarehouseManagement()),
            ("Locations", () => OpenLocationManagement()),
            ("Audit Log", () => OpenAuditLog()),
            ("Users", () => OpenUserManagement()),
            ("Roles & Permissions", () => OpenRoleManagement()),
            ("Departments", () => OpenDepartmentManagement()),
            ("Suppliers", () => OpenSupplierManagement()),
            ("Asset Status Distribution", () => OpenAssetStatusReport()),
            ("Asset Valuation by Department", () => OpenAssetValuationReport()),
            ("Asset Depreciation Report", () => OpenAssetDepreciationReport()),
            ("Inventory Valuation", () => OpenInventoryValuationReport()),
            ("Assets by Department", () => OpenDepartmentAssetReport()),
        };

        // ── Popup suggestion panel ──
        // Using a plain Panel (added to the MainForm, not a TopMost Form or ToolStripDropDown)
        // so the parent MainForm stays active and ListBox click events fire normally.
        // (ToolStripDropDown + ToolStripControlHost had the "first click just activates the
        // dropdown, second click actually hits the ListBox" problem.)
        var popup = new Panel
        {
            BackColor = Color.White,
            BorderStyle = BorderStyle.FixedSingle,
            Visible = false,
            Width = 500,
            Height = 200
        };

        var lstSuggestions = new ListBox
        {
            Dock = DockStyle.Fill,
            Font = MaimsTheme.Body,
            BackColor = Color.White,
            ForeColor = MaimsTheme.TextPrimary,
            BorderStyle = BorderStyle.None,
            IntegralHeight = false
        };
        popup.Controls.Add(lstSuggestions);

        // Track current matches for click handling
        List<(string Display, Action Op)> currentMatches = new();

        // Show popup with suggestions
        void ShowSuggestions(string text)
        {
            var q = text.Trim().ToLowerInvariant();
            if (string.IsNullOrEmpty(q))
            {
                popup.Visible = false;
                lblHint.Visible = true;
                return;
            }
            lblHint.Visible = false;

            currentMatches = searchMap
                .Where(m => m.Display.ToLowerInvariant().Contains(q))
                .ToList();

            lstSuggestions.Items.Clear();
            lstSuggestions.SelectedIndex = -1;

            const int perItem = 24;
            int computedHeight;
            if (currentMatches.Count == 0)
            {
                lstSuggestions.Items.Add($"No results for \"{text}\"");
                computedHeight = 30;
            }
            else
            {
                foreach (var m in currentMatches)
                    lstSuggestions.Items.Add(m.Display);
                computedHeight = Math.Min(currentMatches.Count * perItem + 4, 240);
            }

            // Place the popup below the search box, in MainForm coordinates.
            // bar is Dock=Top with Height=72, txtSearch is inside the bar,
            // so its MainForm-space Y is bar.Bottom + 1.
            popup.Width = txtSearch.Width;
            popup.Height = computedHeight;
            popup.Location = new Point(txtSearch.Left, bar.Height + 1);
            popup.BringToFront();
            popup.Visible = true;
        }

        // Live search: update suggestions as user types
        txtSearch.TextChanged += (s, e) =>
        {
            // If the user cleared the search box, hide the popup (but keep it open otherwise).
            if (string.IsNullOrWhiteSpace(txtSearch.Text))
            {
                popup.Visible = false;
                lblHint.Visible = true;
                return;
            }
            ShowSuggestions(txtSearch.Text);
        };

        // Click on suggestion → hide popup first, then open module
        lstSuggestions.Click += (s, e) =>
        {
            if (lstSuggestions.SelectedIndex < 0) return;
            var selected = lstSuggestions.Items[lstSuggestions.SelectedIndex].ToString();
            var match = currentMatches.FirstOrDefault(m => m.Display == selected);
            if (match.Op != null)
            {
                popup.Visible = false;
                txtSearch.Text = "";
                lblHint.Visible = true;
                match.Op();
            }
        };

        // Mouse hover on the listbox highlights the row under the cursor.
        lstSuggestions.MouseMove += (s, e) =>
        {
            int idx = lstSuggestions.IndexFromPoint(e.Location);
            if (idx >= 0 && idx < lstSuggestions.Items.Count && lstSuggestions.SelectedIndex != idx)
                lstSuggestions.SelectedIndex = idx;
        };

        // Enter key → open selected (or first) suggestion
        txtSearch.KeyDown += (s, e) =>
        {
            if (e.KeyCode == Keys.Enter)
            {
                e.SuppressKeyPress = true;
                if (popup.Visible && currentMatches.Count > 0)
                {
                    int idx = lstSuggestions.SelectedIndex >= 0 ? lstSuggestions.SelectedIndex : 0;
                    var match = currentMatches[idx];
                    popup.Visible = false;
                    txtSearch.Text = "";
                    lblHint.Visible = true;
                    match.Op();
                    return;
                }
                // Fallback: exact keyword search
                PerformSearch(txtSearch.Text.Trim());
                popup.Visible = false;
            }
            else if (e.KeyCode == Keys.Down && popup.Visible && currentMatches.Count > 0)
            {
                e.SuppressKeyPress = true;
                int newIdx = lstSuggestions.SelectedIndex + 1;
                if (newIdx >= currentMatches.Count) newIdx = 0;
                lstSuggestions.SelectedIndex = newIdx;
            }
            else if (e.KeyCode == Keys.Up && popup.Visible && currentMatches.Count > 0)
            {
                e.SuppressKeyPress = true;
                int newIdx = lstSuggestions.SelectedIndex - 1;
                if (newIdx < 0) newIdx = currentMatches.Count - 1;
                lstSuggestions.SelectedIndex = newIdx;
            }
            else if (e.KeyCode == Keys.Escape)
            {
                popup.Visible = false;
            }
        };

        // NOTE: The popup stays open until the user clicks a suggestion, presses
        // Escape/Enter, or clicks elsewhere on the form. No LostFocus timer is needed —
        // the popup is a child Panel of the MainForm, so the form stays active.

        // Hint interactions
        lblHint.Click += (s, e) => txtSearch.Focus();
        txtSearch.GotFocus += (s, e) => { if (!string.IsNullOrWhiteSpace(txtSearch.Text)) lblHint.Visible = false; };

        // Attach the popup Panel to the MainForm once the bar is parented.
        // Also subscribe to MainForm events so the popup closes appropriately.
        bar.ParentChanged += (s, e) =>
        {
            var form = bar.FindForm();
            if (form == null) return;
            // Add popup to MainForm so it can float over the workspace area.
            if (!form.Controls.Contains(popup))
                form.Controls.Add(popup);
            popup.BringToFront();

            // Close popup when the main form is deactivated (alt-tab).
            form.Deactivate += (sender, args) => popup.Visible = false;
            // Close popup when the user clicks anywhere on the form (outside the popup).
            form.Click += (sender, args) => popup.Visible = false;
        };

        // ── Position controls on the bar ──
        void Reposition()
        {
            // Profile button on the right, vertically centered
            btnProfile.Location = new Point(bar.Width - btnProfile.Width - 16,
                (bar.Height - btnProfile.Height) / 2);

            // Title on the left, vertically centered
            lblTitle.Location = new Point(16, (bar.Height - lblTitle.Height) / 2);
            lblTitle.AutoSize = true;

            // Search box: pushed to the LEFT (just after title + small gap), not centered.
            var searchRight = btnProfile.Left - 20;
            var searchLeft = lblTitle.Right + 24;
            var maxSearchWidth = Math.Max(360, searchRight - searchLeft);
            txtSearch.Width = Math.Min(560, maxSearchWidth);
            txtSearch.Location = new Point(searchLeft, (bar.Height - txtSearch.Height) / 2 + 1);

            lblHint.Location = new Point(txtSearch.Left + 8,
                (bar.Height - lblHint.Height) / 2 + 2);

            // If popup is visible, keep it glued to the new search box position.
            if (popup.Visible)
            {
                popup.Width = txtSearch.Width;
                popup.Location = new Point(txtSearch.Left, bar.Height + 1);
            }
        }
        Reposition();
        bar.Resize += (s, e) => Reposition();

        // Dispose popup when bar is disposed
        bar.Disposed += (s, e) => popup.Dispose();

        return bar;
    }

    // ── Search: maps keywords to OpenXxx methods ──
    private void PerformSearch(string query)
    {
        if (string.IsNullOrWhiteSpace(query)) return;
        var q = query.ToLowerInvariant().Trim();

        var map = new (string[] Keys, Action Op)[]
        {
            (new[]{"dashboard","overview","home"}, () => OpenDashboard()),
            (new[]{"asset registry","asset list","assets"}, () => OpenAssetList()),
            (new[]{"new asset","register asset","create asset"}, () => OpenAssetEdit(null)),
            (new[]{"transfer asset","asset transfer"}, () => OpenAssetTransfer()),
            (new[]{"dispose asset","disposal"}, () => OpenAssetDisposal()),
            (new[]{"inspection","inspect","condition"}, () => OpenAssetInspection()),
            (new[]{"attachment","attachments","upload"}, () => OpenAssetAttachments()),
            (new[]{"asset detail","detail view","lifecycle"}, () => OpenAssetDetailView()),
            (new[]{"recent transfer","transfers"}, () => OpenTransferHistory()),
            (new[]{"item catalog","items","sku"}, () => OpenInventoryItems()),
            (new[]{"receive stock","stock in","receipt"}, () => OpenReceiveStock()),
            (new[]{"issue stock","stock out","issue"}, () => OpenIssueStock()),
            (new[]{"stock adjustment","adjustment","adjust"}, () => OpenStockAdjustment()),
            (new[]{"transfer stock","stock transfer"}, () => OpenStockTransfer()),
            (new[]{"cycle count","count","physical inventory"}, () => OpenCycleCount()),
            (new[]{"stock balance","balance","balances"}, () => OpenStockBalances()),
            (new[]{"low stock","reorder","alert"}, () => OpenLowStockAlerts()),
            (new[]{"transaction history","transactions"}, () => OpenStockTransactionHistory()),
            (new[]{"warehouse","warehouses","depot"}, () => OpenWarehouseManagement()),
            (new[]{"location","locations","site"}, () => OpenLocationManagement()),
            (new[]{"audit log","audit","compliance"}, () => OpenAuditLog()),
            (new[]{"users","user management"}, () => OpenUserManagement()),
            (new[]{"roles","role management","permissions"}, () => OpenRoleManagement()),
            (new[]{"departments","department"}, () => OpenDepartmentManagement()),
            (new[]{"suppliers","supplier"}, () => OpenSupplierManagement()),
            (new[]{"asset status","status report"}, () => OpenAssetStatusReport()),
            (new[]{"asset valuation","valuation"}, () => OpenAssetValuationReport()),
            (new[]{"depreciation","depreciation report"}, () => OpenAssetDepreciationReport()),
            (new[]{"inventory valuation","inventory value"}, () => OpenInventoryValuationReport()),
            (new[]{"assets by department","department report"}, () => OpenDepartmentAssetReport()),
        };

        var matches = map.Where(m => m.Keys.Any(k => k.Contains(q) || q.Contains(k))).ToList();
        if (matches.Count == 1) { matches[0].Op(); }
        else if (matches.Count > 1)
        {
            var f = new Form { Text = "Search Results", FormBorderStyle = FormBorderStyle.FixedDialog, StartPosition = FormStartPosition.CenterParent, ClientSize = new Size(350, 50 + matches.Count * 36), BackColor = MaimsTheme.Background, Font = MaimsTheme.Body };
            f.Controls.Add(new Label { Text = $"{matches.Count} results for \"{query}\":", Location = new Point(16, 12), AutoSize = true, ForeColor = MaimsTheme.TextSecondary });
            for (int i = 0; i < matches.Count; i++)
            {
                // Use 36px height to match the standard CreateButton height,
                // and 36px vertical spacing so buttons don't touch each other.
                var btn = new Button { Text = matches[i].Keys[0], Location = new Point(16, 40 + i * 36), Size = new Size(300, 32), FlatStyle = FlatStyle.Flat, Font = MaimsTheme.Body, BackColor = MaimsTheme.Surface, ForeColor = MaimsTheme.TextPrimary, TextAlign = ContentAlignment.MiddleLeft };
                btn.FlatAppearance.BorderColor = MaimsTheme.Border;
                btn.FlatAppearance.BorderSize = 1;
                var op = matches[i].Op; btn.Click += (s, e) => { f.Close(); op(); };
                f.Controls.Add(btn);
            }
            f.ShowDialog(this);
        }
        else
        {
            MessageBox.Show($"No modules found for \"{query}\".\n\nTry: dashboard, assets, inventory, receive, issue, audit, reports, users, roles…", "No Results", MessageBoxButtons.OK, MessageBoxIcon.Information);
        }
    }

    private StatusStrip BuildStatus(out ToolStripStatusLabel lblUser, out ToolStripStatusLabel lblRole, out ToolStripStatusLabel lblConn)
    {
        var status = new StatusStrip { BackColor = MaimsTheme.PrimaryDark, ForeColor = Color.White, Font = MaimsTheme.Small };
        lblUser = new ToolStripStatusLabel { Spring = false, Margin = new Padding(8, 0, 16, 0) };
        lblRole = new ToolStripStatusLabel { Spring = false, Margin = new Padding(0, 0, 16, 0) };
        lblConn = new ToolStripStatusLabel { Spring = true, Text = "Connected", TextAlign = ContentAlignment.MiddleRight };
        status.Items.AddRange(new ToolStripItem[] { lblUser, lblRole, lblConn });
        return status;
    }

    private TreeView BuildNavigation()
    {
        var tv = new TreeView
        {
            Dock = DockStyle.Fill,
            BorderStyle = BorderStyle.None,
            Font = MaimsTheme.Body,
            ShowNodeToolTips = true
        };
        var nAssets = tv.Nodes.Add("assets", "Fixed Assets", 0, 0);
        nAssets.Nodes.Add("assets_list", "Asset Registry", 1, 1);
        nAssets.Nodes.Add("assets_new", "Register New Asset…", 1, 1);
        nAssets.Nodes.Add("assets_transfer_op", "Transfer Asset…", 1, 1);
        nAssets.Nodes.Add("assets_dispose_op", "Dispose Asset…", 1, 1);
        nAssets.Nodes.Add("assets_inspection", "Asset Inspection…", 1, 1);
        nAssets.Nodes.Add("assets_attachments", "Asset Attachments…", 1, 1);
        nAssets.Nodes.Add("assets_detail", "Asset Detail View…", 1, 1);
        nAssets.Nodes.Add("assets_transfers", "Recent Transfers", 1, 1);

        var nInv = tv.Nodes.Add("inv", "Inventory", 0, 0);
        nInv.Nodes.Add("inv_items", "Item Catalog", 1, 1);
        nInv.Nodes.Add("inv_balances", "Stock Balances", 1, 1);
        nInv.Nodes.Add("inv_low", "Low Stock Alerts", 1, 1);
        nInv.Nodes.Add("inv_receive", "Receive Stock", 1, 1);
        nInv.Nodes.Add("inv_issue", "Issue Stock", 1, 1);
        nInv.Nodes.Add("inv_adjust", "Stock Adjustment…", 1, 1);
        nInv.Nodes.Add("inv_transfer", "Transfer Stock…", 1, 1);
        nInv.Nodes.Add("inv_cycle_count", "Cycle Count…", 1, 1);
        nInv.Nodes.Add("inv_tx_history", "Transaction History…", 1, 1);
        nInv.Nodes.Add("inv_warehouses", "Warehouses", 1, 1);
        nInv.Nodes.Add("inv_locations", "Locations", 1, 1);

        var nReports = tv.Nodes.Add("reports", "Reports", 0, 0);
        nReports.Nodes.Add("rep_asset_status", "Asset Status Distribution", 1, 1);
        nReports.Nodes.Add("rep_asset_valuation", "Asset Valuation by Department", 1, 1);
        nReports.Nodes.Add("rep_dept_assets", "Assets by Department", 1, 1);
        nReports.Nodes.Add("rep_depreciation", "Asset Depreciation Report", 1, 1);
        nReports.Nodes.Add("rep_inv_valuation", "Inventory Valuation", 1, 1);

        var nAudit = tv.Nodes.Add("audit", "Audit & Compliance", 0, 0);
        nAudit.Nodes.Add("audit_log", "Audit Log", 1, 1);

        var nAdmin = tv.Nodes.Add("admin", "Administration", 0, 0);
        nAdmin.Nodes.Add("admin_dashboard", "Dashboard", 1, 1);
        nAdmin.Nodes.Add("admin_users", "Users", 1, 1);
        nAdmin.Nodes.Add("admin_roles", "Roles & Permissions", 1, 1);
        nAdmin.Nodes.Add("admin_depts", "Departments", 1, 1);
        nAdmin.Nodes.Add("admin_suppliers", "Suppliers", 1, 1);

        tv.ExpandAll();
        return tv;
    }

    private void WireNavigation()
    {
        _nav.NodeMouseDoubleClick += (s, e) =>
        {
            switch (e.Node.Name)
            {
                case "assets_list":
                    OpenAssetList();
                    break;
                case "assets_new":
                    OpenAssetEdit(null);
                    break;
                case "assets_transfer_op":
                    OpenAssetTransfer();
                    break;
                case "assets_dispose_op":
                    OpenAssetDisposal();
                    break;
                case "assets_inspection":
                    OpenAssetInspection();
                    break;
                case "assets_attachments":
                    OpenAssetAttachments();
                    break;
                case "assets_detail":
                    OpenAssetDetailView();
                    break;
                case "assets_transfers":
                    OpenTransferHistory();
                    break;
                case "inv_items":
                    OpenInventoryItems();
                    break;
                case "inv_balances":
                    OpenStockBalances();
                    break;
                case "inv_low":
                    OpenLowStockAlerts();
                    break;
                case "inv_receive":
                    OpenReceiveStock();
                    break;
                case "inv_issue":
                    OpenIssueStock();
                    break;
                case "inv_adjust":
                    OpenStockAdjustment();
                    break;
                case "inv_transfer":
                    OpenStockTransfer();
                    break;
                case "inv_cycle_count":
                    OpenCycleCount();
                    break;
                case "inv_tx_history":
                    OpenStockTransactionHistory();
                    break;
                case "inv_warehouses":
                    OpenWarehouseManagement();
                    break;
                case "inv_locations":
                    OpenLocationManagement();
                    break;
                case "rep_asset_status":
                    OpenAssetStatusReport();
                    break;
                case "rep_asset_valuation":
                    OpenAssetValuationReport();
                    break;
                case "rep_dept_assets":
                    OpenDepartmentAssetReport();
                    break;
                case "rep_inv_valuation":
                    OpenInventoryValuationReport();
                    break;
                case "rep_depreciation":
                    OpenAssetDepreciationReport();
                    break;
                case "audit_log":
                    OpenAuditLog();
                    break;
                case "admin_dashboard":
                    OpenDashboard();
                    break;
                case "admin_users":
                    OpenUserManagement();
                    break;
                case "admin_roles":
                    OpenRoleManagement();
                    break;
                case "admin_depts":
                    OpenDepartmentManagement();
                    break;
                case "admin_suppliers":
                    OpenSupplierManagement();
                    break;
            }
        };
    }

    private void RefreshStatus()
    {
        _lblUser.Text = $"User: {_auth.CurrentUserName ?? "—"}";
        _lblRole.Text = $"Role: {_auth.CurrentRoleName ?? "—"}";
        _lblConn.Text = "● Connected";
    }

    /// <summary>
    /// Opens the asset registry list view inside a new tab page. Each tab gets
    /// its own DI scope so the scoped DbContext lives as long as the form does.
    /// </summary>
    private void OpenAssetList()
    {
        var scope = _scopeFactory.CreateScope();
        var form = scope.ServiceProvider.GetRequiredService<AssetListForm>();
        OpenInTab("Asset Registry", form, scope);
    }

    /// <summary>
    /// Opens the asset create/edit form inside a new tab page. Each tab gets
    /// its own DI scope.
    /// </summary>
    private void OpenAssetEdit(long? assetId)
    {
        var scope = _scopeFactory.CreateScope();
        var assetService = scope.ServiceProvider.GetRequiredService<IAssetService>();
        var refService = scope.ServiceProvider.GetRequiredService<IReferenceDataService>();
        var form = new AssetEditForm(assetService, refService, assetId);
        OpenInTab(assetId is null ? "New Asset" : $"Asset #{assetId}", form, scope);
    }

    /// <summary>
    /// Opens the inventory item catalog (SKU master records) in a new tab.
    /// </summary>
    private void OpenInventoryItems()
    {
        var scope = _scopeFactory.CreateScope();
        var invService = scope.ServiceProvider.GetRequiredService<IInventoryService>();
        var form = new InventoryItemListForm(invService);
        OpenInTab("Item Catalog", form, scope);
    }

    /// <summary>
    /// Opens the audit log viewer (read-only) in a new tab.
    /// </summary>
    private void OpenAuditLog()
    {
        var scope = _scopeFactory.CreateScope();
        var auditService = scope.ServiceProvider.GetRequiredService<IAuditService>();
        var form = new AuditLogViewerForm(auditService, _scopeFactory);
        OpenInTab("Audit Log", form, scope);
    }

    /// <summary>
    /// Opens the user management form in a new tab.
    /// ACCESS CONTROL: only the SystemAdministrator role may open this form.
    /// The check uses ICurrentSession.RoleName (string compare) AND the
    /// admin.users permission as a defence-in-depth — if either fails, the
    /// user is shown a clear "access denied" message and the form does not open.
    /// </summary>
    private void OpenUserManagement()
    {
        // Resolve the current session to check role + permission.
        // ICurrentSession is a singleton (AuthService instance), so reading it
        // is cheap and does not require a DB round-trip.
        var session = _services.GetService(typeof(MAIMS.Core.Abstractions.ICurrentSession))
            as MAIMS.Core.Abstractions.ICurrentSession;

        var roleName = session?.RoleName;
        var hasPermission = session?.HasPermission(MAIMS.Core.Enums.Permissions.UserManage) ?? false;

        // Only SystemAdministrator may manage users. Even if another role somehow
        // has admin.users permission, we still require the role to be SystemAdministrator.
        if (!string.Equals(roleName, "SystemAdministrator", StringComparison.OrdinalIgnoreCase) || !hasPermission)
        {
            MessageBox.Show(
                "Access denied.\n\n" +
                "User Management is restricted to the SystemAdministrator role.\n" +
                $"Your current role: {roleName ?? "(none)"}\n\n" +
                "If you believe this is an error, contact your system administrator.",
                "Permission denied",
                MessageBoxButtons.OK,
                MessageBoxIcon.Warning);
            return;
        }

        var scope = _scopeFactory.CreateScope();
        var form = new UserManagementForm(_scopeFactory);
        OpenInTab("User Management", form, scope);
    }

    /// <summary>
    /// Opens the role + permissions management form in a new tab.
    /// ACCESS CONTROL: only the SystemAdministrator role may open this form.
    /// Editing role permissions affects every user in the system, so it must
    /// be locked down to the IT administrator only.
    /// </summary>
    private void OpenRoleManagement()
    {
        var session = _services.GetService(typeof(MAIMS.Core.Abstractions.ICurrentSession))
            as MAIMS.Core.Abstractions.ICurrentSession;

        var roleName = session?.RoleName;
        var hasPermission = session?.HasPermission(MAIMS.Core.Enums.Permissions.RoleManage) ?? false;

        if (!string.Equals(roleName, "SystemAdministrator", StringComparison.OrdinalIgnoreCase) || !hasPermission)
        {
            MessageBox.Show(
                "Access denied.\n\n" +
                "Roles & Permissions management is restricted to the SystemAdministrator role.\n" +
                $"Your current role: {roleName ?? "(none)"}\n\n" +
                "Editing role permissions affects every user in the system.\n" +
                "If you believe this is an error, contact your system administrator.",
                "Permission denied",
                MessageBoxButtons.OK,
                MessageBoxIcon.Warning);
            return;
        }

        var scope = _scopeFactory.CreateScope();
        var form = new RoleManagementForm(_scopeFactory);
        OpenInTab("Roles & Permissions", form, scope);
    }

    /// <summary>
    /// Opens the department management form in a new tab.
    /// ACCESS CONTROL: only the SystemAdministrator role may open this form.
    /// Departments are organizational master data — changing them affects
    /// asset ownership, user assignments, and reporting structures.
    /// </summary>
    private void OpenDepartmentManagement()
    {
        var session = _services.GetService(typeof(MAIMS.Core.Abstractions.ICurrentSession))
            as MAIMS.Core.Abstractions.ICurrentSession;

        var roleName = session?.RoleName;
        var hasPermission = session?.HasPermission(MAIMS.Core.Enums.Permissions.DeptManage) ?? false;

        if (!string.Equals(roleName, "SystemAdministrator", StringComparison.OrdinalIgnoreCase) || !hasPermission)
        {
            MessageBox.Show(
                "Access denied.\n\n" +
                "Department management is restricted to the SystemAdministrator role.\n" +
                $"Your current role: {roleName ?? "(none)"}\n\n" +
                "Departments are organizational master data — changing them affects\n" +
                "asset ownership, user assignments, and reporting structures.\n" +
                "If you believe this is an error, contact your system administrator.",
                "Permission denied",
                MessageBoxButtons.OK,
                MessageBoxIcon.Warning);
            return;
        }

        var scope = _scopeFactory.CreateScope();
        var form = new DepartmentManagementForm(_scopeFactory);
        OpenInTab("Departments", form, scope);
    }

    /// <summary>
    /// Opens the stock balances per warehouse viewer in a new tab.
    /// </summary>
    private void OpenStockBalances()
    {
        var scope = _scopeFactory.CreateScope();
        var inv = scope.ServiceProvider.GetRequiredService<IInventoryService>();
        var form = new StockBalancesForm(inv, _scopeFactory);
        OpenInTab("Stock Balances", form, scope);
    }

    /// <summary>
    /// Opens the low-stock alerts viewer in a new tab.
    /// </summary>
    private void OpenLowStockAlerts()
    {
        var scope = _scopeFactory.CreateScope();
        var inv = scope.ServiceProvider.GetRequiredService<IInventoryService>();
        var form = new LowStockAlertsForm(inv);
        OpenInTab("Low Stock Alerts", form, scope);
    }

    /// <summary>
    /// Opens the stock receipt (Stock-In) form in a new tab.
    /// </summary>
    private void OpenReceiveStock()
    {
        var scope = _scopeFactory.CreateScope();
        var inv = scope.ServiceProvider.GetRequiredService<IInventoryService>();
        var form = new ReceiveStockForm(inv, _scopeFactory);
        OpenInTab("Receive Stock", form, scope);
    }

    /// <summary>
    /// Opens the stock issue (Stock-Out) form in a new tab.
    /// </summary>
    private void OpenIssueStock()
    {
        var scope = _scopeFactory.CreateScope();
        var inv = scope.ServiceProvider.GetRequiredService<IInventoryService>();
        var form = new IssueStockForm(inv, _scopeFactory);
        OpenInTab("Issue Stock", form, scope);
    }

    /// <summary>
    /// Opens the recent transfers history in a new tab.
    /// </summary>
    private void OpenTransferHistory()
    {
        var scope = _scopeFactory.CreateScope();
        var form = new PendingTransfersForm(_scopeFactory);
        OpenInTab("Recent Transfers", form, scope);
    }

    /// <summary>
    /// Opens the stock adjustment (cycle count) form in a new tab.
    /// </summary>
    private void OpenStockAdjustment()
    {
        var scope = _scopeFactory.CreateScope();
        var inv = scope.ServiceProvider.GetRequiredService<IInventoryService>();
        var form = new StockAdjustmentForm(inv, _scopeFactory);
        OpenInTab("Stock Adjustment", form, scope);
    }

    /// <summary>
    /// Opens the inter-warehouse stock transfer form in a new tab.
    /// </summary>
    private void OpenStockTransfer()
    {
        var scope = _scopeFactory.CreateScope();
        var inv = scope.ServiceProvider.GetRequiredService<IInventoryService>();
        var form = new StockTransferForm(inv, _scopeFactory);
        OpenInTab("Transfer Stock", form, scope);
    }

    /// <summary>
    /// Opens the warehouse management form in a new tab.
    /// </summary>
    private void OpenWarehouseManagement()
    {
        var scope = _scopeFactory.CreateScope();
        var form = new WarehouseManagementForm(_scopeFactory);
        OpenInTab("Warehouses", form, scope);
    }

    /// <summary>
    /// Opens the location management form in a new tab.
    /// </summary>
    private void OpenLocationManagement()
    {
        var scope = _scopeFactory.CreateScope();
        var form = new LocationManagementForm(_scopeFactory);
        OpenInTab("Locations", form, scope);
    }

    /// <summary>Asset disposal workflow form.</summary>
    private void OpenAssetDisposal()
    {
        var scope = _scopeFactory.CreateScope();
        var assetSvc = scope.ServiceProvider.GetRequiredService<IAssetService>();
        var refSvc = scope.ServiceProvider.GetRequiredService<IReferenceDataService>();
        var form = new AssetDisposalForm(assetSvc, refSvc, _scopeFactory);
        OpenInTab("Dispose Asset", form, scope);
    }

    /// <summary>Asset transfer (between departments / custodians / locations) form.</summary>
    private void OpenAssetTransfer()
    {
        var scope = _scopeFactory.CreateScope();
        var assetSvc = scope.ServiceProvider.GetRequiredService<IAssetService>();
        var refSvc = scope.ServiceProvider.GetRequiredService<IReferenceDataService>();
        var form = new AssetTransferForm(assetSvc, refSvc, _scopeFactory);
        OpenInTab("Transfer Asset", form, scope);
    }

    /// <summary>Asset status distribution report (counts per status).</summary>
    private void OpenAssetStatusReport()
    {
        var scope = _scopeFactory.CreateScope();
        var form = new AssetStatusReportForm(_scopeFactory);
        OpenInTab("Asset Status Distribution", form, scope);
    }

    /// <summary>Asset valuation report (sum of cost/book value per department).</summary>
    private void OpenAssetValuationReport()
    {
        var scope = _scopeFactory.CreateScope();
        var form = new AssetValuationReportForm(_scopeFactory);
        OpenInTab("Asset Valuation by Department", form, scope);
    }

    /// <summary>Inventory valuation report (sum of unit_cost * on_hand per item).</summary>
    private void OpenInventoryValuationReport()
    {
        var scope = _scopeFactory.CreateScope();
        var form = new InventoryValuationReportForm(_scopeFactory);
        OpenInTab("Inventory Valuation", form, scope);
    }

    /// <summary>Department asset count report.</summary>
    private void OpenDepartmentAssetReport()
    {
        var scope = _scopeFactory.CreateScope();
        var form = new DepartmentAssetReportForm(_scopeFactory);
        OpenInTab("Assets by Department", form, scope);
    }

    /// <summary>Stock transaction history form.</summary>
    private void OpenStockTransactionHistory()
    {
        var scope = _scopeFactory.CreateScope();
        var form = new StockTransactionHistoryForm(_scopeFactory);
        OpenInTab("Stock Transaction History", form, scope);
    }

    /// <summary>Asset inspection (condition rating) form.</summary>
    private void OpenAssetInspection()
    {
        var scope = _scopeFactory.CreateScope();
        var form = new AssetInspectionForm(_scopeFactory);
        OpenInTab("Asset Inspection", form, scope);
    }

    /// <summary>Asset attachment manager (upload/download/delete files).</summary>
    private void OpenAssetAttachments()
    {
        var scope = _scopeFactory.CreateScope();
        var form = new AssetAttachmentForm(_scopeFactory);
        OpenInTab("Asset Attachments", form, scope);
    }

    /// <summary>Asset detail view — prompts for asset ID, then shows lifecycle + attachments.</summary>
    private void OpenAssetDetailView()
    {
        var idStr = PromptDialog.Show(this, "Asset Detail",
            "Enter Asset ID to view details:", "1");
        if (string.IsNullOrWhiteSpace(idStr) || !long.TryParse(idStr, out var id) || id <= 0)
        {
            if (!string.IsNullOrWhiteSpace(idStr))
                MessageBox.Show("Please enter a valid numeric Asset ID.", "Invalid input",
                    MessageBoxButtons.OK, MessageBoxIcon.Warning);
            return;
        }

        var scope = _scopeFactory.CreateScope();
        var form = new AssetDetailForm(_scopeFactory, id);
        OpenInTab($"Asset Detail #{id}", form, scope);
    }

    /// <summary>Cycle count (physical inventory) form.</summary>
    private void OpenCycleCount()
    {
        var scope = _scopeFactory.CreateScope();
        var form = new CycleCountForm(_scopeFactory);
        OpenInTab("Cycle Count", form, scope);
    }

    /// <summary>Change password (self-service) form — modal dialog.</summary>
    private void OpenChangePassword()
    {
        using var form = new ChangePasswordForm(_scopeFactory);
        form.ShowDialog(this);
    }

    /// <summary>
    /// Shows a detailed read-only account information dialog.
    /// Pulls all identity fields from the singleton ICurrentSession (AuthService),
    /// which was hydrated at login. No DB round-trip needed.
    /// </summary>
    private void ShowAccountDetails()
    {
        // Resolve ICurrentSession from the DI container (singleton AuthService).
        var session = _services.GetService(typeof(MAIMS.Core.Abstractions.ICurrentSession))
            as MAIMS.Core.Abstractions.ICurrentSession;

        if (session is null)
        {
            MessageBox.Show("Session information is not available.", "Account Details",
                MessageBoxButtons.OK, MessageBoxIcon.Warning);
            return;
        }

        // Build a custom dialog (not a MessageBox) so we can format the info
        // as a clean label/value grid with proper alignment and styling.
        using var dlg = new Form
        {
            Text = "Account Details",
            FormBorderStyle = FormBorderStyle.FixedDialog,
            MaximizeBox = false,
            MinimizeBox = false,
            StartPosition = FormStartPosition.CenterParent,
            ClientSize = new Size(420, 380),  // 320 → 380: room for Close button below the 10 rows
            BackColor = MaimsTheme.Background,
            Font = MaimsTheme.Body
        };

        // Header
        var lblHeader = new Label
        {
            Text = "Account Details",
            Font = MaimsTheme.Heading,
            ForeColor = MaimsTheme.Primary,
            Location = new Point(16, 12),
            AutoSize = true
        };
        dlg.Controls.Add(lblHeader);

        // Build the label/value rows. Each row: label (left, 130px) + value (right, fill).
        var rows = new (string Label, string Value)[]
        {
            ("Username",        session.UserName ?? "—"),
            ("Full Name",       GetCurrentUserFullName() ?? "—"),
            ("Email",           session.Email ?? "—"),
            ("Role",            session.RoleName ?? "—"),
            ("Department",      session.DepartmentName ?? "—"),
            ("User ID",         session.UserId?.ToString() ?? "—"),
            ("Department ID",   session.DepartmentId?.ToString() ?? "—"),
            ("Last Login",      session.LastLoginAt?.ToLocalTime().ToString("yyyy-MM-dd HH:mm:ss") ?? "—"),
            ("Machine Name",    session.MachineName ?? "—"),
            ("Session Started", DateTime.Now.ToString("yyyy-MM-dd HH:mm:ss"))
        };

        int y = 44;
        foreach (var (label, value) in rows)
        {
            var lbl = new Label
            {
                Text = label + ":",
                Location = new Point(16, y),
                Size = new Size(120, 20),
                Font = MaimsTheme.Body,
                ForeColor = MaimsTheme.TextSecondary,
                TextAlign = ContentAlignment.MiddleLeft
            };
            var val = new Label
            {
                Text = value,
                Location = new Point(140, y),
                Size = new Size(264, 20),
                Font = new Font("Segoe UI", 9F, FontStyle.Bold),
                ForeColor = MaimsTheme.TextPrimary,
                TextAlign = ContentAlignment.MiddleLeft
            };
            dlg.Controls.Add(lbl);
            dlg.Controls.Add(val);
            y += 24;
        }

        // Close button at the bottom. y now points just past the last row (284).
        // Add 16px breathing room → button at y=300, height 36 → ends at 336.
        // Form client height is 380 → 44px slack under the button. No overlap.
        var btnClose = MaimsTheme.CreateButton("Close", primary: true);
        btnClose.Location = new Point(160, y + 16);
        btnClose.Click += (_, _) => dlg.Close();
        dlg.Controls.Add(btnClose);
        dlg.AcceptButton = btnClose;

        dlg.ShowDialog(this);
    }

    /// <summary>
    /// Resolves the current user's full Name (not username) from the DB.
    /// Used by ShowAccountDetails — the session only caches UserName (login),
    /// but the user's display Name is needed for the account dialog.
    /// </summary>
    private string? GetCurrentUserFullName()
    {
        try
        {
            using var scope = _scopeFactory.CreateScope();
            var ctx = scope.ServiceProvider.GetRequiredService<MAIMS.Data.MaimsDbContext>();
            var session = _services.GetService(typeof(MAIMS.Core.Abstractions.ICurrentSession))
                as MAIMS.Core.Abstractions.ICurrentSession;
            if (session?.UserId is long uid)
            {
                return ctx.Users.AsNoTracking().Where(u => u.Id == uid).Select(u => u.Name).FirstOrDefault();
            }
        }
        catch { /* best-effort — return null if DB unavailable */ }
        return null;
    }

    /// <summary>Dashboard / overview form with key metrics.</summary>
    private void OpenDashboard()
    {
        var scope = _scopeFactory.CreateScope();
        var form = new DashboardForm(_scopeFactory);
        OpenInTab("Dashboard", form, scope);
    }

    /// <summary>Supplier management form.</summary>
    private void OpenSupplierManagement()
    {
        var scope = _scopeFactory.CreateScope();
        var form = new SupplierManagementForm(_scopeFactory);
        OpenInTab("Suppliers", form, scope);
    }

    /// <summary>Asset depreciation report form.</summary>
    private void OpenAssetDepreciationReport()
    {
        var scope = _scopeFactory.CreateScope();
        var form = new AssetDepreciationReportForm(_scopeFactory);
        OpenInTab("Asset Depreciation Report", form, scope);
    }

    /// <summary>
    /// Embeds a form as a non-top-level control inside a new tab page.
    /// The associated DI scope is disposed when the tab page is closed,
    /// which disposes the scoped DbContext that backs the form's services.
    /// </summary>
    private void OpenInTab(string title, Form innerForm, IServiceScope scope)
    {
        var page = new TabPage(title);
        innerForm.TopLevel = false;
        innerForm.FormBorderStyle = FormBorderStyle.None;
        innerForm.Dock = DockStyle.Fill;
        page.Controls.Add(innerForm);
        page.Tag = scope;  // keep scope alive for the lifetime of the tab
        _workspace.TabPages.Add(page);
        _workspace.SelectedTab = page;
        innerForm.Show();

        // Dispose the DI scope (and thus the DbContext) when the tab closes.
        // This releases pooled connections back to the MySQL connection pool.
        page.Disposed += (s, e) => scope.Dispose();
    }

    /// <summary>
    /// Draws an NxN white user avatar: a solid white circle background with a dark-navy
    /// "head + shoulders" silhouette on top. Tuned proportions (head 0.15R, shoulders
    /// 0.46W) so the silhouette looks balanced — earlier 0.18R/0.62W was too wide
    /// and made the icon look squashed horizontally.
    /// Used in the top-bar profile button (Image property) — no external image asset required.
    /// </summary>
    private static Bitmap CreateWhiteUserIcon(int size)
    {
        var bmp = new Bitmap(size, size, System.Drawing.Imaging.PixelFormat.Format32bppArgb);
        using var g = Graphics.FromImage(bmp);
        g.SmoothingMode = SmoothingMode.AntiAlias;
        g.Clear(Color.Transparent);

        // 1) Background: solid white circle (full opacity)
        using (var bgBrush = new SolidBrush(Color.White))
        {
            g.FillEllipse(bgBrush, 0, 0, size, size);
        }

        // 2) Inner silhouette: dark-navy head + shoulders.
        //    Proportions tuned for a balanced Material Design person icon:
        //      • Head radius    = 0.15 * size   (smaller, was 0.18)
        //      • Head center Y  = 0.36 * size   (slightly lower, more headroom above)
        //      • Shoulder width = 0.46 * size   (narrower, was 0.62 — fixes "too wide")
        //      • Shoulder height= 0.42 * size
        //      • Shoulder top   = 0.60 * size
        using (var personBrush = new SolidBrush(MaimsTheme.PrimaryDark))
        {
            // Head: centered horizontally, upper third.
            float headR  = size * 0.15f;
            float headCx = size * 0.50f;
            float headCy = size * 0.36f;
            g.FillEllipse(personBrush, headCx - headR, headCy - headR, headR * 2, headR * 2);

            // Shoulders: a wide ellipse whose bottom half is clipped by the
            // background circle, producing a clean dome silhouette below the head.
            float bodyW   = size * 0.46f;
            float bodyH   = size * 0.42f;
            float bodyCx  = size * 0.50f;
            float bodyTop = size * 0.60f;
            g.FillEllipse(personBrush, bodyCx - bodyW / 2, bodyTop - bodyH / 2, bodyW, bodyH);
        }

        // 3) Subtle dark-navy ring around the avatar for a polished look.
        using (var ringPen = new Pen(MaimsTheme.PrimaryDark, size * 0.035f))
        {
            float pad = size * 0.025f;
            g.DrawEllipse(ringPen, pad, pad, size - 2 * pad, size - 2 * pad);
        }

        return bmp;
    }
}
