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

const answerFor = (question: string) =>
  `Seçtiğiniz belgelerde “${question}” konusu; açık sorumluluklar, rol bazlı erişim ` +
  'kontrolleri ve düzenli denetim kayıtları üzerinden ele alınıyor. KVKK Uyum Rehberi, ' +
  'işleme amacının ve saklama süresinin önceden belirlenmesini; Bilgi Güvenliği Politikası ' +
  'ise erişimlerin en az yetki ilkesiyle sınırlandırılmasını öneriyor. Uygulamada bu iki ' +
  'kontrolün birlikte izlenmesi ve istisnaların kayıt altına alınması gerekir.';

export function createDemoApi() {
  let documents = initialDocuments();
  let nextDocumentId = 20;
  let nextConversationId = 102;
  let nextMessageId = 2;
  const conversations = new Map<number, ChatConversation>([
    [101, initialConversation()]
  ]);

  const history = (): ChatHistorySummary[] =>
    [...conversations.values()]
      .sort((left, right) => right.createdAt.localeCompare(left.createdAt))
      .map((conversation) => ({
        conversationId: conversation.conversationId,
        createdAt: conversation.createdAt,
        firstQuestion: conversation.messages[0]?.question ?? 'Yeni sohbet',
        messageCount: conversation.messages.length
      }));

  const sourcesFor = (documentIds?: number[]): ChatSource[] => {
    const selectedIds = documentIds?.length ? new Set(documentIds) : null;
    const selectedDocuments = documents
      .filter((document) => document.indexingStatus === 'Ready')
      .filter((document) => !selectedIds || selectedIds.has(document.id))
      .slice(0, 2);

    return selectedDocuments.map((document, index) => ({
      documentId: document.id,
      title: document.title,
      chunkIndex: index === 0 ? 18 : 9,
      pageNumber: index === 0 ? 12 : 8,
      score: index === 0 ? 0.924 : 0.887,
      content: index === 0
        ? 'Veri sorumlusu, işleme amaçlarını ve vasıtalarını belirleyen gerçek veya tüzel kişidir.'
        : 'Erişim yetkileri rol bazlı sınırlandırılır; işlemler kayıt altına alınır ve düzenli gözden geçirilir.'
    }));
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
    uploadDocument: async (file: File) => {
      await wait(350);
      const document: DocumentItem = {
        id: nextDocumentId++,
        title: file.name.replace(/\.pdf$/i, '').replace(/-/g, ' '),
        fileName: file.name,
        fileType: file.type || 'application/pdf',
        fileSize: file.size,
        uploadDate: new Date().toISOString(),
        indexingStatus: 'Ready'
      };
      documents = [document, ...documents];
      return cloneDocument(document);
    },
    deleteDocument: async (id: number) => {
      documents = documents.filter((document) => document.id !== id);
      return { message: 'Demo belgesi kaldırıldı.' };
    },
    reindexDocument: async (id: number) => {
      const document = documents.find((item) => item.id === id);
      if (!document) throw new Error('Belge bulunamadı.');
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
      const sources = sourcesFor(body.documentIds);
      const answer = answerFor(body.question);
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
