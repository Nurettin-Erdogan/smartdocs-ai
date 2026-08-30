using SmartDocsAI.API.Interfaces;
using SmartDocsAI.API.Models;
using UglyToad.PdfPig;
using UglyToad.PdfPig.DocumentLayoutAnalysis.TextExtractor;

namespace SmartDocsAI.API.Services;

public sealed class DocumentProcessor : IDocumentProcessor
{
    private const int ChunkSize = 800;
    private const int Overlap = 150;
    private const int DefaultMaxChunks = 2_000;

    private readonly int _maxChunks;
    private readonly int _maxPages;
    private readonly int _maxExtractedCharacters;
    private readonly TimeSpan _processingTimeout;
    private readonly DocumentProcessingGate _processingGate;

    public DocumentProcessor(
        IConfiguration configuration,
        DocumentProcessingGate processingGate)
    {
        _processingGate = processingGate;
        _maxChunks = Math.Clamp(
            configuration.GetValue<int?>("DocumentProcessingSettings:MaxChunks") ?? DefaultMaxChunks,
            1,
            10_000);
        _maxPages = Math.Clamp(
            configuration.GetValue<int?>("DocumentProcessingSettings:MaxPages") ?? 500,
            1,
            5_000);
        _maxExtractedCharacters = Math.Clamp(
            configuration.GetValue<int?>("DocumentProcessingSettings:MaxExtractedCharacters") ?? 2_000_000,
            10_000,
            20_000_000);
        _processingTimeout = TimeSpan.FromSeconds(Math.Clamp(
            configuration.GetValue<int?>("DocumentProcessingSettings:TimeoutSeconds") ?? 60,
            5,
            600));
    }

    public async Task<List<Chunk>> ProcessPdfAsync(
        Document document,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(document);

        await _processingGate.WaitAsync(cancellationToken);
        var timeoutSource = new CancellationTokenSource(_processingTimeout);
        var linkedSource = CancellationTokenSource.CreateLinkedTokenSource(
            cancellationToken,
            timeoutSource.Token);
        var processingTask = Task.Run(
            () => ProcessPdf(document, linkedSource.Token),
            CancellationToken.None);

        try
        {
            return await processingTask.WaitAsync(_processingTimeout, cancellationToken);
        }
        catch (TimeoutException exception)
        {
            timeoutSource.Cancel();
            throw new InvalidDataException(
                $"PDF işleme süresi {_processingTimeout.TotalSeconds:0} saniyelik güvenlik sınırını aştı.",
                exception);
        }
        catch (OperationCanceledException exception) when (
            timeoutSource.IsCancellationRequested && !cancellationToken.IsCancellationRequested)
        {
            throw new InvalidDataException(
                $"PDF işleme süresi {_processingTimeout.TotalSeconds:0} saniyelik güvenlik sınırını aştı.",
                exception);
        }
        finally
        {
            if (processingTask.IsCompleted)
            {
                linkedSource.Dispose();
                timeoutSource.Dispose();
                _processingGate.Release();
            }
            else
            {
                _ = ObserveAndReleaseAsync(
                    processingTask,
                    linkedSource,
                    timeoutSource,
                    _processingGate);
            }
        }
    }

    private List<Chunk> ProcessPdf(Document document, CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();

        var chunks = new List<Chunk>();
        var chunkIndex = 0;
        var extractedCharacters = 0;

        using var pdf = PdfDocument.Open(document.FilePath);
        if (pdf.NumberOfPages > _maxPages)
        {
            throw new InvalidDataException(
                $"PDF en fazla {_maxPages} sayfa içerebilir.");
        }

        foreach (var page in pdf.GetPages())
        {
            cancellationToken.ThrowIfCancellationRequested();

            // PdfPig's Page.Text preserves the PDF content-stream order and can
            // silently join visually separate words (for example, a person's
            // first and last name). The layout-aware extractor reconstructs
            // human-readable spacing before the text is indexed for RAG.
            var pageText = CleanText(ContentOrderTextExtractor.GetText(page, true));
            if (string.IsNullOrWhiteSpace(pageText))
            {
                continue;
            }

            extractedCharacters = checked(extractedCharacters + pageText.Length);
            if (extractedCharacters > _maxExtractedCharacters)
            {
                throw new InvalidDataException(
                    $"PDF en fazla {_maxExtractedCharacters:N0} çıkarılmış metin karakteri içerebilir.");
            }

            foreach (var content in SplitIntoChunks(pageText, ChunkSize, Overlap))
            {
                cancellationToken.ThrowIfCancellationRequested();

                if (chunks.Count >= _maxChunks)
                {
                    throw new InvalidDataException(
                        $"PDF güvenli işleme sınırını aştı. En fazla {_maxChunks} metin parçası oluşturulabilir.");
                }

                chunks.Add(new Chunk
                {
                    DocumentId = document.Id,
                    ChunkIndex = chunkIndex++,
                    Content = content,
                    PageNumber = page.Number
                });
            }
        }

        return chunks;
    }

    private static async Task ObserveAndReleaseAsync(
        Task processingTask,
        CancellationTokenSource linkedSource,
        CancellationTokenSource timeoutSource,
        DocumentProcessingGate processingGate)
    {
        try
        {
            await processingTask;
        }
        catch
        {
            // The request has already observed cancellation or timeout.
        }
        finally
        {
            linkedSource.Dispose();
            timeoutSource.Dispose();
            processingGate.Release();
        }
    }

    private static string CleanText(string? text)
    {
        if (string.IsNullOrWhiteSpace(text))
        {
            return string.Empty;
        }

        return string.Join(' ', text
            .Split((char[]?)null, StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries));
    }

    private static IEnumerable<string> SplitIntoChunks(string text, int chunkSize, int overlap)
    {
        if (string.IsNullOrEmpty(text))
        {
            yield break;
        }

        var step = chunkSize - overlap;
        if (step <= 0)
        {
            throw new ArgumentOutOfRangeException(nameof(overlap), "Overlap must be smaller than chunk size.");
        }

        for (var start = 0; start < text.Length; start += step)
        {
            var length = Math.Min(chunkSize, text.Length - start);
            yield return text.Substring(start, length);

            if (start + length >= text.Length)
            {
                yield break;
            }
        }
    }
}

public sealed class DocumentProcessingGate
{
    private readonly SemaphoreSlim _semaphore;

    public DocumentProcessingGate(IConfiguration configuration)
    {
        var maxConcurrency = Math.Clamp(
            configuration.GetValue<int?>("DocumentProcessingSettings:MaxConcurrentDocuments") ?? 2,
            1,
            8);
        _semaphore = new SemaphoreSlim(maxConcurrency, maxConcurrency);
    }

    public Task WaitAsync(CancellationToken cancellationToken) =>
        _semaphore.WaitAsync(cancellationToken);

    public void Release() => _semaphore.Release();
}
