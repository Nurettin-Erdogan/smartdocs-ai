import { SESSION_TOKEN_KEY } from './session';
import { createDemoApi, isDemoMode } from './demo';

export { isDemoMode } from './demo';

export const API_BASE_URL = import.meta.env.VITE_API_BASE_URL ?? '/api';

export type AuthResponse = {
  id: number;
  fullName: string;
  email: string;
  role?: string;
  token: string;
};

export type DocumentItem = {
  id: number;
  title: string;
  fileName: string;
  fileType: string;
  fileSize: number;
  uploadDate: string;
  indexingStatus: string;
  indexingError?: string | null;
};

export type ChatSource = {
  documentId: number;
  title: string;
  chunkIndex: number;
  pageNumber: number;
  score: number;
  content: string;
};

export type ChatMessage = {
  id: number;
  question: string;
  answer: string;
  createdAt: string;
};

export type ChatConversation = {
  conversationId: number;
  createdAt: string;
  messages: ChatMessage[];
};

export type ChatHistorySummary = {
  conversationId: number;
  createdAt: string;
  firstQuestion: string;
  messageCount: number;
};

export type ChatResponse = {
  conversationId: number;
  answer: string;
  sources: ChatSource[];
};

export type ChatRequest = {
  question: string;
  conversationId?: number | null;
  documentIds?: number[];
};

export type ChatStreamCallbacks = {
  onStart?: (data: { conversationId: number; sources: ChatSource[] }) => void;
  onChunk?: (content: string) => void;
  signal?: AbortSignal;
};

type UnauthorizedHandler = (message: string) => void;

let unauthorizedHandler: UnauthorizedHandler | null = null;

export class ApiError extends Error {
  readonly status: number;
  readonly payload: unknown;

  constructor(message: string, status: number, payload: unknown) {
    super(message);
    this.name = 'ApiError';
    this.status = status;
    this.payload = payload;
  }
}

const stringValue = (value: unknown) =>
  typeof value === 'string' && value.trim().length > 0 ? value.trim() : null;

export const extractApiErrorMessage = (
  payload: unknown,
  fallback = 'İşlem başarısız oldu.'
) => {
  const plainText = stringValue(payload);
  if (plainText) return plainText;

  if (!payload || typeof payload !== 'object') return fallback;

  const problem = payload as Record<string, unknown>;
  const directMessage = stringValue(problem.message) ?? stringValue(problem.Message);
  if (directMessage) return directMessage;

  if (problem.errors && typeof problem.errors === 'object') {
    const validationMessages = Object.values(problem.errors as Record<string, unknown>)
      .flatMap((value) => Array.isArray(value) ? value : [value])
      .map(stringValue)
      .filter((value): value is string => Boolean(value));

    if (validationMessages.length > 0) {
      return [...new Set(validationMessages)].join(' ');
    }
  }

  return stringValue(problem.detail)
    ?? stringValue(problem.title)
    ?? fallback;
};

export const setUnauthorizedHandler = (handler: UnauthorizedHandler | null) => {
  unauthorizedHandler = handler;

  return () => {
    if (unauthorizedHandler === handler) {
      unauthorizedHandler = null;
    }
  };
};

const getToken = () => globalThis.localStorage?.getItem(SESSION_TOKEN_KEY) ?? null;

const parseResponsePayload = async (response: Response): Promise<unknown> => {
  const text = await response.text();
  if (!text) return undefined;

  const contentType = response.headers.get('content-type') ?? '';
  if (!contentType.includes('application/json')) return text;

  try {
    return JSON.parse(text) as unknown;
  } catch {
    return text;
  }
};

const normalizedApiBaseUrl = API_BASE_URL.endsWith('/')
  ? API_BASE_URL.slice(0, -1)
  : API_BASE_URL;

export async function apiFetch<T>(path: string, init: RequestInit = {}): Promise<T> {
  const headers = new Headers(init.headers);
  const token = getToken();

  if (init.body !== undefined && !(init.body instanceof FormData) && !headers.has('Content-Type')) {
    headers.set('Content-Type', 'application/json');
  }

  if (token) {
    headers.set('Authorization', `Bearer ${token}`);
  }

  const response = await fetch(`${normalizedApiBaseUrl}${path}`, {
    ...init,
    headers
  });
  const payload = await parseResponsePayload(response);

  if (!response.ok) {
    const fallback = response.status === 401
      ? 'Oturumunuzun süresi doldu. Lütfen yeniden giriş yapın.'
      : 'İşlem başarısız oldu.';
    const message = extractApiErrorMessage(payload, fallback);

    if (response.status === 401 && token) {
      unauthorizedHandler?.(message);
    }

    throw new ApiError(message, response.status, payload);
  }

  return payload as T;
}

