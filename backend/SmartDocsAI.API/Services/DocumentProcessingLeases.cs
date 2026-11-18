using System.Linq.Expressions;
using Microsoft.EntityFrameworkCore;
using SmartDocsAI.API.Data;
using SmartDocsAI.API.Models;

namespace SmartDocsAI.API.Services;

/// <summary>
/// İndeksleme kuyruğundaki belgeler için sahiplik (lease) işlemleri.
/// </summary>
/// <remarks>
/// Sahiplenme tek bir koşullu UPDATE'tir: aynı belgeyi aynı anda isteyen
/// worker'lardan yalnızca birinin güncellemesi satır etkiler. Her sahiplenme yeni
/// bir <see cref="Document.ProcessingLeaseId"/> üretir; böylece sahipliği süre
/// aşımıyla kaybeden eski worker'ın sonraki yazmaları reddedilir.
/// </remarks>
public static class DocumentProcessingLeases
{
    public static Task<List<int>> FindClaimableAsync(
        AppDbContext context,
        DateTime now,
        TimeSpan leaseTimeout,
        int batchSize,
        CancellationToken cancellationToken) =>
        context.Documents
            .AsNoTracking()
            .Where(IsClaimable(now, now - leaseTimeout))
            .OrderBy(document => document.UploadDate)
            .Select(document => document.Id)
            .Take(batchSize)
            .ToListAsync(cancellationToken);

    public static async Task<Guid?> TryClaimAsync(
        AppDbContext context,
        int documentId,
        DateTime now,
        TimeSpan leaseTimeout,
        CancellationToken cancellationToken)
    {
        var leaseId = Guid.NewGuid();
        var claimed = await context.Documents
            .Where(document => document.Id == documentId)
            .Where(IsClaimable(now, now - leaseTimeout))
            .ExecuteUpdateAsync(
                setters => setters
                    .SetProperty(document => document.IndexingStatus, "Extracting")
                    .SetProperty(document => document.IndexingError, (string?)null)
                    .SetProperty(document => document.NextProcessingAttemptAt, (DateTime?)null)
                    .SetProperty(document => document.ProcessingStartedAt, now)
                    .SetProperty(document => document.ProcessingHeartbeatAt, now)
                    .SetProperty(document => document.ProcessingLeaseId, leaseId),
                cancellationToken);

        return claimed == 1 ? leaseId : null;
    }

    /// <summary>
    /// Düzgün kapanışta sahipliği hemen bırakır: belge, süre aşımını beklemeden
    /// bir sonraki worker tarafından alınabilsin diye kuyruğa geri döner.
    /// Yalnızca hâlâ bu lease'in sahibiysek etkili olur.
    /// </summary>
    public static Task<int> ReleaseAsync(
        AppDbContext context,
        int documentId,
        Guid leaseId,
        CancellationToken cancellationToken) =>
        context.Documents
            .Where(document => document.Id == documentId && document.ProcessingLeaseId == leaseId)
            .ExecuteUpdateAsync(
                setters => setters
                    .SetProperty(document => document.IndexingStatus, "Pending")
                    .SetProperty(document => document.ProcessingLeaseId, (Guid?)null)
                    .SetProperty(document => document.ProcessingHeartbeatAt, (DateTime?)null)
                    .SetProperty(document => document.ProcessingStartedAt, (DateTime?)null),
                cancellationToken);

    private static Expression<Func<Document, bool>> IsClaimable(DateTime now, DateTime staleBefore) =>
        document =>
            document.IndexingStatus == "Pending" ||
            (document.IndexingStatus == "RetryWaiting" &&
             document.NextProcessingAttemptAt <= now) ||
            ((document.IndexingStatus == "Extracting" || document.IndexingStatus == "Indexing") &&
             ((document.ProcessingHeartbeatAt == null && document.ProcessingStartedAt == null) ||
              (document.ProcessingHeartbeatAt ?? document.ProcessingStartedAt) < staleBefore));
}
