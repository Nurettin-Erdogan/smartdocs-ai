import type {
  AuthResponse,
  ChatConversation,
  ChatHistorySummary,
  ChatRequest,
  ChatResponse,
  ChatSource,
  ChatStreamCallbacks,
  DocumentItem
} from './api';
import { extractPdfChunks, type LocalPdfChunk } from './localPdf';

export const isDemoMode = import.meta.env.VITE_DEMO_MODE === 'true';

const DEMO_USER: AuthResponse = {
  id: 1,
  fullName: 'Nurettin Erdoğan',
  email: 'demo@smartdocs.ai',
  role: 'Vitrin kullanıcısı',
  token: 'smartdocs-demo-session'
};

const recentIso = (minutesAgo: number) =>
  new Date(Date.now() - minutesAgo * 60_000).toISOString();

const initialDocuments = (): DocumentItem[] => [
  {
    id: 11,
    title: 'KVKK Uyum Rehberi 2026',
    fileName: 'kvkk-uyum-rehberi-2026.pdf',
    fileType: 'application/pdf',
    fileSize: 2_486_272,
    uploadDate: recentIso(48),
    indexingStatus: 'Ready'
  },
  {
    id: 12,
    title: 'Bilgi Güvenliği Politikası',
    fileName: 'bilgi-guvenligi-politikasi.pdf',
    fileType: 'application/pdf',
    fileSize: 1_178_624,
    uploadDate: recentIso(41),
    indexingStatus: 'Ready'
  },
  {
    id: 13,
    title: 'Tedarikçi Risk Değerlendirmesi',
    fileName: 'tedarikci-risk-degerlendirmesi.pdf',
    fileType: 'application/pdf',
    fileSize: 864_320,
    uploadDate: recentIso(33),
    indexingStatus: 'Ready'
  }
];

const initialConversation = (): ChatConversation => ({
  conversationId: 101,
  createdAt: recentIso(24),
  messages: [
    {
      id: 1,
      question: 'KVKK kapsamında veri sorumlusunun temel yükümlülükleri nelerdir?',
      answer:
        'Veri sorumlusu; kişisel verilerin işleme amaçlarını ve vasıtalarını açıkça belirlemeli, erişimleri rol bazlı sınırlandırmalı ve işleme faaliyetlerini kayıt altına almalıdır. Saklama süresi dolan veriler güvenli biçimde silinmeli; ihlal riskleri düzenli olarak değerlendirilmelidir.',
      createdAt: recentIso(24)
    }
  ]
});

const cloneDocument = (document: DocumentItem): DocumentItem => ({ ...document });
const cloneConversation = (conversation: ChatConversation): ChatConversation => ({
  ...conversation,
  messages: conversation.messages.map((message) => ({ ...message }))
});

const abortError = () => new DOMException('İşlem iptal edildi.', 'AbortError');

const wait = (milliseconds: number, signal?: AbortSignal) =>
  new Promise<void>((resolve, reject) => {
    if (signal?.aborted) {
      reject(abortError());
      return;
    }

    const timer = globalThis.setTimeout(() => {
      signal?.removeEventListener('abort', onAbort);
      resolve();
    }, milliseconds);
    const onAbort = () => {
      globalThis.clearTimeout(timer);
      reject(abortError());
    };

    signal?.addEventListener('abort', onAbort, { once: true });
  });

type StoredChunk = LocalPdfChunk & {
  documentId: number;
  title: string;
};

type DemoApiOptions = {
  extractPdf?: (file: File) => Promise<LocalPdfChunk[]>;
};

const STOP_WORDS = new Set([
  'acaba', 'ama', 'bana', 'benim', 'bir', 'biri', 'bunu', 'bu', 'da', 'daha', 'de',
  'diye', 'en', 'gibi', 'hangi', 'için', 'ile', 'ise', 'mi', 'mı', 'mu', 'mü', 'nasıl',
  'ne', 'neden', 'nedir', 'olan', 'olarak', 'sonra', 'şu', 've', 'veya', 'ya'
]);

const normalize = (value: string) => value
  .toLocaleLowerCase('tr-TR')
  .normalize('NFD')
  .replace(/[\u0300-\u036f]/g, '')
  .replace(/[^a-z0-9çğıöşü\s]/g, ' ')
  .replace(/\s+/g, ' ')
  .trim();

const questionTerms = (question: string) => [...new Set(
  normalize(question)
    .split(' ')
    .filter((term) => term.length >= 3 && !STOP_WORDS.has(term))
)];

const sharesWordStem = (left: string, right: string) => {
  if (left.startsWith(right) || right.startsWith(left)) return true;
  const minimumStemLength = Math.min(7, left.length, right.length);
  return minimumStemLength >= 4 && left.slice(0, minimumStemLength) === right.slice(0, minimumStemLength);
};

