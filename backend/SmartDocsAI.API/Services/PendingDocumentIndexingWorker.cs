using SmartDocsAI.API.Data;
using SmartDocsAI.API.Interfaces;

namespace SmartDocsAI.API.Services;

public sealed class PendingDocumentIndexingWorker : BackgroundService
{
    private readonly IServiceScopeFactory _scopeFactory;
    private readonly ILogger<PendingDocumentIndexingWorker> _logger;
    private readonly TimeSpan _pollInterval;
    private readonly TimeSpan _leaseTimeout;
    private readonly int _batchSize;

    public PendingDocumentIndexingWorker(
        IServiceScopeFactory scopeFactory,
        IConfiguration configuration,
        ILogger<PendingDocumentIndexingWorker> logger)
    {
        _scopeFactory = scopeFactory;
        _logger = logger;
        _pollInterval = TimeSpan.FromSeconds(Math.Clamp(
            configuration.GetValue<int?>("DocumentIndexingSettings:PollIntervalSeconds") ?? 2,
            1,
            60));
        // Heartbeat sayesinde süre kısa tutulabilir: çöken bir worker'ın belgesi
        // dakikalar içinde devralınır, çalışan bir worker'ınki ise devralınmaz.
        _leaseTimeout = TimeSpan.FromMinutes(Math.Clamp(
            configuration.GetValue<int?>("DocumentIndexingSettings:LeaseTimeoutMinutes") ?? 5,
            1,
            240));
        _batchSize = Math.Clamp(
            configuration.GetValue<int?>("DocumentIndexingSettings:BatchSize") ?? 2,
            1,
            20);

        var heartbeat = TimeSpan.FromSeconds(
            configuration.GetValue<int?>("DocumentIndexingSettings:HeartbeatSeconds") ?? 30);
        if (heartbeat * 3 > _leaseTimeout)
        {
            _logger.LogWarning(
                "DocumentIndexingSettings: heartbeat ({Heartbeat}) should be at most a third of the lease timeout ({LeaseTimeout}); healthy workers may lose their lease.",
                heartbeat,
                _leaseTimeout);
        }
    }

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        await ProcessBatchSafelyAsync(stoppingToken);

        using var timer = new PeriodicTimer(_pollInterval);
        while (await timer.WaitForNextTickAsync(stoppingToken))
        {
            await ProcessBatchSafelyAsync(stoppingToken);
        }
    }

    private async Task ProcessBatchSafelyAsync(CancellationToken cancellationToken)
    {
        try
        {
            await using var scope = _scopeFactory.CreateAsyncScope();
            var context = scope.ServiceProvider.GetRequiredService<AppDbContext>();
            var indexingService =
                scope.ServiceProvider.GetRequiredService<IDocumentIndexingService>();

            var documentIds = await DocumentProcessingLeases.FindClaimableAsync(
                context,
                DateTime.UtcNow,
                _leaseTimeout,
                _batchSize,
                cancellationToken);

            foreach (var documentId in documentIds)
            {
                var leaseId = await DocumentProcessingLeases.TryClaimAsync(
                    context,
                    documentId,
                    DateTime.UtcNow,
                    _leaseTimeout,
                    cancellationToken);

                if (leaseId is { } claimedLease)
                {
                    await indexingService.ProcessAsync(documentId, claimedLease, cancellationToken);
                    context.ChangeTracker.Clear();
                }
            }
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
            throw;
        }
        catch (Exception exception)
        {
            _logger.LogError(exception, "Pending document indexing batch failed.");
        }
    }
}
