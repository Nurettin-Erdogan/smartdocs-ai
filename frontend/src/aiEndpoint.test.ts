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
    vi.stubEnv('OPENAI_API_KEY', 'test-secret-key');
    vi.stubEnv('OPENAI_MODEL', 'gpt-5.6-luna');
    const fetchMock = vi.spyOn(globalThis, 'fetch').mockResolvedValue(new Response(JSON.stringify({
      output: [{
        type: 'message',
        content: [{ type: 'output_text', text: 'Öğrencinin adı Nurettin Erdoğan. (Belge, s. 1)' }]
      }]
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
    expect(url).toBe('https://api.openai.com/v1/responses');
    expect((init?.headers as Record<string, string>).Authorization).toBe('Bearer test-secret-key');
    const upstreamBody = JSON.parse(String(init?.body)) as Record<string, unknown>;
    expect(upstreamBody).toMatchObject({
      model: 'gpt-5.6-luna',
      store: false,
      max_output_tokens: 600
    });
    expect(String(upstreamBody.input)).toContain('Nurettin Erdoğan');
  });

  it('reports an unconfigured server without calling OpenAI', async () => {
    vi.stubEnv('OPENAI_API_KEY', '');
    const fetchMock = vi.spyOn(globalThis, 'fetch');
    const response = responseForTest();

    await answerHandler({ method: 'POST', headers: {}, body: {} }, response);

    expect(response.statusCode).toBe(503);
    expect(response.body).toEqual({ message: 'Yapay zekâ servisi henüz yapılandırılmadı.' });
    expect(fetchMock).not.toHaveBeenCalled();
  });
});
