// npm audit kalite kapısı.
//
// moderate ve üstündeki her güvenlik bildirimi CI'ı durdurur. Tek istisna,
// audit-exceptions.json içinde gerekçesi ve son inceleme tarihi yazılmış
// bildirimlerdir; tarih geçince istisna kendiliğinden düşer ve CI yeniden
// kırmızıya döner. Böylece "yamalı sürüm yok" durumu bütün PR'ları süresiz
// kilitlemez, ama risk kabulü de unutulup kalıcı hâle gelmez.
import { execFileSync } from 'node:child_process';
import { readFileSync } from 'node:fs';
import { pathToFileURL } from 'node:url';

const SEVERITY_ORDER = ['info', 'low', 'moderate', 'high', 'critical'];
const MIN_BLOCKING = SEVERITY_ORDER.indexOf('moderate');

/**
 * npm audit --json çıktısını değerlendirir.
 * @returns {{ blocking: object[], accepted: object[], expired: object[], unused: object[] }}
 */
export function evaluateAudit(report, exceptions, today) {
  // vite, vitest gibi paketler yalnızca başka bir paketin açığı yüzünden
  // listelenir (via: ["picomatch"]); kök bildirimler nesne olarak gelir.
  const advisories = new Map();
  for (const vulnerability of Object.values(report.vulnerabilities ?? {})) {
    for (const source of vulnerability.via ?? []) {
      if (typeof source !== 'object' || source === null) continue;
      if (SEVERITY_ORDER.indexOf(source.severity) < MIN_BLOCKING) continue;
      const id = String(source.url ?? '').split('/').pop() || `npm-${source.source}`;
      advisories.set(id, { id, package: source.name, severity: source.severity, title: source.title });
    }
  }

  const byId = new Map(exceptions.map((exception) => [exception.id, exception]));
  const result = { blocking: [], accepted: [], expired: [], unused: [] };
  for (const advisory of advisories.values()) {
    const exception = byId.get(advisory.id);
    if (!exception) {
      result.blocking.push(advisory);
    } else if (exception.reviewBy < today) {
      result.expired.push({ ...advisory, reviewBy: exception.reviewBy });
    } else {
      result.accepted.push({ ...advisory, reviewBy: exception.reviewBy, reason: exception.reason });
    }
  }
  result.unused = exceptions.filter((exception) => !advisories.has(exception.id));
  return result;
}

function runNpmAudit() {
  try {
    return execFileSync('npm', ['audit', '--json'], {
      encoding: 'utf8',
      maxBuffer: 32 * 1024 * 1024,
      shell: process.platform === 'win32',
    });
  } catch (error) {
    // npm audit açık bulduğunda 1 ile çıkar; JSON yine stdout'tadır.
    if (error.stdout) return error.stdout;
    throw error;
  }
}

function main() {
  const exceptions = JSON.parse(readFileSync(new URL('../audit-exceptions.json', import.meta.url), 'utf8'));
  const report = JSON.parse(runNpmAudit());
  const today = new Date().toISOString().slice(0, 10);
  const { blocking, accepted, expired, unused } = evaluateAudit(report, exceptions, today);

  for (const item of accepted) {
    console.log(`KABUL EDİLEN RİSK  ${item.id} (${item.package}, ${item.severity}) — inceleme tarihi ${item.reviewBy}\n  ${item.reason}`);
  }
  for (const item of unused) {
    console.log(`::warning::${item.id} artık raporda yok; audit-exceptions.json içinden kaldırılabilir.`);
  }
  for (const item of expired) {
    console.log(`::error::${item.id} (${item.package}) için risk kabulünün süresi ${item.reviewBy} tarihinde doldu; yeniden değerlendirin.`);
  }
  for (const item of blocking) {
    console.log(`::error::${item.id} ${item.package} (${item.severity}): ${item.title}`);
  }

  if (blocking.length || expired.length) {
    process.exitCode = 1;
    return;
  }
  console.log('npm audit kapısı geçti.');
}

if (import.meta.url === pathToFileURL(process.argv[1] ?? '').href) {
  main();
}
