export const SESSION_TOKEN_KEY = 'smartdocs_token';
export const SESSION_USER_KEY = 'smartdocs_user';

export type SessionUser = {
  fullName: string;
  email: string;
  role?: string;
};

export type AppSession = {
  token: string;
  user: SessionUser;
};

type SessionStorage = Pick<Storage, 'getItem' | 'setItem' | 'removeItem'>;

const isSessionUser = (value: unknown): value is SessionUser => {
  if (!value || typeof value !== 'object') return false;

  const candidate = value as Partial<SessionUser>;
  return typeof candidate.fullName === 'string'
    && candidate.fullName.trim().length > 0
    && typeof candidate.email === 'string'
    && candidate.email.trim().length > 0
    && (candidate.role === undefined || typeof candidate.role === 'string');
};

export const clearSession = (storage: SessionStorage = window.localStorage) => {
  storage.removeItem(SESSION_TOKEN_KEY);
  storage.removeItem(SESSION_USER_KEY);
};

export const loadSession = (storage: SessionStorage = window.localStorage): AppSession | null => {
  const token = storage.getItem(SESSION_TOKEN_KEY);
  const storedUser = storage.getItem(SESSION_USER_KEY);

  if (!token || !storedUser) {
    clearSession(storage);
    return null;
  }

  try {
    const user: unknown = JSON.parse(storedUser);
    if (!isSessionUser(user)) {
      clearSession(storage);
      return null;
    }

    return { token, user };
  } catch {
    clearSession(storage);
    return null;
  }
};

export const saveSession = (session: AppSession, storage: SessionStorage = window.localStorage) => {
  storage.setItem(SESSION_TOKEN_KEY, session.token);
  storage.setItem(SESSION_USER_KEY, JSON.stringify(session.user));
};
