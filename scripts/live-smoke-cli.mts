// SmartDocs AI canlı demo duman testi. Bağımlılık gerektirmez; Node 24 bu
// TypeScript dosyasını derlemeden çalıştırır:
//
//   node scripts/live-smoke-cli.mts
//   LIVE_BASE_URL=http://localhost:3000 node scripts/live-smoke-cli.mts

import { DEFAULT_BASE_URL, runChecks } from './live-smoke.ts';

const baseUrl = (process.env.LIVE_BASE_URL ?? DEFAULT_BASE_URL).replace(/\/+$/, '');
console.log(`Kontrol ediliyor: ${baseUrl}`);

try {
  const passed = await runChecks(baseUrl, fetch);
  for (const name of passed) console.log(`✓ ${name}`);
  console.log('Canlı demo kontrolü geçti.');
} catch (error) {
  console.error(`Canlı demo kontrolü başarısız: ${error instanceof Error ? error.message : String(error)}`);
  process.exitCode = 1;
}
