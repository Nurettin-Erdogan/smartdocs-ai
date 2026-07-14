import { SESSION_TOKEN_KEY } from './session';

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

export const api = {
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
  listDocuments: () => apiFetch<DocumentItem[]>('/documents'),
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
  askChat: (body: ChatRequest) =>
    apiFetch<ChatResponse>('/chat', {
      method: 'POST',
      body: JSON.stringify(body)
    }),
  chatHistory: () => apiFetch<ChatHistorySummary[]>('/chat/history'),
  getConversation: (conversationId: number) =>
    apiFetch<ChatConversation>(`/chat/${conversationId}`)
};
