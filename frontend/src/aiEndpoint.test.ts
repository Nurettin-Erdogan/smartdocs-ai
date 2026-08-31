import { afterEach, describe, expect, it, vi } from 'vitest';
import answerHandler from '../../api/answer';

type MockResponse = {
  statusCode: number;
  headers: Record<string, string>;
  body: unknown;
  setHeader(name: string, value: string): void;
  status(code: number): MockResponse;
  json(body: unknown): void;
};

const responseForTest = (): MockResponse => ({
  statusCode: 200,
  headers: {},
  body: undefined,
  setHeader(name, value) {
    this.headers[name] = value;
  },
  status(code) {
    this.statusCode = code;
    return this;
  },
  json(body) {
    this.body = body;
  }
});

describe('Vercel AI answer endpoint', () => {
  afterEach(() => {
    vi.restoreAllMocks();
    vi.unstubAllEnvs();
  });

  it('keeps the API key server-side and returns the grounded model answer', async () => {
    vi.stubEnv('GEMINI_API_KEY', 'test-secret-key');
    vi.stubEnv('GEMINI_MODEL', 'gemini-2.5-flash-lite');
    const fetchMock = vi.spyOn(globalThis, 'fetch').mockResolvedValue(new Response(JSON.stringify({
      candidates: [{ content: { parts: [{ text: 'Öğrencinin adı Nurettin Erdoğan. (Belge, s. 1)' }] } }]
    }), { status: 200, headers: { 'Content-Type': 'application/json' } }));
    const response = responseForTest();

    await answerHandler({
      method: 'POST',
      headers: { 'x-forwarded-for': '198.51.100.10' },
      body: {
        question: 'Öğrenci ismi ne?',
        sources: [{ title: 'Belge', pageNumber: 1, content: 'Ad Soyad: Nurettin Erdoğan' }]
      }
    }, response);

    expect(response.statusCode).toBe(200);
    expect(response.body).toEqual({ answer: expect.stringContaining('Nurettin Erdoğan') });
    const [url, init] = fetchMock.mock.calls[0] ?? [];
    expect(url).toBe('https://generativelanguage.googleapis.com/v1beta/models/gemini-2.5-flash-lite:generateContent');
    expect((init?.headers as Record<string, string>)['x-goog-api-key']).toBe('test-secret-key');
    const upstreamBody = JSON.parse(String(init?.body)) as Record<string, unknown>;
    expect(upstreamBody).toMatchObject({
      generationConfig: {
        maxOutputTokens: 600,
        thinkingConfig: { thinkingBudget: 0 }
      }
    });
    expect(JSON.stringify(upstreamBody.contents)).toContain('Nurettin Erdoğan');
  });

  it('reports an unconfigured server without calling Gemini', async () => {
    vi.stubEnv('GEMINI_API_KEY', '');
    const fetchMock = vi.spyOn(globalThis, 'fetch');
    const response = responseForTest();

    await answerHandler({ method: 'POST', headers: {}, body: {} }, response);

    expect(response.statusCode).toBe(503);
    expect(response.body).toEqual({ message: 'Yapay zekâ servisi henüz yapılandırılmadı.' });
    expect(fetchMock).not.toHaveBeenCalled();
  });
});
