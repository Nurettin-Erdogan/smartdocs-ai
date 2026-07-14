import { describe, expect, it } from 'vitest';
import { extractApiErrorMessage } from './api';

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
});