const scoreChunk = (chunk: StoredChunk, terms: string[]) => {
  const content = normalize(chunk.content);
  if (terms.length === 0) return 0;
  const contentTerms = content.split(' ');

  return terms.reduce((score, term) => {
    const matches = contentTerms.filter((token) => sharesWordStem(token, term)).length;
    return score + Math.min(matches, 4);
  }, 0) / Math.sqrt(Math.max(contentTerms.length, 1));
};

const sourceExcerpt = (source: ChatSource, terms: string[]) => {
  const sentences = source.content
    .split(/(?<=[.!?…])\s+/)
    .map((sentence) => sentence.trim())
    .filter(Boolean);
  const ranked = sentences
    .map((sentence) => ({
      sentence,
      score: terms.filter((term) => normalize(sentence)
        .split(' ')
        .some((token) => sharesWordStem(token, term))).length
    }))
    .sort((left, right) => right.score - left.score);
  const selected = (ranked[0]?.sentence || source.content).trim();
  return selected.length <= 360 ? selected : `${selected.slice(0, 357).trimEnd()}…`;
};

const answerFor = (question: string, sources: ChatSource[], hasDirectMatch: boolean) => {
  if (sources.length === 0) {
    return 'Seçtiğiniz belgelerde okunabilir bir kaynak bölümü bulunamadı.';
  }

  const terms = questionTerms(question);
  const excerpts = sources.slice(0, 3).map((source) =>
    `• ${source.title}, sayfa ${source.pageNumber}: ${sourceExcerpt(source, terms)}`);
  const introduction = hasDirectMatch
    ? 'Belgende soruyla en güçlü eşleşen bilgiler şunlar:'
    : 'Soruyla doğrudan eşleşen bir ifade bulamadım. En yakın kaynak bölümleri şunlar:';

  return `${introduction}\n\n${excerpts.join('\n\n')}\n\n` +
    'Yanıt yalnızca tarayıcıda okunan belge metninden çıkarılmıştır; kaynak kartlarından ilgili sayfaları kontrol edebilirsin.';
};

const initialStoredChunks = (): StoredChunk[] => [
  {
    documentId: 11,
    title: 'KVKK Uyum Rehberi 2026',
    chunkIndex: 0,
    pageNumber: 12,
    content: 'Veri sorumlusu, kişisel verilerin işleme amaçlarını ve vasıtalarını belirler. Saklama sürelerini tanımlar ve süresi dolan verilerin güvenli biçimde silinmesini sağlar.'
  },
  {
    documentId: 12,
    title: 'Bilgi Güvenliği Politikası',
    chunkIndex: 1,
    pageNumber: 8,
    content: 'Erişim yetkileri rol bazlı sınırlandırılır ve en az yetki ilkesi uygulanır. İşlemler denetim kaydına alınır ve düzenli olarak gözden geçirilir.'
  },
  {
    documentId: 13,
    title: 'Tedarikçi Risk Değerlendirmesi',
    chunkIndex: 2,
    pageNumber: 5,
    content: 'Tedarikçiler veri erişimi, hizmet sürekliliği ve güvenlik kontrolü başlıklarında periyodik olarak değerlendirilir.'
  }
];

