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
};

export type ChatSource = {
  documentId: number;
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

export type ChatResponse = {
  conversationId: number;
  answer: string;
  sources: ChatSource[];
};

export type ChatRequest = {
  question: string;
  conversationId?: number | null;
};

const getToken = () => localStorage.getItem('smartdocs_token');

async function apiFetch<T>(path: string, init: RequestInit = {}): Promise<T> {
  const headers = new Headers(init.headers);
  const token = getToken();

  if (!(init.body instanceof FormData) && !headers.has('Content-Type')) {
    headers.set('Content-Type', 'application/json');
  }

  if (token) {
    headers.set('Authorization', `Bearer ${token}`);
  }

  const response = await fetch(`${API_BASE_URL}${path}`, {
    ...init,
    headers
  });

  const contentType = response.headers.get('content-type') ?? '';
  const payload = contentType.includes('application/json')
    ? await response.json()
    : await response.text();

  if (!response.ok) {
    const message = typeof payload === 'string'
      ? payload
      : payload?.message ?? payload?.Message ?? 'İşlem başarısız oldu.';
    throw new Error(message);
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
  uploadDocument: async (file: File) => {
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
  askChat: (body: ChatRequest) =>
    apiFetch<ChatResponse>('/chat', {
      method: 'POST',
      body: JSON.stringify(body)
    }),
  chatHistory: () => apiFetch<ChatConversation[]>('/chat/history')
};
