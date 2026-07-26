using Microsoft.EntityFrameworkCore;
using SmartDocsAI.API.Data;
using SmartDocsAI.API.Interfaces;

namespace SmartDocsAI.API.Services;

public sealed class PendingDocumentIndexingWorker : BackgroundService
{
    private static readonly string[] ActiveStatuses = ["Extracting", "Indexing"];

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
        _leaseTimeout = TimeSpan.FromMinutes(Math.Clamp(
            configuration.GetValue<int?>("DocumentIndexingSettings:LeaseTimeoutMinutes") ?? 30,
            5,
            240));
        _batchSize = Math.Clamp(
            configuration.GetValue<int?>("DocumentIndexingSettings:BatchSize") ?? 2,
            1,
            20);
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
            var now = DateTime.UtcNow;
            var staleBefore = now - _leaseTimeout;

            var documentIds = await context.Documents
                .AsNoTracking()
                .Where(document =>
                    document.IndexingStatus == "Pending" ||
                    (document.IndexingStatus == "RetryWaiting" &&
                     document.NextProcessingAttemptAt <= now) ||
                    (ActiveStatuses.Contains(document.IndexingStatus) &&
                     document.ProcessingStartedAt < staleBefore))
                .OrderBy(document => document.UploadDate)
                .Select(document => document.Id)
                .Take(_batchSize)
                .ToListAsync(cancellationToken);

            foreach (var documentId in documentIds)
            {
                var claimedAt = DateTime.UtcNow;
                var claimed = await context.Documents
                    .Where(document =>
                        document.Id == documentId &&
                        (document.IndexingStatus == "Pending" ||
                         (document.IndexingStatus == "RetryWaiting" &&
                          document.NextProcessingAttemptAt <= claimedAt) ||
                         (ActiveStatuses.Contains(document.IndexingStatus) &&
                          document.ProcessingStartedAt < staleBefore)))
                    .ExecuteUpdateAsync(
                        setters => setters
                            .SetProperty(document => document.IndexingStatus, "Extracting")
                            .SetProperty(document => document.IndexingError, (string?)null)
                            .SetProperty(document => document.NextProcessingAttemptAt, (DateTime?)null)
                            .SetProperty(document => document.ProcessingStartedAt, claimedAt),
                        cancellationToken);

                if (claimed == 1)
                {
                    await indexingService.ProcessAsync(documentId, cancellationToken);
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