async function apiFetchBlob(path: string, init: RequestInit = {}): Promise<Blob> {
  const headers = new Headers(init.headers);
  const token = getToken();
  if (token) headers.set('Authorization', `Bearer ${token}`);

  const response = await fetch(`${normalizedApiBaseUrl}${path}`, {
    ...init,
    headers
  });

  if (!response.ok) {
    const payload = await parseResponsePayload(response);
    const fallback = response.status === 401
      ? 'Oturumunuzun süresi doldu. Lütfen yeniden giriş yapın.'
      : 'PDF görüntülenemedi.';
    const message = extractApiErrorMessage(payload, fallback);
    if (response.status === 401 && token) unauthorizedHandler?.(message);
    throw new ApiError(message, response.status, payload);
  }

  return response.blob();
}

async function streamChat(
  body: ChatRequest,
  callbacks: ChatStreamCallbacks = {}
): Promise<ChatResponse> {
  const headers = new Headers({
    'Content-Type': 'application/json',
    Accept: 'application/x-ndjson'
  });
  const token = getToken();
  if (token) headers.set('Authorization', `Bearer ${token}`);

  const response = await fetch(`${normalizedApiBaseUrl}/chat`, {
    method: 'POST',
    headers,
    body: JSON.stringify(body),
    signal: callbacks.signal
  });

  if (!response.ok) {
    const payload = await parseResponsePayload(response);
    const fallback = response.status === 401
      ? 'Oturumunuzun süresi doldu. Lütfen yeniden giriş yapın.'
      : 'Soru gönderilemedi.';
    const message = extractApiErrorMessage(payload, fallback);
    if (response.status === 401 && token) unauthorizedHandler?.(message);
    throw new ApiError(message, response.status, payload);
  }

  if (!response.body) {
    throw new ApiError('Yapay zekâ yanıt akışı başlatılamadı.', 502, undefined);
  }

  const reader = response.body.getReader();
  const decoder = new TextDecoder();
  let buffer = '';
  let conversationId = 0;
  let sources: ChatSource[] = [];
  let answer = '';
  let completed = false;

  const processLine = (line: string) => {
    if (!line.trim()) return;
    const event = JSON.parse(line) as {
      type?: string;
      data?: Record<string, unknown>;
    };
    const data = event.data ?? {};

    if (event.type === 'start') {
      conversationId = Number(data.conversationId);
      sources = Array.isArray(data.sources) ? data.sources as ChatSource[] : [];
      callbacks.onStart?.({ conversationId, sources });
    } else if (event.type === 'chunk') {
      const content = typeof data.content === 'string' ? data.content : '';
      answer += content;
      if (content) callbacks.onChunk?.(content);
    } else if (event.type === 'error') {
      throw new ApiError(
        typeof data.message === 'string' ? data.message : 'Yapay zekâ cevabı tamamlayamadı.',
        502,
        data
      );
    } else if (event.type === 'done') {
      completed = true;
    }
  };

  try {
    while (true) {
      const { done, value } = await reader.read();
      buffer += decoder.decode(value, { stream: !done });
      const lines = buffer.split('\n');
      buffer = lines.pop() ?? '';
      lines.forEach(processLine);
      if (done) break;
    }
    if (buffer.trim()) processLine(buffer);
  } finally {
    reader.releaseLock();
  }

  if (!completed || conversationId <= 0) {
    throw new ApiError('Yapay zekâ yanıtı beklenmedik şekilde kesildi.', 502, undefined);
  }

  return { conversationId, answer: answer.trim(), sources };
}

const liveApi = {
  register: (body: { fullName: string; email: string; password: string }) =>
    apiFetch<AuthResponse>('/auth/register', {
      method: 'POST',
      body: JSON.stringify(body)
    }),
  login: (body: { email: string; password: string }) =>
    apiFetch<AuthResponse>('/auth/login', {
      method: 'POST',
      body: JSON.stringify(body)
    }),
  listDocuments: (signal?: AbortSignal) =>
    apiFetch<DocumentItem[]>('/documents', { signal }),
  getDocumentFile: (id: number, signal?: AbortSignal) =>
    apiFetchBlob(`/documents/${id}/file`, { signal }),
  uploadDocument: (file: File) => {
    const formData = new FormData();
    formData.append('file', file);
    return apiFetch<DocumentItem & { indexingStatus?: string }>('/documents/upload', {
      method: 'POST',
      body: formData
    });
  },
  deleteDocument: (id: number) =>
    apiFetch<{ message?: string; Message?: string }>(`/documents/${id}`, {
      method: 'DELETE'
    }),
  reindexDocument: (id: number) =>
    apiFetch<DocumentItem>(`/documents/${id}/reindex`, {
      method: 'POST'
    }),
  askChat: (body: ChatRequest, callbacks?: ChatStreamCallbacks) =>
    streamChat(body, callbacks),
  chatHistory: (signal?: AbortSignal) =>
    apiFetch<ChatHistorySummary[]>('/chat/history', { signal }),
  getConversation: (conversationId: number, signal?: AbortSignal) =>
    apiFetch<ChatConversation>(`/chat/${conversationId}`, { signal })
};

export const api = isDemoMode ? createDemoApi() : liveApi;
