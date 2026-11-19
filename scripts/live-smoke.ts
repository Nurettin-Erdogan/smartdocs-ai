// SmartDocs AI canlı demo duman testinin kontrol mantığı.
//
// Ağ katmanı (fetch) dışarıdan verilir. Böylece aynı kontroller testlerde gerçek
// /api/answer işleyicisine karşı da çalışır ve uygulama değiştiğinde canlı kontrol
// sessizce yanlış şeyi doğrulamaya başlayamaz. Komut satırı girişi:
// scripts/live-smoke-cli.mts

export const DEFAULT_BASE_URL = 'https://smartdocs-ai-henna.vercel.app';
export const PROBE_CODE = 'MAVI-42';

// Cevabı yalnızca bu kaynakta bulunabilecek bir soru: kod cevapta geçiyorsa model
// kaynağı kullanmış, iddia alıntıyla doğrulanmışsa doğrulayıcı da çalışıyordur.
export const ANSWER_PROBE = {
  question: 'Kontrol kodu nedir?',
  sources: [{
    title: 'Canlı kontrol belgesi',
    pageNumber: 1,
    content: `Bu belge SmartDocs AI canlı demo kontrolü için hazırlanmıştır. Kontrol kodu ${PROBE_CODE} olarak belirlenmiştir. Belge başka bilgi içermez.`
  }]
};

export type FetchLike = (url: string, init?: RequestInit) => Promise<Response>;

type AnswerPayload = {
  answer?: unknown;
  message?: unknown;
  verification?: {
    status?: unknown;
    supportedClaims?: unknown;
  };
};

export class SmokeFailure extends Error {}

const expect = (condition: boolean, message: string): void => {
  if (!condition) throw new SmokeFailure(message);
};

export const assessPage = (status: number, headers: Headers, html: string): void => {
  expect(status === 200, `Ana sayfa ${status} döndürdü.`);
  expect(
    html.includes('<title>SmartDocs AI'),
    'Ana sayfa başlığı bulunamadı; yanlış ya da bozuk bir sürüm yayında olabilir.'
  );
  expect(
    (headers.get('content-security-policy') ?? '').includes("frame-ancestors 'none'"),
    "Content-Security-Policy başlığında frame-ancestors 'none' yok."
  );
};

/** Cevabı değerlendirir; alıntıyla doğrulanan iddia sayısını döndürür. */
export const assessAnswer = (status: number, payload: AnswerPayload | null): number => {
  const detail = typeof payload?.message === 'string' ? ` (${payload.message.replace(/\.$/, '')})` : '';
  expect(
    status === 200,
    `/api/answer ${status} döndürdü${detail}. Gemini anahtarı, model adı ya da kota sorunu olabilir; ` +
      'Vercel loglarındaki "Gemini request failed" satırı nedeni gösterir.'
  );

  const answer = typeof payload?.answer === 'string' ? payload.answer : '';
  expect(
    answer.toUpperCase().includes(PROBE_CODE),
    `Cevap kaynaktaki kontrol kodunu (${PROBE_CODE}) içermiyor: ${JSON.stringify(answer)}`
  );

  const supported = Number(payload?.verification?.supportedClaims ?? 0);
  expect(
    supported >= 1,
    `Cevaptaki hiçbir iddia kaynak alıntısıyla doğrulanamadı (doğrulama: ${String(payload?.verification?.status)}).`
  );
  return supported;
};

const isTemporaryFailure = (status: number) => status === 429 || status >= 500;

export const runChecks = async (
  baseUrl: string,
  fetchImpl: FetchLike,
  options: { timeoutMs?: number; retryDelayMs?: number } = {}
): Promise<string[]> => {
  const timeoutMs = options.timeoutMs ?? 60_000;
  const retryDelayMs = options.retryDelayMs ?? 5_000;
  const passed: string[] = [];

  const page = await fetchImpl(`${baseUrl}/`, { signal: AbortSignal.timeout(timeoutMs) });
  assessPage(page.status, page.headers, await page.text());
  passed.push('ana sayfa ve güvenlik başlıkları');

  // Uygulamanın kendisi gibi geçici hatada bir kez yeniden dener; tek seferlik
  // bir hıçkırık sabah e-postası üretmesin.
  let response = await postProbe(baseUrl, fetchImpl, timeoutMs);
  if (isTemporaryFailure(response.status)) {
    await new Promise((resolve) => setTimeout(resolve, retryDelayMs));
    response = await postProbe(baseUrl, fetchImpl, timeoutMs);
  }

  const payload = await response.json().catch(() => null) as AnswerPayload | null;
  const supported = assessAnswer(response.status, payload);
  passed.push(`kaynaklı yapay zekâ cevabı (${supported} iddia alıntıyla doğrulandı)`);

  return passed;
};

const postProbe = (baseUrl: string, fetchImpl: FetchLike, timeoutMs: number) =>
  fetchImpl(`${baseUrl}/api/answer`, {
    method: 'POST',
    headers: { 'Content-Type': 'application/json' },
    body: JSON.stringify(ANSWER_PROBE),
    signal: AbortSignal.timeout(timeoutMs)
  });
