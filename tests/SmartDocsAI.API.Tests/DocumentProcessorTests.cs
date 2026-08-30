using Microsoft.Extensions.Configuration;
using SmartDocsAI.API.Models;
using SmartDocsAI.API.Services;
using UglyToad.PdfPig.Content;
using UglyToad.PdfPig.Core;
using UglyToad.PdfPig.Fonts.Standard14Fonts;
using UglyToad.PdfPig.Writer;

namespace SmartDocsAI.API.Tests;

public sealed class DocumentProcessorTests
{
    [Fact]
    public async Task ProcessPdfAsync_PreservesSpacesBetweenSeparatelyDrawnWords()
    {
        var filePath = Path.Combine(Path.GetTempPath(), $"smartdocs-{Guid.NewGuid():N}.pdf");

        try
        {
            var builder = new PdfDocumentBuilder();
            var font = builder.AddStandard14Font(Standard14Font.Helvetica);
            var page = builder.AddPage(PageSize.A4);
            page.AddText("NURETTIN", 14, new PdfPoint(100, 700), font);
            page.AddText("ERDOGAN", 14, new PdfPoint(190, 700), font);
            await File.WriteAllBytesAsync(filePath, builder.Build());

            var configuration = new ConfigurationBuilder().Build();
            var processor = new DocumentProcessor(
                configuration,
                new DocumentProcessingGate(configuration));
            var document = new Document
            {
                Id = 42,
                FilePath = filePath,
                FileName = "cv.pdf",
                Title = "CV",
                FileType = "application/pdf"
            };

            var chunks = await processor.ProcessPdfAsync(document);

            var chunk = Assert.Single(chunks);
            Assert.Contains("NURETTIN ERDOGAN", chunk.Content, StringComparison.Ordinal);
            Assert.DoesNotContain("NURETTINERDOGAN", chunk.Content, StringComparison.Ordinal);
        }
        finally
        {
            File.Delete(filePath);
        }
    }
}
