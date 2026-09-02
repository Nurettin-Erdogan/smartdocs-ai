import { describe, expect, it } from 'vitest';
import { reviewModesFor } from './reviewModes';

describe('kanıtlı inceleme modları', () => {
  it('tek belge için özet, kritik bilgi ve risk taraması sunar', () => {
    expect(reviewModesFor(1).map((mode) => mode.id)).toEqual([
      'summary',
      'facts',
      'risks'
    ]);
  });

  it('birden fazla belge seçilince tutarsızlık karşılaştırmasını açar', () => {
    const compareMode = reviewModesFor(2).find((mode) => mode.id === 'compare');

    expect(compareMode?.prompt).toContain('çelişen');
    expect(compareMode?.prompt).toContain('sayfa kanıtıyla');
  });

  it('her inceleme istemi kanıt veya kaynak zorunluluğu taşır', () => {
    const prompts = reviewModesFor(2).map((mode) => mode.prompt.toLocaleLowerCase('tr-TR'));

    expect(prompts.every((prompt) => prompt.includes('kanıt') || prompt.includes('kaynak'))).toBe(true);
  });
});
