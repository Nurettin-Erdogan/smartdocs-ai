import { describe, expect, it } from 'vitest';
import manifest from '../package.json';

describe('runtime dependency versions', () => {
  it('keeps React and React DOM on the same exact version', () => {
    const reactVersion = manifest.dependencies.react;
    const reactDomVersion = manifest.dependencies['react-dom'];

    expect(reactVersion).toMatch(/^\d+\.\d+\.\d+$/);
    expect(reactDomVersion).toBe(reactVersion);
  });
});
