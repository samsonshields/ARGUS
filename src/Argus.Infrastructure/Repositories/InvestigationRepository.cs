using Argus.Core.Entities;
using Argus.Core.Enums;
using Argus.Core.Interfaces;
using Argus.Infrastructure.Data;
using Microsoft.EntityFrameworkCore;
namespace Argus.Infrastructure.Repositories;

public sealed class InvestigationRepository(ArgusDbContext db) : IInvestigationRepository
{
    public async Task<IReadOnlyList<Investigation>> ListAsync(string? search, InvestigationStatus? status, CancellationToken ct)
    {
        var query = db.Investigations.AsNoTracking();
        if (!string.IsNullOrWhiteSpace(search))
        {
            var term = search.Trim().ToLowerInvariant();
            query = query.Where(x => x.Title.ToLower().Contains(term) || x.Description.ToLower().Contains(term)
                || (x.Region != null && x.Region.ToLower().Contains(term)));
        }
        if (status.HasValue) query = query.Where(x => x.Status == status);
        return await query.OrderByDescending(x => x.UpdatedAt).ThenBy(x => x.Id).ToListAsync(ct);
    }
    public Task<Investigation?> FindAsync(Guid id, CancellationToken ct) => db.Investigations.FirstOrDefaultAsync(x => x.Id == id, ct);
    public async Task AddAsync(Investigation investigation, CancellationToken ct)
    {
        db.Add(investigation);
        await db.SaveChangesAsync(ct);
    }
    public async Task SaveAsync(CancellationToken ct) => await db.SaveChangesAsync(ct);
    public async Task DeleteAsync(Investigation investigation, CancellationToken ct)
    {
        if (await db.Sources.AnyAsync(x => x.InvestigationId == investigation.Id, ct))
            throw new Argus.Core.Exceptions.SourceException(409, "This investigation has preserved sources. Archive it instead of deleting it.");
        db.Remove(investigation);
        try { await db.SaveChangesAsync(ct); }
        catch (DbUpdateException ex) when (ex.InnerException is Microsoft.Data.Sqlite.SqliteException { SqliteErrorCode: 19 })
        {
            throw new Argus.Core.Exceptions.SourceException(409, "This investigation has preserved records. Archive it instead of deleting it.");
        }
    }
}
