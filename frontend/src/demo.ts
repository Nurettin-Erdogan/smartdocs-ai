import type {
  AnswerVerification,
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
  generateAiAnswer?: AiAnswerGenerator | null;
};

type AiAnswerRequest = {
  question: string;
  sources: ChatSource[];
  history: Array<{ question: string; answer: string }>;
};

type AiAnswerResult = {
  answer: string;
  verification?: AnswerVerification;
};

type AiAnswerGenerator = (
  request: AiAnswerRequest,
  signal?: AbortSignal
) => Promise<AiAnswerResult | string | null>;

type QuestionIntent =
  | 'document-identity'
  | 'document-purpose'
  | 'summary'
  | 'greeting'
  | 'thanks'
  | 'retrieval';

type DocumentProfile = {
  label: string;
  purpose: string;
  evidenceTerms: string[];
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

const canonical = (value: string) => normalize(value)
  .replace(/[ç]/g, 'c')
  .replace(/[ğ]/g, 'g')
  .replace(/[ı]/g, 'i')
  .replace(/[ö]/g, 'o')
  .replace(/[ş]/g, 's')
  .replace(/[ü]/g, 'u');

const questionIntent = (question: string): QuestionIntent => {
  const value = canonical(question);

  if (/^(merhaba|selam|selamlar|hey|naber|nasilsin)$/.test(value)) return 'greeting';
  if (/^(tesekkurler|tesekkur ederim|sag ol|sagol|eyvallah)$/.test(value)) return 'thanks';
  if (
    /\b(bu|su) ne (kagidi|belgesi|dosyasi|dokumani)\b/.test(value) ||
    /\b(bu|su) (belge|dosya|dokuman) ne(dir)?$/.test(value) ||
    /\b(hangi|ne) (tur|tip) (belge|dosya|dokuman)\b/.test(value) ||
    /\b(hangi|ne) belgesi\b/.test(value) ||
    /^(bu|su) ne(dir)?$/.test(value)
  ) return 'document-identity';
  if (
    /\b(ne ise yariyor|ne icin|amaci ne|amaci nedir|neden duzenlenmis|neden verilmis)\b/.test(value)
  ) return 'document-purpose';
  if (/\b(ozetle|ozeti|kisa ozet|kisaca|ne anlatiyor|icerigi ne)\b/.test(value)) return 'summary';

  return 'retrieval';
};

const DOCUMENT_PROFILES: Array<{ patterns: RegExp[]; profile: DocumentProfile }> = [
  {
    patterns: [/sinava? giris belgesi/, /sinav giris dokumani/],
    profile: {
      label: 'Sınava Giriş Belgesi',
      purpose: 'sınava katılım ve salon girişinde kullanılmak üzere düzenlenmiş; adayın yanında bulundurması gereken evrakları ve temel sınav kurallarını açıklıyor',
      evidenceTerms: ['sinav', 'aday', 'salon', 'kimlik', 'giris belgesi']
    }
  },
  {
    patterns: [/ozgecmis/, /curriculum vitae/, /\bcv\b/],
    profile: {
      label: 'Özgeçmiş (CV)',
      purpose: 'kişinin eğitimini, deneyimini, yetkinliklerini ve iletişim bilgilerini iş veya staj başvuruları için sunuyor',
      evidenceTerms: ['egitim', 'deneyim', 'yetkinlik', 'iletisim', 'ozgecmis']
    }
  },
  {
    patterns: [/\bfatura\b/, /invoice/],
    profile: {
      label: 'Fatura',
      purpose: 'satılan ürün veya hizmetin taraflarını, tutarını, vergilerini ve ödeme bilgilerini kayıt altına alıyor',
      evidenceTerms: ['fatura', 'tutar', 'vergi', 'toplam', 'odeme']
    }
  },
  {
    patterns: [/\bsozlesme\b/, /\bprotokol\b/],
    profile: {
      label: 'Sözleşme',
      purpose: 'tarafların haklarını, yükümlülüklerini ve üzerinde anlaştıkları koşulları kayıt altına alıyor',
      evidenceTerms: ['taraf', 'yukumluluk', 'madde', 'imza', 'sozlesme']
    }
  },
  {
    patterns: [/\btranskript\b/, /not dokumu/],
    profile: {
      label: 'Akademik Transkript',
      purpose: 'alınan dersleri, notları ve akademik başarı durumunu resmi olarak gösteriyor',
      evidenceTerms: ['ders', 'not', 'kredi', 'donem', 'transkript']
    }
  },
  {
    patterns: [/\bdiploma\b/, /\bsertifika\b/, /katilim belgesi/],
    profile: {
      label: 'Sertifika veya Diploma',
      purpose: 'bir eğitim, yeterlilik veya katılım durumunu belgelemek üzere düzenlenmiş',
      evidenceTerms: ['egitim', 'basari', 'katilim', 'mezun', 'sertifika']
    }
  },
  {
    patterns: [/nufus cuzdani/, /kimlik karti/, /\bpasaport\b/],
    profile: {
      label: 'Kimlik Belgesi',
      purpose: 'belge sahibinin kimliğini resmi olarak doğrulamak üzere düzenlenmiş',
      evidenceTerms: ['kimlik', 'ad', 'soyad', 'dogum', 'gecerlilik']
    }
  },
  {
    patterns: [/\bpolitika\b/],
    profile: {
      label: 'Politika Belgesi',
      purpose: 'bir kurumun belirli bir konudaki kurallarını, sorumluluklarını ve uygulama esaslarını tanımlıyor',
      evidenceTerms: ['politika', 'kural', 'sorumluluk', 'uygulama', 'kapsam']
    }
  },
  {
    patterns: [/\brehber\b/, /\bkilavuz\b/],
    profile: {
      label: 'Rehber veya Kılavuz',
      purpose: 'bir konuda izlenecek adımları, kuralları ve önerileri açıklıyor',
      evidenceTerms: ['rehber', 'kilavuz', 'adim', 'uygulama', 'oneri']
    }
  },
  {
    patterns: [/\brapor\b/, /degerlendirmesi/],
    profile: {
      label: 'Rapor',
      purpose: 'incelenen konuya ilişkin bulguları, değerlendirmeleri ve varsa sonuçları bir araya getiriyor',
      evidenceTerms: ['bulgu', 'degerlendirme', 'sonuc', 'rapor', 'risk']
    }
  }
];

const profileFor = (document: DocumentItem, chunks: StoredChunk[]): DocumentProfile | null => {
  const firstPages = [...chunks]
    .sort((left, right) => left.pageNumber - right.pageNumber || left.chunkIndex - right.chunkIndex)
    .slice(0, 4)
    .map((chunk) => chunk.content)
    .join(' ');
  const searchable = canonical(`${document.title} ${document.fileName} ${firstPages}`);
  return DOCUMENT_PROFILES.find(({ patterns }) => patterns.some((pattern) => pattern.test(searchable)))?.profile ?? null;
};

const generateRemoteAiAnswer: AiAnswerGenerator = async (request, signal) => {
  const response = await fetch('/api/answer', {
    method: 'POST',
    headers: { 'Content-Type': 'application/json' },
    body: JSON.stringify(request),
    signal
  });
  if (!response.ok) return null;

  const payload = await response.json() as {
    answer?: unknown;
    verification?: unknown;
  };
  if (typeof payload.answer !== 'string' || !payload.answer.trim()) return null;

  return {
    answer: payload.answer.trim(),
    verification: payload.verification as AnswerVerification | undefined
  };
};

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
  if (terms.length === 0) {
    const content = source.content.trim();
    return content.length <= 360 ? content : `${content.slice(0, 357).trimEnd()}…`;
  }

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

const localVerificationFor = (
  question: string,
  sources: ChatSource[],
  hasDirectMatch: boolean
): AnswerVerification => {
  if (!hasDirectMatch || sources.length === 0) {
    return {
      status: 'insufficient',
      score: 0,
      supportedClaims: 0,
      totalClaims: 0,
      summary: 'Soruyla doğrudan eşleşen birebir belge kanıtı bulunamadı.',
      claims: []
    };
  }

  const source = sources[0];
  const quote = sourceExcerpt(source, questionTerms(question))
    .replace(/…$/, '')
    .trim();
  return {
    status: 'verified',
    score: 100,
    supportedClaims: 1,
    totalClaims: 1,
    summary: 'Yerel cevap doğrudan PDF içindeki eşleşen ifadeden üretildi.',
    claims: [{
      text: 'Gösterilen bilgi seçili PDF içindeki kaynakla eşleşiyor.',
      sourceIndex: 1,
      sourceTitle: source.title,
      pageNumber: source.pageNumber,
      quote,
      verified: true
    }]
  };
};

const overviewAnswer = (
  intent: QuestionIntent,
  document: DocumentItem | undefined,
  profile: DocumentProfile | null,
  sources: ChatSource[]
) => {
  if (intent === 'greeting') {
    return 'Merhaba! Seçtiğin belgeyi açıklayabilir, özetleyebilir veya içindeki belirli bir bilgiyi bulabilirim. Ne öğrenmek istersin?';
  }
  if (intent === 'thanks') {
    return 'Rica ederim. Belgeyle ilgili başka bir sorunu da yanıtlayabilirim.';
  }
  if (!document || sources.length === 0) {
    return 'Yanıtlayabilmem için okunabilir bir belge yükleyip seçmelisin.';
  }

  const documentDescription = profile
    ? `Bu belge bir ${profile.label}.`
    : `Bu, “${document.title}” başlıklı bir PDF belgesi.`;
  const purpose = profile
    ? ` ${profile.purpose.charAt(0).toLocaleUpperCase('tr-TR')}${profile.purpose.slice(1)}.`
    : '';

  if (intent === 'document-identity') return `${documentDescription}${purpose}`;
  if (intent === 'document-purpose') {
    return profile
      ? `Bu ${profile.label}, ${profile.purpose}.`
      : `${documentDescription} Okunabilen bölümlerde belgenin amacı açıkça belirtilmiyor; aşağıdaki kaynaklardan içeriğini inceleyebilirsin.`;
  }

  const excerpts = sources
    .slice(0, 2)
    .map((source) => sourceExcerpt(source, []))
    .filter((excerpt, index, all) => all.findIndex((item) => canonical(item) === canonical(excerpt)) === index)
    .map((excerpt) => `• ${excerpt}`);
  return `Kısaca: ${documentDescription}${purpose}` +
    (excerpts.length ? `\n\nÖne çıkan bilgiler:\n${excerpts.join('\n')}` : '');
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
  const aiAnswerGenerator = options.generateAiAnswer === undefined
    ? (typeof globalThis.location === 'undefined' ? null : generateRemoteAiAnswer)
    : options.generateAiAnswer;

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

  const overviewFor = (documentIds?: number[]) => {
    const requestedIds = documentIds?.length ? documentIds : documents.map((document) => document.id);
    const document = requestedIds
      .map((id) => documents.find((item) => item.id === id))
      .find((item) => item?.indexingStatus === 'Ready');
    if (!document) return { document: undefined, profile: null, sources: [] as ChatSource[] };

    const documentChunks = storedChunks.filter((chunk) => chunk.documentId === document.id);
    const profile = profileFor(document, documentChunks);
    const ranked = documentChunks
      .map((chunk) => {
        const content = canonical(chunk.content);
        const evidenceScore = profile?.evidenceTerms
          .filter((term) => content.includes(canonical(term))).length ?? 0;
        const pageBonus = chunk.pageNumber === 1 ? 2 : 1 / Math.max(chunk.pageNumber, 1);
        return { chunk, score: evidenceScore + pageBonus };
      })
      .sort((left, right) => right.score - left.score || left.chunk.pageNumber - right.chunk.pageNumber || left.chunk.chunkIndex - right.chunk.chunkIndex)
      .slice(0, 3);
    const highestScore = ranked[0]?.score || 1;
    const sources: ChatSource[] = ranked.map(({ chunk, score }) => ({
      documentId: chunk.documentId,
      title: chunk.title,
      chunkIndex: chunk.chunkIndex,
      pageNumber: chunk.pageNumber,
      score: Math.min(0.99, 0.7 + (score / highestScore) * 0.29),
      content: chunk.content
    }));

    return { document, profile, sources };
  };

  const aiContextFor = (question: string, documentIds?: number[]) => {
    const selectedIds = documentIds?.length ? new Set(documentIds) : null;
    const readyIds = new Set(documents
      .filter((document) => document.indexingStatus === 'Ready')
      .map((document) => document.id));
    const terms = questionTerms(question);
    const candidates = storedChunks
      .filter((chunk) => readyIds.has(chunk.documentId))
      .filter((chunk) => !selectedIds || selectedIds.has(chunk.documentId));
    const ranked = candidates
      .map((chunk) => ({ chunk, rawScore: scoreChunk(chunk, terms) }))
      .sort((left, right) => right.rawScore - left.rawScore || left.chunk.pageNumber - right.chunk.pageNumber || left.chunk.chunkIndex - right.chunk.chunkIndex);
    const firstChunks = [...candidates]
      .sort((left, right) => left.pageNumber - right.pageNumber || left.chunkIndex - right.chunkIndex)
      .filter((chunk, index, all) => all.findIndex((item) => item.documentId === chunk.documentId) === index);
    const ordered = [
      ...ranked.filter((item) => item.rawScore > 0).map((item) => item.chunk),
      ...firstChunks,
      ...ranked.map((item) => item.chunk)
    ];

    let characterCount = 0;
    return ordered
      .filter((chunk, index, all) => all.findIndex((item) =>
        item.documentId === chunk.documentId && item.chunkIndex === chunk.chunkIndex) === index)
      .flatMap((chunk) => {
        if (characterCount >= 24_000) return [];
        const content = chunk.content.slice(0, 24_000 - characterCount);
        characterCount += content.length;
        const rawScore = scoreChunk(chunk, terms);
        return [{
          documentId: chunk.documentId,
          title: chunk.title,
          chunkIndex: chunk.chunkIndex,
          pageNumber: chunk.pageNumber,
          score: rawScore > 0 ? Math.min(0.99, 0.7 + rawScore * 0.1) : 0.5,
          content
        } satisfies ChatSource];
      })
      .slice(0, 16);
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
      const intent = questionIntent(body.question);
      const isOverviewIntent = intent !== 'retrieval';
      const needsDocument = intent === 'document-identity' || intent === 'document-purpose' || intent === 'summary';
      const overview = needsDocument ? overviewFor(body.documentIds) : null;
      const retrieval = isOverviewIntent ? null : sourcesFor(body.question, body.documentIds);
      const sources = overview?.sources ?? retrieval?.sources ?? [];
      const localAnswer = isOverviewIntent
        ? overviewAnswer(intent, overview?.document, overview?.profile ?? null, sources)
        : answerFor(body.question, sources, retrieval?.hasDirectMatch ?? false);
      const aiSources = aiContextFor(body.question, body.documentIds);
      const previousConversation = body.conversationId
        ? conversations.get(body.conversationId)
        : undefined;
      let answer = localAnswer;
      let responseSources = sources;
      let mode: 'ai' | 'local' = 'local';
      let verification = intent === 'retrieval'
        ? localVerificationFor(body.question, sources, retrieval?.hasDirectMatch ?? false)
        : undefined;

      if (aiAnswerGenerator && aiSources.length > 0 && intent === 'retrieval') {
        try {
          const generatedResult = await aiAnswerGenerator({
            question: body.question,
            sources: aiSources,
            history: previousConversation?.messages.slice(-4).map((message) => ({
              question: message.question,
              answer: message.answer
            })) ?? []
          }, callbacks.signal);
          const generatedAnswer = typeof generatedResult === 'string'
            ? generatedResult
            : generatedResult?.answer;
          if (generatedAnswer) {
            answer = generatedAnswer;
            responseSources = aiSources.slice(0, 6);
            mode = 'ai';
            verification = typeof generatedResult === 'string'
              ? undefined
              : generatedResult?.verification;
          }
        } catch (error) {
          if (callbacks.signal?.aborted) throw error;
        }
      }
      const chunks = answer.match(/.{1,34}(?:\s|$)/g) ?? [answer];

      callbacks.onStart?.({ conversationId, sources: responseSources });
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

      return { conversationId, answer, sources: responseSources, mode, verification };
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
