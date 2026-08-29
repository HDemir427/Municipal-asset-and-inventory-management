using MAIMS.Core.Entities;
using MAIMS.Core.Interfaces;
using MAIMS.Data;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;

namespace MAIMS.Services.Attachments;

/// <summary>
/// Service for uploading, listing, and deleting asset attachments.
/// Files are stored on disk under a configurable root (default: ./attachments).
/// The DB stores a relative path reference + metadata.
/// </summary>
public class AssetAttachmentService
{
    private readonly IServiceScopeFactory _scopeFactory;
    private readonly string _attachmentRoot;

    public AssetAttachmentService(IServiceScopeFactory scopeFactory, string? attachmentRoot = null)
    {
        _scopeFactory = scopeFactory;
        _attachmentRoot = attachmentRoot ?? Path.Combine(AppContext.BaseDirectory, "attachments");
        Directory.CreateDirectory(_attachmentRoot);
    }

    /// <summary>Lists all attachments for an asset.</summary>
    public async Task<IReadOnlyList<AssetAttachment>> ListAsync(long assetId, CancellationToken ct = default)
    {
        using var scope = _scopeFactory.CreateScope();
        var ctx = scope.ServiceProvider.GetRequiredService<MaimsDbContext>();
        return await ctx.AssetAttachments.AsNoTracking()
            .Where(a => a.AssetId == assetId)
            .OrderByDescending(a => a.CreatedAt)
            .ToListAsync(ct);
    }

    /// <summary>
    /// Uploads a file for an asset. Copies the file to the attachment root,
    /// then creates an AssetAttachment record in the DB.
    /// </summary>
    public async Task<AssetAttachment> UploadAsync(
        long assetId,
        string sourceFilePath,
        string? originalFileName = null,
        string? description = null,
        long? uploadedByUserId = null,
        CancellationToken ct = default)
    {
        if (!File.Exists(sourceFilePath))
            throw new FileNotFoundException($"Source file not found: {sourceFilePath}");

        var fileName = originalFileName ?? Path.GetFileName(sourceFilePath);
        var fileInfo = new FileInfo(sourceFilePath);
        var fileType = GetMimeType(fileName);

        // Generate a unique destination filename to avoid collisions
        var ext = Path.GetExtension(fileName);
        var destFileName = $"{assetId}_{Guid.NewGuid():N}{ext}";
        var destPath = Path.Combine(_attachmentRoot, destFileName);

        // Copy file
        File.Copy(sourceFilePath, destPath, overwrite: true);

        // Save DB record
        using var scope = _scopeFactory.CreateScope();
        var ctx = scope.ServiceProvider.GetRequiredService<MaimsDbContext>();

        var attachment = new AssetAttachment
        {
            AssetId = assetId,
            FilePath = destPath,  // store full path for now (could be relative in production)
            FileType = fileType,
            OriginalFileName = fileName,
            FileSizeBytes = fileInfo.Length,
            Description = description,
            CreatedBy = uploadedByUserId
        };

        ctx.AssetAttachments.Add(attachment);
        await ctx.SaveChangesAsync(ct);
        return attachment;
    }

    /// <summary>
    /// Deletes an attachment: removes the file from disk and the DB record.
    /// </summary>
    public async Task DeleteAsync(long attachmentId, CancellationToken ct = default)
    {
        using var scope = _scopeFactory.CreateScope();
        var ctx = scope.ServiceProvider.GetRequiredService<MaimsDbContext>();

        var attachment = await ctx.AssetAttachments.FirstOrDefaultAsync(a => a.Id == attachmentId, ct);
        if (attachment is null)
            throw new KeyNotFoundException($"Attachment {attachmentId} not found.");

        // Delete file from disk
        if (File.Exists(attachment.FilePath))
        {
            try { File.Delete(attachment.FilePath); }
            catch { /* best-effort delete; DB record is still removed */ }
        }

        ctx.AssetAttachments.Remove(attachment);
        await ctx.SaveChangesAsync(ct);
    }

    /// <summary>
    /// Returns the full path for downloading/viewing an attachment.
    /// </summary>
    public async Task<(string FilePath, string OriginalFileName, string FileType)> GetDownloadInfoAsync(long attachmentId, CancellationToken ct = default)
    {
        using var scope = _scopeFactory.CreateScope();
        var ctx = scope.ServiceProvider.GetRequiredService<MaimsDbContext>();

        var attachment = await ctx.AssetAttachments.AsNoTracking()
            .FirstOrDefaultAsync(a => a.Id == attachmentId, ct)
            ?? throw new KeyNotFoundException($"Attachment {attachmentId} not found.");

        return (attachment.FilePath, attachment.OriginalFileName ?? "file", attachment.FileType);
    }

    private static string GetMimeType(string fileName)
    {
        var ext = Path.GetExtension(fileName).ToLowerInvariant();
        return ext switch
        {
            ".pdf" => "application/pdf",
            ".jpg" or ".jpeg" => "image/jpeg",
            ".png" => "image/png",
            ".gif" => "image/gif",
            ".bmp" => "image/bmp",
            ".doc" => "application/msword",
            ".docx" => "application/vnd.openxmlformats-officedocument.wordprocessingml.document",
            ".xls" => "application/vnd.ms-excel",
            ".xlsx" => "application/vnd.openxmlformats-officedocument.spreadsheetml.sheet",
            ".txt" => "text/plain",
            ".zip" => "application/zip",
            _ => "application/octet-stream"
        };
    }
}
