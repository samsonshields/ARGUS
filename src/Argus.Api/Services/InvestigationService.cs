using Argus.Core.DTOs;
using Argus.Core.Entities;
using Argus.Core.Enums;
using Argus.Core.Interfaces;
namespace Argus.Api.Services;

public sealed class InvestigationService(IInvestigationRepository repository)
{
    public async Task<IReadOnlyList<InvestigationResponse>> ListAsync(string? search, InvestigationStatus? status, CancellationToken ct)
        => (await repository.ListAsync(search, status, ct)).Select(x => x.ToResponse()).ToList();
    public async Task<InvestigationResponse?> GetAsync(Guid id, CancellationToken ct)
        => (await repository.FindAsync(id, ct))?.ToResponse();
    public async Task<InvestigationResponse> CreateAsync(InvestigationRequest request, CancellationToken ct)
    {
        var entity = new Investigation();
        entity.Update(request);
        await repository.AddAsync(entity, ct);
        return entity.ToResponse();
    }
    public async Task<InvestigationResponse?> UpdateAsync(Guid id, InvestigationRequest request, CancellationToken ct)
    {
        var entity = await repository.FindAsync(id, ct);
        if (entity is null) return null;
        entity.Update(request);
        await repository.SaveAsync(ct);
        return entity.ToResponse();
    }
    public async Task<bool> DeleteAsync(Guid id, CancellationToken ct)
    {
        var entity = await repository.FindAsync(id, ct);
        if (entity is null) return false;
        await repository.DeleteAsync(entity, ct);
        return true;
    }
}
