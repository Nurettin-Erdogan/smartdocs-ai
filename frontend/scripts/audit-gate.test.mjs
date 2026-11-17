import { describe, expect, it } from 'vitest';
import { evaluateAudit } from './audit-gate.mjs';

const advisory = (id, severity, name = 'picomatch') => ({
  source: 1,
  name,
  severity,
  title: `${name} açığı`,
  url: `https://github.com/advisories/${id}`,
});

const report = {
  vulnerabilities: {
    picomatch: { via: [advisory('GHSA-aaaa', 'high')] },
    vite: { via: ['picomatch', 'tinyglobby'] },
    lodash: { via: [advisory('GHSA-bbbb', 'moderate', 'lodash')] },
    debug: { via: [advisory('GHSA-cccc', 'low', 'debug')] },
  },
};

describe('evaluateAudit', () => {
  it('blocks every moderate-or-higher advisory without an exception', () => {
    const result = evaluateAudit(report, [], '2026-11-17');

    expect(result.blocking.map((item) => item.id).sort()).toEqual(['GHSA-aaaa', 'GHSA-bbbb']);
  });

  it('accepts an excepted advisory only until its review date', () => {
    const exceptions = [{ id: 'GHSA-aaaa', reviewBy: '2026-12-17', reason: 'yama yok' }];

    const beforeReview = evaluateAudit(report, exceptions, '2026-12-17');
    const afterReview = evaluateAudit(report, exceptions, '2026-12-18');

    expect(beforeReview.accepted.map((item) => item.id)).toEqual(['GHSA-aaaa']);
    expect(beforeReview.blocking.map((item) => item.id)).toEqual(['GHSA-bbbb']);
    expect(afterReview.expired.map((item) => item.id)).toEqual(['GHSA-aaaa']);
  });

  it('does not count packages that are vulnerable only through another package', () => {
    const result = evaluateAudit(
      { vulnerabilities: { vite: { via: ['picomatch'] } } },
      [],
      '2026-11-17',
    );

    expect(result.blocking).toEqual([]);
  });

  it('reports exceptions that are no longer needed', () => {
    const result = evaluateAudit({ vulnerabilities: {} }, [{ id: 'GHSA-old', reviewBy: '2027-01-01' }], '2026-11-17');

    expect(result.unused.map((item) => item.id)).toEqual(['GHSA-old']);
  });
});