export function createDemoApi(options: DemoApiOptions = {}) {
  let documents = initialDocuments();
  let storedChunks = initialStoredChunks();
  let nextDocumentId = 20;
  let nextConversationId = 102;
  let nextMessageId = 2;
  const conversations = new Map<number, ChatConversation>([
    [101, initialConversation()]
  ]);
  const uploadedFiles = new Map<number, File>();
  const pdfExtractor = options.extractPdf ?? extractPdfChunks;

  const history = (): ChatHistorySummary[] =>
    [...conversations.values()]
      .sort((left, right) => right.createdAt.localeCompare(left.createdAt))
      .map((conversation) => ({
        conversationId: conversation.conversationId,
        createdAt: conversation.createdAt,
        firstQuestion: conversation.messages[0]?.question ?? 'Yeni sohbet',
        messageCount: conversation.messages.length
      }));

  const sourcesFor = (question: string, documentIds?: number[]) => {
    const selectedIds = documentIds?.length ? new Set(documentIds) : null;
    const readyIds = new Set(documents
      .filter((document) => document.indexingStatus === 'Ready')
      .map((document) => document.id));
    const terms = questionTerms(question);
    const ranked = storedChunks
      .filter((chunk) => readyIds.has(chunk.documentId))
      .filter((chunk) => !selectedIds || selectedIds.has(chunk.documentId))
      .map((chunk) => ({ chunk, rawScore: scoreChunk(chunk, terms) }))
      .sort((left, right) => right.rawScore - left.rawScore || left.chunk.chunkIndex - right.chunk.chunkIndex);
    const hasDirectMatch = ranked.some((item) => item.rawScore > 0);
    const chosen = (hasDirectMatch ? ranked.filter((item) => item.rawScore > 0) : ranked).slice(0, 4);
    const highestScore = chosen[0]?.rawScore || 1;
    const sources: ChatSource[] = chosen.map(({ chunk, rawScore }) => ({
      documentId: chunk.documentId,
      title: chunk.title,
      chunkIndex: chunk.chunkIndex,
      pageNumber: chunk.pageNumber,
      score: hasDirectMatch ? Math.min(0.99, 0.55 + (rawScore / highestScore) * 0.44) : 0.35,
      content: chunk.content
    }));

    return { sources, hasDirectMatch };
  };

  return {
    register: async (body: { fullName: string; email: string; password: string }) => ({
      ...DEMO_USER,
      fullName: body.fullName || DEMO_USER.fullName,
      email: body.email || DEMO_USER.email
    }),
    login: async (_body: { email: string; password: string }) => ({ ...DEMO_USER }),
    listDocuments: async (signal?: AbortSignal) => {
      await wait(90, signal);
      return documents.map(cloneDocument);
    },
    getDocumentFile: async (id: number, signal?: AbortSignal) => {
      await wait(60, signal);
      return uploadedFiles.get(id) ?? null;
    },
    uploadDocument: async (file: File) => {
      const extractedChunks = await pdfExtractor(file);
      const documentId = nextDocumentId++;
      const title = file.name.replace(/\.pdf$/i, '').replace(/[-_]+/g, ' ').trim();
      const document: DocumentItem = {
        id: documentId,
        title: title || 'Adsız PDF',
        fileName: file.name,
        fileType: file.type || 'application/pdf',
        fileSize: file.size,
        uploadDate: new Date().toISOString(),
        indexingStatus: 'Ready'
      };
      storedChunks = [
        ...extractedChunks.map((chunk) => ({ ...chunk, documentId, title: document.title })),
        ...storedChunks
      ];
      uploadedFiles.set(documentId, file);
      documents = [document, ...documents];
      return cloneDocument(document);
    },
    deleteDocument: async (id: number) => {
      documents = documents.filter((document) => document.id !== id);
      storedChunks = storedChunks.filter((chunk) => chunk.documentId !== id);
      uploadedFiles.delete(id);
      return { message: 'Yerel belge kaldırıldı.' };
    },
    reindexDocument: async (id: number) => {
      const document = documents.find((item) => item.id === id);
      if (!document) throw new Error('Belge bulunamadı.');
      const file = uploadedFiles.get(id);
      if (file) {
        const extractedChunks = await pdfExtractor(file);
        storedChunks = [
          ...storedChunks.filter((chunk) => chunk.documentId !== id),
          ...extractedChunks.map((chunk) => ({
            ...chunk,
            documentId: id,
            title: document.title
          }))
        ];
      }
      document.indexingStatus = 'Ready';
      document.indexingError = null;
      return cloneDocument(document);
    },
    askChat: async (
      body: ChatRequest,
      callbacks: ChatStreamCallbacks = {}
    ): Promise<ChatResponse> => {
      const conversationId = body.conversationId && conversations.has(body.conversationId)
        ? body.conversationId
        : nextConversationId++;
      const createdAt = new Date().toISOString();
      const { sources, hasDirectMatch } = sourcesFor(body.question, body.documentIds);
      const answer = answerFor(body.question, sources, hasDirectMatch);
      const chunks = answer.match(/.{1,34}(?:\s|$)/g) ?? [answer];

      callbacks.onStart?.({ conversationId, sources });
      for (const chunk of chunks) {
        await wait(28, callbacks.signal);
        callbacks.onChunk?.(chunk);
      }

      const conversation = conversations.get(conversationId) ?? {
        conversationId,
        createdAt,
        messages: []
      };
      conversation.messages.push({
        id: nextMessageId++,
        question: body.question,
        answer,
        createdAt
      });
      conversations.set(conversationId, conversation);

      return { conversationId, answer, sources };
    },
    chatHistory: async (signal?: AbortSignal) => {
      await wait(70, signal);
      return history().map((item) => ({ ...item }));
    },
    getConversation: async (conversationId: number, signal?: AbortSignal) => {
      await wait(70, signal);
      const conversation = conversations.get(conversationId);
      if (!conversation) throw new Error('Sohbet bulunamadı.');
      return cloneConversation(conversation);
    }
  };
}
