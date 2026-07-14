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
});
