using Argus.Core.Entities;
using Argus.Core.Enums;
namespace Argus.Core.Interfaces;

public interface IInvestigationRepository
{
    Task<IReadOnlyList<Investigation>> ListAsync(string? search, InvestigationStatus? status, CancellationToken ct);
    Task<Investigation?> FindAsync(Guid id, CancellationToken ct);
    Task AddAsync(Investigation investigation, CancellationToken ct);
    Task SaveAsync(CancellationToken ct);
    Task DeleteAsync(Investigation investigation, CancellationToken ct);
}
