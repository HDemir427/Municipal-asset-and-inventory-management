using MAIMS.Core.Interfaces;
using Microsoft.EntityFrameworkCore;

namespace MAIMS.Data.AssetCodeGeneration;

/// <summary>
/// Default asset code generator. Scheme: DEPT_CODE-CATEGORY_CODE-SEQ (5 digits).
/// Sequence is per (department, category) and derived from the highest existing
/// sequence number for that pair. Production should replace this with a SQL
/// sequence or a counter table locked under SERIALIZABLE for concurrency safety.
/// </summary>
public class SequentialAssetCodeGenerator : IAssetCodeGenerator
{
    private readonly DbContext _ctx;

    public SequentialAssetCodeGenerator(DbContext ctx)
    {
        _ctx = ctx;
    }

    public async Task<string> GenerateAsync(long departmentId, long categoryId, CancellationToken ct = default)
    {
        var dept = await _ctx.Set<MAIMS.Core.Entities.Department>()
            .AsNoTracking()
            .Where(d => d.Id == departmentId)
            .Select(d => new { d.Code })
            .FirstOrDefaultAsync(ct)
            ?? throw new InvalidOperationException($"Department {departmentId} not found.");

        var cat = await _ctx.Set<MAIMS.Core.Entities.AssetCategory>()
            .AsNoTracking()
            .Where(c => c.Id == categoryId)
            .Select(c => new { c.CategoryType })
            .FirstOrDefaultAsync(ct)
            ?? throw new InvalidOperationException($"Asset category {categoryId} not found.");

        var prefix = $"{dept.Code}-{cat.CategoryType.ToString().ToUpperInvariant()}";

        // Find max sequence for this prefix in the asset table.
        var lastCode = await _ctx.Set<MAIMS.Core.Entities.Asset>()
            .AsNoTracking()
            .Where(a => a.AssetCode.StartsWith(prefix + "-"))
            .Select(a => a.AssetCode)
            .ToListAsync(ct);

        int nextSeq = 1;
        if (lastCode.Count > 0)
        {
            var seqs = lastCode
                .Select(c => c.Substring(prefix.Length + 1))
                .Select(s => int.TryParse(s, out var n) ? n : 0)
                .ToList();
            if (seqs.Count > 0) nextSeq = seqs.Max() + 1;
        }

        return $"{prefix}-{nextSeq:D5}";
    }
}
