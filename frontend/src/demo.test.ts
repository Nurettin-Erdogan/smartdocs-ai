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

  it('recognizes a document identity question instead of matching the word paper', async () => {
    const demoApi = createDemoApi({
      extractPdf: async () => [
        {
          chunkIndex: 0,
          pageNumber: 1,
          content: 'SINAVA GİRİŞ BELGESİ. Adayın sınava gireceği bina ve salon bilgileri bu belgede yer alır.'
        },
        {
          chunkIndex: 1,
          pageNumber: 1,
          content: 'Sınav tamamlandıktan sonra cevap kağıdı ve soru kitapçığı salon görevlisine teslim edilmelidir.'
        }
      ]
    });
    const uploaded = await demoApi.uploadDocument(
      new File(['demo'], 'Sınava Giriş Belgesi.pdf', { type: 'application/pdf' })
    );

    const result = await demoApi.askChat({
      question: 'bu ne kağıdı',
      documentIds: [uploaded.id]
    });

    expect(result.answer).toContain('Sınava Giriş Belgesi');
    expect(result.answer.toLocaleLowerCase('tr-TR')).toContain('sınava katılım');
    expect(result.answer).not.toContain('Belgende soruyla en güçlü eşleşen');
    expect(result.sources[0]).toMatchObject({ documentId: uploaded.id, pageNumber: 1 });
  });

  it('answers document purpose and summary questions naturally', async () => {
    const demoApi = createDemoApi({
      extractPdf: async () => [
        {
          chunkIndex: 0,
          pageNumber: 1,
          content: 'ÖZGEÇMİŞ. Eğitim, iş deneyimi ve teknik yetkinlikler.'
        }
      ]
    });
    const uploaded = await demoApi.uploadDocument(
      new File(['demo'], 'Nurettin Erdogan CV.pdf', { type: 'application/pdf' })
    );

    const purpose = await demoApi.askChat({
      question: 'Bu belge ne işe yarıyor?',
      documentIds: [uploaded.id]
    });
    const summary = await demoApi.askChat({
      question: 'Kısaca özetle',
      documentIds: [uploaded.id]
    });

    expect(purpose.answer).toContain('iş veya staj başvuruları');
    expect(summary.answer).toContain('Özgeçmiş (CV)');
    expect(summary.answer).toContain('Eğitim, iş deneyimi');
  });

  it('responds to a greeting without inventing document facts', async () => {
    const result = await createDemoApi().askChat({ question: 'merhaba' });

    expect(result.answer).toContain('Merhaba');
    expect(result.sources).toEqual([]);
  });

  it('uses the AI endpoint with broad document context and conversation history', async () => {
    const generateAiAnswer = vi.fn().mockResolvedValue(
      'Öğrencinin adı Nurettin Erdoğan. (Sınava Giriş Belgesi, s. 1)'
    );
    const demoApi = createDemoApi({
      generateAiAnswer,
      extractPdf: async () => [
        {
          chunkIndex: 0,
          pageNumber: 1,
          content: 'Aday Adı Soyadı: Nurettin Erdoğan. Sınav yeri İstanbul.'
        },
        {
          chunkIndex: 1,
          pageNumber: 1,
          content: 'Öğrenci kimlik kartları ve sınava giriş belgesi yanında bulunmalıdır.'
        }
      ]
    });
    const uploaded = await demoApi.uploadDocument(
      new File(['demo'], 'Sınava Giriş Belgesi.pdf', { type: 'application/pdf' })
    );
    const first = await demoApi.askChat({
      question: 'Bu ne belgesi?',
      documentIds: [uploaded.id]
    });
    const result = await demoApi.askChat({
      question: 'öğrenci ismi ne',
      conversationId: first.conversationId,
      documentIds: [uploaded.id]
    });

    expect(result.mode).toBe('ai');
    expect(result.answer).toContain('Nurettin Erdoğan');
    expect(generateAiAnswer).toHaveBeenLastCalledWith(
      expect.objectContaining({
        question: 'öğrenci ismi ne',
        sources: expect.arrayContaining([
          expect.objectContaining({ content: expect.stringContaining('Nurettin Erdoğan') })
        ]),
        history: expect.arrayContaining([
          expect.objectContaining({ question: 'Bu ne belgesi?' })
        ])
      }),
      undefined
    );
  });

  it('falls back to the grounded local answer when the AI endpoint is unavailable', async () => {
    const demoApi = createDemoApi({
      generateAiAnswer: vi.fn().mockResolvedValue(null),
      extractPdf: async () => [{
        chunkIndex: 0,
        pageNumber: 1,
        content: 'Proje teslim tarihi 18 Eylül 2026 olarak belirlenmiştir.'
      }]
    });
    const uploaded = await demoApi.uploadDocument(
      new File(['demo'], 'proje.pdf', { type: 'application/pdf' })
    );

    const result = await demoApi.askChat({
      question: 'Teslim tarihi ne zaman?',
      documentIds: [uploaded.id]
    });

    expect(result.mode).toBe('local');
    expect(result.answer).toContain('18 Eylül 2026');
  });
});
