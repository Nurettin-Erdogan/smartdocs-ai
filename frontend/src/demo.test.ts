import { describe, expect, it, vi } from 'vitest';
import { createDemoApi } from './demo';

describe('portfolio demo API', () => {
  it('opens with ready sample documents and conversation history', async () => {
    const demoApi = createDemoApi();

    const [documents, history] = await Promise.all([
      demoApi.listDocuments(),
      demoApi.chatHistory()
    ]);

    expect(documents).toHaveLength(3);
    expect(documents.every((document) => document.indexingStatus === 'Ready')).toBe(true);
    expect(history[0]).toMatchObject({ conversationId: 101, messageCount: 1 });
  });

  it('streams a sourced answer and saves it to the demo conversation', async () => {
    const demoApi = createDemoApi();
    const chunks: string[] = [];
    const started = vi.fn();

    const answerPromise = demoApi.askChat(
      { question: 'Erişimler nasıl sınırlandırılmalı?', documentIds: [11, 12] },
      { onStart: started, onChunk: (chunk) => chunks.push(chunk) }
    );
    const result = await answerPromise;

    expect(started).toHaveBeenCalledOnce();
    expect(result.sources).toEqual([
      expect.objectContaining({ documentId: 12, title: 'Bilgi Güvenliği Politikası' })
    ]);
    expect(chunks.join('').toLocaleLowerCase('tr-TR')).toContain('rol bazlı');

    const conversation = await demoApi.getConversation(result.conversationId);
    expect(conversation.messages[conversation.messages.length - 1]?.answer).toBe(result.answer);
  });

  it('indexes the uploaded PDF text locally and answers from its real content', async () => {
    const demoApi = createDemoApi({
      extractPdf: vi.fn().mockResolvedValue([
        {
          chunkIndex: 0,
          pageNumber: 4,
          content: 'Proje teslim tarihi 18 Eylül 2026 olarak belirlenmiştir. Sorumlu ekip ürün geliştirme ekibidir.'
        }
      ])
    });
    const file = new File(['demo'], 'yeni-politika.pdf', { type: 'application/pdf' });

    const uploaded = await demoApi.uploadDocument(file);
    const documents = await demoApi.listDocuments();
    const result = await demoApi.askChat({
      question: 'Proje teslim tarihi ne zaman?',
      documentIds: [uploaded.id]
    });

    expect(uploaded).toMatchObject({
      fileName: 'yeni-politika.pdf',
      indexingStatus: 'Ready'
    });
    expect(documents[0]?.id).toBe(uploaded.id);
    expect(result.answer).toContain('18 Eylül 2026');
    expect(result.sources).toEqual([
      expect.objectContaining({
        documentId: uploaded.id,
        pageNumber: 4,
        content: expect.stringContaining('18 Eylül 2026')
      })
    ]);
  });

  it('keeps an uploaded PDF available for local preview', async () => {
    const file = new File(['%PDF-1.4'], 'onizleme.pdf', { type: 'application/pdf' });
    const demoApi = createDemoApi({
      extractPdf: async () => [{ chunkIndex: 0, pageNumber: 1, content: 'Önizleme metni' }]
    });

    const document = await demoApi.uploadDocument(file);
    const preview = await demoApi.getDocumentFile(document.id);

    expect(preview).toBe(file);
  });
});
