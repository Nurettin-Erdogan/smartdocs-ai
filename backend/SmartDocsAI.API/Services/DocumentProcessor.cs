// Yüklenen PDF'yi işler.
// PDF'den metni çıkarır ve metni küçük parçalara ayırır.
// Daha sonra embedding oluşturulacak yapıyı hazırlar.

using UglyToad.PdfPig;
using SmartDocsAI.API.Interfaces;
using SmartDocsAI.API.Models;

namespace SmartDocsAI.API.Services
{
    public class DocumentProcessor : IDocumentProcessor
    {
        private const int ChunkSize = 800; // Her bir parçanın karakter limiti
        private const int Overlap = 150;    // Parçalar arası çakışan karakter miktarı

        public async Task<List<Chunk>> ProcessPdfAsync(Document document)
        {
            var chunks = new List<Chunk>();
            int chunkIndex = 0;

            // Arka planda CPU yoğunluklu bir işlem olacağı için Task.Run kullanarak asenkron çalıştırıyoruz.
            await Task.Run(() =>
            {
                // PdfPig kütüphanesi ile PDF dosyasını açıyoruz
                using (var pdf = PdfDocument.Open(document.FilePath))
                {
                    foreach (var page in pdf.GetPages())
                    {
                        // Sayfa metnini okuyoruz
                        var pageText = page.Text;

                        if (string.IsNullOrWhiteSpace(pageText))
                            continue;

                        // Sayfadaki metni temizleme (gereksiz boşlukları ve satır sonlarını düzenleme)
                        pageText = CleanText(pageText);

                        // Sayfa metnini örtüşmeli parçalara ayırıyoruz
                        var textChunks = SplitIntoChunks(pageText, ChunkSize, Overlap);

                        foreach (var content in textChunks)
                        {
                            chunks.Add(new Chunk
                            {
                                DocumentId = document.Id,
                                ChunkIndex = chunkIndex++,
                                Content = content,
                                PageNumber = page.Number
                            });
                        }
                    }
                }
            });

            return chunks;
        }

        /// <summary>
        /// Metindeki gereksiz boşlukları ve ardışık satır sonlarını temizler.
        /// </summary>
        private string CleanText(string text)
        {
            return text.Replace("\r\n", " ")
                       .Replace("\n", " ")
                       .Replace("\t", " ")
                       .Trim();
        }

        /// <summary>
        /// Kayar pencere (Sliding Window) tekniğiyle metni parçalara ayırır.
        /// </summary>
        private List<string> SplitIntoChunks(string text, int chunkSize, int overlap)
        {
            var chunks = new List<string>();
            if (string.IsNullOrEmpty(text)) return chunks;

            // Metin belirtilen boyuttan kısaysa tek parça olarak döner.
            if (text.Length <= chunkSize)
            {
                chunks.Add(text);
                return chunks;
            }

            int start = 0;
            while (start < text.Length)
            {
                int end = Math.Min(start + chunkSize, text.Length);
                var chunk = text.Substring(start, end - start);
                chunks.Add(chunk);

                // Dosya sonuna ulaştıysak döngüyü bitir.
                if (end >= text.Length)
                    break;

                // Örtüşmeyi (Overlap) hesaba katarak yeni başlangıç noktasını belirle.
                start += (chunkSize - overlap);
            }

            return chunks;
        }
    }
}
