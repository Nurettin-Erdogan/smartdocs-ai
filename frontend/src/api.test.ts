import { afterEach, describe, expect, it, vi } from 'vitest';
import { api, extractApiErrorMessage } from './api';

afterEach(() => {
  vi.unstubAllGlobals();
});

describe('extractApiErrorMessage', () => {
  it('reads the API message field', () => {
    expect(extractApiErrorMessage({ message: 'Belge bulunamadı.' }))
      .toBe('Belge bulunamadı.');
  });

  it('combines ASP.NET validation errors without duplicates', () => {
    expect(extractApiErrorMessage({
      title: 'Validation failed',
      errors: {
        Email: ['Geçersiz e-posta.', 'Geçersiz e-posta.'],
        Password: ['Şifre çok kısa.']
      }
    })).toBe('Geçersiz e-posta. Şifre çok kısa.');
  });

  it('falls back to ProblemDetails detail and title', () => {
    expect(extractApiErrorMessage({ detail: 'Servis kullanılamıyor.' }))
      .toBe('Servis kullanılamıyor.');
    expect(extractApiErrorMessage({ title: 'Hatalı istek' }))
      .toBe('Hatalı istek');
  });

  it('uses the supplied fallback for an empty response', () => {
    expect(extractApiErrorMessage(undefined, 'Bağlantı kurulamadı.'))
      .toBe('Bağlantı kurulamadı.');
  });

  it('forwards cancellation signals to document requests', async () => {
    const fetchMock = vi.fn().mockResolvedValue(new Response('[]', {
      status: 200,
      headers: { 'content-type': 'application/json' }
    }));
    vi.stubGlobal('fetch', fetchMock);
    const controller = new AbortController();

    await api.listDocuments(controller.signal);

    expect(fetchMock).toHaveBeenCalledOnce();
    expect(fetchMock.mock.calls[0]?.[1]?.signal).toBe(controller.signal);
  });

  it('downloads a PDF as a blob', async () => {
    const pdf = new Blob(['%PDF-1.4'], { type: 'application/pdf' });
    const fetchMock = vi.fn().mockResolvedValue(new Response(pdf, {
      status: 200,
      headers: { 'content-type': 'application/pdf' }
    }));
    vi.stubGlobal('fetch', fetchMock);

    const result = await api.getDocumentFile(17);

    expect(result?.type).toBe('application/pdf');
    expect(fetchMock.mock.calls[0]?.[0]).toContain('/documents/17/file');
  });

  it('streams chat chunks in order and returns the completed answer', async () => {
    const encoder = new TextEncoder();
    const stream = new ReadableStream({
      start(controller) {
        controller.enqueue(encoder.encode(
          '{"type":"start","data":{"conversationId":7,"sources":[]}}\n'));
        controller.enqueue(encoder.encode(
          '{"type":"chunk","data":{"content":"Merhaba"}}\n' +
          '{"type":"chunk","data":{"content":" dünya"}}\n'));
        controller.enqueue(encoder.encode(
          '{"type":"done","data":{"conversationId":7}}\n'));
        controller.close();
      }
    });
    const fetchMock = vi.fn().mockResolvedValue(new Response(stream, {
      status: 200,
      headers: { 'content-type': 'application/x-ndjson' }
    }));
    vi.stubGlobal('fetch', fetchMock);
    const chunks: string[] = [];
    const controller = new AbortController();

    const result = await api.askChat(
      { question: 'Selam', documentIds: [3, 9] },
      { onChunk: (content) => chunks.push(content), signal: controller.signal }
    );

    expect(chunks).toEqual(['Merhaba', ' dünya']);
    expect(result).toEqual({ conversationId: 7, answer: 'Merhaba dünya', sources: [] });
    const headers = fetchMock.mock.calls[0]?.[1]?.headers as Headers;
    expect(headers.get('Accept')).toBe('application/x-ndjson');
    expect(fetchMock.mock.calls[0]?.[1]?.signal).toBe(controller.signal);
    expect(JSON.parse(String(fetchMock.mock.calls[0]?.[1]?.body))).toEqual({
      question: 'Selam',
      documentIds: [3, 9]
    });
  });
});
