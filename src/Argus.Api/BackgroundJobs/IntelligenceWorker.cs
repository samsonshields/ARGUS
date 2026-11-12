using Argus.Api.Services;
using Argus.Infrastructure.ExternalServices;
using Microsoft.Extensions.Options;

namespace Argus.Api.BackgroundJobs;

public sealed class IntelligenceWorker(IServiceScopeFactory scopes, IOptions<IntelligenceOptions> options,
    ILogger<IntelligenceWorker> logger) : BackgroundService
{
    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        if (!options.Value.WorkerEnabled) return;
        var recover = true;
        while (!stoppingToken.IsCancellationRequested)
        {
            try
            {
                using var scope = scopes.CreateScope();
                if (recover)
                {
                    await scope.ServiceProvider.GetRequiredService<ProcessingJobService>().RecoverAsync(stoppingToken);
                    recover = false;
                }
                if (await scope.ServiceProvider.GetRequiredService<ProcessingRunner>().RunNextAsync(stoppingToken)) continue;
            }
            catch (OperationCanceledException) when (stoppingToken.IsCancellationRequested) { break; }
            catch (Exception ex)
            {
                recover = true;
                logger.LogError("Intelligence worker failure category {FailureType}; pending work remains persisted", ex.GetType().Name);
            }
            try { await Task.Delay(500, stoppingToken); }
            catch (OperationCanceledException) when (stoppingToken.IsCancellationRequested) { break; }
        }
    }
}
