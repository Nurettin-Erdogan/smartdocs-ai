import { describe, expect, it } from 'vitest';
import {
  clearSession,
  loadSession,
  saveSession,
  SESSION_TOKEN_KEY,
  SESSION_USER_KEY
} from './session';

class MemoryStorage {
  private readonly values = new Map<string, string>();

  getItem(key: string) {
    return this.values.get(key) ?? null;
  }

  setItem(key: string, value: string) {
    this.values.set(key, value);
  }

  removeItem(key: string) {
    this.values.delete(key);
  }
}

describe('session storage', () => {
  it('round-trips a valid session', () => {
    const storage = new MemoryStorage();
    const session = {
      token: 'token',
      user: { fullName: 'Nurettin Erdoğan', email: 'nurettin@example.com', role: 'Personel' }
    };

    saveSession(session, storage);

    expect(loadSession(storage)).toEqual(session);
  });

  it('clears malformed JSON instead of crashing the app', () => {
    const storage = new MemoryStorage();
    storage.setItem(SESSION_TOKEN_KEY, 'token');
    storage.setItem(SESSION_USER_KEY, '{broken');

    expect(loadSession(storage)).toBeNull();
    expect(storage.getItem(SESSION_TOKEN_KEY)).toBeNull();
    expect(storage.getItem(SESSION_USER_KEY)).toBeNull();
  });

  it('rejects incomplete user data', () => {
    const storage = new MemoryStorage();
    storage.setItem(SESSION_TOKEN_KEY, 'token');
    storage.setItem(SESSION_USER_KEY, JSON.stringify({ fullName: '', email: 'x@example.com' }));

    expect(loadSession(storage)).toBeNull();
  });

  it('removes both stored values on clear', () => {
    const storage = new MemoryStorage();
    storage.setItem(SESSION_TOKEN_KEY, 'token');
    storage.setItem(SESSION_USER_KEY, '{}');

    clearSession(storage);

    expect(storage.getItem(SESSION_TOKEN_KEY)).toBeNull();
    expect(storage.getItem(SESSION_USER_KEY)).toBeNull();
  });
});
