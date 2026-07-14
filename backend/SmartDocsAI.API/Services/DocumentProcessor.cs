using SmartDocsAI.API.Interfaces;
using SmartDocsAI.API.Models;
using UglyToad.PdfPig;

namespace SmartDocsAI.API.Services;

public sealed class DocumentProcessor : IDocumentProcessor
{
    private const int ChunkSize = 800;
    private const int Overlap = 150;
    private const int DefaultMaxChunks = 2_000;

    private readonly int _maxChunks;

    public DocumentProcessor(IConfiguration configuration)
    {
        _maxChunks = Math.Clamp(
            configuration.GetValue<int?>("DocumentProcessingSettings:MaxChunks") ?? DefaultMaxChunks,
            1,
            10_000);
    }

    public Task<List<Chunk>> ProcessPdfAsync(
        Document document,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(document);

        var chunks = new List<Chunk>();
        var chunkIndex = 0;

        using var pdf = PdfDocument.Open(document.FilePath);
        foreach (var page in pdf.GetPages())
        {
            cancellationToken.ThrowIfCancellationRequested();

            var pageText = CleanText(page.Text);
            if (string.IsNullOrWhiteSpace(pageText))
            {
                continue;
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

        return Task.FromResult(chunks);
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
