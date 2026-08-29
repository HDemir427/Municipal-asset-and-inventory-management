using MAIMS.Data;
using MAIMS.Services.Attachments;
using MAIMS.WinUI.Theming;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;

namespace MAIMS.WinUI.Forms;

/// <summary>
/// Asset attachment manager. Lists attachments for a selected asset and
/// allows uploading new files (purchase receipts, photos, manuals) and
/// deleting existing ones.
/// </summary>
public class AssetAttachmentForm : Form
{
    private readonly IServiceScopeFactory _scopeFactory;
    private readonly ComboBox _cmbAsset;
    private readonly DataGridView _grid;
    private readonly Button _btnUpload;
    private readonly Button _btnDownload;
    private readonly Button _btnDelete;
    private readonly Button _btnRefresh;
    private readonly Button _btnClose;
    private readonly Label _statusLabel;
    private bool _initializing;    // suppresses SelectedIndexChanged during LoadAssetsAsync
    private bool _isLoading;      // re-entrancy guard for LoadAttachmentsAsync

    public AssetAttachmentForm(IServiceScopeFactory scopeFactory)
    {
        _scopeFactory = scopeFactory;
        Text = "Asset Attachments";
        FormBorderStyle = FormBorderStyle.None;
        Dock = DockStyle.Fill;
        BackColor = MaimsTheme.Background;

        var pnlTop = new Panel { Dock = DockStyle.Top, Height = 50, BackColor = MaimsTheme.Surface, Padding = new Padding(8) };
        var lblAsset = new Label { Text = "Asset:", Location = new Point(8, 14), AutoSize = true, Font = MaimsTheme.Body };
        _cmbAsset = new ComboBox
        {
            Location = new Point(60, 12),
            Size = new Size(350, 24),
            DropDownStyle = ComboBoxStyle.DropDownList,
            Font = MaimsTheme.Body
        };
        _cmbAsset.SelectedIndexChanged += async (_, _) =>
        {
            if (_initializing) return;
            await LoadAttachmentsAsync();
        };

        _btnUpload = MaimsTheme.CreateButton("Upload…", primary: true);
        _btnDownload = MaimsTheme.CreateButton("Download…");
        _btnDelete = MaimsTheme.CreateButton("Delete…");
        _btnRefresh = MaimsTheme.CreateButton("Refresh");
        _btnClose = MaimsTheme.CreateButton("Close");
        MaimsTheme.LayoutButtons(420, 10, 8, _btnUpload, _btnDownload, _btnDelete, _btnRefresh, _btnClose);
        _btnClose.Click += (_, _) =>
        {
            if (Parent is TabPage page && page.Parent is TabControl tc)
                tc.TabPages.Remove(page);
            else
                Close();
        };

        pnlTop.Controls.AddRange(new Control[]
        {
            lblAsset, _cmbAsset,
            _btnUpload, _btnDownload, _btnDelete, _btnRefresh, _btnClose
        });

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
        AddCol("OriginalFileName", "File Name", 220);
        AddCol("FileType", "Type", 130);
        AddCol("FileSizeBytes", "Size (bytes)", 120, "N0");
        AddCol("Description", "Description", 250);
        AddCol("CreatedAt", "Uploaded At", 140);

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

        _btnUpload.Click += async (_, _) => await UploadAsync();
        _btnDownload.Click += async (_, _) => await DownloadAsync();
        _btnDelete.Click += async (_, _) => await DeleteAsync();
        _btnRefresh.Click += async (_, _) => await LoadAttachmentsAsync();
        Load += async (_, _) => await LoadAssetsAsync();
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

    private async Task LoadAssetsAsync()
    {
        _initializing = true;
        try
        {
            using var scope = _scopeFactory.CreateScope();
            var ctx = scope.ServiceProvider.GetRequiredService<MaimsDbContext>();
            var assets = await ctx.Assets.AsNoTracking().OrderBy(a => a.AssetCode)
                .Select(a => new { a.Id, Display = $"{a.AssetCode} — {a.Name}" })
                .ToListAsync();
            _cmbAsset.DisplayMember = "Display";
            _cmbAsset.ValueMember = "Id";
            _cmbAsset.DataSource = assets;
            if (assets.Count > 0)
            {
                _cmbAsset.SelectedIndex = 0;
                // Manually trigger the first load (SelectedIndexChanged was suppressed).
                await LoadAttachmentsAsync();
            }
        }
        catch (Exception ex)
        {
            MessageBox.Show("Failed to load assets: " + ex.Message, "Error",
                MessageBoxButtons.OK, MessageBoxIcon.Error);
        }
        finally
        {
            _initializing = false;
        }
    }

    private async Task LoadAttachmentsAsync()
    {
        // Re-entrancy guard: prevents concurrent calls if SelectedIndexChanged
        // fires multiple times in rapid succession (e.g. during DataSource assignment).
        if (_isLoading) return;
        _isLoading = true;
        try
        {
            if (_cmbAsset.SelectedValue is not long assetId || assetId <= 0) return;
            _statusLabel.Text = "Loading…";
            using var scope = _scopeFactory.CreateScope();
            var attachSvc = new AssetAttachmentService(_scopeFactory);
            var attachments = await attachSvc.ListAsync(assetId);
            _grid.DataSource = null;  // force DataGridView to rebind
            _grid.DataSource = attachments.ToList();
            _statusLabel.Text = $"{attachments.Count} attachment(s) for asset #{assetId}.";
        }
        catch (Exception ex)
        {
            MessageBox.Show("Failed to load attachments: " + ex.Message, "Error",
                MessageBoxButtons.OK, MessageBoxIcon.Error);
        }
        finally
        {
            _isLoading = false;
        }
    }

    private async Task UploadAsync()
    {
        if (_cmbAsset.SelectedValue is not long assetId || assetId <= 0)
        {
            MessageBox.Show("Please select an asset first.", "No asset",
                MessageBoxButtons.OK, MessageBoxIcon.Information);
            return;
        }

        using var ofd = new OpenFileDialog
        {
            Title = "Select file to attach",
            Filter = "All files (*.*)|*.*|" +
                     "PDF files (*.pdf)|*.pdf|" +
                     "Images (*.jpg;*.jpeg;*.png;*.gif;*.bmp)|*.jpg;*.jpeg;*.png;*.gif;*.bmp|" +
                     "Word documents (*.doc;*.docx)|*.doc;*.docx|" +
                     "Excel files (*.xls;*.xlsx)|*.xls;*.xlsx"
        };
        if (ofd.ShowDialog() != DialogResult.OK) return;

        var description = PromptDialog.Show(this, "Description",
            "Optional description for this attachment:", "");
        if (description == null) return;  // user cancelled

        try
        {
            _statusLabel.Text = "Uploading…";

            // Get current user ID from session
            long? uploadedBy = null;
            using (var scope = _scopeFactory.CreateScope())
            {
                var session = scope.ServiceProvider.GetRequiredService<MAIMS.Core.Abstractions.ICurrentSession>();
                uploadedBy = session.UserId;
            }

            var attachSvc = new AssetAttachmentService(_scopeFactory);
            await attachSvc.UploadAsync(
                assetId: assetId,
                sourceFilePath: ofd.FileName,
                originalFileName: Path.GetFileName(ofd.FileName),
                description: string.IsNullOrWhiteSpace(description) ? null : description,
                uploadedByUserId: uploadedBy);

            MessageBox.Show("File uploaded successfully.", "Upload complete",
                MessageBoxButtons.OK, MessageBoxIcon.Information);
            await LoadAttachmentsAsync();
        }
        catch (Exception ex)
        {
            MessageBox.Show("Upload failed: " + ex.Message, "Error",
                MessageBoxButtons.OK, MessageBoxIcon.Error);
        }
    }

    private async Task DownloadAsync()
    {
        if (_grid.CurrentRow?.DataBoundItem is not MAIMS.Core.Entities.AssetAttachment att)
        {
            MessageBox.Show("Please select an attachment to download.", "No selection",
                MessageBoxButtons.OK, MessageBoxIcon.Information);
            return;
        }

        try
        {
            var attachSvc = new AssetAttachmentService(_scopeFactory);
            var (filePath, originalName, fileType) = await attachSvc.GetDownloadInfoAsync(att.Id);

            if (!File.Exists(filePath))
            {
                MessageBox.Show("The file no longer exists on disk.", "File missing",
                    MessageBoxButtons.OK, MessageBoxIcon.Warning);
                return;
            }

            using var sfd = new SaveFileDialog
            {
                Title = "Save attachment as",
                FileName = originalName,
                Filter = "All files (*.*)|*.*"
            };
            if (sfd.ShowDialog() == DialogResult.OK)
            {
                File.Copy(filePath, sfd.FileName, overwrite: true);
                MessageBox.Show($"Saved to: {sfd.FileName}", "Download complete",
                    MessageBoxButtons.OK, MessageBoxIcon.Information);
            }
        }
        catch (Exception ex)
        {
            MessageBox.Show("Download failed: " + ex.Message, "Error",
                MessageBoxButtons.OK, MessageBoxIcon.Error);
        }
    }

    private async Task DeleteAsync()
    {
        if (_grid.CurrentRow?.DataBoundItem is not MAIMS.Core.Entities.AssetAttachment att)
        {
            MessageBox.Show("Please select an attachment to delete.", "No selection",
                MessageBoxButtons.OK, MessageBoxIcon.Information);
            return;
        }

        var confirm = MessageBox.Show(
            $"Delete attachment?\n\n  File: {att.OriginalFileName}\n  Size: {att.FileSizeBytes:N0} bytes\n\n" +
            "The file will be removed from disk AND the database. This cannot be undone.",
            "Confirm Delete Attachment",
            MessageBoxButtons.YesNo,
            MessageBoxIcon.Warning,
            MessageBoxDefaultButton.Button2);
        if (confirm != DialogResult.Yes) return;

        try
        {
            var attachSvc = new AssetAttachmentService(_scopeFactory);
            await attachSvc.DeleteAsync(att.Id);
            MessageBox.Show("Attachment deleted.", "Deleted",
                MessageBoxButtons.OK, MessageBoxIcon.Information);
            await LoadAttachmentsAsync();
        }
        catch (Exception ex)
        {
            MessageBox.Show("Delete failed: " + ex.Message, "Error",
                MessageBoxButtons.OK, MessageBoxIcon.Error);
        }
    }
}
