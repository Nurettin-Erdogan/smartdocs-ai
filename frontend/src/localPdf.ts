export type LocalPdfChunk = {
  chunkIndex: number;
  pageNumber: number;
  content: string;
};

const MAX_LOCAL_PAGES = 500;
const MAX_LOCAL_CHARACTERS = 2_000_000;
const TARGET_CHUNK_CHARACTERS = 900;
const CHUNK_OVERLAP_WORDS = 24;

const cleanPageText = (text: string) => text
  .replace(/\u0000/g, '')
  .replace(/[ \t]+/g, ' ')
  .replace(/\s*\n\s*/g, '\n')
  .replace(/\n{3,}/g, '\n\n')
  .trim();

const chunkPage = (text: string, pageNumber: number, firstChunkIndex: number) => {
  const words = text.split(/\s+/).filter(Boolean);
  const chunks: LocalPdfChunk[] = [];
  let cursor = 0;

  while (cursor < words.length) {
    const chunkWords: string[] = [];
    let characterCount = 0;

    while (cursor < words.length && characterCount < TARGET_CHUNK_CHARACTERS) {
      const word = words[cursor++];
      chunkWords.push(word);
      characterCount += word.length + 1;
    }

    const content = chunkWords.join(' ').trim();
    if (content) {
      chunks.push({
        chunkIndex: firstChunkIndex + chunks.length,
        pageNumber,
        content
      });
    }

    if (cursor < words.length) {
      cursor = Math.max(0, cursor - Math.min(CHUNK_OVERLAP_WORDS, chunkWords.length - 1));
    }
  }

  return chunks;
};

export async function extractPdfChunks(file: File): Promise<LocalPdfChunk[]> {
  const [pdfjs, workerModule] = await Promise.all([
    import('pdfjs-dist'),
    import('pdfjs-dist/build/pdf.worker.min.mjs?url')
  ]);
  pdfjs.GlobalWorkerOptions.workerSrc = workerModule.default;

  const loadingTask = pdfjs.getDocument({
    data: new Uint8Array(await file.arrayBuffer())
  });
  let pdf: Awaited<(typeof loadingTask)['promise']>;

  try {
    pdf = await loadingTask.promise;
  } catch (error) {
    await loadingTask.destroy();
    const message = error instanceof Error ? error.message.toLocaleLowerCase('tr-TR') : '';
    if (message.includes('password')) {
      throw new Error('Şifreli PDF dosyaları desteklenmiyor. Şifreyi kaldırıp yeniden deneyin.');
    }
    throw new Error('PDF okunamadı. Dosyanın bozuk olmadığını kontrol edip yeniden deneyin.');
  }

  const chunks: LocalPdfChunk[] = [];
  let totalCharacters = 0;

  try {
    if (pdf.numPages > MAX_LOCAL_PAGES) {
      throw new Error(`PDF en fazla ${MAX_LOCAL_PAGES} sayfa olabilir.`);
    }

    for (let pageNumber = 1; pageNumber <= pdf.numPages; pageNumber += 1) {
      const page = await pdf.getPage(pageNumber);
      try {
        const textContent = await page.getTextContent();
        const rawText = textContent.items
          .map((item) => {
            if (!('str' in item)) return '';
            return `${item.str}${item.hasEOL ? '\n' : ' '}`;
          })
          .join('');
        const pageText = cleanPageText(rawText);
        totalCharacters += pageText.length;

        if (totalCharacters > MAX_LOCAL_CHARACTERS) {
          throw new Error('PDF metni yerel işleme sınırını aşıyor. Daha küçük bir PDF deneyin.');
        }

        chunks.push(...chunkPage(pageText, pageNumber, chunks.length));
      } finally {
        page.cleanup();
      }
    }
  } finally {
    await loadingTask.destroy();
  }

  if (chunks.length === 0 || totalCharacters < 20) {
    throw new Error(
      'PDF’de okunabilir metin bulunamadı. Bu dosya taranmış görüntüyse önce OCR uygulanmış bir kopyasını yükleyin.'
    );
  }

  return chunks;
}
