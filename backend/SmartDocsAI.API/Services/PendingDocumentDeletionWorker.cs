using Microsoft.EntityFrameworkCore;
using SmartDocsAI.API.Data;
using SmartDocsAI.API.Interfaces;

namespace SmartDocsAI.API.Services;

public sealed class PendingDocumentDeletionWorker : BackgroundService
{
    private readonly IServiceScopeFactory _scopeFactory;
    private readonly ILogger<PendingDocumentDeletionWorker> _logger;
    private readonly TimeSpan _retryInterval;
    private readonly int _batchSize;

    public PendingDocumentDeletionWorker(
        IServiceScopeFactory scopeFactory,
        IConfiguration configuration,
        ILogger<PendingDocumentDeletionWorker> logger)
    {
        _scopeFactory = scopeFactory;
        _logger = logger;
        _retryInterval = TimeSpan.FromSeconds(Math.Clamp(
            configuration.GetValue<int?>("DocumentDeletionSettings:RetryIntervalSeconds") ?? 30,
            5,
            3_600));
        _batchSize = Math.Clamp(
            configuration.GetValue<int?>("DocumentDeletionSettings:BatchSize") ?? 20,
            1,
            100);
    }

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        await ProcessBatchSafelyAsync(stoppingToken);

        using var timer = new PeriodicTimer(_retryInterval);
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
            var deletionService = scope.ServiceProvider.GetRequiredService<IDocumentDeletionService>();

            var documentIds = await context.Documents
                .AsNoTracking()
                .Where(document => document.IndexingStatus == "Deleting")
                .OrderBy(document => document.UploadDate)
                .Select(document => document.Id)
                .Take(_batchSize)
                .ToListAsync(cancellationToken);

            foreach (var documentId in documentIds)
            {
                try
                {
                    await deletionService.DeleteAsync(documentId, cancellationToken);
                }
                catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
                {
                    throw;
                }
                catch (Exception exception)
                {
                    _logger.LogWarning(
                        exception,
                        "Pending deletion for document {DocumentId} will be retried.",
                        documentId);

                    var error = LimitError(exception);
                    await context.Documents
                        .Where(document =>
                            document.Id == documentId && document.IndexingStatus == "Deleting")
                        .ExecuteUpdateAsync(
                            setters => setters.SetProperty(
                                document => document.IndexingError,
                                error),
                            cancellationToken);
                }
            }
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
            throw;
        }
        catch (Exception exception)
        {
            _logger.LogError(exception, "Pending document deletion batch failed.");
        }
    }

    private static string LimitError(Exception exception)
    {
        var message = exception.GetBaseException().Message;
        return message[..Math.Min(1_000, message.Length)];
    }
}
