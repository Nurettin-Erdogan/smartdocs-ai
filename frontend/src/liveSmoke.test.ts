import { afterEach, describe, expect, it, vi } from 'vitest';
import answerHandler from '../../api/answer';
import { PROBE_CODE, SmokeFailure, assessAnswer, runChecks, type FetchLike } from '../../scripts/live-smoke';

const PAGE = '<!doctype html><title>SmartDocs AI · Kaynaklı belge asistanı</title>';
const CSP = "default-src 'self'; frame-ancestors 'none'; connect-src 'self'";
const PROBE_QUOTE = `Kontrol kodu ${PROBE_CODE} olarak belirlenmiştir`;

let clientNumber = 0;

// Canlı kontrolün isteklerini gerçek /api/answer işleyicisine yönlendirir; yalnızca
// Gemini çağrısı taklit edilir. Her test ayrı bir istemci adresi kullanır, çünkü
// işleyicinin rate limit sayacı modül düzeyindedir.
const appFetch = (csp = CSP): FetchLike => {
  clientNumber += 1;
  const clientAddress = `198.51.100.${clientNumber}`;

  return async (url, init) => {
    if (!url.endsWith('/api/answer')) {
      return new Response(PAGE, {
        status: 200,
        headers: { 'Content-Type': 'text/html', 'Content-Security-Policy': csp }
      });
    }

    const result = {
      statusCode: 200,
      headers: {} as Record<string, string>,
      body: undefined as unknown,
      setHeader(name: string, value: string) {
        this.headers[name] = value;
      },
      status(code: number) {
        this.statusCode = code;
        return this;
      },
      json(body: unknown) {
        this.body = body;
      }
    };
    await answerHandler({
      method: init?.method,
      headers: { 'x-forwarded-for': clientAddress },
      body: JSON.parse(String(init?.body))
    }, result);

    return new Response(JSON.stringify(result.body), {
      status: result.statusCode,
      headers: { 'Content-Type': 'application/json' }
    });
  };
};

const geminiAnswers = (modelAnswer: unknown) =>
  vi.spyOn(globalThis, 'fetch').mockImplementation(async () => new Response(JSON.stringify({
    candidates: [{ content: { parts: [{ text: JSON.stringify(modelAnswer) }] } }]
  }), { status: 200, headers: { 'Content-Type': 'application/json' } }));

describe('canlı demo kontrolü', () => {
  afterEach(() => {
    vi.restoreAllMocks();
    vi.unstubAllEnvs();
  });

  it('kaynaktan alıntıyla doğrulanan cevabı kabul eder', async () => {
    vi.stubEnv('GEMINI_API_KEY', 'test-secret-key');
    geminiAnswers({
      answer: `Kontrol kodu ${PROBE_CODE}.`,
      claims: [{ claim: `Kontrol kodu ${PROBE_CODE}.`, sourceId: 1, quote: PROBE_QUOTE }]
    });

    await expect(runChecks('https://example.test', appFetch())).resolves.toEqual([
      'ana sayfa ve güvenlik başlıkları',
      'kaynaklı yapay zekâ cevabı (1 iddia alıntıyla doğrulandı)'
    ]);
  });

  it('Gemini hatasını bir kez yeniden dener ve loglara yönlendiren bir hatayla bildirir', async () => {
    vi.stubEnv('GEMINI_API_KEY', 'test-secret-key');
    vi.spyOn(console, 'error').mockImplementation(() => undefined);
    const gemini = vi.spyOn(globalThis, 'fetch').mockImplementation(async () => new Response(
      JSON.stringify({ error: { status: 'RESOURCE_EXHAUSTED', message: 'Quota exceeded' } }),
      { status: 429, headers: { 'Content-Type': 'application/json' } }
    ));

    await expect(runChecks('https://example.test', appFetch(), { retryDelayMs: 0 }))
      .rejects.toThrow(/502 döndürdü.*Gemini request failed/s);
    expect(gemini).toHaveBeenCalledTimes(2);
  });

  it('kaynaktaki bilgiyi içermeyen cevabı reddeder', async () => {
    vi.stubEnv('GEMINI_API_KEY', 'test-secret-key');
    geminiAnswers({ answer: 'Belgede kontrol kodu bulunmuyor.', claims: [] });

    await expect(runChecks('https://example.test', appFetch())).rejects.toThrow(/kontrol kodunu/);
  });

  it('alıntısı kaynakta olmayan cevabı kabul etmez (sunucu kanıtsız cevabı geri çeker)', async () => {
    vi.stubEnv('GEMINI_API_KEY', 'test-secret-key');
    geminiAnswers({
      answer: `Kontrol kodu ${PROBE_CODE}.`,
      claims: [{ claim: `Kontrol kodu ${PROBE_CODE}.`, sourceId: 1, quote: `Gizli kod ${PROBE_CODE}` }]
    });

    await expect(runChecks('https://example.test', appFetch())).rejects.toThrow(/kontrol kodunu/);
  });

  it('doğrulayıcı kanıtsız cevabı geri çekmeyi bırakırsa bunu da yakalar', () => {
    expect(() => assessAnswer(200, {
      answer: `Kontrol kodu ${PROBE_CODE}.`,
      verification: { status: 'insufficient', supportedClaims: 0 }
    })).toThrow(/doğrulanamadı/);
  });

  it('frame-ancestors korumasının kaybolmasını yakalar', async () => {
    const check = runChecks('https://example.test', appFetch("default-src 'self'"));

    await expect(check).rejects.toBeInstanceOf(SmokeFailure);
    await expect(check).rejects.toThrow(/frame-ancestors/);
  });
});
