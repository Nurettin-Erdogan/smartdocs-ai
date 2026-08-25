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
    expect(result.sources).toHaveLength(2);
    expect(chunks.join('')).toContain('rol bazlı erişim');

    const conversation = await demoApi.getConversation(result.conversationId);
    expect(conversation.messages[conversation.messages.length - 1]?.answer).toBe(result.answer);
  });

  it('keeps uploaded sample files in memory only', async () => {
    const demoApi = createDemoApi();
    const file = new File(['demo'], 'yeni-politika.pdf', { type: 'application/pdf' });

    const uploaded = await demoApi.uploadDocument(file);
    const documents = await demoApi.listDocuments();

    expect(uploaded).toMatchObject({
      fileName: 'yeni-politika.pdf',
      indexingStatus: 'Ready'
    });
    expect(documents[0]?.id).toBe(uploaded.id);
  });
});
